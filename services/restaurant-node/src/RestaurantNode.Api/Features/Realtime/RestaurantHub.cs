using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RestaurantNode.Api.Features.Realtime;

[Authorize]
public sealed class RestaurantHub : Hub
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
}

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
