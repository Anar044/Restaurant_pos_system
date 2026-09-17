using System.Net.Http.Json;
using PosAgent.Api.Printing;

namespace PosAgent.Api.Agent;

public sealed record AgentReceiptPrinterResponse(Guid Id, string QueueName);
public sealed record AgentSyncResponse(
    Guid DeviceId,
    string? DeviceName,
    Guid RestaurantId,
    string? MachineName,
    DateTimeOffset SyncedAt,
    int SyncIntervalSeconds,
    AgentReceiptPrinterResponse? ReceiptPrinter);

public sealed class RestaurantNodeClient(HttpClient httpClient, IConfiguration configuration)
{
    public bool TryGetIdentity(out Guid restaurantId, out Guid deviceId, out string? error)
    {
        restaurantId = Guid.Empty;
        deviceId = Guid.Empty;
        error = null;

        if (!Guid.TryParse(configuration["Restaurant:Id"], out restaurantId) || restaurantId == Guid.Empty)
        {
            error = "Restaurant:Id is missing or invalid.";
            return false;
        }

        if (!Guid.TryParse(configuration["Device:Id"], out deviceId) || deviceId == Guid.Empty)
        {
            error = "Device:Id is missing or invalid. Create a POS device in BackOffice and configure its ID on this POS.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration["Agent:SharedKey"]))
        {
            error = "Agent:SharedKey is not configured.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration["RestaurantNode:BaseUrl"]))
        {
            error = "RestaurantNode:BaseUrl is not configured.";
            return false;
        }

        return true;
    }

    public async Task<AgentSyncResponse> SyncAsync(
        Guid restaurantId,
        Guid deviceId,
        IReadOnlyList<LocalPrinterInfo> printers,
        CancellationToken ct)
    {
        var baseUrl = configuration["RestaurantNode:BaseUrl"]!.TrimEnd('/');
        var sharedKey = configuration["Agent:SharedKey"]!;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/api/v1/agents/pos/{deviceId}/heartbeat");
        request.Headers.Add("X-Agent-Key", sharedKey);
        request.Content = JsonContent.Create(new
        {
            restaurantId,
            machineName = Environment.MachineName,
            printers = printers.Select(x => new
            {
                queueName = x.QueueName,
                isDefault = x.IsDefault
            })
        });

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AgentSyncResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Restaurant Node returned an empty POS Agent response.");
    }
}
