using PosAgent.Api.Printing;

namespace PosAgent.Api.Agent;

public sealed class AgentSyncWorker(
    WindowsPrinterDiscovery printerDiscovery,
    RestaurantNodeClient nodeClient,
    AgentCacheStore cacheStore,
    IConfiguration configuration,
    ILogger<AgentSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredSeconds = configuration.GetValue<int?>("Agent:SyncIntervalSeconds") ?? 15;
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuredSeconds, 5, 300));
        var configurationWarningLogged = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!nodeClient.TryGetIdentity(out var restaurantId, out var deviceId, out var configurationError))
            {
                if (!configurationWarningLogged)
                {
                    logger.LogWarning("POS Agent is waiting for configuration: {ConfigurationError}", configurationError);
                    configurationWarningLogged = true;
                }
            }
            else
            {
                configurationWarningLogged = false;
                try
                {
                    var printers = printerDiscovery.GetInstalledPrinters();
                    var response = await nodeClient.SyncAsync(restaurantId, deviceId, printers, stoppingToken);
                    var receiptPrinter = response.ReceiptPrinter;

                    await cacheStore.WriteAsync(new AgentLocalConfig(
                        response.DeviceId,
                        response.DeviceName,
                        receiptPrinter?.Id,
                        receiptPrinter?.Name,
                        receiptPrinter?.ConnectionType,
                        receiptPrinter?.Address,
                        receiptPrinter?.Port,
                        response.SyncedAt), stoppingToken);

                    logger.LogInformation(
                        "POS Agent synced {PrinterCount} Windows printers for {DeviceName} ({DeviceId}). Receipt printer: {ReceiptPrinter}.",
                        printers.Count,
                        response.DeviceName,
                        response.DeviceId,
                        receiptPrinter is null
                            ? "not assigned"
                            : $"{receiptPrinter.Name} ({receiptPrinter.ConnectionType}: {receiptPrinter.Address})");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Restaurant Node is unavailable. Local POS printer cache remains available.");
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
