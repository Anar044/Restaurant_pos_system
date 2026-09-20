using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Agents;

public static class PosAgentPrintJobEndpoints
{
    private const int MaxAttempts = 10;

    public static IEndpointRouteBuilder MapPosAgentPrintJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agents/pos");

        group.MapGet("/{deviceId:guid}/print-jobs", async (
            Guid deviceId,
            Guid restaurantId,
            int? limit,
            HttpRequest httpRequest,
            IConfiguration configuration,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            var authError = ValidateAgentKey(httpRequest, configuration);
            if (authError is not null) return authError;

            if (!await DeviceExistsAsync(db, restaurantId, deviceId, ct))
                return Results.NotFound(new { message = "Active POS device was not found for this restaurant." });

            var routes = await GetRoutesAsync(db, restaurantId, deviceId, ct);
            if (routes.Count == 0)
                return Results.Ok(new { jobs = Array.Empty<object>() });

            var printerKeys = routes.Keys.ToArray();
            var take = Math.Clamp(limit ?? 10, 1, 25);
            var staleBefore = DateTimeOffset.UtcNow.AddSeconds(-90);

            var candidates = await db.PrintJobs
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == "KITCHEN_TICKET" &&
                    printerKeys.Contains(x.PrinterKey) &&
                    x.Attempts < MaxAttempts &&
                    (x.Status == PrintJobStatus.Pending ||
                     (x.Status == PrintJobStatus.Printing &&
                      x.PrintedAt.HasValue &&
                      x.PrintedAt.Value < staleBefore)))
                .OrderBy(x => x.CreatedAt)
                .Take(Math.Min(take * 5, 100))
                .ToListAsync(ct);

            if (candidates.Count == 0)
                return Results.Ok(new { jobs = Array.Empty<object>() });

            var orderIds = candidates
                .Select(x => TryGetOrderId(x.PayloadJson))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();

            var printableOrderIds = await db.Orders
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    orderIds.Contains(x.Id) &&
                    x.Status != OrderStatus.Closed &&
                    x.Status != OrderStatus.Cancelled)
                .Select(x => x.Id)
                .ToHashSetAsync(ct);

            var jobs = new List<PrintJob>(take);
            var claimedAt = DateTimeOffset.UtcNow;

            foreach (var job in candidates)
            {
                var orderId = TryGetOrderId(job.PayloadJson);
                if (!orderId.HasValue || !printableOrderIds.Contains(orderId.Value))
                {
                    job.Status = PrintJobStatus.Failed;
                    job.Attempts = MaxAttempts;
                    job.PrintedAt = null;
                    job.LastError = "Kitchen print suppressed because the order is closed, cancelled, or unavailable.";
                    continue;
                }

                if (jobs.Count >= take)
                    continue;

                job.Status = PrintJobStatus.Printing;
                job.Attempts++;
                // While PRINTING, PrintedAt is used as the lease timestamp.
                // On failure it is cleared; on success it becomes the actual printed timestamp.
                job.PrintedAt = claimedAt;
                job.LastError = null;
                jobs.Add(job);
            }

            await db.SaveChangesAsync(ct);

            if (jobs.Count == 0)
                return Results.Ok(new { jobs = Array.Empty<object>() });

            return Results.Ok(new
            {
                jobs = jobs.Select(job =>
                {
                    var route = routes[job.PrinterKey];
                    return new
                    {
                        id = job.Id,
                        type = job.Type,
                        payloadJson = job.PayloadJson,
                        attempts = job.Attempts,
                        createdAt = job.CreatedAt,
                        printer = new
                        {
                            id = route.PrinterId,
                            name = route.PrinterName,
                            connectionType = route.ConnectionType.ToString(),
                            address = route.Address,
                            port = route.Port
                        }
                    };
                })
            });
        });

        group.MapPost("/{deviceId:guid}/print-jobs/{jobId:guid}/complete", async (
            Guid deviceId,
            Guid jobId,
            CompletePrintJobRequest request,
            HttpRequest httpRequest,
            IConfiguration configuration,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            var authError = ValidateAgentKey(httpRequest, configuration);
            if (authError is not null) return authError;

            if (!await DeviceExistsAsync(db, request.RestaurantId, deviceId, ct))
                return Results.NotFound(new { message = "Active POS device was not found for this restaurant." });

            var job = await db.PrintJobs.FirstOrDefaultAsync(
                x => x.Id == jobId &&
                     x.RestaurantId == request.RestaurantId &&
                     x.Type == "KITCHEN_TICKET",
                ct);

            if (job is null)
                return Results.NotFound(new { message = "Print job was not found." });

            var routes = await GetRoutesAsync(db, request.RestaurantId, deviceId, ct);
            if (!routes.ContainsKey(job.PrinterKey))
                return Results.Conflict(new { message = "This print job is no longer assigned to this POS Agent." });

            if (request.Success)
            {
                var printedAt = DateTimeOffset.UtcNow;
                job.Status = PrintJobStatus.Printed;
                job.PrintedAt = printedAt;
                job.LastError = null;

                var ticketId = TryGetTicketId(job.PayloadJson);
                if (ticketId.HasValue)
                {
                    var ticket = await db.KitchenTickets.FirstOrDefaultAsync(
                        x => x.Id == ticketId.Value &&
                             x.RestaurantId == request.RestaurantId,
                        ct);

                    if (ticket is not null)
                    {
                        ticket.Status = KitchenTicketStatus.Printed;
                        ticket.PrintedAt = printedAt;
                    }
                }
            }
            else
            {
                job.Status = PrintJobStatus.Failed;
                job.PrintedAt = null;
                job.LastError = NormalizeError(request.Error);
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = job.Id,
                status = job.Status.ToString().ToUpperInvariant(),
                attempts = job.Attempts,
                job.PrintedAt,
                job.LastError
            });
        });

        return app;
    }

    private static async Task<Dictionary<string, PrinterRoute>> GetRoutesAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid deviceId,
        CancellationToken ct)
    {
        var rows = await (
            from station in db.KitchenStations.AsNoTracking()
            join printer in db.Printers.AsNoTracking()
                on station.PrinterId equals (Guid?)printer.Id
            where station.RestaurantId == restaurantId &&
                  station.IsActive &&
                  printer.RestaurantId == restaurantId &&
                  printer.HostDeviceId == deviceId &&
                  printer.IsConfigured &&
                  printer.IsActive
            select new
            {
                station.Id,
                PrinterId = printer.Id,
                PrinterName = printer.Name,
                printer.ConnectionType,
                printer.Address,
                printer.Port
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            x => $"kitchen:{x.Id:N}",
            x => new PrinterRoute(
                x.PrinterId,
                x.PrinterName,
                x.ConnectionType,
                x.Address,
                x.Port),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Task<bool> DeviceExistsAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid deviceId,
        CancellationToken ct) =>
        db.Devices.AsNoTracking().AnyAsync(x =>
            x.Id == deviceId &&
            x.RestaurantId == restaurantId &&
            x.Type == DeviceType.Pos &&
            x.IsActive,
            ct);

    private static Guid? TryGetOrderId(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("orderId", out var orderIdElement) &&
                orderIdElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(orderIdElement.GetString(), out var orderId))
            {
                return orderId;
            }
        }
        catch (JsonException)
        {
            // Invalid legacy payloads are suppressed instead of being printed blindly.
        }

        return null;
    }

    private static Guid? TryGetTicketId(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("ticketId", out var ticketIdElement) &&
                ticketIdElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(ticketIdElement.GetString(), out var ticketId))
            {
                return ticketId;
            }
        }
        catch (JsonException)
        {
            // Keep the print job result even if an old payload cannot be parsed.
        }

        return null;
    }

    private static string NormalizeError(string? value)
    {
        var error = value?.Trim();
        if (string.IsNullOrWhiteSpace(error))
            return "Kitchen print failed without an error message.";

        return error.Length <= 1000 ? error : error[..1000];
    }

    private static IResult? ValidateAgentKey(HttpRequest request, IConfiguration configuration)
    {
        var configuredKey = configuration["Agent:SharedKey"];
        var providedKey = request.Headers["X-Agent-Key"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(configuredKey))
            return Results.Problem(
                "POS Agent shared key is not configured on Restaurant Node.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        if (string.IsNullOrWhiteSpace(providedKey) || !SecureEquals(configuredKey, providedKey))
            return Results.Unauthorized();

        return null;
    }

    private static bool SecureEquals(string expected, string actual)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private sealed record PrinterRoute(
        Guid PrinterId,
        string PrinterName,
        PrinterConnectionType ConnectionType,
        string Address,
        int? Port);
}

public sealed record CompletePrintJobRequest(
    Guid RestaurantId,
    bool Success,
    string? Error = null);
