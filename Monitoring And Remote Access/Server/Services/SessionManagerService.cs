using Microsoft.AspNetCore.SignalR;
using Server.Hubs;
using Shared.Contracts;

namespace Server.Services
{
    public enum GlobalSessionStatus
    {
        None,
        Running,
        Paused,
        Ended
    }

    public class SessionManagerService
    {
        private readonly IHubContext<RemoteMonitoringHub> _hub;
        private readonly object _lock = new();

        private GlobalSessionStatus _status = GlobalSessionStatus.None;
        private DateTime _startedAt;
        private double _accumulatedSeconds;
        private int? _labRuleId;

        // Where the lab's state is kept between server runs. Students can only sign
        // in while a lab is open, so losing it on a restart would lock the whole
        // room out mid-class until the teacher started again - which also resets
        // everyone's timer. Null in tests, which keep it in memory only.
        private readonly string? _statePath;

        private sealed record SavedLab(string Status, DateTime StartedAtUtc, double AccumulatedSeconds, int? RuleId);

        public SessionManagerService(IHubContext<RemoteMonitoringHub> hub, string? statePath = null)
        {
            _hub = hub;
            _statePath = statePath;
            Load();
        }

        private void Load()
        {
            if (_statePath is null || !File.Exists(_statePath)) return;
            try
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<SavedLab>(File.ReadAllText(_statePath));
                if (saved is null || !Enum.TryParse<GlobalSessionStatus>(saved.Status, out var status)) return;
                _status = status;
                _startedAt = DateTime.SpecifyKind(saved.StartedAtUtc, DateTimeKind.Utc);
                _accumulatedSeconds = saved.AccumulatedSeconds;
                _labRuleId = saved.RuleId;
            }
            catch (Exception)
            {
                // A damaged file means no lab is open; the teacher starts one again.
            }
        }

        /// <summary>Called with the lock held, after every change of state.</summary>
        private void Save()
        {
            if (_statePath is null) return;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    new SavedLab(_status.ToString(), _startedAt, _accumulatedSeconds, _labRuleId));
                var temporary = _statePath + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, _statePath, overwrite: true);
            }
            catch (Exception)
            {
                // Persistence is a convenience across restarts; the lab keeps
                // running in memory if the file cannot be written.
            }
        }

        public int ElapsedSeconds
        {
            get
            {
                lock (_lock)
                {
                    if (_status != GlobalSessionStatus.Running)
                        return (int)_accumulatedSeconds;
                    return (int)(_accumulatedSeconds + (DateTime.UtcNow - _startedAt).TotalSeconds);
                }
            }
        }

        /// <summary>
        /// The session rule a teacher chose when starting the lab for everyone.
        /// Students who sign in afterwards, on any computer, join under it; null
        /// means the active default rule. Held in memory only, so a server restart
        /// falls back to the default rule until the lab is started again.
        /// </summary>
        public int? LabRuleId
        {
            get { lock (_lock) return _labRuleId; }
        }

        /// <summary>True while a lab session is running or paused.</summary>
        public bool IsLabOpen
        {
            get { lock (_lock) return _status is GlobalSessionStatus.Running or GlobalSessionStatus.Paused; }
        }

        /// <summary>
        /// Starts the lab for everyone from zero under <paramref name="sessionRuleId"/>,
        /// whatever state it was in. This is the lab itself, which exists before any
        /// student has signed in; each student's own session is a separate record.
        /// </summary>
        public void StartLab(int? sessionRuleId)
        {
            lock (_lock)
            {
                _status = GlobalSessionStatus.Running;
                _startedAt = DateTime.UtcNow;
                _accumulatedSeconds = 0;
                _labRuleId = sessionRuleId;
                Save();
            }
            BroadcastToTeachers();
        }

        /// <summary>Resumes a paused lab. Does not open a lab that was never started.</summary>
        public void ResumeLab()
        {
            lock (_lock)
            {
                if (_status != GlobalSessionStatus.Paused) return;
            }
            StartSession();
        }

        public GlobalSessionMessage Snapshot()
        {
            lock (_lock)
            {
                return new GlobalSessionMessage(
                    _status.ToString(),
                    ElapsedSeconds,
                    _status == GlobalSessionStatus.None ? null : _startedAt);
            }
        }

        public void StartSession()
        {
            lock (_lock)
            {
                if (_status == GlobalSessionStatus.Running) return;
                if (_status == GlobalSessionStatus.Paused)
                {
                    _status = GlobalSessionStatus.Running;
                    _startedAt = DateTime.UtcNow;
                }
                else
                {
                    _status = GlobalSessionStatus.Running;
                    _startedAt = DateTime.UtcNow;
                    _accumulatedSeconds = 0;
                }
                Save();
            }

            BroadcastToTeachers();
        }

        public void PauseSession()
        {
            lock (_lock)
            {
                if (_status != GlobalSessionStatus.Running) return;
                _accumulatedSeconds += (DateTime.UtcNow - _startedAt).TotalSeconds;
                _status = GlobalSessionStatus.Paused;
                Save();
            }

            BroadcastToTeachers();
        }

        public void EndSession()
        {
            lock (_lock)
            {
                if (_status == GlobalSessionStatus.None || _status == GlobalSessionStatus.Ended) return;
                if (_status == GlobalSessionStatus.Running)
                {
                    _accumulatedSeconds += (DateTime.UtcNow - _startedAt).TotalSeconds;
                }
                _status = GlobalSessionStatus.Ended;
                _labRuleId = null;   // the next sign-in goes back to the default rule
                Save();
            }

            BroadcastToTeachers();
        }

        /// <summary>
        /// The lab's state goes to teacher and admin screens only. Student agents
        /// listen for the same event to drive their own session timer and pause
        /// screen, so sending them the lab-wide state would overwrite each
        /// student's own session. Ending the lab no longer sends SessionEnded to
        /// the students group either: that closed every connected client, even
        /// ones with no session. The students whose sessions end are told by
        /// <see cref="LabSessionLifecycleService.EndAllSessionsAsync"/>.
        /// </summary>
        private void BroadcastToTeachers() =>
            _ = _hub.Clients.Group(HubEventNames.TeachersGroup).SendAsync(HubEventNames.GlobalSessionState, Snapshot());
    }
}
