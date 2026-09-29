using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Shifts;

public static class ShiftEndpoints
{
    public static IEndpointRouteBuilder MapShiftEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shifts").RequireAuthorization();

        group.MapGet("/current", async (
            Guid deviceId,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var deviceExists = await db.Devices
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == deviceId &&
                    x.RestaurantId == restaurantId &&
                    x.Type == DeviceType.Pos &&
                    x.IsActive,
                    ct);

            if (!deviceExists)
                return Results.BadRequest(new { message = "POS device is not available in this restaurant." });

            var shift = await db.Shifts
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.DeviceId == deviceId &&
                    x.Status == ShiftStatus.Open)
                .OrderByDescending(x => x.OpenedAt)
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                shift = shift is null ? null : ToDto(shift)
            });
        }).RequireAuthorization("shifts.manage");

        group.MapGet("/{id:guid}/report", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var shift = await db.Shifts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.RestaurantId == restaurantId,
                    ct);

            if (shift is null)
                return Results.NotFound(new { message = "Shift not found." });

            var report = await BuildReportAsync(db, restaurantId, shift, ct);
            return Results.Ok(report);
        }).RequireAuthorization("shifts.manage");

        group.MapPost("/open", async (
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

            var device = await db.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.DeviceId &&
                    x.RestaurantId == restaurantId &&
                    x.Type == DeviceType.Pos &&
                    x.IsActive,
                    ct);

            if (device is null)
                return Results.BadRequest(new { message = "POS device is not available in this restaurant." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var existing = await db.Shifts
                .FirstOrDefaultAsync(x =>
                    x.RestaurantId == restaurantId &&
                    x.DeviceId == request.DeviceId &&
                    x.Status == ShiftStatus.Open,
                    ct);

            if (existing is not null)
                return Results.Conflict(new
                {
                    message = "This POS already has an open shift.",
                    shift = ToDto(existing)
                });

            var shift = new Shift
            {
                RestaurantId = restaurantId,
                DeviceId = request.DeviceId,
                OpenedByEmployeeId = employeeId,
                OpeningCash = Money(request.OpeningCash),
                Status = ShiftStatus.Open,
                OpenedAt = DateTimeOffset.UtcNow
            };

            db.Shifts.Add(shift);
            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                request.DeviceId,
                "SHIFT_OPENED",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.OpeningCash,
                    device.Name
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "SHIFT_OPENED",
                "Shift",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.OpeningCash,
                    shift.OpenedAt
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/v1/shifts/{shift.Id}", ToDto(shift));
        }).RequireAuthorization("shifts.manage");

        group.MapPost("/{id:guid}/cash-transactions", async (
            Guid id,
            CashTransactionRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.Amount <= 0)
                return Results.BadRequest(new { message = "Cash transaction amount must be greater than zero." });

            if (!Enum.TryParse<CashTransactionType>(request.Type, true, out var type) ||
                !Enum.IsDefined(type))
                return Results.BadRequest(new { message = "Cash transaction type must be DEPOSIT or WITHDRAWAL." });

            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                return Results.BadRequest(new { message = "Cash transaction reason is required." });
            if (reason.Length > 500)
                reason = reason[..500];

            var shift = await db.Shifts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.RestaurantId == restaurantId &&
                    x.Status == ShiftStatus.Open,
                    ct);

            if (shift is null)
                return Results.Conflict(new { message = "An open shift is required." });

            var entry = new CashTransaction
            {
                RestaurantId = restaurantId,
                ShiftId = shift.Id,
                EmployeeId = employeeId,
                Type = type,
                Amount = Money(request.Amount),
                Reason = reason,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.CashTransactions.Add(entry);

            await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);
            var cashMoneyAccount = await AccountingLedger.EnsurePaymentMoneyAccountAsync(
                db, restaurantId, PaymentMethod.Cash, shift.DeviceId, ct);
            var cashLedgerAccount = await AccountingLedger.EnsureMoneyAccountAsync(
                db, restaurantId, cashMoneyAccount, ct);
            var cashClearingAccount = await AccountingLedger.EnsureSystemAccountAsync(
                db, restaurantId, AccountingLedger.CashClearingKey,
                "3.90", "Кассовые внесения и изъятия", LedgerAccountType.Equity, ct);

            var cashLedgerLines = type == CashTransactionType.Deposit
                ? new[]
                {
                    new AccountingLedger.LineDraft(
                        cashLedgerAccount,
                        Debit: entry.Amount,
                        MoneyAccountId: cashMoneyAccount.Id),
                    new AccountingLedger.LineDraft(
                        cashClearingAccount,
                        Credit: entry.Amount)
                }
                : new[]
                {
                    new AccountingLedger.LineDraft(
                        cashClearingAccount,
                        Debit: entry.Amount),
                    new AccountingLedger.LineDraft(
                        cashLedgerAccount,
                        Credit: entry.Amount,
                        MoneyAccountId: cashMoneyAccount.Id)
                };

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "SHIFT_CASH_TRANSACTION",
                entry.Id,
                entry.CreatedAt,
                type == CashTransactionType.Deposit
                    ? "Внесение в кассу"
                    : "Изъятие из кассы",
                employeeId,
                cashLedgerLines,
                ct);

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                shift.DeviceId,
                type == CashTransactionType.Deposit ? "CASH_DEPOSIT" : "CASH_WITHDRAWAL",
                entry.Id,
                new
                {
                    entry.Id,
                    shiftId = shift.Id,
                    type = EnumText(type),
                    entry.Amount,
                    entry.Reason
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                type == CashTransactionType.Deposit
                    ? "CASH_DEPOSIT"
                    : "CASH_WITHDRAWAL",
                "CashTransaction",
                entry.Id,
                new
                {
                    entry.Id,
                    shiftId = shift.Id,
                    shift.DeviceId,
                    type = EnumText(type),
                    entry.Amount,
                    entry.Reason,
                    entry.CreatedAt
                }));

            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/shifts/{shift.Id}/cash-transactions/{entry.Id}", new
            {
                entry.Id,
                entry.ShiftId,
                type = EnumText(entry.Type),
                entry.Amount,
                entry.Reason,
                entry.CreatedAt
            });
        }).RequireAuthorization("shifts.manage");

        group.MapPost("/{id:guid}/close", async (
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

            var shift = await db.Shifts
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.RestaurantId == restaurantId,
                    ct);

            if (shift is null)
                return Results.NotFound(new { message = "Shift not found." });

            if (shift.Status != ShiftStatus.Open)
                return Results.Conflict(new { message = "Shift is already closed." });

            var pendingPayments = await db.Payments
                .AsNoTracking()
                .AnyAsync(x =>
                    x.RestaurantId == restaurantId &&
                    x.ShiftId == shift.Id &&
                    x.Status == PaymentStatus.Pending,
                    ct);

            if (pendingPayments)
                return Results.Conflict(new { message = "Shift has pending payments and cannot be closed." });

            var report = await BuildReportAsync(db, restaurantId, shift, ct);
            var closingCash = Money(request.ClosingCash);
            var difference = Money(closingCash - report.ExpectedCash);
            var closingNote = NormalizeOptional(request.Reason, 500);

            if (Math.Abs(difference) >= 0.01m && closingNote is null)
            {
                return Results.BadRequest(new
                {
                    code = "SHIFT_DIFFERENCE_REASON_REQUIRED",
                    message = "При расхождении наличности обязательно укажите причину.",
                    expectedCash = report.ExpectedCash,
                    closingCash,
                    difference
                });
            }

            shift.ClosingCash = closingCash;
            shift.ExpectedCashAtClose = report.ExpectedCash;
            shift.CashDifference = difference;
            shift.ClosingNote = closingNote;
            shift.ClosedByEmployeeId = employeeId;
            shift.ClosedAt = DateTimeOffset.UtcNow;
            shift.Status = ShiftStatus.Closed;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                shift.DeviceId,
                "SHIFT_CLOSED",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.OpeningCash,
                    closingCash,
                    report.ExpectedCash,
                    difference,
                    closingNote,
                    report.GrossSales,
                    report.Refunds,
                    report.NetSales,
                    report.OrdersCount,
                    report.Payments
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
                    shift.ClosedAt,
                    closingCash,
                    report.ExpectedCash,
                    difference,
                    closingNote
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var closedByEmployeeName = await db.Employees
                .AsNoTracking()
                .Where(x =>
                    x.Id == employeeId &&
                    x.RestaurantId == restaurantId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);

            var closedReport = report with
            {
                ReportType = "Z",
                Status = EnumText(shift.Status),
                ClosedByEmployeeName = closedByEmployeeName,
                ClosedAt = shift.ClosedAt,
                ClosingCash = shift.ClosingCash,
                CashDifference = difference,
                ClosingNote = closingNote
            };

            return Results.Ok(new
            {
                shift = ToDto(shift),
                expectedCash = closedReport.ExpectedCash,
                difference = closedReport.CashDifference,
                report = closedReport
            });
        }).RequireAuthorization("shifts.manage");

        return app;
    }

    internal static async Task<ShiftReportDto> BuildReportAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Shift shift,
        CancellationToken ct)
    {
        var shiftInfo = await (
            from device in db.Devices.AsNoTracking()
            where device.Id == shift.DeviceId &&
                  device.RestaurantId == restaurantId
            select device.Name)
            .FirstOrDefaultAsync(ct);

        var employeeIds = new[]
        {
            shift.OpenedByEmployeeId,
            shift.ClosedByEmployeeId ?? Guid.Empty
        }
        .Where(x => x != Guid.Empty)
        .Distinct()
        .ToArray();

        var employeeNames = await db.Employees
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                employeeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var payments = await db.Payments
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.ShiftId == shift.Id &&
                (x.Status == PaymentStatus.Completed || x.Status == PaymentStatus.Refunded))
            .ToListAsync(ct);

        var refunds = await (
            from refund in db.PaymentRefunds.AsNoTracking()
            join payment in db.Payments.AsNoTracking() on refund.PaymentId equals payment.Id
            where refund.RestaurantId == restaurantId &&
                  refund.ShiftId == shift.Id
            select new
            {
                refund.Amount,
                payment.Method
            })
            .ToListAsync(ct);

        var cashEntries = await db.CashTransactions
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.ShiftId == shift.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        var cashEmployeeIds = cashEntries
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToArray();

        var cashEmployeeNames = await db.Employees
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                cashEmployeeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var methodRows = Enum.GetValues<PaymentMethod>()
            .Select(method =>
            {
                var gross = payments
                    .Where(x => x.Method == method)
                    .Sum(x => x.Amount);
                var refunded = refunds
                    .Where(x => x.Method == method)
                    .Sum(x => x.Amount);
                return new ShiftPaymentTotalDto(
                    EnumText(method),
                    Money(gross),
                    Money(refunded),
                    Money(gross - refunded));
            })
            .Where(x => x.Gross > 0 || x.Refunds > 0)
            .ToArray();

        var grossSales = Money(payments.Sum(x => x.Amount));
        var refundedTotal = Money(refunds.Sum(x => x.Amount));
        var netSales = Money(grossSales - refundedTotal);

        var cashGross = payments
            .Where(x => x.Method == PaymentMethod.Cash)
            .Sum(x => x.Amount);
        var cashRefunds = refunds
            .Where(x => x.Method == PaymentMethod.Cash)
            .Sum(x => x.Amount);
        var deposits = cashEntries
            .Where(x => x.Type == CashTransactionType.Deposit)
            .Sum(x => x.Amount);
        var withdrawals = cashEntries
            .Where(x => x.Type == CashTransactionType.Withdrawal)
            .Sum(x => x.Amount);

        var calculatedExpectedCash = Money(
            shift.OpeningCash +
            cashGross -
            cashRefunds +
            deposits -
            withdrawals);

        var expectedCash =
            shift.Status == ShiftStatus.Closed &&
            shift.ExpectedCashAtClose.HasValue
                ? shift.ExpectedCashAtClose.Value
                : calculatedExpectedCash;

        var cashDifference =
            shift.Status == ShiftStatus.Closed
                ? shift.CashDifference
                : shift.ClosingCash.HasValue
                    ? Money(shift.ClosingCash.Value - expectedCash)
                    : null;

        var cashTransactions = cashEntries
            .Select(x => new ShiftCashTransactionDto(
                x.Id,
                EnumText(x.Type),
                x.Amount,
                x.Reason,
                x.EmployeeId,
                cashEmployeeNames.GetValueOrDefault(x.EmployeeId),
                x.CreatedAt))
            .ToArray();

        return new ShiftReportDto(
            shift.Id,
            shift.DeviceId,
            shiftInfo,
            employeeNames.GetValueOrDefault(shift.OpenedByEmployeeId),
            shift.ClosedByEmployeeId.HasValue
                ? employeeNames.GetValueOrDefault(shift.ClosedByEmployeeId.Value)
                : null,
            shift.Status == ShiftStatus.Open ? "X" : "Z",
            EnumText(shift.Status),
            shift.OpenedAt,
            shift.ClosedAt,
            shift.OpeningCash,
            shift.ClosingCash,
            expectedCash,
            cashDifference,
            shift.ClosingNote,
            payments.Select(x => x.OrderId).Distinct().Count(),
            payments.Count,
            grossSales,
            refundedTotal,
            netSales,
            Money(cashGross),
            Money(cashRefunds),
            Money(deposits),
            Money(withdrawals),
            methodRows,
            cashTransactions);
    }

    private static object ToDto(Shift shift) => new
    {
        shift.Id,
        shift.RestaurantId,
        shift.DeviceId,
        shift.OpenedByEmployeeId,
        shift.ClosedByEmployeeId,
        status = EnumText(shift.Status),
        shift.OpeningCash,
        shift.ClosingCash,
        shift.ExpectedCashAtClose,
        shift.CashDifference,
        shift.ClosingNote,
        shift.OpenedAt,
        shift.ClosedAt
    };

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum =>
        value.ToString().ToUpperInvariant();

    private static string? NormalizeOptional(
        string? value,
        int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
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
        Guid deviceId,
        string eventType,
        Guid entityId,
        object payload) => new()
    {
        RestaurantId = restaurantId,
        EmployeeId = employeeId,
        DeviceId = deviceId,
        EventType = eventType,
        EntityType = "Shift",
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

public sealed record OpenShiftRequest(Guid DeviceId, decimal OpeningCash = 0m);
public sealed record CloseShiftRequest(
    decimal ClosingCash,
    string? Reason = null);
public sealed record CashTransactionRequest(string Type, decimal Amount, string? Reason);

public sealed record ShiftPaymentTotalDto(
    string Method,
    decimal Gross,
    decimal Refunds,
    decimal Net);

public sealed record ShiftCashTransactionDto(
    Guid Id,
    string Type,
    decimal Amount,
    string? Reason,
    Guid EmployeeId,
    string? EmployeeName,
    DateTimeOffset CreatedAt);

public sealed record ShiftReportDto(
    Guid ShiftId,
    Guid DeviceId,
    string? DeviceName,
    string? OpenedByEmployeeName,
    string? ClosedByEmployeeName,
    string ReportType,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal OpeningCash,
    decimal? ClosingCash,
    decimal ExpectedCash,
    decimal? CashDifference,
    string? ClosingNote,
    int OrdersCount,
    int PaymentsCount,
    decimal GrossSales,
    decimal Refunds,
    decimal NetSales,
    decimal CashSales,
    decimal CashRefunds,
    decimal Deposits,
    decimal Withdrawals,
    IReadOnlyList<ShiftPaymentTotalDto> Payments,
    IReadOnlyList<ShiftCashTransactionDto> CashTransactions);
