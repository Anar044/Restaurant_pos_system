using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Features.Orders;

namespace RestaurantNode.Pricing.Tests;

public sealed class PricingEngineTests
{
    [Fact]
    public void ProductAndCategoryTargetsOnlyAffectMatchingItems()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var categoryA = Guid.NewGuid();
        var categoryB = Guid.NewGuid();

        var order = OrderWithItems(
            Item(productA, categoryA, 100m),
            Item(productB, categoryB, 50m));

        order.Adjustments.Add(Adjustment(
            type: OrderAdjustmentType.Discount,
            mode: OrderAdjustmentMode.Percent,
            value: 10m,
            priority: 10,
            productIds: [productA]));

        order.Adjustments.Add(Adjustment(
            type: OrderAdjustmentType.Discount,
            mode: OrderAdjustmentMode.Percent,
            value: 20m,
            priority: 20,
            categoryIds: [categoryB]));

        var result = OrderPricingCalculator.Recalculate(order);

        Assert.Equal(150m, result.Subtotal);
        Assert.Equal(20m, result.DiscountTotal);
        Assert.Equal(130m, result.Total);
    }

    [Fact]
    public void PriorityChangesResultWhenDiscountAndSurchargeAreReordered()
    {
        var product = Guid.NewGuid();
        var category = Guid.NewGuid();

        var surchargeFirst = OrderWithItems(
            Item(product, category, 100m));
        surchargeFirst.Adjustments.Add(Adjustment(
            OrderAdjustmentType.ServiceCharge,
            OrderAdjustmentMode.Percent,
            10m,
            10));
        surchargeFirst.Adjustments.Add(Adjustment(
            OrderAdjustmentType.Discount,
            OrderAdjustmentMode.Fixed,
            10m,
            20));

        var resultA = OrderPricingCalculator.Recalculate(
            surchargeFirst);

        var discountFirst = OrderWithItems(
            Item(product, category, 100m));
        discountFirst.Adjustments.Add(Adjustment(
            OrderAdjustmentType.Discount,
            OrderAdjustmentMode.Fixed,
            10m,
            10));
        discountFirst.Adjustments.Add(Adjustment(
            OrderAdjustmentType.ServiceCharge,
            OrderAdjustmentMode.Percent,
            10m,
            20));

        var resultB = OrderPricingCalculator.Recalculate(
            discountFirst);

        Assert.Equal(100m, resultA.Total);
        Assert.Equal(99m, resultB.Total);
    }

    [Fact]
    public void NonStackableRuleMakesFirstMatchingRuleWin()
    {
        var product = Guid.NewGuid();
        var category = Guid.NewGuid();
        var order = OrderWithItems(
            Item(product, category, 100m));

        order.Adjustments.Add(Adjustment(
            OrderAdjustmentType.Discount,
            OrderAdjustmentMode.Percent,
            10m,
            10,
            canStack: false));

        order.Adjustments.Add(Adjustment(
            OrderAdjustmentType.Discount,
            OrderAdjustmentMode.Percent,
            50m,
            20,
            canStack: true));

        var result = OrderPricingCalculator.Recalculate(order);

        Assert.Equal(10m, result.DiscountTotal);
        Assert.Equal(90m, result.Total);
    }

    [Fact]
    public void TimeBasisCanUseOrderOpeningOrItemAddedTime()
    {
        var product = Guid.NewGuid();
        var category = Guid.NewGuid();

        var orderOpened = AtBaku(
            2026, 10, 2, 15, 50);
        var itemAdded = AtBaku(
            2026, 10, 2, 16, 10);

        var order = OrderWithItems(
            Item(product, category, 100m, itemAdded));
        order.CreatedAt = orderOpened;

        order.Adjustments.Add(Adjustment(
            OrderAdjustmentType.Discount,
            OrderAdjustmentMode.Percent,
            10m,
            10,
            timeBasis: OrderAdjustmentTimeBasis.OrderOpenedAt,
            weekdayMask: 1 << 4,
            startMinute: 15 * 60,
            endMinute: 16 * 60));

        var openedResult =
            OrderPricingCalculator.Recalculate(order);

        Assert.Equal(90m, openedResult.Total);

        order.Adjustments.Clear();
        order.Adjustments.Add(Adjustment(
            OrderAdjustmentType.Discount,
            OrderAdjustmentMode.Percent,
            10m,
            10,
            timeBasis: OrderAdjustmentTimeBasis.ItemAddedAt,
            weekdayMask: 1 << 4,
            startMinute: 15 * 60,
            endMinute: 16 * 60));

        var itemResult =
            OrderPricingCalculator.Recalculate(order);

        Assert.Equal(100m, itemResult.Total);
    }

    [Fact]
    public void CrossMidnightScheduleUsesPreviousStartDay()
    {
        var fridayMask = 1 << 4;

        var saturdayOneAm = AtBaku(
            2026, 10, 3, 1, 0);

        Assert.True(PricingRuleEngine.MatchesSchedule(
            saturdayOneAm,
            fridayMask,
            22 * 60,
            2 * 60,
            "Asia/Baku"));

        var saturdayThreeAm = AtBaku(
            2026, 10, 3, 3, 0);

        Assert.False(PricingRuleEngine.MatchesSchedule(
            saturdayThreeAm,
            fridayMask,
            22 * 60,
            2 * 60,
            "Asia/Baku"));
    }

    private static Order OrderWithItems(
        params OrderItem[] items)
    {
        var order = new Order
        {
            RestaurantId = Guid.NewGuid(),
            CreatedByEmployeeId = Guid.NewGuid(),
            GuestCount = 1,
            CreatedAt = AtBaku(2026, 10, 2, 12, 0)
        };

        foreach (var item in items)
        {
            item.OrderId = order.Id;
            item.Order = order;
            order.Items.Add(item);
        }

        return order;
    }

    private static OrderItem Item(
        Guid productId,
        Guid categoryId,
        decimal total,
        DateTimeOffset? createdAt = null) =>
        new()
        {
            ProductId = productId,
            CategoryIdSnapshot = categoryId,
            ProductNameSnapshot = "Test",
            GuestNumber = 1,
            Quantity = 1m,
            UnitPrice = total,
            LineTotal = total,
            Status = OrderItemStatus.New,
            CreatedByEmployeeId = Guid.NewGuid(),
            CreatedAt = createdAt ??
                AtBaku(2026, 10, 2, 12, 0)
        };

    private static OrderAdjustment Adjustment(
        OrderAdjustmentType type,
        OrderAdjustmentMode mode,
        decimal value,
        int priority,
        bool canStack = true,
        OrderAdjustmentTimeBasis timeBasis =
            OrderAdjustmentTimeBasis.ItemAddedAt,
        int weekdayMask = 127,
        int? startMinute = null,
        int? endMinute = null,
        Guid[]? productIds = null,
        Guid[]? categoryIds = null) =>
        new()
        {
            Type = type,
            Mode = mode,
            Value = value,
            PrioritySnapshot = priority,
            CanStackSnapshot = canStack,
            ApplicationModeSnapshot =
                OrderAdjustmentApplicationMode.Manual,
            TimeBasisSnapshot = timeBasis,
            WeekdayMaskSnapshot = weekdayMask,
            StartMinuteSnapshot = startMinute,
            EndMinuteSnapshot = endMinute,
            TimeZoneIdSnapshot = "Asia/Baku",
            ProductIdsSnapshot = productIds ?? [],
            CategoryIdsSnapshot = categoryIds ?? [],
            AppliedByEmployeeId = Guid.NewGuid(),
            CreatedAt = AtBaku(2026, 10, 2, 12, 1),
            UpdatedAt = AtBaku(2026, 10, 2, 12, 1)
        };

    private static DateTimeOffset AtBaku(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new(
            year,
            month,
            day,
            hour,
            minute,
            0,
            TimeSpan.FromHours(4));
}
