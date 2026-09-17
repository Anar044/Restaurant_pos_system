using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Halls;

public static class HallEndpoints
{
    public static IEndpointRouteBuilder MapHallEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/halls", async (ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("restaurant_id"), out var restaurantId))
                return Results.Unauthorized();

            var halls = await db.Halls
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.SortOrder })
                .ToListAsync(ct);

            var tables = await db.DiningTables
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new { x.Id, x.HallId, x.Name, x.Seats, x.SortOrder })
                .ToListAsync(ct);

            var openOrders = await db.Orders
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.TableId != null &&
                    x.Status != OrderStatus.Closed &&
                    x.Status != OrderStatus.Cancelled)
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new
                {
                    x.Id,
                    TableId = x.TableId!.Value,
                    x.DisplayNumber,
                    x.Status,
                    x.Total,
                    x.GuestCount,
                    x.UpdatedAt
                })
                .ToListAsync(ct);

            var orderByTable = openOrders
                .GroupBy(x => x.TableId)
                .ToDictionary(x => x.Key, x => x.First());

            var result = halls.Select(hall => new
            {
                hall.Id,
                hall.Name,
                tables = tables
                    .Where(table => table.HallId == hall.Id)
                    .Select(table =>
                    {
                        orderByTable.TryGetValue(table.Id, out var order);
                        return new
                        {
                            table.Id,
                            table.Name,
                            table.Seats,
                            occupied = order is not null,
                            openOrder = order is null
                                ? null
                                : new
                                {
                                    order.Id,
                                    order.DisplayNumber,
                                    status = order.Status.ToString().ToUpperInvariant(),
                                    order.Total,
                                    order.GuestCount,
                                    order.UpdatedAt
                                }
                        };
                    })
            });

            return Results.Ok(new { halls = result });
        }).RequireAuthorization("orders.read");

        return app;
    }
}
