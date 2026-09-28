using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeRoutingEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeRoutingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/routing")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();

            var types = await db.PreparationPlaceTypes.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive })
                .ToListAsync(ct);

            var places = await db.PreparationPlaces.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    preparationPlaceTypeId = x.PreparationPlaceTypeId,
                    preparationPlaceTypeName = db.PreparationPlaceTypes.Where(t => t.Id == x.PreparationPlaceTypeId).Select(t => t.Name).FirstOrDefault(),
                    printerId = x.PrinterId,
                    printerName = x.PrinterId == null ? null : db.Printers.Where(p => p.Id == x.PrinterId).Select(p => p.Name).FirstOrDefault(),
                    warehouseId = x.WarehouseId,
                    warehouseName = x.WarehouseId == null ? null : db.Warehouses.Where(w => w.Id == x.WarehouseId).Select(w => w.Name).FirstOrDefault(),
                    isActive = x.IsActive
                })
                .ToListAsync(ct);

            var salesPoints = await db.SalesPoints.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    type = x.Type,
                    hallId = x.HallId,
                    hallName = x.HallId == null ? null : db.Halls.Where(h => h.Id == x.HallId).Select(h => h.Name).FirstOrDefault(),
                    isActive = x.IsActive
                })
                .ToListAsync(ct);

            var routes = await db.PreparationRoutes.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.SalesPoint!.Name).ThenBy(x => x.PreparationPlaceType!.Name)
                .Select(x => new
                {
                    id = x.Id,
                    salesPointId = x.SalesPointId,
                    salesPointName = x.SalesPoint!.Name,
                    preparationPlaceTypeId = x.PreparationPlaceTypeId,
                    preparationPlaceTypeName = x.PreparationPlaceType!.Name,
                    preparationPlaceId = x.PreparationPlaceId,
                    preparationPlaceName = x.PreparationPlace!.Name,
                    isActive = x.IsActive
                })
                .ToListAsync(ct);

            var printers = await db.Printers.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.IsConfigured)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToListAsync(ct);

            var warehouses = await db.Warehouses.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToListAsync(ct);

            var halls = await db.Halls.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToListAsync(ct);

            return Results.Ok(new { types, places, salesPoints, routes, printers, warehouses, halls });
        });

        group.MapPost("/types", async (NameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var name = Normalize(request.Name);
            if (name is null) return Results.BadRequest(new { message = "Название обязательно." });
            if (await db.PreparationPlaceTypes.AnyAsync(x => x.RestaurantId == restaurantId && x.Name == name, ct))
                return Results.Conflict(new { message = "Такой тип уже существует." });

            var entity = new PreparationPlaceType { RestaurantId = restaurantId, Name = name };
            db.PreparationPlaceTypes.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/routing/types/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/types/{id:guid}", async (Guid id, UpdateNameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity = await db.PreparationPlaceTypes.FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (entity is null) return Results.NotFound();
            var name = Normalize(request.Name);
            if (name is null) return Results.BadRequest(new { message = "Название обязательно." });
            entity.Name = name;
            entity.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPost("/places", async (UpsertPlaceRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var validation = await ValidatePlace(db, restaurantId, request, ct);
            if (validation is not null) return validation;
            var entity = new PreparationPlace
            {
                RestaurantId = restaurantId,
                Name = request.Name.Trim(),
                PreparationPlaceTypeId = request.PreparationPlaceTypeId,
                PrinterId = request.PrinterId,
                WarehouseId = request.WarehouseId
            };
            db.PreparationPlaces.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/routing/places/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/places/{id:guid}", async (Guid id, UpsertPlaceRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity = await db.PreparationPlaces.FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (entity is null) return Results.NotFound();
            var validation = await ValidatePlace(db, restaurantId, request, ct);
            if (validation is not null) return validation;
            entity.Name = request.Name.Trim();
            entity.PreparationPlaceTypeId = request.PreparationPlaceTypeId;
            entity.PrinterId = request.PrinterId;
            entity.WarehouseId = request.WarehouseId;
            entity.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPost("/sales-points", async (UpsertSalesPointRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var name = Normalize(request.Name);
            if (name is null) return Results.BadRequest(new { message = "Название обязательно." });
            var type = NormalizeSalesType(request.Type);
            if (type is null) return Results.BadRequest(new { message = "Неверный тип места продажи." });
            if (request.HallId.HasValue && !await db.Halls.AnyAsync(x => x.Id == request.HallId && x.RestaurantId == restaurantId, ct))
                return Results.BadRequest(new { message = "Зал не найден." });

            var entity = new SalesPoint { RestaurantId = restaurantId, Name = name, Type = type, HallId = request.HallId };
            db.SalesPoints.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/routing/sales-points/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/sales-points/{id:guid}", async (Guid id, UpsertSalesPointRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity = await db.SalesPoints.FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (entity is null) return Results.NotFound();
            var name = Normalize(request.Name);
            var type = NormalizeSalesType(request.Type);
            if (name is null || type is null) return Results.BadRequest(new { message = "Проверьте название и тип." });
            entity.Name = name;
            entity.Type = type;
            entity.HallId = request.HallId;
            entity.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPost("/routes", async (UpsertRouteRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var validation = await ValidateRoute(db, restaurantId, request, null, ct);
            if (validation is not null) return validation;
            var entity = new PreparationRoute
            {
                RestaurantId = restaurantId,
                SalesPointId = request.SalesPointId,
                PreparationPlaceTypeId = request.PreparationPlaceTypeId,
                PreparationPlaceId = request.PreparationPlaceId
            };
            db.PreparationRoutes.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/routing/routes/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/routes/{id:guid}", async (Guid id, UpsertRouteRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity = await db.PreparationRoutes.FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (entity is null) return Results.NotFound();
            var validation = await ValidateRoute(db, restaurantId, request, id, ct);
            if (validation is not null) return validation;
            entity.SalesPointId = request.SalesPointId;
            entity.PreparationPlaceTypeId = request.PreparationPlaceTypeId;
            entity.PreparationPlaceId = request.PreparationPlaceId;
            entity.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        return app;
    }

    private static async Task<IResult?> ValidatePlace(RestaurantDbContext db, Guid restaurantId, UpsertPlaceRequest request, CancellationToken ct)
    {
        if (Normalize(request.Name) is null) return Results.BadRequest(new { message = "Название обязательно." });
        if (!await db.PreparationPlaceTypes.AnyAsync(x => x.Id == request.PreparationPlaceTypeId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Тип места приготовления не найден." });
        if (request.PrinterId.HasValue && !await db.Printers.AnyAsync(x => x.Id == request.PrinterId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Принтер не найден." });
        if (request.WarehouseId.HasValue && !await db.Warehouses.AnyAsync(x => x.Id == request.WarehouseId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Склад не найден." });
        return null;
    }

    private static async Task<IResult?> ValidateRoute(RestaurantDbContext db, Guid restaurantId, UpsertRouteRequest request, Guid? routeId, CancellationToken ct)
    {
        if (!await db.SalesPoints.AnyAsync(x => x.Id == request.SalesPointId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Место продажи не найдено." });
        if (!await db.PreparationPlaceTypes.AnyAsync(x => x.Id == request.PreparationPlaceTypeId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Тип места приготовления не найден." });
        if (!await db.PreparationPlaces.AnyAsync(x => x.Id == request.PreparationPlaceId && x.RestaurantId == restaurantId && x.IsActive && x.PreparationPlaceTypeId == request.PreparationPlaceTypeId, ct))
            return Results.BadRequest(new { message = "Место приготовления не соответствует выбранному типу." });
        if (await db.PreparationRoutes.AnyAsync(x => x.RestaurantId == restaurantId && x.Id != routeId && x.SalesPointId == request.SalesPointId && x.PreparationPlaceTypeId == request.PreparationPlaceTypeId, ct))
            return Results.Conflict(new { message = "Для этой пары «место продажи + тип приготовления» маршрут уже настроен." });
        return null;
    }

    private static string? Normalize(string? value)
    {
        var result = value?.Trim();
        return string.IsNullOrWhiteSpace(result) || result.Length > 120 ? null : result;
    }

    private static string? NormalizeSalesType(string? value)
    {
        var type = value?.Trim().ToUpperInvariant();
        return type is "HALL" or "DELIVERY" or "PICKUP" or "KIOSK" or "OTHER" ? type : null;
    }

    private static bool TryRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
}

public sealed record NameRequest(string Name);
public sealed record UpdateNameRequest(string Name, bool IsActive);
public sealed record UpsertPlaceRequest(string Name, Guid PreparationPlaceTypeId, Guid? PrinterId, Guid? WarehouseId, bool IsActive = true);
public sealed record UpsertSalesPointRequest(string Name, string Type, Guid? HallId, bool IsActive = true);
public sealed record UpsertRouteRequest(Guid SalesPointId, Guid PreparationPlaceTypeId, Guid PreparationPlaceId, bool IsActive = true);
