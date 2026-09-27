using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Features.Orders;

internal sealed record OrderPricingSnapshot(
    decimal Subtotal,
    decimal DiscountTotal,
    decimal SurchargeTotal,
    decimal Total,
    IReadOnlyDictionary<int, decimal> GuestSubtotals,
    IReadOnlyDictionary<int, decimal> GuestDiscounts,
    IReadOnlyDictionary<int, decimal> GuestTotals);

internal static class OrderPricingCalculator
{
    public static OrderPricingSnapshot Recalculate(Order order)
    {
        var snapshot = Calculate(order, updateAdjustmentAmounts: true);
        order.Subtotal = snapshot.Subtotal;
        order.DiscountTotal = snapshot.DiscountTotal;
        order.SurchargeTotal = snapshot.SurchargeTotal;
        order.Total = snapshot.Total;
        return snapshot;
    }

    public static OrderPricingSnapshot Calculate(Order order) =>
        Calculate(order, updateAdjustmentAmounts: false);

    private static OrderPricingSnapshot Calculate(
        Order order,
        bool updateAdjustmentAmounts)
    {
        var guestCount = Math.Max(1, order.GuestCount);
        var guestSubtotals = Enumerable.Range(1, guestCount)
            .ToDictionary(guest => guest, _ => 0m);

        foreach (var item in order.Items.Where(x => x.Status != OrderItemStatus.Voided))
        {
            if (item.GuestNumber >= 1 && item.GuestNumber <= guestCount)
                guestSubtotals[item.GuestNumber] += Money(item.LineTotal);
        }

        foreach (var guest in guestSubtotals.Keys.ToArray())
            guestSubtotals[guest] = Money(guestSubtotals[guest]);

        var guestNet = guestSubtotals.ToDictionary(x => x.Key, x => x.Value);
        var guestDiscounts = Enumerable.Range(1, guestCount)
            .ToDictionary(guest => guest, _ => 0m);

        decimal discountTotal = 0m;
        decimal surchargeTotal = 0m;

        var guestDiscountAdjustments = order.Adjustments
            .Where(x =>
                x.Type == OrderAdjustmentType.Discount &&
                x.GuestNumber.HasValue)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();

        foreach (var adjustment in guestDiscountAdjustments)
        {
            var guest = adjustment.GuestNumber!.Value;
            if (!guestNet.ContainsKey(guest))
            {
                SetCalculatedAmount(adjustment, 0m, updateAdjustmentAmounts);
                continue;
            }

            var amount = CalculateDiscount(
                guestNet[guest],
                adjustment.Mode,
                adjustment.Value);

            guestNet[guest] = Money(Math.Max(0m, guestNet[guest] - amount));
            guestDiscounts[guest] = Money(guestDiscounts[guest] + amount);
            discountTotal = Money(discountTotal + amount);
            SetCalculatedAmount(adjustment, amount, updateAdjustmentAmounts);
        }

        var orderDiscountAdjustments = order.Adjustments
            .Where(x =>
                x.Type == OrderAdjustmentType.Discount &&
                !x.GuestNumber.HasValue)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();

        foreach (var adjustment in orderDiscountAdjustments)
        {
            var baseAmount = Money(guestNet.Values.Sum());
            var amount = CalculateDiscount(
                baseAmount,
                adjustment.Mode,
                adjustment.Value);

            var allocation = Allocate(amount, guestNet);
            foreach (var guest in guestNet.Keys.ToArray())
            {
                var part = allocation.GetValueOrDefault(guest);
                guestNet[guest] = Money(Math.Max(0m, guestNet[guest] - part));
                guestDiscounts[guest] = Money(guestDiscounts[guest] + part);
            }

            discountTotal = Money(discountTotal + amount);
            SetCalculatedAmount(adjustment, amount, updateAdjustmentAmounts);
        }

        var guestTotals = guestNet.ToDictionary(x => x.Key, x => x.Value);
        var serviceBase = Money(guestNet.Values.Sum());

        var serviceAdjustments = order.Adjustments
            .Where(x => x.Type == OrderAdjustmentType.ServiceCharge)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();

        foreach (var adjustment in serviceAdjustments)
        {
            var amount = CalculateSurcharge(
                serviceBase,
                adjustment.Mode,
                adjustment.Value);

            var allocation = Allocate(amount, guestNet);
            foreach (var guest in guestTotals.Keys.ToArray())
            {
                guestTotals[guest] = Money(
                    guestTotals[guest] + allocation.GetValueOrDefault(guest));
            }

            surchargeTotal = Money(surchargeTotal + amount);
            SetCalculatedAmount(adjustment, amount, updateAdjustmentAmounts);
        }

        var subtotal = Money(guestSubtotals.Values.Sum());
        var total = Money(Math.Max(0m, subtotal - discountTotal + surchargeTotal));

        return new OrderPricingSnapshot(
            subtotal,
            Money(discountTotal),
            Money(surchargeTotal),
            total,
            guestSubtotals,
            guestDiscounts,
            guestTotals);
    }

    private static decimal CalculateDiscount(
        decimal baseAmount,
        OrderAdjustmentMode mode,
        decimal value)
    {
        if (baseAmount <= 0m || value <= 0m)
            return 0m;

        var amount = mode == OrderAdjustmentMode.Percent
            ? baseAmount * Math.Clamp(value, 0m, 100m) / 100m
            : value;

        return Money(Math.Min(baseAmount, Math.Max(0m, amount)));
    }

    private static decimal CalculateSurcharge(
        decimal baseAmount,
        OrderAdjustmentMode mode,
        decimal value)
    {
        if (baseAmount <= 0m || value <= 0m)
            return 0m;

        var amount = mode == OrderAdjustmentMode.Percent
            ? baseAmount * Math.Max(0m, value) / 100m
            : value;

        return Money(Math.Max(0m, amount));
    }

    private static Dictionary<int, decimal> Allocate(
        decimal target,
        IReadOnlyDictionary<int, decimal> bases)
    {
        var result = bases.Keys.ToDictionary(key => key, _ => 0m);
        target = Money(Math.Max(0m, target));

        var positive = bases
            .Where(pair => pair.Value > 0m)
            .OrderBy(pair => pair.Key)
            .ToArray();

        var baseTotal = positive.Sum(pair => pair.Value);
        if (target <= 0m || baseTotal <= 0m || positive.Length == 0)
            return result;

        var allocated = 0m;
        for (var index = 0; index < positive.Length; index++)
        {
            var pair = positive[index];
            var isLast = index == positive.Length - 1;
            var amount = isLast
                ? Money(Math.Max(0m, target - allocated))
                : Money(target * pair.Value / baseTotal);

            result[pair.Key] = amount;
            allocated = Money(allocated + amount);
        }

        return result;
    }

    private static void SetCalculatedAmount(
        OrderAdjustment adjustment,
        decimal amount,
        bool update)
    {
        if (update)
            adjustment.CalculatedAmount = Money(amount);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
