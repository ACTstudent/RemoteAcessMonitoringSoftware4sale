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
        private int? _labTeacherId;

        // Where the lab's state is kept between server runs. Students can only sign
        // in while a lab is open, so losing it on a restart would lock the whole
        // room out mid-class until the teacher started again - which also resets
        // everyone's timer. Null in tests, which keep it in memory only.
        private readonly string? _statePath;

        private sealed record SavedLab(string Status, DateTime StartedAtUtc, double AccumulatedSeconds, int? RuleId, int? TeacherId = null);

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
                _labTeacherId = saved.TeacherId;
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
                    new SavedLab(_status.ToString(), _startedAt, _accumulatedSeconds, _labRuleId, _labTeacherId));
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
        /// means the active default rule. Saved with the rest of the lab state.
        /// </summary>
        public int? LabRuleId
        {
            get { lock (_lock) return _labRuleId; }
        }

        /// <summary>
        /// The teacher running the lab: whoever started it for everyone. Their
        /// Access Restrictions rules apply to every student in the lab, not only
        /// to their own classes - a newcomer with no class or adviser had a
        /// session tied to no teacher and got none of them.
        /// </summary>
        public int? LabTeacherId
        {
            get { lock (_lock) return IsLabOpen ? _labTeacherId : null; }
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
        public void StartLab(int? sessionRuleId, int? teacherId = null)
        {
            lock (_lock)
            {
                _status = GlobalSessionStatus.Running;
                _startedAt = DateTime.UtcNow;
                _accumulatedSeconds = 0;
                _labRuleId = sessionRuleId;
                _labTeacherId = teacherId;
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
                _labTeacherId = null;
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
