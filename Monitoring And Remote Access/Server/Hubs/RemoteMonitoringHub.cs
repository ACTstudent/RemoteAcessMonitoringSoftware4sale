using Microsoft.AspNetCore.SignalR;
using Server.Services;
using Shared.Contracts;

namespace Server.Hubs
{
    public class RemoteMonitoringHub : Hub
    {
        private readonly IMonitoringService _monitoringService;

        public RemoteMonitoringHub(IMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
        }

        // Student Client sends a live screen frame
        public async Task SendScreenFrame(ScreenFrameMessage frame)
        {
            // Broadcast frame to connected teacher/admin dashboard
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.ReceiveScreenFrame, Context.ConnectionId, frame);
        }

        // Student Client registers upon login
        public async Task RegisterStudent(string studentId, string pcName)
        {
            var student = _monitoringService.RegisterStudent(Context.ConnectionId, studentId, pcName);
            await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.StudentsGroup);
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.StudentConnected, student);
        }

        // Teacher dashboard joins monitoring group
        public async Task RegisterTeacher()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.TeachersGroup);
        }

        // Teacher dashboard transmits mouse/keyboard event to a specific student connection
        public async Task SendRemoteInput(string targetConnectionId, RemoteInputMessage input)
        {
            await Clients.Client(targetConnectionId)
                .SendAsync(HubEventNames.ExecuteRemoteInput, input);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var student = _monitoringService.UnregisterStudent(Context.ConnectionId);
            if (student != null)
            {
                await Clients.Group(HubEventNames.TeachersGroup)
                    .SendAsync(HubEventNames.StudentDisconnected, student.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
