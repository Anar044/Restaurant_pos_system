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

        throw Unsupported(config);
    }

    public async Task<ReceiptPrintResult> PrintReceiptAsync(
        ReceiptPrintRequest receipt,
        CancellationToken ct)
    {
        ReceiptBuilder.Validate(receipt);
        var config = await GetConfiguredReceiptPrinterAsync(ct);
        var printerName = config.ReceiptPrinterName ?? config.ReceiptPrinterAddress!;
        var printMode = await PrintDocumentAsync(
            config,
            ReceiptBuilder.BuildWindowsText(receipt),
            ReceiptBuilder.BuildEscPos(receipt),
            $"Order #{receipt.OrderNumber} - {receipt.RestaurantName}",
            ct);

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

    public async Task<PaidReceiptBundleResult> PrintPaidBundleAsync(
        ReceiptPrintRequest receipt,
        CancellationToken ct)
    {
        ReceiptBuilder.ValidatePaid(receipt);

        var config = await GetConfiguredReceiptPrinterAsync(ct);
        var printerName = config.ReceiptPrinterName ?? config.ReceiptPrinterAddress!;

        var printMode = await PrintDocumentAsync(
            config,
            ReceiptBuilder.BuildWindowsText(receipt),
            ReceiptBuilder.BuildEscPos(receipt),
            $"Paid order #{receipt.OrderNumber} - {receipt.RestaurantName}",
            ct);
        var salePrintedAt = DateTimeOffset.UtcNow;

        var pickupWindows = PickupTicketBuilder.BuildWindowsText(receipt);
        var pickupEscPos = PickupTicketBuilder.BuildEscPos(receipt);

        await PrintDocumentAsync(
            config,
            pickupWindows,
            pickupEscPos,
            $"Pickup ticket #{receipt.OrderNumber} - {receipt.RestaurantName}",
            ct);
        var pickupPrintedAt = DateTimeOffset.UtcNow;

        return new PaidReceiptBundleResult(
            config.ReceiptPrinterId!.Value,
            printerName,
            config.ReceiptPrinterConnectionType!,
            config.ReceiptPrinterAddress!,
            config.ReceiptPrinterPort,
            printMode,
            receipt.OrderNumber,
            salePrintedAt,
            pickupPrintedAt);
    }

    private async Task<string> PrintDocumentAsync(
        AgentLocalConfig config,
        string windowsText,
        byte[] networkPayload,
        string documentName,
        CancellationToken ct)
    {
        if (IsWindowsQueue(config))
        {
            EnsureWindowsPrinterInstalled(config.ReceiptPrinterAddress!);
            windowsDriverPrinter.PrintText(
                config.ReceiptPrinterAddress!,
                windowsText,
                documentName);
            return "WindowsDriver";
        }

        if (IsNetwork(config))
        {
            await networkRawPrinter.SendAsync(
                config.ReceiptPrinterAddress!,
                config.ReceiptPrinterPort ?? 9100,
                networkPayload,
                ct);
            return "NetworkEscPosRaw";
        }

        throw Unsupported(config);
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

    private static InvalidOperationException Unsupported(AgentLocalConfig config) =>
        new($"Unsupported receipt printer connection type '{config.ReceiptPrinterConnectionType}'.");

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
