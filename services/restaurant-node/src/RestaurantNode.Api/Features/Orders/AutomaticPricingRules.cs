using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Orders;

internal static class AutomaticPricingRules
{
    public static async Task SyncAsync(
        RestaurantDbContext db,
        Order order,
        Guid restaurantId,
        Guid employeeId,
        CancellationToken ct)
    {
        if (!order.Items.Any(x => x.Status != OrderItemStatus.Voided))
            return;

        var timeZoneId = await db.Restaurants
            .AsNoTracking()
            .Where(x => x.Id == restaurantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(ct) ?? "Asia/Baku";

        var presets = await db.OrderAdjustmentPresets
            .AsNoTracking()
            .Include(x => x.Products)
            .Include(x => x.Categories)
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.IsActive &&
                x.ApplicationMode == OrderAdjustmentApplicationMode.Automatic)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        foreach (var preset in presets)
        {
            if (order.Adjustments.Any(x => x.PresetId == preset.Id))
                continue;

            if (!PricingRuleEngine.PresetHasEligibleItems(
                    preset,
                    order,
                    timeZoneId))
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var adjustment = new OrderAdjustment
            {
                OrderId = order.Id,
                Order = order,
                GuestNumber = null,
                Reason = null,
                AppliedByEmployeeId = employeeId,
                CreatedAt = now,
                UpdatedAt = now
            };

            PricingRuleEngine.CopyPresetSnapshot(
                adjustment,
                preset,
                timeZoneId);

            order.Adjustments.Add(adjustment);
            db.OrderAdjustments.Add(adjustment);
        }
    }

    public static async Task<string> GetRestaurantTimeZoneAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        CancellationToken ct) =>
        await db.Restaurants
            .AsNoTracking()
            .Where(x => x.Id == restaurantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(ct) ?? "Asia/Baku";
}
