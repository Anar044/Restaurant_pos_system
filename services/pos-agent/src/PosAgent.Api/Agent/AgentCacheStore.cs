using System.Text.Json;

namespace PosAgent.Api.Agent;

public sealed record AgentLocalConfig(
    Guid? DeviceId,
    string? DeviceName,
    Guid? ReceiptPrinterId,
    string? ReceiptPrinterName,
    string? ReceiptPrinterConnectionType,
    string? ReceiptPrinterAddress,
    int? ReceiptPrinterPort,
    DateTimeOffset? SyncedAt);

public sealed class AgentCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public AgentCacheStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RestaurantPlatform",
            "PosAgent");
        Directory.CreateDirectory(root);
        path = Path.Combine(root, "agent-cache.json");
    }

    public async Task<AgentLocalConfig> ReadAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(path))
                return Empty();

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AgentLocalConfig>(stream, JsonOptions, ct)
                ?? Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteAsync(AgentLocalConfig config, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var tempPath = path + ".tmp";
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, config, JsonOptions, ct);

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private static AgentLocalConfig Empty() =>
        new(null, null, null, null, null, null, null, null);
}
