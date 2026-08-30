using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Contracts;

namespace Client.Services;

public class MonitoringHubClient : IMonitoringHubClient
{
    private HubConnection? _connection;
    private readonly CookieContainer _cookies = new();
    private HttpClient? _httpClient;
    private string? _serverUrl;
    private readonly DurableTelemetryQueue _telemetryQueue;
    private readonly int _telemetryBatchSize;
    private readonly TimeSpan _policyRefreshInterval;
    private readonly SemaphoreSlim _telemetryFlushLock = new(1, 1);
    private readonly SemaphoreSlim _policyRefreshLock = new(1, 1);
    private CancellationTokenSource? _lifetimeCts;
    private Task? _policyRefreshTask;
    private bool _disposed;

    public MonitoringHubClient()
    {
        var options = ClientResilienceOptions.Load();
        _telemetryQueue = new DurableTelemetryQueue(
            maxRecords: options.TelemetryQueue.MaxRecords,
            maxBytes: options.TelemetryQueue.MaxBytes);
        _telemetryBatchSize = options.TelemetryQueue.BatchSize;
        _policyRefreshInterval = TimeSpan.FromSeconds(options.PolicyRefreshIntervalSeconds);
    }

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection is not null)
            throw new InvalidOperationException("The monitoring hub client has already been started.");

        var hubUri = new Uri(serverUrl, UriKind.Absolute);
        if (!string.Equals(hubUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CAMS requires an HTTPS server URL.");

        var connection = new HubConnectionBuilder()
            .WithUrl(serverUrl, options => options.Cookies = _cookies)
            .WithAutomaticReconnect()
            .Build();

        connection.Reconnected += _connectionId => HandleReconnectedAsync();

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
        connection.On(HubEventNames.PolicyRefreshRequired,
            () => RefreshRestrictionsSafelyAsync(_lifetimeCts?.Token ?? CancellationToken.None));

        _connection = connection;
        _serverUrl = serverUrl;
        _lifetimeCts = new CancellationTokenSource();
        try
        {
            await connection.StartAsync(cancellationToken);
            await RefreshRestrictionsSafelyAsync(_lifetimeCts.Token);
            await FlushQueuedTelemetrySafelyAsync(_lifetimeCts.Token);
            _policyRefreshTask = RunPolicyRefreshLoopAsync(_lifetimeCts.Token);
        }
        catch
        {
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            _lifetimeCts = null;
            _connection = null;
            _serverUrl = null;
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task SendScreenFrameAsync(ScreenFrameMessage frame)
    {
        EnsureConnected();
        await _connection!.InvokeAsync(HubMethodNames.SendScreenFrame, frame);
    }

    public async Task ReportIdleStatusAsync(IdleStatusMessage status)
    {
        await QueueTelemetryAsync(TelemetryBatchItem.From(status));
    }

    public async Task ReportActiveAppAsync(ActiveAppMessage app)
    {
        await QueueTelemetryAsync(TelemetryBatchItem.From(app));
    }

    public async Task ReportWebsiteActivityAsync(WebsiteActivityMessage website)
    {
        await QueueTelemetryAsync(TelemetryBatchItem.From(website));
    }

    public async Task ReportBrowserMonitoringStatusAsync(BrowserMonitoringStatusMessage status)
    {
        await QueueTelemetryAsync(TelemetryBatchItem.From(status));
    }

    public async Task FetchRestrictionsAsync()
    {
        await RefreshRestrictionsAsync(ignoreDisconnected: false, CancellationToken.None);
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
        if (_disposed)
            return;
        _disposed = true;

        _lifetimeCts?.Cancel();
        if (_policyRefreshTask is not null)
        {
            try
            {
                await _policyRefreshTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_connection is not null)
            await _connection.DisposeAsync();

        await _telemetryFlushLock.WaitAsync();
        _telemetryFlushLock.Release();
        await _policyRefreshLock.WaitAsync();
        _policyRefreshLock.Release();

        _httpClient?.Dispose();
        _httpClient = null;
        _connection = null;
        _serverUrl = null;
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _telemetryFlushLock.Dispose();
        _policyRefreshLock.Dispose();
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

    private async Task QueueTelemetryAsync(TelemetryBatchItem item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!DurableTelemetryQueue.TryNormalizeItem(item, out var normalized))
            throw new ArgumentException("The telemetry item is invalid or is not privacy-safe.", nameof(item));
        try
        {
            await _telemetryQueue.EnqueueAsync(normalized);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await TrySendDirectAsync(normalized);
            return;
        }

        var cancellationToken = _lifetimeCts?.Token ?? CancellationToken.None;
        await FlushQueuedTelemetryAsync(cancellationToken);
    }

    private async Task FlushQueuedTelemetryAsync(CancellationToken cancellationToken)
    {
        await _telemetryFlushLock.WaitAsync(cancellationToken);

        try
        {
            while (_connection?.State == HubConnectionState.Connected && !cancellationToken.IsCancellationRequested)
            {
                var queued = await _telemetryQueue.ReadBatchAsync(_telemetryBatchSize, cancellationToken);
                if (queued.Count == 0)
                    return;

                try
                {
                    var result = await _connection.InvokeCoreAsync<TelemetryBatchResult>(
                        HubMethodNames.ReportTelemetryBatch,
                        new object?[] { new TelemetryBatchMessage(queued.Select(record => record.Item).ToList()) },
                        cancellationToken);
                    var processed = Math.Clamp(result.ProcessedCount, 0, queued.Count);
                    if (processed == 0)
                        return;

                    await _telemetryQueue.AcknowledgeAsync(
                        queued.Take(processed).Select(record => record.Id),
                        cancellationToken);
                    if (processed < queued.Count)
                        return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
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

    private async Task TrySendDirectAsync(TelemetryBatchItem item)
    {
        var connection = _connection;
        if (connection?.State != HubConnectionState.Connected)
            return;

        try
        {
            await connection.InvokeCoreAsync<TelemetryBatchResult>(
                HubMethodNames.ReportTelemetryBatch,
                new object?[] { new TelemetryBatchMessage(new[] { item }) },
                _lifetimeCts?.Token ?? CancellationToken.None);
        }
        catch
        {
            // A disk failure leaves no durable fallback, so live delivery remains best effort.
        }
    }

    private async Task HandleReconnectedAsync()
    {
        var cancellationToken = _lifetimeCts?.Token ?? CancellationToken.None;
        await RefreshRestrictionsSafelyAsync(cancellationToken);
        await FlushQueuedTelemetrySafelyAsync(cancellationToken);
    }

    private async Task RunPolicyRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_policyRefreshInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await RefreshRestrictionsSafelyAsync(cancellationToken);
    }

    private async Task FlushQueuedTelemetrySafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FlushQueuedTelemetryAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // A later report or reconnect will retry the durable queue.
        }
    }

    private async Task RefreshRestrictionsSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshRestrictionsAsync(ignoreDisconnected: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // The periodic/reconnect refresh will retry without interrupting monitoring.
        }
    }

    private async Task RefreshRestrictionsAsync(bool ignoreDisconnected, CancellationToken cancellationToken)
    {
        var connection = _connection;
        if (connection?.State != HubConnectionState.Connected)
        {
            if (ignoreDisconnected)
                return;
            EnsureConnected();
        }

        await _policyRefreshLock.WaitAsync(cancellationToken);
        try
        {
            connection = _connection;
            if (connection?.State != HubConnectionState.Connected)
            {
                if (ignoreDisconnected)
                    return;
                EnsureConnected();
            }

            await connection!.InvokeCoreAsync(
                HubMethodNames.FetchRestrictions,
                Array.Empty<object?>(),
                cancellationToken);
        }
        finally
        {
            _policyRefreshLock.Release();
        }
    }
}
