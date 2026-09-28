using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeNomenclatureEndpoints
{
    private static readonly string[] SupportedTypes = ["DISH", "GOODS", "PREPARATION", "MODIFIER"];

    public static IEndpointRouteBuilder MapBackOfficeNomenclatureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/nomenclature")
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
                .FirstAsync(ct);

            var categories = await db.Categories
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive })
                .ToListAsync(ct);

            var preparationPlaces = await db.KitchenStations
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive })
                .ToListAsync(ct);

            var items = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Type)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    categoryId = x.CategoryId,
                    categoryName = x.CategoryId == null
                        ? null
                        : db.Categories.Where(c => c.Id == x.CategoryId).Select(c => c.Name).FirstOrDefault(),
                    kitchenStationId = x.KitchenStationId,
                    name = x.Name,
                    sku = x.Sku,
                    type = x.Type,
                    unit = x.Unit,
                    minStock = x.MinStock,
                    trackStock = x.TrackStock,
                    isSellable = x.IsSellable,
                    isActive = x.IsActive,
                    sortOrder = x.SortOrder,
                    currentPrice = db.ProductPrices
                        .Where(price =>
                            price.RestaurantId == restaurantId &&
                            price.ProductId == x.Id &&
                            price.ValidFrom <= now &&
                            (price.ValidTo == null || price.ValidTo > now))
                        .OrderByDescending(price => price.ValidFrom)
                        .Select(price => (decimal?)price.Amount)
                        .FirstOrDefault(),
                    recipe = db.RecipeLines
                        .Where(line => line.ProductId == x.Id)
                        .OrderBy(line => line.IngredientProduct!.Name)
                        .Select(line => new
                        {
                            id = line.Id,
                            ingredientProductId = line.IngredientProductId,
                            ingredientName = line.IngredientProduct!.Name,
                            ingredientUnit = line.IngredientProduct!.Unit,
                            quantity = line.Quantity
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                supportedTypes = SupportedTypes,
                currencyCode = restaurant.CurrencyCode,
                categories,
                preparationPlaces,
                items
            });
        });

        group.MapPost("/items", async (
            UpsertNomenclatureItemRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var validation = await ValidateItem(db, restaurantId, null, request, ct);
            if (validation.Error is not null)
                return validation.Error;

            var item = new Product
            {
                RestaurantId = restaurantId,
                CategoryId = request.CategoryId,
                KitchenStationId = request.KitchenStationId,
                Name = validation.Name!,
                Sku = validation.Sku,
                Type = validation.Type!,
                Unit = validation.Unit!,
                MinStock = request.MinStock,
                TrackStock = request.TrackStock,
                IsSellable = request.IsSellable,
                IsActive = true,
                SortOrder = request.SortOrder
            };

            db.Products.Add(item);

            if (request.IsSellable)
            {
                if (request.Price is null ||
                    (validation.Type != "MODIFIER" && request.Price < 0) ||
                    request.Price < -1_000_000m ||
                    request.Price > 1_000_000m)
                {
                    return Results.BadRequest(new
                    {
                        message = validation.Type == "MODIFIER"
                            ? "Для модификатора укажите изменение цены от -1000000 до 1000000."
                            : "Для продаваемой позиции укажите неотрицательную цену."
                    });
                }

                db.ProductPrices.Add(new ProductPrice
                {
                    RestaurantId = restaurantId,
                    ProductId = item.Id,
                    Amount = request.Price.Value,
                    CurrencyCode = await db.Restaurants
                        .Where(x => x.Id == restaurantId)
                        .Select(x => x.CurrencyCode)
                        .FirstAsync(ct),
                    ValidFrom = DateTimeOffset.UtcNow
                });
            }
            AddAudit(db, user, restaurantId, "NOMENCLATURE_ITEM_CREATED", "Product", item.Id, item);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/nomenclature/items/{item.Id}", new { id = item.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPut("/items/{itemId:guid}", async (
            Guid itemId,
            UpsertNomenclatureItemRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var item = await db.Products.FirstOrDefaultAsync(
                x => x.Id == itemId && x.RestaurantId == restaurantId,
                ct);
            if (item is null)
                return Results.NotFound();

            var validation = await ValidateItem(db, restaurantId, itemId, request, ct);
            if (validation.Error is not null)
                return validation.Error;

            item.CategoryId = request.CategoryId;
            item.KitchenStationId = request.KitchenStationId;
            item.Name = validation.Name!;
            item.Sku = validation.Sku;
            item.Type = validation.Type!;
            item.Unit = validation.Unit!;
            item.MinStock = request.MinStock;
            item.TrackStock = request.TrackStock;
            item.IsSellable = request.IsSellable;
            item.IsActive = request.IsActive;
            item.SortOrder = request.SortOrder;

            if (request.IsSellable)
            {
                if (request.Price is null ||
                    (validation.Type != "MODIFIER" && request.Price < 0) ||
                    request.Price < -1_000_000m ||
                    request.Price > 1_000_000m)
                {
                    return Results.BadRequest(new
                    {
                        message = validation.Type == "MODIFIER"
                            ? "Для модификатора укажите изменение цены от -1000000 до 1000000."
                            : "Для продаваемой позиции укажите неотрицательную цену."
                    });
                }

                var now = DateTimeOffset.UtcNow;
                var restaurant = await db.Restaurants
                    .AsNoTracking()
                    .Where(x => x.Id == restaurantId)
                    .Select(x => new { x.CurrencyCode })
                    .FirstAsync(ct);

                var activePrices = await db.ProductPrices
                    .Where(x =>
                        x.RestaurantId == restaurantId &&
                        x.ProductId == itemId &&
                        x.ValidFrom <= now &&
                        (x.ValidTo == null || x.ValidTo > now))
                    .ToListAsync(ct);

                var currentPrice = activePrices
                    .OrderByDescending(x => x.ValidFrom)
                    .FirstOrDefault();

                if (currentPrice is null || currentPrice.Amount != request.Price.Value)
                {
                    foreach (var price in activePrices)
                        price.ValidTo = now;

                    db.ProductPrices.Add(new ProductPrice
                    {
                        RestaurantId = restaurantId,
                        ProductId = itemId,
                        Amount = request.Price.Value,
                        CurrencyCode = restaurant.CurrencyCode,
                        ValidFrom = now
                    });
                }
            }

            AddAudit(db, user, restaurantId, "NOMENCLATURE_ITEM_UPDATED", "Product", item.Id, new
            {
                item.Name,
                item.Sku,
                item.Type,
                item.Unit,
                item.MinStock,
                item.TrackStock,
                item.IsSellable,
                item.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = item.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPut("/items/{itemId:guid}/recipe", async (
            Guid itemId,
            UpdateRecipeRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var product = await db.Products.FirstOrDefaultAsync(
                x => x.Id == itemId && x.RestaurantId == restaurantId,
                ct);
            if (product is null)
                return Results.NotFound();

            if (product.Type is not ("DISH" or "PREPARATION" or "MODIFIER"))
                return Results.BadRequest(new { message = "Техкарта доступна для блюда, заготовки или модификатора." });

            if (request.Lines.Count != request.Lines.Select(x => x.IngredientProductId).Distinct().Count())
                return Results.BadRequest(new { message = "Один ингредиент нельзя добавлять в техкарту дважды." });

            if (request.Lines.Any(x => x.IngredientProductId == itemId))
                return Results.BadRequest(new { message = "Позиция не может содержать саму себя." });

            if (request.Lines.Any(x => x.Quantity <= 0 || x.Quantity > 1_000_000m))
                return Results.BadRequest(new { message = "Количество ингредиента должно быть больше нуля." });

            var ingredientIds = request.Lines.Select(x => x.IngredientProductId).Distinct().ToArray();
            var validIngredientIds = await db.Products
                .Where(x => x.RestaurantId == restaurantId && x.IsActive && ingredientIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(ct);

            if (validIngredientIds.Count != ingredientIds.Length)
                return Results.BadRequest(new { message = "Один или несколько ингредиентов не найдены." });

            var existing = await db.RecipeLines
                .Where(x => x.ProductId == itemId)
                .ToListAsync(ct);
            db.RecipeLines.RemoveRange(existing);

            foreach (var line in request.Lines)
            {
                db.RecipeLines.Add(new RecipeLine
                {
                    RestaurantId = restaurantId,
                    ProductId = itemId,
                    IngredientProductId = line.IngredientProductId,
                    Quantity = line.Quantity
                });
            }

            AddAudit(db, user, restaurantId, "RECIPE_UPDATED", "Product", itemId, new
            {
                lines = request.Lines
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = itemId, lineCount = request.Lines.Count });
        }).RequireAuthorization(Permissions.InventoryManage);

        return app;
    }

    private static async Task<ItemValidationResult> ValidateItem(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid? itemId,
        UpsertNomenclatureItemRequest request,
        CancellationToken ct)
    {
        var name = NormalizeRequired(request.Name, 160);
        var type = request.Type?.Trim().ToUpperInvariant();
        var unit = NormalizeRequired(request.Unit, 20);
        var sku = NormalizeOptional(request.Sku, 100);

        if (name is null)
            return ItemValidationResult.Fail(Results.BadRequest(new { message = "Название обязательно." }));
        if (type is null || !SupportedTypes.Contains(type))
            return ItemValidationResult.Fail(Results.BadRequest(new { message = "Неверный тип номенклатуры." }));
        if (unit is null)
            return ItemValidationResult.Fail(Results.BadRequest(new { message = "Единица измерения обязательна." }));
        if (request.MinStock < 0)
            return ItemValidationResult.Fail(Results.BadRequest(new { message = "Минимальный остаток не может быть отрицательным." }));
        if (request.SortOrder < 0)
            return ItemValidationResult.Fail(Results.BadRequest(new { message = "Порядок сортировки не может быть отрицательным." }));

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var duplicateSku = await db.Products.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != itemId && x.Sku == sku,
                ct);
            if (duplicateSku)
                return ItemValidationResult.Fail(Results.Conflict(new { message = "Позиция с таким SKU уже существует." }));
        }

        if (request.IsSellable && type != "MODIFIER" && request.CategoryId is null)
            return ItemValidationResult.Fail(Results.BadRequest(new { message = "Для продаваемой позиции нужно выбрать категорию продажи." }));

        if (request.CategoryId is not null)
        {
            var categoryExists = await db.Categories.AnyAsync(
                x => x.Id == request.CategoryId && x.RestaurantId == restaurantId,
                ct);
            if (!categoryExists)
                return ItemValidationResult.Fail(Results.BadRequest(new { message = "Категория меню не найдена." }));
        }

        if (request.KitchenStationId is not null)
        {
            var stationExists = await db.KitchenStations.AnyAsync(
                x => x.Id == request.KitchenStationId && x.RestaurantId == restaurantId,
                ct);
            if (!stationExists)
                return ItemValidationResult.Fail(Results.BadRequest(new { message = "Тип места приготовления не найден." }));
        }

        return ItemValidationResult.Ok(name, sku, type, unit);
    }

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static string? NormalizeRequired(string? value, int maxLength)
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

    private sealed record ItemValidationResult(
        string? Name,
        string? Sku,
        string? Type,
        string? Unit,
        IResult? Error)
    {
        public static ItemValidationResult Ok(string name, string? sku, string type, string unit) =>
            new(name, sku, type, unit, null);

        public static ItemValidationResult Fail(IResult error) =>
            new(null, null, null, null, error);
    }
}

public sealed record UpsertNomenclatureItemRequest(
    string Name,
    string? Sku,
    string Type,
    string Unit,
    decimal MinStock,
    bool TrackStock,
    bool IsSellable,
    bool IsActive,
    int SortOrder,
    Guid? CategoryId,
    Guid? KitchenStationId,
    decimal? Price);

public sealed record RecipeLineRequest(Guid IngredientProductId, decimal Quantity);
public sealed record UpdateRecipeRequest(List<RecipeLineRequest> Lines);
