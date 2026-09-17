using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Agents;

public static class PosAgentEndpoints
{
    public static IEndpointRouteBuilder MapPosAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agents/pos");

        group.MapPost("/{deviceId:guid}/heartbeat", async (
            Guid deviceId,
            PosAgentHeartbeatRequest request,
            HttpRequest httpRequest,
            IConfiguration configuration,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            var configuredKey = configuration["Agent:SharedKey"];
            var providedKey = httpRequest.Headers["X-Agent-Key"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(configuredKey))
                return Results.Problem("POS Agent shared key is not configured on Restaurant Node.", statusCode: StatusCodes.Status503ServiceUnavailable);

            if (string.IsNullOrWhiteSpace(providedKey) || !SecureEquals(configuredKey, providedKey))
                return Results.Unauthorized();

            if (request.RestaurantId == Guid.Empty)
                return Results.BadRequest(new { message = "RestaurantId is required." });

            var device = await db.Devices.FirstOrDefaultAsync(
                x => x.Id == deviceId &&
                     x.RestaurantId == request.RestaurantId &&
                     x.Type == DeviceType.Pos &&
                     x.IsActive,
                ct);

            if (device is null)
                return Results.NotFound(new { message = "Active POS device was not found for this restaurant." });

            var now = DateTimeOffset.UtcNow;
            device.LastSeenAt = now;

            var normalizedQueues = (request.Printers ?? [])
                .Select(x => new
                {
                    QueueName = NormalizeQueueName(x.QueueName),
                    x.IsDefault
                })
                .Where(x => x.QueueName is not null)
                .GroupBy(x => x.QueueName!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var existingPrinters = await db.Printers
                .Where(x => x.RestaurantId == request.RestaurantId &&
                            x.HostDeviceId == deviceId &&
                            x.ConnectionType == PrinterConnectionType.WindowsQueue)
                .ToListAsync(ct);

            var byQueue = existingPrinters.ToDictionary(x => x.Address, StringComparer.OrdinalIgnoreCase);

            foreach (var discovered in normalizedQueues)
            {
                var queueName = discovered.QueueName!;
                if (byQueue.TryGetValue(queueName, out var existing))
                {
                    existing.Name = queueName;
                    existing.Address = queueName;
                    existing.LastSeenAt = now;
                    continue;
                }

                var printer = new Printer
                {
                    RestaurantId = request.RestaurantId,
                    HostDeviceId = deviceId,
                    Name = queueName,
                    ConnectionType = PrinterConnectionType.WindowsQueue,
                    Address = queueName,
                    Port = null,
                    IsActive = true,
                    LastSeenAt = now
                };
                db.Printers.Add(printer);
                existingPrinters.Add(printer);
                byQueue[queueName] = printer;
            }

            await db.SaveChangesAsync(ct);

            var selectedReceiptPrinter = device.ReceiptPrinterId.HasValue
                ? existingPrinters.FirstOrDefault(x => x.Id == device.ReceiptPrinterId.Value && x.IsActive)
                : null;

            return Results.Ok(new
            {
                deviceId = device.Id,
                deviceName = device.Name,
                restaurantId = request.RestaurantId,
                machineName = request.MachineName,
                syncedAt = now,
                syncIntervalSeconds = 15,
                receiptPrinter = selectedReceiptPrinter is null
                    ? null
                    : new
                    {
                        id = selectedReceiptPrinter.Id,
                        queueName = selectedReceiptPrinter.Address
                    }
            });
        });

        return app;
    }

    private static string? NormalizeQueueName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 250)
            return null;
        return normalized;
    }

    private static bool SecureEquals(string expected, string actual)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}

public sealed record PosAgentHeartbeatRequest(
    Guid RestaurantId,
    string? MachineName,
    IReadOnlyList<PosAgentPrinterRequest>? Printers);

public sealed record PosAgentPrinterRequest(string QueueName, bool IsDefault);