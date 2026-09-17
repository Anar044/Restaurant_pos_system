using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeKitchenEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeKitchenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/kitchen")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var stations = await db.KitchenStations
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(station => new
                {
                    id = station.Id,
                    name = station.Name,
                    isActive = station.IsActive,
                    activeProductCount = db.Products.Count(product =>
                        product.RestaurantId == restaurantId &&
                        product.KitchenStationId == station.Id &&
                        product.IsActive),
                    totalProductCount = db.Products.Count(product =>
                        product.RestaurantId == restaurantId &&
                        product.KitchenStationId == station.Id),
                    products = db.Products
                        .Where(product =>
                            product.RestaurantId == restaurantId &&
                            product.KitchenStationId == station.Id)
                        .OrderByDescending(product => product.IsActive)
                        .ThenBy(product => product.Name)
                        .Select(product => new
                        {
                            id = product.Id,
                            name = product.Name,
                            sku = product.Sku,
                            isActive = product.IsActive,
                            categoryId = product.CategoryId,
                            categoryName = db.Categories
                                .Where(category => category.Id == product.CategoryId)
                                .Select(category => category.Name)
                                .FirstOrDefault()
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            var unassignedProducts = await db.Products
                .AsNoTracking()
                .Where(product =>
                    product.RestaurantId == restaurantId &&
                    product.KitchenStationId == null &&
                    product.IsActive)
                .OrderBy(product => product.Name)
                .Select(product => new
                {
                    id = product.Id,
                    name = product.Name,
                    sku = product.Sku,
                    isActive = product.IsActive,
                    categoryId = product.CategoryId,
                    categoryName = db.Categories
                        .Where(category => category.Id == product.CategoryId)
                        .Select(category => category.Name)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                stations,
                unassignedProducts
            });
        });

        group.MapPost("/stations", async (
            CreateKitchenStationRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var name = NormalizeName(request.Name);
            if (name is null)
                return Results.BadRequest(new { message = "Kitchen station name is required and must be 100 characters or fewer." });

            var duplicate = await db.KitchenStations.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A kitchen station with this name already exists." });

            var station = new KitchenStation
            {
                RestaurantId = restaurantId,
                Name = name,
                IsActive = true
            };

            db.KitchenStations.Add(station);
            AddAudit(db, user, restaurantId, "KITCHEN_STATION_CREATED", "KitchenStation", station.Id, new
            {
                station.Name,
                station.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/kitchen/stations/{station.Id}", new
            {
                id = station.Id,
                name = station.Name,
                isActive = station.IsActive,
                activeProductCount = 0,
                totalProductCount = 0
            });
        }).RequireAuthorization(Permissions.KitchenManage);

        group.MapPut("/stations/{stationId:guid}", async (
            Guid stationId,
            UpdateKitchenStationRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var station = await db.KitchenStations.FirstOrDefaultAsync(
                x => x.Id == stationId && x.RestaurantId == restaurantId,
                ct);
            if (station is null)
                return Results.NotFound();

            var name = NormalizeName(request.Name);
            if (name is null)
                return Results.BadRequest(new { message = "Kitchen station name is required and must be 100 characters or fewer." });

            var duplicate = await db.KitchenStations.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != stationId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A kitchen station with this name already exists." });

            if (station.IsActive && !request.IsActive)
            {
                var activeProducts = await db.Products
                    .Where(x =>
                        x.RestaurantId == restaurantId &&
                        x.KitchenStationId == stationId &&
                        x.IsActive)
                    .Select(x => x.Name)
                    .OrderBy(x => x)
                    .Take(5)
                    .ToListAsync(ct);

                if (activeProducts.Count > 0)
                {
                    return Results.Conflict(new
                    {
                        message = "The kitchen station cannot be deactivated while active products are assigned to it. Reassign or deactivate those products first.",
                        products = activeProducts
                    });
                }
            }

            station.Name = name;
            station.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "KITCHEN_STATION_UPDATED", "KitchenStation", station.Id, new
            {
                station.Name,
                station.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = station.Id,
                name = station.Name,
                isActive = station.IsActive
            });
        }).RequireAuthorization(Permissions.KitchenManage);

        return app;
    }

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static string? NormalizeName(string? value)
    {
        var name = value?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.Length > 100 ? null : name;
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

public sealed record CreateKitchenStationRequest(string Name);
public sealed record UpdateKitchenStationRequest(string Name, bool IsActive);
