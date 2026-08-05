using Microsoft.AspNetCore.SignalR.Client;
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

    Task StartAsync(string serverUrl, CancellationToken cancellationToken = default);
    Task RegisterStudentAsync(string studentId, string pcName);
    Task SendScreenFrameAsync(ScreenFrameMessage frame);
    Task ReportIdleStatusAsync(IdleStatusMessage status);
    Task ReportActiveAppAsync(ActiveAppMessage app);
    ValueTask DisposeAsync();
}