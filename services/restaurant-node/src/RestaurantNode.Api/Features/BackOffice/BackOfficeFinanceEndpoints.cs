using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeFinanceEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/finance")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            Guid? shiftId,
            int? take,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var limit = Math.Clamp(take ?? 100, 10, 300);

            var shifts = await (
                from shift in db.Shifts.AsNoTracking()
                join device in db.Devices.AsNoTracking()
                    on shift.DeviceId equals device.Id
                where shift.RestaurantId == restaurantId
                orderby shift.OpenedAt descending
                select new
                {
                    id = shift.Id,
                    deviceId = shift.DeviceId,
                    deviceName = device.Name,
                    status = shift.Status.ToString().ToUpperInvariant(),
                    shift.OpeningCash,
                    shift.ClosingCash,
                    shift.OpenedAt,
                    shift.ClosedAt
                })
                .Take(50)
                .ToListAsync(ct);

            var paymentQuery =
                from payment in db.Payments.AsNoTracking()
                join order in db.Orders.AsNoTracking()
                    on payment.OrderId equals order.Id
                join employee in db.Employees.AsNoTracking()
                    on payment.EmployeeId equals employee.Id
                where payment.RestaurantId == restaurantId
                select new
                {
                    Payment = payment,
                    OrderNumber = order.DisplayNumber,
                    OrderStatus = order.Status,
                    EmployeeName = employee.Name
                };

            if (shiftId.HasValue)
            {
                paymentQuery = paymentQuery.Where(x => x.Payment.ShiftId == shiftId.Value);
            }

            var paymentRows = await paymentQuery
                .OrderByDescending(x => x.Payment.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            var paymentIds = paymentRows.Select(x => x.Payment.Id).ToArray();

            var refundTotals = await db.PaymentRefunds
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    paymentIds.Contains(x.PaymentId))
                .GroupBy(x => x.PaymentId)
                .Select(group => new
                {
                    PaymentId = group.Key,
                    Amount = group.Sum(x => x.Amount)
                })
                .ToListAsync(ct);

            var refundedByPayment = refundTotals
                .ToDictionary(x => x.PaymentId, x => x.Amount);

            var refundQuery =
                from refund in db.PaymentRefunds.AsNoTracking()
                join payment in db.Payments.AsNoTracking()
                    on refund.PaymentId equals payment.Id
                join order in db.Orders.AsNoTracking()
                    on refund.OrderId equals order.Id
                join employee in db.Employees.AsNoTracking()
                    on refund.EmployeeId equals employee.Id
                where refund.RestaurantId == restaurantId
                select new
                {
                    refund.Id,
                    refund.PaymentId,
                    refund.OrderId,
                    orderNumber = order.DisplayNumber,
                    refund.ShiftId,
                    employeeName = employee.Name,
                    method = payment.Method.ToString().ToUpperInvariant(),
                    refund.Amount,
                    payment.CurrencyCode,
                    refund.Reason,
                    refund.CreatedAt
                };

            if (shiftId.HasValue)
                refundQuery = refundQuery.Where(x => x.ShiftId == shiftId.Value);

            var refunds = await refundQuery
                .OrderByDescending(x => x.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            var openShifts = shifts
                .Where(x => x.status == "OPEN")
                .Select(x => new
                {
                    x.id,
                    x.deviceId,
                    x.deviceName,
                    openedAt = x.OpenedAt
                })
                .ToArray();

            return Results.Ok(new
            {
                shifts,
                openShifts,
                payments = paymentRows.Select(row =>
                {
                    var refunded = refundedByPayment.GetValueOrDefault(row.Payment.Id);
                    return new
                    {
                        id = row.Payment.Id,
                        orderId = row.Payment.OrderId,
                        orderNumber = row.OrderNumber,
                        orderStatus = row.OrderStatus.ToString().ToUpperInvariant(),
                        shiftId = row.Payment.ShiftId,
                        employeeId = row.Payment.EmployeeId,
                        employeeName = row.EmployeeName,
                        method = row.Payment.Method.ToString().ToUpperInvariant(),
                        status = row.Payment.Status.ToString().ToUpperInvariant(),
                        row.Payment.Amount,
                        row.Payment.TenderedAmount,
                        row.Payment.ChangeAmount,
                        refundedAmount = Money(refunded),
                        refundableAmount = Money(Math.Max(0m, row.Payment.Amount - refunded)),
                        row.Payment.CurrencyCode,
                        row.Payment.ProviderReference,
                        row.Payment.CreatedAt
                    };
                }),
                refunds
            });
        });

        return app;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
}
