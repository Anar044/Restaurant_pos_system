using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Shifts;

public static class ShiftEndpoints
{
    public static IEndpointRouteBuilder MapShiftEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shifts").RequireAuthorization();

        group.MapGet("/current", async (
            Guid deviceId,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var deviceExists = await db.Devices
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == deviceId &&
                    x.RestaurantId == restaurantId &&
                    x.Type == DeviceType.Pos &&
                    x.IsActive,
                    ct);

            if (!deviceExists)
                return Results.BadRequest(new { message = "POS device is not available in this restaurant." });

            var shift = await db.Shifts
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.DeviceId == deviceId &&
                    x.Status == ShiftStatus.Open)
                .OrderByDescending(x => x.OpenedAt)
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                shift = shift is null ? null : ToDto(shift)
            });
        }).RequireAuthorization("shifts.manage");

        group.MapPost("/open", async (
            OpenShiftRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.DeviceId == Guid.Empty)
                return Results.BadRequest(new { message = "DeviceId is required." });

            if (request.OpeningCash < 0)
                return Results.BadRequest(new { message = "Opening cash cannot be negative." });

            var device = await db.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.DeviceId &&
                    x.RestaurantId == restaurantId &&
                    x.Type == DeviceType.Pos &&
                    x.IsActive,
                    ct);

            if (device is null)
                return Results.BadRequest(new { message = "POS device is not available in this restaurant." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var existing = await db.Shifts
                .FirstOrDefaultAsync(x =>
                    x.RestaurantId == restaurantId &&
                    x.DeviceId == request.DeviceId &&
                    x.Status == ShiftStatus.Open,
                    ct);

            if (existing is not null)
                return Results.Conflict(new
                {
                    message = "This POS already has an open shift.",
                    shift = ToDto(existing)
                });

            var shift = new Shift
            {
                RestaurantId = restaurantId,
                DeviceId = request.DeviceId,
                OpenedByEmployeeId = employeeId,
                OpeningCash = decimal.Round(request.OpeningCash, 4, MidpointRounding.AwayFromZero),
                Status = ShiftStatus.Open,
                OpenedAt = DateTimeOffset.UtcNow
            };

            db.Shifts.Add(shift);
            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                request.DeviceId,
                "SHIFT_OPENED",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.OpeningCash,
                    device.Name
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "SHIFT_OPENED",
                "Shift",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.OpeningCash,
                    shift.OpenedAt
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/v1/shifts/{shift.Id}", ToDto(shift));
        }).RequireAuthorization("shifts.manage");

        group.MapPost("/{id:guid}/close", async (
            Guid id,
            CloseShiftRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (request.ClosingCash < 0)
                return Results.BadRequest(new { message = "Closing cash cannot be negative." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var shift = await db.Shifts
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.RestaurantId == restaurantId,
                    ct);

            if (shift is null)
                return Results.NotFound(new { message = "Shift not found." });

            if (shift.Status != ShiftStatus.Open)
                return Results.Conflict(new { message = "Shift is already closed." });

            var pendingPayments = await db.Payments
                .AsNoTracking()
                .AnyAsync(x =>
                    x.RestaurantId == restaurantId &&
                    x.ShiftId == shift.Id &&
                    x.Status == PaymentStatus.Pending,
                    ct);

            if (pendingPayments)
                return Results.Conflict(new { message = "Shift has pending payments and cannot be closed." });

            var completedPayments = await db.Payments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.ShiftId == shift.Id &&
                    x.Status == PaymentStatus.Completed)
                .GroupBy(x => x.Method)
                .Select(x => new
                {
                    Method = x.Key,
                    Total = x.Sum(p => p.Amount)
                })
                .ToListAsync(ct);

            var cashSales = completedPayments
                .Where(x => x.Method == PaymentMethod.Cash)
                .Sum(x => x.Total);

            var cashAdjustments = await db.CashTransactions
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.ShiftId == shift.Id)
                .SumAsync(
                    x => x.Type == CashTransactionType.Deposit ? x.Amount : -x.Amount,
                    ct);

            var expectedCash = shift.OpeningCash + cashSales + cashAdjustments;
            var closingCash = decimal.Round(request.ClosingCash, 4, MidpointRounding.AwayFromZero);

            shift.ClosingCash = closingCash;
            shift.ClosedByEmployeeId = employeeId;
            shift.ClosedAt = DateTimeOffset.UtcNow;
            shift.Status = ShiftStatus.Closed;

            db.AuditEvents.Add(Audit(
                restaurantId,
                employeeId,
                shift.DeviceId,
                "SHIFT_CLOSED",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.OpeningCash,
                    closingCash,
                    expectedCash,
                    difference = closingCash - expectedCash,
                    payments = completedPayments.Select(x => new
                    {
                        method = x.Method.ToString().ToUpperInvariant(),
                        x.Total
                    })
                }));
            db.OutboxEvents.Add(Outbox(
                restaurantId,
                "SHIFT_CLOSED",
                "Shift",
                shift.Id,
                new
                {
                    shift.Id,
                    shift.DeviceId,
                    shift.ClosedAt,
                    closingCash,
                    expectedCash
                }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                shift = ToDto(shift),
                expectedCash,
                difference = closingCash - expectedCash,
                payments = completedPayments.Select(x => new
                {
                    method = x.Method.ToString().ToUpperInvariant(),
                    x.Total
                })
            });
        }).RequireAuthorization("shifts.manage");

        return app;
    }

    private static object ToDto(Shift shift) => new
    {
        shift.Id,
        shift.RestaurantId,
        shift.DeviceId,
        shift.OpenedByEmployeeId,
        shift.ClosedByEmployeeId,
        status = shift.Status.ToString().ToUpperInvariant(),
        shift.OpeningCash,
        shift.ClosingCash,
        shift.OpenedAt,
        shift.ClosedAt
    };

    private static bool TryClaims(
        ClaimsPrincipal user,
        out Guid restaurantId,
        out Guid employeeId)
    {
        var okRestaurant = Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
        var okEmployee = Guid.TryParse(user.FindFirstValue("employee_id"), out employeeId);
        return okRestaurant && okEmployee;
    }

    private static AuditEvent Audit(
        Guid restaurantId,
        Guid employeeId,
        Guid deviceId,
        string eventType,
        Guid entityId,
        object payload) => new()
    {
        RestaurantId = restaurantId,
        EmployeeId = employeeId,
        DeviceId = deviceId,
        EventType = eventType,
        EntityType = "Shift",
        EntityId = entityId,
        PayloadJson = JsonSerializer.Serialize(payload)
    };

    private static OutboxEvent Outbox(
        Guid restaurantId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object payload) => new()
    {
        RestaurantId = restaurantId,
        EventType = eventType,
        AggregateType = aggregateType,
        AggregateId = aggregateId,
        PayloadJson = JsonSerializer.Serialize(payload)
    };
}

public sealed record OpenShiftRequest(Guid DeviceId, decimal OpeningCash = 0m);
public sealed record CloseShiftRequest(decimal ClosingCash);
