using System.Collections.Concurrent;
using Shared.Contracts;

namespace Server.Services
{
    public class MonitoringService : IMonitoringService
    {
        private readonly ConcurrentDictionary<string, StudentConnectionMessage> _students = new();
        private readonly ConcurrentDictionary<string, IdleStatusMessage> _idleStatus = new();
        private readonly ConcurrentDictionary<string, ActiveAppMessage> _activeApps = new();

        public IReadOnlyCollection<StudentConnectionMessage> ActiveStudents => _students.Values.ToList();
        public IReadOnlyCollection<IdleStatusMessage> IdleStatus => _idleStatus.Values.ToList();
        public IReadOnlyCollection<ActiveAppMessage> ActiveApps => _activeApps.Values.ToList();

        public StudentConnectionMessage RegisterStudent(string connectionId, string studentId, string pcName)
        {
            var message = new StudentConnectionMessage(connectionId, studentId, pcName, DateTime.Now);
            _students[connectionId] = message;
            return message;
        }

        public StudentConnectionMessage? FindStudent(string connectionId)
        {
            return _students.TryGetValue(connectionId, out var student) ? student : null;
        }

        public StudentConnectionMessage? UnregisterStudent(string connectionId)
        {
            _idleStatus.TryRemove(connectionId, out _);
            _activeApps.TryRemove(connectionId, out _);
            return _students.TryRemove(connectionId, out var message) ? message : null;
        }

        public void ReportIdleStatus(IdleStatusMessage status)
        {
            _idleStatus[status.ConnectionId] = status;
        }

        public void ReportActiveApp(ActiveAppMessage app)
        {
            _activeApps[app.ConnectionId] = app;
        }
    }
}
