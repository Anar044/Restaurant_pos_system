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
                        x.Permissions.Contains(Permissions.OrdersAdjustmentsApply)
                })
                .ToListAsync(ct);

            var presets = await db.OrderAdjustmentPresets
                .AsNoTracking()
                .Include(x => x.AllowedRoles)
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                roles,
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

            var preset = new OrderAdjustmentPreset
            {
                RestaurantId = restaurantId,
                Name = validation.Name!,
                Type = validation.Type!.Value,
                Mode = validation.Mode!.Value,
                Scope = validation.Scope!.Value,
                Value = Money(request.Value),
                RequireComment = request.RequireComment,
                IsActive = request.IsActive,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            foreach (var roleId in validation.RoleIds)
            {
                preset.AllowedRoles.Add(new OrderAdjustmentPresetRole
                {
                    PresetId = preset.Id,
                    RoleId = roleId
                });
            }

            db.OrderAdjustmentPresets.Add(preset);
            AddAudit(
                db,
                user,
                restaurantId,
                "ADJUSTMENT_PRESET_CREATED",
                preset,
                validation.RoleIds);
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
            preset.Value = Money(request.Value);
            preset.RequireComment = request.RequireComment;
            preset.IsActive = request.IsActive;
            preset.UpdatedAt = DateTimeOffset.UtcNow;

            var requestedRoles = validation.RoleIds.ToHashSet();
            foreach (var link in preset.AllowedRoles
                         .Where(x => !requestedRoles.Contains(x.RoleId))
                         .ToArray())
            {
                preset.AllowedRoles.Remove(link);
                db.OrderAdjustmentPresetRoles.Remove(link);
            }

            var existingRoles = preset.AllowedRoles
                .Select(x => x.RoleId)
                .ToHashSet();

            foreach (var roleId in validation.RoleIds.Where(
                         x => !existingRoles.Contains(x)))
            {
                preset.AllowedRoles.Add(new OrderAdjustmentPresetRole
                {
                    PresetId = preset.Id,
                    RoleId = roleId
                });
            }

            AddAudit(
                db,
                user,
                restaurantId,
                "ADJUSTMENT_PRESET_UPDATED",
                preset,
                validation.RoleIds);
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
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Название обязательно и не должно превышать 120 символов."
                }));
        }

        if (!TryType(request.Type, out var type))
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Тип должен быть DISCOUNT или SERVICE_CHARGE."
                }));
        }

        if (!TryMode(request.Mode, out var mode))
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Режим должен быть PERCENT или FIXED."
                }));
        }

        if (!TryScope(request.Scope, out var scope))
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Область должна быть ORDER, GUEST или BOTH."
                }));
        }

        if (type == OrderAdjustmentType.ServiceCharge &&
            scope != OrderAdjustmentScope.Order)
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = "Сервисный сбор пока применяется только ко всему заказу."
                }));
        }

        if (request.Value <= 0m ||
            request.Value > 1_000_000m ||
            (mode == OrderAdjustmentMode.Percent && request.Value > 100m))
        {
            return ValidationResult.Fail(
                Results.BadRequest(new
                {
                    message = mode == OrderAdjustmentMode.Percent
                        ? "Процент должен быть больше 0 и не больше 100."
                        : "Сумма должна быть больше 0."
                }));
        }

        var duplicate = await db.OrderAdjustmentPresets.AnyAsync(
            x => x.RestaurantId == restaurantId &&
                 x.Id != existingPresetId &&
                 x.Name.ToLower() == name.ToLower(),
            ct);

        if (duplicate)
        {
            return ValidationResult.Fail(
                Results.Conflict(new
                {
                    message = "Правило с таким названием уже существует."
                }));
        }

        var roleIds = (request.RoleIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

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
                        message = "Одна или несколько выбранных ролей не найдены."
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

        return new ValidationResult(
            name,
            type,
            mode,
            scope,
            roleIds,
            null);
    }

    private static object ToResponse(OrderAdjustmentPreset preset) => new
    {
        preset.Id,
        preset.Name,
        type = EnumText(preset.Type),
        mode = EnumText(preset.Mode),
        scope = EnumText(preset.Scope),
        preset.Value,
        preset.RequireComment,
        preset.IsActive,
        roleIds = preset.AllowedRoles
            .Select(x => x.RoleId)
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
        OrderAdjustmentPreset preset,
        IReadOnlyCollection<Guid> roleIds)
    {
        Guid.TryParse(
            user.FindFirstValue("employee_id"),
            out var employeeId);

        db.AuditEvents.Add(new AuditEvent
        {
            RestaurantId = restaurantId,
            EmployeeId = employeeId == Guid.Empty ? null : employeeId,
            EventType = eventType,
            EntityType = "OrderAdjustmentPreset",
            EntityId = preset.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                preset.Name,
                type = EnumText(preset.Type),
                mode = EnumText(preset.Mode),
                scope = EnumText(preset.Scope),
                preset.Value,
                preset.RequireComment,
                preset.IsActive,
                roleIds
            })
        });
    }

    private static bool TryRestaurantId(
        ClaimsPrincipal user,
        out Guid restaurantId) =>
        Guid.TryParse(
            user.FindFirstValue("restaurant_id"),
            out restaurantId);

    private static bool TryType(
        string? raw,
        out OrderAdjustmentType value) =>
        TryEnum(raw, out value);

    private static bool TryMode(
        string? raw,
        out OrderAdjustmentMode value) =>
        TryEnum(raw, out value);

    private static bool TryScope(
        string? raw,
        out OrderAdjustmentScope value) =>
        TryEnum(raw, out value);

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
            if (index > 0 && char.IsUpper(text[index]))
                result.Append('_');

            result.Append(char.ToUpperInvariant(text[index]));
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
        Guid[] RoleIds,
        IResult? Error)
    {
        public static ValidationResult Fail(IResult error) =>
            new(
                null,
                null,
                null,
                null,
                [],
                error);
    }
}

public sealed record UpsertAdjustmentPresetRequest(
    string Name,
    string Type,
    string Mode,
    string Scope,
    decimal Value,
    bool RequireComment,
    bool IsActive,
    Guid[]? RoleIds);
