namespace RestaurantNode.Api.Features.Realtime;

public sealed record OrderEditLockOwner(
    Guid RestaurantId,
    Guid TableId,
    Guid? OrderId,
    Guid DeviceId,
    string DeviceName,
    Guid EmployeeId,
    string EmployeeName,
    string InstanceId,
    string ConnectionId,
    DateTimeOffset ExpiresAt);

public sealed record OrderEditLockAcquireResult(
    bool Acquired,
    bool Changed,
    OrderEditLockOwner Owner);

public sealed class OrderEditLockRegistry
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(75);
    private readonly object _gate = new();
    private readonly Dictionary<(Guid RestaurantId, Guid TableId), OrderEditLockOwner> _locks = [];

    public OrderEditLockAcquireResult Acquire(
        Guid restaurantId,
        Guid tableId,
        Guid? orderId,
        Guid deviceId,
        string deviceName,
        Guid employeeId,
        string employeeName,
        string instanceId,
        string connectionId)
    {
        lock (_gate)
        {
            PurgeExpiredUnsafe();

            var key = (restaurantId, tableId);
            var now = DateTimeOffset.UtcNow;

            if (_locks.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.InstanceId, instanceId, StringComparison.Ordinal))
                    return new OrderEditLockAcquireResult(false, false, existing);

                var refreshed = existing with
                {
                    OrderId = orderId ?? existing.OrderId,
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    EmployeeId = employeeId,
                    EmployeeName = employeeName,
                    ConnectionId = connectionId,
                    ExpiresAt = now.Add(LeaseDuration)
                };

                _locks[key] = refreshed;
                return new OrderEditLockAcquireResult(true, false, refreshed);
            }

            var owner = new OrderEditLockOwner(
                restaurantId,
                tableId,
                orderId,
                deviceId,
                deviceName,
                employeeId,
                employeeName,
                instanceId,
                connectionId,
                now.Add(LeaseDuration));

            _locks[key] = owner;
            return new OrderEditLockAcquireResult(true, true, owner);
        }
    }

    public OrderEditLockOwner? GetActive(Guid restaurantId, Guid tableId)
    {
        lock (_gate)
        {
            PurgeExpiredUnsafe();
            return _locks.GetValueOrDefault((restaurantId, tableId));
        }
    }

    public OrderEditLockOwner? Release(
        Guid restaurantId,
        Guid tableId,
        string instanceId)
    {
        lock (_gate)
        {
            PurgeExpiredUnsafe();
            var key = (restaurantId, tableId);

            if (!_locks.TryGetValue(key, out var existing) ||
                !string.Equals(existing.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return null;
            }

            _locks.Remove(key);
            return existing;
        }
    }

    public IReadOnlyList<OrderEditLockOwner> ReleaseByConnection(string connectionId)
    {
        lock (_gate)
        {
            PurgeExpiredUnsafe();

            var released = _locks
                .Where(pair => string.Equals(
                    pair.Value.ConnectionId,
                    connectionId,
                    StringComparison.Ordinal))
                .Select(pair => pair.Value)
                .ToArray();

            foreach (var owner in released)
                _locks.Remove((owner.RestaurantId, owner.TableId));

            return released;
        }
    }

    public IResult? ValidateMutation(
        HttpRequest request,
        Guid restaurantId,
        Guid tableId)
    {
        var owner = GetActive(restaurantId, tableId);
        if (owner is null)
            return null;

        var instanceId = request.Headers["X-POS-INSTANCE-ID"].FirstOrDefault();
        if (string.Equals(owner.InstanceId, instanceId, StringComparison.Ordinal))
            return null;

        return Locked(owner);
    }

    public static IResult Locked(OrderEditLockOwner owner) =>
        Results.Json(
            new
            {
                code = "ORDER_EDIT_LOCKED",
                message = $"Стол сейчас редактируется на {owner.DeviceName} ({owner.EmployeeName}).",
                tableId = owner.TableId,
                orderId = owner.OrderId,
                deviceId = owner.DeviceId,
                deviceName = owner.DeviceName,
                employeeName = owner.EmployeeName,
                expiresAt = owner.ExpiresAt
            },
            statusCode: StatusCodes.Status423Locked);

    private void PurgeExpiredUnsafe()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _locks
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in expired)
            _locks.Remove(key);
    }
}
