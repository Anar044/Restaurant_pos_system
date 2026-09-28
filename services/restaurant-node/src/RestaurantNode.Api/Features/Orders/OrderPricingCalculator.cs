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
    private sealed class ItemState
    {
        public required OrderItem Item { get; init; }
        public decimal Original { get; init; }
        public decimal Net { get; set; }
        public bool Touched { get; set; }
        public bool Locked { get; set; }
    }

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

        var states = order.Items
            .Where(x => x.Status != OrderItemStatus.Voided)
            .Select(x => new ItemState
            {
                Item = x,
                Original = Money(x.LineTotal),
                Net = Money(x.LineTotal)
            })
            .ToArray();

        var guestSubtotals = Enumerable.Range(1, guestCount)
            .ToDictionary(guest => guest, _ => 0m);
        var guestDiscounts = Enumerable.Range(1, guestCount)
            .ToDictionary(guest => guest, _ => 0m);

        foreach (var state in states)
        {
            if (state.Item.GuestNumber >= 1 &&
                state.Item.GuestNumber <= guestCount)
            {
                guestSubtotals[state.Item.GuestNumber] = Money(
                    guestSubtotals[state.Item.GuestNumber] + state.Original);
            }
        }

        decimal discountTotal = 0m;
        decimal surchargeTotal = 0m;

        var adjustments = order.Adjustments
            .OrderBy(x => x.PrioritySnapshot)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();

        foreach (var adjustment in adjustments)
        {
            var eligible = states
                .Where(state =>
                    state.Net > 0m &&
                    !state.Locked &&
                    PricingRuleEngine.AdjustmentMatchesItem(
                        adjustment,
                        order,
                        state.Item) &&
                    (adjustment.CanStackSnapshot || !state.Touched))
                .ToArray();

            var baseAmount = Money(eligible.Sum(x => x.Net));
            if (baseAmount <= 0m)
            {
                SetCalculatedAmount(
                    adjustment,
                    0m,
                    updateAdjustmentAmounts);
                continue;
            }

            var amount = adjustment.Type == OrderAdjustmentType.Discount
                ? CalculateDiscount(
                    baseAmount,
                    adjustment.Mode,
                    adjustment.Value)
                : CalculateSurcharge(
                    baseAmount,
                    adjustment.Mode,
                    adjustment.Value);

            if (amount <= 0m)
            {
                SetCalculatedAmount(
                    adjustment,
                    0m,
                    updateAdjustmentAmounts);
                continue;
            }

            var bases = eligible.ToDictionary(
                x => x.Item.Id,
                x => x.Net);
            var allocation = Allocate(amount, bases);

            foreach (var state in eligible)
            {
                var part = allocation.GetValueOrDefault(state.Item.Id);
                if (part <= 0m)
                    continue;

                if (adjustment.Type == OrderAdjustmentType.Discount)
                {
                    state.Net = Money(
                        Math.Max(0m, state.Net - part));

                    if (state.Item.GuestNumber >= 1 &&
                        state.Item.GuestNumber <= guestCount)
                    {
                        guestDiscounts[state.Item.GuestNumber] = Money(
                            guestDiscounts[state.Item.GuestNumber] + part);
                    }
                }
                else
                {
                    state.Net = Money(state.Net + part);
                }

                state.Touched = true;
                if (!adjustment.CanStackSnapshot)
                    state.Locked = true;
            }

            if (adjustment.Type == OrderAdjustmentType.Discount)
                discountTotal = Money(discountTotal + amount);
            else
                surchargeTotal = Money(surchargeTotal + amount);

            SetCalculatedAmount(
                adjustment,
                amount,
                updateAdjustmentAmounts);
        }

        var guestTotals = Enumerable.Range(1, guestCount)
            .ToDictionary(guest => guest, _ => 0m);

        foreach (var state in states)
        {
            if (state.Item.GuestNumber >= 1 &&
                state.Item.GuestNumber <= guestCount)
            {
                guestTotals[state.Item.GuestNumber] = Money(
                    guestTotals[state.Item.GuestNumber] + state.Net);
            }
        }

        var subtotal = Money(states.Sum(x => x.Original));
        var total = Money(states.Sum(x => x.Net));

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

        return Money(Math.Min(
            baseAmount,
            Math.Max(0m, amount)));
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

    private static Dictionary<Guid, decimal> Allocate(
        decimal target,
        IReadOnlyDictionary<Guid, decimal> bases)
    {
        var result = bases.Keys.ToDictionary(key => key, _ => 0m);
        target = Money(Math.Max(0m, target));

        var positive = bases
            .Where(pair => pair.Value > 0m)
            .OrderBy(pair => pair.Key)
            .ToArray();

        var baseTotal = positive.Sum(pair => pair.Value);
        if (target <= 0m ||
            baseTotal <= 0m ||
            positive.Length == 0)
        {
            return result;
        }

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
