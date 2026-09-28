using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.Orders;

public static class OrderAdjustmentPresetEndpoints
{
    public static IEndpointRouteBuilder MapOrderAdjustmentPresetEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/order-adjustment-presets", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(
                    user.FindFirstValue("restaurant_id"),
                    out var restaurantId) ||
                !Guid.TryParse(
                    user.FindFirstValue("employee_id"),
                    out var employeeId))
            {
                return Results.Unauthorized();
            }

            var roleId = await db.Employees
                .AsNoTracking()
                .Where(x =>
                    x.Id == employeeId &&
                    x.RestaurantId == restaurantId &&
                    x.IsActive)
                .Select(x => (Guid?)x.RoleId)
                .FirstOrDefaultAsync(ct);

            if (!roleId.HasValue)
                return Results.Unauthorized();

            var presets = await db.OrderAdjustmentPresets
                .AsNoTracking()
                .Include(x => x.Products)
                .Include(x => x.Categories)
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    x.ApplicationMode == OrderAdjustmentApplicationMode.Manual &&
                    x.AllowedRoles.Any(
                        role => role.RoleId == roleId.Value))
                .OrderBy(x => x.Type)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                presets = presets.Select(x => new
                {
                    x.Id,
                    x.Name,
                    type = ToEnumText(x.Type.ToString()),
                    mode = ToEnumText(x.Mode.ToString()),
                    scope = ToEnumText(x.Scope.ToString()),
                    applicationMode = ToEnumText(x.ApplicationMode.ToString()),
                    timeBasis = ToEnumText(x.TimeBasis.ToString()),
                    x.Value,
                    x.Priority,
                    x.CanStack,
                    x.WeekdayMask,
                    x.StartMinute,
                    x.EndMinute,
                    productIds = x.Products.Select(p => p.ProductId).ToArray(),
                    categoryIds = x.Categories.Select(category => category.CategoryId).ToArray(),
                    x.RequireComment
                })
            });
        }).RequireAuthorization(
            Permissions.OrdersAdjustmentsApply);

        return app;
    }

    private static string ToEnumText(string value)
    {
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]))
                result.Append('_');

            result.Append(char.ToUpperInvariant(value[index]));
        }

        return result.ToString();
    }
}
