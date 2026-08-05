using System.Diagnostics;
using System.Runtime.InteropServices;
using Client.Services;
using Shared.Contracts;

namespace Client
{
    public partial class MainForm : Form
    {
        private const string ServerUrl = "http://localhost:5000/remoteMonitoringHub";

        private readonly IScreenCaptureService _screenCaptureService = new ScreenCaptureService();

        private IMonitoringHubClient? _hubClient;
        private bool _isStreaming = false;
        private CancellationTokenSource? _streamCts;
        private bool _isLocked = false;

        private TextBox txtStudentId = new();
        private TextBox txtPassword = new();
        private Button btnLogin = new();
        private Label lblStatus = new();

        public MainForm()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Student Client - Lab Monitor";
            Size = new Size(420, 360);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            var lblTitle = new Label
            {
                Text = "Student Login",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(150, 20),
                AutoSize = true
            };

            var lblId = new Label { Text = "Student ID:", Location = new Point(30, 70), AutoSize = true };
            txtStudentId.Location = new Point(130, 66);
            txtStudentId.Size = new Size(230, 24);

            var lblPass = new Label { Text = "Password:", Location = new Point(30, 105), AutoSize = true };
            txtPassword.Location = new Point(130, 101);
            txtPassword.Size = new Size(230, 24);
            txtPassword.UseSystemPasswordChar = true;

            btnLogin = new Button
            {
                Text = "Log In",
                Location = new Point(130, 145),
                Size = new Size(230, 35),
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnLogin.Click += BtnLogin_Click;

            lblStatus = new Label
            {
                Text = "Status: Not connected",
                Location = new Point(30, 200),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            Controls.AddRange(new Control[] { lblTitle, lblId, txtStudentId, lblPass, txtPassword, btnLogin, lblStatus });
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            string studentId = txtStudentId.Text.Trim();
            string password = txtPassword.Text;

            if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter your Student ID and Password.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnLogin.Enabled = false;
            lblStatus.Text = "Status: Connecting...";

            try
            {
                var hubClient = new MonitoringHubClient();
                hubClient.RemoteInputReceived += InputSimulator.ProcessRemoteInput;
                hubClient.Locked += () => this.Invoke(() => SetLocked(true));
                hubClient.Unlocked += () => this.Invoke(() => SetLocked(false));
                hubClient.ForceLogoutRequested += () => this.Invoke(async () => await ForceLogout());
                hubClient.BroadcastReceived += msg => this.Invoke(() => ShowBroadcast(msg));
                hubClient.NotificationReceived += msg => this.Invoke(() => ShowNotification(msg));

                await hubClient.StartAsync(ServerUrl);
                await hubClient.RegisterStudentAsync(studentId, Environment.MachineName);

                _hubClient = hubClient;

                lblStatus.Text = "Status: Connected & Streaming";
                lblStatus.ForeColor = Color.Green;

                _isStreaming = true;
                _streamCts = new CancellationTokenSource();
                _ = Task.Run(() => ScreenCaptureLoop(studentId, _streamCts.Token));
                _ = Task.Run(() => StatusReportLoop(_streamCts.Token));
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Status: Connection failed";
                lblStatus.ForeColor = Color.Red;
                btnLogin.Enabled = true;
                MessageBox.Show($"Connection error:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool _lastIdleReported = false;
        private DateTime _lastActiveAppReport = DateTime.MinValue;

        // Reports idle/active status and the active foreground app periodically.
        private async Task StatusReportLoop(CancellationToken token)
        {
            while (_isStreaming && !token.IsCancellationRequested)
            {
                try
                {
                    if (_hubClient != null)
                    {
                        var idleSeconds = (uint)NativeMethods.GetIdleTime() / 1000;
                        bool isIdle = idleSeconds >= 60; // 60s inactivity threshold

                        if (isIdle != _lastIdleReported)
                        {
                            _lastIdleReported = isIdle;
                            await _hubClient.ReportIdleStatusAsync(new IdleStatusMessage(
                                connectionId: "",
                                studentId: "",
                                pcName: Environment.MachineName,
                                IsIdle: isIdle,
                                Timestamp: DateTime.Now));
                        }

                        if (DateTime.Now - _lastActiveAppReport > TimeSpan.FromSeconds(5))
                        {
                            _lastActiveAppReport = DateTime.Now;
                            var appName = ActiveAppInfo.Get();
                            if (!string.IsNullOrEmpty(appName))
                            {
                                await _hubClient.ReportActiveAppAsync(new ActiveAppMessage(
                                    connectionId: "",
                                    studentId: "",
                                    pcName: Environment.MachineName,
                                    ApplicationName: appName,
                                    Timestamp: DateTime.Now));
                            }
                        }
                    }
                }
                catch
                {
                    // ignore telemetry errors
                }

                await Task.Delay(5000, token);
            }
        }

        private async Task ScreenCaptureLoop(string studentId, CancellationToken token)
        {
            while (_isStreaming && !token.IsCancellationRequested)
            {
                try
                {
                    if (_hubClient != null && !_isLocked)
                    {
                        var frame = new ScreenFrameMessage(
                            studentId,
                            Environment.MachineName,
                            _screenCaptureService.CaptureBase64(),
                            DateTime.Now);

                        await _hubClient.SendScreenFrameAsync(frame);
                    }
                }
                catch
                {
                    // Frame dropped; keep the loop alive
                }

                await Task.Delay(80, token); // ~12 FPS
            }
        }

        private void SetLocked(bool locked)
        {
            _isLocked = locked;
            lblStatus.Text = locked ? "Status: Locked by teacher" : "Status: Connected & Streaming";
            lblStatus.ForeColor = locked ? Color.Orange : Color.Green;
            if (locked)
            {
                NativeMethods.LockWorkStation();
            }
        }

        private async Task ForceLogout()
        {
            _isStreaming = false;
            _streamCts?.Cancel();
            if (_hubClient != null)
            {
                await _hubClient.DisposeAsync();
                _hubClient = null;
            }
            lblStatus.Text = "Status: Logged out by teacher";
            lblStatus.ForeColor = Color.Red;
            MessageBox.Show("Your session was ended by the teacher.", "Session Ended", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnLogin.Enabled = true;
        }

        private void ShowBroadcast(BroadcastMessage msg)
        {
            lblStatus.Text = "Status: Receiving broadcast";
        }

        private void ShowNotification(NotificationMessage msg)
        {
            MessageBox.Show($"[{msg.Type}] {msg.Title}: {msg.Message}", "Teacher Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _isStreaming = false;
            _streamCts?.Cancel();
            _ = _hubClient?.DisposeAsync();
            base.OnFormClosing(e);
        }
    }

    internal static class ActiveAppInfo
    {
        public static string Get()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;

            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);

            try
            {
                using var process = Process.GetProcessById((int)pid);
                return string.IsNullOrWhiteSpace(process.MainWindowTitle)
                    ? process.ProcessName
                    : $"{process.ProcessName} - {process.MainWindowTitle}";
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        public static uint GetIdleTime()
        {
            var last = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            GetLastInputInfo(ref last);
            return (uint)Environment.TickCount - last.dwTime;
        }

        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}