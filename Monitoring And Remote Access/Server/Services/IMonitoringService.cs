using Shared.Contracts;

namespace Server.Services;

public interface IMonitoringService
{
    IReadOnlyCollection<StudentConnectionMessage> ActiveStudents { get; }

    StudentConnectionMessage RegisterStudent(string connectionId, string studentId, string pcName);
    StudentConnectionMessage? UnregisterStudent(string connectionId);
}
