using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public static class AccountingLedger
{
    // Azerbaijan chart of accounts (KOS / IFRS for SMEs based rules).
    // Restaurant-specific dimensions (warehouse, supplier, cash register, bank account)
    // are stored on LedgerLine and do not create separate synthetic accounts.
    public const string InventoryRawMaterialsKey = "AZ_201_1_RAW_MATERIALS";
    public const string InventoryMaterialsKey = "AZ_201_2_MATERIALS";
    public const string InventoryPackagingKey = "AZ_201_3_PACKAGING";
    public const string GoodsInventoryKey = "AZ_205_GOODS";
    public const string CashKey = "AZ_221_CASH";
    public const string TransitKey = "AZ_222_TRANSIT";
    public const string BankKey = "AZ_223_BANK";
    public const string CashEquivalentKey = "AZ_225_CASH_EQUIVALENTS";
    public const string VatRecoverableKey = "AZ_241_1_VAT_RECOVERABLE";

    public const string SupplierPayableKey = "SUPPLIER_PAYABLE";
    public const string SupplierAdvanceKey = "SUPPLIER_ADVANCE";
    public const string SalesRevenueKey = "SALES_REVENUE";
    public const string SalesReturnsKey = "AZ_602_SALES_RETURNS";
    public const string DiscountsKey = "AZ_603_DISCOUNTS";
    public const string TaxLiabilityKey = "AZ_521_TAX_LIABILITY";
    public const string CostOfGoodsSoldKey = "COGS";

    public const string OtherOperatingIncomeKey = "AZ_611_10_OTHER_OPERATING_INCOME";
    public const string OtherOperatingExpenseKey = "AZ_731_10_OTHER_OPERATING_EXPENSE";

    // Keep the existing public names used by stock/shift code, but route them to
    // the official Azerbaijan accounts.
    public const string InventoryGainKey = OtherOperatingIncomeKey;
    public const string WriteOffExpenseKey = OtherOperatingExpenseKey;
    public const string InventoryLossKey = OtherOperatingExpenseKey;
    public const string CashClearingKey = CashEquivalentKey;

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
        await EnsureSystemAccountAsync(
            db, restaurantId, InventoryRawMaterialsKey,
            "201-1", "Xammal", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, InventoryMaterialsKey,
            "201-2", "Materiallar", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, InventoryPackagingKey,
            "201-3", "Qablaşdırma materialları", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, GoodsInventoryKey,
            "205", "Mallar", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, CashKey,
            "221", "Kassa", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, TransitKey,
            "222", "Yolda olan pul köçürmələri", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, BankKey,
            "223", "Bank hesablaşma hesabları", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, CashEquivalentKey,
            "225", "Pul vəsaitlərinin ekvivalentləri", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, VatRecoverableKey,
            "241-1", "Əvəzləşdirilən əlavə dəyər vergisi", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, SupplierAdvanceKey,
            "243", "Verilmiş qısamüddətli avanslar", LedgerAccountType.Asset, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, TaxLiabilityKey,
            "521", "Vergi öhdəlikləri", LedgerAccountType.Liability, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, SupplierPayableKey,
            "531", "Malsatan və podratçılara qısamüddətli kreditor borcları",
            LedgerAccountType.Liability, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, SalesRevenueKey,
            "601-1", "Malların satışı", LedgerAccountType.Income, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, SalesReturnsKey,
            "602", "Satılmış malların qaytarılması və ucuzlaşdırılması",
            LedgerAccountType.Income, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, DiscountsKey,
            "603", "Verilmiş güzəştlər", LedgerAccountType.Income, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, OtherOperatingIncomeKey,
            "611-10", "Digər əməliyyat gəlirləri", LedgerAccountType.Income, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, CostOfGoodsSoldKey,
            "701-3", "Satılmış malların (hazır məhsulun) balans dəyəri",
            LedgerAccountType.Expense, ct);

        await EnsureSystemAccountAsync(
            db, restaurantId, OtherOperatingExpenseKey,
            "731-10", "Digər əməliyyat xərcləri", LedgerAccountType.Expense, ct);
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
        var definition = ResolveOfficialDefinition(systemKey, code, name, type);

        var local = db.LedgerAccounts.Local.FirstOrDefault(x =>
            x.RestaurantId == restaurantId && x.SystemKey == definition.SystemKey);
        if (local is not null)
        {
            Sync(local, definition.Code, definition.Name, definition.Type);
            return local;
        }

        var account = await db.LedgerAccounts.FirstOrDefaultAsync(x =>
            x.RestaurantId == restaurantId && x.SystemKey == definition.SystemKey, ct);
        if (account is not null)
        {
            Sync(account, definition.Code, definition.Name, definition.Type);
            return account;
        }

        account = new LedgerAccount
        {
            RestaurantId = restaurantId,
            Code = definition.Code,
            Name = definition.Name,
            Type = definition.Type,
            SystemKey = definition.SystemKey,
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
            InventoryRawMaterialsKey,
            "201-1",
            "Xammal",
            LedgerAccountType.Asset,
            ct);

    public static async Task<LedgerAccount> EnsureWarehouseAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid warehouseId,
        CancellationToken ct)
    {
        var warehouse = await db.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.Id == warehouseId, ct)
            ?? throw new InvalidOperationException("Warehouse was not found for accounting.");

        return await EnsureWarehouseAccountAsync(db, restaurantId, warehouse, ct);
    }

    public static Task<LedgerAccount> EnsureMoneyAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        MoneyAccount moneyAccount,
        CancellationToken ct)
    {
        var definition = moneyAccount.Type switch
        {
            MoneyAccountType.Cash => new AccountDefinition(
                CashKey, "221", "Kassa", LedgerAccountType.Asset),
            MoneyAccountType.Card => new AccountDefinition(
                TransitKey, "222", "Yolda olan pul köçürmələri", LedgerAccountType.Asset),
            MoneyAccountType.Bank => new AccountDefinition(
                BankKey, "223", "Bank hesablaşma hesabları", LedgerAccountType.Asset),
            _ => new AccountDefinition(
                CashEquivalentKey, "225", "Pul vəsaitlərinin ekvivalentləri", LedgerAccountType.Asset)
        };

        return EnsureSystemAccountAsync(
            db,
            restaurantId,
            definition.SystemKey,
            definition.Code,
            definition.Name,
            definition.Type,
            ct);
    }

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
            isIncome ? OtherOperatingIncomeKey : OtherOperatingExpenseKey,
            isIncome ? "611-10" : "731-10",
            isIncome ? "Digər əməliyyat gəlirləri" : "Digər əməliyyat xərcləri",
            isIncome ? LedgerAccountType.Income : LedgerAccountType.Expense,
            ct);
    }

    public static Task<MoneyAccount> EnsurePaymentMoneyAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        PaymentMethod method,
        CancellationToken ct) =>
        EnsurePaymentMoneyAccountAsync(db, restaurantId, method, null, ct);

    public static async Task<MoneyAccount> EnsurePaymentMoneyAccountAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        PaymentMethod method,
        Guid? deviceId,
        CancellationToken ct)
    {
        var type = method switch
        {
            PaymentMethod.Cash => MoneyAccountType.Cash,
            PaymentMethod.Card => MoneyAccountType.Card,
            _ => MoneyAccountType.Other
        };

        string? deviceName = null;
        if (deviceId.HasValue)
        {
            deviceName = db.Devices.Local
                .Where(x => x.RestaurantId == restaurantId && x.Id == deviceId.Value)
                .Select(x => x.Name)
                .FirstOrDefault()
                ?? await db.Devices
                    .AsNoTracking()
                    .Where(x => x.RestaurantId == restaurantId && x.Id == deviceId.Value)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(ct);
        }

        var baseName = method switch
        {
            PaymentMethod.Cash => "Касса",
            PaymentMethod.Card => "Эквайринг",
            _ => "Прочие оплаты"
        };
        var desiredName = deviceName is null ? baseName : $"{baseName}: {deviceName}";

        MoneyAccount? account;
        if (deviceId.HasValue && deviceName is not null)
        {
            account = db.MoneyAccounts.Local.FirstOrDefault(x =>
                x.RestaurantId == restaurantId && x.Name == desiredName)
                ?? await db.MoneyAccounts.FirstOrDefaultAsync(x =>
                    x.RestaurantId == restaurantId && x.Name == desiredName, ct);

            if (account is null)
            {
                account = new MoneyAccount
                {
                    RestaurantId = restaurantId,
                    Name = desiredName,
                    Type = type,
                    IsActive = true
                };
                db.MoneyAccounts.Add(account);
            }
            else
            {
                account.Type = type;
                account.IsActive = true;
            }
        }
        else
        {
            account = db.MoneyAccounts.Local
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
                        Name = desiredName,
                        Type = type,
                        IsActive = true
                    };
                    db.MoneyAccounts.Add(account);
                }
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

    private static AccountDefinition ResolveOfficialDefinition(
        string systemKey,
        string code,
        string name,
        LedgerAccountType type) =>
        systemKey switch
        {
            SupplierAdvanceKey => new(
                SupplierAdvanceKey, "243", "Verilmiş qısamüddətli avanslar", LedgerAccountType.Asset),
            SupplierPayableKey => new(
                SupplierPayableKey, "531",
                "Malsatan və podratçılara qısamüddətli kreditor borcları",
                LedgerAccountType.Liability),
            SalesRevenueKey => new(
                SalesRevenueKey, "601-1", "Malların satışı", LedgerAccountType.Income),
            CostOfGoodsSoldKey => new(
                CostOfGoodsSoldKey, "701-3",
                "Satılmış malların (hazır məhsulun) balans dəyəri",
                LedgerAccountType.Expense),
            InventoryRawMaterialsKey => new(
                InventoryRawMaterialsKey, "201-1", "Xammal", LedgerAccountType.Asset),
            InventoryMaterialsKey => new(
                InventoryMaterialsKey, "201-2", "Materiallar", LedgerAccountType.Asset),
            InventoryPackagingKey => new(
                InventoryPackagingKey, "201-3", "Qablaşdırma materialları", LedgerAccountType.Asset),
            GoodsInventoryKey => new(
                GoodsInventoryKey, "205", "Mallar", LedgerAccountType.Asset),
            CashKey => new(
                CashKey, "221", "Kassa", LedgerAccountType.Asset),
            TransitKey => new(
                TransitKey, "222", "Yolda olan pul köçürmələri", LedgerAccountType.Asset),
            BankKey => new(
                BankKey, "223", "Bank hesablaşma hesabları", LedgerAccountType.Asset),
            CashEquivalentKey => new(
                CashEquivalentKey, "225", "Pul vəsaitlərinin ekvivalentləri", LedgerAccountType.Asset),
            VatRecoverableKey => new(
                VatRecoverableKey, "241-1", "Əvəzləşdirilən əlavə dəyər vergisi", LedgerAccountType.Asset),
            TaxLiabilityKey => new(
                TaxLiabilityKey, "521", "Vergi öhdəlikləri", LedgerAccountType.Liability),
            SalesReturnsKey => new(
                SalesReturnsKey, "602", "Satılmış malların qaytarılması və ucuzlaşdırılması", LedgerAccountType.Income),
            DiscountsKey => new(
                DiscountsKey, "603", "Verilmiş güzəştlər", LedgerAccountType.Income),
            OtherOperatingIncomeKey => new(
                OtherOperatingIncomeKey, "611-10", "Digər əməliyyat gəlirləri", LedgerAccountType.Income),
            OtherOperatingExpenseKey => new(
                OtherOperatingExpenseKey, "731-10", "Digər əməliyyat xərcləri", LedgerAccountType.Expense),
            _ => new(systemKey, code, name, type)
        };

    private static void Sync(LedgerAccount account, string code, string name, LedgerAccountType type)
    {
        account.Code = code;
        account.Name = name;
        account.Type = type;
        account.IsSystem = true;
        account.IsActive = true;
    }

    private sealed record AccountDefinition(
        string SystemKey,
        string Code,
        string Name,
        LedgerAccountType Type);
}
