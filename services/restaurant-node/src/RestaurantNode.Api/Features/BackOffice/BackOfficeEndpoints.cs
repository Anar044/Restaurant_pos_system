using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("/context", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("organization_id"), out var organizationId) ||
                !Guid.TryParse(user.FindFirstValue("restaurant_id"), out var restaurantId))
            {
                return Results.Unauthorized();
            }

            var context = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId && x.OrganizationId == organizationId && x.IsActive)
                .Select(x => new
                {
                    organization = new
                    {
                        id = x.OrganizationId,
                        name = x.Organization!.Name,
                        isActive = x.Organization.IsActive
                    },
                    restaurant = new
                    {
                        id = x.Id,
                        organizationId = x.OrganizationId,
                        name = x.Name,
                        currencyCode = x.CurrencyCode,
                        timeZone = x.TimeZone,
                        isActive = x.IsActive
                    }
                })
                .FirstOrDefaultAsync(ct);

            return context is null ? Results.NotFound() : Results.Ok(context);
        });

        return app;
    }
}
