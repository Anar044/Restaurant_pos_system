using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeDeviceEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/devices")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var printers = await db.Printers
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsConfigured)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var printerNames = printers.ToDictionary(x => x.Id, x => x.Name);

            var devices = await db.Devices
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var stations = await db.KitchenStations
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                deviceTypes = Enum.GetNames<DeviceType>(),
                printerConnectionTypes = Enum.GetNames<PrinterConnectionType>(),
                devices = devices.Select(device => new
                {
                    id = device.Id,
                    name = device.Name,
                    type = device.Type.ToString(),
                    receiptPrinterId = device.ReceiptPrinterId,
                    receiptPrinterName = device.ReceiptPrinterId.HasValue && printerNames.TryGetValue(device.ReceiptPrinterId.Value, out var receiptPrinterName)
                        ? receiptPrinterName
                        : null,
                    isActive = device.IsActive,
                    lastSeenAt = device.LastSeenAt,
                    isOnline = IsOnline(device.LastSeenAt)
                }),
                printers = printers.Select(printer => new
                {
                    id = printer.Id,
                    hostDeviceId = printer.HostDeviceId,
                    name = printer.Name,
                    connectionType = printer.ConnectionType.ToString(),
                    address = printer.Address,
                    port = printer.Port,
                    isActive = printer.IsActive,
                    lastSeenAt = printer.LastSeenAt,
                    isOnline = printer.ConnectionType == PrinterConnectionType.Network || IsOnline(printer.LastSeenAt),
                    kitchenStationCount = stations.Count(x => x.PrinterId == printer.Id),
                    posDeviceCount = devices.Count(x => x.Type == DeviceType.Pos && x.ReceiptPrinterId == printer.Id)
                }),
                kitchenStations = stations.Select(station => new
                {
                    id = station.Id,
                    name = station.Name,
                    isActive = station.IsActive,
                    printerId = station.PrinterId,
                    printerName = station.PrinterId.HasValue && printerNames.TryGetValue(station.PrinterId.Value, out var printerName)
                        ? printerName
                        : null
                })
            });
        });

        group.MapPost("/printers", async (
            CreatePrinterRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var validation = ValidatePrinter(request.Name, request.ConnectionType, request.Address, request.Port);
            if (validation.Error is not null)
                return validation.Error;

            var duplicate = await db.Printers.AnyAsync(
                x => x.RestaurantId == restaurantId && x.IsConfigured && x.Name.ToLower() == validation.Name!.ToLower(),
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A configured printer with this name already exists." });

            var printer = new Printer
            {
                RestaurantId = restaurantId,
                Name = validation.Name!,
                ConnectionType = validation.ConnectionType!.Value,
                Address = validation.Address!,
                Port = validation.Port,
                IsConfigured = true,
                IsActive = true
            };

            db.Printers.Add(printer);
            AddAudit(db, user, restaurantId, "PRINTER_CREATED", "Printer", printer.Id, new
            {
                printer.Name,
                connectionType = printer.ConnectionType.ToString(),
                printer.Address,
                printer.Port,
                printer.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/devices/printers/{printer.Id}", new { id = printer.Id });
        }).RequireAuthorization(Permissions.DevicesManage);

        group.MapPut("/printers/{printerId:guid}", async (
            Guid printerId,
            UpdatePrinterRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var printer = await db.Printers.FirstOrDefaultAsync(
                x => x.Id == printerId && x.RestaurantId == restaurantId && x.IsConfigured,
                ct);
            if (printer is null)
                return Results.NotFound();

            var validation = ValidatePrinter(request.Name, request.ConnectionType, request.Address, request.Port);
            if (validation.Error is not null)
                return validation.Error;

            var duplicate = await db.Printers.AnyAsync(
                x => x.RestaurantId == restaurantId && x.IsConfigured && x.Id != printerId && x.Name.ToLower() == validation.Name!.ToLower(),
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A configured printer with this name already exists." });

            if (printer.IsActive && !request.IsActive)
            {
                var stationNames = await db.KitchenStations
                    .AsNoTracking()
                    .Where(x => x.RestaurantId == restaurantId && x.PrinterId == printerId && x.IsActive)
                    .Select(x => x.Name)
                    .OrderBy(x => x)
                    .Take(5)
                    .ToListAsync(ct);

                var deviceNames = await db.Devices
                    .AsNoTracking()
                    .Where(x => x.RestaurantId == restaurantId && x.ReceiptPrinterId == printerId && x.IsActive)
                    .Select(x => x.Name)
                    .OrderBy(x => x)
                    .Take(5)
                    .ToListAsync(ct);

                if (stationNames.Count > 0 || deviceNames.Count > 0)
                {
                    return Results.Conflict(new
                    {
                        message = "The printer cannot be deactivated while active kitchen stations or POS devices use it. Remove those assignments first.",
                        kitchenStations = stationNames,
                        devices = deviceNames
                    });
                }
            }

            printer.Name = validation.Name!;
            printer.ConnectionType = validation.ConnectionType!.Value;
            printer.Address = validation.Address!;
            printer.Port = validation.Port;
            printer.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "PRINTER_UPDATED", "Printer", printer.Id, new
            {
                printer.Name,
                connectionType = printer.ConnectionType.ToString(),
                printer.Address,
                printer.Port,
                printer.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = printer.Id });
        }).RequireAuthorization(Permissions.DevicesManage);

        group.MapPost("/terminals", async (
            CreateDeviceRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var validation = await ValidateDevice(db, restaurantId, request.Name, request.Type, request.ReceiptPrinterId, ct);
            if (validation.Error is not null)
                return validation.Error;

            var duplicate = await db.Devices.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name.ToLower() == validation.Name!.ToLower(),
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A device with this name already exists." });

            var device = new Device
            {
                RestaurantId = restaurantId,
                Name = validation.Name!,
                Type = validation.Type!.Value,
                ReceiptPrinterId = validation.ReceiptPrinterId,
                IsActive = true
            };

            db.Devices.Add(device);
            AddAudit(db, user, restaurantId, "DEVICE_CREATED", "Device", device.Id, new
            {
                device.Name,
                type = device.Type.ToString(),
                device.ReceiptPrinterId,
                device.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/devices/terminals/{device.Id}", new { id = device.Id });
        }).RequireAuthorization(Permissions.DevicesManage);

        group.MapPut("/terminals/{deviceId:guid}", async (
            Guid deviceId,
            UpdateDeviceRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var device = await db.Devices.FirstOrDefaultAsync(
                x => x.Id == deviceId && x.RestaurantId == restaurantId,
                ct);
            if (device is null)
                return Results.NotFound();

            var validation = await ValidateDevice(db, restaurantId, request.Name, request.Type, request.ReceiptPrinterId, ct);
            if (validation.Error is not null)
                return validation.Error;

            var duplicate = await db.Devices.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != deviceId && x.Name.ToLower() == validation.Name!.ToLower(),
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A device with this name already exists." });

            if (device.IsActive && !request.IsActive)
            {
                var hasOpenShift = await db.Shifts.AnyAsync(
                    x => x.RestaurantId == restaurantId && x.DeviceId == deviceId && x.Status == ShiftStatus.Open,
                    ct);
                if (hasOpenShift)
                    return Results.Conflict(new { message = "The device cannot be deactivated while it has an open shift." });
            }

            device.Name = validation.Name!;
            device.Type = validation.Type!.Value;
            device.ReceiptPrinterId = validation.ReceiptPrinterId;
            device.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "DEVICE_UPDATED", "Device", device.Id, new
            {
                device.Name,
                type = device.Type.ToString(),
                device.ReceiptPrinterId,
                device.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = device.Id });
        }).RequireAuthorization(Permissions.DevicesManage);

        group.MapPut("/kitchen-stations/{stationId:guid}/printer", async (
            Guid stationId,
            SetKitchenPrinterRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var station = await db.KitchenStations.FirstOrDefaultAsync(
                x => x.Id == stationId && x.RestaurantId == restaurantId,
                ct);
            if (station is null)
                return Results.NotFound();

            if (request.PrinterId.HasValue)
            {
                var printerExists = await db.Printers.AnyAsync(
                    x => x.Id == request.PrinterId.Value &&
                         x.RestaurantId == restaurantId &&
                         x.IsConfigured &&
                         x.IsActive,
                    ct);
                if (!printerExists)
                    return Results.BadRequest(new { message = "Active configured printer was not found." });
            }

            station.PrinterId = request.PrinterId;
            AddAudit(db, user, restaurantId, "KITCHEN_PRINTER_ASSIGNED", "KitchenStation", station.Id, new
            {
                station.Name,
                station.PrinterId
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = station.Id, printerId = station.PrinterId });
        }).RequireAuthorization(Permissions.DevicesManage);

        return app;
    }

    private static bool IsOnline(DateTimeOffset? lastSeenAt) =>
        lastSeenAt.HasValue && lastSeenAt.Value >= DateTimeOffset.UtcNow.AddMinutes(-2);

    private static PrinterValidationResult ValidatePrinter(
        string? rawName,
        string? rawConnectionType,
        string? rawAddress,
        int? requestedPort)
    {
        var name = NormalizeName(rawName, 100);
        if (name is null)
            return PrinterValidationResult.Fail(Results.BadRequest(new { message = "Printer name is required and must be 100 characters or fewer." }));

        if (!Enum.TryParse<PrinterConnectionType>(rawConnectionType, ignoreCase: true, out var connectionType))
            return PrinterValidationResult.Fail(Results.BadRequest(new { message = "Unknown printer connection type." }));

        var address = NormalizeName(rawAddress, 250);
        if (address is null)
            return PrinterValidationResult.Fail(Results.BadRequest(new { message = "Printer address or Windows queue name is required." }));

        int? port = null;
        if (connectionType == PrinterConnectionType.Network)
        {
            port = requestedPort ?? 9100;
            if (port is < 1 or > 65535)
                return PrinterValidationResult.Fail(Results.BadRequest(new { message = "Network printer port must be between 1 and 65535." }));
        }

        return PrinterValidationResult.Ok(name, connectionType, address, port);
    }

    private static async Task<DeviceValidationResult> ValidateDevice(
        RestaurantDbContext db,
        Guid restaurantId,
        string? rawName,
        string? rawType,
        Guid? receiptPrinterId,
        CancellationToken ct)
    {
        var name = NormalizeName(rawName, 100);
        if (name is null)
            return DeviceValidationResult.Fail(Results.BadRequest(new { message = "Device name is required and must be 100 characters or fewer." }));

        if (!Enum.TryParse<DeviceType>(rawType, ignoreCase: true, out var type))
            return DeviceValidationResult.Fail(Results.BadRequest(new { message = "Unknown device type." }));

        if (receiptPrinterId.HasValue && type != DeviceType.Pos)
            return DeviceValidationResult.Fail(Results.BadRequest(new { message = "A receipt printer can only be assigned to a POS terminal." }));

        if (receiptPrinterId.HasValue)
        {
            var printerExists = await db.Printers.AnyAsync(
                x => x.Id == receiptPrinterId.Value &&
                     x.RestaurantId == restaurantId &&
                     x.IsConfigured &&
                     x.IsActive,
                ct);
            if (!printerExists)
                return DeviceValidationResult.Fail(Results.BadRequest(new { message = "Active configured receipt printer was not found." }));
        }

        return DeviceValidationResult.Ok(name, type, receiptPrinterId);
    }

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static string? NormalizeName(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength ? null : normalized;
    }

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

    private sealed record PrinterValidationResult(
        string? Name,
        PrinterConnectionType? ConnectionType,
        string? Address,
        int? Port,
        IResult? Error)
    {
        public static PrinterValidationResult Ok(string name, PrinterConnectionType connectionType, string address, int? port) =>
            new(name, connectionType, address, port, null);
        public static PrinterValidationResult Fail(IResult error) => new(null, null, null, null, error);
    }

    private sealed record DeviceValidationResult(
        string? Name,
        DeviceType? Type,
        Guid? ReceiptPrinterId,
        IResult? Error)
    {
        public static DeviceValidationResult Ok(string name, DeviceType type, Guid? receiptPrinterId) =>
            new(name, type, receiptPrinterId, null);
        public static DeviceValidationResult Fail(IResult error) => new(null, null, null, error);
    }
}

public sealed record CreatePrinterRequest(string Name, string ConnectionType, string Address, int? Port);
public sealed record UpdatePrinterRequest(string Name, string ConnectionType, string Address, int? Port, bool IsActive);
public sealed record CreateDeviceRequest(string Name, string Type, Guid? ReceiptPrinterId);
public sealed record UpdateDeviceRequest(string Name, string Type, Guid? ReceiptPrinterId, bool IsActive);
public sealed record SetKitchenPrinterRequest(Guid? PrinterId);