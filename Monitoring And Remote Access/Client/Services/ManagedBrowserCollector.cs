using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Shared.Contracts;

namespace Client.Services;

public sealed record ManagedBrowserOptions(bool Enabled = true, bool ManageChrome = true, bool ManageBrave = true, int ChromePort = 9222, int BravePort = 9223, int RestartDelayMilliseconds = 1000);
public sealed record ManagedBrowserDefinition(string Identity, string ExecutableName, int Port);
public sealed record ManagedBrowserStatus(string Identity, bool Running, bool EndpointAvailable, string Message);

public sealed class ManagedBrowserCollector : IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMilliseconds(350) };
    private readonly ManagedBrowserOptions _options;
    private readonly string _profileRoot;
    private readonly Func<string, string?> _findExecutable;
    private readonly Dictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _processLock = new();
    private CancellationTokenSource? _lifecycleCts;
    private bool _disposed;

    public ManagedBrowserCollector(ManagedBrowserOptions? options = null, string? profileRoot = null, Func<string, string?>? findExecutable = null)
    {
        _options = options ?? new ManagedBrowserOptions();
        _profileRoot = profileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CAMS", "BrowserProfiles");
        _findExecutable = findExecutable ?? FindExecutable;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _lifecycleCts != null) return Task.CompletedTask;
        _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = MaintainBrowsersAsync(_lifecycleCts.Token);
        return Task.CompletedTask;
    }

    public IReadOnlyList<ManagedBrowserStatus> GetStatus() => Definitions().Select(definition =>
    {
        var running = IsManagedProcessRunning(definition.Identity);
        var endpointAvailable = running && IsEndpointAvailable(definition.Port);
        return new ManagedBrowserStatus(definition.Identity, running, endpointAvailable,
            endpointAvailable ? "Managed endpoint available" : running ? "Managed process starting" : "Managed browser unavailable");
    }).ToList();

    public async Task<BrowserWebsiteObservation?> TryGetActiveWebsiteAsync(CancellationToken cancellationToken = default)
        => await TryGetActiveWebsiteAsync(null, cancellationToken);

    public async Task<BrowserWebsiteObservation?> TryGetActiveWebsiteAsync(string? browserIdentity, CancellationToken cancellationToken = default)
        => await TryGetActiveWebsiteAsync(browserIdentity, null, cancellationToken);

    public async Task<BrowserWebsiteObservation?> TryGetActiveWebsiteAsync(
        string? browserIdentity,
        string? foregroundWindowTitle,
        CancellationToken cancellationToken = default)
    {
        foreach (var definition in Definitions().Where(definition =>
                     string.IsNullOrWhiteSpace(browserIdentity) || definition.Identity.Equals(browserIdentity, StringComparison.OrdinalIgnoreCase)))
        {
            if (!IsManagedProcessRunning(definition.Identity)) continue;
            try
            {
                using var metadataResponse = await _httpClient.GetAsync($"http://127.0.0.1:{definition.Port}/json/version", cancellationToken);
                if (!metadataResponse.IsSuccessStatusCode) continue;
                var metadata = await metadataResponse.Content.ReadFromJsonAsync<DevToolsMetadata>(cancellationToken: cancellationToken);
                if (!IsExpectedIdentity(metadata?.Browser, definition.Identity)) continue;
                using var tabsResponse = await _httpClient.GetAsync($"http://127.0.0.1:{definition.Port}/json/list", cancellationToken);
                if (!tabsResponse.IsSuccessStatusCode) continue;
                var tabs = await tabsResponse.Content.ReadFromJsonAsync<List<DevToolsTab>>(cancellationToken: cancellationToken);
                var tab = await SelectForegroundTabAsync(tabs, foregroundWindowTitle, cancellationToken);
                if (tab != null && WebsiteDomainNormalizer.TryNormalize(tab.Url, out var domain))
                    return new BrowserWebsiteObservation(domain, definition.Identity, BrowserMonitoringStatus.Captured, BrowserMonitoringMode.ManagedProtocol);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            catch (HttpRequestException) { }
            catch (JsonException) { }
        }
        return null;
    }

    private async Task<DevToolsTab?> SelectForegroundTabAsync(
        IReadOnlyList<DevToolsTab>? tabs,
        string? foregroundWindowTitle,
        CancellationToken cancellationToken)
    {
        var pages = tabs?.Where(item => item.Type == "page" &&
                !string.IsNullOrWhiteSpace(item.Url) &&
                WebsiteDomainNormalizer.TryNormalize(item.Url, out _))
            .ToList() ?? new List<DevToolsTab>();
        if (pages.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(foregroundWindowTitle))
        {
            var titleMatch = pages.FirstOrDefault(page => IsForegroundTitleMatch(page.Title, foregroundWindowTitle));
            if (titleMatch is not null) return titleMatch;
        }

        if (pages.Count == 1) return pages[0];
        foreach (var page in pages)
        {
            if (await IsVisiblePageAsync(page.WebSocketDebuggerUrl, cancellationToken))
                return page;
        }

        // Reporting no domain is safer than attributing a background tab as foreground.
        return null;
    }

    private static async Task<bool> IsVisiblePageAsync(string? debuggerUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(debuggerUrl, UriKind.Absolute, out var uri) || uri.Scheme != "ws" || !IsLoopback(uri.Host))
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
        using var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(uri, timeout.Token);
            var request = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"document.visibilityState\",\"returnByValue\":true}}");
            await socket.SendAsync(request, WebSocketMessageType.Text, true, timeout.Token);

            while (!timeout.IsCancellationRequested)
            {
                var message = await ReceiveMessageAsync(socket, timeout.Token);
                if (message is null) return false;
                using var document = JsonDocument.Parse(message);
                if (!document.RootElement.TryGetProperty("id", out var id) || id.GetInt32() != 1) continue;
                return document.RootElement.TryGetProperty("result", out var result) &&
                       result.TryGetProperty("result", out var remoteResult) &&
                       remoteResult.TryGetProperty("value", out var value) &&
                       value.ValueKind == JsonValueKind.String &&
                       value.GetString() == "visible";
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
        {
        }
        return false;
    }

    private static async Task<string?> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (stream.Length <= 16 * 1024)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
        }
        return null;
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    public static bool IsForegroundTitleMatch(string? tabTitle, string? foregroundWindowTitle) =>
        !string.IsNullOrWhiteSpace(tabTitle) &&
        !string.IsNullOrWhiteSpace(foregroundWindowTitle) &&
        foregroundWindowTitle.Contains(tabTitle, StringComparison.OrdinalIgnoreCase);

    public static string BuildArguments(ManagedBrowserDefinition definition, string profileRoot) =>
        $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={definition.Port} --user-data-dir=\"{Path.Combine(profileRoot, definition.Identity)}\" --no-first-run --no-default-browser-check";

    public static bool IsExpectedIdentity(string? browser, string identity)
    {
        if (string.IsNullOrWhiteSpace(browser)) return false;
        var value = browser.ToLowerInvariant();
        return identity.Equals("chrome", StringComparison.OrdinalIgnoreCase) ? value.Contains("chrome") && !value.Contains("brave") : identity.Equals("brave", StringComparison.OrdinalIgnoreCase) && value.Contains("brave");
    }

    public static string? FindExecutable(string executableName)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), executableName == "chrome.exe" ? "Google\\Chrome\\Application\\chrome.exe" : "BraveSoftware\\Brave-Browser\\Application\\brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), executableName == "chrome.exe" ? "Google\\Chrome\\Application\\chrome.exe" : "BraveSoftware\\Brave-Browser\\Application\\brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", executableName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "Application", executableName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private IEnumerable<ManagedBrowserDefinition> Definitions()
    {
        if (_options.ManageChrome) yield return new("chrome", "chrome.exe", _options.ChromePort);
        if (_options.ManageBrave) yield return new("brave", "brave.exe", _options.BravePort);
    }

    private async Task MaintainBrowsersAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_profileRoot);
        while (!token.IsCancellationRequested)
        {
            foreach (var definition in Definitions())
            {
                lock (_processLock)
                {
                    if (_processes.TryGetValue(definition.Identity, out var running) && !running.HasExited) continue;
                    running?.Dispose();
                    _processes.Remove(definition.Identity);
                }
                // The isolated profile can run beside the user's normal browser.
                // Never attach to a debugging endpoint CAMS did not start.
                if (IsEndpointAvailable(definition.Port)) continue;
                var executable = _findExecutable(definition.ExecutableName);
                if (executable == null) continue;
                var process = Process.Start(new ProcessStartInfo(executable, BuildArguments(definition, _profileRoot)) { UseShellExecute = false, CreateNoWindow = true });
                if (process != null) lock (_processLock) _processes[definition.Identity] = process;
            }
            try { await Task.Delay(_options.RestartDelayMilliseconds, token); } catch (OperationCanceledException) { }
        }
    }

    private bool IsEndpointAvailable(int port)
    {
        try { using var response = _httpClient.GetAsync($"http://127.0.0.1:{port}/json/version").GetAwaiter().GetResult(); return response.IsSuccessStatusCode; }
        catch { return false; }
    }

    private bool IsManagedProcessRunning(string identity)
    {
        lock (_processLock)
            return _processes.TryGetValue(identity, out var process) && !process.HasExited;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifecycleCts?.Cancel();
        Process[] processes;
        lock (_processLock)
        {
            processes = _processes.Values.ToArray();
            _processes.Clear();
        }
        foreach (var process in processes)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
        }
        _lifecycleCts?.Dispose();
        _httpClient.Dispose();
    }

    private sealed record DevToolsMetadata(string? Browser);
    private sealed record DevToolsTab(string? Type, string? Url, string? Title, string? WebSocketDebuggerUrl);
}
