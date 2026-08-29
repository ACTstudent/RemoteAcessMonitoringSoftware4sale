using System.Net.Http.Json;
using System.Text.Json;
using Shared.Contracts;

namespace Client.Services;

public sealed class ManagedBrowserCollector : IDisposable
{
    private static readonly string[] BrowserNames = ["chrome", "msedge", "brave", "opera"];
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMilliseconds(350) };
    private readonly int[] _ports;

    public ManagedBrowserCollector(IEnumerable<int>? ports = null)
    {
        _ports = (ports ?? [9222, 9223, 9224, 9225]).Where(port => port is > 0 and <= 65535).Distinct().ToArray();
    }

    public async Task<BrowserWebsiteObservation?> TryGetActiveWebsiteAsync(CancellationToken cancellationToken = default)
    {
        foreach (var port in _ports)
        {
            try
            {
                using var response = await _httpClient.GetAsync($"http://127.0.0.1:{port}/json/list", cancellationToken);
                if (!response.IsSuccessStatusCode) continue;
                var tabs = await response.Content.ReadFromJsonAsync<List<DevToolsTab>>(cancellationToken: cancellationToken);
                var tab = tabs?.FirstOrDefault(item => item.Type == "page" && !string.IsNullOrWhiteSpace(item.Url));
                if (tab is null || !WebsiteDomainNormalizer.TryNormalize(tab.Url, out var domain)) continue;
                var browser = BrowserName(tab.WebSocketDebuggerUrl);
                return new BrowserWebsiteObservation(domain, browser, BrowserMonitoringStatus.Captured);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (HttpRequestException)
            {
                // The browser may not be managed or the endpoint may not be running.
            }
            catch (JsonException)
            {
                // Ignore malformed endpoint responses and allow fallback monitoring.
            }
        }

        return null;
    }

    private static string BrowserName(string? debuggerUrl)
    {
        if (string.IsNullOrWhiteSpace(debuggerUrl)) return "managed-browser";
        var value = debuggerUrl.ToLowerInvariant();
        return BrowserNames.FirstOrDefault(value.Contains) ?? "managed-browser";
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record DevToolsTab(string? Type, string? Url, string? WebSocketDebuggerUrl);
}
