using System.Text.Json;

namespace PosAgent.Api.Agent;

public sealed class PrintedJobStore
{
    private const int MaxEntries = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public PrintedJobStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RestaurantPlatform",
            "PosAgent");
        Directory.CreateDirectory(root);
        path = Path.Combine(root, "printed-kitchen-jobs.json");
    }

    public async Task<bool> ContainsAsync(Guid jobId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var entries = await ReadInternalAsync(ct);
            return entries.Any(x => x.JobId == jobId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkPrintedAsync(Guid jobId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var entries = await ReadInternalAsync(ct);
            entries.RemoveAll(x => x.JobId == jobId);
            entries.Add(new PrintedJobReceipt(jobId, DateTimeOffset.UtcNow));

            if (entries.Count > MaxEntries)
            {
                entries = entries
                    .OrderByDescending(x => x.PrintedAt)
                    .Take(MaxEntries)
                    .OrderBy(x => x.PrintedAt)
                    .ToList();
            }

            var tempPath = path + ".tmp";
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, ct);

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<PrintedJobReceipt>> ReadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<PrintedJobReceipt>>(
                stream,
                JsonOptions,
                ct) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed record PrintedJobReceipt(
    Guid JobId,
    DateTimeOffset PrintedAt);
