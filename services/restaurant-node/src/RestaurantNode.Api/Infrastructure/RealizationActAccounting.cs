using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public static class RealizationActAccounting
{
    public sealed record ShiftResult(
        int CreatedActs,
        int PostedActs,
        int PendingActs,
        decimal TotalCost);

    private sealed record ProductInfo(
        Guid Id,
        string Name,
        bool TrackStock,
        Guid? PreparationPlaceTypeId,
        string? InventoryAccountCode);

    private sealed record ConsumptionKey(Guid WarehouseId, Guid ProductId);
    private sealed record CostBucketKey(Guid WarehouseId, string InventoryAccountCode);
    private sealed record BalanceInfo(decimal Quantity, decimal StockValue);
    private sealed record FifoLayer(decimal Quantity, decimal UnitCost);

    public static async Task<ShiftResult> CreateAndPostForShiftAsync(
        RestaurantDbContext db,
        Shift shift,
        Guid restaurantId,
        Guid employeeId,
        CancellationToken ct)
    {
        var restaurant = await db.Restaurants
            .AsNoTracking()
            .Where(x => x.Id == restaurantId)
            .Select(x => new
            {
                x.InventoryCostMethod,
                x.AllowNegativeRealization
            })
            .FirstAsync(ct);

        // A cash shift owns the sale through its completed payments, not through
        // the shift in which the order happened to be opened. Orders may stay open
        // across shifts, so using OpenedShiftId can make a sale appear in the Z report
        // while silently missing from realization.
        var soldOrderIds = await db.Payments
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.ShiftId == shift.Id &&
                (x.Status == PaymentStatus.Completed ||
                 x.Status == PaymentStatus.Refunded))
            .Select(x => x.OrderId)
            .Distinct()
            .ToArrayAsync(ct);

        if (soldOrderIds.Length == 0)
            return new ShiftResult(0, 0, 0, 0m);

        var orders = await db.Orders
            .AsNoTracking()
            .Include(x => x.Items)
                .ThenInclude(x => x.Modifiers)
            .Where(x =>
                x.RestaurantId == restaurantId &&
                soldOrderIds.Contains(x.Id) &&
                (x.Status == OrderStatus.Closed || x.Status == OrderStatus.Paid))
            .ToListAsync(ct);

        if (orders.Count == 0)
            return new ShiftResult(0, 0, 0, 0m);

        var orderIds = orders.Select(x => x.Id).ToArray();
        var alreadyAccountedOrderIds = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.ReferenceType == "ORDER" &&
                x.ReferenceId.HasValue &&
                orderIds.Contains(x.ReferenceId.Value))
            .Select(x => x.ReferenceId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var legacyAccounted = alreadyAccountedOrderIds.ToHashSet();
        orders = orders.Where(x => !legacyAccounted.Contains(x.Id)).ToList();
        if (orders.Count == 0)
            return new ShiftResult(0, 0, 0, 0m);

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
            .ToDictionary(group => group.Key, group => group.ToArray());

        var groupId = await db.RestaurantGroupDevices
            .AsNoTracking()
            .Where(x => x.DeviceId == shift.DeviceId)
            .Select(x => (Guid?)x.GroupId)
            .FirstOrDefaultAsync(ct);

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

        void AddConsumption(Guid productId, decimal quantity, Guid? inheritedPreparationType, int depth)
        {
            if (quantity <= 0m || depth > 12)
                return;
            if (!products.TryGetValue(productId, out var product))
                return;

            var routeType = product.PreparationPlaceTypeId ?? inheritedPreparationType;
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

        foreach (var order in orders)
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

                    AddConsumption(
                        modifier.ModifierId,
                        item.Quantity * modifier.Quantity,
                        modifierProduct.PreparationPlaceTypeId ?? product.PreparationPlaceTypeId,
                        0);
                }
            }
        }

        if (consumption.Count == 0)
            return new ShiftResult(0, 0, 0, 0m);

        var existingActs = await db.StockDocuments
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.Type == StockDocumentType.Realization &&
                x.ReferenceType == "SHIFT" &&
                x.ReferenceId == shift.Id)
            .Include(x => x.Lines)
            .ToListAsync(ct);

        var existingByWarehouse = existingActs
            .Where(x => x.WarehouseId.HasValue)
            .ToDictionary(x => x.WarehouseId!.Value);

        var created = 0;
        foreach (var warehouseGroup in consumption.GroupBy(x => x.Key.WarehouseId))
        {
            if (existingByWarehouse.ContainsKey(warehouseGroup.Key))
                continue;

            var act = new StockDocument
            {
                RestaurantId = restaurantId,
                Type = StockDocumentType.Realization,
                Status = StockDocumentStatus.Draft,
                Number = CreateNumber("AR", shift.ClosedAt ?? DateTimeOffset.UtcNow),
                DocumentDate = shift.ClosedAt ?? DateTimeOffset.UtcNow,
                WarehouseId = warehouseGroup.Key,
                PurchaseSource = "POS_SHIFT",
                PurchaseDocumentKind = "REALIZATION",
                TaxRegimeSnapshot = "NOT_APPLICABLE",
                VatPriceMode = "INCLUDED",
                InputVatCreditStatus = "NOT_APPLICABLE",
                ReferenceType = "SHIFT",
                ReferenceId = shift.Id,
                CostMethodSnapshot = restaurant.InventoryCostMethod.ToString(),
                CreatedByEmployeeId = employeeId,
                Comment = $"Акт реализации по кассовой смене {shift.Id}"
            };

            foreach (var row in warehouseGroup.OrderBy(x => products[x.Key.ProductId].Name))
            {
                act.Lines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    ProductId = row.Key.ProductId,
                    Quantity = decimal.Round(row.Value, 3, MidpointRounding.AwayFromZero),
                    UnitPrice = 0m,
                    VatTaxCode = "NO_VAT",
                    NetAmount = 0m,
                    VatAmount = 0m,
                    InventoryCostAmount = 0m,
                    Amount = 0m
                });
            }

            db.StockDocuments.Add(act);
            existingActs.Add(act);
            existingByWarehouse[warehouseGroup.Key] = act;
            created++;
        }

        var posted = 0;
        var pending = 0;
        var totalCost = 0m;

        foreach (var act in existingActs.Where(x => x.Status == StockDocumentStatus.Draft))
        {
            var result = await TryPostActAsync(
                db,
                act,
                restaurantId,
                employeeId,
                restaurant.InventoryCostMethod,
                restaurant.AllowNegativeRealization,
                products,
                ct);

            if (result.Posted)
            {
                posted++;
                totalCost += result.TotalCost;
            }
            else
            {
                pending++;
            }
        }

        return new ShiftResult(
            created,
            posted,
            pending,
            AccountingLedger.Money(totalCost));
    }

    public static async Task<(bool Posted, decimal TotalCost, string? Error)> TryPostActAsync(
        RestaurantDbContext db,
        StockDocument act,
        Guid restaurantId,
        Guid employeeId,
        InventoryCostMethod method,
        bool allowNegativeRealization,
        CancellationToken ct)
    {
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

        return await TryPostActAsync(
            db,
            act,
            restaurantId,
            employeeId,
            method,
            allowNegativeRealization,
            products,
            ct);
    }

    private static async Task<(bool Posted, decimal TotalCost, string? Error)> TryPostActAsync(
        RestaurantDbContext db,
        StockDocument act,
        Guid restaurantId,
        Guid employeeId,
        InventoryCostMethod method,
        bool allowNegativeRealization,
        IReadOnlyDictionary<Guid, ProductInfo> products,
        CancellationToken ct)
    {
        if (act.Status == StockDocumentStatus.Posted)
            return (true, act.TotalAmount, null);
        if (act.Type != StockDocumentType.Realization || !act.WarehouseId.HasValue)
            return (false, 0m, "Некорректный акт реализации.");

        if (act.Lines.Count == 0)
        {
            act.PostingError = "В акте реализации нет позиций.";
            return (false, 0m, act.PostingError);
        }

        var olderPendingAct = await db.StockDocuments
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.Type == StockDocumentType.Realization &&
                x.Status == StockDocumentStatus.Draft &&
                x.WarehouseId == act.WarehouseId &&
                x.Id != act.Id &&
                (x.DocumentDate < act.DocumentDate ||
                 (x.DocumentDate == act.DocumentDate && x.CreatedAt < act.CreatedAt)))
            .OrderBy(x => x.DocumentDate)
            .ThenBy(x => x.CreatedAt)
            .Select(x => x.Number)
            .FirstOrDefaultAsync(ct);

        if (olderPendingAct is not null)
        {
            act.PostingError =
                $"Сначала нужно провести более ранний акт {olderPendingAct} по этому складу.";
            return (false, 0m, act.PostingError);
        }

        var productIds = act.Lines.Select(x => x.ProductId).Distinct().ToArray();
        var balances = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.WarehouseId == act.WarehouseId.Value &&
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

        if (method == InventoryCostMethod.Fifo || !allowNegativeRealization)
        {
            foreach (var line in act.Lines)
            {
                var available = balances.GetValueOrDefault(line.ProductId)?.Quantity ?? 0m;
                if (available < line.Quantity)
                {
                    var productName = products.GetValueOrDefault(line.ProductId)?.Name ?? "Номенклатура";
                    act.PostingError =
                        $"Недостаточно остатка '{productName}'. Нужно {line.Quantity:0.###}, доступно {available:0.###}. " +
                        "Введите приходные документы и повторите проведение акта.";
                    return (false, 0m, act.PostingError);
                }
            }
        }

        var latestCosts = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.WarehouseId == act.WarehouseId.Value &&
                productIds.Contains(x.ProductId) &&
                x.UnitCost.HasValue &&
                x.UnitCost.Value > 0m)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.ProductId, UnitCost = x.UnitCost!.Value })
            .ToListAsync(ct);
        var latestCostLookup = latestCosts
            .GroupBy(x => x.ProductId)
            .ToDictionary(group => group.Key, group => group.First().UnitCost);

        var warehouseCost = new Dictionary<CostBucketKey, decimal>();
        var totalCost = 0m;

        foreach (var line in act.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product) ||
                !AccountingLedger.IsSupportedInventoryAccountCode(product.InventoryAccountCode))
            {
                act.PostingError = "Для одной из позиций не настроен корректный счёт складского учёта.";
                return (false, 0m, act.PostingError);
            }

            decimal unitCost;
            decimal cost;

            if (method == InventoryCostMethod.Fifo)
            {
                var fifo = await CalculateFifoCostAsync(
                    db,
                    restaurantId,
                    act.WarehouseId.Value,
                    line.ProductId,
                    line.Quantity,
                    act.DocumentDate,
                    ct);

                if (!fifo.HasValue)
                {
                    act.PostingError =
                        $"Не удалось подобрать FIFO-партии для '{product.Name}'. " +
                        "Введите недостающие приходы и повторите проведение акта.";
                    return (false, 0m, act.PostingError);
                }

                cost = AccountingLedger.Money(fifo.Value);
                unitCost = line.Quantity == 0m
                    ? 0m
                    : AccountingLedger.Money(cost / line.Quantity);
            }
            else
            {
                var balance = balances.GetValueOrDefault(line.ProductId);
                unitCost = balance is not null && balance.Quantity != 0m
                    ? AccountingLedger.Money(balance.StockValue / balance.Quantity)
                    : AccountingLedger.Money(latestCostLookup.GetValueOrDefault(line.ProductId));
                cost = AccountingLedger.Money(line.Quantity * unitCost);
            }

            line.UnitPrice = unitCost;
            line.NetAmount = cost;
            line.InventoryCostAmount = cost;
            line.Amount = cost;

            db.StockMovements.Add(new StockMovement
            {
                RestaurantId = restaurantId,
                WarehouseId = act.WarehouseId.Value,
                ProductId = line.ProductId,
                EmployeeId = employeeId,
                OperationId = act.Id,
                Type = "REALIZATION",
                QuantityDelta = -line.Quantity,
                UnitCost = unitCost,
                CostDelta = -cost,
                ReferenceType = "STOCK_DOCUMENT",
                ReferenceId = act.Id,
                Note = act.Comment,
                CreatedAt = act.DocumentDate
            });

            var costKey = new CostBucketKey(
                act.WarehouseId.Value,
                AccountingLedger.NormalizeInventoryAccountCode(product.InventoryAccountCode));
            warehouseCost[costKey] = warehouseCost.GetValueOrDefault(costKey) + cost;
            totalCost += cost;
        }

        totalCost = AccountingLedger.Money(totalCost);
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
                    db,
                    restaurantId,
                    row.Key.InventoryAccountCode,
                    ct);
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    stockAccount,
                    Credit: AccountingLedger.Money(row.Value),
                    WarehouseId: row.Key.WarehouseId));
            }

            await AccountingLedger.PostAsync(
                db,
                restaurantId,
                "REALIZATION_ACT",
                act.Id,
                act.DocumentDate,
                $"Акт реализации {act.Number}",
                employeeId,
                ledgerLines,
                ct);
        }

        act.NetAmount = totalCost;
        act.InventoryCostAmount = totalCost;
        act.TotalAmount = totalCost;
        act.Status = StockDocumentStatus.Posted;
        act.PostedByEmployeeId = employeeId;
        act.PostedAt = DateTimeOffset.UtcNow;
        act.PostingError = null;

        return (true, totalCost, null);
    }

    private static async Task<decimal?> CalculateFifoCostAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid warehouseId,
        Guid productId,
        decimal requestedQuantity,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var movements = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.WarehouseId == warehouseId &&
                x.ProductId == productId &&
                x.CreatedAt <= occurredAt)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.QuantityDelta,
                x.UnitCost,
                x.CostDelta
            })
            .ToListAsync(ct);

        var layers = new Queue<FifoLayer>();

        void AddLayer(decimal quantity, decimal unitCost)
        {
            if (quantity > 0m)
                layers.Enqueue(new FifoLayer(quantity, unitCost));
        }

        bool Consume(decimal quantity, out decimal cost)
        {
            cost = 0m;
            var remaining = quantity;

            while (remaining > 0m)
            {
                if (layers.Count == 0)
                    return false;

                var layer = layers.Dequeue();
                var take = Math.Min(layer.Quantity, remaining);
                cost += take * layer.UnitCost;
                remaining -= take;

                var layerRemainder = layer.Quantity - take;
                if (layerRemainder > 0m)
                {
                    var rest = layers.ToArray();
                    layers.Clear();
                    layers.Enqueue(new FifoLayer(layerRemainder, layer.UnitCost));
                    foreach (var row in rest)
                        layers.Enqueue(row);
                }
            }

            return true;
        }

        foreach (var movement in movements)
        {
            if (movement.QuantityDelta > 0m)
            {
                var unitCost = movement.UnitCost ??
                    (movement.CostDelta.HasValue && movement.QuantityDelta != 0m
                        ? AccountingLedger.Money(movement.CostDelta.Value / movement.QuantityDelta)
                        : 0m);
                AddLayer(movement.QuantityDelta, unitCost);
            }
            else if (movement.QuantityDelta < 0m)
            {
                if (!Consume(-movement.QuantityDelta, out _))
                    return null;
            }
        }

        if (!Consume(requestedQuantity, out var result))
            return null;

        return AccountingLedger.Money(result);
    }

    private static string CreateNumber(string prefix, DateTimeOffset now) =>
        $"{prefix}-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
}
