using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Menu;

public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/menu", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;

            var categories = await db.Categories
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.SortOrder
                })
                .ToListAsync(ct);

            var products = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.CategoryId,
                    x.Name,
                    x.SortOrder
                })
                .ToListAsync(ct);

            var productIds = products.Select(x => x.Id).ToArray();

            var prices = await db.ProductPrices
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    productIds.Contains(x.ProductId) &&
                    x.ValidFrom <= now &&
                    (x.ValidTo == null || x.ValidTo > now))
                .OrderByDescending(x => x.ValidFrom)
                .Select(x => new
                {
                    x.ProductId,
                    x.Amount,
                    x.CurrencyCode,
                    x.ValidFrom
                })
                .ToListAsync(ct);

            var priceByProduct = prices
                .GroupBy(x => x.ProductId)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderByDescending(price => price.ValidFrom).First());

            var productGroupLinks = await db.ProductModifierGroups
                .AsNoTracking()
                .Where(x => productIds.Contains(x.ProductId))
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            var groupIds = productGroupLinks
                .Select(x => x.ModifierGroupId)
                .Distinct()
                .ToArray();

            var groups = await db.ModifierGroups
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    groupIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            var groupModifierLinks = await db.ModifierGroupModifiers
                .AsNoTracking()
                .Where(x => groupIds.Contains(x.ModifierGroupId))
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            var modifierIds = groupModifierLinks
                .Select(x => x.ModifierId)
                .Distinct()
                .ToArray();

            var modifiers = await db.Modifiers
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    modifierIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            var result = categories.Select(category => new
            {
                category.Id,
                category.Name,
                products = products
                    .Where(product => product.CategoryId == category.Id)
                    .OrderBy(product => product.SortOrder)
                    .ThenBy(product => product.Name)
                    .Select(product =>
                    {
                        priceByProduct.TryGetValue(product.Id, out var price);

                        var modifierGroups = productGroupLinks
                            .Where(link => link.ProductId == product.Id)
                            .OrderBy(link => link.SortOrder)
                            .Select(link =>
                            {
                                if (!groups.TryGetValue(link.ModifierGroupId, out var group))
                                    return null;

                                var options = groupModifierLinks
                                    .Where(x => x.ModifierGroupId == group.Id)
                                    .OrderBy(x => x.SortOrder)
                                    .Select(x =>
                                    {
                                        if (!modifiers.TryGetValue(x.ModifierId, out var modifier))
                                            return null;

                                        return new
                                        {
                                            id = modifier.Id,
                                            name = modifier.Name,
                                            priceDelta = modifier.PriceDelta
                                        };
                                    })
                                    .Where(x => x is not null)
                                    .ToArray();

                                return new
                                {
                                    id = group.Id,
                                    name = group.Name,
                                    minSelections = group.MinSelections,
                                    maxSelections = group.MaxSelections,
                                    isRequired = group.IsRequired,
                                    modifiers = options
                                };
                            })
                            .Where(x => x is not null)
                            .ToArray();

                        return new
                        {
                            product.Id,
                            product.Name,
                            price = price?.Amount ?? 0m,
                            currencyCode = price?.CurrencyCode ?? "AZN",
                            modifierGroups
                        };
                    })
                    .ToArray()
            }).ToArray();

            return Results.Ok(new { categories = result });
        }).RequireAuthorization("menu.read");

        return app;
    }

    private static bool TryRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
}
