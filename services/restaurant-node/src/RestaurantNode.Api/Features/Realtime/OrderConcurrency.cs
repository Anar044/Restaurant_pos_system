using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Features.Realtime;

public static class OrderConcurrency
{
    public static bool TryReadExpectedVersion(
        HttpRequest request,
        out int? expectedVersion,
        out IResult? error)
    {
        expectedVersion = null;
        error = null;

        var raw = request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var normalized = raw.Trim().Trim('"');
        if (!int.TryParse(normalized, out var parsed) || parsed < 0)
        {
            error = Results.BadRequest(new
            {
                code = "INVALID_ORDER_VERSION",
                message = "If-Match must contain the numeric order version."
            });
            return false;
        }

        expectedVersion = parsed;
        return true;
    }

    public static IResult? Validate(HttpRequest request, Order order)
    {
        if (!TryReadExpectedVersion(request, out var expectedVersion, out var error))
            return error;

        if (!expectedVersion.HasValue || expectedVersion.Value == order.Version)
            return null;

        return Conflict(order.Id, order.Version);
    }

    public static IResult Conflict(Guid orderId, int currentVersion) =>
        Results.Conflict(new
        {
            code = "ORDER_VERSION_CONFLICT",
            message = "Order was changed on another terminal. Refresh the order and try again.",
            orderId,
            currentVersion
        });
}
