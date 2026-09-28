using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Features.Orders;

internal static class PricingRuleEngine
{
    public static void CopyPresetSnapshot(
        OrderAdjustment target,
        OrderAdjustmentPreset preset,
        string timeZoneId,
        IReadOnlyCollection<Guid>? selectedOrderItemIds = null)
    {
        target.PresetId = preset.Id;
        target.PresetNameSnapshot = preset.Name;
        target.Type = preset.Type;
        target.Mode = preset.Mode;
        target.ApplicationModeSnapshot = preset.ApplicationMode;
        target.TimeBasisSnapshot = preset.TimeBasis;
        target.TargetModeSnapshot = preset.TargetMode;
        target.PrioritySnapshot = preset.Priority;
        target.CanStackSnapshot = preset.CanStack;
        target.WeekdayMaskSnapshot = preset.WeekdayMask;
        target.StartMinuteSnapshot = preset.StartMinute;
        target.EndMinuteSnapshot = preset.EndMinute;
        target.TimeZoneIdSnapshot = string.IsNullOrWhiteSpace(timeZoneId)
            ? "Asia/Baku"
            : timeZoneId;

        if (preset.TargetMode == OrderAdjustmentTargetMode.PresetSelection)
        {
            target.ProductIdsSnapshot = preset.Products
                .Select(x => x.ProductId)
                .Distinct()
                .ToArray();
            target.CategoryIdsSnapshot = preset.Categories
                .Select(x => x.CategoryId)
                .Distinct()
                .ToArray();
            target.OrderItemIdsSnapshot = [];
        }
        else if (preset.TargetMode == OrderAdjustmentTargetMode.PosSelection)
        {
            target.ProductIdsSnapshot = [];
            target.CategoryIdsSnapshot = [];
            target.OrderItemIdsSnapshot = (selectedOrderItemIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();
        }
        else
        {
            target.ProductIdsSnapshot = [];
            target.CategoryIdsSnapshot = [];
            target.OrderItemIdsSnapshot = [];
        }

        target.Value = Money(preset.Value);
    }

    public static bool PresetHasEligibleItems(
        OrderAdjustmentPreset preset,
        Order order,
        string timeZoneId,
        int? guestNumber = null)
    {
        foreach (var item in order.Items)
        {
            if (item.Status == OrderItemStatus.Voided)
                continue;
            if (guestNumber.HasValue && item.GuestNumber != guestNumber.Value)
                continue;
            if (IsPresetItemEligible(
                    preset,
                    order,
                    item,
                    timeZoneId,
                    guestNumber))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPresetItemEligible(
        OrderAdjustmentPreset preset,
        Order order,
        OrderItem item,
        string timeZoneId,
        int? guestNumber = null)
    {
        if (item.Status == OrderItemStatus.Voided)
            return false;

        if (guestNumber.HasValue &&
            item.GuestNumber != guestNumber.Value)
        {
            return false;
        }

        var targetMatches = preset.TargetMode switch
        {
            OrderAdjustmentTargetMode.AllItems => true,
            OrderAdjustmentTargetMode.PresetSelection => MatchesTargets(
                item,
                preset.Products.Select(x => x.ProductId),
                preset.Categories.Select(x => x.CategoryId)),
            OrderAdjustmentTargetMode.PosSelection => true,
            _ => false
        };

        if (!targetMatches)
            return false;

        var instant = preset.TimeBasis ==
                OrderAdjustmentTimeBasis.OrderOpenedAt
            ? order.CreatedAt
            : item.CreatedAt;

        return MatchesSchedule(
            instant,
            preset.WeekdayMask,
            preset.StartMinute,
            preset.EndMinute,
            timeZoneId);
    }

    public static bool AdjustmentMatchesItem(
        OrderAdjustment adjustment,
        Order order,
        OrderItem item)
    {
        if (item.Status == OrderItemStatus.Voided)
            return false;

        if (adjustment.GuestNumber.HasValue &&
            item.GuestNumber != adjustment.GuestNumber.Value)
        {
            return false;
        }

        var targetMatches = adjustment.TargetModeSnapshot switch
        {
            OrderAdjustmentTargetMode.AllItems => true,
            OrderAdjustmentTargetMode.PresetSelection => MatchesTargets(
                item,
                adjustment.ProductIdsSnapshot,
                adjustment.CategoryIdsSnapshot),
            OrderAdjustmentTargetMode.PosSelection =>
                adjustment.OrderItemIdsSnapshot.Contains(item.Id),
            _ => false
        };

        if (!targetMatches)
            return false;

        var instant =
            adjustment.TimeBasisSnapshot == OrderAdjustmentTimeBasis.OrderOpenedAt
                ? order.CreatedAt
                : item.CreatedAt;

        return MatchesSchedule(
            instant,
            adjustment.WeekdayMaskSnapshot,
            adjustment.StartMinuteSnapshot,
            adjustment.EndMinuteSnapshot,
            adjustment.TimeZoneIdSnapshot);
    }

    public static bool MatchesSchedule(
        DateTimeOffset instantUtc,
        int weekdayMask,
        int? startMinute,
        int? endMinute,
        string timeZoneId)
    {
        var zone = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(instantUtc, zone);
        var minute = local.Hour * 60 + local.Minute;

        if (!startMinute.HasValue || !endMinute.HasValue)
            return IsDayEnabled(weekdayMask, local.DayOfWeek);

        var start = Math.Clamp(startMinute.Value, 0, 1439);
        var end = Math.Clamp(endMinute.Value, 0, 1439);

        if (start == end)
            return IsDayEnabled(weekdayMask, local.DayOfWeek);

        if (start < end)
        {
            return IsDayEnabled(weekdayMask, local.DayOfWeek) &&
                   minute >= start &&
                   minute < end;
        }

        // Window crosses midnight, e.g. Friday 22:00 -> Saturday 02:00.
        if (minute >= start)
            return IsDayEnabled(weekdayMask, local.DayOfWeek);

        if (minute < end)
        {
            var previous = local.AddDays(-1).DayOfWeek;
            return IsDayEnabled(weekdayMask, previous);
        }

        return false;
    }

    private static bool MatchesTargets(
        OrderItem item,
        IEnumerable<Guid> productIds,
        IEnumerable<Guid> categoryIds)
    {
        var products = productIds as ICollection<Guid> ?? productIds.ToArray();
        var categories = categoryIds as ICollection<Guid> ?? categoryIds.ToArray();

        if (products.Count == 0 && categories.Count == 0)
            return true;

        return products.Contains(item.ProductId) ||
               categories.Contains(item.CategoryIdSnapshot);
    }

    private static bool IsDayEnabled(
        int weekdayMask,
        DayOfWeek day)
    {
        var index = day switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };

        return (weekdayMask & (1 << index)) != 0;
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
