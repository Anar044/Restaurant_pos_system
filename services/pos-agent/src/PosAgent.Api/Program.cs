using PosAgent.Api.Agent;
using PosAgent.Api.Printing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WindowsPrinterDiscovery>();
builder.Services.AddSingleton<WindowsDriverPrinter>();
builder.Services.AddSingleton<NetworkRawPrinter>();
builder.Services.AddSingleton<LocalReceiptPrinter>();
builder.Services.AddSingleton<AgentCacheStore>();
builder.Services.AddHttpClient<RestaurantNodeClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHostedService<AgentSyncWorker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "pos-agent",
    machineName = Environment.MachineName,
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/v1/printers", (WindowsPrinterDiscovery discovery) =>
{
    try
    {
        return Results.Ok(new
        {
            machineName = Environment.MachineName,
            printers = discovery.GetInstalledPrinters()
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Could not enumerate Windows printers.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/v1/config", async (AgentCacheStore cache, CancellationToken ct) =>
    Results.Ok(await cache.ReadAsync(ct)));

app.MapGet("/api/v1/status", async (
    RestaurantNodeClient nodeClient,
    AgentCacheStore cache,
    CancellationToken ct) =>
{
    var configured = nodeClient.TryGetIdentity(out var restaurantId, out var deviceId, out var error);
    var localConfig = await cache.ReadAsync(ct);

    return Results.Ok(new
    {
        configured,
        configurationError = error,
        restaurantId = configured ? restaurantId : (Guid?)null,
        deviceId = configured ? deviceId : (Guid?)null,
        machineName = Environment.MachineName,
        localConfig
    });
});

app.MapPost("/api/v1/print/test-receipt", async (
    LocalReceiptPrinter printer,
    CancellationToken ct) =>
{
    try
    {
        var result = await printer.PrintTestAsync(ct);
        return Results.Ok(new
        {
            status = "printed",
            result.PrinterId,
            result.PrinterName,
            result.ConnectionType,
            result.Address,
            result.Port,
            result.PrintMode,
            result.PrintedAt
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Local receipt print failed.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();
