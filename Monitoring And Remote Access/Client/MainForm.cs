using Client.Services;
using Shared.Contracts;

namespace Client
{
    public partial class MainForm : Form
    {
        // Point this to your ASP.NET Core server's LAN IP (e.g. http://192.168.1.100:5000)
        private const string ServerUrl = "http://localhost:5000/remoteMonitoringHub";

        private readonly IScreenCaptureService _screenCaptureService = new ScreenCaptureService();

        private IMonitoringHubClient? _hubClient;
        private bool _isStreaming = false;
        private CancellationTokenSource? _streamCts;

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
            Size = new Size(420, 320);
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

                await hubClient.StartAsync(ServerUrl);
                await hubClient.RegisterStudentAsync(studentId, Environment.MachineName);

                _hubClient = hubClient;

                lblStatus.Text = "Status: Connected & Streaming";
                lblStatus.ForeColor = Color.Green;

                _isStreaming = true;
                _streamCts = new CancellationTokenSource();
                _ = Task.Run(() => ScreenCaptureLoop(studentId, _streamCts.Token));
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Status: Connection failed";
                lblStatus.ForeColor = Color.Red;
                btnLogin.Enabled = true;
                MessageBox.Show($"Connection error:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ScreenCaptureLoop(string studentId, CancellationToken token)
        {
            while (_isStreaming && !token.IsCancellationRequested)
            {
                try
                {
                    if (_hubClient != null)
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _isStreaming = false;
            _streamCts?.Cancel();
            _ = _hubClient?.DisposeAsync();
            base.OnFormClosing(e);
        }
    }
}
