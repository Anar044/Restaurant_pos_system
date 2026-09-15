using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Menu;

public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/menu", async (ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId)) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;

            var categories = await db.Categories
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .Select(category => new
                {
                    category.Id,
                    category.Name,
                    Products = db.Products
                        .Where(product => product.RestaurantId == restaurantId && product.CategoryId == category.Id && product.IsActive)
                        .OrderBy(product => product.SortOrder)
                        .Select(product => new
                        {
                            product.Id,
                            product.Name,
                            Price = db.ProductPrices
                                .Where(price => price.ProductId == product.Id && price.ValidFrom <= now && (price.ValidTo == null || price.ValidTo > now))
                                .OrderByDescending(price => price.ValidFrom)
                                .Select(price => price.Amount)
                                .FirstOrDefault(),
                            CurrencyCode = db.ProductPrices
                                .Where(price => price.ProductId == product.Id && price.ValidFrom <= now && (price.ValidTo == null || price.ValidTo > now))
                                .OrderByDescending(price => price.ValidFrom)
                                .Select(price => price.CurrencyCode)
                                .FirstOrDefault() ?? "AZN"
                        }).ToList()
                })
                .ToListAsync(ct);

            return Results.Ok(new { categories });
        }).RequireAuthorization("menu.read");

        return app;
    }

    private static bool TryRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
}
