using System.Net.Sockets;

namespace PosAgent.Api.Printing;

public sealed class NetworkRawPrinter
{
    public async Task SendAsync(string address, int port, byte[] payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Network printer address is required.", nameof(address));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Printer port must be between 1 and 65535.");
        if (payload.Length == 0)
            throw new ArgumentException("Print payload is empty.", nameof(payload));

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        await client.ConnectAsync(address, port, timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(payload, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
}
