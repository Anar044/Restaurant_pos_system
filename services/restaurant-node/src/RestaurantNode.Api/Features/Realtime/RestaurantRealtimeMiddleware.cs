using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Infrastructure;

namespace RestaurantNode.Api.Features.Realtime;

public sealed class RestaurantRealtimeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        RestaurantDbContext db,
        RestaurantRealtimePublisher publisher,
        OrderEditLockRegistry editLocks)
    {
        var isMutation = !HttpMethods.IsGet(context.Request.Method) &&
                         !HttpMethods.IsHead(context.Request.Method) &&
                         !HttpMethods.IsOptions(context.Request.Method);

        var isOrderRoute =
            context.Request.Path.StartsWithSegments("/api/v1/orders");

        if (isMutation && isOrderRoute &&
            TryOrderId(context, out var orderId) &&
            Guid.TryParse(
                context.User.FindFirstValue("restaurant_id"),
                out var restaurantId))
        {
            if (!OrderConcurrency.TryReadExpectedVersion(
                    context.Request,
                    out var expectedVersion,
                    out var error))
            {
                await WriteResultAsync(context, error!);
                return;
            }

            var orderState = await db.Orders
                .AsNoTracking()
                .Where(x =>
                    x.Id == orderId &&
                    x.RestaurantId == restaurantId)
                .Select(x => new
                {
                    x.Version,
                    x.TableId
                })
                .FirstOrDefaultAsync(context.RequestAborted);

            if (orderState?.TableId is Guid tableId)
            {
                var lockConflict = editLocks.ValidateMutation(
                    context.Request,
                    restaurantId,
                    tableId);

                if (lockConflict is not null)
                {
                    await WriteResultAsync(context, lockConflict);
                    return;
                }
            }

            if (expectedVersion.HasValue &&
                orderState is not null &&
                orderState.Version != expectedVersion.Value)
            {
                await WriteResultAsync(
                    context,
                    OrderConcurrency.Conflict(orderId, orderState.Version));
                return;
            }
        }

        try
        {
            await next(context);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;

            if (isOrderRoute)
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "ORDER_VERSION_CONFLICT",
                    message = "Order was changed on another terminal. Refresh the order and try again."
                }, context.RequestAborted);
            }
            else
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "DATA_CONCURRENCY_CONFLICT",
                    message = "Данные были изменены другим пользователем. Обновите страницу и повторите попытку."
                }, context.RequestAborted);
            }

            return;
        }

        if (!isMutation ||
            context.Response.StatusCode < 200 ||
            context.Response.StatusCode >= 300 ||
            !Guid.TryParse(
                context.User.FindFirstValue("restaurant_id"),
                out var changedRestaurantId))
        {
            return;
        }

        var resource = GetRealtimeResource(context.Request.Path);
        if (resource is null)
            return;

        Guid? changedOrderId = null;
        if (resource == "orders" &&
            !context.Request.Path.Value!.Contains(
                "/transfer-items",
                StringComparison.OrdinalIgnoreCase) &&
            TryOrderId(context, out var routedOrderId))
        {
            changedOrderId = routedOrderId;
        }

        try
        {
            await publisher.PublishAsync(
                changedRestaurantId,
                resource,
                context.Request.Method.ToUpperInvariant(),
                changedOrderId,
                context.RequestAborted);
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    private static string? GetRealtimeResource(PathString path)
    {
        if (path.StartsWithSegments("/api/v1/orders"))
            return "orders";
        if (path.StartsWithSegments("/api/v1/payments"))
            return "payments";

        return null;
    }

    private static bool TryOrderId(HttpContext context, out Guid orderId)
    {
        orderId = Guid.Empty;
        var raw = context.Request.RouteValues["id"]?.ToString();
        return Guid.TryParse(raw, out orderId);
    }

    private static async Task WriteResultAsync(
        HttpContext context,
        IResult result)
    {
        context.Response.Clear();
        await result.ExecuteAsync(context);
    }
}
