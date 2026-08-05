using System.Collections.Concurrent;
using Shared.Contracts;

namespace Server.Services;

public class MonitoringService : IMonitoringService
{
    private readonly ConcurrentDictionary<string, StudentConnectionMessage> _students = new();

    public IReadOnlyCollection<StudentConnectionMessage> ActiveStudents => _students.Values.ToList();

    public StudentConnectionMessage RegisterStudent(string connectionId, string studentId, string pcName)
    {
        var message = new StudentConnectionMessage(connectionId, studentId, pcName, DateTime.Now);
        _students[connectionId] = message;
        return message;
    }

    public StudentConnectionMessage? UnregisterStudent(string connectionId)
    {
        return _students.TryRemove(connectionId, out var message) ? message : null;
    }
}
