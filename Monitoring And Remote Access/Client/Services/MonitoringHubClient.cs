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

    public event Action<RemoteInputMessage>? RemoteInputReceived;
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

        connection.On<RemoteInputMessage>(HubEventNames.ExecuteRemoteInput,
            message => RemoteInputReceived?.Invoke(message));
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
    }

    public async Task SendScreenFrameAsync(ScreenFrameMessage frame)
    {
        EnsureConnected();
        await _connection!.InvokeAsync(HubMethodNames.SendScreenFrame, frame);
    }

    public async Task ReportIdleStatusAsync(IdleStatusMessage status)
    {
        EnsureConnected();
        await _connection!.InvokeAsync(HubMethodNames.ReportIdleStatus, status);
    }

    public async Task ReportActiveAppAsync(ActiveAppMessage app)
    {
        EnsureConnected();
        await _connection!.InvokeAsync(HubMethodNames.ReportActiveApp, app);
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
}
