using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public static class AccountingLedger
{
    public const string SupplierPayableKey = "SUPPLIER_PAYABLE";
    public const string SupplierAdvanceKey = "SUPPLIER_ADVANCE";
    public const string SalesRevenueKey = "SALES_REVENUE";
    public const string InventoryGainKey = "INVENTORY_GAIN";
    public const string CostOfGoodsSoldKey = "COGS";
    public const string WriteOffExpenseKey = "WRITE_OFF_EXPENSE";
    public const string InventoryLossKey = "INVENTORY_LOSS";
    public const string CashClearingKey = "CASH_CLEARING";

    public sealed record LineDraft(
        LedgerAccount Account,
        decimal Debit = 0m,
        decimal Credit = 0m,
        Guid? SupplierId = null,
        Guid? WarehouseId = null,
        Guid? MoneyAccountId = null);

    public static async Task EnsureFoundationAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        CancellationToken ct)
    {
        await EnsureSystemAccountAsync(db, restaurantId, SupplierAdvanceKey, "1.30", "Авансы поставщикам", LedgerAccountType.Asset, ct);
        await EnsureSystemAccountAsync(db, restaurantId, SupplierPayableKey, "2.10", "Задолженность перед поставщиками", LedgerAccountType.Liability, ct);
        await EnsureSystemAccountAsync(db, restaurantId, SalesRevenueKey, "4.10", "Выручка от продаж", LedgerAccountType.Income, ct);
        await EnsureSystemAccountAsync(db, restaurantId, InventoryGainKey, "4.20", "Излишки по инвентаризации", LedgerAccountType.Income, ct);
        await EnsureSystemAccountAsync(db, restaurantId, CostOfGoodsSoldKey, "5.10", "Себестоимость продаж", LedgerAccountType.Expense, ct);
        await EnsureSystemAccountAsync(db, restaurantId, WriteOffExpenseKey, "5.20", "Списания и порча", LedgerAccountType.Expense, ct);
        await EnsureSystemAccountAsync(db, restaurantId, InventoryLossKey, "5.30", "Недостачи по инвентаризации", LedgerAccountType.Expense, ct);
        await EnsureSystemAccountAsync(db, restaurantId, CashClearingKey, "3.90", "Кассовые внесения и изъятия", LedgerAccountType.Equity, ct);

        var warehouses = await db.Warehouses
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(ct);
        foreach (var warehouse in warehouses)
            await EnsureWarehouseAccountAsync(db, restaurantId, warehouse, ct);

        var moneyAccounts = await db.MoneyAccounts
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(ct);
        foreach (var moneyAccount in moneyAccounts)
            await EnsureMoneyAccountAsync(db, restaurantId, moneyAccount, ct);

        var categories = await db.MoneyCategories
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(ct);
        foreach (var category in categories)
            await EnsureCategoryAccountAsync(db, restaurantId, category, ct);

    }

    public static async Task<LedgerAccount> EnsureSystemAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        string systemKey,
        string code,
        string name,
        LedgerAccountType type,
        CancellationToken ct)
    {
        var local = db.LedgerAccounts.Local.FirstOrDefault(x =>
            x.RestaurantId == restaurantId && x.SystemKey == systemKey);
        if (local is not null)
        {
            Sync(local, code, name, type);
            return local;
        }

        var account = await db.LedgerAccounts.FirstOrDefaultAsync(x =>
            x.RestaurantId == restaurantId && x.SystemKey == systemKey, ct);
        if (account is not null)
        {
            Sync(account, code, name, type);
            return account;
        }

        account = new LedgerAccount
        {
            RestaurantId = restaurantId,
            Code = code,
            Name = name,
            Type = type,
            SystemKey = systemKey,
            IsSystem = true,
            IsActive = true
        };
        db.LedgerAccounts.Add(account);
        return account;
    }

    public static Task<LedgerAccount> EnsureWarehouseAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Warehouse warehouse,
        CancellationToken ct) =>
        EnsureSystemAccountAsync(
            db,
            restaurantId,
            "WAREHOUSE:" + warehouse.Id,
            "1.10." + ShortId(warehouse.Id),
            "Склад: " + warehouse.Name,
            LedgerAccountType.Asset,
            ct);

    public static async Task<LedgerAccount> EnsureWarehouseAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid warehouseId,
        CancellationToken ct)
    {
        var warehouse = await db.Warehouses
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.Id == warehouseId, ct)
            ?? throw new InvalidOperationException("Warehouse was not found for accounting.");
        return await EnsureWarehouseAccountAsync(db, restaurantId, warehouse, ct);
    }

    public static Task<LedgerAccount> EnsureMoneyAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        MoneyAccount moneyAccount,
        CancellationToken ct) =>
        EnsureSystemAccountAsync(
            db,
            restaurantId,
            "MONEY:" + moneyAccount.Id,
            MoneyCode(moneyAccount),
            moneyAccount.Name,
            LedgerAccountType.Asset,
            ct);

    public static Task<LedgerAccount> EnsureCategoryAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        MoneyCategory category,
        CancellationToken ct)
    {
        var isIncome = category.Direction == MoneyDirection.Income;
        return EnsureSystemAccountAsync(
            db,
            restaurantId,
            (isIncome ? "INCOME_CATEGORY:" : "EXPENSE_CATEGORY:") + category.Id,
            (isIncome ? "4.90." : "5.90.") + ShortId(category.Id),
            category.Name,
            isIncome ? LedgerAccountType.Income : LedgerAccountType.Expense,
            ct);
    }

    public static async Task<MoneyAccount> EnsurePaymentMoneyAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        PaymentMethod method,
        CancellationToken ct)
    {
        var type = method switch
        {
            PaymentMethod.Cash => MoneyAccountType.Cash,
            PaymentMethod.Card => MoneyAccountType.Card,
            _ => MoneyAccountType.Other
        };

        var account = db.MoneyAccounts.Local
            .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.Type == type)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefault()
            ?? await db.MoneyAccounts
                .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.Type == type)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            account = db.MoneyAccounts.Local
                .Where(x => x.RestaurantId == restaurantId && x.Type == type)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefault()
                ?? await db.MoneyAccounts
                    .Where(x => x.RestaurantId == restaurantId && x.Type == type)
                    .OrderBy(x => x.CreatedAt)
                    .FirstOrDefaultAsync(ct);

            if (account is not null)
            {
                account.IsActive = true;
            }
            else
            {
                account = new MoneyAccount
                {
                    RestaurantId = restaurantId,
                    Name = method switch
                    {
                        PaymentMethod.Cash => "Торговая касса",
                        PaymentMethod.Card => "Эквайринг",
                        _ => "Прочие оплаты"
                    },
                    Type = type,
                    IsActive = true
                };
                db.MoneyAccounts.Add(account);
            }
        }

        await EnsureMoneyAccountAsync(db, restaurantId, account, ct);
        return account;
    }

    public static async Task<Guid> PostAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        string referenceType,
        Guid referenceId,
        DateTimeOffset occurredAt,
        string description,
        Guid? employeeId,
        IEnumerable<LineDraft> sourceLines,
        CancellationToken ct)
    {
        var existing = await db.LedgerEntries
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.ReferenceType == referenceType &&
                x.ReferenceId == referenceId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (existing.HasValue)
            return existing.Value;

        var lines = sourceLines
            .Select(x => x with
            {
                Debit = Money(x.Debit),
                Credit = Money(x.Credit)
            })
            .Where(x => x.Debit != 0m || x.Credit != 0m)
            .ToList();

        if (lines.Count < 2)
            throw new InvalidOperationException("Accounting entry requires at least two non-zero lines.");
        if (lines.Any(x => x.Debit < 0m || x.Credit < 0m || (x.Debit > 0m && x.Credit > 0m)))
            throw new InvalidOperationException("Each accounting line must contain either debit or credit.");

        var debit = Money(lines.Sum(x => x.Debit));
        var credit = Money(lines.Sum(x => x.Credit));
        if (debit != credit)
            throw new InvalidOperationException($"Accounting entry is not balanced: debit={debit}, credit={credit}.");

        var entry = new LedgerEntry
        {
            RestaurantId = restaurantId,
            OccurredAt = occurredAt,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description.Length <= 300 ? description : description[..300],
            EmployeeId = employeeId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        foreach (var line in lines)
        {
            entry.Lines.Add(new LedgerLine
            {
                RestaurantId = restaurantId,
                AccountId = line.Account.Id,
                Debit = line.Debit,
                Credit = line.Credit,
                SupplierId = line.SupplierId,
                WarehouseId = line.WarehouseId,
                MoneyAccountId = line.MoneyAccountId
            });
        }

        db.LedgerEntries.Add(entry);
        return entry.Id;
    }

    public static decimal NaturalBalance(LedgerAccountType type, decimal debit, decimal credit) =>
        type is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? Money(debit - credit)
            : Money(credit - debit);

    public static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static void Sync(LedgerAccount account, string code, string name, LedgerAccountType type)
    {
        account.Code = code;
        account.Name = name;
        account.Type = type;
        account.IsSystem = true;
    }

    private static string MoneyCode(MoneyAccount account)
    {
        var prefix = account.Type switch
        {
            MoneyAccountType.Cash => "1.01.",
            MoneyAccountType.Bank => "1.02.",
            MoneyAccountType.Card => "1.03.",
            _ => "1.09."
        };
        return prefix + ShortId(account.Id);
    }

    private static string ShortId(Guid value) =>
        value.ToString("N")[..8].ToUpperInvariant();
}
