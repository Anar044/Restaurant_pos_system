using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeAdjustmentEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeAdjustmentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/adjustments")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var roles = await db.Roles
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    canApplyAdjustments =
                        x.Permissions.Contains(
                            Permissions.OrdersAdjustmentsApply)
                })
                .ToListAsync(ct);

            var categories = await db.Categories
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.IsActive
                })
                .ToListAsync(ct);

            var products = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.CategoryId,
                    x.Name,
                    x.IsActive
                })
                .ToListAsync(ct);

            var presets = await db.OrderAdjustmentPresets
                .AsNoTracking()
                .Include(x => x.AllowedRoles)
                .Include(x => x.Products)
                .Include(x => x.Categories)
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Priority)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                roles,
                categories,
                products,
                presets = presets.Select(ToResponse)
            });
        });

        group.MapPost("", async (
            UpsertAdjustmentPresetRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var validation = await ValidateRequest(
                request,
                restaurantId,
                null,
                db,
                ct);
            if (validation.Error is not null)
                return validation.Error;

            var now = DateTimeOffset.UtcNow;
            var preset = new OrderAdjustmentPreset
            {
                RestaurantId = restaurantId,
                Name = validation.Name!,
                Type = validation.Type!.Value,
                Mode = validation.Mode!.Value,
                Scope = validation.Scope!.Value,
                ApplicationMode = validation.ApplicationMode!.Value,
                TimeBasis = validation.TimeBasis!.Value,
                TargetMode = validation.TargetMode!.Value,
                Value = Money(request.Value),
                Priority = request.Priority,
                CanStack = request.CanStack,
                WeekdayMask = request.WeekdayMask,
                StartMinute = request.StartMinute,
                EndMinute = request.EndMinute,
                RequireComment =
                    validation.ApplicationMode ==
                    OrderAdjustmentApplicationMode.Manual &&
                    request.RequireComment,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now
            };

            ApplyLinks(
                preset,
                validation.RoleIds,
                validation.ProductIds,
                validation.CategoryIds);

            db.OrderAdjustmentPresets.Add(preset);
            AddAudit(
                db,
                user,
                restaurantId,
                "ADJUSTMENT_PRESET_CREATED",
                preset);
            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/v1/backoffice/adjustments/{preset.Id}",
                ToResponse(preset));
        }).RequireAuthorization(Permissions.PricingManage);

        group.MapPut("/{presetId:guid}", async (
            Guid presetId,
            UpsertAdjustmentPresetRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var preset = await db.OrderAdjustmentPresets
                .Include(x => x.AllowedRoles)
                .Include(x => x.Products)
                .Include(x => x.Categories)
                .FirstOrDefaultAsync(
                    x => x.Id == presetId &&
                         x.RestaurantId == restaurantId,
                    ct);

            if (preset is null)
                return Results.NotFound();

            var validation = await ValidateRequest(
                request,
                restaurantId,
                presetId,
                db,
                ct);
            if (validation.Error is not null)
                return validation.Error;

            preset.Name = validation.Name!;
            preset.Type = validation.Type!.Value;
            preset.Mode = validation.Mode!.Value;
            preset.Scope = validation.Scope!.Value;
            preset.ApplicationMode =
                validation.ApplicationMode!.Value;
            preset.TimeBasis = validation.TimeBasis!.Value;
            preset.TargetMode = validation.TargetMode!.Value;
            preset.Value = Money(request.Value);
            preset.Priority = request.Priority;
            preset.CanStack = request.CanStack;
            preset.WeekdayMask = request.WeekdayMask;
            preset.StartMinute = request.StartMinute;
            preset.EndMinute = request.EndMinute;
            preset.RequireComment =
                preset.ApplicationMode ==
                OrderAdjustmentApplicationMode.Manual &&
                request.RequireComment;
            preset.IsActive = request.IsActive;
            preset.UpdatedAt = DateTimeOffset.UtcNow;

            ReplaceLinks(
                db,
                preset,
                validation.RoleIds,
                validation.ProductIds,
                validation.CategoryIds);

            AddAudit(
                db,
                user,
                restaurantId,
                "ADJUSTMENT_PRESET_UPDATED",
                preset);
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(preset));
        }).RequireAuthorization(Permissions.PricingManage);

        return app;
    }

    private static async Task<ValidationResult> ValidateRequest(
        UpsertAdjustmentPresetRequest request,
        Guid restaurantId,
        Guid? existingPresetId,
        RestaurantDbContext db,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > 120)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message =
                        "Название обязательно и не должно превышать 120 символов."
                }));
        }

        if (!TryEnum(request.Type, out OrderAdjustmentType type) ||
            !TryEnum(request.Mode, out OrderAdjustmentMode mode) ||
            !TryEnum(request.Scope, out OrderAdjustmentScope scope) ||
            !TryEnum(
                request.ApplicationMode,
                out OrderAdjustmentApplicationMode applicationMode) ||
            !TryEnum(
                request.TimeBasis,
                out OrderAdjustmentTimeBasis timeBasis) ||
            !TryEnum(
                request.TargetMode,
                out OrderAdjustmentTargetMode targetMode))
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Некорректный тип, режим, область, цель или режим времени правила."
                }));
        }

        if (type == OrderAdjustmentType.ServiceCharge &&
            scope != OrderAdjustmentScope.Order)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message =
                        "Надбавка/сервис применяется ко всему заказу; ограничение по блюдам задаётся отдельным блоком целей."
                }));
        }

        if (applicationMode ==
                OrderAdjustmentApplicationMode.Automatic &&
            scope != OrderAdjustmentScope.Order)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message =
                        "Автоматическое правило должно иметь область «весь заказ»."
                }));
        }

        if (applicationMode ==
                OrderAdjustmentApplicationMode.Automatic &&
            targetMode == OrderAdjustmentTargetMode.PosSelection)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message =
                        "Автоматическое правило не может требовать выбора позиций кассиром."
                }));
        }

        if (request.Value <= 0m ||
            request.Value > 1_000_000m ||
            (mode == OrderAdjustmentMode.Percent &&
             request.Value > 100m))
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = mode == OrderAdjustmentMode.Percent
                        ? "Процент должен быть больше 0 и не больше 100."
                        : "Сумма должна быть больше 0."
                }));
        }

        if (request.Priority is < 0 or > 9999)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Приоритет должен быть от 0 до 9999."
                }));
        }

        if (request.WeekdayMask is < 1 or > 127)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Выберите хотя бы один день недели."
                }));
        }

        if (request.StartMinute.HasValue !=
            request.EndMinute.HasValue)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message =
                        "Для временного окна нужно указать и начало, и конец."
                }));
        }

        if (request.StartMinute is < 0 or > 1439 ||
            request.EndMinute is < 0 or > 1439)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message =
                        "Время должно находиться в диапазоне суток."
                }));
        }

        var duplicate = await db.OrderAdjustmentPresets
            .AnyAsync(
                x =>
                    x.RestaurantId == restaurantId &&
                    x.Id != existingPresetId &&
                    x.Name.ToLower() == name.ToLower(),
                ct);

        if (duplicate)
        {
            return ValidationResult.Fail(
                Results.Conflict(new
                {
                    message =
                        "Правило с таким названием уже существует."
                }));
        }

        var roleIds = applicationMode ==
                OrderAdjustmentApplicationMode.Manual
            ? (request.RoleIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray()
            : [];

        if (roleIds.Length > 0)
        {
            var selectedRoles = await db.Roles
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    roleIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Permissions
                })
                .ToListAsync(ct);

            if (selectedRoles.Count != roleIds.Length)
            {
                return ValidationResult.Fail(
                    Results.BadRequest(new
                    {
                        message =
                            "Одна или несколько выбранных ролей не найдены."
                    }));
            }

            var forbiddenRole = selectedRoles.FirstOrDefault(
                x => !x.Permissions.Contains(
                    Permissions.OrdersAdjustmentsApply));

            if (forbiddenRole is not null)
            {
                return ValidationResult.Fail(
                    Results.BadRequest(new
                    {
                        message =
                            $"Роль «{forbiddenRole.Name}» не имеет права применять скидки и надбавки."
                    }));
            }
        }

        var productIds = targetMode ==
                OrderAdjustmentTargetMode.PresetSelection
            ? (request.ProductIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray()
            : [];

        if (productIds.Length > 0)
        {
            var count = await db.Products.CountAsync(
                x =>
                    x.RestaurantId == restaurantId &&
                    productIds.Contains(x.Id),
                ct);

            if (count != productIds.Length)
            {
                return ValidationResult.Fail(
                    Results.BadRequest(new
                    {
                        message =
                            "Одно или несколько выбранных блюд не найдены."
                    }));
            }
        }

        var categoryIds = targetMode ==
                OrderAdjustmentTargetMode.PresetSelection
            ? (request.CategoryIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray()
            : [];

        if (categoryIds.Length > 0)
        {
            var count = await db.Categories.CountAsync(
                x =>
                    x.RestaurantId == restaurantId &&
                    categoryIds.Contains(x.Id),
                ct);

            if (count != categoryIds.Length)
            {
                return ValidationResult.Fail(
                    Results.BadRequest(new
                    {
                        message =
                            "Одна или несколько выбранных категорий не найдены."
                    }));
            }
        }

        if (targetMode ==
                OrderAdjustmentTargetMode.PresetSelection &&
            productIds.Length == 0 &&
            categoryIds.Length == 0)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message =
                        "Для режима «Заданные категории / блюда» выберите хотя бы одну категорию или блюдо."
                }));
        }

        return new ValidationResult(
            name,
            type,
            mode,
            scope,
            applicationMode,
            timeBasis,
            targetMode,
            roleIds,
            productIds,
            categoryIds,
            null);
    }

    private static void ApplyLinks(
        OrderAdjustmentPreset preset,
        IReadOnlyCollection<Guid> roleIds,
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> categoryIds)
    {
        foreach (var roleId in roleIds)
        {
            preset.AllowedRoles.Add(
                new OrderAdjustmentPresetRole
                {
                    PresetId = preset.Id,
                    RoleId = roleId
                });
        }

        foreach (var productId in productIds)
        {
            preset.Products.Add(
                new OrderAdjustmentPresetProduct
                {
                    PresetId = preset.Id,
                    ProductId = productId
                });
        }

        foreach (var categoryId in categoryIds)
        {
            preset.Categories.Add(
                new OrderAdjustmentPresetCategory
                {
                    PresetId = preset.Id,
                    CategoryId = categoryId
                });
        }
    }

    private static void ReplaceLinks(
        RestaurantDbContext db,
        OrderAdjustmentPreset preset,
        IReadOnlyCollection<Guid> roleIds,
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> categoryIds)
    {
        ReplaceRoleLinks(db, preset, roleIds);
        ReplaceProductLinks(db, preset, productIds);
        ReplaceCategoryLinks(db, preset, categoryIds);
    }

    private static void ReplaceRoleLinks(
        RestaurantDbContext db,
        OrderAdjustmentPreset preset,
        IReadOnlyCollection<Guid> requestedIds)
    {
        var requested = requestedIds.ToHashSet();
        foreach (var link in preset.AllowedRoles
                     .Where(x => !requested.Contains(x.RoleId))
                     .ToArray())
        {
            preset.AllowedRoles.Remove(link);
            db.OrderAdjustmentPresetRoles.Remove(link);
        }

        var existing = preset.AllowedRoles
            .Select(x => x.RoleId)
            .ToHashSet();

        foreach (var id in requested.Where(x => !existing.Contains(x)))
        {
            preset.AllowedRoles.Add(
                new OrderAdjustmentPresetRole
                {
                    PresetId = preset.Id,
                    RoleId = id
                });
        }
    }

    private static void ReplaceProductLinks(
        RestaurantDbContext db,
        OrderAdjustmentPreset preset,
        IReadOnlyCollection<Guid> requestedIds)
    {
        var requested = requestedIds.ToHashSet();
        foreach (var link in preset.Products
                     .Where(x => !requested.Contains(x.ProductId))
                     .ToArray())
        {
            preset.Products.Remove(link);
            db.OrderAdjustmentPresetProducts.Remove(link);
        }

        var existing = preset.Products
            .Select(x => x.ProductId)
            .ToHashSet();

        foreach (var id in requested.Where(x => !existing.Contains(x)))
        {
            preset.Products.Add(
                new OrderAdjustmentPresetProduct
                {
                    PresetId = preset.Id,
                    ProductId = id
                });
        }
    }

    private static void ReplaceCategoryLinks(
        RestaurantDbContext db,
        OrderAdjustmentPreset preset,
        IReadOnlyCollection<Guid> requestedIds)
    {
        var requested = requestedIds.ToHashSet();
        foreach (var link in preset.Categories
                     .Where(x => !requested.Contains(x.CategoryId))
                     .ToArray())
        {
            preset.Categories.Remove(link);
            db.OrderAdjustmentPresetCategories.Remove(link);
        }

        var existing = preset.Categories
            .Select(x => x.CategoryId)
            .ToHashSet();

        foreach (var id in requested.Where(x => !existing.Contains(x)))
        {
            preset.Categories.Add(
                new OrderAdjustmentPresetCategory
                {
                    PresetId = preset.Id,
                    CategoryId = id
                });
        }
    }

    private static object ToResponse(
        OrderAdjustmentPreset preset) => new
    {
        preset.Id,
        preset.Name,
        type = EnumText(preset.Type),
        mode = EnumText(preset.Mode),
        scope = EnumText(preset.Scope),
        applicationMode = EnumText(preset.ApplicationMode),
        timeBasis = EnumText(preset.TimeBasis),
        targetMode = EnumText(preset.TargetMode),
        preset.Value,
        preset.Priority,
        preset.CanStack,
        preset.WeekdayMask,
        preset.StartMinute,
        preset.EndMinute,
        preset.RequireComment,
        preset.IsActive,
        roleIds = preset.AllowedRoles
            .Select(x => x.RoleId)
            .OrderBy(x => x)
            .ToArray(),
        productIds = preset.Products
            .Select(x => x.ProductId)
            .OrderBy(x => x)
            .ToArray(),
        categoryIds = preset.Categories
            .Select(x => x.CategoryId)
            .OrderBy(x => x)
            .ToArray(),
        preset.CreatedAt,
        preset.UpdatedAt
    };

    private static void AddAudit(
        RestaurantDbContext db,
        ClaimsPrincipal user,
        Guid restaurantId,
        string eventType,
        OrderAdjustmentPreset preset)
    {
        Guid.TryParse(
            user.FindFirstValue("employee_id"),
            out var employeeId);

        db.AuditEvents.Add(new AuditEvent
        {
            RestaurantId = restaurantId,
            EmployeeId =
                employeeId == Guid.Empty ? null : employeeId,
            EventType = eventType,
            EntityType = "OrderAdjustmentPreset",
            EntityId = preset.Id,
            PayloadJson = JsonSerializer.Serialize(
                ToResponse(preset))
        });
    }

    private static bool TryRestaurantId(
        ClaimsPrincipal user,
        out Guid restaurantId) =>
        Guid.TryParse(
            user.FindFirstValue("restaurant_id"),
            out restaurantId);

    private static bool TryEnum<TEnum>(
        string? raw,
        out TEnum value)
        where TEnum : struct, Enum
    {
        var normalized = raw?.Trim()
            .Replace("-", "_")
            .Replace("_", string.Empty);

        foreach (var item in Enum.GetValues<TEnum>())
        {
            if (string.Equals(
                    item.ToString(),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = item;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string EnumText<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var text = value.ToString();
        var result = new System.Text.StringBuilder();

        for (var index = 0; index < text.Length; index++)
        {
            if (index > 0 &&
                char.IsUpper(text[index]))
            {
                result.Append('_');
            }

            result.Append(
                char.ToUpperInvariant(text[index]));
        }

        return result.ToString();
    }

    private static decimal Money(decimal value) =>
        decimal.Round(
            value,
            4,
            MidpointRounding.AwayFromZero);

    private sealed record ValidationResult(
        string? Name,
        OrderAdjustmentType? Type,
        OrderAdjustmentMode? Mode,
        OrderAdjustmentScope? Scope,
        OrderAdjustmentApplicationMode? ApplicationMode,
        OrderAdjustmentTimeBasis? TimeBasis,
        OrderAdjustmentTargetMode? TargetMode,
        Guid[] RoleIds,
        Guid[] ProductIds,
        Guid[] CategoryIds,
        IResult? Error)
    {
        public static ValidationResult Fail(
            IResult error) =>
            new(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                [],
                error);
    }
}

public sealed record UpsertAdjustmentPresetRequest(
    string Name,
    string Type,
    string Mode,
    string Scope,
    string ApplicationMode,
    string TimeBasis,
    string TargetMode,
    decimal Value,
    int Priority,
    bool CanStack,
    int WeekdayMask,
    int? StartMinute,
    int? EndMinute,
    bool RequireComment,
    bool IsActive,
    Guid[]? RoleIds,
    Guid[]? ProductIds,
    Guid[]? CategoryIds);
