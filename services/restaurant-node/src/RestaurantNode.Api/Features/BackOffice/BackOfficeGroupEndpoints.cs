using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeGroupEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/groups")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();

            var groups = await db.RestaurantGroups.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                .ToListAsync(ct);
            var groupIds = groups.Select(x => x.Id).ToArray();

            var links = await db.RestaurantGroupDevices.AsNoTracking()
                .Where(x => groupIds.Contains(x.GroupId))
                .ToListAsync(ct);

            var devices = await db.Devices.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name, type = x.Type.ToString(), isActive = x.IsActive })
                .ToListAsync(ct);

            var departments = await db.RestaurantDepartments.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    groupId = x.GroupId,
                    name = x.Name,
                    preparationPlaceTypeId = x.PreparationPlaceTypeId,
                    preparationPlaceTypeName = x.PreparationPlaceTypeId == null
                        ? null
                        : db.PreparationPlaceTypes.Where(t => t.Id == x.PreparationPlaceTypeId).Select(t => t.Name).FirstOrDefault(),
                    warehouseId = x.WarehouseId,
                    warehouseName = x.WarehouseId == null
                        ? null
                        : db.Warehouses.Where(w => w.Id == x.WarehouseId).Select(w => w.Name).FirstOrDefault(),
                    printerId = x.PrinterId,
                    printerName = x.PrinterId == null
                        ? null
                        : db.Printers.Where(p => p.Id == x.PrinterId).Select(p => p.Name).FirstOrDefault(),
                    isActive = x.IsActive
                })
                .ToListAsync(ct);

            var halls = await db.Halls.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    groupId = x.GroupId,
                    name = x.Name,
                    sortOrder = x.SortOrder,
                    precheckPrinterId = x.PrecheckPrinterId,
                    precheckPrinterName = x.PrecheckPrinterId == null
                        ? null
                        : db.Printers.Where(p => p.Id == x.PrecheckPrinterId).Select(p => p.Name).FirstOrDefault(),
                    tableCount = db.DiningTables.Count(t => t.HallId == x.Id && t.RestaurantId == restaurantId),
                    tables = db.DiningTables
                        .Where(t => t.HallId == x.Id && t.RestaurantId == restaurantId)
                        .OrderBy(t => t.SortOrder)
                        .ThenBy(t => t.Name)
                        .Select(t => new
                        {
                            id = t.Id,
                            hallId = t.HallId,
                            name = t.Name,
                            seats = t.Seats,
                            sortOrder = t.SortOrder,
                            isActive = t.IsActive
                        })
                        .ToArray(),
                    isActive = x.IsActive
                })
                .ToListAsync(ct);

            var types = await db.PreparationPlaceTypes.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive })
                .ToListAsync(ct);

            var warehouses = await db.Warehouses.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToListAsync(ct);

            var printers = await db.Printers.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.IsConfigured)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                groups = groups.Select(g => new
                {
                    id = g.Id,
                    name = g.Name,
                    isActive = g.IsActive,
                    deviceIds = links.Where(x => x.GroupId == g.Id).Select(x => x.DeviceId).ToArray(),
                    mainCashRegisterId = links.Where(x => x.GroupId == g.Id && x.IsMainCashRegister)
                        .Select(x => (Guid?)x.DeviceId).FirstOrDefault()
                }),
                departments,
                halls,
                types,
                devices,
                warehouses,
                printers
            });
        });

        group.MapPost("", async (GroupNameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var name = Normalize(request.Name);
            if (name is null) return Results.BadRequest(new { message = "Название группы обязательно." });
            if (await db.RestaurantGroups.AnyAsync(x => x.RestaurantId == restaurantId && x.Name == name, ct))
                return Results.Conflict(new { message = "Группа с таким названием уже существует." });

            var entity = new RestaurantGroup { RestaurantId = restaurantId, Name = name };
            db.RestaurantGroups.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/groups/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/{id:guid}", async (Guid id, GroupUpdateNameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity = await db.RestaurantGroups.FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (entity is null) return Results.NotFound();

            var name = Normalize(request.Name);
            if (name is null) return Results.BadRequest(new { message = "Название группы обязательно." });
            if (await db.RestaurantGroups.AnyAsync(x => x.RestaurantId == restaurantId && x.Id != id && x.Name == name, ct))
                return Results.Conflict(new { message = "Группа с таким названием уже существует." });

            entity.Name = name;
            entity.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/{id:guid}/devices", async (Guid id, SetGroupDevicesRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            if (!await db.RestaurantGroups.AnyAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct))
                return Results.NotFound();

            var ids = (request.DeviceIds ?? []).Distinct().ToArray();
            var valid = await db.Devices.CountAsync(x => x.RestaurantId == restaurantId && ids.Contains(x.Id), ct);
            if (valid != ids.Length) return Results.BadRequest(new { message = "Один или несколько терминалов не найдены." });
            if (request.MainCashRegisterId.HasValue && !ids.Contains(request.MainCashRegisterId.Value))
                return Results.BadRequest(new { message = "Главная касса должна входить в группу." });

            var old = await db.RestaurantGroupDevices
                .Where(x => x.GroupId == id || ids.Contains(x.DeviceId))
                .ToListAsync(ct);
            db.RestaurantGroupDevices.RemoveRange(old);

            foreach (var deviceId in ids)
            {
                db.RestaurantGroupDevices.Add(new RestaurantGroupDevice
                {
                    GroupId = id,
                    DeviceId = deviceId,
                    IsMainCashRegister = request.MainCashRegisterId == deviceId
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPost("/{groupId:guid}/departments", async (Guid groupId, UpsertDepartmentRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var validation = await ValidateDepartment(db, restaurantId, groupId, null, request, ct);
            if (validation is not null) return validation;

            var entity = new RestaurantDepartment
            {
                RestaurantId = restaurantId,
                GroupId = groupId,
                Name = request.Name.Trim(),
                PreparationPlaceTypeId = request.PreparationPlaceTypeId,
                WarehouseId = request.WarehouseId,
                PrinterId = request.PrinterId,
                IsActive = true
            };
            db.RestaurantDepartments.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/groups/{groupId}/departments/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/{groupId:guid}/departments/{id:guid}", async (Guid groupId, Guid id, UpsertDepartmentRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity = await db.RestaurantDepartments
                .FirstOrDefaultAsync(x => x.Id == id && x.GroupId == groupId && x.RestaurantId == restaurantId, ct);
            if (entity is null) return Results.NotFound();

            var validation = await ValidateDepartment(db, restaurantId, groupId, id, request, ct);
            if (validation is not null) return validation;

            entity.Name = request.Name.Trim();
            entity.PreparationPlaceTypeId = request.PreparationPlaceTypeId;
            entity.WarehouseId = request.WarehouseId;
            entity.PrinterId = request.PrinterId;
            entity.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPost("/{groupId:guid}/halls", async (Guid groupId, UpsertGroupHallRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var validation = await ValidateHall(db, restaurantId, groupId, null, request, ct);
            if (validation is not null) return validation;

            var entity = new Hall
            {
                RestaurantId = restaurantId,
                GroupId = groupId,
                Name = request.Name.Trim(),
                SortOrder = request.SortOrder,
                PrecheckPrinterId = request.PrecheckPrinterId,
                IsActive = true
            };
            db.Halls.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/groups/{groupId}/halls/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.HallsManage);

        group.MapPut("/{groupId:guid}/halls/{id:guid}", async (Guid groupId, Guid id, UpsertGroupHallRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity = await db.Halls.FirstOrDefaultAsync(
                x => x.Id == id && x.GroupId == groupId && x.RestaurantId == restaurantId, ct);
            if (entity is null) return Results.NotFound();

            var validation = await ValidateHall(db, restaurantId, groupId, id, request, ct);
            if (validation is not null) return validation;

            entity.Name = request.Name.Trim();
            entity.SortOrder = request.SortOrder;
            entity.PrecheckPrinterId = request.PrecheckPrinterId;
            entity.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = entity.Id });
        }).RequireAuthorization(Permissions.HallsManage);

        group.MapPost("/types", async (GroupNameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var name = Normalize(request.Name);
            if (name is null) return Results.BadRequest(new { message = "Название типа обязательно." });
            if (await db.PreparationPlaceTypes.AnyAsync(x => x.RestaurantId == restaurantId && x.Name == name, ct))
                return Results.Conflict(new { message = "Такой тип уже существует." });

            var entity = new PreparationPlaceType { RestaurantId = restaurantId, Name = name };
            db.PreparationPlaceTypes.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/groups/types/{entity.Id}", new { id = entity.Id });
        }).RequireAuthorization(Permissions.KitchenManage);

        return app;
    }

    private static async Task<IResult?> ValidateDepartment(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid groupId,
        Guid? departmentId,
        UpsertDepartmentRequest request,
        CancellationToken ct)
    {
        var name = Normalize(request.Name);
        if (name is null) return Results.BadRequest(new { message = "Название отделения обязательно." });
        if (!await db.RestaurantGroups.AnyAsync(x => x.Id == groupId && x.RestaurantId == restaurantId, ct))
            return Results.BadRequest(new { message = "Группа не найдена." });
        if (await db.RestaurantDepartments.AnyAsync(x =>
            x.GroupId == groupId && x.Id != departmentId && x.Name == name, ct))
            return Results.Conflict(new { message = "В этой группе уже есть отделение с таким названием." });

        if (request.PreparationPlaceTypeId.HasValue)
        {
            if (!await db.PreparationPlaceTypes.AnyAsync(x =>
                x.Id == request.PreparationPlaceTypeId && x.RestaurantId == restaurantId && x.IsActive, ct))
                return Results.BadRequest(new { message = "Тип места приготовления не найден." });

            if (await db.RestaurantDepartments.AnyAsync(x =>
                x.GroupId == groupId &&
                x.Id != departmentId &&
                x.PreparationPlaceTypeId == request.PreparationPlaceTypeId, ct))
                return Results.Conflict(new { message = "Этот тип места приготовления уже назначен другому отделению группы." });
        }

        if (request.WarehouseId.HasValue && !await db.Warehouses.AnyAsync(x =>
            x.Id == request.WarehouseId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Склад списания не найден." });

        if (request.PrinterId.HasValue && !await db.Printers.AnyAsync(x =>
            x.Id == request.PrinterId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Принтер отделения не найден." });

        return null;
    }

    private static async Task<IResult?> ValidateHall(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid groupId,
        Guid? hallId,
        UpsertGroupHallRequest request,
        CancellationToken ct)
    {
        var name = Normalize(request.Name);
        if (name is null) return Results.BadRequest(new { message = "Название зала обязательно." });
        if (request.SortOrder < 0) return Results.BadRequest(new { message = "Порядок не может быть отрицательным." });
        if (!await db.RestaurantGroups.AnyAsync(x => x.Id == groupId && x.RestaurantId == restaurantId, ct))
            return Results.BadRequest(new { message = "Группа не найдена." });
        if (await db.Halls.AnyAsync(x => x.GroupId == groupId && x.Id != hallId && x.Name == name, ct))
            return Results.Conflict(new { message = "В этой группе уже есть зал с таким названием." });

        if (request.PrecheckPrinterId.HasValue && !await db.Printers.AnyAsync(x =>
            x.Id == request.PrecheckPrinterId && x.RestaurantId == restaurantId && x.IsActive, ct))
            return Results.BadRequest(new { message = "Принтер пречека не найден." });

        return null;
    }

    private static string? Normalize(string? value)
    {
        var s = value?.Trim();
        return string.IsNullOrWhiteSpace(s) || s.Length > 120 ? null : s;
    }

    private static bool TryRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
}

public sealed record GroupNameRequest(string Name);
public sealed record GroupUpdateNameRequest(string Name, bool IsActive);
public sealed record SetGroupDevicesRequest(Guid[]? DeviceIds, Guid? MainCashRegisterId);
public sealed record UpsertDepartmentRequest(
    string Name,
    Guid? PreparationPlaceTypeId,
    Guid? WarehouseId,
    Guid? PrinterId,
    bool IsActive = true);
public sealed record UpsertGroupHallRequest(
    string Name,
    int SortOrder,
    Guid? PrecheckPrinterId,
    bool IsActive = true);
