using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public static class AccountingAzerbaijanChartMigration
{
    private const string Marker = "ACCOUNTING_AZ_CHART_V1";

    private static readonly HashSet<string> LegacyFixedKeys =
    [
        "INVENTORY_GAIN",
        "WRITE_OFF_EXPENSE",
        "INVENTORY_LOSS",
        "CASH_CLEARING"
    ];

    public static async Task EnsureAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        CancellationToken ct)
    {
        var completed = await db.AuditEvents
            .AsNoTracking()
            .AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.EventType == Marker,
                ct);

        if (completed)
            return;

        // First persist the official accounts. Existing fixed-key accounts such as
        // SUPPLIER_PAYABLE, SUPPLIER_ADVANCE, SALES_REVENUE and COGS are updated
        // in place by EnsureFoundationAsync, so their ledger history is preserved.
        await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);
        await db.SaveChangesAsync(ct);

        var targets = await db.LedgerAccounts
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.SystemKey != null)
            .ToDictionaryAsync(x => x.SystemKey!, ct);

        var moneyAccounts = await db.MoneyAccounts
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .ToDictionaryAsync(x => x.Id, ct);

        var allAccounts = await db.LedgerAccounts
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(ct);

        var legacyAccounts = allAccounts
            .Where(x => IsLegacyKey(x.SystemKey))
            .ToList();

        foreach (var legacy in legacyAccounts)
        {
            var targetKey = ResolveTargetKey(legacy.SystemKey!, moneyAccounts);
            if (targetKey is null ||
                !targets.TryGetValue(targetKey, out var target) ||
                target.Id == legacy.Id)
            {
                continue;
            }

            await db.LedgerLines
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.AccountId == legacy.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.AccountId, target.Id),
                    ct);

            db.LedgerAccounts.Remove(legacy);
        }

        if (targets.TryGetValue(AccountingLedger.SalesRevenueKey, out var salesRevenue) &&
            targets.TryGetValue(AccountingLedger.SalesReturnsKey, out var salesReturns))
        {
            var refundEntryIds = db.LedgerEntries
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.ReferenceType == "POS_REFUND")
                .Select(x => x.Id);

            await db.LedgerLines
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.AccountId == salesRevenue.Id &&
                    x.Debit > 0m &&
                    refundEntryIds.Contains(x.EntryId))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.AccountId, salesReturns.Id),
                    ct);
        }

        await db.SaveChangesAsync(ct);

        db.AuditEvents.Add(new AuditEvent
        {
            RestaurantId = restaurantId,
            EventType = Marker,
            EntityType = "Accounting",
            EntityId = restaurantId,
            PayloadJson = "{\"profile\":\"AZ_KOS_IFRS_SME\",\"version\":1}"
        });

        await db.SaveChangesAsync(ct);
    }

    private static bool IsLegacyKey(string? systemKey)
    {
        if (systemKey is null) return false;

        return systemKey.StartsWith("WAREHOUSE:", StringComparison.Ordinal) ||
               systemKey.StartsWith("MONEY:", StringComparison.Ordinal) ||
               systemKey.StartsWith("INCOME_CATEGORY:", StringComparison.Ordinal) ||
               systemKey.StartsWith("EXPENSE_CATEGORY:", StringComparison.Ordinal) ||
               LegacyFixedKeys.Contains(systemKey);
    }

    private static string? ResolveTargetKey(
        string systemKey,
        IReadOnlyDictionary<Guid, MoneyAccount> moneyAccounts)
    {
        if (systemKey.StartsWith("WAREHOUSE:", StringComparison.Ordinal))
            return AccountingLedger.InventoryRawMaterialsKey;

        if (systemKey.StartsWith("INCOME_CATEGORY:", StringComparison.Ordinal))
            return AccountingLedger.OtherOperatingIncomeKey;

        if (systemKey.StartsWith("EXPENSE_CATEGORY:", StringComparison.Ordinal))
            return AccountingLedger.OtherOperatingExpenseKey;

        if (systemKey == "INVENTORY_GAIN")
            return AccountingLedger.OtherOperatingIncomeKey;

        if (systemKey is "WRITE_OFF_EXPENSE" or "INVENTORY_LOSS")
            return AccountingLedger.OtherOperatingExpenseKey;

        if (systemKey == "CASH_CLEARING")
            return AccountingLedger.CashEquivalentKey;

        if (!systemKey.StartsWith("MONEY:", StringComparison.Ordinal))
            return null;

        var rawId = systemKey["MONEY:".Length..];
        if (!Guid.TryParse(rawId, out var moneyAccountId) ||
            !moneyAccounts.TryGetValue(moneyAccountId, out var moneyAccount))
        {
            return AccountingLedger.CashEquivalentKey;
        }

        return moneyAccount.Type switch
        {
            MoneyAccountType.Cash => AccountingLedger.CashKey,
            MoneyAccountType.Card => AccountingLedger.TransitKey,
            MoneyAccountType.Bank => AccountingLedger.BankKey,
            _ => AccountingLedger.CashEquivalentKey
        };
    }
}
