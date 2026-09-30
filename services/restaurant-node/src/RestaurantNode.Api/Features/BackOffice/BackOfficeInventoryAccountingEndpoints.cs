using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeInventoryAccountingEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeInventoryAccountingEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/inventory-accounting")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var restaurant = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new
                {
                    x.InventoryCostMethod,
                    x.AllowNegativeRealization
                })
                .FirstOrDefaultAsync(ct);

            if (restaurant is null)
                return Results.NotFound();

            var warehouses = await db.Warehouses
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            var products = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .Select(x => new { x.Id, x.Name, x.Unit })
                .ToDictionaryAsync(x => x.Id, ct);

            var shifts = await (
                from shift in db.Shifts.AsNoTracking()
                join device in db.Devices.AsNoTracking()
                    on shift.DeviceId equals device.Id
                where shift.RestaurantId == restaurantId
                select new
                {
                    shift.Id,
                    shift.DeviceId,
                    DeviceName = device.Name,
                    shift.OpenedAt,
                    shift.ClosedAt
                })
                .ToDictionaryAsync(x => x.Id, ct);

            var acts = await db.StockDocuments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Realization)
                .Include(x => x.Lines)
                .OrderByDescending(x => x.DocumentDate)
                .ThenByDescending(x => x.CreatedAt)
                .Take(300)
                .ToListAsync(ct);

            var negativeBalances = await db.StockMovements
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .GroupBy(x => new { x.WarehouseId, x.ProductId })
                .Select(group => new
                {
                    group.Key.WarehouseId,
                    group.Key.ProductId,
                    Quantity = group.Sum(x => x.QuantityDelta),
                    StockValue = group.Sum(x => x.CostDelta ?? 0m)
                })
                .Where(x => x.Quantity < 0m)
                .OrderBy(x => x.WarehouseId)
                .ThenBy(x => x.ProductId)
                .Take(100)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                settings = new
                {
                    costMethod = EnumText(restaurant.InventoryCostMethod),
                    allowNegativeRealization =
                        restaurant.InventoryCostMethod == InventoryCostMethod.WeightedAverage &&
                        restaurant.AllowNegativeRealization
                },
                costMethods = new[]
                {
                    new
                    {
                        code = "WEIGHTED_AVERAGE",
                        name = "Средневзвешенная",
                        description = "Продажи могут уходить в минус. Себестоимость отрицательного остатка корректируется после последующего прихода."
                    },
                    new
                    {
                        code = "FIFO",
                        name = "FIFO",
                        description = "Сначала списываются самые ранние приходные партии. Акт не проводится, если партий недостаточно."
                    }
                },
                negativeBalances = negativeBalances.Select(row => new
                {
                    row.WarehouseId,
                    warehouseName = warehouses.GetValueOrDefault(row.WarehouseId),
                    row.ProductId,
                    productName = products.GetValueOrDefault(row.ProductId)?.Name ?? "Номенклатура",
                    unit = products.GetValueOrDefault(row.ProductId)?.Unit ?? "pcs",
                    row.Quantity,
                    row.StockValue
                }),
                acts = acts.Select(act =>
                {
                    var shift = act.ReferenceType == "SHIFT" &&
                                act.ReferenceId.HasValue &&
                                shifts.TryGetValue(act.ReferenceId.Value, out var shiftInfo)
                        ? shiftInfo
                        : null;

                    return new
                    {
                        id = act.Id,
                        act.Number,
                        status = EnumText(act.Status),
                        act.DocumentDate,
                        act.WarehouseId,
                        warehouseName = act.WarehouseId.HasValue
                            ? warehouses.GetValueOrDefault(act.WarehouseId.Value)
                            : null,
                        shiftId = act.ReferenceType == "SHIFT" ? act.ReferenceId : null,
                        deviceId = shift?.DeviceId,
                        deviceName = shift?.DeviceName,
                        shiftOpenedAt = shift?.OpenedAt,
                        shiftClosedAt = shift?.ClosedAt,
                        costMethod = NormalizeCostMethodCode(act.CostMethodSnapshot),
                        act.PostingError,
                        act.TotalAmount,
                        act.CreatedAt,
                        act.PostedAt,
                        lines = act.Lines
                            .OrderBy(x => products.GetValueOrDefault(x.ProductId)?.Name)
                            .Select(line => new
                            {
                                id = line.Id,
                                line.ProductId,
                                productName = products.GetValueOrDefault(line.ProductId)?.Name ?? "Номенклатура",
                                unit = products.GetValueOrDefault(line.ProductId)?.Unit ?? "pcs",
                                line.Quantity,
                                unitCost = line.UnitPrice,
                                cost = line.InventoryCostAmount
                            })
                    };
                })
            });
        });

        group.MapPut("/settings", async (
            UpdateInventoryAccountingSettingsRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            if (!TryParseCostMethod(request.CostMethod, out var method))
            {
                return Results.BadRequest(new
                {
                    message = "Метод себестоимости должен быть WEIGHTED_AVERAGE или FIFO."
                });
            }

            if (method == InventoryCostMethod.Fifo)
            {
                var negatives = await db.StockMovements
                    .AsNoTracking()
                    .Where(x => x.RestaurantId == restaurantId)
                    .GroupBy(x => new { x.WarehouseId, x.ProductId })
                    .Select(group => new
                    {
                        group.Key.WarehouseId,
                        group.Key.ProductId,
                        Quantity = group.Sum(x => x.QuantityDelta)
                    })
                    .Where(x => x.Quantity < 0m)
                    .Take(20)
                    .ToListAsync(ct);

                if (negatives.Count > 0)
                {
                    return Results.Conflict(new
                    {
                        code = "NEGATIVE_STOCK_BLOCKS_FIFO",
                        message = "Нельзя включить FIFO, пока есть отрицательные остатки. Сначала оформите приходы или инвентаризацию.",
                        negativeCount = negatives.Count
                    });
                }
            }

            var restaurant = await db.Restaurants
                .FirstOrDefaultAsync(x => x.Id == restaurantId, ct);
            if (restaurant is null)
                return Results.NotFound();

            restaurant.InventoryCostMethod = method;
            restaurant.AllowNegativeRealization =
                method == InventoryCostMethod.WeightedAverage &&
                request.AllowNegativeRealization;

            AddAudit(
                db,
                user,
                restaurantId,
                "INVENTORY_ACCOUNTING_SETTINGS_UPDATED",
                "Restaurant",
                restaurant.Id,
                new
                {
                    costMethod = EnumText(restaurant.InventoryCostMethod),
                    restaurant.AllowNegativeRealization
                });

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                costMethod = EnumText(restaurant.InventoryCostMethod),
                allowNegativeRealization = restaurant.AllowNegativeRealization
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/acts/{id:guid}/post", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var act = await db.StockDocuments
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Realization,
                    ct);

            if (act is null)
                return Results.NotFound();

            if (act.Status == StockDocumentStatus.Posted)
            {
                return Results.Ok(new
                {
                    id = act.Id,
                    status = EnumText(act.Status),
                    act.TotalAmount,
                    act.PostedAt
                });
            }

            if (act.Status != StockDocumentStatus.Draft)
                return Results.Conflict(new { message = "Можно провести только черновик акта реализации." });

            var settings = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new
                {
                    x.InventoryCostMethod,
                    x.AllowNegativeRealization
                })
                .FirstAsync(ct);

            act.CostMethodSnapshot = settings.InventoryCostMethod.ToString();

            var result = await RealizationActAccounting.TryPostActAsync(
                db,
                act,
                restaurantId,
                employeeId,
                settings.InventoryCostMethod,
                settings.AllowNegativeRealization,
                ct);

            if (!result.Posted)
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Results.Conflict(new
                {
                    code = "REALIZATION_ACT_PENDING",
                    message = result.Error ?? "Акт пока нельзя провести.",
                    id = act.Id,
                    status = EnumText(act.Status)
                });
            }

            AddAudit(
                db,
                user,
                restaurantId,
                "REALIZATION_ACT_POSTED",
                "StockDocument",
                act.Id,
                new
                {
                    act.Number,
                    act.ReferenceId,
                    costMethod = EnumText(settings.InventoryCostMethod),
                    totalCost = result.TotalCost
                });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                id = act.Id,
                status = EnumText(act.Status),
                totalCost = result.TotalCost,
                act.PostedAt
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/acts/retry-pending", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var settings = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new
                {
                    x.InventoryCostMethod,
                    x.AllowNegativeRealization
                })
                .FirstAsync(ct);

            var acts = await db.StockDocuments
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Realization &&
                    x.Status == StockDocumentStatus.Draft)
                .Include(x => x.Lines)
                .OrderBy(x => x.DocumentDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);

            var posted = 0;
            var pending = 0;
            var totalCost = 0m;

            foreach (var act in acts)
            {
                act.CostMethodSnapshot = settings.InventoryCostMethod.ToString();

                var result = await RealizationActAccounting.TryPostActAsync(
                    db,
                    act,
                    restaurantId,
                    employeeId,
                    settings.InventoryCostMethod,
                    settings.AllowNegativeRealization,
                    ct);

                if (result.Posted)
                {
                    posted++;
                    totalCost += result.TotalCost;
                    AddAudit(
                        db,
                        user,
                        restaurantId,
                        "REALIZATION_ACT_POSTED",
                        "StockDocument",
                        act.Id,
                        new
                        {
                            act.Number,
                            act.ReferenceId,
                            retry = true,
                            costMethod = EnumText(settings.InventoryCostMethod),
                            totalCost = result.TotalCost
                        });
                }
                else
                {
                    pending++;
                }

                // Make each result visible before the next FIFO act is evaluated.
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                checkedActs = acts.Count,
                postedActs = posted,
                pendingActs = pending,
                totalCost = AccountingLedger.Money(totalCost)
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        return app;
    }

    private static bool TryParseCostMethod(
        string? value,
        out InventoryCostMethod method)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (normalized == "WEIGHTED_AVERAGE")
        {
            method = InventoryCostMethod.WeightedAverage;
            return true;
        }

        if (normalized == "FIFO")
        {
            method = InventoryCostMethod.Fifo;
            return true;
        }

        method = default;
        return false;
    }

    private static string NormalizeCostMethodCode(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "FIFO" => "FIFO",
            "WEIGHTEDAVERAGE" or "WEIGHTED_AVERAGE" => "WEIGHTED_AVERAGE",
            _ => "WEIGHTED_AVERAGE"
        };

    private static string EnumText<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        System.Text.RegularExpressions.Regex.Replace(
            value.ToString(),
            "([a-z0-9])([A-Z])",
            "$1_$2").ToUpperInvariant();

    private static bool TryClaims(
        ClaimsPrincipal user,
        out Guid restaurantId,
        out Guid employeeId)
    {
        var okRestaurant = Guid.TryParse(
            user.FindFirstValue("restaurant_id"),
            out restaurantId);
        var okEmployee = Guid.TryParse(
            user.FindFirstValue("employee_id"),
            out employeeId);
        return okRestaurant && okEmployee;
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
        Guid? employeeId = Guid.TryParse(
            user.FindFirstValue("employee_id"),
            out var parsedEmployeeId)
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

public sealed record UpdateInventoryAccountingSettingsRequest(
    string CostMethod,
    bool AllowNegativeRealization);
