using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeHallEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeHallEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("/halls", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var halls = await db.Halls
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    sortOrder = x.SortOrder,
                    isActive = x.IsActive,
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
                        .ToArray()
                })
                .ToListAsync(ct);

            return Results.Ok(halls);
        });

        group.MapPost("/halls", async (
            CreateHallRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var name = NormalizeName(request.Name);
            if (name is null)
                return Results.BadRequest(new { message = "Hall name is required and must be 100 characters or fewer." });
            if (request.SortOrder < 0)
                return Results.BadRequest(new { message = "Sort order cannot be negative." });

            var duplicate = await db.Halls.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A hall with this name already exists." });

            var hall = new Hall
            {
                RestaurantId = restaurantId,
                Name = name,
                SortOrder = request.SortOrder,
                IsActive = true
            };

            db.Halls.Add(hall);
            AddAudit(db, user, restaurantId, "HALL_CREATED", "Hall", hall.Id, new
            {
                hall.Name,
                hall.SortOrder,
                hall.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/halls/{hall.Id}", ToHallResponse(hall));
        }).RequireAuthorization(Permissions.HallsManage);

        group.MapPut("/halls/{hallId:guid}", async (
            Guid hallId,
            UpdateHallRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var hall = await db.Halls.FirstOrDefaultAsync(
                x => x.Id == hallId && x.RestaurantId == restaurantId,
                ct);
            if (hall is null)
                return Results.NotFound();

            var name = NormalizeName(request.Name);
            if (name is null)
                return Results.BadRequest(new { message = "Hall name is required and must be 100 characters or fewer." });
            if (request.SortOrder < 0)
                return Results.BadRequest(new { message = "Sort order cannot be negative." });

            var duplicate = await db.Halls.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != hallId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A hall with this name already exists." });

            if (hall.IsActive && !request.IsActive)
            {
                var hasOpenOrders = await db.Orders.AnyAsync(
                    o => o.RestaurantId == restaurantId &&
                         o.TableId != null &&
                         db.DiningTables.Any(t => t.Id == o.TableId && t.HallId == hallId) &&
                         o.Status != OrderStatus.Closed &&
                         o.Status != OrderStatus.Cancelled,
                    ct);
                if (hasOpenOrders)
                    return Results.Conflict(new { message = "The hall cannot be deactivated while one of its tables has an active order." });
            }

            hall.Name = name;
            hall.SortOrder = request.SortOrder;
            hall.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "HALL_UPDATED", "Hall", hall.Id, new
            {
                hall.Name,
                hall.SortOrder,
                hall.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToHallResponse(hall));
        }).RequireAuthorization(Permissions.HallsManage);

        group.MapPost("/halls/{hallId:guid}/tables", async (
            Guid hallId,
            CreateTableRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var hallExists = await db.Halls.AnyAsync(
                x => x.Id == hallId && x.RestaurantId == restaurantId,
                ct);
            if (!hallExists)
                return Results.NotFound(new { message = "Hall was not found." });

            var name = NormalizeName(request.Name);
            if (name is null)
                return Results.BadRequest(new { message = "Table name is required and must be 100 characters or fewer." });
            if (request.Seats <= 0 || request.Seats > 100)
                return Results.BadRequest(new { message = "Seats must be between 1 and 100." });
            if (request.SortOrder < 0)
                return Results.BadRequest(new { message = "Sort order cannot be negative." });

            var duplicate = await db.DiningTables.AnyAsync(
                x => x.HallId == hallId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A table with this name already exists in the hall." });

            var table = new DiningTable
            {
                RestaurantId = restaurantId,
                HallId = hallId,
                Name = name,
                Seats = request.Seats,
                SortOrder = request.SortOrder,
                IsActive = true
            };

            db.DiningTables.Add(table);
            AddAudit(db, user, restaurantId, "TABLE_CREATED", "DiningTable", table.Id, new
            {
                table.HallId,
                table.Name,
                table.Seats,
                table.SortOrder,
                table.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/tables/{table.Id}", ToTableResponse(table));
        }).RequireAuthorization(Permissions.HallsManage);

        group.MapPut("/tables/{tableId:guid}", async (
            Guid tableId,
            UpdateTableRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var table = await db.DiningTables.FirstOrDefaultAsync(
                x => x.Id == tableId && x.RestaurantId == restaurantId,
                ct);
            if (table is null)
                return Results.NotFound();

            var hallExists = await db.Halls.AnyAsync(
                x => x.Id == request.HallId && x.RestaurantId == restaurantId,
                ct);
            if (!hallExists)
                return Results.BadRequest(new { message = "Target hall was not found." });

            var name = NormalizeName(request.Name);
            if (name is null)
                return Results.BadRequest(new { message = "Table name is required and must be 100 characters or fewer." });
            if (request.Seats <= 0 || request.Seats > 100)
                return Results.BadRequest(new { message = "Seats must be between 1 and 100." });
            if (request.SortOrder < 0)
                return Results.BadRequest(new { message = "Sort order cannot be negative." });

            var duplicate = await db.DiningTables.AnyAsync(
                x => x.HallId == request.HallId && x.Id != tableId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A table with this name already exists in the hall." });

            if (table.IsActive && !request.IsActive)
            {
                var hasOpenOrder = await db.Orders.AnyAsync(
                    o => o.RestaurantId == restaurantId &&
                         o.TableId == tableId &&
                         o.Status != OrderStatus.Closed &&
                         o.Status != OrderStatus.Cancelled,
                    ct);
                if (hasOpenOrder)
                    return Results.Conflict(new { message = "The table cannot be deactivated while it has an active order." });
            }

            table.HallId = request.HallId;
            table.Name = name;
            table.Seats = request.Seats;
            table.SortOrder = request.SortOrder;
            table.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "TABLE_UPDATED", "DiningTable", table.Id, new
            {
                table.HallId,
                table.Name,
                table.Seats,
                table.SortOrder,
                table.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToTableResponse(table));
        }).RequireAuthorization(Permissions.HallsManage);

        return app;
    }

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static string? NormalizeName(string? value)
    {
        var name = value?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.Length > 100 ? null : name;
    }

    private static object ToHallResponse(Hall hall) => new
    {
        id = hall.Id,
        name = hall.Name,
        sortOrder = hall.SortOrder,
        isActive = hall.IsActive
    };

    private static object ToTableResponse(DiningTable table) => new
    {
        id = table.Id,
        hallId = table.HallId,
        name = table.Name,
        seats = table.Seats,
        sortOrder = table.SortOrder,
        isActive = table.IsActive
    };

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

public sealed record CreateHallRequest(string Name, int SortOrder = 0);
public sealed record UpdateHallRequest(string Name, int SortOrder, bool IsActive);
public sealed record CreateTableRequest(string Name, int Seats = 4, int SortOrder = 0);
public sealed record UpdateTableRequest(Guid HallId, string Name, int Seats, int SortOrder, bool IsActive);
