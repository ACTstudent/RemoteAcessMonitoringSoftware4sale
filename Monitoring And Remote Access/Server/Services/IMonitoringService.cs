using Shared.Contracts;

namespace Server.Services;

public interface IMonitoringService
{
    IReadOnlyCollection<StudentConnectionMessage> ActiveStudents { get; }
    IReadOnlyCollection<IdleStatusMessage> IdleStatus { get; }
    IReadOnlyCollection<ActiveAppMessage> ActiveApps { get; }

    StudentConnectionMessage RegisterStudent(string connectionId, string studentId, string pcName);
    StudentConnectionMessage? UnregisterStudent(string connectionId);
    void ReportIdleStatus(IdleStatusMessage status);
    void ReportActiveApp(ActiveAppMessage app);
}