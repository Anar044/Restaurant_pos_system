using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeMenuEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeMenuEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/menu")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;

            var restaurant = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new { x.CurrencyCode })
                .FirstOrDefaultAsync(ct);
            if (restaurant is null)
                return Results.NotFound();

            var stations = await db.KitchenStations
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    isActive = x.IsActive
                })
                .ToListAsync(ct);

            var categories = await db.Categories
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(category => new
                {
                    id = category.Id,
                    name = category.Name,
                    sortOrder = category.SortOrder,
                    isActive = category.IsActive,
                    products = db.Products
                        .Where(product => product.RestaurantId == restaurantId && product.CategoryId == category.Id)
                        .OrderBy(product => product.SortOrder)
                        .ThenBy(product => product.Name)
                        .Select(product => new
                        {
                            id = product.Id,
                            categoryId = product.CategoryId,
                            kitchenStationId = product.KitchenStationId,
                            kitchenStationName = product.KitchenStationId == null
                                ? null
                                : db.KitchenStations
                                    .Where(station => station.Id == product.KitchenStationId)
                                    .Select(station => station.Name)
                                    .FirstOrDefault(),
                            name = product.Name,
                            sku = product.Sku,
                            sortOrder = product.SortOrder,
                            isActive = product.IsActive,
                            currentPrice = db.ProductPrices
                                .Where(price => price.RestaurantId == restaurantId &&
                                                price.ProductId == product.Id &&
                                                price.ValidFrom <= now &&
                                                (price.ValidTo == null || price.ValidTo > now))
                                .OrderByDescending(price => price.ValidFrom)
                                .Select(price => new
                                {
                                    id = price.Id,
                                    amount = price.Amount,
                                    currencyCode = price.CurrencyCode,
                                    validFrom = price.ValidFrom,
                                    validTo = price.ValidTo
                                })
                                .FirstOrDefault(),
                            priceHistory = db.ProductPrices
                                .Where(price => price.RestaurantId == restaurantId && price.ProductId == product.Id)
                                .OrderByDescending(price => price.ValidFrom)
                                .Select(price => new
                                {
                                    id = price.Id,
                                    amount = price.Amount,
                                    currencyCode = price.CurrencyCode,
                                    validFrom = price.ValidFrom,
                                    validTo = price.ValidTo
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                currencyCode = restaurant.CurrencyCode,
                kitchenStations = stations,
                categories
            });
        });

        group.MapPost("/categories", async (
            CreateCategoryRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var name = NormalizeName(request.Name, 100);
            if (name is null)
                return Results.BadRequest(new { message = "Category name is required and must be 100 characters or fewer." });
            if (request.SortOrder < 0)
                return Results.BadRequest(new { message = "Sort order cannot be negative." });

            var duplicate = await db.Categories.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A category with this name already exists." });

            var category = new Category
            {
                RestaurantId = restaurantId,
                Name = name,
                SortOrder = request.SortOrder,
                IsActive = true
            };

            db.Categories.Add(category);
            AddAudit(db, user, restaurantId, "CATEGORY_CREATED", "Category", category.Id, new
            {
                category.Name,
                category.SortOrder,
                category.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/menu/categories/{category.Id}", ToCategoryResponse(category));
        }).RequireAuthorization(Permissions.MenuManage);

        group.MapPut("/categories/{categoryId:guid}", async (
            Guid categoryId,
            UpdateCategoryRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var category = await db.Categories.FirstOrDefaultAsync(
                x => x.Id == categoryId && x.RestaurantId == restaurantId,
                ct);
            if (category is null)
                return Results.NotFound();

            var name = NormalizeName(request.Name, 100);
            if (name is null)
                return Results.BadRequest(new { message = "Category name is required and must be 100 characters or fewer." });
            if (request.SortOrder < 0)
                return Results.BadRequest(new { message = "Sort order cannot be negative." });

            var duplicate = await db.Categories.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != categoryId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A category with this name already exists." });

            category.Name = name;
            category.SortOrder = request.SortOrder;
            category.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "CATEGORY_UPDATED", "Category", category.Id, new
            {
                category.Name,
                category.SortOrder,
                category.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToCategoryResponse(category));
        }).RequireAuthorization(Permissions.MenuManage);

        group.MapPost("/products", async (
            CreateProductRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var validation = await ValidateProductRequest(
                db,
                restaurantId,
                productId: null,
                request.CategoryId,
                request.KitchenStationId,
                request.Name,
                request.Sku,
                request.SortOrder,
                request.Price,
                ct);
            if (validation.Error is not null)
                return validation.Error;

            var restaurant = await db.Restaurants
                .AsNoTracking()
                .FirstAsync(x => x.Id == restaurantId, ct);

            var product = new Product
            {
                RestaurantId = restaurantId,
                CategoryId = request.CategoryId,
                KitchenStationId = request.KitchenStationId,
                Name = validation.Name!,
                Sku = validation.Sku,
                SortOrder = request.SortOrder,
                IsActive = true
            };

            db.Products.Add(product);
            db.ProductPrices.Add(new ProductPrice
            {
                RestaurantId = restaurantId,
                ProductId = product.Id,
                Amount = request.Price,
                CurrencyCode = restaurant.CurrencyCode,
                ValidFrom = DateTimeOffset.UtcNow
            });

            AddAudit(db, user, restaurantId, "PRODUCT_CREATED", "Product", product.Id, new
            {
                product.CategoryId,
                product.KitchenStationId,
                product.Name,
                product.Sku,
                product.SortOrder,
                product.IsActive,
                price = request.Price,
                currencyCode = restaurant.CurrencyCode
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/menu/products/{product.Id}", new { id = product.Id });
        }).RequireAuthorization(Permissions.MenuManage);

        group.MapPut("/products/{productId:guid}", async (
            Guid productId,
            UpdateProductRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var product = await db.Products.FirstOrDefaultAsync(
                x => x.Id == productId && x.RestaurantId == restaurantId,
                ct);
            if (product is null)
                return Results.NotFound();

            var validation = await ValidateProductRequest(
                db,
                restaurantId,
                productId,
                request.CategoryId,
                request.KitchenStationId,
                request.Name,
                request.Sku,
                request.SortOrder,
                price: null,
                ct);
            if (validation.Error is not null)
                return validation.Error;

            product.CategoryId = request.CategoryId;
            product.KitchenStationId = request.KitchenStationId;
            product.Name = validation.Name!;
            product.Sku = validation.Sku;
            product.SortOrder = request.SortOrder;
            product.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "PRODUCT_UPDATED", "Product", product.Id, new
            {
                product.CategoryId,
                product.KitchenStationId,
                product.Name,
                product.Sku,
                product.SortOrder,
                product.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = product.Id });
        }).RequireAuthorization(Permissions.MenuManage);

        group.MapPut("/products/{productId:guid}/price", async (
            Guid productId,
            UpdateProductPriceRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();
            if (request.Amount < 0 || request.Amount > 1_000_000m)
                return Results.BadRequest(new { message = "Price must be between 0 and 1000000." });

            var product = await db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == productId && x.RestaurantId == restaurantId, ct);
            if (product is null)
                return Results.NotFound();

            var restaurant = await db.Restaurants
                .AsNoTracking()
                .FirstAsync(x => x.Id == restaurantId, ct);

            var now = DateTimeOffset.UtcNow;
            var activePrices = await db.ProductPrices
                .Where(x => x.RestaurantId == restaurantId &&
                            x.ProductId == productId &&
                            x.ValidFrom <= now &&
                            (x.ValidTo == null || x.ValidTo > now))
                .ToListAsync(ct);

            var current = activePrices
                .OrderByDescending(x => x.ValidFrom)
                .FirstOrDefault();
            if (current is not null && current.Amount == request.Amount && current.CurrencyCode == restaurant.CurrencyCode)
                return Results.Ok(new
                {
                    id = current.Id,
                    amount = current.Amount,
                    currencyCode = current.CurrencyCode,
                    validFrom = current.ValidFrom,
                    validTo = current.ValidTo
                });

            foreach (var price in activePrices)
                price.ValidTo = now;

            var nextPrice = new ProductPrice
            {
                RestaurantId = restaurantId,
                ProductId = productId,
                Amount = request.Amount,
                CurrencyCode = restaurant.CurrencyCode,
                ValidFrom = now
            };
            db.ProductPrices.Add(nextPrice);

            AddAudit(db, user, restaurantId, "PRODUCT_PRICE_CHANGED", "Product", productId, new
            {
                previousAmount = current?.Amount,
                amount = request.Amount,
                currencyCode = restaurant.CurrencyCode,
                validFrom = now
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = nextPrice.Id,
                amount = nextPrice.Amount,
                currencyCode = nextPrice.CurrencyCode,
                validFrom = nextPrice.ValidFrom,
                validTo = nextPrice.ValidTo
            });
        }).RequireAuthorization(Permissions.MenuManage);

        return app;
    }

    private static async Task<ProductValidationResult> ValidateProductRequest(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid? productId,
        Guid categoryId,
        Guid? kitchenStationId,
        string? rawName,
        string? rawSku,
        int sortOrder,
        decimal? price,
        CancellationToken ct)
    {
        var name = NormalizeName(rawName, 160);
        if (name is null)
            return ProductValidationResult.Fail(Results.BadRequest(new { message = "Product name is required and must be 160 characters or fewer." }));
        if (sortOrder < 0)
            return ProductValidationResult.Fail(Results.BadRequest(new { message = "Sort order cannot be negative." }));
        if (price is < 0 or > 1_000_000m)
            return ProductValidationResult.Fail(Results.BadRequest(new { message = "Price must be between 0 and 1000000." }));

        var categoryExists = await db.Categories.AnyAsync(
            x => x.Id == categoryId && x.RestaurantId == restaurantId,
            ct);
        if (!categoryExists)
            return ProductValidationResult.Fail(Results.BadRequest(new { message = "Category was not found." }));

        if (kitchenStationId is not null)
        {
            var stationExists = await db.KitchenStations.AnyAsync(
                x => x.Id == kitchenStationId && x.RestaurantId == restaurantId,
                ct);
            if (!stationExists)
                return ProductValidationResult.Fail(Results.BadRequest(new { message = "Kitchen station was not found." }));
        }

        var sku = NormalizeOptional(rawSku, 100);
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var duplicateSku = await db.Products.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != productId && x.Sku == sku,
                ct);
            if (duplicateSku)
                return ProductValidationResult.Fail(Results.Conflict(new { message = "A product with this SKU already exists." }));
        }

        return ProductValidationResult.Ok(name, sku);
    }

    private static object ToCategoryResponse(Category category) => new
    {
        id = category.Id,
        name = category.Name,
        sortOrder = category.SortOrder,
        isActive = category.IsActive
    };

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static string? NormalizeName(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength ? null : normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
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

    private sealed record ProductValidationResult(string? Name, string? Sku, IResult? Error)
    {
        public static ProductValidationResult Ok(string name, string? sku) => new(name, sku, null);
        public static ProductValidationResult Fail(IResult error) => new(null, null, error);
    }
}

public sealed record CreateCategoryRequest(string Name, int SortOrder = 0);
public sealed record UpdateCategoryRequest(string Name, int SortOrder, bool IsActive);
public sealed record CreateProductRequest(
    Guid CategoryId,
    Guid? KitchenStationId,
    string Name,
    string? Sku,
    int SortOrder,
    decimal Price);
public sealed record UpdateProductRequest(
    Guid CategoryId,
    Guid? KitchenStationId,
    string Name,
    string? Sku,
    int SortOrder,
    bool IsActive);
public sealed record UpdateProductPriceRequest(decimal Amount);
