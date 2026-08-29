using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Client.Services;
using Shared.Contracts;

namespace Client
{
    public partial class MainForm : Form
    {
        private static async Task<string> GetServerUrlAsync(bool forceDiscovery = false)
        {
            var configured = ReadConfiguredServerUrl();
            if (!forceDiscovery && configured != null && !IsLocalhost(configured))
                return configured;

            var discovered = await ServerDiscoveryClient.DiscoverAsync(4000, 5);
            if (discovered != null)
                return discovered;

            return configured ?? "https://localhost:5000/remoteMonitoringHub";
        }

        private static string? ReadConfiguredServerUrl()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client-settings.json");
                if (File.Exists(path))
                {
                    var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                    if (json.RootElement.TryGetProperty("ServerUrl", out var urlElement) &&
                        Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var url) &&
                        string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(url.Host))
                    {
                        return url.ToString();
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool IsLocalhost(string serverUrl)
        {
            return Uri.TryCreate(serverUrl, UriKind.Absolute, out var url) &&
                   (url.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    url.Host.Equals(IPAddress.Loopback.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        private readonly IScreenCaptureService _screenCaptureService = new ScreenCaptureService();

        private IMonitoringHubClient? _hubClient;
        private bool _isStreaming = false;
        private CancellationTokenSource? _streamCts;
        private bool _isLocked = false;
        private string _studentId = "";
        private string _studentName = "";

        // Restriction rules fetched from the server after login
        private readonly List<RestrictionRuleMessage> _blockRules = new();
        private readonly List<RestrictionRuleMessage> _allowRules = new();
        private readonly Dictionary<string, DateTime> _lastInfraction = new();

        // Global session state
        private string _sessionStatus = "None";
        private int _sessionElapsed = 0;

        private TextBox txtStudentId = new();
        private TextBox txtPassword = new();
        private Button btnLogin = new();
        private Label lblStatus = new();

        // Post-login toolbar
        private Label lblUnit = new();
        private Label lblStudent = new();
        private Label lblTimer = new();
        private Label lblState = new();
        private System.Windows.Forms.Timer _countdownTimer = new();

        public MainForm()
        {
            BuildUi();
            _countdownTimer.Interval = 1000;
            _countdownTimer.Tick += (_, _) =>
            {
                if (_sessionStatus == "Running")
                {
                    _sessionElapsed++;
                    RenderTimer();
                }
            };
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

        // Builds the sticky toolbar shown after a successful login
        private void BuildToolbar()
        {
            Controls.Clear();
            Size = new Size(640, 400);

            var bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(26, 29, 36),
                Padding = new Padding(10, 0, 10, 0)
            };

            lblUnit = new Label { Text = "Unit: -", ForeColor = Color.White, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(12, 13) };
            lblStudent = new Label { Text = "Student: -", ForeColor = Color.FromArgb(182, 186, 198), AutoSize = true, Font = new Font("Segoe UI", 10), Location = new Point(230, 13) };
            lblState = new Label { Text = "No Session", ForeColor = Color.FromArgb(120, 130, 150), AutoSize = true, Font = new Font("Segoe UI", 9), Location = new Point(430, 13) };
            lblTimer = new Label { Text = "--:--", ForeColor = Color.FromArgb(34, 197, 94), AutoSize = true, Font = new Font("Consolas", 16, FontStyle.Bold), Location = new Point(500, 8) };

            bar.Controls.AddRange(new Control[] { lblUnit, lblStudent, lblState, lblTimer });

            lblStatus = new Label
            {
                Text = "Status: Connected & Streaming",
                Location = new Point(20, 70),
                AutoSize = true,
                ForeColor = Color.Green,
                Font = new Font("Segoe UI", 10)
            };

            var lblInfo = new Label
            {
                Text = "This workstation is monitored by your teacher.\r\nRestricted applications and websites are blocked.\r\nYour session timer appears above.",
                Location = new Point(20, 110),
                AutoSize = true,
                ForeColor = Color.FromArgb(150, 155, 170)
            };

            var btnLogout = new Button
            {
                Text = "Log Out",
                Location = new Point(20, 210),
                Size = new Size(140, 32),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnLogout.Click += async (_, _) => await ForceLogout(true);

            Controls.AddRange(new Control[] { bar, lblStatus, lblInfo, btnLogout });
            lblUnit.Text = $"Unit: {Environment.MachineName}";
            lblStudent.Text = $"Student: {_studentName}";
        }

        private void RenderTimer()
        {
            int sec = Math.Max(0, _sessionElapsed);
            lblTimer.Text = $"{sec / 60:00}:{sec % 60:00}";
            lblTimer.ForeColor = _sessionStatus == "Running"
                ? Color.FromArgb(34, 197, 94)
                : Color.FromArgb(120, 130, 150);
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            await TryLoginAsync(forceDiscovery: false);
        }

        private async Task TryLoginAsync(bool forceDiscovery)
        {
            string studentId = txtStudentId.Text.Trim();
            string password = txtPassword.Text;

            if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter your Student ID and Password.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnLogin.Enabled = false;
            lblStatus.Text = "Status: Searching for server...";

            string? serverUrl = null;

            try
            {
                serverUrl = await GetServerUrlAsync(forceDiscovery);
                lblStatus.Text = $"Status: Connecting to {serverUrl}...";

                var hubClient = new MonitoringHubClient();
                hubClient.RemoteInputReceived += InputSimulator.ProcessRemoteInput;
                hubClient.RemoteControlStateReceived += state => this.Invoke(() =>
                    Text = state.IsActive ? "CAMS Student Client - Remote support active" : "CAMS Student Client");
                hubClient.Locked += () => this.Invoke(() => SetLocked(true));
                hubClient.Unlocked += () => this.Invoke(() => SetLocked(false));
                hubClient.ForceLogoutRequested += () => this.Invoke(async () => await ForceLogout(false));
                hubClient.BroadcastReceived += msg => this.Invoke(() => ShowBroadcast(msg));
                hubClient.NotificationReceived += msg => this.Invoke(() => ShowPopup("Notification", msg.Title, msg.Message, false));
                hubClient.WarningPopupReceived += msg => this.Invoke(() => ShowPopup("Teacher Warning", msg.Title, msg.Message, true));
                hubClient.GlobalSessionStateReceived += state => this.Invoke(() => OnSessionStateChanged(state));
                hubClient.SessionEnded += () => this.Invoke(async () => await OnSessionEnded());
                hubClient.ShutdownRequested += () => this.Invoke(OnShutdownRequested);
                hubClient.RestartRequested += () => this.Invoke(OnRestartRequested);
                hubClient.RestrictionsReceived += rules => this.Invoke(() => OnRestrictionsReceived(rules));

                var login = await hubClient.LoginAsync(serverUrl, studentId, password, Environment.MachineName);
                await hubClient.StartAsync(serverUrl);
                await hubClient.FetchRestrictionsAsync();

                _hubClient = hubClient;
                _studentId = login.StudentId;
                _studentName = login.DisplayName;

                _isStreaming = true;
                _streamCts = new CancellationTokenSource();

                BuildToolbar();
                _countdownTimer.Start();

                _ = Task.Run(() => ScreenCaptureLoop(_streamCts.Token));
                _ = Task.Run(() => StatusReportLoop(_streamCts.Token));
                _ = Task.Run(() => RestrictionEnforcementLoop(_streamCts.Token));
            }
            catch (SocketException)
            {
                lblStatus.Text = "Status: Server not found";
                lblStatus.ForeColor = Color.Red;
                btnLogin.Enabled = true;
                var choice = MessageBox.Show(
                    "Cannot reach the server.\n\nMake sure the teacher has started CAMS Server and you are on the same network.\n\nWould you like to enter the server IP manually?",
                    "Connection Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes)
                    ShowServerUrlDialog();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized ||
                                                  ex.StatusCode == HttpStatusCode.Forbidden ||
                                                  ex.StatusCode == HttpStatusCode.BadRequest)
            {
                lblStatus.Text = "Status: Login rejected";
                lblStatus.ForeColor = Color.Red;
                btnLogin.Enabled = true;
                MessageBox.Show(
                    "The server rejected this student login.\n\nCheck the Student ID and password, and make sure this workstation is assigned to the student.",
                    "Student Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lblStatus.Text = "Status: Login temporarily blocked";
                lblStatus.ForeColor = Color.Red;
                btnLogin.Enabled = true;
                MessageBox.Show(
                    "Too many failed login attempts were received. Wait one minute and try again.",
                    "Login Temporarily Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (HttpRequestException ex)
            {
                lblStatus.Text = "Status: Server not reachable";
                lblStatus.ForeColor = Color.Red;
                btnLogin.Enabled = true;
                var message = IsCertificateError(ex)
                    ? "The server was discovered, but its HTTPS certificate is not trusted by this PC.\n\nCopy CAMS-Server-Root.cer from the teacher PC and run the client installer again, selecting that certificate. Do not copy the private .pfx file."
                    : "The server was discovered, but HTTPS port 5000 could not complete the connection.\n\nMake sure the server is running, both PCs are on the same Wi-Fi or hotspot, Windows Firewall allows TCP port 5000, and the configured address uses https://.";
                message += $"\n\nTarget: {serverUrl ?? "unknown"}\nDetails: {ex.Message}";
                var choice = MessageBox.Show(
                    message + "\n\nRetry to discover the server again, or choose Cancel to enter the server IP manually.",
                    "Connection Failed", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                if (choice == DialogResult.Retry)
                {
                    ServerDiscoveryClient.ResetCache();
                    await TryLoginAsync(forceDiscovery: true);
                }
                else
                {
                    ShowServerUrlDialog();
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Status: Connection failed";
                lblStatus.ForeColor = Color.Red;
                btnLogin.Enabled = true;
                var choice = MessageBox.Show(
                    $"Connection error:\n\n{ex.Message}\n\nWould you like to enter the server IP manually?",
                    "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (choice == DialogResult.Yes)
                    ShowServerUrlDialog();
            }
        }

        private static bool IsCertificateError(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is System.Security.Authentication.AuthenticationException ||
                    current.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("trust", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("untrusted", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ShowServerUrlDialog()
        {
            var prompt = new Form
            {
                Text = "Enter Server Address",
                Width = 450,
                Height = 180,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false
            };
            var lbl = new Label
            {
                Text = "Server URL (e.g. https://192.168.1.100:5000/remoteMonitoringHub):",
                Location = new Point(14, 18),
                AutoSize = true
            };
            var txt = new TextBox
            {
                Text = "https://localhost:5000/remoteMonitoringHub",
                Location = new Point(14, 45),
                Width = 400
            };
            var btnOk = new Button
            {
                Text = "Save & Retry",
                Location = new Point(250, 80),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnOk.Click += (_, _) =>
            {
                if (!Uri.TryCreate(txt.Text.Trim(), UriKind.Absolute, out var serverUri) ||
                    serverUri.Scheme != Uri.UriSchemeHttps ||
                    string.IsNullOrWhiteSpace(serverUri.Host))
                {
                    MessageBox.Show("Enter a valid HTTPS CAMS server URL.", "Invalid Server URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client-settings.json");
                    var json = "{\n  \"ServerUrl\": \"" + serverUri.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\n}";
                    File.WriteAllText(settingsPath, json);
                    ServerDiscoveryClient.ResetCache();
                }
                catch { }
                prompt.Close();
                BtnLogin_Click(null, EventArgs.Empty);
            };
            prompt.Controls.AddRange(new Control[] { lbl, txt, btnOk });
            prompt.ShowDialog(this);
        }

        private void OnSessionStateChanged(GlobalSessionMessage state)
        {
            _sessionStatus = state.Status;
            if (state.Status == "Running") _sessionElapsed = state.ElapsedSeconds;
            lblState.Text = state.Status;
            lblState.ForeColor = state.Status == "Running" ? Color.FromArgb(34, 197, 94)
                : state.Status == "Paused" ? Color.FromArgb(245, 158, 11)
                : state.Status == "Ended" ? Color.FromArgb(239, 68, 68) : Color.FromArgb(120, 130, 150);
            RenderTimer();
        }

        private async Task OnSessionEnded()
        {
            _sessionStatus = "Ended";
            _sessionElapsed = 0;
            RenderTimer();
            ShowPopup("Session Ended", "",
                "Your laboratory session has ended by the teacher. The workstation is being locked.", true);
            await ForceLogout(false);
            NativeMethods.LockWorkStation();
        }

        private void OnShutdownRequested()
        {
            ShowPopup("Teacher Command", "Shut Down",
                "The teacher has shut down this workstation. Saving work...", false);
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 15") { CreateNoWindow = true, UseShellExecute = false });
        }

        private void OnRestartRequested()
        {
            ShowPopup("Teacher Command", "Restart", "The teacher has restarted this workstation.", false);
            Process.Start(new ProcessStartInfo("shutdown", "/r /t 15") { CreateNoWindow = true, UseShellExecute = false });
        }

        // ---------- Restriction enforcement ----------

        private void OnRestrictionsReceived(List<RestrictionRuleMessage> rules)
        {
            _blockRules.Clear();
            _allowRules.Clear();
            foreach (var r in rules)
            {
                if (r.Mode == "Allow") _allowRules.Add(r);
                else _blockRules.Add(r);
            }
        }

        private async Task RestrictionEnforcementLoop(CancellationToken token)
        {
            while (_isStreaming && !token.IsCancellationRequested)
            {
                try
                {
                    await EnforceOnce(token);
                }
                catch
                {
                    // telemetry/enforcement errors never kill the loop
                }

                await Task.Delay(4000, token);
            }
        }

        private async Task EnforceOnce(CancellationToken token)
        {
            if (_hubClient == null || _isLocked) return;

            var app = ActiveAppInfo.Get(); // e.g. "chrome - Facebook - Google Chrome" or "game.exe"
            if (string.IsNullOrWhiteSpace(app)) return;

            var processName = app.Split(" - ")[0].Trim().ToLowerInvariant();
            var windowTitle = app.ToLowerInvariant();

            // 1) Block rules (blacklist)
            foreach (var rule in _blockRules)
            {
                var target = rule.Target.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(target)) continue;

                bool hit = rule.RuleType == "Website"
                    ? windowTitle.Contains(target) && (processName.Contains("chrome") || processName.Contains("msedge") || processName.Contains("firefox") || processName.Contains("opera"))
                    : processName.Contains(target);

                if (hit) await HandleViolation(rule, app, processName, token);
            }

            // 2) Whitelist (allow) rules — only enforced when the allow list is non-empty
            if (_allowRules.Count > 0 && _allowRules.Any(r => r.RuleType == "Application"))
            {
                bool allowed = _allowRules.Any(r =>
                    r.RuleType == "Application" && processName.Contains(r.Target.Trim().ToLowerInvariant()));
                if (!allowed)
                {
                    // In whitelist mode the session shell is always permitted
                    if (processName is "explorer" or "shellexperiencehost" or "searchapp") return;
                    var allow = _allowRules.First(r => r.RuleType == "Application");
                    await ReportViolation("Application", app, processName, kill: true, token);
                }
            }
        }

        private async Task HandleViolation(RestrictionRuleMessage rule, string app, string processName, CancellationToken token)
        {
            // Kill the offending process for blocked applications/games
            bool kill = rule.RuleType == "Application";
            await ReportViolation(rule.RuleType, app, processName, kill, token);
        }

        private async Task ReportViolation(string targetType, string app, string processName, bool kill, CancellationToken token)
        {
            var key = targetType + ":" + app;
            if (_lastInfraction.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(30))
            {
                return; // throttled
            }
            _lastInfraction[key] = DateTime.UtcNow;

            if (kill)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)))
                    {
                        p.Kill();
                    }
                }
                catch
                {
                    // process may already be gone
                }
            }

            var hub = _hubClient;
            if (hub != null)
            {
                try
                {
                    await hub.ReportInfractionAsync(new InfractionMessage("", _studentId, Environment.MachineName, app, targetType, DateTime.UtcNow));
                }
                catch
                {
                    // offline alert — server will get it on reconnect
                }
            }

            this.Invoke(() => ShowPopup("Restricted Activity Detected", "Blocked by CAMS",
                $"'{app}' is restricted during laboratory sessions. This incident has been reported to your teacher.",
                true));
        }

        private bool _lastIdleReported = false;
        private DateTime _lastActiveAppReport = DateTime.MinValue;
        private string _lastWebsiteReport = string.Empty;

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
                                ConnectionId: "",
                                StudentId: "",
                                PcName: Environment.MachineName,
                                IsIdle: isIdle,
                                Timestamp: DateTime.UtcNow));
                        }

                        if (DateTime.UtcNow - _lastActiveAppReport > TimeSpan.FromSeconds(5))
                        {
                            _lastActiveAppReport = DateTime.UtcNow;
                            var appName = ActiveAppInfo.Get();
                            if (!string.IsNullOrEmpty(appName))
                            {
                                await _hubClient.ReportActiveAppAsync(new ActiveAppMessage(
                                    ConnectionId: "",
                                    StudentId: "",
                                    PcName: Environment.MachineName,
                                    ApplicationName: appName,
                                    Timestamp: DateTime.UtcNow));
                                var website = BrowserUrlCollector.TryGetForegroundWebsite();
                                if (website is { Status: BrowserMonitoringStatus.Captured, Domain: not null } &&
                                    website.Domain != _lastWebsiteReport)
                                {
                                    _lastWebsiteReport = website.Domain;
                                    await _hubClient.ReportWebsiteActivityAsync(new WebsiteActivityMessage(
                                        "", "", Environment.MachineName, website.Domain, website.Browser, DateTime.UtcNow));
                                }
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

        private async Task ScreenCaptureLoop(CancellationToken token)
        {
            while (_isStreaming && !token.IsCancellationRequested)
            {
                try
                {
                    if (_hubClient != null && !_isLocked)
                    {
                        var frame = new ScreenFrameMessage(
                            _studentId,
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

                await Task.Delay(50, token); // ~20 FPS, with one in-flight frame at a time
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

        private async Task ForceLogout(bool manual)
        {
            _isStreaming = false;
            _streamCts?.Cancel();
            _countdownTimer.Stop();
            if (_hubClient != null)
            {
                if (manual)
                {
                    try { await _hubClient.LogoutAsync(); }
                    catch { }
                }
                await _hubClient.DisposeAsync();
                _hubClient = null;
            }
            if (!manual)
            {
                MessageBox.Show("Your session was ended by the teacher.", "Session Ended", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            Application.Exit();
        }

        private void ShowBroadcast(BroadcastMessage msg)
        {
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(msg.FrameBase64));
                using var img = Image.FromStream(ms);
                var form = new Form
                {
                    Text = "Teacher Screen Broadcast",
                    WindowState = FormWindowState.Maximized,
                    StartPosition = FormStartPosition.CenterScreen,
                    TopMost = true,
                    BackColor = Color.Black
                };
                var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = (Image)img.Clone() };
                form.Controls.Add(pic);
                form.Show(this);
                form.KeyPreview = true;
                form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
            }
            catch
            {
                // ignore corrupt frames
            }
        }

        private void ShowPopup(string title, string heading, string message, bool warning)
        {
            var popup = new Form
            {
                Text = title,
                Width = 440,
                Height = 230,
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = warning ? Color.FromArgb(45, 15, 15) : Color.FromArgb(15, 20, 30)
            };

            var head = new Label
            {
                Text = heading,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = warning ? Color.FromArgb(239, 68, 68) : Color.FromArgb(59, 130, 246),
                Location = new Point(20, 18),
                AutoSize = true
            };
            var body = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White,
                Location = new Point(20, 58),
                Size = new Size(385, 80)
            };
            var ok = new Button
            {
                Text = "OK",
                Location = new Point(330, 150),
                Size = new Size(75, 30),
                BackColor = warning ? Color.FromArgb(220, 53, 69) : Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            ok.Click += (_, _) => popup.Close();

            popup.Controls.AddRange(new Control[] { head, body, ok });
            popup.Show(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _isStreaming = false;
            _streamCts?.Cancel();
            _countdownTimer.Stop();
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
