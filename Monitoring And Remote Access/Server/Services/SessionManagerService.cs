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

        public SessionManagerService(IHubContext<RemoteMonitoringHub> hub)
        {
            _hub = hub;
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
            }

            _ = _hub.Clients.All.SendAsync(HubEventNames.GlobalSessionState, Snapshot());
        }

        public void PauseSession()
        {
            lock (_lock)
            {
                if (_status != GlobalSessionStatus.Running) return;
                _accumulatedSeconds += (DateTime.UtcNow - _startedAt).TotalSeconds;
                _status = GlobalSessionStatus.Paused;
            }

            _ = _hub.Clients.All.SendAsync(HubEventNames.GlobalSessionState, Snapshot());
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
            }

            _ = _hub.Clients.All.SendAsync(HubEventNames.GlobalSessionState, Snapshot());
            _ = _hub.Clients.Group(HubEventNames.StudentsGroup).SendAsync(HubEventNames.SessionEnded);
        }
    }
}
