using Microsoft.AspNetCore.SignalR.Client;
using Shared.Contracts;

namespace Client.Services;

public class MonitoringHubClient : IMonitoringHubClient
{
    private HubConnection? _connection;

    public event Action<RemoteInputMessage>? RemoteInputReceived;
    public event Action? Locked;
    public event Action? Unlocked;
    public event Action? ForceLogoutRequested;
    public event Action<BroadcastMessage>? BroadcastReceived;
    public event Action<NotificationMessage>? NotificationReceived;

    public async Task StartAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(serverUrl)
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

        await connection.StartAsync(cancellationToken);
        _connection = connection;
    }

    public async Task RegisterStudentAsync(string studentId, string pcName)
    {
        EnsureConnected();
        await _connection!.InvokeAsync("RegisterStudent", studentId, pcName);
    }

    public async Task SendScreenFrameAsync(ScreenFrameMessage frame)
    {
        EnsureConnected();
        await _connection!.InvokeAsync("SendScreenFrame", frame);
    }

    public async Task ReportIdleStatusAsync(IdleStatusMessage status)
    {
        EnsureConnected();
        await _connection!.InvokeAsync("ReportIdleStatus", status);
    }

    public async Task ReportActiveAppAsync(ActiveAppMessage app)
    {
        EnsureConnected();
        await _connection!.InvokeAsync("ReportActiveApp", app);
    }

    public ValueTask DisposeAsync()
    {
        return _connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (_connection?.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Not connected to the monitoring hub.");
        }
    }
}