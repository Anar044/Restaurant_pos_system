using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficePosPrinterEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficePosPrinterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/pos-printers")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var posDevices = await db.Devices
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.Type == DeviceType.Pos)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var deviceIds = posDevices.Select(x => x.Id).ToArray();
            var printers = await db.Printers
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId &&
                            x.HostDeviceId.HasValue &&
                            deviceIds.Contains(x.HostDeviceId.Value))
                .OrderBy(x => x.Name)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                posDevices = posDevices.Select(device => new
                {
                    id = device.Id,
                    name = device.Name,
                    isActive = device.IsActive,
                    lastSeenAt = device.LastSeenAt,
                    isOnline = IsOnline(device.LastSeenAt),
                    receiptPrinterId = device.ReceiptPrinterId,
                    discoveredWindowsPrinters = printers
                        .Where(printer => printer.HostDeviceId == device.Id &&
                                          printer.ConnectionType == PrinterConnectionType.WindowsQueue)
                        .OrderByDescending(printer => printer.IsDefault)
                        .ThenBy(printer => printer.Address)
                        .Select(printer => new
                        {
                            id = printer.Id,
                            queueName = printer.Address,
                            isDefault = printer.IsDefault,
                            lastSeenAt = printer.LastSeenAt,
                            isOnline = IsOnline(printer.LastSeenAt),
                            isConfigured = printer.IsConfigured
                        })
                        .ToArray(),
                    configuredPrinters = printers
                        .Where(printer => printer.HostDeviceId == device.Id && printer.IsConfigured)
                        .OrderBy(printer => printer.Name)
                        .Select(printer => new
                        {
                            id = printer.Id,
                            name = printer.Name,
                            connectionType = printer.ConnectionType.ToString(),
                            address = printer.Address,
                            port = printer.Port,
                            isActive = printer.IsActive,
                            lastSeenAt = printer.LastSeenAt,
                            isOnline = printer.ConnectionType == PrinterConnectionType.Network || IsOnline(printer.LastSeenAt),
                            isSelectedReceipt = device.ReceiptPrinterId == printer.Id
                        })
                        .ToArray()
                })
            });
        });

        group.MapPost("/{deviceId:guid}/printers", async (
            Guid deviceId,
            ConfigurePosPrinterRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var device = await db.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == deviceId &&
                         x.RestaurantId == restaurantId &&
                         x.Type == DeviceType.Pos &&
                         x.IsActive,
                    ct);
            if (device is null)
                return Results.NotFound(new { message = "Active POS device was not found." });

            var name = NormalizeName(request.Name, 100);
            if (name is null)
                return Results.BadRequest(new { message = "Printer name is required and must be 100 characters or fewer." });

            if (!Enum.TryParse<PrinterConnectionType>(request.ConnectionType, true, out var connectionType))
                return Results.BadRequest(new { message = "Unknown printer connection type." });

            var duplicateName = await db.Printers.AnyAsync(
                x => x.RestaurantId == restaurantId &&
                     x.HostDeviceId == deviceId &&
                     x.IsConfigured &&
                     x.Name.ToLower() == name.ToLower(),
                ct);
            if (duplicateName)
                return Results.Conflict(new { message = "A configured printer with this name already exists on this POS." });

            Printer printer;
            if (connectionType == PrinterConnectionType.WindowsQueue)
            {
                var queueName = NormalizeName(request.QueueName, 250);
                if (queueName is null)
                    return Results.BadRequest(new { message = "Select a Windows printer discovered by this POS Agent." });

                printer = await db.Printers.FirstOrDefaultAsync(
                    x => x.RestaurantId == restaurantId &&
                         x.HostDeviceId == deviceId &&
                         x.ConnectionType == PrinterConnectionType.WindowsQueue &&
                         x.Address == queueName,
                    ct) ?? null!;

                if (printer is null)
                    return Results.BadRequest(new { message = "This Windows printer was not discovered on the selected POS." });

                if (printer.IsConfigured)
                    return Results.Conflict(new { message = "This Windows printer is already configured on the selected POS." });

                printer.Name = name;
                printer.IsConfigured = true;
                printer.IsActive = true;
            }
            else
            {
                var address = NormalizeName(request.Address, 250);
                if (address is null)
                    return Results.BadRequest(new { message = "IP address or hostname is required for a network printer." });

                var port = request.Port ?? 9100;
                if (port is < 1 or > 65535)
                    return Results.BadRequest(new { message = "Network printer port must be between 1 and 65535." });

                var duplicateAddress = await db.Printers.AnyAsync(
                    x => x.RestaurantId == restaurantId &&
                         x.HostDeviceId == deviceId &&
                         x.Address == address,
                    ct);
                if (duplicateAddress)
                    return Results.Conflict(new { message = "This printer address is already registered on the selected POS." });

                printer = new Printer
                {
                    RestaurantId = restaurantId,
                    HostDeviceId = deviceId,
                    Name = name,
                    ConnectionType = PrinterConnectionType.Network,
                    Address = address,
                    Port = port,
                    IsConfigured = true,
                    IsActive = true
                };
                db.Printers.Add(printer);
            }

            AddAudit(db, user, restaurantId, "POS_PRINTER_CONFIGURED", "Printer", printer.Id, new
            {
                posDeviceId = deviceId,
                posDeviceName = device.Name,
                printer.Name,
                connectionType = printer.ConnectionType.ToString(),
                printer.Address,
                printer.Port
            });

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/pos-printers/{deviceId}/printers/{printer.Id}", new { id = printer.Id });
        }).RequireAuthorization(Permissions.DevicesManage);

        group.MapPut("/{deviceId:guid}/receipt-printer", async (
            Guid deviceId,
            SetPosReceiptPrinterRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var device = await db.Devices.FirstOrDefaultAsync(
                x => x.Id == deviceId && x.RestaurantId == restaurantId && x.Type == DeviceType.Pos,
                ct);
            if (device is null)
                return Results.NotFound(new { message = "POS device was not found." });

            Printer? printer = null;
            if (request.PrinterId.HasValue)
            {
                printer = await db.Printers.FirstOrDefaultAsync(
                    x => x.Id == request.PrinterId.Value &&
                         x.RestaurantId == restaurantId &&
                         x.HostDeviceId == deviceId &&
                         x.IsConfigured &&
                         x.IsActive,
                    ct);
                if (printer is null)
                    return Results.BadRequest(new { message = "Active configured printer was not found on this POS." });
            }

            device.ReceiptPrinterId = request.PrinterId;
            AddAudit(db, user, restaurantId, "POS_RECEIPT_PRINTER_ASSIGNED", "Device", device.Id, new
            {
                device.Name,
                printerId = printer?.Id,
                printerName = printer?.Name,
                connectionType = printer?.ConnectionType.ToString()
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = device.Id,
                receiptPrinterId = device.ReceiptPrinterId,
                receiptPrinterName = printer?.Name
            });
        }).RequireAuthorization(Permissions.DevicesManage);

        return app;
    }

    private static bool IsOnline(DateTimeOffset? lastSeenAt) =>
        lastSeenAt.HasValue && lastSeenAt.Value >= DateTimeOffset.UtcNow.AddMinutes(-2);

    private static string? NormalizeName(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength ? null : normalized;
    }

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static void AddAudit(
        RestaurantDbContext db,
        ClaimsPrincipal user,
        Guid restaurantId,
        string eventType,
        string entityType,
        Guid entityId,
        object payload)
    {
        Guid? employeeId = Guid.TryParse(user.FindFirstValue("employee_id"), out var parsedEmployeeId)
            ? parsedEmployeeId
            : null;

        db.AuditEvents.Add(new AuditEvent
        {
            RestaurantId = restaurantId,
            EmployeeId = employeeId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(payload)
        });
    }
}

public sealed record ConfigurePosPrinterRequest(
    string Name,
    string ConnectionType,
    string? QueueName,
    string? Address,
    int? Port);

public sealed record SetPosReceiptPrinterRequest(Guid? PrinterId);