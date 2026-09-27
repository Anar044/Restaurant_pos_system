using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Features.Orders;

namespace RestaurantNode.Api.Features.Payments;

internal sealed record GuestPaymentBalance(
    int GuestNumber,
    decimal Total,
    decimal Paid,
    decimal Remaining);

internal static class GuestPaymentMath
{
    public const string None = "NONE";
    public const string WholeOrder = "WHOLE_ORDER";
    public const string ByGuest = "BY_GUEST";
    public const string MixedLegacy = "MIXED";

    public static string GetPaymentMode(Order order)
    {
        var completed = order.Payments.Where(IsCompleted).ToArray();
        if (completed.Length == 0)
            return None;

        var hasGuest = completed.Any(x => x.GuestNumber.HasValue);
        var hasWhole = completed.Any(x => !x.GuestNumber.HasValue);

        if (hasGuest && hasWhole)
            return MixedLegacy;
        return hasGuest ? ByGuest : WholeOrder;
    }

    public static decimal GetCompletedTotal(Order order) =>
        Money(order.Payments.Where(IsCompleted).Sum(x => x.Amount));

    public static IReadOnlyList<GuestPaymentBalance> GetGuestBalances(Order order)
    {
        var pricing = OrderPricingCalculator.Calculate(order);
        var guestCount = Math.Max(1, order.GuestCount);

        var paid = order.Payments
            .Where(x => IsCompleted(x) && x.GuestNumber.HasValue)
            .GroupBy(x => x.GuestNumber!.Value)
            .ToDictionary(group => group.Key, group => Money(group.Sum(x => x.Amount)));

        return Enumerable.Range(1, guestCount)
            .Select(guest =>
            {
                var total = Money(pricing.GuestTotals.GetValueOrDefault(guest));
                var paidAmount = Money(paid.GetValueOrDefault(guest));
                return new GuestPaymentBalance(
                    guest,
                    total,
                    paidAmount,
                    Money(Math.Max(0m, total - paidAmount)));
            })
            .ToArray();
    }

    private static bool IsCompleted(Payment payment) =>
        payment.Status is PaymentStatus.Completed or PaymentStatus.Refunded;

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
