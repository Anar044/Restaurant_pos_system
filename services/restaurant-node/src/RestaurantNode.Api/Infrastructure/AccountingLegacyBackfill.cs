using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public static class AccountingLegacyBackfill
{
    public static async Task EnsureAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        CancellationToken ct)
    {
        await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);

        var receipts = await db.StockDocuments
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.Type == StockDocumentType.Receipt &&
                x.Status == StockDocumentStatus.Posted &&
                x.WarehouseId.HasValue &&
                x.SupplierId.HasValue &&
                x.TotalAmount > 0m)
            .ToListAsync(ct);

        var warehouses = await db.Warehouses
            .Where(x => x.RestaurantId == restaurantId)
            .ToDictionaryAsync(x => x.Id, ct);

        var payableAccount = await AccountingLedger.EnsureSystemAccountAsync(
            db, restaurantId, AccountingLedger.SupplierPayableKey,
            "2.10", "Задолженность перед поставщиками", LedgerAccountType.Liability, ct);

        foreach (var document in receipts)
        {
            if (!warehouses.TryGetValue(document.WarehouseId!.Value, out var warehouse))
                continue;

            var stockAccount = await AccountingLedger.EnsureWarehouseAccountAsync(
                db, restaurantId, warehouse, ct);

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "STOCK_RECEIPT",
                document.Id,
                document.DocumentDate,
                $"Приходная накладная {document.Number}",
                document.PostedByEmployeeId ?? document.CreatedByEmployeeId,
                new[]
                {
                    new AccountingLedger.LineDraft(
                        stockAccount,
                        Debit: document.TotalAmount,
                        WarehouseId: warehouse.Id),
                    new AccountingLedger.LineDraft(
                        payableAccount,
                        Credit: document.TotalAmount,
                        SupplierId: document.SupplierId)
                },
                ct);
        }

        var manualMoney = await db.MoneyTransactions
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(ct);

        var moneyAccounts = await db.MoneyAccounts
            .Where(x => x.RestaurantId == restaurantId)
            .ToDictionaryAsync(x => x.Id, ct);
        var categories = await db.MoneyCategories
            .Where(x => x.RestaurantId == restaurantId)
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var transaction in manualMoney)
        {
            if (!moneyAccounts.TryGetValue(transaction.AccountId, out var moneyAccount) ||
                !categories.TryGetValue(transaction.CategoryId, out var category))
            {
                continue;
            }

            var moneyLedger = await AccountingLedger.EnsureMoneyAccountAsync(
                db, restaurantId, moneyAccount, ct);
            var categoryLedger = await AccountingLedger.EnsureCategoryAccountAsync(
                db, restaurantId, category, ct);

            var lines = transaction.Direction == MoneyDirection.Income
                ? new[]
                {
                    new AccountingLedger.LineDraft(
                        moneyLedger,
                        Debit: transaction.Amount,
                        MoneyAccountId: moneyAccount.Id),
                    new AccountingLedger.LineDraft(
                        categoryLedger,
                        Credit: transaction.Amount)
                }
                : new[]
                {
                    new AccountingLedger.LineDraft(
                        categoryLedger,
                        Debit: transaction.Amount),
                    new AccountingLedger.LineDraft(
                        moneyLedger,
                        Credit: transaction.Amount,
                        MoneyAccountId: moneyAccount.Id)
                };

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "MONEY_TRANSACTION",
                transaction.Id,
                transaction.OccurredAt,
                category.Name + (transaction.Note is null ? string.Empty : ": " + transaction.Note),
                transaction.EmployeeId,
                lines,
                ct);
        }

        var salesAccount = await AccountingLedger.EnsureSystemAccountAsync(
            db, restaurantId, AccountingLedger.SalesRevenueKey,
            "4.10", "Выручка от продаж", LedgerAccountType.Income, ct);

        var payments = await db.Payments
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                (x.Status == PaymentStatus.Completed || x.Status == PaymentStatus.Refunded))
            .ToListAsync(ct);

        var shiftDevices = await db.Shifts
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .ToDictionaryAsync(x => x.Id, x => x.DeviceId, ct);

        var orderNumbers = await db.Orders
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayNumber, ct);

        foreach (var payment in payments)
        {
            shiftDevices.TryGetValue(payment.ShiftId, out var paymentDeviceId);
            var moneyAccount = await AccountingLedger.EnsurePaymentMoneyAccountAsync(
                db,
                restaurantId,
                payment.Method,
                paymentDeviceId == Guid.Empty ? null : paymentDeviceId,
                ct);
            var moneyLedger = await AccountingLedger.EnsureMoneyAccountAsync(
                db, restaurantId, moneyAccount, ct);
            orderNumbers.TryGetValue(payment.OrderId, out var orderNumber);

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "POS_PAYMENT",
                payment.Id,
                payment.CreatedAt,
                orderNumber > 0 ? $"Оплата заказа №{orderNumber}" : "Оплата заказа",
                payment.EmployeeId,
                new[]
                {
                    new AccountingLedger.LineDraft(
                        moneyLedger,
                        Debit: payment.Amount,
                        MoneyAccountId: moneyAccount.Id),
                    new AccountingLedger.LineDraft(
                        salesAccount,
                        Credit: payment.Amount)
                },
                ct);
        }

        var paymentLookup = payments.ToDictionary(x => x.Id);
        var refunds = await db.PaymentRefunds
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(ct);

        foreach (var refund in refunds)
        {
            if (!paymentLookup.TryGetValue(refund.PaymentId, out var payment))
                continue;

            shiftDevices.TryGetValue(refund.ShiftId, out var refundDeviceId);
            var moneyAccount = await AccountingLedger.EnsurePaymentMoneyAccountAsync(
                db,
                restaurantId,
                payment.Method,
                refundDeviceId == Guid.Empty ? null : refundDeviceId,
                ct);
            var moneyLedger = await AccountingLedger.EnsureMoneyAccountAsync(
                db, restaurantId, moneyAccount, ct);

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "POS_REFUND",
                refund.Id,
                refund.CreatedAt,
                "Возврат оплаты заказа",
                refund.EmployeeId,
                new[]
                {
                    new AccountingLedger.LineDraft(
                        salesAccount,
                        Debit: refund.Amount),
                    new AccountingLedger.LineDraft(
                        moneyLedger,
                        Credit: refund.Amount,
                        MoneyAccountId: moneyAccount.Id)
                },
                ct);
        }

        var cashEntries = await db.CashTransactions
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(ct);

        if (cashEntries.Count > 0)
        {
            var cashClearing = await AccountingLedger.EnsureSystemAccountAsync(
                db, restaurantId, AccountingLedger.CashClearingKey,
                "3.90", "Кассовые внесения и изъятия", LedgerAccountType.Equity, ct);

            foreach (var entry in cashEntries)
            {
                shiftDevices.TryGetValue(entry.ShiftId, out var cashDeviceId);
                var cashMoneyAccount = await AccountingLedger.EnsurePaymentMoneyAccountAsync(
                    db,
                    restaurantId,
                    PaymentMethod.Cash,
                    cashDeviceId == Guid.Empty ? null : cashDeviceId,
                    ct);
                var cashLedger = await AccountingLedger.EnsureMoneyAccountAsync(
                    db, restaurantId, cashMoneyAccount, ct);

                var lines = entry.Type == CashTransactionType.Deposit
                    ? new[]
                    {
                        new AccountingLedger.LineDraft(
                            cashLedger,
                            Debit: entry.Amount,
                            MoneyAccountId: cashMoneyAccount.Id),
                        new AccountingLedger.LineDraft(
                            cashClearing,
                            Credit: entry.Amount)
                    }
                    : new[]
                    {
                        new AccountingLedger.LineDraft(
                            cashClearing,
                            Debit: entry.Amount),
                        new AccountingLedger.LineDraft(
                            cashLedger,
                            Credit: entry.Amount,
                            MoneyAccountId: cashMoneyAccount.Id)
                    };

                await AccountingLedger.PostAsync(
                    db,
                    restaurantId,
                    "SHIFT_CASH_TRANSACTION",
                    entry.Id,
                    entry.CreatedAt,
                    entry.Type == CashTransactionType.Deposit
                        ? "Внесение в кассу"
                        : "Изъятие из кассы",
                    entry.EmployeeId,
                    lines,
                    ct);
            }
        }

        await BackfillStockOperationsAsync(db, restaurantId, warehouses, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task BackfillStockOperationsAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        IReadOnlyDictionary<Guid, Warehouse> warehouses,
        CancellationToken ct)
    {
        var movements = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                (x.ReferenceType == "WRITE_OFF" ||
                 x.ReferenceType == "TRANSFER" ||
                 x.ReferenceType == "INVENTORY"))
            .ToListAsync(ct);

        var writeOffExpense = await AccountingLedger.EnsureSystemAccountAsync(
            db, restaurantId, AccountingLedger.WriteOffExpenseKey,
            "5.20", "Списания и порча", LedgerAccountType.Expense, ct);
        var inventoryGain = await AccountingLedger.EnsureSystemAccountAsync(
            db, restaurantId, AccountingLedger.InventoryGainKey,
            "4.20", "Излишки по инвентаризации", LedgerAccountType.Income, ct);
        var inventoryLoss = await AccountingLedger.EnsureSystemAccountAsync(
            db, restaurantId, AccountingLedger.InventoryLossKey,
            "5.30", "Недостачи по инвентаризации", LedgerAccountType.Expense, ct);

        foreach (var group in movements.GroupBy(x => new { x.ReferenceType, x.OperationId }))
        {
            var rows = group.ToList();
            var occurredAt = rows.Min(x => x.CreatedAt);
            var employeeId = rows.Select(x => (Guid?)x.EmployeeId).FirstOrDefault();

            if (group.Key.ReferenceType == "WRITE_OFF")
            {
                var costs = rows
                    .Where(x => (x.CostDelta ?? 0m) < 0m)
                    .GroupBy(x => x.WarehouseId)
                    .Select(g => new
                    {
                        WarehouseId = g.Key,
                        Amount = AccountingLedger.Money(-g.Sum(x => x.CostDelta ?? 0m))
                    })
                    .Where(x => x.Amount > 0m)
                    .ToList();
                var total = AccountingLedger.Money(costs.Sum(x => x.Amount));
                if (total <= 0m) continue;

                var lines = new List<AccountingLedger.LineDraft>
                {
                    new(writeOffExpense, Debit: total)
                };
                foreach (var cost in costs)
                {
                    if (!warehouses.TryGetValue(cost.WarehouseId, out var warehouse)) continue;
                    var stockAccount = await AccountingLedger.EnsureWarehouseAccountAsync(
                        db, restaurantId, warehouse, ct);
                    lines.Add(new AccountingLedger.LineDraft(
                        stockAccount,
                        Credit: cost.Amount,
                        WarehouseId: warehouse.Id));
                }

                if (AccountingLedger.Money(lines.Sum(x => x.Debit)) ==
                    AccountingLedger.Money(lines.Sum(x => x.Credit)))
                {
                    await AccountingLedger.PostAsync(
                        db, restaurantId, "STOCK_WRITE_OFF", group.Key.OperationId,
                        occurredAt, "Списание со склада", employeeId, lines, ct);
                }
            }
            else if (group.Key.ReferenceType == "TRANSFER")
            {
                var byWarehouse = rows
                    .GroupBy(x => x.WarehouseId)
                    .Select(g => new
                    {
                        WarehouseId = g.Key,
                        Amount = AccountingLedger.Money(g.Sum(x => x.CostDelta ?? 0m))
                    })
                    .Where(x => x.Amount != 0m)
                    .ToList();

                var lines = new List<AccountingLedger.LineDraft>();
                foreach (var row in byWarehouse)
                {
                    if (!warehouses.TryGetValue(row.WarehouseId, out var warehouse)) continue;
                    var stockAccount = await AccountingLedger.EnsureWarehouseAccountAsync(
                        db, restaurantId, warehouse, ct);
                    lines.Add(row.Amount > 0m
                        ? new AccountingLedger.LineDraft(
                            stockAccount,
                            Debit: row.Amount,
                            WarehouseId: warehouse.Id)
                        : new AccountingLedger.LineDraft(
                            stockAccount,
                            Credit: -row.Amount,
                            WarehouseId: warehouse.Id));
                }

                if (lines.Count >= 2 &&
                    AccountingLedger.Money(lines.Sum(x => x.Debit)) ==
                    AccountingLedger.Money(lines.Sum(x => x.Credit)))
                {
                    await AccountingLedger.PostAsync(
                        db, restaurantId, "STOCK_TRANSFER", group.Key.OperationId,
                        occurredAt, "Внутреннее перемещение", employeeId, lines, ct);
                }
            }
            else if (group.Key.ReferenceType == "INVENTORY")
            {
                var lines = new List<AccountingLedger.LineDraft>();
                foreach (var warehouseGroup in rows.GroupBy(x => x.WarehouseId))
                {
                    if (!warehouses.TryGetValue(warehouseGroup.Key, out var warehouse)) continue;
                    var stockAccount = await AccountingLedger.EnsureWarehouseAccountAsync(
                        db, restaurantId, warehouse, ct);
                    var gain = AccountingLedger.Money(
                        warehouseGroup.Where(x => (x.CostDelta ?? 0m) > 0m)
                            .Sum(x => x.CostDelta ?? 0m));
                    var loss = AccountingLedger.Money(
                        -warehouseGroup.Where(x => (x.CostDelta ?? 0m) < 0m)
                            .Sum(x => x.CostDelta ?? 0m));

                    if (gain > 0m)
                    {
                        lines.Add(new AccountingLedger.LineDraft(
                            stockAccount,
                            Debit: gain,
                            WarehouseId: warehouse.Id));
                        lines.Add(new AccountingLedger.LineDraft(
                            inventoryGain,
                            Credit: gain));
                    }
                    if (loss > 0m)
                    {
                        lines.Add(new AccountingLedger.LineDraft(
                            inventoryLoss,
                            Debit: loss));
                        lines.Add(new AccountingLedger.LineDraft(
                            stockAccount,
                            Credit: loss,
                            WarehouseId: warehouse.Id));
                    }
                }

                if (lines.Count >= 2)
                {
                    await AccountingLedger.PostAsync(
                        db, restaurantId, "STOCK_INVENTORY", group.Key.OperationId,
                        occurredAt, "Инвентаризация склада", employeeId, lines, ct);
                }
            }
        }
    }
}
