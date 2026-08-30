using Shared.Contracts;

namespace Server.Services;

public interface IMonitoringService
{
    IReadOnlyCollection<StudentConnectionMessage> ActiveStudents { get; }
    IReadOnlyCollection<IdleStatusMessage> IdleStatus { get; }
    IReadOnlyCollection<ActiveAppMessage> ActiveApps { get; }
    IReadOnlyCollection<BrowserMonitoringStatusMessage> BrowserMonitoringStatus { get; }

    StudentConnectionMessage RegisterStudent(string connectionId, string studentId, string pcName);
    StudentConnectionMessage? FindStudent(string connectionId);
    StudentConnectionMessage? UnregisterStudent(string connectionId);
    void ReportIdleStatus(IdleStatusMessage status);
    void ReportActiveApp(ActiveAppMessage app);
    void ReportBrowserMonitoringStatus(BrowserMonitoringStatusMessage status);
}
