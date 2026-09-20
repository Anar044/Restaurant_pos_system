using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Features.Orders;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments").RequireAuthorization();

        group.MapPost("/", async (
            CreatePaymentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.OrderId == Guid.Empty || request.ShiftId == Guid.Empty)
                return Results.BadRequest(new { message = "OrderId and ShiftId are required." });

            if (request.Amount <= 0)
                return Results.BadRequest(new { message = "Payment amount must be greater than zero." });

            if (!Enum.IsDefined(request.Method))
                return Results.BadRequest(new { message = "Unsupported payment method." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var shift = await db.Shifts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.ShiftId &&
                    x.RestaurantId == restaurantId &&
                    x.Status == ShiftStatus.Open,
                    ct);

            if (shift is null)
                return Results.Conflict(new { message = "An open POS shift is required before payment." });

            var order = await db.Orders
                .Include(x => x.Items)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x =>
                    x.Id == request.OrderId &&
                    x.RestaurantId == restaurantId,
                    ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });

            if (order.Status is OrderStatus.Closed or OrderStatus.Cancelled)
                return Results.Conflict(new { message = $"Order cannot be paid in status {order.Status}." });

            if (order.Items.Count == 0 || order.Total <= 0)
                return Results.Conflict(new { message = "Order has no payable total." });

            var completedTotal = order.Payments
                .Where(x => x.Status == PaymentStatus.Completed)
                .Sum(x => x.Amount);

            var remaining = decimal.Round(
                Math.Max(0m, order.Total - completedTotal),
                4,
                MidpointRounding.AwayFromZero);

            if (remaining <= 0)
                return Results.Conflict(new { message = "Order is already fully paid." });

            var amount = decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero);
            if (amount > remaining)
                return Results.BadRequest(new
                {
                    message = "Payment amount cannot exceed the remaining order balance.",
                    remaining
                });

            var restaurantCurrency = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => x.CurrencyCode)
                .FirstAsync(ct);

            var payment = new Payment
            {
                RestaurantId = restaurantId,
                OrderId = order.Id,
                ShiftId = shift.Id,
                EmployeeId = employeeId,
                Method = request.Method,
                Status = PaymentStatus.Completed,
                Amount = amount,
                CurrencyCode = restaurantCurrency,
                ProviderReference = string.IsNullOrWhiteSpace(request.ProviderReference)
                    ? null
                    : request.ProviderReference.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Payments.Add(payment);
            order.Payments.Add(payment);

            var newPaidTotal = decimal.Round(
                completedTotal + amount,
                4,
                MidpointRounding.AwayFromZero);

            order.PaidTotal = newPaidTotal;
            order.Status = newPaidTotal >= order.Total
                ? OrderStatus.Paid
                : OrderStatus.PartiallyPaid;
            order.Version++;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "PAYMENT_COMPLETED",
                "Payment",
                payment.Id,
                new
                {
                    payment.Id,
                    orderId = order.Id,
                    shiftId = shift.Id,
                    method = payment.Method.ToString().ToUpperInvariant(),
                    payment.Amount,
                    payment.CurrencyCode,
                    order.PaidTotal,
                    order.Total,
                    order.Status
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "PAYMENT_COMPLETED",
                "Payment",
                payment.Id,
                new
                {
                    payment.Id,
                    orderId = order.Id,
                    shiftId = shift.Id,
                    method = payment.Method.ToString().ToUpperInvariant(),
                    payment.Amount,
                    payment.CurrencyCode,
                    payment.CreatedAt
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/v1/payments/{payment.Id}", new
            {
                payment = ToDto(payment),
                order = OrderEndpoints.ToDto(order),
                remaining = decimal.Round(
                    Math.Max(0m, order.Total - order.PaidTotal),
                    4,
                    MidpointRounding.AwayFromZero)
            });
        }).RequireAuthorization("payments.write");

        group.MapGet("/order/{orderId:guid}", async (
            Guid orderId,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var exists = await db.Orders
                .AsNoTracking()
                .AnyAsync(x => x.Id == orderId && x.RestaurantId == restaurantId, ct);

            if (!exists)
                return Results.NotFound(new { message = "Order not found." });

            var payments = await db.Payments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.OrderId == orderId)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                payments = payments.Select(ToDto),
                completedTotal = payments
                    .Where(x => x.Status == PaymentStatus.Completed)
                    .Sum(x => x.Amount)
            });
        }).RequireAuthorization("orders.read");

        return app;
    }

    private static object ToDto(Payment payment) => new
    {
        payment.Id,
        payment.OrderId,
        payment.ShiftId,
        payment.EmployeeId,
        method = payment.Method.ToString().ToUpperInvariant(),
        status = payment.Status.ToString().ToUpperInvariant(),
        payment.Amount,
        payment.CurrencyCode,
        payment.ProviderReference,
        payment.CreatedAt
    };

    private static bool TryClaims(
        ClaimsPrincipal user,
        out Guid restaurantId,
        out Guid employeeId)
    {
        var okRestaurant = Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
        var okEmployee = Guid.TryParse(user.FindFirstValue("employee_id"), out employeeId);
        return okRestaurant && okEmployee;
    }

    private static AuditEvent Audit(
        Guid restaurantId,
        Guid employeeId,
        string eventType,
        string entityType,
        Guid entityId,
        object payload) => new()
    {
        RestaurantId = restaurantId,
        EmployeeId = employeeId,
        EventType = eventType,
        EntityType = entityType,
        EntityId = entityId,
        PayloadJson = JsonSerializer.Serialize(payload)
    };

    private static OutboxEvent Outbox(
        Guid restaurantId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object payload) => new()
    {
        RestaurantId = restaurantId,
        EventType = eventType,
        AggregateType = aggregateType,
        AggregateId = aggregateId,
        PayloadJson = JsonSerializer.Serialize(payload)
    };
}

public sealed record CreatePaymentRequest(
    Guid OrderId,
    Guid ShiftId,
    PaymentMethod Method,
    decimal Amount,
    string? ProviderReference = null);
