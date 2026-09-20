using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Payments;

public static class ShiftPaymentEndpoints
{
    public static IEndpointRouteBuilder MapShiftPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shifts = app.MapGroup("/api/v1/shifts").RequireAuthorization();

        shifts.MapGet("/current", async (
            Guid deviceId,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            if (!await IsActivePosAsync(db, restaurantId, deviceId, ct))
                return Results.BadRequest(new { message = "POS device does not exist or is inactive." });

            var shift = await db.Shifts
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.DeviceId == deviceId &&
                    x.Status == ShiftStatus.Open)
                .OrderByDescending(x => x.OpenedAt)
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new { shift = shift is null ? null : ToShiftDto(shift) });
        }).RequireAuthorization("shifts.manage");

        shifts.MapPost("/open", async (
            OpenShiftRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.DeviceId == Guid.Empty)
                return Results.BadRequest(new { message = "DeviceId is required." });
            if (request.OpeningCash < 0)
                return Results.BadRequest(new { message = "Opening cash cannot be negative." });

            if (!await IsActivePosAsync(db, restaurantId, request.DeviceId, ct))
                return Results.BadRequest(new { message = "POS device does not exist or is inactive." });

            var existing = await db.Shifts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.RestaurantId == restaurantId &&
                    x.DeviceId == request.DeviceId &&
                    x.Status == ShiftStatus.Open, ct);

            if (existing is not null)
                return Results.Conflict(new
                {
                    message = "This POS already has an open shift.",
                    shift = ToShiftDto(existing)
                });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var shift = new Shift
            {
                RestaurantId = restaurantId,
                DeviceId = request.DeviceId,
                OpenedByEmployeeId = employeeId,
                OpeningCash = decimal.Round(request.OpeningCash, 4, MidpointRounding.AwayFromZero),
                Status = ShiftStatus.Open,
                OpenedAt = DateTimeOffset.UtcNow
            };

            db.Shifts.Add(shift);
            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                request.DeviceId,
                "SHIFT_OPENED",
                "Shift",
                shift.Id,
                new { shift.Id, request.DeviceId, shift.OpeningCash, shift.OpenedAt }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "SHIFT_OPENED",
                "Shift",
                shift.Id,
                new { shift.Id, request.DeviceId, shift.OpeningCash, shift.OpenedAt }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/v1/shifts/{shift.Id}", ToShiftDto(shift));
        }).RequireAuthorization("shifts.manage");

        shifts.MapPost("/{id:guid}/close", async (
            Guid id,
            CloseShiftRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.ClosingCash < 0)
                return Results.BadRequest(new { message = "Closing cash cannot be negative." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var shift = await db.Shifts.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.RestaurantId == restaurantId, ct);

            if (shift is null)
                return Results.NotFound(new { message = "Shift not found." });
            if (shift.Status != ShiftStatus.Open)
                return Results.Conflict(new { message = "Shift is already closed." });

            var pendingPayments = await db.Payments.AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.ShiftId == shift.Id &&
                x.Status == PaymentStatus.Pending, ct);
            if (pendingPayments)
                return Results.Conflict(new { message = "Shift has pending payments and cannot be closed." });

            var cashSales = await db.Payments
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.ShiftId == shift.Id &&
                    x.Status == PaymentStatus.Completed &&
                    x.Method == PaymentMethod.Cash)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

            var deposits = await db.CashTransactions
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.ShiftId == shift.Id &&
                    x.Type == CashTransactionType.Deposit)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

            var withdrawals = await db.CashTransactions
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.ShiftId == shift.Id &&
                    x.Type == CashTransactionType.Withdrawal)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

            var expectedCash = decimal.Round(
                shift.OpeningCash + cashSales + deposits - withdrawals,
                4,
                MidpointRounding.AwayFromZero);

            shift.Status = ShiftStatus.Closed;
            shift.ClosedByEmployeeId = employeeId;
            shift.ClosingCash = decimal.Round(request.ClosingCash, 4, MidpointRounding.AwayFromZero);
            shift.ClosedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                shift.DeviceId,
                "SHIFT_CLOSED",
                "Shift",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.OpeningCash,
                    cashSales,
                    deposits,
                    withdrawals,
                    expectedCash,
                    shift.ClosingCash,
                    difference = shift.ClosingCash - expectedCash,
                    shift.ClosedAt
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "SHIFT_CLOSED",
                "Shift",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    expectedCash,
                    shift.ClosingCash,
                    shift.ClosedAt
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                shift = ToShiftDto(shift),
                cashSales,
                deposits,
                withdrawals,
                expectedCash,
                difference = shift.ClosingCash - expectedCash
            });
        }).RequireAuthorization("shifts.manage");

        app.MapPost("/api/v1/orders/{id:guid}/payments", async (
            Guid id,
            CreatePaymentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.ShiftId == Guid.Empty)
                return Results.BadRequest(new { message = "ShiftId is required." });
            if (request.Amount <= 0)
                return Results.BadRequest(new { message = "Payment amount must be greater than zero." });

            if (!Enum.TryParse<PaymentMethod>(request.Method, true, out var method))
                return Results.BadRequest(new { message = "Payment method must be CASH, CARD or OTHER." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var shift = await db.Shifts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.ShiftId &&
                    x.RestaurantId == restaurantId &&
                    x.Status == ShiftStatus.Open, ct);
            if (shift is null)
                return Results.Conflict(new { message = "An open shift is required before payment." });

            var order = await db.Orders
                .Include(x => x.Items)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });
            if (order.Status is OrderStatus.Closed or OrderStatus.Cancelled)
                return Results.Conflict(new { message = $"Order cannot be paid in status {order.Status}." });
            if (order.Status == OrderStatus.Paid || order.PaidTotal >= order.Total)
                return Results.Conflict(new { message = "Order is already fully paid." });
            if (order.Items.Count == 0)
                return Results.Conflict(new { message = "Empty order cannot be paid." });
            if (order.Items.Any(x => x.Status == OrderItemStatus.New))
                return Results.Conflict(new { message = "Send all new items to the kitchen before payment." });

            var completedTotal = order.Payments
                .Where(x => x.Status == PaymentStatus.Completed)
                .Sum(x => x.Amount);
            var remaining = decimal.Round(order.Total - completedTotal, 4, MidpointRounding.AwayFromZero);
            var amount = decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero);

            if (remaining <= 0)
                return Results.Conflict(new { message = "Order has no remaining balance." });
            if (amount > remaining)
                return Results.BadRequest(new
                {
                    message = "Payment amount cannot exceed the remaining balance.",
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
                Method = method,
                Status = PaymentStatus.Completed,
                Amount = amount,
                CurrencyCode = restaurantCurrency,
                ProviderReference = string.IsNullOrWhiteSpace(request.ProviderReference)
                    ? null
                    : request.ProviderReference.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Payments.Add(payment);

            order.PaidTotal = decimal.Round(completedTotal + amount, 4, MidpointRounding.AwayFromZero);
            order.Status = order.PaidTotal >= order.Total
                ? OrderStatus.Paid
                : OrderStatus.PartiallyPaid;
            order.Version++;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                shift.DeviceId,
                "PAYMENT_COMPLETED",
                "Payment",
                payment.Id,
                new
                {
                    payment.Id,
                    orderId = order.Id,
                    order.DisplayNumber,
                    shiftId = shift.Id,
                    method = EnumText(method),
                    payment.Amount,
                    payment.CurrencyCode,
                    order.PaidTotal,
                    remaining = Math.Max(0m, order.Total - order.PaidTotal)
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "PAYMENT_COMPLETED",
                "Order",
                order.Id,
                new
                {
                    order.Id,
                    order.DisplayNumber,
                    paymentId = payment.Id,
                    shiftId = shift.Id,
                    method = EnumText(method),
                    payment.Amount,
                    order.PaidTotal,
                    order.Total
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                payment = ToPaymentDto(payment),
                order = ToOrderDto(order),
                remaining = Math.Max(0m, order.Total - order.PaidTotal)
            });
        }).RequireAuthorization("payments.write");

        app.MapPost("/api/v1/orders/{id:guid}/close", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var order = await db.Orders
                .Include(x => x.Items)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);

            if (order is null)
                return Results.NotFound(new { message = "Order not found." });
            if (order.Status == OrderStatus.Closed)
                return Results.Ok(ToOrderDto(order));
            if (order.Status == OrderStatus.Cancelled)
                return Results.Conflict(new { message = "Cancelled order cannot be closed as paid." });

            var completedTotal = order.Payments
                .Where(x => x.Status == PaymentStatus.Completed)
                .Sum(x => x.Amount);
            order.PaidTotal = decimal.Round(completedTotal, 4, MidpointRounding.AwayFromZero);

            if (order.Total <= 0 || order.PaidTotal < order.Total)
                return Results.Conflict(new
                {
                    message = "Order must be fully paid before it can be closed.",
                    order.Total,
                    order.PaidTotal
                });

            order.Status = OrderStatus.Closed;
            order.ClosedAt = DateTimeOffset.UtcNow;
            order.UpdatedAt = order.ClosedAt.Value;
            order.Version++;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                null,
                "ORDER_CLOSED",
                "Order",
                order.Id,
                new { order.Id, order.DisplayNumber, order.Total, order.PaidTotal, order.ClosedAt }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "ORDER_CLOSED",
                "Order",
                order.Id,
                new { order.Id, order.DisplayNumber, order.Total, order.PaidTotal, order.ClosedAt }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(ToOrderDto(order));
        }).RequireAuthorization("orders.write");

        return app;
    }

    private static async Task<bool> IsActivePosAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid deviceId,
        CancellationToken ct) =>
        await db.Devices.AsNoTracking().AnyAsync(x =>
            x.Id == deviceId &&
            x.RestaurantId == restaurantId &&
            x.Type == DeviceType.Pos &&
            x.IsActive, ct);

    private static object ToShiftDto(Shift shift) => new
    {
        shift.Id,
        shift.RestaurantId,
        shift.DeviceId,
        status = EnumText(shift.Status),
        shift.OpeningCash,
        shift.ClosingCash,
        shift.OpenedAt,
        shift.ClosedAt,
        shift.OpenedByEmployeeId,
        shift.ClosedByEmployeeId
    };

    private static object ToPaymentDto(Payment payment) => new
    {
        payment.Id,
        payment.OrderId,
        payment.ShiftId,
        payment.EmployeeId,
        method = EnumText(payment.Method),
        status = EnumText(payment.Status),
        payment.Amount,
        payment.CurrencyCode,
        payment.ProviderReference,
        payment.CreatedAt
    };

    private static object ToOrderDto(Order order) => new
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
            x.SentAt
        })
    };

    private static AuditEvent Audit(
        Guid restaurantId,
        Guid employeeId,
        Guid? deviceId,
        string eventType,
        string entityType,
        Guid entityId,
        object payload) => new()
    {
        RestaurantId = restaurantId,
        EmployeeId = employeeId,
        DeviceId = deviceId,
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

    private static string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Regex.Replace(value.ToString(), "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant();

    private static bool TryClaims(
        ClaimsPrincipal user,
        out Guid restaurantId,
        out Guid employeeId)
    {
        var okRestaurant = Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
        var okEmployee = Guid.TryParse(user.FindFirstValue("employee_id"), out employeeId);
        return okRestaurant && okEmployee;
    }
}

public sealed record OpenShiftRequest(Guid DeviceId, decimal OpeningCash);
public sealed record CloseShiftRequest(decimal ClosingCash);
public sealed record CreatePaymentRequest(
    Guid ShiftId,
    string Method,
    decimal Amount,
    string? ProviderReference = null);
