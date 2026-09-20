using System.Globalization;
using System.Text;

namespace PosAgent.Api.Printing;

public static class ReceiptBuilder
{
    public static string BuildWindowsText(ReceiptPrintRequest receipt)
    {
        Validate(receipt);

        var sb = new StringBuilder();
        var paid = !string.IsNullOrWhiteSpace(receipt.PaymentMethod);
        var when = receipt.CompletedAt?.ToLocalTime() ?? DateTimeOffset.Now;
        var currency = Normalize(receipt.CurrencyCode, "AZN");

        sb.AppendLine(Normalize(receipt.RestaurantName, "Restaurant"));
        sb.AppendLine(paid ? "ЧЕК / RECEIPT" : "ПРЕДЧЕК / PRECHECK");
        if (receipt.IsCopy)
            sb.AppendLine("КОПИЯ / COPY");
        sb.AppendLine(new string('-', 42));
        sb.AppendLine($"Заказ / Order: #{receipt.OrderNumber}");
        if (!string.IsNullOrWhiteSpace(receipt.HallName))
            sb.AppendLine($"Зал / Hall: {receipt.HallName!.Trim()}");
        if (!string.IsNullOrWhiteSpace(receipt.TableName))
            sb.AppendLine($"Стол / Table: {receipt.TableName!.Trim()}");
        sb.AppendLine($"Кассир / Cashier: {Normalize(receipt.CashierName, "-")}");
        sb.AppendLine($"Гостей / Guests: {receipt.GuestCount}");
        sb.AppendLine($"Время / Time: {when:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 42));

        foreach (var item in receipt.Items)
        {
            sb.AppendLine(Normalize(item.Name, "Item"));
            sb.AppendLine($"  {FormatQuantity(item.Quantity)} x {Money(item.UnitPrice)} = {Money(item.LineTotal)} {currency}");
        }

        sb.AppendLine(new string('-', 42));
        sb.AppendLine($"ИТОГО / TOTAL: {Money(receipt.Total)} {currency}");

        if (paid)
        {
            sb.AppendLine($"Оплата / Payment: {receipt.PaymentMethod!.Trim()}");
            sb.AppendLine($"Оплачено / Paid: {Money(receipt.PaidAmount ?? receipt.Total)} {currency}");
            if (receipt.CashReceived.HasValue)
                sb.AppendLine($"Получено / Cash received: {Money(receipt.CashReceived.Value)} {currency}");
            if ((receipt.ChangeAmount ?? 0m) > 0)
                sb.AppendLine($"Сдача / Change: {Money(receipt.ChangeAmount!.Value)} {currency}");
        }
        else
        {
            sb.AppendLine("НЕ ОПЛАЧЕНО / NOT PAID");
        }

        sb.AppendLine(new string('-', 42));
        sb.AppendLine("Спасибо / Thank you");
        return sb.ToString();
    }

    public static byte[] BuildEscPos(ReceiptPrintRequest receipt)
    {
        Validate(receipt);

        var bytes = new List<byte>();
        var paid = !string.IsNullOrWhiteSpace(receipt.PaymentMethod);
        var when = receipt.CompletedAt?.ToLocalTime() ?? DateTimeOffset.Now;
        var currency = ToAscii(Normalize(receipt.CurrencyCode, "AZN"));

        bytes.AddRange([0x1B, 0x40]);
        bytes.AddRange([0x1B, 0x61, 0x01]);
        bytes.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(bytes, ToAscii(Normalize(receipt.RestaurantName, "Restaurant")) + "\n");
        AppendAscii(bytes, paid ? "RECEIPT\n" : "PRECHECK\n");
        if (receipt.IsCopy)
            AppendAscii(bytes, "COPY\n");
        bytes.AddRange([0x1B, 0x45, 0x00]);
        AppendAscii(bytes, "--------------------------------\n");

        bytes.AddRange([0x1B, 0x61, 0x00]);
        AppendAscii(bytes, $"Order: #{receipt.OrderNumber}\n");
        if (!string.IsNullOrWhiteSpace(receipt.HallName))
            AppendAscii(bytes, $"Hall: {ToAscii(receipt.HallName!.Trim())}\n");
        if (!string.IsNullOrWhiteSpace(receipt.TableName))
            AppendAscii(bytes, $"Table: {ToAscii(receipt.TableName!.Trim())}\n");
        AppendAscii(bytes, $"Cashier: {ToAscii(Normalize(receipt.CashierName, "-"))}\n");
        AppendAscii(bytes, $"Guests: {receipt.GuestCount}\n");
        AppendAscii(bytes, $"Time: {when:yyyy-MM-dd HH:mm:ss}\n");
        AppendAscii(bytes, "--------------------------------\n");

        foreach (var item in receipt.Items)
        {
            AppendAscii(bytes, ToAscii(Normalize(item.Name, "Item")) + "\n");
            AppendAscii(bytes, $" {FormatQuantity(item.Quantity)} x {Money(item.UnitPrice)} = {Money(item.LineTotal)} {currency}\n");
        }

        AppendAscii(bytes, "--------------------------------\n");
        bytes.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(bytes, $"TOTAL: {Money(receipt.Total)} {currency}\n");
        bytes.AddRange([0x1B, 0x45, 0x00]);

        if (paid)
        {
            AppendAscii(bytes, $"Payment: {ToAscii(receipt.PaymentMethod!.Trim())}\n");
            AppendAscii(bytes, $"Paid: {Money(receipt.PaidAmount ?? receipt.Total)} {currency}\n");
            if (receipt.CashReceived.HasValue)
                AppendAscii(bytes, $"Cash received: {Money(receipt.CashReceived.Value)} {currency}\n");
            if ((receipt.ChangeAmount ?? 0m) > 0)
                AppendAscii(bytes, $"Change: {Money(receipt.ChangeAmount!.Value)} {currency}\n");
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
