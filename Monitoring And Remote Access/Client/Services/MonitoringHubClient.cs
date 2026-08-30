using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Contracts;

namespace Client.Services;

public class MonitoringHubClient : IMonitoringHubClient
{
    private const int MaxQueuedTelemetry = 100;
    private HubConnection? _connection;
    private readonly CookieContainer _cookies = new();
    private HttpClient? _httpClient;
    private string? _serverUrl;
    private readonly object _telemetryQueueLock = new();
    private readonly LinkedList<Func<Task>> _queuedTelemetry = new();
    private readonly SemaphoreSlim _telemetryFlushLock = new(1, 1);

    public event Action<RemoteInputMessage>? RemoteInputReceived;
    public event Action<RemoteControlStateMessage>? RemoteControlStateReceived;
    public event Action? Locked;
    public event Action? Unlocked;
    public event Action? ForceLogoutRequested;
    public event Action<BroadcastMessage>? BroadcastReceived;
    public event Action<NotificationMessage>? NotificationReceived;
    public event Action<GlobalSessionMessage>? GlobalSessionStateReceived;
    public event Action? SessionEnded;
    public event Action? ShutdownRequested;
    public event Action? RestartRequested;
    public event Action<NotificationMessage>? WarningPopupReceived;
    public event Action<List<RestrictionRuleMessage>>? RestrictionsReceived;

    public async Task<StudentClientLoginResponse> LoginAsync(
        string serverUrl,
        string username,
        string password,
        string pcName,
        CancellationToken cancellationToken = default)
    {
        var rootUri = GetRootUri(serverUrl);
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = _cookies
        };

        _httpClient?.Dispose();
        _httpClient = new HttpClient(handler);

        using var response = await _httpClient.PostAsJsonAsync(
            new Uri(rootUri, "api/client/login"),
            new StudentClientLoginRequest(username, password, pcName),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(detail))
                detail = $"Student login failed with status {(int)response.StatusCode}.";

            throw new HttpRequestException(detail.Trim(), null, response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<StudentClientLoginResponse>(cancellationToken: cancellationToken);
        return result ?? throw new HttpRequestException("The server returned an empty login response.");
    }

    public async Task StartAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        var hubUri = new Uri(serverUrl, UriKind.Absolute);
        if (!string.Equals(hubUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CAMS requires an HTTPS server URL.");

        var connection = new HubConnectionBuilder()
            .WithUrl(serverUrl, options => options.Cookies = _cookies)
            .WithAutomaticReconnect()
            .Build();

        connection.Reconnected += _connectionId =>
        {
            _ = FlushQueuedTelemetryAsync();
            return Task.CompletedTask;
        };

        connection.On<RemoteInputMessage>(HubEventNames.ExecuteRemoteInput,
            message => RemoteInputReceived?.Invoke(message));
        connection.On<RemoteControlStateMessage>(HubEventNames.RemoteControlState,
            message => RemoteControlStateReceived?.Invoke(message));
        connection.On(HubEventNames.LockStudent, () => Locked?.Invoke());
        connection.On(HubEventNames.UnlockStudent, () => Unlocked?.Invoke());
        connection.On(HubEventNames.ForceLogout, () => ForceLogoutRequested?.Invoke());
        connection.On<BroadcastMessage>(HubEventNames.BroadcastScreen,
            message => BroadcastReceived?.Invoke(message));
        connection.On<NotificationMessage>(HubEventNames.SendNotification,
            message => NotificationReceived?.Invoke(message));
        connection.On<GlobalSessionMessage>(HubEventNames.GlobalSessionState,
            message => GlobalSessionStateReceived?.Invoke(message));
        connection.On(HubEventNames.SessionEnded, () => SessionEnded?.Invoke());
        connection.On(HubEventNames.ShutdownStudent, () => ShutdownRequested?.Invoke());
        connection.On(HubEventNames.RestartStudent, () => RestartRequested?.Invoke());
        connection.On<NotificationMessage>(HubEventNames.SendWarningPopup,
            message => WarningPopupReceived?.Invoke(message));
        connection.On<List<RestrictionRuleMessage>>(HubEventNames.RestrictionsReceived,
            rules => RestrictionsReceived?.Invoke(rules));

        await connection.StartAsync(cancellationToken);
        _connection = connection;
        _serverUrl = serverUrl;
        await FlushQueuedTelemetryAsync();
    }

    public async Task SendScreenFrameAsync(ScreenFrameMessage frame)
    {
        EnsureConnected();
        await _connection!.InvokeAsync(HubMethodNames.SendScreenFrame, frame);
    }

    public async Task ReportIdleStatusAsync(IdleStatusMessage status)
    {
        await SendTelemetryAsync(() => _connection!.InvokeAsync(HubMethodNames.ReportIdleStatus, status));
    }

    public async Task ReportActiveAppAsync(ActiveAppMessage app)
    {
        await SendTelemetryAsync(() => _connection!.InvokeAsync(HubMethodNames.ReportActiveApp, app));
    }

    public async Task ReportWebsiteActivityAsync(WebsiteActivityMessage website)
    {
        await SendTelemetryAsync(() => _connection!.InvokeAsync(HubMethodNames.ReportWebsiteActivity, website));
    }

    public async Task ReportBrowserMonitoringStatusAsync(BrowserMonitoringStatusMessage status)
    {
        await SendTelemetryAsync(() => _connection!.InvokeAsync(HubMethodNames.ReportBrowserMonitoringStatus, status));
    }

    public async Task FetchRestrictionsAsync()
    {
        EnsureConnected();
        await _connection!.InvokeAsync(HubMethodNames.FetchRestrictions);
    }

    public async Task ReportInfractionAsync(InfractionMessage infraction)
    {
        EnsureConnected();
        await _connection!.InvokeAsync(HubMethodNames.ReportInfraction, infraction);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (_httpClient is null) return;

        using var response = await _httpClient.PostAsync(
            new Uri(GetRootUri(_serverUrl ?? "https://localhost:5000/"), "api/client/logout"),
            new StringContent(string.Empty),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _httpClient?.Dispose();
        _httpClient = null;
        _connection = null;
        _serverUrl = null;
        _telemetryFlushLock.Dispose();
    }

    private static Uri GetRootUri(string serverUrl)
    {
        var uri = new Uri(serverUrl, UriKind.Absolute);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CAMS requires an HTTPS server URL.");

        return new UriBuilder(uri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private void EnsureConnected()
    {
        if (_connection?.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Not connected to the monitoring hub.");
        }
    }

    private async Task SendTelemetryAsync(Func<Task> send)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                EnsureConnected();
                await send();
                return;
            }
            catch when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
            }
            catch
            {
                break;
            }
        }

        lock (_telemetryQueueLock)
        {
            if (_queuedTelemetry.Count == MaxQueuedTelemetry)
                _queuedTelemetry.RemoveFirst();
            _queuedTelemetry.AddLast(send);
        }
    }

    private async Task FlushQueuedTelemetryAsync()
    {
        if (!await _telemetryFlushLock.WaitAsync(0))
            return;

        try
        {
            while (true)
            {
                Func<Task>? send;
                lock (_telemetryQueueLock)
                    send = _queuedTelemetry.First?.Value;
                if (send is null)
                    return;

                try
                {
                    EnsureConnected();
                    await send();
                    lock (_telemetryQueueLock)
                        _queuedTelemetry.RemoveFirst();
                }
                catch
                {
                    return;
                }
            }
        }
        finally
        {
            _telemetryFlushLock.Release();
        }
    }
}
