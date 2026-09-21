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
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .Where(x => x.RestaurantId == restaurantId && x.Status != OrderStatus.Closed && x.Status != OrderStatus.Cancelled)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(100)
                .ToListAsync(ct);
            return Results.Ok(new { orders = orders.Select(ToDto) });
        }).RequireAuthorization("orders.read");

        group.MapGet("/history", async (
            Guid? shiftId,
            int? orderNumber,
            int? take,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var limit = Math.Clamp(take ?? 100, 1, 300);

            var query = db.Orders
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    (x.Status == OrderStatus.Closed || x.Status == OrderStatus.Paid));

            if (shiftId.HasValue)
            {
                query = query.Where(x =>
                    x.Payments.Any(payment => payment.ShiftId == shiftId.Value));
            }

            if (orderNumber.HasValue)
                query = query.Where(x => x.DisplayNumber == orderNumber.Value);

            var orders = await query
                .OrderByDescending(x => x.ClosedAt ?? x.UpdatedAt)
                .Take(limit)
                .ToListAsync(ct);

            var tableIds = orders
                .Where(x => x.TableId.HasValue)
                .Select(x => x.TableId!.Value)
                .Distinct()
                .ToArray();

            var tableRows = await (
                from table in db.DiningTables.AsNoTracking()
                join hall in db.Halls.AsNoTracking() on table.HallId equals hall.Id
                where table.RestaurantId == restaurantId &&
                      tableIds.Contains(table.Id)
                select new
                {
                    table.Id,
                    TableName = table.Name,
                    HallName = hall.Name
                })
                .ToListAsync(ct);

            var tables = tableRows.ToDictionary(x => x.Id);

            var employeeIds = orders
                .SelectMany(order =>
                    order.Payments.Select(payment => payment.EmployeeId)
                        .Append(order.CreatedByEmployeeId))
                .Distinct()
                .ToArray();

            var employees = await db.Employees
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    employeeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            return Results.Ok(new
            {
                orders = orders.Select(order =>
                {
                    var tableInfo = order.TableId.HasValue
                        ? tables.GetValueOrDefault(order.TableId.Value)
                        : null;
                    var lastPayment = order.Payments
                        .Where(payment =>
                            payment.Status == PaymentStatus.Completed ||
                            payment.Status == PaymentStatus.Refunded)
                        .OrderBy(payment => payment.CreatedAt)
                        .LastOrDefault();
                    var cashierId = lastPayment?.EmployeeId ?? order.CreatedByEmployeeId;

                    return new
                    {
                        order = ToDto(order),
                        hallName = tableInfo?.HallName,
                        tableName = tableInfo?.TableName,
                        cashierName = employees.GetValueOrDefault(
                            cashierId,
                            "Employee")
                    };
                })
            });
        }).RequireAuthorization("orders.read");

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _)) return Results.Unauthorized();
            var order = await db.Orders
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
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
            var order = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Modifiers).FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (order is null) return Results.NotFound(new { message = "Order not found." });
            if (order.Status is OrderStatus.Closed or OrderStatus.Cancelled or OrderStatus.Paid or OrderStatus.PartiallyPaid)
                return Results.Conflict(new { message = $"Order cannot be edited in status {order.Status}." });

            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ProductId && x.RestaurantId == restaurantId && x.IsActive, ct);
            if (product is null) return Results.BadRequest(new { message = "Product is not available." });

            var now = DateTimeOffset.UtcNow;
            var price = await db.ProductPrices.AsNoTracking()
                .Where(x => x.ProductId == product.Id && x.ValidFrom <= now && (x.ValidTo == null || x.ValidTo > now))
                .OrderByDescending(x => x.ValidFrom)
                .FirstOrDefaultAsync(ct);
            if (price is null) return Results.Conflict(new { message = "Product has no active price." });

            var comment = NormalizeText(request.Comment, 500);
            var selections = (request.Modifiers ?? [])
                .Where(x => x.GroupId != Guid.Empty && x.ModifierId != Guid.Empty)
                .ToArray();

            if (selections.Any(x => x.Quantity <= 0 || x.Quantity > 100))
                return Results.BadRequest(new { message = "Modifier quantity must be between 0 and 100." });

            if (selections
                .GroupBy(x => new { x.GroupId, x.ModifierId })
                .Any(x => x.Count() > 1))
            {
                return Results.BadRequest(new { message = "The same modifier cannot be selected twice in one group." });
            }

            var productGroupLinks = await db.ProductModifierGroups
                .AsNoTracking()
                .Where(x => x.ProductId == product.Id)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            var productGroupIds = productGroupLinks
                .Select(x => x.ModifierGroupId)
                .Distinct()
                .ToArray();

            var modifierGroups = await db.ModifierGroups
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    productGroupIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            if (selections.Any(x => !modifierGroups.ContainsKey(x.GroupId)))
            {
                return Results.BadRequest(new
                {
                    message = "One or more selected modifier groups are not available for this product."
                });
            }

            var groupModifierLinks = await db.ModifierGroupModifiers
                .AsNoTracking()
                .Where(x => productGroupIds.Contains(x.ModifierGroupId))
                .ToListAsync(ct);

            var selectedModifierIds = selections
                .Select(x => x.ModifierId)
                .Distinct()
                .ToArray();

            var modifierEntities = await db.Modifiers
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    selectedModifierIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            foreach (var group in modifierGroups.Values)
            {
                var selectedForGroup = selections
                    .Where(x => x.GroupId == group.Id)
                    .ToArray();
                var selectedCount = selectedForGroup.Sum(x => x.Quantity);

                if (selectedCount < group.MinSelections ||
                    selectedCount > group.MaxSelections)
                {
                    return Results.BadRequest(new
                    {
                        message = $"Modifier group '{group.Name}' requires between {group.MinSelections} and {group.MaxSelections} selections."
                    });
                }

                foreach (var selection in selectedForGroup)
                {
                    var linked = groupModifierLinks.Any(x =>
                        x.ModifierGroupId == group.Id &&
                        x.ModifierId == selection.ModifierId);

                    if (!linked || !modifierEntities.ContainsKey(selection.ModifierId))
                    {
                        return Results.BadRequest(new
                        {
                            message = $"A selected modifier is not available in group '{group.Name}'."
                        });
                    }
                }
            }

            if (modifierGroups.Values.Any(group =>
                    group.IsRequired &&
                    selections.Where(x => x.GroupId == group.Id).Sum(x => x.Quantity) < 1))
            {
                return Results.BadRequest(new { message = "Complete all required modifier groups." });
            }

            var line = new OrderItem
            {
                OrderId = order.Id,
                Order = order,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                Quantity = request.Quantity,
                UnitPrice = price.Amount,
                Comment = comment,
                CreatedByEmployeeId = employeeId,
                Status = OrderItemStatus.New
            };

            foreach (var selection in selections)
            {
                var modifier = modifierEntities[selection.ModifierId];
                var modifierTotal = decimal.Round(
                    modifier.PriceDelta * selection.Quantity * request.Quantity,
                    4,
                    MidpointRounding.AwayFromZero);

                line.Modifiers.Add(new OrderItemModifier
                {
                    OrderItemId = line.Id,
                    ModifierId = modifier.Id,
                    ModifierNameSnapshot = modifier.Name,
                    Quantity = selection.Quantity,
                    PriceDelta = modifier.PriceDelta,
                    Total = modifierTotal
                });
            }

            line.ModifiersTotal = decimal.Round(
                line.Modifiers.Sum(x => x.Total),
                4,
                MidpointRounding.AwayFromZero);
            line.LineTotal = decimal.Round(
                price.Amount * request.Quantity + line.ModifiersTotal,
                4,
                MidpointRounding.AwayFromZero);

            // Explicitly mark the whole graph as new because UUIDs are generated client-side.
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
                unitPrice = price.Amount,
                modifiersTotal = line.ModifiersTotal,
                modifiers = line.Modifiers.Select(x => new
                {
                    x.ModifierId,
                    name = x.ModifierNameSnapshot,
                    x.Quantity,
                    x.PriceDelta,
                    x.Total
                }),
                comment
            }));
            db.OutboxEvents.Add(Outbox(restaurantId, "ORDER_CHANGED", "Order", order.Id, new { order.Id, order.Version }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapPut("/{id:guid}/guest-count", async (
            Guid id,
            UpdateGuestCountRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.GuestCount is < 1 or > 100)
                return Results.BadRequest(new { message = "GuestCount must be between 1 and 100." });

            var order = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });

            if (!CanEditOrder(order.Status))
                return Results.Conflict(new { message = $"Order cannot be edited in status {order.Status}." });

            var previous = order.GuestCount;
            order.GuestCount = request.GuestCount;
            order.Version++;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "ORDER_GUEST_COUNT_CHANGED",
                "Order",
                order.Id,
                new { previous, current = order.GuestCount }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CHANGED",
                "Order",
                order.Id,
                new { order.Id, order.Version }));

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapPut("/{id:guid}/table", async (
            Guid id,
            MoveOrderRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var order = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });

            if (!CanEditOrder(order.Status))
                return Results.Conflict(new { message = $"Order cannot be moved in status {order.Status}." });

            var target = await (
                from table in db.DiningTables.AsNoTracking()
                join hall in db.Halls.AsNoTracking() on table.HallId equals hall.Id
                where table.Id == request.TableId &&
                      table.RestaurantId == restaurantId &&
                      table.IsActive &&
                      hall.IsActive
                select new
                {
                    table.Id,
                    TableName = table.Name,
                    HallName = hall.Name
                })
                .FirstOrDefaultAsync(ct);

            if (target is null)
                return Results.BadRequest(new { message = "Target table is not available." });

            if (order.TableId == target.Id)
                return Results.Ok(ToDto(order));

            var occupied = await db.Orders
                .AsNoTracking()
                .AnyAsync(x =>
                    x.RestaurantId == restaurantId &&
                    x.Id != order.Id &&
                    x.TableId == target.Id &&
                    x.Status != OrderStatus.Closed &&
                    x.Status != OrderStatus.Cancelled,
                    ct);

            if (occupied)
                return Results.Conflict(new { message = "Target table already has an open order." });

            var previousTableId = order.TableId;
            order.TableId = target.Id;
            order.Version++;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "ORDER_MOVED",
                "Order",
                order.Id,
                new
                {
                    previousTableId,
                    tableId = target.Id,
                    target.HallName,
                    target.TableName
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CHANGED",
                "Order",
                order.Id,
                new { order.Id, order.Version, order.TableId }));

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapPut("/{id:guid}/items/{itemId:guid}/comment", async (
            Guid id,
            Guid itemId,
            UpdateOrderItemCommentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var order = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });

            if (!CanEditOrder(order.Status))
                return Results.Conflict(new { message = $"Order cannot be edited in status {order.Status}." });

            var line = order.Items.FirstOrDefault(x => x.Id == itemId);
            if (line is null)
                return Results.NotFound(new { message = "Order item not found." });

            if (line.Status != OrderItemStatus.New)
                return Results.Conflict(new
                {
                    message = "A kitchen comment can only be changed before the item is sent."
                });

            var previous = line.Comment;
            line.Comment = NormalizeText(request.Comment, 500);
            order.Version++;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "ITEM_COMMENT_CHANGED",
                "Order",
                order.Id,
                new { lineId = line.Id, previous, current = line.Comment }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CHANGED",
                "Order",
                order.Id,
                new { order.Id, order.Version }));

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapPut("/{id:guid}/items/{itemId:guid}/modifiers", async (
            Guid id,
            Guid itemId,
            UpdateOrderItemModifiersRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var order = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });

            if (!CanEditOrder(order.Status))
                return Results.Conflict(new { message = $"Order cannot be edited in status {order.Status}." });

            var line = order.Items.FirstOrDefault(x => x.Id == itemId);
            if (line is null)
                return Results.NotFound(new { message = "Order item not found." });

            if (line.Status != OrderItemStatus.New)
            {
                return Results.Conflict(new
                {
                    message = "Modifiers can only be changed before the item is sent to the kitchen."
                });
            }

            var product = await db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == line.ProductId &&
                    x.RestaurantId == restaurantId &&
                    x.IsActive,
                    ct);

            if (product is null)
                return Results.Conflict(new { message = "Product is no longer available." });

            var selections = (request.Modifiers ?? [])
                .Where(x => x.GroupId != Guid.Empty && x.ModifierId != Guid.Empty)
                .ToArray();

            if (selections.Any(x => x.Quantity <= 0 || x.Quantity > 100))
                return Results.BadRequest(new { message = "Modifier quantity must be between 0 and 100." });

            if (selections
                .GroupBy(x => new { x.GroupId, x.ModifierId })
                .Any(x => x.Count() > 1))
            {
                return Results.BadRequest(new
                {
                    message = "The same modifier cannot be selected twice in one group."
                });
            }

            var productGroupLinks = await db.ProductModifierGroups
                .AsNoTracking()
                .Where(x => x.ProductId == product.Id)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            var productGroupIds = productGroupLinks
                .Select(x => x.ModifierGroupId)
                .Distinct()
                .ToArray();

            var modifierGroups = await db.ModifierGroups
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    productGroupIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            if (selections.Any(x => !modifierGroups.ContainsKey(x.GroupId)))
            {
                return Results.BadRequest(new
                {
                    message = "One or more selected modifier groups are not available for this product."
                });
            }

            var groupModifierLinks = await db.ModifierGroupModifiers
                .AsNoTracking()
                .Where(x => productGroupIds.Contains(x.ModifierGroupId))
                .ToListAsync(ct);

            var selectedModifierIds = selections
                .Select(x => x.ModifierId)
                .Distinct()
                .ToArray();

            var modifierEntities = await db.Modifiers
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    selectedModifierIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            foreach (var group in modifierGroups.Values)
            {
                var selectedForGroup = selections
                    .Where(x => x.GroupId == group.Id)
                    .ToArray();
                var selectedCount = selectedForGroup.Sum(x => x.Quantity);

                if (selectedCount < group.MinSelections ||
                    selectedCount > group.MaxSelections)
                {
                    return Results.BadRequest(new
                    {
                        message = $"Modifier group '{group.Name}' requires between {group.MinSelections} and {group.MaxSelections} selections."
                    });
                }

                foreach (var selection in selectedForGroup)
                {
                    var linked = groupModifierLinks.Any(x =>
                        x.ModifierGroupId == group.Id &&
                        x.ModifierId == selection.ModifierId);

                    if (!linked || !modifierEntities.ContainsKey(selection.ModifierId))
                    {
                        return Results.BadRequest(new
                        {
                            message = $"A selected modifier is not available in group '{group.Name}'."
                        });
                    }
                }
            }

            if (modifierGroups.Values.Any(group =>
                    group.IsRequired &&
                    selections.Where(x => x.GroupId == group.Id).Sum(x => x.Quantity) < 1))
            {
                return Results.BadRequest(new { message = "Complete all required modifier groups." });
            }

            var previous = line.Modifiers
                .Select(x => new
                {
                    x.ModifierId,
                    name = x.ModifierNameSnapshot,
                    x.Quantity,
                    x.PriceDelta,
                    x.Total
                })
                .ToArray();

            var oldModifiers = line.Modifiers.ToArray();
            db.OrderItemModifiers.RemoveRange(oldModifiers);
            line.Modifiers.Clear();

            var replacement = new List<OrderItemModifier>();
            foreach (var selection in selections)
            {
                var modifier = modifierEntities[selection.ModifierId];
                var total = decimal.Round(
                    modifier.PriceDelta * selection.Quantity * line.Quantity,
                    4,
                    MidpointRounding.AwayFromZero);

                replacement.Add(new OrderItemModifier
                {
                    OrderItemId = line.Id,
                    ModifierId = modifier.Id,
                    ModifierNameSnapshot = modifier.Name,
                    Quantity = selection.Quantity,
                    PriceDelta = modifier.PriceDelta,
                    Total = total
                });
            }

            if (replacement.Count > 0)
            {
                line.Modifiers.AddRange(replacement);
                db.OrderItemModifiers.AddRange(replacement);
            }

            line.ModifiersTotal = decimal.Round(
                replacement.Sum(x => x.Total),
                4,
                MidpointRounding.AwayFromZero);
            line.LineTotal = decimal.Round(
                line.UnitPrice * line.Quantity + line.ModifiersTotal,
                4,
                MidpointRounding.AwayFromZero);

            Recalculate(order);
            order.Version++;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "ITEM_MODIFIERS_CHANGED",
                "Order",
                order.Id,
                new
                {
                    lineId = line.Id,
                    line.ProductId,
                    line.ProductNameSnapshot,
                    previous,
                    current = replacement.Select(x => new
                    {
                        x.ModifierId,
                        name = x.ModifierNameSnapshot,
                        x.Quantity,
                        x.PriceDelta,
                        x.Total
                    }),
                    line.ModifiersTotal,
                    line.LineTotal
                }));

            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CHANGED",
                "Order",
                order.Id,
                new { order.Id, order.Version }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapPost("/{id:guid}/items/{itemId:guid}/void", async (
            Guid id,
            Guid itemId,
            VoidOrderItemRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var reason = NormalizeText(request.Reason, 500);
            if (reason is null)
                return Results.BadRequest(new { message = "Void reason is required." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var order = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });

            if (!CanEditOrder(order.Status))
                return Results.Conflict(new { message = $"Order cannot be edited in status {order.Status}." });

            var line = order.Items.FirstOrDefault(x => x.Id == itemId);
            if (line is null)
                return Results.NotFound(new { message = "Order item not found." });

            if (line.Status == OrderItemStatus.Voided)
                return Results.Ok(ToDto(order));

            if (line.Status != OrderItemStatus.Sent)
                return Results.Conflict(new
                {
                    message = "Only a sent item requires a void. New items can be removed normally."
                });

            var route = await (
                from product in db.Products.AsNoTracking()
                join station in db.KitchenStations.AsNoTracking()
                    on product.KitchenStationId equals (Guid?)station.Id
                join printer in db.Printers.AsNoTracking()
                    on station.PrinterId equals (Guid?)printer.Id
                where product.Id == line.ProductId &&
                      product.RestaurantId == restaurantId &&
                      station.RestaurantId == restaurantId &&
                      station.IsActive &&
                      printer.RestaurantId == restaurantId &&
                      printer.IsConfigured &&
                      printer.IsActive &&
                      printer.HostDeviceId.HasValue
                select new
                {
                    StationId = station.Id,
                    StationName = station.Name
                })
                .FirstOrDefaultAsync(ct);

            if (route is null)
                return Results.Conflict(new
                {
                    message = "Kitchen cancellation cannot be printed because the item's kitchen printer is unavailable."
                });

            string? tableName = null;
            string? hallName = null;
            if (order.TableId.HasValue)
            {
                var tableInfo = await (
                    from table in db.DiningTables.AsNoTracking()
                    join hall in db.Halls.AsNoTracking() on table.HallId equals hall.Id
                    where table.Id == order.TableId.Value &&
                          table.RestaurantId == restaurantId
                    select new { TableName = table.Name, HallName = hall.Name })
                    .FirstOrDefaultAsync(ct);

                tableName = tableInfo?.TableName;
                hallName = tableInfo?.HallName;
            }

            var now = DateTimeOffset.UtcNow;
            var ticket = new KitchenTicket
            {
                RestaurantId = restaurantId,
                OrderId = order.Id,
                KitchenStationId = route.StationId,
                Status = KitchenTicketStatus.Pending,
                CreatedAt = now
            };
            db.KitchenTickets.Add(ticket);

            var payload = new
            {
                ticketId = ticket.Id,
                orderId = order.Id,
                orderNumber = order.DisplayNumber,
                order.TableId,
                tableName,
                hallName,
                stationId = route.StationId,
                stationName = route.StationName,
                createdAt = now,
                isVoid = true,
                voidReason = reason,
                items = new[]
                {
                    new
                    {
                        lineId = line.Id,
                        productId = line.ProductId,
                        name = line.ProductNameSnapshot,
                        line.Quantity,
                        line.Comment,
                        modifiers = line.Modifiers.Select(modifier => new
                        {
                            modifier.ModifierId,
                            name = modifier.ModifierNameSnapshot,
                            modifier.Quantity
                        })
                    }
                }
            };

            db.PrintJobs.Add(new PrintJob
            {
                RestaurantId = restaurantId,
                PrinterKey = $"kitchen:{route.StationId:N}",
                Type = "KITCHEN_TICKET",
                PayloadJson = JsonSerializer.Serialize(payload),
                Status = PrintJobStatus.Pending,
                CreatedAt = now
            });

            line.Status = OrderItemStatus.Voided;
            line.VoidedAt = now;
            Recalculate(order);
            RefreshOrderStatus(order);
            order.Version++;
            order.UpdatedAt = now;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "ITEM_VOIDED",
                "Order",
                order.Id,
                new
                {
                    lineId = line.Id,
                    line.ProductId,
                    line.ProductNameSnapshot,
                    line.Quantity,
                    reason,
                    kitchenStationId = route.StationId,
                    ticketId = ticket.Id
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CHANGED",
                "Order",
                order.Id,
                new { order.Id, order.Version }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.void");

        group.MapPost("/{id:guid}/transfer-items", async (
            Guid id,
            TransferOrderItemsRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var itemIds = (request.ItemIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            if (itemIds.Length == 0)
                return Results.BadRequest(new { message = "Select at least one order item to transfer." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var source = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (source is null)
                return Results.NotFound(new { message = "Source order not found." });

            if (!CanEditOrder(source.Status))
                return Results.Conflict(new { message = $"Order cannot be edited in status {source.Status}." });

            if (!source.TableId.HasValue)
                return Results.Conflict(new { message = "Source order is not assigned to a table." });

            if (source.TableId.Value == request.TargetTableId)
                return Results.BadRequest(new { message = "Target table must be different from the current table." });

            var sourceTableInfo = await (
                from table in db.DiningTables.AsNoTracking()
                join hall in db.Halls.AsNoTracking() on table.HallId equals hall.Id
                where table.Id == source.TableId.Value &&
                      table.RestaurantId == restaurantId
                select new
                {
                    TableId = table.Id,
                    TableName = table.Name,
                    HallName = hall.Name
                })
                .FirstOrDefaultAsync(ct);

            var targetTableInfo = await (
                from table in db.DiningTables.AsNoTracking()
                join hall in db.Halls.AsNoTracking() on table.HallId equals hall.Id
                where table.Id == request.TargetTableId &&
                      table.RestaurantId == restaurantId &&
                      table.IsActive &&
                      hall.IsActive
                select new
                {
                    TableId = table.Id,
                    TableName = table.Name,
                    HallName = hall.Name
                })
                .FirstOrDefaultAsync(ct);

            if (targetTableInfo is null)
                return Results.BadRequest(new { message = "Target table is not available." });

            var selected = source.Items
                .Where(x => itemIds.Contains(x.Id) && x.Status != OrderItemStatus.Voided)
                .ToList();

            if (selected.Count != itemIds.Length)
                return Results.BadRequest(new
                {
                    message = "One or more selected positions are unavailable or already voided."
                });

            var target = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.TableId == request.TargetTableId &&
                    x.Status != OrderStatus.Closed &&
                    x.Status != OrderStatus.Cancelled)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            var targetCreated = false;
            if (target is not null && !CanEditOrder(target.Status))
                return Results.Conflict(new
                {
                    message = $"Target table order cannot accept positions in status {target.Status}."
                });

            if (target is null)
            {
                target = new Order
                {
                    RestaurantId = restaurantId,
                    TableId = request.TargetTableId,
                    CreatedByEmployeeId = employeeId,
                    GuestCount = 1,
                    Status = OrderStatus.Open
                };
                db.Orders.Add(target);
                targetCreated = true;

                db.AuditEvents.Add(Audit(
                    restaurantId,
                    employeeId,
                    "ORDER_CREATED_BY_ITEM_TRANSFER",
                    "Order",
                    target.Id,
                    new
                    {
                        sourceOrderId = source.Id,
                        sourceOrderNumber = source.DisplayNumber,
                        request.TargetTableId
                    }));

                // Generate the target display number before building transfer kitchen tickets.
                await db.SaveChangesAsync(ct);
            }

            var now = DateTimeOffset.UtcNow;
            var movedSentItems = selected
                .Where(x => x.Status == OrderItemStatus.Sent)
                .ToList();

            foreach (var item in selected)
            {
                source.Items.Remove(item);
                item.OrderId = target.Id;
                item.Order = target;
                target.Items.Add(item);
            }

            Recalculate(source);
            Recalculate(target);

            if (source.Items.Any(x => x.Status != OrderItemStatus.Voided))
            {
                RefreshOrderStatus(source);
            }
            else
            {
                source.Status = OrderStatus.Cancelled;
            }

            RefreshOrderStatus(target);

            source.Version++;
            source.UpdatedAt = now;
            target.Version++;
            target.UpdatedAt = now;

            var transferTicketIds = new List<Guid>();

            if (movedSentItems.Count > 0)
            {
                var productIds = movedSentItems
                    .Select(x => x.ProductId)
                    .Distinct()
                    .ToArray();

                var routing = await db.Products
                    .AsNoTracking()
                    .Where(x => x.RestaurantId == restaurantId && productIds.Contains(x.Id))
                    .Select(x => new { x.Id, x.KitchenStationId })
                    .ToDictionaryAsync(x => x.Id, ct);

                var stationIds = routing.Values
                    .Where(x => x.KitchenStationId.HasValue)
                    .Select(x => x.KitchenStationId!.Value)
                    .Distinct()
                    .ToArray();

                var stations = await db.KitchenStations
                    .AsNoTracking()
                    .Where(x =>
                        x.RestaurantId == restaurantId &&
                        stationIds.Contains(x.Id) &&
                        x.IsActive &&
                        x.PrinterId.HasValue)
                    .ToDictionaryAsync(x => x.Id, ct);

                foreach (var stationId in stationIds)
                {
                    if (!stations.TryGetValue(stationId, out var station))
                        continue;

                    var stationItems = movedSentItems
                        .Where(x =>
                            routing.TryGetValue(x.ProductId, out var route) &&
                            route.KitchenStationId == stationId)
                        .ToList();

                    if (stationItems.Count == 0)
                        continue;

                    var ticket = new KitchenTicket
                    {
                        RestaurantId = restaurantId,
                        OrderId = target.Id,
                        KitchenStationId = stationId,
                        Status = KitchenTicketStatus.Pending,
                        CreatedAt = now
                    };
                    db.KitchenTickets.Add(ticket);
                    transferTicketIds.Add(ticket.Id);

                    var payload = new
                    {
                        ticketId = ticket.Id,
                        orderId = target.Id,
                        orderNumber = target.DisplayNumber,
                        target.TableId,
                        tableName = targetTableInfo.TableName,
                        hallName = targetTableInfo.HallName,
                        stationId,
                        stationName = station.Name,
                        createdAt = now,
                        isTransfer = true,
                        fromOrderNumber = source.DisplayNumber,
                        toOrderNumber = target.DisplayNumber,
                        fromTableName = sourceTableInfo?.TableName,
                        fromHallName = sourceTableInfo?.HallName,
                        toTableName = targetTableInfo.TableName,
                        toHallName = targetTableInfo.HallName,
                        items = stationItems.Select(x => new
                        {
                            lineId = x.Id,
                            productId = x.ProductId,
                            name = x.ProductNameSnapshot,
                            x.Quantity,
                            x.Comment,
                            modifiers = x.Modifiers.Select(modifier => new
                            {
                                modifier.ModifierId,
                                name = modifier.ModifierNameSnapshot,
                                modifier.Quantity
                            })
                        })
                    };

                    db.PrintJobs.Add(new PrintJob
                    {
                        RestaurantId = restaurantId,
                        PrinterKey = $"kitchen:{stationId:N}",
                        Type = "KITCHEN_TICKET",
                        PayloadJson = JsonSerializer.Serialize(payload),
                        Status = PrintJobStatus.Pending,
                        CreatedAt = now
                    });
                }
            }

            var movedItemIds = selected.Select(x => x.Id).ToArray();

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "ORDER_ITEMS_TRANSFERRED",
                "Order",
                source.Id,
                new
                {
                    sourceOrderId = source.Id,
                    sourceOrderNumber = source.DisplayNumber,
                    targetOrderId = target.Id,
                    targetOrderNumber = target.DisplayNumber,
                    targetCreated,
                    sourceTableId = source.TableId,
                    targetTableId = request.TargetTableId,
                    itemIds = movedItemIds,
                    transferTicketIds
                }));

            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CHANGED",
                "Order",
                source.Id,
                new { source.Id, source.Version }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CHANGED",
                "Order",
                target.Id,
                new { target.Id, target.Version }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                sourceOrder = ToDto(source),
                targetOrder = ToDto(target),
                targetCreated,
                movedItemIds
            });
        }).RequireAuthorization("orders.write");

        group.MapPost("/{id:guid}/send", async (Guid id, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId)) return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var order = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null) return Results.NotFound(new { message = "Order not found." });
            if (order.Status is OrderStatus.Closed or OrderStatus.Cancelled or OrderStatus.Paid or OrderStatus.PartiallyPaid)
                return Results.Conflict(new { message = $"Order cannot be sent in status {order.Status}." });

            var newItems = order.Items
                .Where(x => x.Status == OrderItemStatus.New)
                .OrderBy(x => x.CreatedAt)
                .ToList();

            if (newItems.Count == 0)
                return Results.Conflict(new { message = "There are no new items to send to the kitchen." });

            var productIds = newItems.Select(x => x.ProductId).Distinct().ToArray();
            var routing = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.KitchenStationId })
                .ToDictionaryAsync(x => x.Id, ct);

            foreach (var item in newItems)
            {
                if (!routing.TryGetValue(item.ProductId, out var productRoute) || productRoute.KitchenStationId is null)
                    return Results.Conflict(new { message = $"Product '{item.ProductNameSnapshot}' has no kitchen station." });
            }

            var stationIds = routing.Values
                .Select(x => x.KitchenStationId!.Value)
                .Distinct()
                .ToArray();

            var stations = await db.KitchenStations
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && stationIds.Contains(x.Id) && x.IsActive)
                .ToDictionaryAsync(x => x.Id, ct);

            if (stations.Count != stationIds.Length)
                return Results.Conflict(new { message = "One or more kitchen stations are unavailable." });

            var stationWithoutPrinter = stations.Values.FirstOrDefault(x => !x.PrinterId.HasValue);
            if (stationWithoutPrinter is not null)
                return Results.Conflict(new
                {
                    message = $"Kitchen station '{stationWithoutPrinter.Name}' has no printer assigned. Configure it in BackOffice > Kitchen."
                });

            var kitchenPrinterIds = stations.Values
                .Select(x => x.PrinterId!.Value)
                .Distinct()
                .ToArray();

            var configuredKitchenPrinterCount = await db.Printers
                .AsNoTracking()
                .CountAsync(x =>
                    x.RestaurantId == restaurantId &&
                    kitchenPrinterIds.Contains(x.Id) &&
                    x.IsConfigured &&
                    x.IsActive &&
                    x.HostDeviceId.HasValue,
                    ct);

            if (configuredKitchenPrinterCount != kitchenPrinterIds.Length)
                return Results.Conflict(new
                {
                    message = "One or more kitchen printers are unavailable or are not attached to a POS Agent."
                });

            string? tableName = null;
            string? hallName = null;
            if (order.TableId.HasValue)
            {
                var tableInfo = await (
                    from table in db.DiningTables.AsNoTracking()
                    join hall in db.Halls.AsNoTracking() on table.HallId equals hall.Id
                    where table.Id == order.TableId.Value &&
                          table.RestaurantId == restaurantId
                    select new { TableName = table.Name, HallName = hall.Name })
                    .FirstOrDefaultAsync(ct);

                tableName = tableInfo?.TableName;
                hallName = tableInfo?.HallName;
            }

            var now = DateTimeOffset.UtcNow;
            var ticketIds = new List<Guid>();

            foreach (var stationId in stationIds)
            {
                var station = stations[stationId];
                var stationItems = newItems
                    .Where(x => routing[x.ProductId].KitchenStationId == stationId)
                    .ToList();

                var ticket = new KitchenTicket
                {
                    RestaurantId = restaurantId,
                    OrderId = order.Id,
                    KitchenStationId = stationId,
                    Status = KitchenTicketStatus.Pending,
                    CreatedAt = now
                };
                db.KitchenTickets.Add(ticket);
                ticketIds.Add(ticket.Id);

                var printPayload = new
                {
                    ticketId = ticket.Id,
                    orderId = order.Id,
                    orderNumber = order.DisplayNumber,
                    order.TableId,
                    tableName,
                    hallName,
                    stationId,
                    stationName = station.Name,
                    createdAt = now,
                    items = stationItems.Select(x => new
                    {
                        lineId = x.Id,
                        productId = x.ProductId,
                        name = x.ProductNameSnapshot,
                        x.Quantity,
                        x.Comment,
                        modifiers = x.Modifiers.Select(modifier => new
                        {
                            modifier.ModifierId,
                            name = modifier.ModifierNameSnapshot,
                            modifier.Quantity
                        })
                    })
                };

                db.PrintJobs.Add(new PrintJob
                {
                    RestaurantId = restaurantId,
                    PrinterKey = $"kitchen:{stationId:N}",
                    Type = "KITCHEN_TICKET",
                    PayloadJson = JsonSerializer.Serialize(printPayload),
                    Status = PrintJobStatus.Pending,
                    CreatedAt = now
                });
            }

            foreach (var item in newItems)
            {
                item.Status = OrderItemStatus.Sent;
                item.SentAt = now;
            }

            order.Status = order.Items.Any(x => x.Status == OrderItemStatus.New)
                ? OrderStatus.PartiallySent
                : OrderStatus.Sent;
            order.Version++;
            order.UpdatedAt = now;

            db.AuditEvents.Add(Audit(restaurantId, employeeId, "ORDER_SENT_TO_KITCHEN", "Order", order.Id, new
            {
                order.Id,
                order.Version,
                ticketIds,
                itemIds = newItems.Select(x => x.Id).ToArray()
            }));
            db.OutboxEvents.Add(Outbox(restaurantId, "ORDER_SENT_TO_KITCHEN", "Order", order.Id, new
            {
                order.Id,
                order.Version,
                ticketIds
            }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("orders.write");

        group.MapPost("/{id:guid}/close", async (Guid id, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var order = await db.Orders
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });

            if (order.Status == OrderStatus.Closed)
                return Results.Ok(ToDto(order));

            if (order.Status != OrderStatus.Paid)
                return Results.Conflict(new { message = "Only a fully paid order can be closed." });

            order.Status = OrderStatus.Closed;
            order.ClosedAt = DateTimeOffset.UtcNow;
            order.UpdatedAt = order.ClosedAt.Value;
            order.Version++;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "ORDER_CLOSED",
                "Order",
                order.Id,
                new
                {
                    order.Id,
                    order.DisplayNumber,
                    order.Total,
                    order.PaidTotal,
                    order.ClosedAt
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CLOSED",
                "Order",
                order.Id,
                new
                {
                    order.Id,
                    order.DisplayNumber,
                    order.ClosedAt
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToDto(order));
        }).RequireAuthorization("payments.write");

        group.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var order = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Modifiers).FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (order is null) return Results.NotFound(new { message = "Order not found." });
            var line = order.Items.FirstOrDefault(x => x.Id == itemId);
            if (line is null) return Results.NotFound(new { message = "Order item not found." });
            if (order.Status is OrderStatus.Closed or OrderStatus.Cancelled or OrderStatus.Paid or OrderStatus.PartiallyPaid)
                return Results.Conflict(new { message = $"Order cannot be edited in status {order.Status}." });
            if (line.Status != OrderItemStatus.New)
                return Results.Conflict(new { message = "Sent items cannot be deleted; they require an explicit void operation." });

            db.OrderItems.Remove(line);
            order.Items.Remove(line);
            Recalculate(order);
            RefreshOrderStatus(order);
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

    private static bool CanEditOrder(OrderStatus status) =>
        status is not (OrderStatus.Closed or OrderStatus.Cancelled or OrderStatus.Paid or OrderStatus.PartiallyPaid);

    private static void RefreshOrderStatus(Order order)
    {
        if (!CanEditOrder(order.Status))
            return;

        var active = order.Items
            .Where(x => x.Status != OrderItemStatus.Voided)
            .ToArray();

        var hasNew = active.Any(x => x.Status == OrderItemStatus.New);
        var hasSent = active.Any(x => x.Status == OrderItemStatus.Sent);

        order.Status = (hasNew, hasSent) switch
        {
            (true, true) => OrderStatus.PartiallySent,
            (false, true) => OrderStatus.Sent,
            _ => OrderStatus.Open
        };
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    internal static object ToDto(Order order) => new
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
        order.ClosedAt,
        payments = order.Payments
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
        {
            x.Id,
            x.ShiftId,
            x.EmployeeId,
            method = EnumText(x.Method),
            status = EnumText(x.Status),
            x.Amount,
            x.TenderedAmount,
            x.ChangeAmount,
            x.CurrencyCode,
            x.ProviderReference,
            x.CreatedAt
        }),
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
            x.Comment,
            x.SentAt,
            x.VoidedAt,
            modifiers = x.Modifiers
                .Select(modifier => new
                {
                    modifier.Id,
                    modifier.ModifierId,
                    name = modifier.ModifierNameSnapshot,
                    modifier.Quantity,
                    modifier.PriceDelta,
                    modifier.Total
                })
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
public sealed record ModifierSelectionRequest(
    Guid GroupId,
    Guid ModifierId,
    decimal Quantity = 1m);
public sealed record AddOrderItemRequest(
    Guid ProductId,
    decimal Quantity = 1m,
    string? Comment = null,
    ModifierSelectionRequest[]? Modifiers = null);
public sealed record UpdateGuestCountRequest(int GuestCount);
public sealed record MoveOrderRequest(Guid TableId);
public sealed record TransferOrderItemsRequest(Guid TargetTableId, Guid[]? ItemIds);
public sealed record UpdateOrderItemCommentRequest(string? Comment);
public sealed record UpdateOrderItemModifiersRequest(ModifierSelectionRequest[]? Modifiers);
public sealed record VoidOrderItemRequest(string? Reason);
