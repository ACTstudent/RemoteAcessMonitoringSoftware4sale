using Microsoft.AspNetCore.SignalR;

namespace Server.Hubs
{
    public class RemoteMonitoringHub : Hub
    {
        // Student Client sends a live screen frame
        public async Task SendScreenFrame(string studentId, string pcName, string frameBase64)
        {
            // Broadcast frame to connected teacher/admin dashboard
            await Clients.Group("Teachers").SendAsync("ReceiveScreenFrame", Context.ConnectionId, studentId, pcName, frameBase64);
        }

        // Student Client registers upon login
        public async Task RegisterStudent(string studentId, string pcName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Students");
            await Clients.Group("Teachers").SendAsync("StudentConnected", Context.ConnectionId, studentId, pcName);
        }

        // Teacher dashboard joins monitoring group
        public async Task RegisterTeacher()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Teachers");
        }

        // Teacher dashboard transmits mouse/keyboard event to a specific student connection
        public async Task SendRemoteInput(string targetConnectionId, string eventType, int x, int y, int keyCode, bool isShift)
        {
            await Clients.Client(targetConnectionId).SendAsync("ExecuteRemoteInput", eventType, x, y, keyCode, isShift);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Clients.Group("Teachers").SendAsync("StudentDisconnected", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
