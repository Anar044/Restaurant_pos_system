using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public static class OrderStockAccounting
{
    public sealed record ApplyResult(bool Applied, decimal TotalCost, string? Error);

    private sealed record ConsumptionKey(Guid WarehouseId, Guid ProductId);
    private sealed record CostBucketKey(Guid WarehouseId, string InventoryAccountCode);
    private sealed record ProductInfo(
        Guid Id,
        string Name,
        bool TrackStock,
        Guid? PreparationPlaceTypeId,
        string? InventoryAccountCode);
    private sealed record BalanceInfo(decimal Quantity, decimal StockValue);

    public static async Task<ApplyResult> ApplyOnCloseAsync(
        RestaurantDbContext db,
        Order order,
        Guid restaurantId,
        Guid employeeId,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var alreadyApplied = await db.StockMovements
            .AsNoTracking()
            .AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.ReferenceType == "ORDER" &&
                x.ReferenceId == order.Id,
                ct);
        if (alreadyApplied)
            return new ApplyResult(false, 0m, null);

        var products = await db.Products
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => new ProductInfo(
                x.Id,
                x.Name,
                x.TrackStock,
                x.PreparationPlaceTypeId,
                x.InventoryAccountCode))
            .ToDictionaryAsync(x => x.Id, ct);

        var recipeLines = await db.RecipeLines
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .Select(x => new { x.ProductId, x.IngredientProductId, x.Quantity })
            .ToListAsync(ct);
        var recipeLookup = recipeLines
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        Guid? groupId = null;
        if (order.OriginDeviceId.HasValue)
        {
            groupId = await db.RestaurantGroupDevices
                .AsNoTracking()
                .Where(x => x.DeviceId == order.OriginDeviceId.Value)
                .Select(x => (Guid?)x.GroupId)
                .FirstOrDefaultAsync(ct);
        }

        var departmentsQuery = db.RestaurantDepartments
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.IsActive &&
                x.WarehouseId.HasValue);
        if (groupId.HasValue)
            departmentsQuery = departmentsQuery.Where(x => x.GroupId == groupId.Value);

        var departmentRoutes = await departmentsQuery
            .Select(x => new { x.PreparationPlaceTypeId, x.WarehouseId })
            .ToListAsync(ct);

        var activeWarehouses = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => x.Id)
            .ToArrayAsync(ct);

        Guid? ResolveWarehouse(Guid? preparationPlaceTypeId)
        {
            if (preparationPlaceTypeId.HasValue)
            {
                var route = departmentRoutes.FirstOrDefault(x =>
                    x.PreparationPlaceTypeId == preparationPlaceTypeId &&
                    x.WarehouseId.HasValue);
                if (route?.WarehouseId is Guid routed)
                    return routed;
            }

            return activeWarehouses.Length == 1 ? activeWarehouses[0] : null;
        }

        var consumption = new Dictionary<ConsumptionKey, decimal>();

        void AddConsumption(Guid productId, decimal quantity, Guid? preparationPlaceTypeId, int depth)
        {
            if (quantity <= 0m || depth > 12)
                return;
            if (!products.TryGetValue(productId, out var product))
                return;

            var routeType = product.PreparationPlaceTypeId ?? preparationPlaceTypeId;
            if (recipeLookup.TryGetValue(productId, out var recipe) && recipe.Length > 0)
            {
                foreach (var line in recipe)
                {
                    AddConsumption(
                        line.IngredientProductId,
                        quantity * line.Quantity,
                        routeType,
                        depth + 1);
                }
                return;
            }

            if (!product.TrackStock)
                return;
            if (!AccountingLedger.IsSupportedInventoryAccountCode(product.InventoryAccountCode))
                throw new InvalidOperationException(
                    $"Для позиции '{product.Name}' не настроен счёт складского учёта.");

            var warehouseId = ResolveWarehouse(routeType);
            if (!warehouseId.HasValue)
                throw new InvalidOperationException(
                    $"Для позиции '{product.Name}' не настроен склад места приготовления.");

            var key = new ConsumptionKey(warehouseId.Value, productId);
            consumption[key] = consumption.GetValueOrDefault(key) + quantity;
        }

        try
        {
            foreach (var item in order.Items.Where(x => x.Status != OrderItemStatus.Voided))
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                    continue;

                AddConsumption(item.ProductId, item.Quantity, product.PreparationPlaceTypeId, 0);

                foreach (var modifier in item.Modifiers)
                {
                    if (!products.TryGetValue(modifier.ModifierId, out var modifierProduct))
                        continue;

                    var modifierQuantity = item.Quantity * modifier.Quantity;
                    AddConsumption(
                        modifier.ModifierId,
                        modifierQuantity,
                        modifierProduct.PreparationPlaceTypeId ?? product.PreparationPlaceTypeId,
                        0);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            return new ApplyResult(false, 0m, ex.Message);
        }

        if (consumption.Count == 0)
            return new ApplyResult(false, 0m, null);

        var warehouseIds = consumption.Keys.Select(x => x.WarehouseId).Distinct().ToArray();
        var productIds = consumption.Keys.Select(x => x.ProductId).Distinct().ToArray();

        var balances = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                warehouseIds.Contains(x.WarehouseId) &&
                productIds.Contains(x.ProductId))
            .GroupBy(x => new { x.WarehouseId, x.ProductId })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.ProductId,
                Quantity = g.Sum(x => x.QuantityDelta),
                StockValue = g.Sum(x => x.CostDelta ?? 0m)
            })
            .ToListAsync(ct);

        var balanceLookup = balances.ToDictionary(
            x => new ConsumptionKey(x.WarehouseId, x.ProductId),
            x => new BalanceInfo(x.Quantity, x.StockValue));

        var latestCosts = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                warehouseIds.Contains(x.WarehouseId) &&
                productIds.Contains(x.ProductId) &&
                x.UnitCost.HasValue &&
                x.UnitCost.Value > 0m)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.WarehouseId, x.ProductId, UnitCost = x.UnitCost!.Value })
            .ToListAsync(ct);
        var latestCostLookup = latestCosts
            .GroupBy(x => new ConsumptionKey(x.WarehouseId, x.ProductId))
            .ToDictionary(g => g.Key, g => g.First().UnitCost);

        var warehouseCost = new Dictionary<CostBucketKey, decimal>();
        foreach (var row in consumption)
        {
            var balance = balanceLookup.GetValueOrDefault(row.Key);
            var averageCost = balance is not null && balance.Quantity != 0m
                ? AccountingLedger.Money(balance.StockValue / balance.Quantity)
                : AccountingLedger.Money(latestCostLookup.GetValueOrDefault(row.Key));

            var cost = AccountingLedger.Money(row.Value * averageCost);
            db.StockMovements.Add(new StockMovement
            {
                RestaurantId = restaurantId,
                WarehouseId = row.Key.WarehouseId,
                ProductId = row.Key.ProductId,
                EmployeeId = employeeId,
                OperationId = order.Id,
                Type = "SALE_CONSUMPTION",
                QuantityDelta = -row.Value,
                UnitCost = averageCost,
                CostDelta = -cost,
                ReferenceType = "ORDER",
                ReferenceId = order.Id,
                Note = $"Заказ №{order.DisplayNumber}",
                CreatedAt = occurredAt
            });

            var inventoryAccountCode =
                AccountingLedger.NormalizeInventoryAccountCode(
                    products[row.Key.ProductId].InventoryAccountCode);
            var costKey = new CostBucketKey(row.Key.WarehouseId, inventoryAccountCode);
            warehouseCost[costKey] =
                warehouseCost.GetValueOrDefault(costKey) + cost;
        }

        var totalCost = AccountingLedger.Money(warehouseCost.Values.Sum());
        if (totalCost > 0m)
        {
            await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);
            var cogsAccount = await AccountingLedger.EnsureSystemAccountAsync(
                db,
                restaurantId,
                AccountingLedger.CostOfGoodsSoldKey,
                "701-3",
                "Satılmış malların (hazır məhsulun) balans dəyəri",
                LedgerAccountType.Expense,
                ct);

            var ledgerLines = new List<AccountingLedger.LineDraft>
            {
                new(cogsAccount, Debit: totalCost)
            };

            foreach (var row in warehouseCost.Where(x => x.Value > 0m))
            {
                var stockAccount = await AccountingLedger.EnsureInventoryAccountAsync(
                    db, restaurantId, row.Key.InventoryAccountCode, ct);
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    stockAccount,
                    Credit: AccountingLedger.Money(row.Value),
                    WarehouseId: row.Key.WarehouseId));
            }

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "ORDER_COGS",
                order.Id,
                occurredAt,
                $"Себестоимость заказа №{order.DisplayNumber}",
                employeeId,
                ledgerLines,
                ct);
        }

        return new ApplyResult(true, totalCost, null);
    }
}
