using Shared.Contracts;

namespace Client.Services;

public interface IMonitoringHubClient
{
    event Action<RemoteInputMessage>? RemoteInputReceived;
    event Action? Locked;
    event Action? Unlocked;
    event Action? ForceLogoutRequested;
    event Action<BroadcastMessage>? BroadcastReceived;
    event Action<NotificationMessage>? NotificationReceived;
    event Action<GlobalSessionMessage>? GlobalSessionStateReceived;
    event Action? SessionEnded;
    event Action? ShutdownRequested;
    event Action<NotificationMessage>? WarningPopupReceived;
    event Action<List<RestrictionRuleMessage>>? RestrictionsReceived;

    Task StartAsync(string serverUrl, CancellationToken cancellationToken = default);
    Task RegisterStudentAsync(string studentId, string pcName);
    Task SendScreenFrameAsync(ScreenFrameMessage frame);
    Task ReportIdleStatusAsync(IdleStatusMessage status);
    Task ReportActiveAppAsync(ActiveAppMessage app);
    Task FetchRestrictionsAsync();
    Task ReportInfractionAsync(InfractionMessage infraction);
    ValueTask DisposeAsync();
}
