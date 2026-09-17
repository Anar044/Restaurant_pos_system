using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Orders;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders").RequireAuthorization();

        group.MapGet("/open", async (ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _)) return Results.Unauthorized();
            var orders = await db.Orders
                .AsNoTracking()
                .Include(x => x.Items)
                .Where(x => x.RestaurantId == restaurantId && x.Status != OrderStatus.Closed && x.Status != OrderStatus.Cancelled)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(100)
                .ToListAsync(ct);
            return Results.Ok(new { orders = orders.Select(ToDto) });
        }).RequireAuthorization("orders.read");

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _)) return Results.Unauthorized();
            var order = await db.Orders.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            return order is null ? Results.NotFound(new { message = "Order not found." }) : Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.read");

        group.MapPost("/", async (CreateOrderRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId)) return Results.Unauthorized();
            if (request.GuestCount is < 1 or > 100) return Results.BadRequest(new { message = "GuestCount must be between 1 and 100." });

            if (request.TableId is Guid tableId)
            {
                var tableExists = await db.DiningTables.AnyAsync(x => x.Id == tableId && x.RestaurantId == restaurantId && x.IsActive, ct);
                if (!tableExists) return Results.BadRequest(new { message = "Table does not exist in this restaurant." });
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var order = new Order
            {
                RestaurantId = restaurantId,
                TableId = request.TableId,
                CreatedByEmployeeId = employeeId,
                GuestCount = request.GuestCount,
                Status = OrderStatus.Open
            };
            db.Orders.Add(order);
            db.AuditEvents.Add(Audit(restaurantId, employeeId, "ORDER_CREATED", "Order", order.Id, new { request.TableId, request.GuestCount }));
            db.OutboxEvents.Add(Outbox(restaurantId, "ORDER_CREATED", "Order", order.Id, new { order.Id, request.TableId, request.GuestCount }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/orders/{order.Id}", ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapPost("/{id:guid}/items", async (Guid id, AddOrderItemRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId)) return Results.Unauthorized();
            if (request.Quantity <= 0 || request.Quantity > 1000) return Results.BadRequest(new { message = "Quantity must be greater than zero." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var order = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (order is null) return Results.NotFound(new { message = "Order not found." });
            if (order.Status is OrderStatus.Closed or OrderStatus.Cancelled or OrderStatus.Paid)
                return Results.Conflict(new { message = $"Order cannot be edited in status {order.Status}." });

            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ProductId && x.RestaurantId == restaurantId && x.IsActive, ct);
            if (product is null) return Results.BadRequest(new { message = "Product is not available." });

            var now = DateTimeOffset.UtcNow;
            var price = await db.ProductPrices.AsNoTracking()
                .Where(x => x.ProductId == product.Id && x.ValidFrom <= now && (x.ValidTo == null || x.ValidTo > now))
                .OrderByDescending(x => x.ValidFrom)
                .FirstOrDefaultAsync(ct);
            if (price is null) return Results.Conflict(new { message = "Product has no active price." });

            var line = new OrderItem
            {
                OrderId = order.Id,
                Order = order,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                Quantity = request.Quantity,
                UnitPrice = price.Amount,
                LineTotal = decimal.Round(price.Amount * request.Quantity, 4, MidpointRounding.AwayFromZero),
                CreatedByEmployeeId = employeeId,
                Status = OrderItemStatus.New
            };

            // Explicitly mark a client-generated UUID entity as new. Without this, EF Core may
            // infer an existing row from the non-default key and issue UPDATE instead of INSERT.
            db.OrderItems.Add(line);
            Recalculate(order);
            order.Version++;
            order.UpdatedAt = now;

            db.AuditEvents.Add(Audit(restaurantId, employeeId, "ITEM_ADDED", "Order", order.Id, new
            {
                lineId = line.Id,
                productId = product.Id,
                productName = product.Name,
                request.Quantity,
                unitPrice = price.Amount
            }));
            db.OutboxEvents.Add(Outbox(restaurantId, "ORDER_CHANGED", "Order", order.Id, new { order.Id, order.Version }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var order = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (order is null) return Results.NotFound(new { message = "Order not found." });
            var line = order.Items.FirstOrDefault(x => x.Id == itemId);
            if (line is null) return Results.NotFound(new { message = "Order item not found." });
            if (line.Status != OrderItemStatus.New)
                return Results.Conflict(new { message = "Sent items cannot be deleted; they require an explicit void operation." });

            db.OrderItems.Remove(line);
            order.Items.Remove(line);
            Recalculate(order);
            order.Version++;
            order.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(Audit(restaurantId, employeeId, "ITEM_REMOVED", "Order", order.Id, new { itemId }));
            db.OutboxEvents.Add(Outbox(restaurantId, "ORDER_CHANGED", "Order", order.Id, new { order.Id, order.Version }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        return app;
    }

    private static void Recalculate(Order order)
    {
        order.Subtotal = order.Items.Where(x => x.Status != OrderItemStatus.Voided).Sum(x => x.LineTotal);
        order.Total = decimal.Round(order.Subtotal - order.DiscountTotal + order.SurchargeTotal, 4, MidpointRounding.AwayFromZero);
    }

    private static object ToDto(Order order) => new
    {
        order.Id,
        order.DisplayNumber,
        status = EnumText(order.Status),
        order.TableId,
        order.GuestCount,
        order.Subtotal,
        order.DiscountTotal,
        order.SurchargeTotal,
        order.Total,
        order.PaidTotal,
        order.Version,
        order.CreatedAt,
        order.UpdatedAt,
        items = order.Items.OrderBy(x => x.CreatedAt).Select(x => new
        {
            x.Id,
            x.ProductId,
            productName = x.ProductNameSnapshot,
            x.Quantity,
            x.UnitPrice,
            x.ModifiersTotal,
            x.LineTotal,
            status = EnumText(x.Status),
            x.Comment
        })
    };

    private static AuditEvent Audit(Guid restaurantId, Guid employeeId, string eventType, string entityType, Guid entityId, object payload) => new()
    {
        RestaurantId = restaurantId,
        EmployeeId = employeeId,
        EventType = eventType,
        EntityType = entityType,
        EntityId = entityId,
        PayloadJson = JsonSerializer.Serialize(payload)
    };

    private static OutboxEvent Outbox(Guid restaurantId, string eventType, string aggregateType, Guid aggregateId, object payload) => new()
    {
        RestaurantId = restaurantId,
        EventType = eventType,
        AggregateType = aggregateType,
        AggregateId = aggregateId,
        PayloadJson = JsonSerializer.Serialize(payload)
    };

    private static string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Regex.Replace(value.ToString(), "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant();

    private static bool TryClaims(ClaimsPrincipal user, out Guid restaurantId, out Guid employeeId)
    {
        var okRestaurant = Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
        var okEmployee = Guid.TryParse(user.FindFirstValue("employee_id"), out employeeId);
        return okRestaurant && okEmployee;
    }
}

public sealed record CreateOrderRequest(Guid? TableId = null, int GuestCount = 1);
public sealed record AddOrderItemRequest(Guid ProductId, decimal Quantity = 1m);
