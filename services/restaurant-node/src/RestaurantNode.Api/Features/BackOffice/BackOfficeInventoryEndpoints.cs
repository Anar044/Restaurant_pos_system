using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeInventoryEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/inventory")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var warehouses = await db.Warehouses
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var items = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.TrackStock)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var movements = await db.StockMovements
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .ToListAsync(ct);

            var balances = await db.StockMovements
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .GroupBy(x => new { x.WarehouseId, x.ProductId })
                .Select(group => new
                {
                    group.Key.WarehouseId,
                    group.Key.ProductId,
                    Quantity = group.Sum(x => x.QuantityDelta)
                })
                .ToListAsync(ct);

            var balanceLookup = balances.ToDictionary(
                x => (x.WarehouseId, x.ProductId),
                x => x.Quantity);

            var warehouseLookup = warehouses.ToDictionary(x => x.Id, x => x.Name);
            var itemLookup = items.ToDictionary(x => x.Id, x => x);

            return Results.Ok(new
            {
                warehouses = warehouses.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    isActive = x.IsActive,
                    createdAt = x.CreatedAt
                }),
                items = items.Select(item => new
                {
                    id = item.Id,
                    name = item.Name,
                    sku = item.Sku,
                    type = item.Type,
                    unit = item.Unit,
                    minStock = item.MinStock,
                    isActive = item.IsActive,
                    createdAt = DateTimeOffset.MinValue,
                    totalStock = warehouses.Sum(warehouse =>
                        balanceLookup.GetValueOrDefault((warehouse.Id, item.Id))),
                    warehouseBalances = warehouses.Select(warehouse => new
                    {
                        warehouseId = warehouse.Id,
                        warehouseName = warehouse.Name,
                        quantity = balanceLookup.GetValueOrDefault((warehouse.Id, item.Id))
                    })
                }),
                recentMovements = movements.Select(movement => new
                {
                    id = movement.Id,
                    warehouseId = movement.WarehouseId,
                    warehouseName = warehouseLookup.GetValueOrDefault(movement.WarehouseId) ?? "Склад",
                    productId = movement.ProductId,
                    productName = itemLookup.TryGetValue(movement.ProductId, out var product)
                        ? product.Name
                        : "Номенклатура",
                    unit = itemLookup.TryGetValue(movement.ProductId, out var movementItem)
                        ? movementItem.Unit
                        : string.Empty,
                    employeeId = movement.EmployeeId,
                    operationId = movement.OperationId,
                    type = movement.Type,
                    quantityDelta = movement.QuantityDelta,
                    referenceType = movement.ReferenceType,
                    referenceId = movement.ReferenceId,
                    note = movement.Note,
                    createdAt = movement.CreatedAt
                })
            });
        });

        group.MapPost("/warehouses", async (
            CreateWarehouseRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var name = NormalizeRequired(request.Name, 120);
            if (name is null)
                return Results.BadRequest(new { message = "Название склада обязательно." });

            var duplicate = await db.Warehouses.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "Склад с таким названием уже существует." });

            var warehouse = new Warehouse
            {
                RestaurantId = restaurantId,
                Name = name,
                IsActive = true
            };

            db.Warehouses.Add(warehouse);
            AddAudit(db, user, restaurantId, "WAREHOUSE_CREATED", "Warehouse", warehouse.Id, new
            {
                warehouse.Name,
                warehouse.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/inventory/warehouses/{warehouse.Id}", new
            {
                id = warehouse.Id,
                warehouse.Name,
                warehouse.IsActive,
                warehouse.CreatedAt
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPut("/warehouses/{warehouseId:guid}", async (
            Guid warehouseId,
            UpdateWarehouseRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var warehouse = await db.Warehouses.FirstOrDefaultAsync(
                x => x.Id == warehouseId && x.RestaurantId == restaurantId,
                ct);
            if (warehouse is null)
                return Results.NotFound();

            var name = NormalizeRequired(request.Name, 120);
            if (name is null)
                return Results.BadRequest(new { message = "Название склада обязательно." });

            var duplicate = await db.Warehouses.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != warehouseId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "Склад с таким названием уже существует." });

            warehouse.Name = name;
            warehouse.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "WAREHOUSE_UPDATED", "Warehouse", warehouse.Id, new
            {
                warehouse.Name,
                warehouse.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = warehouse.Id,
                warehouse.Name,
                warehouse.IsActive,
                warehouse.CreatedAt
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/movements", async (
            CreateStockMovementRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId) ||
                !Guid.TryParse(user.FindFirstValue("employee_id"), out var employeeId))
            {
                return Results.Unauthorized();
            }

            var type = request.Type?.Trim().ToUpperInvariant();
            if (type is not ("RECEIPT" or "WRITE_OFF"))
                return Results.BadRequest(new { message = "Поддерживаются операции RECEIPT и WRITE_OFF." });
            if (request.Quantity <= 0 || request.Quantity > 1_000_000m)
                return Results.BadRequest(new { message = "Количество должно быть больше нуля." });

            var warehouseExists = await db.Warehouses.AnyAsync(
                x => x.Id == request.WarehouseId &&
                     x.RestaurantId == restaurantId &&
                     x.IsActive,
                ct);
            if (!warehouseExists)
                return Results.BadRequest(new { message = "Активный склад не найден." });

            var item = await db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.ProductId &&
                         x.RestaurantId == restaurantId &&
                         x.IsActive &&
                         x.TrackStock,
                    ct);
            if (item is null)
                return Results.BadRequest(new { message = "Позиция номенклатуры не найдена или складской учёт отключён." });

            var currentStock = await db.StockMovements
                .Where(x => x.RestaurantId == restaurantId &&
                            x.WarehouseId == request.WarehouseId &&
                            x.ProductId == request.ProductId)
                .SumAsync(x => (decimal?)x.QuantityDelta, ct) ?? 0m;

            var delta = type == "RECEIPT" ? request.Quantity : -request.Quantity;
            if (currentStock + delta < 0)
            {
                return Results.BadRequest(new
                {
                    message = $"Недостаточно остатка. Доступно: {currentStock:0.###} {item.Unit}."
                });
            }

            var operationId = Guid.NewGuid();
            var movement = new StockMovement
            {
                RestaurantId = restaurantId,
                WarehouseId = request.WarehouseId,
                ProductId = request.ProductId,
                EmployeeId = employeeId,
                OperationId = operationId,
                Type = type,
                QuantityDelta = delta,
                Note = NormalizeOptional(request.Note, 500)
            };

            db.StockMovements.Add(movement);
            AddAudit(db, user, restaurantId, "STOCK_MOVEMENT_CREATED", "Product", movement.ProductId, new
            {
                movement.WarehouseId,
                movement.ProductId,
                movement.Type,
                movement.QuantityDelta,
                movement.Note
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/inventory/movements/{movement.Id}", new
            {
                id = movement.Id,
                movement.OperationId,
                movement.WarehouseId,
                productId = movement.ProductId,
                movement.EmployeeId,
                movement.Type,
                movement.QuantityDelta,
                movement.Note,
                movement.CreatedAt
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/transfer", async (
            TransferStockRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId) ||
                !Guid.TryParse(user.FindFirstValue("employee_id"), out var employeeId))
            {
                return Results.Unauthorized();
            }

            if (request.FromWarehouseId == request.ToWarehouseId)
                return Results.BadRequest(new { message = "Склад-источник и склад-получатель должны отличаться." });

            var lines = NormalizeLines(request.Lines);
            if (lines.Error is not null) return lines.Error;

            var warehouseIds = new[] { request.FromWarehouseId, request.ToWarehouseId };
            var warehouses = await db.Warehouses
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive && warehouseIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToArrayAsync(ct);
            if (warehouses.Length != 2)
                return Results.BadRequest(new { message = "Один из складов не найден или отключён." });

            var productIds = lines.Lines!.Select(x => x.ProductId).ToArray();
            var products = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.TrackStock && productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, x.Unit })
                .ToDictionaryAsync(x => x.Id, ct);
            if (products.Count != productIds.Length)
                return Results.BadRequest(new { message = "Одна или несколько позиций не найдены или складской учёт у них отключён." });

            var sourceBalances = await db.StockMovements
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.WarehouseId == request.FromWarehouseId &&
                    productIds.Contains(x.ProductId))
                .GroupBy(x => x.ProductId)
                .Select(group => new { ProductId = group.Key, Quantity = group.Sum(x => x.QuantityDelta) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, ct);

            foreach (var line in lines.Lines)
            {
                var available = sourceBalances.GetValueOrDefault(line.ProductId);
                if (available < line.Quantity)
                {
                    var product = products[line.ProductId];
                    return Results.BadRequest(new
                    {
                        message = $"Недостаточно остатка '{product.Name}'. Доступно: {available:0.###} {product.Unit}."
                    });
                }
            }

            var operationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var note = NormalizeOptional(request.Note, 500);

            foreach (var line in lines.Lines)
            {
                db.StockMovements.AddRange(
                    new StockMovement
                    {
                        RestaurantId = restaurantId,
                        WarehouseId = request.FromWarehouseId,
                        ProductId = line.ProductId,
                        EmployeeId = employeeId,
                        OperationId = operationId,
                        Type = "TRANSFER_OUT",
                        QuantityDelta = -line.Quantity,
                        ReferenceType = "TRANSFER",
                        ReferenceId = operationId,
                        Note = note,
                        CreatedAt = now
                    },
                    new StockMovement
                    {
                        RestaurantId = restaurantId,
                        WarehouseId = request.ToWarehouseId,
                        ProductId = line.ProductId,
                        EmployeeId = employeeId,
                        OperationId = operationId,
                        Type = "TRANSFER_IN",
                        QuantityDelta = line.Quantity,
                        ReferenceType = "TRANSFER",
                        ReferenceId = operationId,
                        Note = note,
                        CreatedAt = now
                    });
            }

            AddAudit(db, user, restaurantId, "STOCK_TRANSFER_CREATED", "StockOperation", operationId, new
            {
                operationId,
                request.FromWarehouseId,
                request.ToWarehouseId,
                lines = lines.Lines.Select(x => new { x.ProductId, x.Quantity }),
                note
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { operationId, lineCount = lines.Lines.Count });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/inventory-count", async (
            InventoryCountRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId) ||
                !Guid.TryParse(user.FindFirstValue("employee_id"), out var employeeId))
            {
                return Results.Unauthorized();
            }

            var lines = NormalizeCountLines(request.Lines);
            if (lines.Error is not null) return lines.Error;

            var warehouseExists = await db.Warehouses.AnyAsync(x =>
                x.Id == request.WarehouseId &&
                x.RestaurantId == restaurantId &&
                x.IsActive, ct);
            if (!warehouseExists)
                return Results.BadRequest(new { message = "Активный склад не найден." });

            var productIds = lines.Lines!.Select(x => x.ProductId).ToArray();
            var products = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.TrackStock && productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, x.Unit })
                .ToDictionaryAsync(x => x.Id, ct);
            if (products.Count != productIds.Length)
                return Results.BadRequest(new { message = "Одна или несколько позиций не найдены или складской учёт у них отключён." });

            var balances = await db.StockMovements
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.WarehouseId == request.WarehouseId &&
                    productIds.Contains(x.ProductId))
                .GroupBy(x => x.ProductId)
                .Select(group => new { ProductId = group.Key, Quantity = group.Sum(x => x.QuantityDelta) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, ct);

            var operationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var note = NormalizeOptional(request.Note, 500);
            var adjusted = 0;

            foreach (var line in lines.Lines)
            {
                var current = balances.GetValueOrDefault(line.ProductId);
                var delta = line.CountedQuantity - current;
                if (delta == 0m) continue;

                db.StockMovements.Add(new StockMovement
                {
                    RestaurantId = restaurantId,
                    WarehouseId = request.WarehouseId,
                    ProductId = line.ProductId,
                    EmployeeId = employeeId,
                    OperationId = operationId,
                    Type = delta > 0 ? "INVENTORY_GAIN" : "INVENTORY_LOSS",
                    QuantityDelta = delta,
                    ReferenceType = "INVENTORY",
                    ReferenceId = operationId,
                    Note = note,
                    CreatedAt = now
                });
                adjusted++;
            }

            AddAudit(db, user, restaurantId, "STOCK_INVENTORY_COUNTED", "StockOperation", operationId, new
            {
                operationId,
                request.WarehouseId,
                adjusted,
                lines = lines.Lines.Select(x => new
                {
                    x.ProductId,
                    currentQuantity = balances.GetValueOrDefault(x.ProductId),
                    x.CountedQuantity
                }),
                note
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { operationId, lineCount = lines.Lines.Count, adjustedCount = adjusted });
        }).RequireAuthorization(Permissions.InventoryManage);

        return app;
    }

    private sealed record NormalizedLines(List<StockOperationLineRequest>? Lines, IResult? Error);
    private sealed record NormalizedCountLines(List<InventoryCountLineRequest>? Lines, IResult? Error);

    private static NormalizedLines NormalizeLines(IReadOnlyList<StockOperationLineRequest>? source)
    {
        var lines = (source ?? [])
            .Where(x => x.ProductId != Guid.Empty)
            .GroupBy(x => x.ProductId)
            .Select(group => new StockOperationLineRequest(group.Key, group.Sum(x => x.Quantity)))
            .ToList();

        if (lines.Count == 0)
            return new(null, Results.BadRequest(new { message = "Добавьте хотя бы одну позицию." }));
        if (lines.Any(x => x.Quantity <= 0 || x.Quantity > 1_000_000m))
            return new(null, Results.BadRequest(new { message = "Количество каждой позиции должно быть больше нуля." }));

        return new(lines, null);
    }

    private static NormalizedCountLines NormalizeCountLines(IReadOnlyList<InventoryCountLineRequest>? source)
    {
        var rows = (source ?? [])
            .Where(x => x.ProductId != Guid.Empty)
            .ToList();

        if (rows.Count == 0)
            return new(null, Results.BadRequest(new { message = "Добавьте хотя бы одну позицию." }));
        if (rows.GroupBy(x => x.ProductId).Any(group => group.Count() > 1))
            return new(null, Results.BadRequest(new { message = "Одна позиция не может повторяться в инвентаризации." }));
        if (rows.Any(x => x.CountedQuantity < 0 || x.CountedQuantity > 1_000_000m))
            return new(null, Results.BadRequest(new { message = "Фактический остаток не может быть отрицательным." }));

        return new(rows, null);
    }

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static string? NormalizeRequired(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength
            ? null
            : normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

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

public sealed record CreateWarehouseRequest(string Name);
public sealed record UpdateWarehouseRequest(string Name, bool IsActive);
public sealed record CreateStockMovementRequest(
    Guid WarehouseId,
    Guid ProductId,
    string Type,
    decimal Quantity,
    string? Note);
public sealed record StockOperationLineRequest(Guid ProductId, decimal Quantity);
public sealed record TransferStockRequest(
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    IReadOnlyList<StockOperationLineRequest>? Lines,
    string? Note);
public sealed record InventoryCountLineRequest(Guid ProductId, decimal CountedQuantity);
public sealed record InventoryCountRequest(
    Guid WarehouseId,
    IReadOnlyList<InventoryCountLineRequest>? Lines,
    string? Note);
