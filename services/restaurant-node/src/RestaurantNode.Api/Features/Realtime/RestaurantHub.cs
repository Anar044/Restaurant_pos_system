using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Realtime;

[Authorize]
public sealed class RestaurantHub(
    OrderEditLockRegistry editLocks,
    RestaurantDbContext db) : Hub
{
    internal static string GroupName(Guid restaurantId) =>
        $"restaurant:{restaurantId:N}";

    public override async Task OnConnectedAsync()
    {
        var value = Context.User?.FindFirstValue("restaurant_id");
        if (!Guid.TryParse(value, out var restaurantId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(restaurantId));
        await base.OnConnectedAsync();
    }

    public async Task<OrderEditLockResponse> BeginEditingTable(
        Guid tableId,
        string? orderId,
        Guid deviceId,
        string instanceId)
    {
        if (!TryClaims(out var restaurantId, out var employeeId, out var employeeName) ||
            tableId == Guid.Empty ||
            deviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(instanceId) ||
            instanceId.Length > 120)
        {
            throw new HubException("Invalid table edit lock request.");
        }

        Guid? parsedOrderId = null;
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            if (!Guid.TryParse(orderId, out var parsed))
                throw new HubException("Invalid order id.");

            parsedOrderId = parsed;
        }

        var tableExists = await db.DiningTables
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == tableId &&
                x.RestaurantId == restaurantId &&
                x.IsActive,
                Context.ConnectionAborted);

        if (!tableExists)
            throw new HubException("Table was not found.");

        if (parsedOrderId.HasValue)
        {
            var orderMatches = await db.Orders
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == parsedOrderId.Value &&
                    x.RestaurantId == restaurantId &&
                    x.TableId == tableId,
                    Context.ConnectionAborted);

            if (!orderMatches)
                throw new HubException("Order does not belong to this table.");
        }

        var deviceName = await db.Devices
            .AsNoTracking()
            .Where(x =>
                x.Id == deviceId &&
                x.RestaurantId == restaurantId &&
                x.Type == DeviceType.Pos &&
                x.IsActive)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        deviceName ??= $"POS {deviceId.ToString("N")[..8]}";

        var result = editLocks.Acquire(
            restaurantId,
            tableId,
            parsedOrderId,
            deviceId,
            deviceName,
            employeeId,
            employeeName,
            instanceId.Trim(),
            Context.ConnectionId);

        if (result.Acquired && result.Changed)
            await PublishLockChangedAsync(result.Owner, locked: true);

        return ToResponse(result);
    }

    public async Task<bool> EndEditingTable(Guid tableId, string instanceId)
    {
        if (!TryRestaurantId(out var restaurantId) ||
            tableId == Guid.Empty ||
            string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        var released = editLocks.Release(
            restaurantId,
            tableId,
            instanceId.Trim());

        if (released is null)
            return false;

        await PublishLockChangedAsync(released, locked: false);
        return true;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var released = editLocks.ReleaseByConnection(Context.ConnectionId);
        foreach (var owner in released)
            await PublishLockChangedAsync(owner, locked: false);

        await base.OnDisconnectedAsync(exception);
    }

    private bool TryClaims(
        out Guid restaurantId,
        out Guid employeeId,
        out string employeeName)
    {
        restaurantId = Guid.Empty;
        employeeId = Guid.Empty;
        employeeName = Context.User?.Identity?.Name ?? "Employee";

        return TryRestaurantId(out restaurantId) &&
               Guid.TryParse(
                   Context.User?.FindFirstValue("employee_id"),
                   out employeeId);
    }

    private bool TryRestaurantId(out Guid restaurantId) =>
        Guid.TryParse(
            Context.User?.FindFirstValue("restaurant_id"),
            out restaurantId);

    private Task PublishLockChangedAsync(
        OrderEditLockOwner owner,
        bool locked) =>
        Clients
            .Group(GroupName(owner.RestaurantId))
            .SendAsync(
                "OrderEditLockChanged",
                new
                {
                    owner.TableId,
                    owner.OrderId,
                    locked,
                    owner.DeviceId,
                    owner.DeviceName,
                    owner.EmployeeName,
                    owner.ExpiresAt
                },
                Context.ConnectionAborted);

    private static OrderEditLockResponse ToResponse(
        OrderEditLockAcquireResult result) =>
        new(
            result.Acquired,
            result.Owner.TableId,
            result.Owner.OrderId,
            result.Owner.DeviceId,
            result.Owner.DeviceName,
            result.Owner.EmployeeName,
            result.Owner.ExpiresAt);
}

public sealed record OrderEditLockResponse(
    bool Acquired,
    Guid TableId,
    Guid? OrderId,
    Guid DeviceId,
    string DeviceName,
    string EmployeeName,
    DateTimeOffset ExpiresAt);

public sealed record RestaurantChangedMessage(
    string Resource,
    string Operation,
    Guid? OrderId,
    DateTimeOffset Utc);

public sealed class RestaurantRealtimePublisher(IHubContext<RestaurantHub> hub)
{
    public Task PublishAsync(
        Guid restaurantId,
        string resource,
        string operation,
        Guid? orderId,
        CancellationToken cancellationToken = default) =>
        hub.Clients
            .Group(RestaurantHub.GroupName(restaurantId))
            .SendAsync(
                "RestaurantChanged",
                new RestaurantChangedMessage(
                    resource,
                    operation,
                    orderId,
                    DateTimeOffset.UtcNow),
                cancellationToken);
}
