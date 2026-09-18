using System.Globalization;
using System.Text;

namespace PosAgent.Api.Printing;

public static class ReceiptBuilder
{
    private const int ReceiptWidth = 42;
    private const int NameWidth = 18;
    private const int QuantityWidth = 6;
    private const int PriceWidth = 8;
    private const int TotalWidth = 8;

    public static string BuildWindowsText(ReceiptPrintRequest receipt)
    {
        Validate(receipt);

        var sb = new StringBuilder();
        var paid = IsPaid(receipt);
        var when = receipt.CompletedAt?.ToLocalTime() ?? DateTimeOffset.Now;
        var currency = Normalize(receipt.CurrencyCode, "AZN");

        sb.AppendLine(Center(Normalize(receipt.RestaurantName, "Restaurant"), ReceiptWidth));
        sb.AppendLine(Center(paid ? "SATIŞ ÇEKİ / SALES RECEIPT" : "PREDÇEK / PRECHECK", ReceiptWidth));
        if (paid)
            sb.AppendLine(Center("QEYRİ-FİSKAL / NON-FISCAL", ReceiptWidth));
        sb.AppendLine(new string('-', ReceiptWidth));
        sb.AppendLine($"Sifariş / Order: #{receipt.OrderNumber}");
        if (!string.IsNullOrWhiteSpace(receipt.HallName))
            sb.AppendLine($"Zal / Hall: {receipt.HallName!.Trim()}");
        if (!string.IsNullOrWhiteSpace(receipt.TableName))
            sb.AppendLine($"Masa / Table: {receipt.TableName!.Trim()}");
        sb.AppendLine($"Kassir / Cashier: {Normalize(receipt.CashierName, "-")}");
        sb.AppendLine($"Qonaq / Guests: {receipt.GuestCount}");
        sb.AppendLine($"Tarix / Time: {when:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', ReceiptWidth));

        AppendWindowsItemsTable(sb, receipt.Items);

        sb.AppendLine(new string('-', ReceiptWidth));
        sb.AppendLine($"YEKUN / TOTAL: {Money(receipt.Total)} {currency}");

        if (paid)
        {
            sb.AppendLine($"Ödəniş / Payment: {receipt.PaymentMethod!.Trim()}");
            sb.AppendLine($"Ödənilib / Paid: {Money(receipt.PaidAmount ?? receipt.Total)} {currency}");
        }
        else
        {
            sb.AppendLine("ÖDƏNİLMƏYİB / NOT PAID");
        }

        sb.AppendLine(new string('-', ReceiptWidth));
        if (paid)
            sb.AppendLine("Fiskal qəbz NKA inteqrasiyasından gələcək.");
        sb.AppendLine("Təşəkkür edirik / Thank you");
        return sb.ToString();
    }

    public static byte[] BuildEscPos(ReceiptPrintRequest receipt)
    {
        Validate(receipt);

        var bytes = new List<byte>();
        var paid = IsPaid(receipt);
        var when = receipt.CompletedAt?.ToLocalTime() ?? DateTimeOffset.Now;
        var currency = ToAscii(Normalize(receipt.CurrencyCode, "AZN"));

        bytes.AddRange([0x1B, 0x40]);
        bytes.AddRange([0x1B, 0x61, 0x01]);
        bytes.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(bytes, ToAscii(Normalize(receipt.RestaurantName, "Restaurant")) + "\n");
        AppendAscii(bytes, paid ? "SALES RECEIPT\n" : "PRECHECK\n");
        if (paid)
            AppendAscii(bytes, "NON-FISCAL\n");
        bytes.AddRange([0x1B, 0x45, 0x00]);
        AppendAscii(bytes, "------------------------------------------\n");

        bytes.AddRange([0x1B, 0x61, 0x00]);
        AppendAscii(bytes, $"Order: #{receipt.OrderNumber}\n");
        if (!string.IsNullOrWhiteSpace(receipt.HallName))
            AppendAscii(bytes, $"Hall: {ToAscii(receipt.HallName!.Trim())}\n");
        if (!string.IsNullOrWhiteSpace(receipt.TableName))
            AppendAscii(bytes, $"Table: {ToAscii(receipt.TableName!.Trim())}\n");
        AppendAscii(bytes, $"Cashier: {ToAscii(Normalize(receipt.CashierName, "-"))}\n");
        AppendAscii(bytes, $"Guests: {receipt.GuestCount}\n");
        AppendAscii(bytes, $"Time: {when:yyyy-MM-dd HH:mm:ss}\n");
        AppendAscii(bytes, "------------------------------------------\n");

        AppendEscPosItemsTable(bytes, receipt.Items);

        AppendAscii(bytes, "------------------------------------------\n");
        bytes.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(bytes, $"TOTAL: {Money(receipt.Total)} {currency}\n");
        bytes.AddRange([0x1B, 0x45, 0x00]);

        if (paid)
        {
            AppendAscii(bytes, $"Payment: {ToAscii(receipt.PaymentMethod!.Trim())}\n");
            AppendAscii(bytes, $"Paid: {Money(receipt.PaidAmount ?? receipt.Total)} {currency}\n");
            AppendAscii(bytes, "Fiscal receipt will come from NKA.\n");
        }
        else
        {
            AppendAscii(bytes, "NOT PAID\n");
        }

        AppendAscii(bytes, "\nThank you\n\n\n");
        return bytes.ToArray();
    }

    public static void Validate(ReceiptPrintRequest receipt)
    {
        if (receipt.OrderNumber <= 0)
            throw new ArgumentException("Order number must be greater than zero.");
        if (receipt.GuestCount is < 1 or > 1000)
            throw new ArgumentException("Guest count is invalid.");
        if (receipt.Total < 0)
            throw new ArgumentException("Receipt total cannot be negative.");
        if (receipt.Items is null || receipt.Items.Count == 0)
            throw new ArgumentException("Receipt must contain at least one item.");
        if (receipt.Items.Count > 500)
            throw new ArgumentException("Receipt contains too many items.");

        foreach (var item in receipt.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Receipt item name is required.");
            if (item.Quantity <= 0)
                throw new ArgumentException("Receipt item quantity must be greater than zero.");
            if (item.UnitPrice < 0 || item.LineTotal < 0)
                throw new ArgumentException("Receipt item prices cannot be negative.");
        }
    }

    public static void ValidatePaid(ReceiptPrintRequest receipt)
    {
        Validate(receipt);

        if (string.IsNullOrWhiteSpace(receipt.PaymentMethod))
            throw new ArgumentException("Payment method is required for a paid receipt.");

        if (!receipt.PaidAmount.HasValue)
            throw new ArgumentException("Paid amount is required for a paid receipt.");

        if (receipt.PaidAmount.Value < receipt.Total)
            throw new ArgumentException("Paid amount cannot be less than the order total.");
    }

    public static bool IsPaid(ReceiptPrintRequest receipt) =>
        !string.IsNullOrWhiteSpace(receipt.PaymentMethod) &&
        receipt.PaidAmount.HasValue &&
        receipt.PaidAmount.Value >= receipt.Total;

    private static void AppendWindowsItemsTable(StringBuilder sb, IReadOnlyList<ReceiptLineRequest> items)
    {
        sb.AppendLine(
            Fit("Malın adı", NameWidth) +
            FitRight("Miqdar", QuantityWidth) +
            FitRight("Qiymət", PriceWidth) +
            FitRight("Toplam", TotalWidth));

        foreach (var item in items)
        {
            var name = Normalize(item.Name, "Item");
            var chunks = Wrap(name, NameWidth).ToList();
            var quantity = FormatQuantity(item.Quantity);
            var price = Money(item.UnitPrice);
            var total = Money(item.LineTotal);

            sb.AppendLine(
                Fit(chunks[0], NameWidth) +
                FitRight(quantity, QuantityWidth) +
                FitRight(price, PriceWidth) +
                FitRight(total, TotalWidth));

            for (var i = 1; i < chunks.Count; i++)
                sb.AppendLine(Fit(chunks[i], NameWidth));
        }
    }

    private static void AppendEscPosItemsTable(List<byte> bytes, IReadOnlyList<ReceiptLineRequest> items)
    {
        AppendAscii(
            bytes,
            Fit("Item", NameWidth) +
            FitRight("Qty", QuantityWidth) +
            FitRight("Price", PriceWidth) +
            FitRight("Total", TotalWidth) +
            "\n");

        foreach (var item in items)
        {
            var chunks = Wrap(ToAscii(Normalize(item.Name, "Item")), NameWidth).ToList();
            AppendAscii(
                bytes,
                Fit(chunks[0], NameWidth) +
                FitRight(FormatQuantity(item.Quantity), QuantityWidth) +
                FitRight(Money(item.UnitPrice), PriceWidth) +
                FitRight(Money(item.LineTotal), TotalWidth) +
                "\n");

            for (var i = 1; i < chunks.Count; i++)
                AppendAscii(bytes, Fit(chunks[i], NameWidth) + "\n");
        }
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        while (text.Length > width)
        {
            var split = text.LastIndexOf(' ', width - 1, width);
            if (split <= 0)
                split = width;

            yield return text[..split].TrimEnd();
            text = text[split..].TrimStart();
        }

        yield return text;
    }

    private static string Fit(string value, int width) =>
        value.Length <= width ? value.PadRight(width) : value[..width];

    private static string FitRight(string value, int width) =>
        value.Length <= width ? value.PadLeft(width) : value[^width..];

    private static string Center(string value, int width)
    {
        if (value.Length >= width)
            return value[..width];

        var left = (width - value.Length) / 2;
        return new string(' ', left) + value;
    }

    private static string Normalize(string? value, string fallback)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string FormatQuantity(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static void AppendAscii(List<byte> target, string text) =>
        target.AddRange(Encoding.ASCII.GetBytes(text));

    private static string ToAscii(string text) =>
        new(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray());
}
