using PosAgent.Api.Agent;

namespace PosAgent.Api.Printing;

public sealed record LocalPrintResult(
    Guid PrinterId,
    string PrinterName,
    string ConnectionType,
    string Address,
    int? Port,
    DateTimeOffset PrintedAt);

public sealed class LocalReceiptPrinter(
    AgentCacheStore cacheStore,
    WindowsPrinterDiscovery discovery,
    WindowsRawPrinter windowsRawPrinter,
    NetworkRawPrinter networkRawPrinter)
{
    public async Task<LocalPrintResult> PrintTestAsync(CancellationToken ct)
    {
        var config = await cacheStore.ReadAsync(ct);
        if (!config.ReceiptPrinterId.HasValue ||
            string.IsNullOrWhiteSpace(config.ReceiptPrinterConnectionType) ||
            string.IsNullOrWhiteSpace(config.ReceiptPrinterAddress))
        {
            throw new InvalidOperationException(
                "Receipt printer is not cached on this POS yet. Assign it in BackOffice and wait for POS Agent sync.");
        }

        var printerName = config.ReceiptPrinterName ?? config.ReceiptPrinterAddress;
        var payload = TestReceiptBuilder.Build(config.DeviceName, printerName);

        if (string.Equals(config.ReceiptPrinterConnectionType, "WindowsQueue", StringComparison.OrdinalIgnoreCase))
        {
            var installed = discovery.GetInstalledPrinters().Any(x =>
                string.Equals(x.QueueName, config.ReceiptPrinterAddress, StringComparison.OrdinalIgnoreCase));
            if (!installed)
            {
                throw new InvalidOperationException(
                    $"Windows printer '{config.ReceiptPrinterAddress}' is no longer installed on this POS.");
            }

            windowsRawPrinter.Send(
                config.ReceiptPrinterAddress,
                payload,
                $"Restaurant POS test - {config.DeviceName ?? Environment.MachineName}");
        }
        else if (string.Equals(config.ReceiptPrinterConnectionType, "Network", StringComparison.OrdinalIgnoreCase))
        {
            await networkRawPrinter.SendAsync(
                config.ReceiptPrinterAddress,
                config.ReceiptPrinterPort ?? 9100,
                payload,
                ct);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported receipt printer connection type '{config.ReceiptPrinterConnectionType}'.");
        }

        return new LocalPrintResult(
            config.ReceiptPrinterId.Value,
            printerName,
            config.ReceiptPrinterConnectionType,
            config.ReceiptPrinterAddress,
            config.ReceiptPrinterPort,
            DateTimeOffset.UtcNow);
    }
}
