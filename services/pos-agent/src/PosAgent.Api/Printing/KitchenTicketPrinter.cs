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
        sb.AppendLine(
            payload.IsVoid
                ? "ОТМЕНА / VOID"
                : payload.IsTransfer
                    ? "ПЕРЕНОС / TRANSFER"
                    : payload.IsGuestTransfer
                        ? "ПЕРЕНОС ГОСТЯ / GUEST MOVE"
                        : "КУХНЯ / KITCHEN");
        sb.AppendLine(payload.StationName.Trim());
        sb.AppendLine(new string('=', 42));
        sb.AppendLine($"ЗАКАЗ / ORDER #{payload.OrderNumber}");
        if (payload.IsVoid && !string.IsNullOrWhiteSpace(payload.VoidReason))
            sb.AppendLine($"Причина / Reason: {payload.VoidReason!.Trim()}");
        if (payload.IsTransfer)
        {
            if (payload.FromOrderNumber.HasValue)
                sb.AppendLine($"Из заказа / From order: #{payload.FromOrderNumber.Value}");
            if (payload.ToOrderNumber.HasValue)
                sb.AppendLine($"В заказ / To order: #{payload.ToOrderNumber.Value}");
            if (!string.IsNullOrWhiteSpace(payload.FromHallName) ||
                !string.IsNullOrWhiteSpace(payload.FromTableName))
            {
                sb.AppendLine(
                    $"Откуда / From: {JoinPlace(payload.FromHallName, payload.FromTableName)}");
            }
            if (!string.IsNullOrWhiteSpace(payload.ToHallName) ||
                !string.IsNullOrWhiteSpace(payload.ToTableName))
            {
                sb.AppendLine(
                    $"Куда / To: {JoinPlace(payload.ToHallName, payload.ToTableName)}");
            }
        }
        if (payload.IsGuestTransfer &&
            payload.FromGuestNumber.HasValue &&
            payload.ToGuestNumber.HasValue)
        {
            sb.AppendLine(
                $"Гость / Guest: {payload.FromGuestNumber.Value} -> {payload.ToGuestNumber.Value}");
        }
        if (!string.IsNullOrWhiteSpace(payload.HallName))
            sb.AppendLine($"Зал / Hall: {payload.HallName!.Trim()}");
        if (!string.IsNullOrWhiteSpace(payload.TableName))
            sb.AppendLine($"Стол / Table: {payload.TableName!.Trim()}");
        sb.AppendLine($"Время / Time: {payload.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 42));

        int? currentGuestNumber = null;
        foreach (var item in payload.Items)
        {
            if (currentGuestNumber != item.GuestNumber)
            {
                if (currentGuestNumber.HasValue)
                    sb.AppendLine(new string('-', 20));
                sb.AppendLine($"ГОСТЬ / GUEST {item.GuestNumber}");
                currentGuestNumber = item.GuestNumber;
            }

            sb.AppendLine($"{FormatQuantity(item.Quantity)} x {item.Name.Trim()}");
            foreach (var modifier in item.Modifiers ?? [])
            {
                var quantity = modifier.Quantity == 1m
                    ? string.Empty
                    : $" x{FormatQuantity(modifier.Quantity)}";
                sb.AppendLine($"  + {modifier.Name.Trim()}{quantity}");
            }
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
        AppendAscii(
            bytes,
            payload.IsVoid
                ? "VOID\n"
                : payload.IsTransfer
                    ? "TRANSFER\n"
                    : payload.IsGuestTransfer
                        ? "GUEST MOVE\n"
                        : "KITCHEN\n");
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
        if (payload.IsTransfer)
        {
            if (payload.FromOrderNumber.HasValue)
                AppendAscii(bytes, $"From order: #{payload.FromOrderNumber.Value}\n");
            if (payload.ToOrderNumber.HasValue)
                AppendAscii(bytes, $"To order: #{payload.ToOrderNumber.Value}\n");
            if (!string.IsNullOrWhiteSpace(payload.FromHallName) ||
                !string.IsNullOrWhiteSpace(payload.FromTableName))
            {
                AppendAscii(
                    bytes,
                    $"From: {ToAscii(JoinPlace(payload.FromHallName, payload.FromTableName))}\n");
            }
            if (!string.IsNullOrWhiteSpace(payload.ToHallName) ||
                !string.IsNullOrWhiteSpace(payload.ToTableName))
            {
                AppendAscii(
                    bytes,
                    $"To: {ToAscii(JoinPlace(payload.ToHallName, payload.ToTableName))}\n");
            }
        }
        if (payload.IsGuestTransfer &&
            payload.FromGuestNumber.HasValue &&
            payload.ToGuestNumber.HasValue)
        {
            AppendAscii(
                bytes,
                $"Guest: {payload.FromGuestNumber.Value} -> {payload.ToGuestNumber.Value}\n");
        }
        AppendAscii(bytes, "--------------------------------\n");

        int? currentGuestNumber = null;
        foreach (var item in payload.Items)
        {
            if (currentGuestNumber != item.GuestNumber)
            {
                if (currentGuestNumber.HasValue)
                    AppendAscii(bytes, "----------------\n");
                bytes.AddRange([0x1B, 0x45, 0x01]);
                AppendAscii(bytes, $"GUEST {item.GuestNumber}\n");
                bytes.AddRange([0x1B, 0x45, 0x00]);
                currentGuestNumber = item.GuestNumber;
            }

            bytes.AddRange([0x1B, 0x45, 0x01]);
            AppendAscii(bytes, $"{FormatQuantity(item.Quantity)} x {ToAscii(item.Name.Trim())}\n");
            bytes.AddRange([0x1B, 0x45, 0x00]);
            foreach (var modifier in item.Modifiers ?? [])
            {
                var quantity = modifier.Quantity == 1m
                    ? string.Empty
                    : $" x{FormatQuantity(modifier.Quantity)}";
                AppendAscii(bytes, $" + {ToAscii(modifier.Name.Trim())}{quantity}\n");
            }
            if (!string.IsNullOrWhiteSpace(item.Comment))
                AppendAscii(bytes, $" ! {ToAscii(item.Comment!.Trim())}\n");
        }

        AppendAscii(bytes, "--------------------------------\n");
        if (payload.IsVoid)
            AppendAscii(bytes, "VOID / CANCEL\n");
        else if (payload.IsTransfer)
            AppendAscii(bytes, "TRANSFER\n");
        else if (payload.IsGuestTransfer)
            AppendAscii(bytes, "GUEST MOVE\n");
        AppendAscii(bytes, $"ORDER #{payload.OrderNumber}\n\n\n");
        return bytes.ToArray();
    }

    private static string JoinPlace(string? hall, string? table)
    {
        var hallText = hall?.Trim();
        var tableText = table?.Trim();

        if (!string.IsNullOrWhiteSpace(hallText) &&
            !string.IsNullOrWhiteSpace(tableText))
            return $"{hallText}, стол {tableText}";

        if (!string.IsNullOrWhiteSpace(tableText))
            return $"стол {tableText}";

        return hallText ?? "-";
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
            if (item.GuestNumber <= 0)
                throw new InvalidOperationException("Kitchen item guest number is invalid.");

            foreach (var modifier in item.Modifiers ?? [])
            {
                if (string.IsNullOrWhiteSpace(modifier.Name))
                    throw new InvalidOperationException("Kitchen modifier name is missing.");
                if (modifier.Quantity <= 0)
                    throw new InvalidOperationException("Kitchen modifier quantity is invalid.");
            }
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
    string? VoidReason = null,
    bool IsTransfer = false,
    long? FromOrderNumber = null,
    long? ToOrderNumber = null,
    string? FromTableName = null,
    string? FromHallName = null,
    string? ToTableName = null,
    string? ToHallName = null,
    bool IsGuestTransfer = false,
    int? FromGuestNumber = null,
    int? ToGuestNumber = null);

public sealed record KitchenTicketItemPayload(
    Guid LineId,
    Guid ProductId,
    string Name,
    decimal Quantity,
    string? Comment,
    IReadOnlyList<KitchenTicketModifierPayload>? Modifiers = null,
    int GuestNumber = 1);

public sealed record KitchenTicketModifierPayload(
    Guid ModifierId,
    string Name,
    decimal Quantity);
