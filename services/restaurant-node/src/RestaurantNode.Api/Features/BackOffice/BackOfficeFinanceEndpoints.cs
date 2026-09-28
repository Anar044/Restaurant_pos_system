using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Features.Shifts;
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
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? deviceIds,
            int? take,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var todayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var periodFrom = from ?? todayUtc;
            var periodTo = to ?? periodFrom.AddDays(1);

            if (periodTo <= periodFrom)
                return Results.BadRequest(new { message = "Period end must be after period start." });

            if (periodTo - periodFrom > TimeSpan.FromDays(366))
                return Results.BadRequest(new { message = "Finance period cannot exceed 366 days." });

            var limit = Math.Clamp(take ?? 200, 20, 500);

            var cashRegisters = await db.Devices
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == DeviceType.Pos)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    x.Name,
                    x.IsActive
                })
                .ToListAsync(ct);

            var selectedDeviceIds = ParseDeviceIds(deviceIds);
            if (selectedDeviceIds is null)
                return Results.BadRequest(new { message = "One or more cash register IDs are invalid." });

            if (selectedDeviceIds.Count > 0)
            {
                var knownDeviceIds = cashRegisters.Select(x => x.id).ToHashSet();
                if (selectedDeviceIds.Any(x => !knownDeviceIds.Contains(x)))
                    return Results.BadRequest(new { message = "One or more cash registers do not belong to this restaurant." });
            }

            Guid[] shiftsForSelectedDevices = [];
            if (selectedDeviceIds.Count > 0)
            {
                shiftsForSelectedDevices = await db.Shifts
                    .AsNoTracking()
                    .Where(x =>
                        x.RestaurantId == restaurantId &&
                        selectedDeviceIds.Contains(x.DeviceId))
                    .Select(x => x.Id)
                    .ToArrayAsync(ct);
            }

            var orderQuery = db.Orders
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.CreatedAt >= periodFrom &&
                    x.CreatedAt < periodTo &&
                    x.Status != OrderStatus.Cancelled);

            if (selectedDeviceIds.Count > 0)
            {
                orderQuery = orderQuery.Where(x =>
                    (x.OriginDeviceId.HasValue &&
                     selectedDeviceIds.Contains(x.OriginDeviceId.Value)) ||
                    (!x.OriginDeviceId.HasValue &&
                     x.Payments.Any(payment =>
                         shiftsForSelectedDevices.Contains(payment.ShiftId))));
            }

            var orderSummaryRows = await orderQuery
                .Select(x => new
                {
                    x.Status,
                    x.Total
                })
                .ToListAsync(ct);

            var completedStatuses = new HashSet<OrderStatus>
            {
                OrderStatus.Paid,
                OrderStatus.Closed
            };

            var completedOrderRows = orderSummaryRows
                .Where(x => completedStatuses.Contains(x.Status))
                .ToArray();

            var openOrderRows = orderSummaryRows
                .Where(x => !completedStatuses.Contains(x.Status))
                .ToArray();

            var completedOrdersAmount = Money(completedOrderRows.Sum(x => x.Total));
            var openOrdersAmount = Money(openOrderRows.Sum(x => x.Total));
            var expectedRevenue = Money(completedOrdersAmount + openOrdersAmount);

            var refundSummaryQuery = db.PaymentRefunds
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.CreatedAt >= periodFrom &&
                    x.CreatedAt < periodTo);

            if (selectedDeviceIds.Count > 0)
            {
                refundSummaryQuery = refundSummaryQuery
                    .Where(x => shiftsForSelectedDevices.Contains(x.ShiftId));
            }

            var refundAmount = Money(
                await refundSummaryQuery.SumAsync(x => (decimal?)x.Amount, ct) ?? 0m);

            var shiftQuery =
                from shift in db.Shifts.AsNoTracking()
                join device in db.Devices.AsNoTracking()
                    on shift.DeviceId equals device.Id
                where shift.RestaurantId == restaurantId &&
                      shift.OpenedAt < periodTo &&
                      (shift.ClosedAt == null || shift.ClosedAt >= periodFrom)
                select new
                {
                    id = shift.Id,
                    deviceId = shift.DeviceId,
                    deviceName = device.Name,
                    status = shift.Status.ToString().ToUpperInvariant(),
                    shift.OpenedByEmployeeId,
                    shift.ClosedByEmployeeId,
                    shift.OpeningCash,
                    shift.ClosingCash,
                    shift.ExpectedCashAtClose,
                    shift.CashDifference,
                    shift.ClosingNote,
                    shift.OpenedAt,
                    shift.ClosedAt
                };

            if (selectedDeviceIds.Count > 0)
                shiftQuery = shiftQuery.Where(x => selectedDeviceIds.Contains(x.deviceId));

            var shifts = await shiftQuery
                .OrderByDescending(x => x.OpenedAt)
                .Take(300)
                .ToListAsync(ct);

            var shiftIds = shifts.Select(x => x.id).ToArray();

            var shiftEmployeeIds = shifts
                .SelectMany(x => new[]
                {
                    x.OpenedByEmployeeId,
                    x.ClosedByEmployeeId ?? Guid.Empty
                })
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            var shiftEmployeeNames = await db.Employees
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    shiftEmployeeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            var shiftPayments = await db.Payments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    shiftIds.Contains(x.ShiftId))
                .Select(x => new
                {
                    x.ShiftId,
                    x.OrderId,
                    x.Method,
                    x.Amount
                })
                .ToListAsync(ct);

            var shiftRefunds = await (
                from refund in db.PaymentRefunds.AsNoTracking()
                join payment in db.Payments.AsNoTracking()
                    on refund.PaymentId equals payment.Id
                where refund.RestaurantId == restaurantId &&
                      shiftIds.Contains(refund.ShiftId)
                select new
                {
                    refund.ShiftId,
                    refund.OrderId,
                    payment.Method,
                    refund.Amount
                })
                .ToListAsync(ct);

            var shiftCashRows = await db.CashTransactions
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    shiftIds.Contains(x.ShiftId))
                .Select(x => new
                {
                    x.ShiftId,
                    x.Type,
                    x.Amount
                })
                .ToListAsync(ct);

            var originOrderPairs = await db.Orders
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.OpenedShiftId.HasValue &&
                    shiftIds.Contains(x.OpenedShiftId.Value))
                .Select(x => new
                {
                    ShiftId = x.OpenedShiftId!.Value,
                    OrderId = x.Id
                })
                .ToListAsync(ct);

            var orderIdsByShift = shifts.ToDictionary(
                x => x.id,
                _ => new HashSet<Guid>());

            foreach (var pair in originOrderPairs)
                orderIdsByShift[pair.ShiftId].Add(pair.OrderId);

            foreach (var payment in shiftPayments)
                orderIdsByShift[payment.ShiftId].Add(payment.OrderId);

            var shiftRows = shifts.Select(shift =>
            {
                var payments = shiftPayments
                    .Where(x => x.ShiftId == shift.id)
                    .ToArray();
                var refunds = shiftRefunds
                    .Where(x => x.ShiftId == shift.id)
                    .ToArray();
                var cashRows = shiftCashRows
                    .Where(x => x.ShiftId == shift.id)
                    .ToArray();

                var grossSales = Money(payments.Sum(x => x.Amount));
                var refundsTotal = Money(refunds.Sum(x => x.Amount));
                var netSales = Money(grossSales - refundsTotal);
                var cashSales = Money(
                    payments
                        .Where(x => x.Method == PaymentMethod.Cash)
                        .Sum(x => x.Amount));
                var cashRefunds = Money(
                    refunds
                        .Where(x => x.Method == PaymentMethod.Cash)
                        .Sum(x => x.Amount));
                var deposits = Money(
                    cashRows
                        .Where(x => x.Type == CashTransactionType.Deposit)
                        .Sum(x => x.Amount));
                var withdrawals = Money(
                    cashRows
                        .Where(x => x.Type == CashTransactionType.Withdrawal)
                        .Sum(x => x.Amount));
                var calculatedExpectedCash = Money(
                    shift.OpeningCash +
                    cashSales -
                    cashRefunds +
                    deposits -
                    withdrawals);
                var expectedCash =
                    shift.status == "CLOSED" &&
                    shift.ExpectedCashAtClose.HasValue
                        ? shift.ExpectedCashAtClose.Value
                        : calculatedExpectedCash;

                return new
                {
                    shift.id,
                    shift.deviceId,
                    shift.deviceName,
                    shift.status,
                    shift.OpenedByEmployeeId,
                    openedByEmployeeName =
                        shiftEmployeeNames.GetValueOrDefault(
                            shift.OpenedByEmployeeId),
                    shift.ClosedByEmployeeId,
                    closedByEmployeeName =
                        shift.ClosedByEmployeeId.HasValue
                            ? shiftEmployeeNames.GetValueOrDefault(
                                shift.ClosedByEmployeeId.Value)
                            : null,
                    shift.OpeningCash,
                    shift.ClosingCash,
                    expectedCashAtClose = expectedCash,
                    shift.CashDifference,
                    shift.ClosingNote,
                    shift.OpenedAt,
                    shift.ClosedAt,
                    ordersCount = orderIdsByShift[shift.id].Count,
                    grossSales,
                    refunds = refundsTotal,
                    netSales,
                    cashSales,
                    cashRefunds,
                    deposits,
                    withdrawals
                };
            }).ToArray();

            var closedShiftDifference = Money(
                shifts
                    .Where(x => x.status == "CLOSED")
                    .Sum(x => x.CashDifference ?? 0m));

            ShiftReportDto? selectedShiftReport = null;
            object[] selectedShiftOrders = [];
            object[] payments = [];
            object[] refunds = [];

            if (shiftId.HasValue)
            {
                var selectedShift = await db.Shifts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == shiftId.Value &&
                        x.RestaurantId == restaurantId,
                        ct);

                if (selectedShift is null)
                    return Results.NotFound(new { message = "Shift not found." });

                if (selectedDeviceIds.Count > 0 &&
                    !selectedDeviceIds.Contains(selectedShift.DeviceId))
                {
                    return Results.BadRequest(new { message = "Selected shift is outside the cash register filter." });
                }

                selectedShiftReport =
                    await ShiftEndpoints.BuildReportAsync(
                        db,
                        restaurantId,
                        selectedShift,
                        ct);

                var selectedOrders = await db.Orders
                    .AsNoTracking()
                    .Include(x => x.Payments)
                    .Where(x =>
                        x.RestaurantId == restaurantId &&
                        (x.OpenedShiftId == shiftId.Value ||
                         x.Payments.Any(payment =>
                             payment.ShiftId == shiftId.Value)))
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(limit)
                    .ToListAsync(ct);

                var tableIds = selectedOrders
                    .Where(x => x.TableId.HasValue)
                    .Select(x => x.TableId!.Value)
                    .Distinct()
                    .ToArray();

                var tableRows = await (
                    from table in db.DiningTables.AsNoTracking()
                    join hall in db.Halls.AsNoTracking()
                        on table.HallId equals hall.Id
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

                var orderEmployeeIds = selectedOrders
                    .Select(x => x.CreatedByEmployeeId)
                    .Distinct()
                    .ToArray();

                var orderEmployees = await db.Employees
                    .AsNoTracking()
                    .Where(x =>
                        x.RestaurantId == restaurantId &&
                        orderEmployeeIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

                var selectedOrderIds = selectedOrders
                    .Select(x => x.Id)
                    .ToArray();

                var selectedOrderRefunds = await db.PaymentRefunds
                    .AsNoTracking()
                    .Where(x =>
                        x.RestaurantId == restaurantId &&
                        x.ShiftId == shiftId.Value &&
                        selectedOrderIds.Contains(x.OrderId))
                    .GroupBy(x => x.OrderId)
                    .Select(group => new
                    {
                        OrderId = group.Key,
                        Amount = group.Sum(x => x.Amount)
                    })
                    .ToListAsync(ct);

                var refundsByOrder = selectedOrderRefunds
                    .ToDictionary(x => x.OrderId, x => x.Amount);

                selectedShiftOrders = selectedOrders
                    .Select(order =>
                    {
                        tables.TryGetValue(
                            order.TableId ?? Guid.Empty,
                            out var tableInfo);

                        var shiftPaymentRows = order.Payments
                            .Where(x => x.ShiftId == shiftId.Value)
                            .ToArray();

                        var paidInShift = Money(
                            shiftPaymentRows.Sum(x => x.Amount));
                        var refundedInShift = Money(
                            refundsByOrder.GetValueOrDefault(order.Id));

                        return (object)new
                        {
                            id = order.Id,
                            orderNumber = order.DisplayNumber,
                            status = order.Status.ToString().ToUpperInvariant(),
                            order.Total,
                            order.PaidTotal,
                            remaining = Money(
                                Math.Max(0m, order.Total - order.PaidTotal)),
                            paidInShift,
                            refundedInShift,
                            netPaidInShift = Money(
                                paidInShift - refundedInShift),
                            order.GuestCount,
                            tableName = tableInfo?.TableName,
                            hallName = tableInfo?.HallName,
                            employeeName =
                                orderEmployees.GetValueOrDefault(
                                    order.CreatedByEmployeeId),
                            openedInThisShift =
                                order.OpenedShiftId == shiftId.Value,
                            paymentMethods = shiftPaymentRows
                                .GroupBy(x => x.Method)
                                .Select(group => new
                                {
                                    method = group.Key
                                        .ToString()
                                        .ToUpperInvariant(),
                                    amount = Money(
                                        group.Sum(x => x.Amount))
                                })
                                .ToArray(),
                            order.CreatedAt,
                            order.UpdatedAt,
                            order.ClosedAt
                        };
                    })
                    .ToArray();

                var paymentRows = await (
                    from payment in db.Payments.AsNoTracking()
                    join order in db.Orders.AsNoTracking()
                        on payment.OrderId equals order.Id
                    join employee in db.Employees.AsNoTracking()
                        on payment.EmployeeId equals employee.Id
                    where payment.RestaurantId == restaurantId &&
                          payment.ShiftId == shiftId.Value
                    orderby payment.CreatedAt descending
                    select new
                    {
                        Payment = payment,
                        OrderNumber = order.DisplayNumber,
                        OrderStatus = order.Status,
                        EmployeeName = employee.Name
                    })
                    .Take(limit)
                    .ToListAsync(ct);

                var paymentIds = paymentRows
                    .Select(x => x.Payment.Id)
                    .ToArray();

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

                payments = paymentRows
                    .Select(row =>
                    {
                        var refunded =
                            refundedByPayment.GetValueOrDefault(
                                row.Payment.Id);

                        return (object)new
                        {
                            id = row.Payment.Id,
                            orderId = row.Payment.OrderId,
                            orderNumber = row.OrderNumber,
                            orderStatus = row.OrderStatus
                                .ToString()
                                .ToUpperInvariant(),
                            shiftId = row.Payment.ShiftId,
                            employeeId = row.Payment.EmployeeId,
                            employeeName = row.EmployeeName,
                            guestNumber = row.Payment.GuestNumber,
                            method = row.Payment.Method
                                .ToString()
                                .ToUpperInvariant(),
                            status = row.Payment.Status
                                .ToString()
                                .ToUpperInvariant(),
                            row.Payment.Amount,
                            row.Payment.TenderedAmount,
                            row.Payment.ChangeAmount,
                            refundedAmount = Money(refunded),
                            refundableAmount = Money(
                                Math.Max(
                                    0m,
                                    row.Payment.Amount - refunded)),
                            row.Payment.CurrencyCode,
                            row.Payment.ProviderReference,
                            row.Payment.CreatedAt
                        };
                    })
                    .ToArray();

                refunds = await (
                    from refund in db.PaymentRefunds.AsNoTracking()
                    join payment in db.Payments.AsNoTracking()
                        on refund.PaymentId equals payment.Id
                    join order in db.Orders.AsNoTracking()
                        on refund.OrderId equals order.Id
                    join employee in db.Employees.AsNoTracking()
                        on refund.EmployeeId equals employee.Id
                    where refund.RestaurantId == restaurantId &&
                          refund.ShiftId == shiftId.Value
                    orderby refund.CreatedAt descending
                    select (object)new
                    {
                        refund.Id,
                        refund.PaymentId,
                        refund.OrderId,
                        orderNumber = order.DisplayNumber,
                        refund.ShiftId,
                        employeeName = employee.Name,
                        method = payment.Method
                            .ToString()
                            .ToUpperInvariant(),
                        refund.Amount,
                        payment.CurrencyCode,
                        refund.Reason,
                        refund.CreatedAt
                    })
                    .Take(limit)
                    .ToArrayAsync(ct);
            }

            return Results.Ok(new
            {
                period = new
                {
                    from = periodFrom,
                    to = periodTo
                },
                cashRegisters,
                summary = new
                {
                    completedOrdersAmount,
                    completedOrdersCount = completedOrderRows.Length,
                    openOrdersAmount,
                    openOrdersCount = openOrderRows.Length,
                    expectedRevenue,
                    refundAmount,
                    netExpectedRevenue = Money(
                        expectedRevenue - refundAmount),
                    openShiftsCount = shiftRows.Count(x => x.status == "OPEN"),
                    closedShiftsCount = shiftRows.Count(x => x.status == "CLOSED"),
                    cashDifference = closedShiftDifference
                },
                shifts = shiftRows,
                selectedShiftReport,
                selectedShiftOrders,
                payments,
                refunds
            });
        });

        return app;
    }

    private static HashSet<Guid>? ParseDeviceIds(string? raw)
    {
        var result = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var segment in raw.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(segment, out var id))
                return null;

            result.Add(id);
        }

        return result;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(
            value,
            4,
            MidpointRounding.AwayFromZero);

    private static bool TryGetRestaurantId(
        ClaimsPrincipal user,
        out Guid restaurantId) =>
        Guid.TryParse(
            user.FindFirstValue("restaurant_id"),
            out restaurantId);
}
