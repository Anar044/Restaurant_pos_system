using System.Globalization;
using System.Text;
using System.Text.Json;
using PosAgent.Api.Agent;

namespace PosAgent.Api.Printing;

public sealed class KitchenPrintJobExecutor(
    WindowsPrinterDiscovery discovery,
    WindowsDriverPrinter windowsDriverPrinter,
    NetworkRawPrinter networkRawPrinter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(AgentPrintJobResponse job, CancellationToken ct)
    {
        if (!string.Equals(job.Type, "KITCHEN_TICKET", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported print job type '{job.Type}'.");

        var payload = JsonSerializer.Deserialize<KitchenTicketPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Kitchen ticket payload is empty.");

        Validate(payload);

        if (string.Equals(job.Printer.ConnectionType, "WindowsQueue", StringComparison.OrdinalIgnoreCase))
        {
            EnsureWindowsPrinterInstalled(job.Printer.Address);
            windowsDriverPrinter.PrintText(
                job.Printer.Address,
                BuildWindowsText(payload),
                $"Kitchen #{payload.OrderNumber} - {payload.StationName}");
            return;
        }

        if (string.Equals(job.Printer.ConnectionType, "Network", StringComparison.OrdinalIgnoreCase))
        {
            await networkRawPrinter.SendAsync(
                job.Printer.Address,
                job.Printer.Port ?? 9100,
                BuildEscPos(payload),
                ct);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported kitchen printer connection type '{job.Printer.ConnectionType}'.");
    }

    private void EnsureWindowsPrinterInstalled(string queueName)
    {
        var installed = discovery.GetInstalledPrinters().Any(x =>
            string.Equals(x.QueueName, queueName, StringComparison.OrdinalIgnoreCase));

        if (!installed)
            throw new InvalidOperationException(
                $"Windows kitchen printer '{queueName}' is no longer installed on this POS.");
    }

    private static string BuildWindowsText(KitchenTicketPayload payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine(payload.IsVoid ? "ОТМЕНА / VOID" : "КУХНЯ / KITCHEN");
        sb.AppendLine(payload.StationName.Trim());
        sb.AppendLine(new string('=', 42));
        sb.AppendLine($"ЗАКАЗ / ORDER #{payload.OrderNumber}");
        if (payload.IsVoid && !string.IsNullOrWhiteSpace(payload.VoidReason))
            sb.AppendLine($"Причина / Reason: {payload.VoidReason!.Trim()}");
        if (!string.IsNullOrWhiteSpace(payload.HallName))
            sb.AppendLine($"Зал / Hall: {payload.HallName!.Trim()}");
        if (!string.IsNullOrWhiteSpace(payload.TableName))
            sb.AppendLine($"Стол / Table: {payload.TableName!.Trim()}");
        sb.AppendLine($"Время / Time: {payload.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 42));

        foreach (var item in payload.Items)
        {
            sb.AppendLine($"{FormatQuantity(item.Quantity)} x {item.Name.Trim()}");
            if (!string.IsNullOrWhiteSpace(item.Comment))
                sb.AppendLine($"  ! {item.Comment!.Trim()}");
        }

        sb.AppendLine(new string('=', 42));
        sb.AppendLine($"ORDER #{payload.OrderNumber}");
        return sb.ToString();
    }

    private static byte[] BuildEscPos(KitchenTicketPayload payload)
    {
        var bytes = new List<byte>();
        bytes.AddRange([0x1B, 0x40]);
        bytes.AddRange([0x1B, 0x61, 0x01]);
        bytes.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(bytes, payload.IsVoid ? "VOID\n" : "KITCHEN\n");
        AppendAscii(bytes, ToAscii(payload.StationName.Trim()) + "\n");
        bytes.AddRange([0x1D, 0x21, 0x11]);
        AppendAscii(bytes, $"ORDER #{payload.OrderNumber}\n");
        bytes.AddRange([0x1D, 0x21, 0x00]);
        bytes.AddRange([0x1B, 0x45, 0x00]);
        AppendAscii(bytes, "--------------------------------\n");

        bytes.AddRange([0x1B, 0x61, 0x00]);
        if (!string.IsNullOrWhiteSpace(payload.HallName))
            AppendAscii(bytes, $"Hall: {ToAscii(payload.HallName!.Trim())}\n");
        if (!string.IsNullOrWhiteSpace(payload.TableName))
            AppendAscii(bytes, $"Table: {ToAscii(payload.TableName!.Trim())}\n");
        AppendAscii(bytes, $"Time: {payload.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n");
        if (payload.IsVoid && !string.IsNullOrWhiteSpace(payload.VoidReason))
            AppendAscii(bytes, $"Reason: {ToAscii(payload.VoidReason!.Trim())}\n");
        AppendAscii(bytes, "--------------------------------\n");

        foreach (var item in payload.Items)
        {
            bytes.AddRange([0x1B, 0x45, 0x01]);
            AppendAscii(bytes, $"{FormatQuantity(item.Quantity)} x {ToAscii(item.Name.Trim())}\n");
            bytes.AddRange([0x1B, 0x45, 0x00]);
            if (!string.IsNullOrWhiteSpace(item.Comment))
                AppendAscii(bytes, $" ! {ToAscii(item.Comment!.Trim())}\n");
        }

        AppendAscii(bytes, "--------------------------------\n");
        if (payload.IsVoid)
            AppendAscii(bytes, "VOID / CANCEL\n");
        AppendAscii(bytes, $"ORDER #{payload.OrderNumber}\n\n\n");
        return bytes.ToArray();
    }

    private static void Validate(KitchenTicketPayload payload)
    {
        if (payload.TicketId == Guid.Empty)
            throw new InvalidOperationException("Kitchen ticket ID is missing.");
        if (payload.OrderNumber <= 0)
            throw new InvalidOperationException("Kitchen order number is invalid.");
        if (string.IsNullOrWhiteSpace(payload.StationName))
            throw new InvalidOperationException("Kitchen station name is missing.");
        if (payload.Items is null || payload.Items.Count == 0)
            throw new InvalidOperationException("Kitchen ticket has no items.");

        foreach (var item in payload.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidOperationException("Kitchen item name is missing.");
            if (item.Quantity <= 0)
                throw new InvalidOperationException("Kitchen item quantity is invalid.");
        }
    }

    private static string FormatQuantity(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void AppendAscii(List<byte> target, string text) =>
        target.AddRange(Encoding.ASCII.GetBytes(text));

    private static string ToAscii(string text) =>
        new(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray());
}

public sealed record KitchenTicketPayload(
    Guid TicketId,
    Guid OrderId,
    long OrderNumber,
    Guid? TableId,
    string? TableName,
    string? HallName,
    Guid StationId,
    string StationName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<KitchenTicketItemPayload> Items,
    bool IsVoid = false,
    string? VoidReason = null);

public sealed record KitchenTicketItemPayload(
    Guid LineId,
    Guid ProductId,
    string Name,
    decimal Quantity,
    string? Comment);
