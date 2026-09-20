using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Features.Orders;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

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

            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method) ||
                !Enum.IsDefined(method))
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
                .Include(x => x.Items).ThenInclude(x => x.Modifiers)
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

            if (order.Items.Any(x => x.Status == OrderItemStatus.New))
                return Results.Conflict(new { message = "Send all order items to the kitchen before payment." });

            var completedTotal = order.Payments
                .Where(x => x.Status is PaymentStatus.Completed or PaymentStatus.Refunded)
                .Sum(x => x.Amount);

            var remaining = Money(Math.Max(0m, order.Total - completedTotal));
            if (remaining <= 0)
                return Results.Conflict(new { message = "Order is already fully paid." });

            var amount = Money(request.Amount);
            if (amount > remaining)
                return Results.BadRequest(new
                {
                    message = "Payment amount cannot exceed the remaining order balance.",
                    remaining
                });

            decimal? tenderedAmount = null;
            var changeAmount = 0m;

            if (method == PaymentMethod.Cash)
            {
                tenderedAmount = Money(request.TenderedAmount ?? amount);
                if (tenderedAmount < amount)
                    return Results.BadRequest(new
                    {
                        message = "Cash received cannot be less than the payment amount.",
                        amount,
                        tenderedAmount
                    });

                changeAmount = Money(tenderedAmount.Value - amount);
            }
            else if (request.TenderedAmount.HasValue)
            {
                return Results.BadRequest(new
                {
                    message = "TenderedAmount is only valid for CASH payments."
                });
            }

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
                TenderedAmount = tenderedAmount,
                ChangeAmount = changeAmount,
                CurrencyCode = restaurantCurrency,
                ProviderReference = NormalizeOptional(request.ProviderReference, 250),
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Payments.Add(payment);
            if (!order.Payments.Any(x => x.Id == payment.Id))
                order.Payments.Add(payment);

            var newPaidTotal = Money(completedTotal + amount);
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
                    method = EnumText(payment.Method),
                    payment.Amount,
                    payment.TenderedAmount,
                    payment.ChangeAmount,
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
                    method = EnumText(payment.Method),
                    payment.Amount,
                    payment.TenderedAmount,
                    payment.ChangeAmount,
                    payment.CurrencyCode,
                    payment.CreatedAt
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/v1/payments/{payment.Id}", new
            {
                payment = ToDto(payment, 0m),
                order = OrderEndpoints.ToDto(order),
                remaining = Money(Math.Max(0m, order.Total - order.PaidTotal))
            });
        }).RequireAuthorization("payments.write");

        group.MapPost("/{id:guid}/refund", async (
            Guid id,
            RefundPaymentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.ShiftId == Guid.Empty)
                return Results.BadRequest(new { message = "ShiftId is required." });
            if (request.Amount <= 0)
                return Results.BadRequest(new { message = "Refund amount must be greater than zero." });

            var reason = NormalizeOptional(request.Reason, 500);
            if (reason is null)
                return Results.BadRequest(new { message = "Refund reason is required." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var shift = await db.Shifts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.ShiftId &&
                    x.RestaurantId == restaurantId &&
                    x.Status == ShiftStatus.Open,
                    ct);

            if (shift is null)
                return Results.Conflict(new { message = "An open POS shift is required for a refund." });

            var payment = await db.Payments.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.RestaurantId == restaurantId,
                ct);

            if (payment is null)
                return Results.NotFound(new { message = "Payment not found." });

            var orderStatus = await db.Orders
                .AsNoTracking()
                .Where(x =>
                    x.Id == payment.OrderId &&
                    x.RestaurantId == restaurantId)
                .Select(x => (OrderStatus?)x.Status)
                .FirstOrDefaultAsync(ct);

            if (orderStatus is null)
                return Results.NotFound(new { message = "Payment order was not found." });

            if (orderStatus != OrderStatus.Closed)
                return Results.Conflict(new
                {
                    message = "Refunds are allowed only after the order is closed."
                });

            if (payment.Status is not (PaymentStatus.Completed or PaymentStatus.Refunded))
                return Results.Conflict(new { message = $"Payment cannot be refunded in status {payment.Status}." });

            var alreadyRefunded = await db.PaymentRefunds
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.PaymentId == payment.Id)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

            var refundable = Money(Math.Max(0m, payment.Amount - alreadyRefunded));
            var amount = Money(request.Amount);

            if (refundable <= 0)
                return Results.Conflict(new { message = "Payment is already fully refunded." });

            if (amount > refundable)
                return Results.BadRequest(new
                {
                    message = "Refund amount exceeds the refundable balance.",
                    refundable
                });

            var refund = new PaymentRefund
            {
                RestaurantId = restaurantId,
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                ShiftId = shift.Id,
                EmployeeId = employeeId,
                Amount = amount,
                Reason = reason,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.PaymentRefunds.Add(refund);

            var refundedTotal = Money(alreadyRefunded + amount);
            if (refundedTotal >= payment.Amount)
                payment.Status = PaymentStatus.Refunded;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                "PAYMENT_REFUNDED",
                "Payment",
                payment.Id,
                new
                {
                    payment.Id,
                    refundId = refund.Id,
                    payment.OrderId,
                    originalShiftId = payment.ShiftId,
                    refundShiftId = shift.Id,
                    method = EnumText(payment.Method),
                    refund.Amount,
                    refundedTotal,
                    refundableAfter = Money(Math.Max(0m, payment.Amount - refundedTotal)),
                    refund.Reason
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "PAYMENT_REFUNDED",
                "Payment",
                payment.Id,
                new
                {
                    payment.Id,
                    refundId = refund.Id,
                    payment.OrderId,
                    refundShiftId = shift.Id,
                    method = EnumText(payment.Method),
                    refund.Amount,
                    refund.Reason,
                    refund.CreatedAt
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                refund = ToRefundDto(refund, payment.Method, payment.CurrencyCode),
                payment = ToDto(payment, refundedTotal),
                refundable = Money(Math.Max(0m, payment.Amount - refundedTotal))
            });
        }).RequireAuthorization(Permissions.PaymentsRefund);

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

            var paymentIds = payments.Select(x => x.Id).ToArray();
            var refunds = await db.PaymentRefunds
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    paymentIds.Contains(x.PaymentId))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);

            var refundedByPayment = refunds
                .GroupBy(x => x.PaymentId)
                .ToDictionary(x => x.Key, x => x.Sum(r => r.Amount));

            var grossTotal = payments.Sum(x => x.Amount);
            var refundedTotal = refunds.Sum(x => x.Amount);

            return Results.Ok(new
            {
                payments = payments.Select(payment => ToDto(
                    payment,
                    refundedByPayment.GetValueOrDefault(payment.Id))),
                refunds = refunds.Select(refund =>
                {
                    var payment = payments.First(x => x.Id == refund.PaymentId);
                    return ToRefundDto(refund, payment.Method, payment.CurrencyCode);
                }),
                grossTotal = Money(grossTotal),
                refundedTotal = Money(refundedTotal),
                netTotal = Money(grossTotal - refundedTotal)
            });
        }).RequireAuthorization("orders.read");

        return app;
    }

    internal static object ToDto(Payment payment, decimal refundedAmount) => new
    {
        payment.Id,
        payment.OrderId,
        payment.ShiftId,
        payment.EmployeeId,
        method = EnumText(payment.Method),
        status = EnumText(payment.Status),
        payment.Amount,
        payment.TenderedAmount,
        payment.ChangeAmount,
        refundedAmount = Money(refundedAmount),
        refundableAmount = Money(Math.Max(0m, payment.Amount - refundedAmount)),
        payment.CurrencyCode,
        payment.ProviderReference,
        payment.CreatedAt
    };

    internal static object ToRefundDto(
        PaymentRefund refund,
        PaymentMethod method,
        string currencyCode) => new
    {
        refund.Id,
        refund.PaymentId,
        refund.OrderId,
        refund.ShiftId,
        refund.EmployeeId,
        method = EnumText(method),
        refund.Amount,
        currencyCode,
        refund.Reason,
        refund.CreatedAt
    };

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum =>
        value.ToString().ToUpperInvariant();

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

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
    string Method,
    decimal Amount,
    decimal? TenderedAmount = null,
    string? ProviderReference = null);

public sealed record RefundPaymentRequest(
    Guid ShiftId,
    decimal Amount,
    string? Reason);
