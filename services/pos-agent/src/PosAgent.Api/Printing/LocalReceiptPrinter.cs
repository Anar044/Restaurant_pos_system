using PosAgent.Api.Agent;

namespace PosAgent.Api.Printing;

public sealed record LocalPrintResult(
    Guid PrinterId,
    string PrinterName,
    string ConnectionType,
    string Address,
    int? Port,
    string PrintMode,
    DateTimeOffset PrintedAt);

public sealed class LocalReceiptPrinter(
    AgentCacheStore cacheStore,
    WindowsPrinterDiscovery discovery,
    WindowsDriverPrinter windowsDriverPrinter,
    NetworkRawPrinter networkRawPrinter)
{
    public async Task<LocalPrintResult> PrintTestAsync(CancellationToken ct)
    {
        var config = await GetConfiguredReceiptPrinterAsync(ct);
        var printerName = config.ReceiptPrinterName ?? config.ReceiptPrinterAddress!;

        if (IsWindowsQueue(config))
        {
            EnsureWindowsPrinterInstalled(config.ReceiptPrinterAddress!);
            var text = TestReceiptBuilder.BuildWindowsText(config.DeviceName, printerName);
            windowsDriverPrinter.PrintText(
                config.ReceiptPrinterAddress!,
                text,
                $"Restaurant POS test - {config.DeviceName ?? Environment.MachineName}");

            return Result(config, printerName, "WindowsDriver");
        }

        if (IsNetwork(config))
        {
            var payload = TestReceiptBuilder.BuildEscPos(config.DeviceName, printerName);
            await networkRawPrinter.SendAsync(
                config.ReceiptPrinterAddress!,
                config.ReceiptPrinterPort ?? 9100,
                payload,
                ct);

            return Result(config, printerName, "NetworkEscPosRaw");
        }

        throw new InvalidOperationException(
            $"Unsupported receipt printer connection type '{config.ReceiptPrinterConnectionType}'.");
    }

    public async Task<ReceiptPrintResult> PrintReceiptAsync(
        ReceiptPrintRequest receipt,
        CancellationToken ct)
    {
        ReceiptBuilder.Validate(receipt);
        var config = await GetConfiguredReceiptPrinterAsync(ct);
        var printerName = config.ReceiptPrinterName ?? config.ReceiptPrinterAddress!;
        string printMode;

        if (IsWindowsQueue(config))
        {
            EnsureWindowsPrinterInstalled(config.ReceiptPrinterAddress!);
            var text = ReceiptBuilder.BuildWindowsText(receipt);
            windowsDriverPrinter.PrintText(
                config.ReceiptPrinterAddress!,
                text,
                $"Order #{receipt.OrderNumber} - {receipt.RestaurantName}");
            printMode = "WindowsDriver";
        }
        else if (IsNetwork(config))
        {
            var payload = ReceiptBuilder.BuildEscPos(receipt);
            await networkRawPrinter.SendAsync(
                config.ReceiptPrinterAddress!,
                config.ReceiptPrinterPort ?? 9100,
                payload,
                ct);
            printMode = "NetworkEscPosRaw";
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported receipt printer connection type '{config.ReceiptPrinterConnectionType}'.");
        }

        return new ReceiptPrintResult(
            config.ReceiptPrinterId!.Value,
            printerName,
            config.ReceiptPrinterConnectionType!,
            config.ReceiptPrinterAddress!,
            config.ReceiptPrinterPort,
            printMode,
            receipt.OrderNumber,
            DateTimeOffset.UtcNow);
    }

    private async Task<AgentLocalConfig> GetConfiguredReceiptPrinterAsync(CancellationToken ct)
    {
        var config = await cacheStore.ReadAsync(ct);
        if (!config.ReceiptPrinterId.HasValue ||
            string.IsNullOrWhiteSpace(config.ReceiptPrinterConnectionType) ||
            string.IsNullOrWhiteSpace(config.ReceiptPrinterAddress))
        {
            throw new InvalidOperationException(
                "Receipt printer is not cached on this POS yet. Assign it in BackOffice and wait for POS Agent sync.");
        }

        return config;
    }

    private void EnsureWindowsPrinterInstalled(string queueName)
    {
        var installed = discovery.GetInstalledPrinters().Any(x =>
            string.Equals(x.QueueName, queueName, StringComparison.OrdinalIgnoreCase));
        if (!installed)
        {
            throw new InvalidOperationException(
                $"Windows printer '{queueName}' is no longer installed on this POS.");
        }
    }

    private static bool IsWindowsQueue(AgentLocalConfig config) =>
        string.Equals(config.ReceiptPrinterConnectionType, "WindowsQueue", StringComparison.OrdinalIgnoreCase);

    private static bool IsNetwork(AgentLocalConfig config) =>
        string.Equals(config.ReceiptPrinterConnectionType, "Network", StringComparison.OrdinalIgnoreCase);

    private static LocalPrintResult Result(AgentLocalConfig config, string printerName, string printMode) =>
        new(
            config.ReceiptPrinterId!.Value,
            printerName,
            config.ReceiptPrinterConnectionType!,
            config.ReceiptPrinterAddress!,
            config.ReceiptPrinterPort,
            printMode,
            DateTimeOffset.UtcNow);
}
