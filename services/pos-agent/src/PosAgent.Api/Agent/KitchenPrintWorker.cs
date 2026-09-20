using PosAgent.Api.Printing;

namespace PosAgent.Api.Agent;

public sealed class KitchenPrintWorker(
    RestaurantNodeClient nodeClient,
    KitchenPrintJobExecutor executor,
    IConfiguration configuration,
    ILogger<KitchenPrintWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredSeconds = configuration.GetValue<int?>("Agent:KitchenPrintIntervalSeconds") ?? 2;
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuredSeconds, 1, 30));
        var configurationWarningLogged = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var hadJobs = false;

            if (!nodeClient.TryGetIdentity(out var restaurantId, out var deviceId, out var configurationError))
            {
                if (!configurationWarningLogged)
                {
                    logger.LogWarning(
                        "Kitchen print worker is waiting for POS Agent configuration: {ConfigurationError}",
                        configurationError);
                    configurationWarningLogged = true;
                }
            }
            else
            {
                configurationWarningLogged = false;

                try
                {
                    var jobs = await nodeClient.GetPrintJobsAsync(
                        restaurantId,
                        deviceId,
                        limit: 10,
                        stoppingToken);

                    hadJobs = jobs.Count > 0;

                    foreach (var job in jobs)
                    {
                        try
                        {
                            await executor.ExecuteAsync(job, stoppingToken);
                            await nodeClient.CompletePrintJobAsync(
                                restaurantId,
                                deviceId,
                                job.Id,
                                success: true,
                                error: null,
                                stoppingToken);

                            logger.LogInformation(
                                "Kitchen print job {PrintJobId} printed on {PrinterName} ({PrinterAddress}).",
                                job.Id,
                                job.Printer.Name,
                                job.Printer.Address);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(
                                ex,
                                "Kitchen print job {PrintJobId} failed on {PrinterName}. Attempt {Attempt}.",
                                job.Id,
                                job.Printer.Name,
                                job.Attempts);

                            try
                            {
                                await nodeClient.CompletePrintJobAsync(
                                    restaurantId,
                                    deviceId,
                                    job.Id,
                                    success: false,
                                    error: ex.Message,
                                    stoppingToken);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception reportError)
                            {
                                logger.LogWarning(
                                    reportError,
                                    "Could not report failure for kitchen print job {PrintJobId}.",
                                    job.Id);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Kitchen print queue is temporarily unavailable. Pending jobs remain on Restaurant Node.");
                }
            }

            try
            {
                await Task.Delay(hadJobs ? TimeSpan.FromMilliseconds(250) : interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
