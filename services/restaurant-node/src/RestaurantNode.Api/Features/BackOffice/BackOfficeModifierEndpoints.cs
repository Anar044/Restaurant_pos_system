using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeModifierEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeModifierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/modifiers")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            var currencyCode = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => x.CurrencyCode)
                .FirstOrDefaultAsync(ct) ?? "AZN";

            var modifierRows = await db.Products
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == "MODIFIER")
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.IsActive,
                    PriceDelta = db.ProductPrices
                        .Where(price =>
                            price.RestaurantId == restaurantId &&
                            price.ProductId == x.Id &&
                            price.ValidFrom <= now &&
                            (price.ValidTo == null || price.ValidTo > now))
                        .OrderByDescending(price => price.ValidFrom)
                        .Select(price => (decimal?)price.Amount)
                        .FirstOrDefault() ?? 0m
                })
                .ToListAsync(ct);

            var groupRows = await db.ModifierGroups
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.MinSelections,
                    x.MaxSelections,
                    x.IsRequired,
                    x.IsActive
                })
                .ToListAsync(ct);

            var groupIds = groupRows.Select(x => x.Id).ToArray();
            var groupLinks = await db.ModifierGroupModifiers
                .AsNoTracking()
                .Where(x => groupIds.Contains(x.ModifierGroupId))
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            var productRows = await db.Products
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type != "MODIFIER" &&
                    x.IsSellable)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.IsActive
                })
                .ToListAsync(ct);

            var productIds = productRows.Select(x => x.Id).ToArray();
            var productLinks = await db.ProductModifierGroups
                .AsNoTracking()
                .Where(x => productIds.Contains(x.ProductId))
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            var modifiersById = modifierRows.ToDictionary(x => x.Id);

            return Results.Ok(new
            {
                currencyCode,
                modifiers = modifierRows.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    priceDelta = x.PriceDelta,
                    isActive = x.IsActive,
                    groupIds = groupLinks
                        .Where(link => link.ModifierId == x.Id)
                        .Select(link => link.ModifierGroupId)
                        .ToArray()
                }),
                groups = groupRows.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    minSelections = x.MinSelections,
                    maxSelections = x.MaxSelections,
                    isRequired = x.IsRequired,
                    isActive = x.IsActive,
                    modifiers = groupLinks
                        .Where(link => link.ModifierGroupId == x.Id)
                        .OrderBy(link => link.SortOrder)
                        .Select(link =>
                        {
                            var modifier = modifiersById.GetValueOrDefault(link.ModifierId);
                            return modifier is null
                                ? null
                                : new
                                {
                                    id = modifier.Id,
                                    name = modifier.Name,
                                    priceDelta = modifier.PriceDelta,
                                    isActive = modifier.IsActive,
                                    sortOrder = link.SortOrder
                                };
                        })
                        .Where(x => x is not null)
                        .ToArray(),
                    productIds = productLinks
                        .Where(link => link.ModifierGroupId == x.Id)
                        .Select(link => link.ProductId)
                        .ToArray()
                }),
                products = productRows.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    isActive = x.IsActive,
                    groupIds = productLinks
                        .Where(link => link.ProductId == x.Id)
                        .OrderBy(link => link.SortOrder)
                        .Select(link => link.ModifierGroupId)
                        .ToArray()
                })
            });
        });

        group.MapPost("/groups", async (
            CreateModifierGroupRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var validation = ValidateGroup(request.Name, request.MinSelections, request.MaxSelections, request.IsRequired);
            if (validation is not null)
                return validation;

            var name = request.Name.Trim();
            var duplicate = await db.ModifierGroups.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "Группа модификаторов с таким названием уже существует." });

            var entity = new ModifierGroup
            {
                RestaurantId = restaurantId,
                Name = name,
                MinSelections = request.MinSelections,
                MaxSelections = request.MaxSelections,
                IsRequired = request.IsRequired,
                IsActive = true
            };

            db.ModifierGroups.Add(entity);
            AddAudit(db, user, restaurantId, "MODIFIER_GROUP_CREATED", "ModifierGroup", entity.Id, new
            {
                entity.Name,
                entity.MinSelections,
                entity.MaxSelections,
                entity.IsRequired,
                entity.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/modifiers/groups/{entity.Id}", ToGroup(entity));
        }).RequireAuthorization(Permissions.MenuManage);

        group.MapPut("/groups/{groupId:guid}", async (
            Guid groupId,
            UpdateModifierGroupRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var validation = ValidateGroup(request.Name, request.MinSelections, request.MaxSelections, request.IsRequired);
            if (validation is not null)
                return validation;

            var entity = await db.ModifierGroups.FirstOrDefaultAsync(
                x => x.Id == groupId && x.RestaurantId == restaurantId,
                ct);
            if (entity is null)
                return Results.NotFound();

            var name = request.Name.Trim();
            var duplicate = await db.ModifierGroups.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != groupId && x.Name == name,
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "Группа модификаторов с таким названием уже существует." });

            entity.Name = name;
            entity.MinSelections = request.MinSelections;
            entity.MaxSelections = request.MaxSelections;
            entity.IsRequired = request.IsRequired;
            entity.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "MODIFIER_GROUP_UPDATED", "ModifierGroup", entity.Id, new
            {
                entity.Name,
                entity.MinSelections,
                entity.MaxSelections,
                entity.IsRequired,
                entity.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToGroup(entity));
        }).RequireAuthorization(Permissions.MenuManage);

        group.MapPut("/groups/{groupId:guid}/items", async (
            Guid groupId,
            SetGroupModifiersRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var groupEntity = await db.ModifierGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == groupId && x.RestaurantId == restaurantId, ct);
            if (groupEntity is null)
                return Results.NotFound();

            var ids = (request.ModifierIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            var validCount = await db.Products.CountAsync(
                x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == "MODIFIER" &&
                    ids.Contains(x.Id),
                ct);
            if (validCount != ids.Length)
                return Results.BadRequest(new { message = "Один или несколько модификаторов не найдены в номенклатуре." });

            var existing = await db.ModifierGroupModifiers
                .Where(x => x.ModifierGroupId == groupId)
                .ToListAsync(ct);
            db.ModifierGroupModifiers.RemoveRange(existing);

            for (var index = 0; index < ids.Length; index++)
            {
                db.ModifierGroupModifiers.Add(new ModifierGroupModifier
                {
                    ModifierGroupId = groupId,
                    ModifierId = ids[index],
                    SortOrder = index
                });
            }

            AddAudit(db, user, restaurantId, "MODIFIER_GROUP_ITEMS_CHANGED", "ModifierGroup", groupId, new
            {
                modifierIds = ids
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { groupId, modifierIds = ids });
        }).RequireAuthorization(Permissions.MenuManage);

        group.MapPut("/products/{productId:guid}/groups", async (
            Guid productId,
            SetProductModifierGroupsRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var productExists = await db.Products.AnyAsync(
                x =>
                    x.Id == productId &&
                    x.RestaurantId == restaurantId &&
                    x.Type != "MODIFIER",
                ct);
            if (!productExists)
                return Results.NotFound();

            var ids = (request.GroupIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            var validCount = await db.ModifierGroups.CountAsync(
                x => x.RestaurantId == restaurantId && ids.Contains(x.Id),
                ct);
            if (validCount != ids.Length)
                return Results.BadRequest(new { message = "Одна или несколько групп модификаторов не найдены." });

            var existing = await db.ProductModifierGroups
                .Where(x => x.ProductId == productId)
                .ToListAsync(ct);
            db.ProductModifierGroups.RemoveRange(existing);

            for (var index = 0; index < ids.Length; index++)
            {
                db.ProductModifierGroups.Add(new ProductModifierGroup
                {
                    ProductId = productId,
                    ModifierGroupId = ids[index],
                    SortOrder = index
                });
            }

            AddAudit(db, user, restaurantId, "PRODUCT_MODIFIER_GROUPS_CHANGED", "Product", productId, new
            {
                groupIds = ids
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { productId, groupIds = ids });
        }).RequireAuthorization(Permissions.MenuManage);

        return app;
    }

    private static IResult? ValidateGroup(string? rawName, int minSelections, int maxSelections, bool isRequired)
    {
        var name = rawName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Results.BadRequest(new { message = "Название группы обязательно и должно быть не длиннее 120 символов." });
        if (minSelections < 0)
            return Results.BadRequest(new { message = "Минимум выборов не может быть отрицательным." });
        if (maxSelections < 1 || maxSelections > 100)
            return Results.BadRequest(new { message = "Максимум выборов должен быть от 1 до 100." });
        if (minSelections > maxSelections)
            return Results.BadRequest(new { message = "Минимум не может быть больше максимума." });
        if (isRequired && minSelections < 1)
            return Results.BadRequest(new { message = "Для обязательной группы минимум должен быть не меньше 1." });

        return null;
    }

    private static bool TryRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static object ToGroup(ModifierGroup entity) => new
    {
        id = entity.Id,
        name = entity.Name,
        minSelections = entity.MinSelections,
        maxSelections = entity.MaxSelections,
        isRequired = entity.IsRequired,
        isActive = entity.IsActive
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
        Guid? employeeId = Guid.TryParse(user.FindFirstValue("employee_id"), out var parsed)
            ? parsed
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

public sealed record CreateModifierGroupRequest(
    string Name,
    int MinSelections,
    int MaxSelections,
    bool IsRequired);

public sealed record UpdateModifierGroupRequest(
    string Name,
    int MinSelections,
    int MaxSelections,
    bool IsRequired,
    bool IsActive);

public sealed record SetGroupModifiersRequest(Guid[]? ModifierIds);
public sealed record SetProductModifierGroupsRequest(Guid[]? GroupIds);
