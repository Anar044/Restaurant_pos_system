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
                            x.ConnectionType == PrinterConnectionType.WindowsQueue &&
                            x.HostDeviceId.HasValue &&
                            deviceIds.Contains(x.HostDeviceId.Value))
                .OrderBy(x => x.Name)
                .ToListAsync(ct);

            var networkPrinters = await db.Printers
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId &&
                            x.ConnectionType == PrinterConnectionType.Network &&
                            x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    address = x.Address,
                    port = x.Port
                })
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
                    windowsPrinters = printers
                        .Where(printer => printer.HostDeviceId == device.Id)
                        .Select(printer => new
                        {
                            id = printer.Id,
                            name = printer.Name,
                            queueName = printer.Address,
                            isActive = printer.IsActive,
                            lastSeenAt = printer.LastSeenAt,
                            isOnline = IsOnline(printer.LastSeenAt),
                            isSelectedReceipt = device.ReceiptPrinterId == printer.Id
                        })
                        .ToArray()
                }),
                networkPrinters
            });
        });

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
                    x => x.Id == request.PrinterId.Value && x.RestaurantId == restaurantId && x.IsActive,
                    ct);
                if (printer is null)
                    return Results.BadRequest(new { message = "Active printer was not found." });

                var validForPos = printer.ConnectionType == PrinterConnectionType.Network ||
                                  (printer.ConnectionType == PrinterConnectionType.WindowsQueue && printer.HostDeviceId == deviceId);
                if (!validForPos)
                {
                    return Results.BadRequest(new
                    {
                        message = "A Windows printer can only be assigned to the POS where it was discovered."
                    });
                }
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

public sealed record SetPosReceiptPrinterRequest(Guid? PrinterId);