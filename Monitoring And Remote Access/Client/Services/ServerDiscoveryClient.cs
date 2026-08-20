using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Client.Services;

public static class ServerDiscoveryClient
{
    private const int DiscoveryPort = 5001;
    private static string? _cachedUrl;

    public static async Task<string?> DiscoverAsync(int timeoutMs = 4000, int retries = 3)
    {
        if (_cachedUrl != null)
            return _cachedUrl;

        for (int attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                using var client = new UdpClient();
                client.EnableBroadcast = true;
                client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

                var cts = new CancellationTokenSource(timeoutMs);

                try
                {
                    var result = await client.ReceiveAsync(cts.Token);
                    var json = System.Text.Encoding.UTF8.GetString(result.Buffer);

                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("serverUrl", out var url) &&
                        doc.RootElement.TryGetProperty("appName", out var name) &&
                        name.GetString() == "CAMS")
                    {
                        _cachedUrl = url.GetString();
                        return _cachedUrl;
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
            }
            catch (SocketException) { }

            await Task.Delay(500);
        }

        return null;
    }

    public static void ResetCache()
    {
        _cachedUrl = null;
    }
}