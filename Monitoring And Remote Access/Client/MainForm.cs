using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Client.Services;
using Shared.Contracts;

namespace Client
{
    public partial class MainForm : Form
    {
        private static async Task<string> GetServerUrlAsync(bool forceDiscovery = false)
        {
            var configured = new ClientSettingsStore().LoadOrDefault().ServerUrl;
            if (!forceDiscovery && configured != null && !IsLocalhost(configured))
                return configured;

            var discovered = await ServerDiscoveryClient.DiscoverAsync(4000, 5);
            if (discovered != null)
                return discovered;

            return configured ?? ClientSettingsStore.DefaultServerUrl;
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
        private bool _isClosing;
        private Form? _broadcastForm;
        private PictureBox? _broadcastPicture;
        private string _studentId = "";
        private string _studentName = "";

        // Restriction rules fetched from the server after login
        private readonly List<RestrictionRuleMessage> _blockRules = new();
        private readonly List<RestrictionRuleMessage> _allowRules = new();
        private readonly Dictionary<string, DateTime> _lastInfraction = new();

        // Global session state
        private string _sessionStatus = "None";
        private int _sessionElapsed = 0;

        // CAMS palette, mirroring the design tokens used by the web portal so the
        // agent and the browser read as one product. Every entry names the token
        // it corresponds to, and carries its measured contrast against the
        // surface it is used on. WCAG AA for body text is 4.5:1.
        //
        // Entries with no token are agent-only: the desktop client has a dark
        // brand bar and a compact status line that the portal has no equivalent
        // for, and the on-white status colours would be unreadable there.
        private static readonly Color BrandDark = Color.FromArgb(22, 64, 31);      // --sidebar-bg      #16401F
        private static readonly Color BrandDarker = Color.FromArgb(17, 50, 24);    // agent only        #113218, the bar's shadowed edge
        private static readonly Color BrandEmerald = Color.FromArgb(23, 128, 58);  // --accent-emerald  #17803A
        private static readonly Color BrandMint = Color.FromArgb(187, 243, 198);   // agent only        #BBF3C6, 9.37:1 on the brand bar
        private static readonly Color SurfaceBody = Color.FromArgb(250, 248, 243); // --body-bg         #FAF8F3
        private static readonly Color SurfaceCard = Color.White;                   // --card-bg         #FFFFFF
        private static readonly Color BorderSubtle = Color.FromArgb(231, 226, 217);// --card-border     #E7E2D9
        private static readonly Color TextMain = Color.FromArgb(28, 25, 23);       // --text-main       #1C1917
        private static readonly Color TextMuted = Color.FromArgb(111, 104, 97);    // --text-muted      #6F6861
        private static readonly Color StatusOk = Color.FromArgb(23, 128, 58);      // --cams-success    #17803A, 5.02:1 on white
        private static readonly Color StatusWarn = Color.FromArgb(180, 83, 9);     // --cams-warning    #B45309, 5.02:1 on white
        private static readonly Color StatusDanger = Color.FromArgb(185, 28, 28);  // --cams-danger     #B91C1C, 6.47:1 on white

        // Variants for text sitting on the dark brand bar, where the on-white
        // status colours above would not meet a readable contrast. Agent only;
        // the portal has no dark surface carrying status text.
        private static readonly Color OnDarkStrong = Color.White;                  //                   #FFFFFF, 13.4:1 on the brand bar
        private static readonly Color OnDarkMuted = Color.FromArgb(150, 178, 152); //                   #96B298, 5.10:1
        private static readonly Color OnDarkWarn = Color.FromArgb(252, 211, 77);   //                   #FCD34D, 8.15:1
        private static readonly Color OnDarkDanger = Color.FromArgb(252, 165, 165);//                   #FCA5A5, 6.19:1

        private TextBox txtStudentId = new();
        private TextBox txtPassword = new();
        private Button btnLogin = new();
        private Label lblStatus = new();

        // Post-login toolbar
        private Label lblUnit = new();
        private Label lblStudent = new();
        private Label lblTimer = new();
        private Label lblState = new();
        private Label lblRemoteState = new();
        private Label lblBrowserState = new();
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

        /// <summary>Applies the shared CAMS field styling to a text box.</summary>
        private static TextBox StyleField(TextBox field, bool isPassword = false)
        {
            field.BorderStyle = BorderStyle.FixedSingle;
            field.Font = new Font("Segoe UI", 10.5f);
            field.BackColor = SurfaceCard;
            field.ForeColor = TextMain;
            field.UseSystemPasswordChar = isPassword;
            field.Margin = new Padding(0, 2, 0, 12);
            field.Height = 30;
            return field;
        }

        /// <summary>Field caption in the muted, uppercase style used across CAMS.</summary>
        private static Label FieldLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2)
        };

        private static Button BrandButton(string text, Color background) => new()
        {
            Text = text,
            BackColor = background,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Height = 40,
            Cursor = Cursors.Hand
        };

        private void BuildUi()
        {
            Controls.Clear();
            Text = "CAMS Student Client";
            ClientSize = new Size(440, 460);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = SurfaceBody;
            Font = new Font("Segoe UI", 9.75f);

            // Branded banner, echoing the portal's sign-in header.
            var banner = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = BrandDark };
            var lblBrand = new Label
            {
                Text = "CAMS",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 26)
            };
            var lblBrandSub = new Label
            {
                Text = "Student Client  ·  Pardo Elementary School",
                ForeColor = BrandMint,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Location = new Point(26, 68)
            };
            banner.Controls.AddRange(new Control[] { lblBrand, lblBrandSub });

            // Stacked form body; a flow layout keeps it correct at any DPI scale.
            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(24, 22, 24, 16),
                BackColor = SurfaceBody
            };

            var lblTitle = new Label
            {
                Text = "Sign in to your session",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = TextMain,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14)
            };

            int fieldWidth = ClientSize.Width - 48;

            StyleField(txtStudentId).Width = fieldWidth;
            StyleField(txtPassword, isPassword: true).Width = fieldWidth;

            btnLogin = BrandButton("Log in", BrandEmerald);
            btnLogin.Width = fieldWidth;
            btnLogin.Margin = new Padding(0, 6, 0, 14);
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.FlatAppearance.MouseOverBackColor = Color.FromArgb(23, 128, 58);
            btnLogin.Click += BtnLogin_Click;

            lblStatus = new Label
            {
                Text = "Status: Not connected",
                AutoSize = true,
                MaximumSize = new Size(fieldWidth, 0),
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9)
            };

            var lblHint = new Label
            {
                Text = "Use the account issued by your teacher.",
                AutoSize = true,
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 8.5f),
                Margin = new Padding(0, 10, 0, 0)
            };

            body.Controls.AddRange(new Control[]
            {
                lblTitle,
                FieldLabel("STUDENT ID"), txtStudentId,
                FieldLabel("PASSWORD"), txtPassword,
                btnLogin,
                lblStatus,
                lblHint
            });

            // Fill order matters: the banner docks above the filled body.
            Controls.Add(body);
            Controls.Add(banner);
            AcceptButton = btnLogin;
        }

        /// <summary>One "caption + value" line inside the status card.</summary>
        private static TableLayoutPanel StatusRow(string caption, Label value)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 8)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 3, 8, 0),
                UseMnemonic = false
            };

            value.AutoSize = true;
            value.UseMnemonic = false;
            value.Anchor = AnchorStyles.Left;
            value.Margin = new Padding(0, 2, 0, 0);

            row.Controls.Add(label, 0, 0);
            row.Controls.Add(value, 1, 0);
            return row;
        }

        // Builds the session view shown after a successful login.
        private void BuildToolbar()
        {
            Controls.Clear();
            ClientSize = new Size(660, 430);
            BackColor = SurfaceBody;

            // --- Branded header bar -------------------------------------------------
            // A table layout keeps the identity block and the timer from colliding,
            // which fixed-position labels could not guarantee with long student names.
            var bar = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = BrandDark };
            var barGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(18, 0, 18, 0),
                BackColor = Color.Transparent
            };
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var identity = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                BackColor = Color.Transparent
            };
            lblUnit = new Label
            {
                Text = "Unit: -",
                ForeColor = Color.White,
                AutoSize = true,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 1)
            };
            lblStudent = new Label
            {
                Text = "Student: -",
                ForeColor = BrandMint,
                AutoSize = true,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0)
            };
            identity.Controls.AddRange(new Control[] { lblUnit, lblStudent });

            var meter = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            lblTimer = new Label
            {
                Text = "--:--",
                ForeColor = Color.White,
                AutoSize = true,
                Font = new Font("Consolas", 19, FontStyle.Bold),
                Margin = new Padding(0),
                TextAlign = ContentAlignment.MiddleRight
            };
            lblState = new Label
            {
                Text = "No session",
                ForeColor = BrandMint,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(0),
                TextAlign = ContentAlignment.MiddleRight
            };
            meter.Controls.AddRange(new Control[] { lblTimer, lblState });

            barGrid.Controls.Add(identity, 0, 0);
            barGrid.Controls.Add(meter, 1, 0);
            bar.Controls.Add(barGrid);

            // --- Status card --------------------------------------------------------
            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 18, 20, 18), BackColor = SurfaceBody };

            var card = new Panel
            {
                Dock = DockStyle.Top,
                Height = 132,
                BackColor = SurfaceCard,
                Padding = new Padding(18, 16, 18, 16)
            };
            card.Paint += (sender, e) =>
            {
                if (sender is not Control c) return;
                using var pen = new Pen(BorderSubtle);
                e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
            };

            lblStatus = new Label { Text = "Status: Connected & Streaming", ForeColor = StatusOk, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            lblRemoteState = new Label { Text = "Remote support: inactive", ForeColor = TextMuted, Font = new Font("Segoe UI", 9.75f) };
            lblBrowserState = new Label { Text = "Browser monitoring: starting", ForeColor = TextMuted, Font = new Font("Segoe UI", 9.75f) };

            var rows = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = SurfaceCard
            };
            rows.Controls.Add(StatusRow("CONNECTION", lblStatus));
            rows.Controls.Add(StatusRow("REMOTE SUPPORT", lblRemoteState));
            rows.Controls.Add(StatusRow("BROWSER MONITORING", lblBrowserState));
            card.Controls.Add(rows);

            // --- Guidance + logout --------------------------------------------------
            var lblInfo = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 14, 0, 0),
                Padding = new Padding(0, 14, 0, 10),
                Text = "This workstation is monitored by your teacher." + Environment.NewLine +
                       "Restricted applications may be closed automatically." + Environment.NewLine +
                       "Restricted websites will trigger a warning." + Environment.NewLine +
                       "Your session timer is shown above."
            };

            var btnLogout = BrandButton("Log out", BrandDark);
            btnLogout.Dock = DockStyle.Bottom;
            btnLogout.Width = 150;
            btnLogout.Height = 38;
            btnLogout.FlatAppearance.BorderSize = 0;
            btnLogout.FlatAppearance.MouseOverBackColor = BrandDarker;
            btnLogout.Click += async (_, _) => await ForceLogout(true);

            var logoutHost = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = SurfaceBody };
            btnLogout.Dock = DockStyle.Left;
            logoutHost.Controls.Add(btnLogout);

            // Added last-to-first so docking stacks in the intended order.
            content.Controls.Add(lblInfo);
            content.Controls.Add(card);
            content.Controls.Add(logoutHost);

            Controls.Add(content);
            Controls.Add(bar);

            lblUnit.Text = $"Unit: {Environment.MachineName}";
            lblStudent.Text = $"Student: {_studentName}";
        }

        private void RenderTimer()
        {
            int sec = Math.Max(0, _sessionElapsed);
            lblTimer.Text = $"{sec / 60:00}:{sec % 60:00}";
            lblTimer.ForeColor = _sessionStatus == "Running" ? OnDarkStrong : OnDarkMuted;
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
            MonitoringHubClient? pendingClient = null;

            try
            {
                serverUrl = await GetServerUrlAsync(forceDiscovery);
                lblStatus.Text = $"Status: Connecting to {serverUrl}...";

                var hubClient = new MonitoringHubClient();
                pendingClient = hubClient;
                hubClient.RemoteInputReceived += InputSimulator.ProcessRemoteInput;
                 hubClient.RemoteControlStateReceived += state => this.Invoke(() => OnRemoteControlStateChanged(state));
                hubClient.Locked += () => this.Invoke(() => SetLocked(true));
                hubClient.Unlocked += () => this.Invoke(() => SetLocked(false));
                hubClient.ForceLogoutRequested += () => this.Invoke(async () => await ForceLogout(false));
                hubClient.BroadcastReceived += msg => this.Invoke(() => ShowBroadcast(msg));
                hubClient.BroadcastStopped += () => this.Invoke(CloseBroadcast);
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

                await _managedBrowserCollector.StartAsync();
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
                lblStatus.ForeColor = StatusDanger;
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
                lblStatus.ForeColor = StatusDanger;
                btnLogin.Enabled = true;
                MessageBox.Show(
                    "The server rejected this student login.\n\nCheck the Student ID and password. The student must be active, and the workstation must be available without a conflicting active session.",
                    "Student Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lblStatus.Text = "Status: Login temporarily blocked";
                lblStatus.ForeColor = StatusDanger;
                btnLogin.Enabled = true;
                MessageBox.Show(
                    "Too many failed login attempts were received. Wait one minute and try again.",
                    "Login Temporarily Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (HttpRequestException ex)
            {
                lblStatus.Text = "Status: Server not reachable";
                lblStatus.ForeColor = StatusDanger;
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
                lblStatus.ForeColor = StatusDanger;
                btnLogin.Enabled = true;
                var choice = MessageBox.Show(
                    $"Connection error:\n\n{ex.Message}\n\nWould you like to enter the server IP manually?",
                    "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (choice == DialogResult.Yes)
                    ShowServerUrlDialog();
            }
            finally
            {
                if (pendingClient is not null && !ReferenceEquals(_hubClient, pendingClient))
                {
                    try { await pendingClient.LogoutAsync(); } catch { }
                    try { await pendingClient.DisposeAsync(); } catch { }
                }
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
                Text = "Save and retry",
                Location = new Point(250, 80),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnOk.Click += (_, _) =>
            {
                if (!ClientSettingsStore.TryNormalizeServerUrl(txt.Text.Trim(), out var serverUrl, out var error))
                {
                    MessageBox.Show(error, "Invalid Server URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    new ClientSettingsStore().UpdateServerUrl(serverUrl);
                    ServerDiscoveryClient.ResetCache();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"The server URL could not be saved.\n\n{ex.Message}", "Settings Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                prompt.Close();
                BtnLogin_Click(null, EventArgs.Empty);
            };
            prompt.Controls.AddRange(new Control[] { lbl, txt, btnOk });
            prompt.ShowDialog(this);
        }

        private void OnSessionStateChanged(GlobalSessionMessage state)
        {
            _sessionStatus = state.Status;
            _sessionElapsed = state.ElapsedSeconds;
            lblState.Text = state.Status;
            lblState.ForeColor = state.Status == "Running" ? BrandMint
                : state.Status == "Paused" ? OnDarkWarn
                : state.Status == "Ended" ? OnDarkDanger : OnDarkMuted;
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

        private void OnRemoteControlStateChanged(RemoteControlStateMessage state)
        {
            Text = state.IsActive ? "CAMS Student Client - Remote support active" : "CAMS Student Client";
            lblRemoteState.Text = state.IsActive ? "Remote support: active (teacher controls input)" : "Remote support: inactive";
            lblRemoteState.ForeColor = state.IsActive ? StatusWarn : TextMuted;
            lblStatus.Text = state.IsActive ? "Status: Connected & Streaming" : lblStatus.Text;
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
            var processName = string.IsNullOrWhiteSpace(app)
                ? string.Empty
                : app.Split(" - ")[0].Trim().ToLowerInvariant();
            var appRules = _blockRules.Concat(_allowRules).Where(r => r.RuleType == "Application").ToList();
            var hasApplicationAllowlist = appRules.Any(rule => rule.Mode == "Allow");
            foreach (var running in GetRunningApplications())
            {
                var matchingApp = appRules.Where(rule => PolicyPatternMatcher.MatchesApplication(running.Name, rule.Target))
                    .OrderByDescending(rule => rule.Target.Count(c => c != '*'))
                    .ThenByDescending(rule => rule.Mode == "Allow")
                    .FirstOrDefault();
                if (matchingApp is not null && matchingApp.Mode != "Allow")
                    await HandleViolation(matchingApp, running.Name, running.Name, token);
                else if (hasApplicationAllowlist && matchingApp is null && running.HasWindow && !IsRequiredProcess(running.Name))
                    await ReportViolation("Application", running.Name, running.Name, kill: true, token, reportedTarget: running.Name);
            }

            var website = _lastForegroundWebsite;
            if (website is not { Status: BrowserMonitoringStatus.Captured, Domain: not null }) return;
            var websiteRules = _blockRules.Concat(_allowRules).Where(r => r.RuleType == "Website").ToList();
            var matchingWebsite = websiteRules.Where(r => PolicyPatternMatcher.MatchesDomain(website.Domain, r.Target))
                .OrderByDescending(r => r.Target.Count(c => c != '*')).ThenByDescending(r => r.Mode == "Allow").FirstOrDefault();
            if (matchingWebsite is not null && matchingWebsite.Mode != "Allow")
                await ReportViolation("Website", website.Domain, processName, kill: false, token, reportedTarget: website.Domain);
            else if (websiteRules.Any(rule => rule.Mode == "Allow") && matchingWebsite is null)
                await ReportViolation("Website", website.Domain, processName, kill: false, token, reportedTarget: website.Domain);
        }

        private async Task HandleViolation(RestrictionRuleMessage rule, string app, string processName, CancellationToken token)
        {
            // Kill the offending process for blocked applications/games
            bool kill = rule.RuleType == "Application";
            // Persist only a normalized process name or the matched rule target, never raw window titles.
            var reportedTarget = rule.RuleType == "Website" ? rule.Target : processName;
            await ReportViolation(rule.RuleType, app, processName, kill, token, reportedTarget);
        }

        private async Task ReportViolation(string targetType, string app, string processName, bool kill, CancellationToken token, string? reportedTarget = null)
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
                    await hub.ReportInfractionAsync(new InfractionMessage("", _studentId, Environment.MachineName, reportedTarget ?? app, targetType, DateTime.UtcNow));
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
        private BrowserWebsiteObservation? _lastForegroundWebsite;
        private readonly Dictionary<string, string> _lastBrowserStatus = new(StringComparer.OrdinalIgnoreCase);
        private readonly ManagedBrowserCollector _managedBrowserCollector = CreateManagedBrowserCollector();

        private static ManagedBrowserCollector CreateManagedBrowserCollector()
        {
            try
            {
                return new ManagedBrowserCollector(new ClientSettingsStore().Load().ToManagedBrowserOptions());
            }
            catch { return new ManagedBrowserCollector(); }
        }

        private static IEnumerable<(string Name, bool HasWindow)> GetRunningApplications()
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    string name;
                    bool hasWindow;
                    try
                    {
                        name = process.ProcessName.Trim().ToLowerInvariant();
                        hasWindow = process.MainWindowHandle != IntPtr.Zero;
                    }
                    catch
                    {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(name)) yield return (name, hasWindow);
                }
            }
        }

        private static bool IsRequiredProcess(string processName)
        {
            var clientName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Client").ToLowerInvariant();
            return processName == clientName || processName is "explorer" or "shellexperiencehost" or "searchapp" or
                "searchhost" or "startmenuexperiencehost" or "textinputhost" or "dwm" or "winlogon" or "csrss" or
                "services" or "lsass" or "svchost" or "system" or "idle";
        }

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
                                var foregroundBrowser = appName.Split(" - ")[0].Trim().ToLowerInvariant();
                                var fallbackWebsite = BrowserUrlCollector.TryGetForegroundWebsite();
                                var website = fallbackWebsite is { Status: BrowserMonitoringStatus.Captured }
                                    ? fallbackWebsite
                                    : null;
                                if (website == null && foregroundBrowser is "chrome" or "brave")
                                    website = await _managedBrowserCollector.TryGetActiveWebsiteAsync(foregroundBrowser, appName, token);
                                website ??= fallbackWebsite;
                                _lastForegroundWebsite = website is { Status: BrowserMonitoringStatus.Captured, Domain: not null }
                                    ? website
                                    : null;
                                if (website is { Status: BrowserMonitoringStatus.Captured, Domain: not null } &&
                                    $"{website.Browser}:{website.Domain}" != _lastWebsiteReport)
                                {
                                    _lastWebsiteReport = $"{website.Browser}:{website.Domain}";
                                    await _hubClient.ReportWebsiteActivityAsync(new WebsiteActivityMessage(
                                        "", "", Environment.MachineName, website.Domain, website.Browser, DateTime.UtcNow));
                                }
                                if (_lastForegroundWebsite is null) _lastWebsiteReport = string.Empty;
                                await ReportBrowserMonitoringStatusAsync(website);
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

        private async Task ReportBrowserMonitoringStatusAsync(BrowserWebsiteObservation? observation)
        {
            if (_hubClient == null) return;

            var summaries = new List<string>();
            foreach (var status in _managedBrowserCollector.GetStatus())
            {
                var foreground = observation != null && string.Equals(observation.Browser, status.Identity, StringComparison.OrdinalIgnoreCase);
                var mode = foreground ? observation!.Mode : status.EndpointAvailable
                    ? BrowserMonitoringMode.ManagedProtocol
                    : BrowserMonitoringMode.Unavailable;
                var detail = foreground && observation!.Mode == BrowserMonitoringMode.WindowTitleFallback
                    ? observation.Status == BrowserMonitoringStatus.Captured ? "Foreground URL captured" : "Foreground browser detected; URL unavailable"
                    : status.Message;
                var signature = $"{mode}:{detail}";
                summaries.Add($"{status.Identity}: {ModeLabel(mode)}");
                if (_lastBrowserStatus.TryGetValue(status.Identity, out var previous) && previous == signature) continue;

                _lastBrowserStatus[status.Identity] = signature;
                await _hubClient.ReportBrowserMonitoringStatusAsync(new BrowserMonitoringStatusMessage(
                    "", "", Environment.MachineName, status.Identity, mode, DateTime.UtcNow, detail));
            }

            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(() => lblBrowserState.Text = $"Browser monitoring: {string.Join(" | ", summaries)}");
        }

        private static string ModeLabel(BrowserMonitoringMode mode) => mode switch
        {
            BrowserMonitoringMode.ManagedProtocol => "managed",
            BrowserMonitoringMode.WindowTitleFallback => "fallback",
            _ => "unavailable"
        };

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
                            DateTime.UtcNow);

                        await _hubClient.SendScreenFrameAsync(frame);
                    }
                }
                catch
                {
                    // Frame dropped; keep the loop alive
                }

                await Task.Delay(50, token); // Capture-loop delay target, with one in-flight frame; effective FPS varies
            }
        }

        private void SetLocked(bool locked)
        {
            _isLocked = locked;
            lblStatus.Text = locked ? "Status: Locked by teacher" : "Status: Connected & Streaming";
            lblStatus.ForeColor = locked ? StatusWarn : StatusOk;
            if (locked)
            {
                NativeMethods.LockWorkStation();
            }
        }

        private async Task ForceLogout(bool manual)
        {
            _isClosing = true;
            _isStreaming = false;
            _streamCts?.Cancel();
            _countdownTimer.Stop();
            _managedBrowserCollector.Dispose();
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
                if (_broadcastForm is null || _broadcastForm.IsDisposed)
                {
                    _broadcastForm = new Form
                    {
                        Text = "Teacher Screen Broadcast",
                        WindowState = FormWindowState.Maximized,
                        StartPosition = FormStartPosition.CenterScreen,
                        TopMost = true,
                        BackColor = Color.Black
                    };
                    _broadcastPicture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
                    _broadcastForm.Controls.Add(_broadcastPicture);
                    _broadcastForm.FormClosed += (_, _) =>
                    {
                        _broadcastPicture?.Image?.Dispose();
                        _broadcastPicture = null;
                        _broadcastForm = null;
                    };
                    _broadcastForm.Show(this);
                }
                var previous = _broadcastPicture!.Image;
                _broadcastPicture.Image = (Image)img.Clone();
                previous?.Dispose();
            }
            catch
            {
                // ignore corrupt frames
            }
        }

        private void CloseBroadcast()
        {
            if (_broadcastForm is { IsDisposed: false }) _broadcastForm.Close();
        }

        private void ShowPopup(string title, string heading, string message, bool warning)
        {
            // Mirrors the portal's themed dialog: a coloured header band above a light body.
            var accent = warning ? StatusDanger : BrandDark;

            var popup = new Form
            {
                Text = title,
                ClientSize = new Size(440, 236),
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = SurfaceCard,
                Font = new Font("Segoe UI", 9.75f)
            };

            var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = accent };
            var head = new Label
            {
                Text = heading,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                Location = new Point(20, 17)
            };
            header.Controls.Add(head);

            var body = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 10),
                ForeColor = TextMain,
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 18, 20, 8)
            };

            var ok = BrandButton("I understand", accent);
            ok.Width = 150;
            ok.Height = 38;
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (_, _) => popup.Close();

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = SurfaceCard, Padding = new Padding(20, 0, 20, 16) };
            ok.Dock = DockStyle.Right;
            footer.Controls.Add(ok);

            popup.Controls.Add(body);
            popup.Controls.Add(footer);
            popup.Controls.Add(header);
            popup.AcceptButton = ok;
            popup.Show(this);
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isClosing && _hubClient is not null)
            {
                e.Cancel = true;
                await ForceLogout(true);
                return;
            }
            _isStreaming = false;
            _streamCts?.Cancel();
            _countdownTimer.Stop();
            _managedBrowserCollector.Dispose();
            CloseBroadcast();
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
