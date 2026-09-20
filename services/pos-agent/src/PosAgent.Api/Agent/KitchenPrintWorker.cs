using PosAgent.Api.Printing;

namespace PosAgent.Api.Agent;

public sealed class KitchenPrintWorker(
    RestaurantNodeClient nodeClient,
    KitchenPrintJobExecutor executor,
    PrintedJobStore printedJobStore,
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
                        var alreadyPrinted = await printedJobStore.ContainsAsync(job.Id, stoppingToken);

                        if (!alreadyPrinted)
                        {
                            try
                            {
                                await executor.ExecuteAsync(job, stoppingToken);

                                // Persist locally BEFORE acknowledging Restaurant Node.
                                // If the acknowledgement fails, a later lease retry must not
                                // send the same physical ticket to the printer again.
                                await printedJobStore.MarkPrintedAsync(job.Id, stoppingToken);

                                logger.LogInformation(
                                    "Kitchen print job {PrintJobId} physically printed on {PrinterName} ({PrinterAddress}).",
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
                                    "Kitchen print job {PrintJobId} failed before a successful physical print on {PrinterName}. Attempt {Attempt}.",
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
                                        "Could not report physical print failure for kitchen job {PrintJobId}.",
                                        job.Id);
                                }

                                continue;
                            }
                        }
                        else
                        {
                            logger.LogWarning(
                                "Kitchen print job {PrintJobId} was already physically printed on this POS. Skipping duplicate output and retrying acknowledgement only.",
                                job.Id);
                        }

                        var acknowledged = false;
                        Exception? lastAckError = null;

                        for (var ackAttempt = 1; ackAttempt <= 3 && !acknowledged; ackAttempt++)
                        {
                            try
                            {
                                await nodeClient.CompletePrintJobAsync(
                                    restaurantId,
                                    deviceId,
                                    job.Id,
                                    success: true,
                                    error: null,
                                    stoppingToken);

                                acknowledged = true;
                                logger.LogInformation(
                                    "Kitchen print job {PrintJobId} acknowledged as printed.",
                                    job.Id);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ackError)
                            {
                                lastAckError = ackError;
                                logger.LogWarning(
                                    ackError,
                                    "Kitchen print job {PrintJobId} acknowledgement attempt {AckAttempt}/3 failed.",
                                    job.Id,
                                    ackAttempt);

                                if (ackAttempt < 3)
                                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                            }
                        }

                        if (!acknowledged)
                        {
                            // IMPORTANT: physical printing already succeeded. Never report it
                            // as FAILED, because that can cause the same ticket to be printed again.
                            logger.LogError(
                                lastAckError,
                                "Kitchen print job {PrintJobId} was physically printed, but acknowledgement failed after 3 attempts. The job remains PRINTING and will not be auto-printed again.",
                                job.Id);
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
                await Task.Delay(hadJobs ? TimeSpan.FromSeconds(1) : interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
