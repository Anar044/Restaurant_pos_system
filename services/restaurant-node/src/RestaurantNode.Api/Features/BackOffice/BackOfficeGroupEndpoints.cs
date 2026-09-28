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
                .Select(x => new {
                    id=x.Id, groupId=x.GroupId, name=x.Name, hallId=x.HallId,
                    hallName=x.HallId==null?null:db.Halls.Where(h=>h.Id==x.HallId).Select(h=>h.Name).FirstOrDefault(),
                    warehouseId=x.WarehouseId,
                    warehouseName=x.WarehouseId==null?null:db.Warehouses.Where(w=>w.Id==x.WarehouseId).Select(w=>w.Name).FirstOrDefault(),
                    printerId=x.PrinterId,
                    printerName=x.PrinterId==null?null:db.Printers.Where(p=>p.Id==x.PrinterId).Select(p=>p.Name).FirstOrDefault(),
                    isActive=x.IsActive
                }).ToListAsync(ct);

            var types = await db.PreparationPlaceTypes.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x=>x.IsActive).ThenBy(x=>x.Name)
                .Select(x=>new { id=x.Id, name=x.Name, isActive=x.IsActive })
                .ToListAsync(ct);

            var maps = await db.GroupPreparationMaps.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .Select(x => new { id=x.Id, groupId=x.GroupId, preparationPlaceTypeId=x.PreparationPlaceTypeId, departmentId=x.DepartmentId, isActive=x.IsActive })
                .ToListAsync(ct);

            var halls = await db.Halls.AsNoTracking().Where(x=>x.RestaurantId==restaurantId && x.IsActive)
                .OrderBy(x=>x.SortOrder).ThenBy(x=>x.Name).Select(x=>new {id=x.Id,name=x.Name}).ToListAsync(ct);
            var warehouses = await db.Warehouses.AsNoTracking().Where(x=>x.RestaurantId==restaurantId && x.IsActive)
                .OrderBy(x=>x.Name).Select(x=>new {id=x.Id,name=x.Name}).ToListAsync(ct);
            var printers = await db.Printers.AsNoTracking().Where(x=>x.RestaurantId==restaurantId && x.IsActive && x.IsConfigured)
                .OrderBy(x=>x.Name).Select(x=>new {id=x.Id,name=x.Name}).ToListAsync(ct);

            return Results.Ok(new {
                groups = groups.Select(g => new {
                    id=g.Id,name=g.Name,isActive=g.IsActive,
                    deviceIds=links.Where(x=>x.GroupId==g.Id).Select(x=>x.DeviceId).ToArray(),
                    mainCashRegisterId=links.Where(x=>x.GroupId==g.Id && x.IsMainCashRegister).Select(x=>(Guid?)x.DeviceId).FirstOrDefault()
                }),
                departments, types, maps, devices, halls, warehouses, printers
            });
        });

        group.MapPost("", async (NameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var name=Normalize(request.Name); if(name is null) return Results.BadRequest(new {message="Название группы обязательно."});
            if(await db.RestaurantGroups.AnyAsync(x=>x.RestaurantId==restaurantId && x.Name==name,ct))
                return Results.Conflict(new {message="Группа с таким названием уже существует."});
            var entity=new RestaurantGroup{RestaurantId=restaurantId,Name=name};
            db.RestaurantGroups.Add(entity); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/groups/{entity.Id}",new{id=entity.Id});
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateNameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity=await db.RestaurantGroups.FirstOrDefaultAsync(x=>x.Id==id && x.RestaurantId==restaurantId,ct);
            if(entity is null) return Results.NotFound();
            var name=Normalize(request.Name); if(name is null) return Results.BadRequest(new{message="Название группы обязательно."});
            entity.Name=name; entity.IsActive=request.IsActive; await db.SaveChangesAsync(ct);
            return Results.Ok(new{id=entity.Id});
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/{id:guid}/devices", async (Guid id, SetGroupDevicesRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            if(!await db.RestaurantGroups.AnyAsync(x=>x.Id==id && x.RestaurantId==restaurantId,ct)) return Results.NotFound();
            var ids=(request.DeviceIds??[]).Distinct().ToArray();
            var valid=await db.Devices.CountAsync(x=>x.RestaurantId==restaurantId && ids.Contains(x.Id),ct);
            if(valid!=ids.Length) return Results.BadRequest(new{message="Один или несколько терминалов не найдены."});
            if(request.MainCashRegisterId.HasValue && !ids.Contains(request.MainCashRegisterId.Value))
                return Results.BadRequest(new{message="Главная касса должна входить в группу."});
            var old=await db.RestaurantGroupDevices.Where(x=>x.GroupId==id).ToListAsync(ct);
            db.RestaurantGroupDevices.RemoveRange(old);
            foreach(var deviceId in ids) db.RestaurantGroupDevices.Add(new RestaurantGroupDevice{GroupId=id,DeviceId=deviceId,IsMainCashRegister=request.MainCashRegisterId==deviceId});
            await db.SaveChangesAsync(ct); return Results.Ok(new{id});
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPost("/{groupId:guid}/departments", async (Guid groupId, UpsertDepartmentRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var validation=await ValidateDepartment(db,restaurantId,groupId,request,ct); if(validation is not null) return validation;
            var entity=new RestaurantDepartment{RestaurantId=restaurantId,GroupId=groupId,Name=request.Name.Trim(),HallId=request.HallId,WarehouseId=request.WarehouseId,PrinterId=request.PrinterId,IsActive=true};
            db.RestaurantDepartments.Add(entity); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/groups/{groupId}/departments/{entity.Id}",new{id=entity.Id});
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/{groupId:guid}/departments/{id:guid}", async (Guid groupId, Guid id, UpsertDepartmentRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var entity=await db.RestaurantDepartments.FirstOrDefaultAsync(x=>x.Id==id && x.GroupId==groupId && x.RestaurantId==restaurantId,ct);
            if(entity is null) return Results.NotFound();
            var validation=await ValidateDepartment(db,restaurantId,groupId,request,ct); if(validation is not null) return validation;
            entity.Name=request.Name.Trim(); entity.HallId=request.HallId; entity.WarehouseId=request.WarehouseId; entity.PrinterId=request.PrinterId; entity.IsActive=request.IsActive;
            await db.SaveChangesAsync(ct); return Results.Ok(new{id=entity.Id});
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPost("/types", async (NameRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var name=Normalize(request.Name); if(name is null) return Results.BadRequest(new{message="Название типа обязательно."});
            if(await db.PreparationPlaceTypes.AnyAsync(x=>x.RestaurantId==restaurantId && x.Name==name,ct))
                return Results.Conflict(new{message="Такой тип уже существует."});
            var entity=new PreparationPlaceType{RestaurantId=restaurantId,Name=name}; db.PreparationPlaceTypes.Add(entity); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/groups/types/{entity.Id}",new{id=entity.Id});
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/{groupId:guid}/map/{typeId:guid}", async (Guid groupId, Guid typeId, SetCookingMapRequest request, ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            if(!await db.RestaurantGroups.AnyAsync(x=>x.Id==groupId && x.RestaurantId==restaurantId,ct)) return Results.NotFound();
            if(!await db.PreparationPlaceTypes.AnyAsync(x=>x.Id==typeId && x.RestaurantId==restaurantId && x.IsActive,ct)) return Results.BadRequest(new{message="Тип места приготовления не найден."});
            if(!await db.RestaurantDepartments.AnyAsync(x=>x.Id==request.DepartmentId && x.GroupId==groupId && x.RestaurantId==restaurantId && x.IsActive,ct))
                return Results.BadRequest(new{message="Отделение не найдено в этой группе."});
            var entity=await db.GroupPreparationMaps.FirstOrDefaultAsync(x=>x.GroupId==groupId && x.PreparationPlaceTypeId==typeId,ct);
            if(entity is null){entity=new GroupPreparationMap{RestaurantId=restaurantId,GroupId=groupId,PreparationPlaceTypeId=typeId,DepartmentId=request.DepartmentId};db.GroupPreparationMaps.Add(entity);}
            else {entity.DepartmentId=request.DepartmentId;entity.IsActive=true;}
            await db.SaveChangesAsync(ct); return Results.Ok(new{id=entity.Id});
        }).RequireAuthorization(Permissions.KitchenManage);

        return app;
    }

    private static async Task<IResult?> ValidateDepartment(RestaurantDbContext db, Guid restaurantId, Guid groupId, UpsertDepartmentRequest request, CancellationToken ct)
    {
        if(Normalize(request.Name) is null) return Results.BadRequest(new{message="Название отделения обязательно."});
        if(!await db.RestaurantGroups.AnyAsync(x=>x.Id==groupId && x.RestaurantId==restaurantId,ct)) return Results.BadRequest(new{message="Группа не найдена."});
        if(request.HallId.HasValue && !await db.Halls.AnyAsync(x=>x.Id==request.HallId && x.RestaurantId==restaurantId,ct)) return Results.BadRequest(new{message="Зал не найден."});
        if(request.WarehouseId.HasValue && !await db.Warehouses.AnyAsync(x=>x.Id==request.WarehouseId && x.RestaurantId==restaurantId && x.IsActive,ct)) return Results.BadRequest(new{message="Склад не найден."});
        if(request.PrinterId.HasValue && !await db.Printers.AnyAsync(x=>x.Id==request.PrinterId && x.RestaurantId==restaurantId && x.IsActive,ct)) return Results.BadRequest(new{message="Принтер не найден."});
        return null;
    }
    private static string? Normalize(string? value){var s=value?.Trim();return string.IsNullOrWhiteSpace(s)||s.Length>120?null:s;}
    private static bool TryRestaurantId(ClaimsPrincipal user,out Guid restaurantId)=>Guid.TryParse(user.FindFirstValue("restaurant_id"),out restaurantId);
}

public sealed record SetGroupDevicesRequest(Guid[]? DeviceIds, Guid? MainCashRegisterId);
public sealed record UpsertDepartmentRequest(string Name, Guid? HallId, Guid? WarehouseId, Guid? PrinterId, bool IsActive=true);
public sealed record SetCookingMapRequest(Guid DepartmentId);
