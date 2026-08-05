using Shared.Contracts;

namespace Client.Services;

public interface IMonitoringHubClient : IAsyncDisposable
{
    event Action<RemoteInputMessage>? RemoteInputReceived;

    Task StartAsync(string serverUrl, CancellationToken cancellationToken = default);
    Task RegisterStudentAsync(string studentId, string pcName);
    Task SendScreenFrameAsync(ScreenFrameMessage frame);
}
