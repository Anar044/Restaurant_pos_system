using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeAccountingEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeAccountingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/accounting")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            await AccountingLegacyBackfill.EnsureAsync(db, restaurantId, ct);

            var now = DateTimeOffset.UtcNow;
            var periodTo = to ?? now.AddMinutes(1);
            var periodFrom = from ?? periodTo.AddDays(-30);
            if (periodTo <= periodFrom)
                return Results.BadRequest(new { message = "Period end must be after period start." });
            if (periodTo - periodFrom > TimeSpan.FromDays(366))
                return Results.BadRequest(new { message = "Accounting period cannot exceed 366 days." });

            var accounts = await db.LedgerAccounts
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Code)
                .ToListAsync(ct);

            var accountTotals = await (
                from line in db.LedgerLines.AsNoTracking()
                join entry in db.LedgerEntries.AsNoTracking() on line.EntryId equals entry.Id
                where line.RestaurantId == restaurantId && entry.OccurredAt < periodTo
                group line by line.AccountId into g
                select new
                {
                    AccountId = g.Key,
                    Debit = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .ToDictionaryAsync(x => x.AccountId, ct);

            var periodTotals = await (
                from line in db.LedgerLines.AsNoTracking()
                join entry in db.LedgerEntries.AsNoTracking() on line.EntryId equals entry.Id
                where line.RestaurantId == restaurantId &&
                      entry.OccurredAt >= periodFrom &&
                      entry.OccurredAt < periodTo
                group line by line.AccountId into g
                select new
                {
                    AccountId = g.Key,
                    Debit = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .ToDictionaryAsync(x => x.AccountId, ct);

            var accountLookup = accounts.ToDictionary(x => x.Id);

            var analyticTotals = await (
                from line in db.LedgerLines.AsNoTracking()
                join entry in db.LedgerEntries.AsNoTracking() on line.EntryId equals entry.Id
                where line.RestaurantId == restaurantId &&
                      line.AccountId == accountId &&
                      entry.OccurredAt < periodTo &&
                      (line.SupplierId.HasValue ||
                       line.WarehouseId.HasValue ||
                       line.MoneyAccountId.HasValue)
                group line by new
                {
                    line.SupplierId,
                    line.WarehouseId,
                    line.MoneyAccountId
                }
                into g
                select new
                {
                    g.Key.SupplierId,
                    g.Key.WarehouseId,
                    g.Key.MoneyAccountId,
                    Debit = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .ToListAsync(ct);

            var analyticPeriodTotals = await (
                from line in db.LedgerLines.AsNoTracking()
                join entry in db.LedgerEntries.AsNoTracking() on line.EntryId equals entry.Id
                where line.RestaurantId == restaurantId &&
                      line.AccountId == accountId &&
                      entry.OccurredAt >= periodFrom &&
                      entry.OccurredAt < periodTo &&
                      (line.SupplierId.HasValue ||
                       line.WarehouseId.HasValue ||
                       line.MoneyAccountId.HasValue)
                group line by new
                {
                    line.SupplierId,
                    line.WarehouseId,
                    line.MoneyAccountId
                }
                into g
                select new
                {
                    g.Key.SupplierId,
                    g.Key.WarehouseId,
                    g.Key.MoneyAccountId,
                    Debit = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .ToListAsync(ct);

            var entries = await db.LedgerEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x => x.RestaurantId == restaurantId &&
                            x.OccurredAt >= periodFrom &&
                            x.OccurredAt < periodTo)
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.CreatedAt)
                .Take(300)
                .ToListAsync(ct);

            var suppliers = await db.Suppliers
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var payable = accounts.First(x => x.SystemKey == AccountingLedger.SupplierPayableKey);
            var advance = accounts.First(x => x.SystemKey == AccountingLedger.SupplierAdvanceKey);

            var supplierTotals = await db.LedgerLines
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId &&
                            x.SupplierId.HasValue &&
                            (x.AccountId == payable.Id || x.AccountId == advance.Id))
                .GroupBy(x => new { SupplierId = x.SupplierId!.Value, x.AccountId })
                .Select(g => new
                {
                    g.Key.SupplierId,
                    g.Key.AccountId,
                    Debit = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .ToListAsync(ct);

            var moneyAccounts = await db.MoneyAccounts
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var warehouses = await db.Warehouses
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Name)
                .ToListAsync(ct);

            var supplierLookup = suppliers.ToDictionary(x => x.Id, x => x.Name);
            var warehouseLookup = warehouses.ToDictionary(x => x.Id, x => x.Name);
            var moneyAccountLookup = moneyAccounts.ToDictionary(x => x.Id, x => x.Name);

            return Results.Ok(new
            {
                chartProfile = "AZ_KOS_IFRS_SME",
                period = new { from = periodFrom, to = periodTo },
                accounts = accounts.Select(account =>
                {
                    var total = accountTotals.GetValueOrDefault(account.Id);
                    var period = periodTotals.GetValueOrDefault(account.Id);
                    return new
                    {
                        id = account.Id,
                        account.Code,
                        account.Name,
                        type = EnumText(account.Type),
                        account.SystemKey,
                        account.IsSystem,
                        account.IsActive,
                        debit = Money(total?.Debit ?? 0m),
                        credit = Money(total?.Credit ?? 0m),
                        balance = AccountingLedger.NaturalBalance(
                            account.Type,
                            total?.Debit ?? 0m,
                            total?.Credit ?? 0m),
                        periodDebit = Money(period?.Debit ?? 0m),
                        periodCredit = Money(period?.Credit ?? 0m)
                    };
                }),
                entries = entries.Select(entry => new
                {
                    id = entry.Id,
                    entry.OccurredAt,
                    entry.ReferenceType,
                    entry.ReferenceId,
                    entry.Description,
                    entry.EmployeeId,
                    entry.CreatedAt,
                    lines = entry.Lines
                        .OrderBy(x => x.Id)
                        .Select(line =>
                        {
                            accountLookup.TryGetValue(line.AccountId, out var account);
                            return new
                            {
                                id = line.Id,
                                line.AccountId,
                                accountCode = account?.Code ?? "—",
                                accountName = account?.Name ?? "Счёт",
                                line.Debit,
                                line.Credit,
                                line.SupplierId,
                                supplierName = line.SupplierId.HasValue
                                    ? supplierLookup.GetValueOrDefault(line.SupplierId.Value)
                                    : null,
                                line.WarehouseId,
                                warehouseName = line.WarehouseId.HasValue
                                    ? warehouseLookup.GetValueOrDefault(line.WarehouseId.Value)
                                    : null,
                                line.MoneyAccountId,
                                moneyAccountName = line.MoneyAccountId.HasValue
                                    ? moneyAccountLookup.GetValueOrDefault(line.MoneyAccountId.Value)
                                    : null
                            };
                        })
                }),
                suppliers = suppliers.Select(supplier =>
                {
                    var payableRow = supplierTotals.FirstOrDefault(x =>
                        x.SupplierId == supplier.Id && x.AccountId == payable.Id);
                    var advanceRow = supplierTotals.FirstOrDefault(x =>
                        x.SupplierId == supplier.Id && x.AccountId == advance.Id);
                    var payableBalance = Money((payableRow?.Credit ?? 0m) - (payableRow?.Debit ?? 0m));
                    var advanceBalance = Money((advanceRow?.Debit ?? 0m) - (advanceRow?.Credit ?? 0m));
                    return new
                    {
                        id = supplier.Id,
                        supplier.Name,
                        payable = payableBalance,
                        advance = advanceBalance,
                        balance = Money(payableBalance - advanceBalance)
                    };
                }),
                moneyAccounts = moneyAccounts.Select(account => new
                {
                    id = account.Id,
                    account.Name,
                    type = EnumText(account.Type),
                    account.IsActive
                })
            });
        });

        group.MapGet("/accounts/{accountId:guid}/movements", async (
            Guid accountId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            await AccountingLegacyBackfill.EnsureAsync(db, restaurantId, ct);

            var now = DateTimeOffset.UtcNow;
            var periodTo = to ?? now.AddMinutes(1);
            var periodFrom = from ?? periodTo.AddDays(-30);
            if (periodTo <= periodFrom)
                return Results.BadRequest(new { message = "Period end must be after period start." });
            if (periodTo - periodFrom > TimeSpan.FromDays(366))
                return Results.BadRequest(new { message = "Accounting period cannot exceed 366 days." });

            var account = await db.LedgerAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == accountId &&
                    x.RestaurantId == restaurantId,
                    ct);

            if (account is null)
                return Results.NotFound(new { message = "Счёт не найден." });

            var openingTotals = await (
                from line in db.LedgerLines.AsNoTracking()
                join entry in db.LedgerEntries.AsNoTracking() on line.EntryId equals entry.Id
                where line.RestaurantId == restaurantId &&
                      line.AccountId == accountId &&
                      entry.OccurredAt < periodFrom
                group line by line.AccountId into g
                select new
                {
                    Debit = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .FirstOrDefaultAsync(ct);

            var entries = await db.LedgerEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.OccurredAt >= periodFrom &&
                    x.OccurredAt < periodTo &&
                    x.Lines.Any(line => line.AccountId == accountId))
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.CreatedAt)
                .Take(1000)
                .ToListAsync(ct);

            var accountIds = entries
                .SelectMany(x => x.Lines)
                .Select(x => x.AccountId)
                .Append(accountId)
                .Distinct()
                .ToArray();

            var accountLookup = await db.LedgerAccounts
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && accountIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            var supplierIds = entries
                .SelectMany(x => x.Lines)
                .Where(x => x.SupplierId.HasValue)
                .Select(x => x.SupplierId!.Value)
                .Concat(analyticTotals.Where(x => x.SupplierId.HasValue).Select(x => x.SupplierId!.Value))
                .Distinct()
                .ToArray();
            var warehouseIds = entries
                .SelectMany(x => x.Lines)
                .Where(x => x.WarehouseId.HasValue)
                .Select(x => x.WarehouseId!.Value)
                .Concat(analyticTotals.Where(x => x.WarehouseId.HasValue).Select(x => x.WarehouseId!.Value))
                .Distinct()
                .ToArray();
            var moneyAccountIds = entries
                .SelectMany(x => x.Lines)
                .Where(x => x.MoneyAccountId.HasValue)
                .Select(x => x.MoneyAccountId!.Value)
                .Concat(analyticTotals.Where(x => x.MoneyAccountId.HasValue).Select(x => x.MoneyAccountId!.Value))
                .Distinct()
                .ToArray();

            var supplierLookup = await db.Suppliers
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && supplierIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var warehouseLookup = await db.Warehouses
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && warehouseIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var moneyAccountLookup = await db.MoneyAccounts
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && moneyAccountIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            string? Analytics(LedgerLine line)
            {
                var parts = new List<string>(3);
                if (line.WarehouseId.HasValue &&
                    warehouseLookup.TryGetValue(line.WarehouseId.Value, out var warehouseName))
                    parts.Add("Склад: " + warehouseName);
                if (line.SupplierId.HasValue &&
                    supplierLookup.TryGetValue(line.SupplierId.Value, out var supplierName))
                    parts.Add("Поставщик: " + supplierName);
                if (line.MoneyAccountId.HasValue &&
                    moneyAccountLookup.TryGetValue(line.MoneyAccountId.Value, out var moneyName))
                    parts.Add("Деньги: " + moneyName);
                return parts.Count == 0 ? null : string.Join(" · ", parts);
            }

            var openingDebit = openingTotals?.Debit ?? 0m;
            var openingCredit = openingTotals?.Credit ?? 0m;
            var openingBalance = AccountingLedger.NaturalBalance(
                account.Type, openingDebit, openingCredit);

            var runningBalance = openingBalance;
            var movements = new List<object>();
            decimal periodDebit = 0m;
            decimal periodCredit = 0m;

            foreach (var entry in entries)
            {
                var targetLines = entry.Lines
                    .Where(x => x.AccountId == accountId)
                    .OrderBy(x => x.Id)
                    .ToList();

                foreach (var line in targetLines)
                {
                    periodDebit += line.Debit;
                    periodCredit += line.Credit;

                    runningBalance = AccountingLedger.NaturalBalance(
                        account.Type,
                        account.Type is LedgerAccountType.Asset or LedgerAccountType.Expense
                            ? runningBalance + line.Debit
                            : line.Debit,
                        account.Type is LedgerAccountType.Asset or LedgerAccountType.Expense
                            ? line.Credit
                            : runningBalance + line.Credit);

                    var correspondents = entry.Lines
                        .Where(x => x.Id != line.Id)
                        .OrderBy(x => x.Id)
                        .Select(other =>
                        {
                            accountLookup.TryGetValue(other.AccountId, out var otherAccount);
                            return new
                            {
                                id = other.Id,
                                accountId = other.AccountId,
                                accountCode = otherAccount?.Code ?? "—",
                                accountName = otherAccount?.Name ?? "Счёт",
                                debit = Money(other.Debit),
                                credit = Money(other.Credit),
                                analytics = Analytics(other)
                            };
                        })
                        .ToArray();

                    movements.Add(new
                    {
                        id = line.Id,
                        entryId = entry.Id,
                        entry.OccurredAt,
                        entry.ReferenceType,
                        entry.ReferenceId,
                        entry.Description,
                        debit = Money(line.Debit),
                        credit = Money(line.Credit),
                        balanceAfter = Money(runningBalance),
                        analytics = Analytics(line),
                        correspondents
                    });
                }
            }

            var analytics = analyticTotals
                .Select(total =>
                {
                    var period = analyticPeriodTotals.FirstOrDefault(x =>
                        x.SupplierId == total.SupplierId &&
                        x.WarehouseId == total.WarehouseId &&
                        x.MoneyAccountId == total.MoneyAccountId);

                    var periodDebitValue = period?.Debit ?? 0m;
                    var periodCreditValue = period?.Credit ?? 0m;
                    var openingDebitValue = total.Debit - periodDebitValue;
                    var openingCreditValue = total.Credit - periodCreditValue;

                    string kind;
                    Guid id;
                    string name;

                    if (total.WarehouseId.HasValue)
                    {
                        kind = "WAREHOUSE";
                        id = total.WarehouseId.Value;
                        name = warehouseLookup.GetValueOrDefault(id) ?? "Склад";
                    }
                    else if (total.SupplierId.HasValue)
                    {
                        kind = "SUPPLIER";
                        id = total.SupplierId.Value;
                        name = supplierLookup.GetValueOrDefault(id) ?? "Поставщик";
                    }
                    else
                    {
                        kind = "MONEY_ACCOUNT";
                        id = total.MoneyAccountId!.Value;
                        name = moneyAccountLookup.GetValueOrDefault(id) ?? "Денежный счёт";
                    }

                    return new
                    {
                        kind,
                        id,
                        name,
                        openingBalance = AccountingLedger.NaturalBalance(
                            account.Type, openingDebitValue, openingCreditValue),
                        periodDebit = Money(periodDebitValue),
                        periodCredit = Money(periodCreditValue),
                        closingBalance = AccountingLedger.NaturalBalance(
                            account.Type, total.Debit, total.Credit)
                    };
                })
                .OrderBy(x => x.kind)
                .ThenBy(x => x.name)
                .ToArray();

            return Results.Ok(new
            {
                period = new { from = periodFrom, to = periodTo },
                account = new
                {
                    id = account.Id,
                    account.Code,
                    account.Name,
                    type = EnumText(account.Type)
                },
                openingBalance = Money(openingBalance),
                periodDebit = Money(periodDebit),
                periodCredit = Money(periodCredit),
                closingBalance = Money(AccountingLedger.NaturalBalance(
                    account.Type,
                    openingDebit + periodDebit,
                    openingCredit + periodCredit)),
                analytics,
                movements = movements.AsEnumerable().Reverse()
            });
        });

        group.MapPost("/suppliers/{supplierId:guid}/payments", async (
            Guid supplierId,
            SupplierPaymentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.Amount <= 0m || request.Amount > 1_000_000_000m)
                return Results.BadRequest(new { message = "Сумма оплаты должна быть больше нуля." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var supplier = await db.Suppliers.FirstOrDefaultAsync(x =>
                x.Id == supplierId &&
                x.RestaurantId == restaurantId &&
                x.IsActive, ct);
            if (supplier is null)
                return Results.BadRequest(new { message = "Активный поставщик не найден." });

            var moneyAccount = await db.MoneyAccounts.FirstOrDefaultAsync(x =>
                x.Id == request.MoneyAccountId &&
                x.RestaurantId == restaurantId &&
                x.IsActive, ct);
            if (moneyAccount is null)
                return Results.BadRequest(new { message = "Активный денежный счёт не найден." });

            await AccountingLegacyBackfill.EnsureAsync(db, restaurantId, ct);

            var payableAccount = await AccountingLedger.EnsureSystemAccountAsync(
                db, restaurantId, AccountingLedger.SupplierPayableKey,
                "2.10", "Задолженность перед поставщиками", LedgerAccountType.Liability, ct);
            var advanceAccount = await AccountingLedger.EnsureSystemAccountAsync(
                db, restaurantId, AccountingLedger.SupplierAdvanceKey,
                "1.30", "Авансы поставщикам", LedgerAccountType.Asset, ct);
            var cashAccount = await AccountingLedger.EnsureMoneyAccountAsync(
                db, restaurantId, moneyAccount, ct);

            var payableTotals = await db.LedgerLines
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId &&
                            x.SupplierId == supplierId &&
                            x.AccountId == payableAccount.Id)
                .GroupBy(_ => 1)
                .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
                .FirstOrDefaultAsync(ct);

            var outstanding = Money((payableTotals?.Credit ?? 0m) - (payableTotals?.Debit ?? 0m));
            var amount = Money(request.Amount);
            var appliedToDebt = Money(Math.Min(amount, Math.Max(0m, outstanding)));
            var advancePart = Money(amount - appliedToDebt);
            var operationId = Guid.NewGuid();
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var description = $"Оплата поставщику {supplier.Name}";

            var lines = new List<AccountingLedger.LineDraft>();
            if (appliedToDebt > 0m)
            {
                lines.Add(new AccountingLedger.LineDraft(
                    payableAccount,
                    Debit: appliedToDebt,
                    SupplierId: supplier.Id));
            }
            if (advancePart > 0m)
            {
                lines.Add(new AccountingLedger.LineDraft(
                    advanceAccount,
                    Debit: advancePart,
                    SupplierId: supplier.Id));
            }
            lines.Add(new AccountingLedger.LineDraft(
                cashAccount,
                Credit: amount,
                MoneyAccountId: moneyAccount.Id));

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "SUPPLIER_PAYMENT",
                operationId,
                occurredAt,
                description,
                employeeId,
                lines,
                ct);

            AddAudit(db, user, restaurantId, "SUPPLIER_PAYMENT_CREATED", "Supplier", supplier.Id, new
            {
                supplierId = supplier.Id,
                supplierName = supplier.Name,
                moneyAccountId = moneyAccount.Id,
                moneyAccountName = moneyAccount.Name,
                amount,
                appliedToDebt,
                advancePart,
                outstandingBefore = outstanding,
                note = request.Note,
                occurredAt,
                operationId
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                operationId,
                amount,
                appliedToDebt,
                advance = advancePart,
                outstandingBefore = outstanding,
                outstandingAfter = Money(Math.Max(0m, outstanding - appliedToDebt))
            });
        }).RequireAuthorization(Permissions.FinanceManage);

        return app;
    }

    private static bool TryClaims(ClaimsPrincipal user, out Guid restaurantId, out Guid employeeId)
    {
        var restaurantOk = Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
        var employeeOk = Guid.TryParse(user.FindFirstValue("employee_id"), out employeeId);
        return restaurantOk && employeeOk;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum =>
        System.Text.RegularExpressions.Regex.Replace(value.ToString(), "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant();

    private static void AddAudit(
        RestaurantDbContext db,
        ClaimsPrincipal user,
        Guid restaurantId,
        string eventType,
        string entityType,
        Guid entityId,
        object payload)
    {
        Guid? employeeId = Guid.TryParse(user.FindFirstValue("employee_id"), out var parsedEmployeeId)
            ? parsedEmployeeId
            : null;

        db.AuditEvents.Add(new AuditEvent
        {
            RestaurantId = restaurantId,
            EmployeeId = employeeId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(payload)
        });
    }
}

public sealed record SupplierPaymentRequest(
    Guid MoneyAccountId,
    decimal Amount,
    DateTimeOffset? OccurredAt,
    string? Note);
