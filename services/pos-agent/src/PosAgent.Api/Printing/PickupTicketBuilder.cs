using System.Globalization;
using System.Text;

namespace PosAgent.Api.Printing;

public static class PickupTicketBuilder
{
    private const int Width = 42;

    public static string BuildWindowsText(ReceiptPrintRequest receipt)
    {
        ReceiptBuilder.ValidatePaid(receipt);

        var when = receipt.CompletedAt?.ToLocalTime() ?? DateTimeOffset.Now;
        var currency = Normalize(receipt.CurrencyCode, "AZN");
        var sb = new StringBuilder();

        sb.AppendLine(Center(Normalize(receipt.RestaurantName, "Restaurant")));
        sb.AppendLine(Center("SİFARİŞ TALONU / ORDER TICKET"));
        sb.AppendLine(new string('=', Width));
        sb.AppendLine();
        sb.AppendLine(Center($"*** SİFARİŞ № {receipt.OrderNumber} ***"));
        sb.AppendLine(Center($"*** ORDER #{receipt.OrderNumber} ***"));
        sb.AppendLine();
        sb.AppendLine(new string('=', Width));
        sb.AppendLine(Center("ÖDƏNİLİB / PAID"));
        sb.AppendLine($"Məbləğ / Amount: {Money(receipt.PaidAmount ?? receipt.Total)} {currency}");
        sb.AppendLine($"Ödəniş / Payment: {receipt.PaymentMethod!.Trim()}");
        if (!string.IsNullOrWhiteSpace(receipt.HallName))
            sb.AppendLine($"Zal / Hall: {receipt.HallName!.Trim()}");
        if (!string.IsNullOrWhiteSpace(receipt.TableName))
            sb.AppendLine($"Masa / Table: {receipt.TableName!.Trim()}");
        sb.AppendLine($"Tarix / Time: {when:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', Width));
        sb.AppendLine("Sifariş / Order:");

        foreach (var item in receipt.Items)
            sb.AppendLine($"{Quantity(item.Quantity)} x {Normalize(item.Name, "Item")}");

        sb.AppendLine(new string('-', Width));
        sb.AppendLine("Bu talonu sifarişi götürəndə təqdim edin.");
        sb.AppendLine("Present this ticket when collecting the order.");
        return sb.ToString();
    }

    public static byte[] BuildEscPos(ReceiptPrintRequest receipt)
    {
        ReceiptBuilder.ValidatePaid(receipt);

        var bytes = new List<byte>();
        var when = receipt.CompletedAt?.ToLocalTime() ?? DateTimeOffset.Now;
        var currency = ToAscii(Normalize(receipt.CurrencyCode, "AZN"));

        bytes.AddRange([0x1B, 0x40]);
        bytes.AddRange([0x1B, 0x61, 0x01]);
        bytes.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(bytes, ToAscii(Normalize(receipt.RestaurantName, "Restaurant")) + "\n");
        AppendAscii(bytes, "ORDER TICKET\n");
        bytes.AddRange([0x1B, 0x45, 0x00]);
        AppendAscii(bytes, "==========================================\n\n");

        // Double width + double height for the pickup number.
        bytes.AddRange([0x1D, 0x21, 0x11]);
        AppendAscii(bytes, $"ORDER #{receipt.OrderNumber}\n");
        bytes.AddRange([0x1D, 0x21, 0x00]);

        AppendAscii(bytes, "\n==========================================\n");
        bytes.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(bytes, "PAID\n");
        bytes.AddRange([0x1B, 0x45, 0x00]);
        AppendAscii(bytes, $"Amount: {Money(receipt.PaidAmount ?? receipt.Total)} {currency}\n");
        AppendAscii(bytes, $"Payment: {ToAscii(receipt.PaymentMethod!.Trim())}\n");

        bytes.AddRange([0x1B, 0x61, 0x00]);
        if (!string.IsNullOrWhiteSpace(receipt.HallName))
            AppendAscii(bytes, $"Hall: {ToAscii(receipt.HallName!.Trim())}\n");
        if (!string.IsNullOrWhiteSpace(receipt.TableName))
            AppendAscii(bytes, $"Table: {ToAscii(receipt.TableName!.Trim())}\n");
        AppendAscii(bytes, $"Time: {when:yyyy-MM-dd HH:mm:ss}\n");
        AppendAscii(bytes, "------------------------------------------\n");
        AppendAscii(bytes, "Order:\n");

        foreach (var item in receipt.Items)
            AppendAscii(bytes, $"{Quantity(item.Quantity)} x {ToAscii(Normalize(item.Name, "Item"))}\n");

        AppendAscii(bytes, "------------------------------------------\n");
        AppendAscii(bytes, "Present this ticket when collecting order.\n\n\n");
        return bytes.ToArray();
    }

    private static string Center(string value)
    {
        if (value.Length >= Width)
            return value[..Width];

        var left = (Width - value.Length) / 2;
        return new string(' ', left) + value;
    }

    private static string Normalize(string? value, string fallback)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string Quantity(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static void AppendAscii(List<byte> target, string text) =>
        target.AddRange(Encoding.ASCII.GetBytes(text));

    private static string ToAscii(string text) =>
        new(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray());
}
