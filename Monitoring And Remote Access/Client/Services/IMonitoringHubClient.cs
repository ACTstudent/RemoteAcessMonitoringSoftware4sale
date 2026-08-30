using Shared.Contracts;

namespace Client.Services;

public interface IMonitoringHubClient
{
    event Action<RemoteInputMessage>? RemoteInputReceived;
    event Action<RemoteControlStateMessage>? RemoteControlStateReceived;
    event Action? Locked;
    event Action? Unlocked;
    event Action? ForceLogoutRequested;
    event Action<BroadcastMessage>? BroadcastReceived;
    event Action<NotificationMessage>? NotificationReceived;
    event Action<GlobalSessionMessage>? GlobalSessionStateReceived;
    event Action? SessionEnded;
    event Action? ShutdownRequested;
    event Action? RestartRequested;
    event Action<NotificationMessage>? WarningPopupReceived;
    event Action<List<RestrictionRuleMessage>>? RestrictionsReceived;

    Task<StudentClientLoginResponse> LoginAsync(string serverUrl, string username, string password, string pcName, CancellationToken cancellationToken = default);
    Task StartAsync(string serverUrl, CancellationToken cancellationToken = default);
    Task SendScreenFrameAsync(ScreenFrameMessage frame);
    Task ReportIdleStatusAsync(IdleStatusMessage status);
    Task ReportActiveAppAsync(ActiveAppMessage app);
    Task ReportWebsiteActivityAsync(WebsiteActivityMessage website);
    Task ReportBrowserMonitoringStatusAsync(BrowserMonitoringStatusMessage status);
    Task FetchRestrictionsAsync();
    Task ReportInfractionAsync(InfractionMessage infraction);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    ValueTask DisposeAsync();
}
