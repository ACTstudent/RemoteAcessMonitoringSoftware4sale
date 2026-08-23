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
                    if (TryGetPropertyIgnoreCase(doc.RootElement, "serverUrl", out var url) &&
                        TryGetPropertyIgnoreCase(doc.RootElement, "appName", out var name) &&
                        name.GetString() == "CAMS")
                    {
                        var discoveredUrl = url.GetString();
                        if (Uri.TryCreate(discoveredUrl, UriKind.Absolute, out var parsedUrl) &&
                            parsedUrl.Scheme == Uri.UriSchemeHttps &&
                            !string.IsNullOrWhiteSpace(parsedUrl.Host))
                        {
                            _cachedUrl = parsedUrl.ToString();
                            return _cachedUrl;
                        }
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

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
