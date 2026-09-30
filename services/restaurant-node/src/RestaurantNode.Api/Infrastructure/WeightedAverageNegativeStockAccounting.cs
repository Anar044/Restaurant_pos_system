using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public static class WeightedAverageNegativeStockAccounting
{
    public sealed record Result(
        int AdjustedProducts,
        decimal CogsAdjustment);

    private sealed record BalanceInfo(
        decimal Quantity,
        decimal StockValue);

    public static async Task<Result> ApplyReceiptAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid employeeId,
        Guid warehouseId,
        Guid receiptDocumentId,
        DateTimeOffset occurredAt,
        IReadOnlyCollection<StockDocumentLine> receiptLines,
        CancellationToken ct)
    {
        if (receiptLines.Count == 0)
            return new Result(0, 0m);

        var restaurant = await db.Restaurants
            .AsNoTracking()
            .Where(x => x.Id == restaurantId)
            .Select(x => new { x.InventoryCostMethod })
            .FirstAsync(ct);

        if (restaurant.InventoryCostMethod != InventoryCostMethod.WeightedAverage)
            return new Result(0, 0m);

        var productIds = receiptLines
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();

        // These balances come from the database before the current receipt is saved.
        // Unsaved receipt movements in the DbContext are intentionally not included.
        var balances = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.WarehouseId == warehouseId &&
                productIds.Contains(x.ProductId))
            .GroupBy(x => x.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Quantity = group.Sum(x => x.QuantityDelta),
                StockValue = group.Sum(x => x.CostDelta ?? 0m)
            })
            .ToDictionaryAsync(
                x => x.ProductId,
                x => new BalanceInfo(x.Quantity, x.StockValue),
                ct);

        var products = await db.Products
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                productIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.InventoryAccountCode
            })
            .ToDictionaryAsync(x => x.Id, ct);

        await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);
        var cogsAccount = await AccountingLedger.EnsureSystemAccountAsync(
            db,
            restaurantId,
            AccountingLedger.CostOfGoodsSoldKey,
            "701-3",
            "Satılmış malların (hazır məhsulun) balans dəyəri",
            LedgerAccountType.Expense,
            ct);

        var ledgerLines = new List<AccountingLedger.LineDraft>();
        var adjustedProducts = 0;
        var signedCogsAdjustment = 0m;

        foreach (var line in receiptLines)
        {
            var balance = balances.GetValueOrDefault(line.ProductId);
            if (balance is null || balance.Quantity >= 0m || line.Quantity <= 0m)
                continue;

            if (!products.TryGetValue(line.ProductId, out var product) ||
                !AccountingLedger.IsSupportedInventoryAccountCode(product.InventoryAccountCode))
            {
                continue;
            }

            var coveredNegativeQuantity = Math.Min(-balance.Quantity, line.Quantity);
            if (coveredNegativeQuantity <= 0m)
                continue;

            var provisionalUnitCost = balance.Quantity == 0m
                ? 0m
                : AccountingLedger.Money(balance.StockValue / balance.Quantity);
            var actualReceiptUnitCost = AccountingLedger.Money(
                line.InventoryCostAmount / line.Quantity);

            var correction = AccountingLedger.Money(
                coveredNegativeQuantity *
                (actualReceiptUnitCost - provisionalUnitCost));

            if (correction == 0m)
                continue;

            // Positive correction means the actual receipt cost is higher than the
            // provisional cost used while stock was negative. Increase COGS and
            // decrease the inventory value created by the receipt. Negative
            // correction performs the exact opposite.
            db.StockMovements.Add(new StockMovement
            {
                RestaurantId = restaurantId,
                WarehouseId = warehouseId,
                ProductId = line.ProductId,
                EmployeeId = employeeId,
                OperationId = receiptDocumentId,
                Type = "COST_CORRECTION",
                QuantityDelta = 0m,
                UnitCost = actualReceiptUnitCost,
                CostDelta = -correction,
                ReferenceType = "RECEIPT_NEGATIVE_COST",
                ReferenceId = receiptDocumentId,
                Note = "Корректировка себестоимости отрицательного остатка",
                CreatedAt = occurredAt
            });

            var stockAccount = await AccountingLedger.EnsureInventoryAccountAsync(
                db,
                restaurantId,
                product.InventoryAccountCode,
                ct);

            if (correction > 0m)
            {
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    cogsAccount,
                    Debit: correction));
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    stockAccount,
                    Credit: correction,
                    WarehouseId: warehouseId));
            }
            else
            {
                var amount = -correction;
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    stockAccount,
                    Debit: amount,
                    WarehouseId: warehouseId));
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    cogsAccount,
                    Credit: amount));
            }

            adjustedProducts++;
            signedCogsAdjustment += correction;
        }

        if (ledgerLines.Count > 0)
        {
            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "RECEIPT_NEGATIVE_COST_CORRECTION",
                receiptDocumentId,
                occurredAt,
                "Корректировка себестоимости после прихода на отрицательный остаток",
                employeeId,
                ledgerLines,
                ct);
        }

        return new Result(
            adjustedProducts,
            AccountingLedger.Money(signedCogsAdjustment));
    }
}
