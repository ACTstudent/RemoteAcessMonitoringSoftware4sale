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
        private volatile bool _sessionPaused;
        private volatile bool _restartRequested;
        private bool _restartScheduling;
        private bool _restartScheduled;
        private readonly SessionScreenGuard _sessionScreen = new();

        // Tray presence. The window hides into the notification area instead of
        // terminating; the icon stays visible and its Exit item is the only way
        // out, so the agent is tucked away, never concealed.
        private TrayIconController? _tray;
        private bool _exitRequested;
        private bool _trayNoticeShown;

        // Set while the tray's own menu is open, so the sign-in guard lets go of
        // the foreground long enough for someone to reach Exit.
        private bool _trayMenuOpen;
        private Form? _broadcastForm;
        private PictureBox? _broadcastPicture;
        private string _studentId = "";
        private string _studentName = "";

        // Restriction rules fetched from the server after login
        private RestrictionRuleMessage[] _restrictionRules = Array.Empty<RestrictionRuleMessage>();

        // Global session state
        // Owns every "should this be sent" decision the status loop makes.
        private readonly TelemetryGate _telemetry = new();
        private readonly SessionClock _session = new();

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
                if (_session.Tick()) RenderTimer();
            };

            // The window, the taskbar and Alt+Tab all take this one.
            var windowIcon = LoadBrandIcon();
            if (windowIcon is not null) Icon = windowIcon;

            _tray = new TrayIconController("CAMS Student Client", LoadBrandIcon(SystemInformation.SmallIconSize));
            _tray.RestoreRequested += RestoreFromTray;
            _tray.StatusRequested += ShowTrayStatus;
            _tray.LogoutRequested += LogoutFromTray;
            _tray.ExitRequested += ExitFromTray;
            _tray.MenuOpened += () => _trayMenuOpen = true;
            _tray.MenuClosed += () => _trayMenuOpen = false;

            TopMost = true;   // released once a student signs in
        }

        /// <summary>
        /// The CAMS mark, at the size Windows asked for rather than one bitmap
        /// rescaled. Returns null if the file is missing, so the tray falls back
        /// to the icon it draws itself - a missing icon must never be the reason
        /// a lab machine has no agent.
        /// </summary>
        private static Icon? LoadBrandIcon(Size? size = null)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "cams.ico");
                if (!File.Exists(path)) return null;
                return size is Size wanted ? new Icon(path, wanted) : new Icon(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Hides the window into the tray, leaving the agent running.</summary>
        private void HideToTray(
            string title = "CAMS is still running",
            string text = "The window is in the notification area. Double-click the icon to open it, or right-click for Exit.")
        {
            Hide();
            ShowInTaskbar = false;
            if (_trayNoticeShown) return;
            _trayNoticeShown = true;
            _tray?.ShowBalloon(title, text, ToolTipIcon.Info);
        }

        /// <summary>Brings the window back from the tray and gives it focus.</summary>
        private void RestoreFromTray()
        {
            if (IsDisposed) return;
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            CentreOnScreen();   // comes back to the middle, not wherever it was
            Activate();
            BringToFront();
        }

        /// <summary>Answers "Check Status" from the tray with what the agent is doing now.</summary>
        private void ShowTrayStatus()
        {
            string body;
            if (_hubClient is null)
            {
                body = "Not signed in.";
            }
            else
            {
                var who = string.IsNullOrWhiteSpace(_studentName) ? _studentId : $"{_studentName} ({_studentId})";
                var line = string.IsNullOrWhiteSpace(lblStatus.Text) ? "Connected" : lblStatus.Text.Replace("Status: ", "");
                var timer = _session.IsRunning ? $"\nSession: {_session.Display()}" : "";
                body = $"{who}\n{line}{timer}";
            }
            _tray?.SetTooltip(_hubClient is null ? "CAMS Student Client — signed out" : $"CAMS — {_studentId}");
            _tray?.ShowBalloon("CAMS status", body, ToolTipIcon.Info);
        }

        /// <summary>
        /// The tray's Log out: ends the student's session, exactly as the Log out
        /// button in the session view does. Offered only while signed in.
        /// </summary>
        private void LogoutFromTray()
        {
            if (_hubClient is null) return;
            _ = ForceLogout(true, quit: false);
        }

        /// <summary>The tray's Exit: a real quit, logging out first if a session is live.</summary>
        private void ExitFromTray()
        {
            _exitRequested = true;
            if (_hubClient is not null)
            {
                _ = ForceLogout(true); // logs the session out, then Application.Exit()
            }
            else
            {
                Close();
            }
        }

        /// <summary>Minimising the window sends it to the tray rather than the taskbar.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Minimising before signing in would be a way past the guard, so it is
            // refused. After signing in it goes to the tray as usual.
            if (WindowState == FormWindowState.Minimized && _hubClient is null && !_isClosing)
            {
                WindowState = FormWindowState.Normal;
                return;
            }
            if (WindowState == FormWindowState.Minimized && _tray is not null && !_isClosing)
            {
                HideToTray();
            }
        }

        // --- Fixed position ---------------------------------------------------
        // On a shared lab machine the client should always be where the student
        // expects it, and should not be draggable off the edge of the screen.
        // Windows routes every way of moving a window - the title-bar drag, the
        // system menu's Move, Alt+Space then M - through one WM_SYSCOMMAND, so
        // refusing that single message covers all of them.
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MOVE = 0xF010;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_MOVE)
                return;   // swallow the move; the close and minimise buttons are untouched

            base.WndProc(ref m);

            // A caption that reports itself as client area cannot begin a drag at
            // all, so the window does not even twitch when a student tries.
            if (m.Msg == WM_NCHITTEST && m.Result == (IntPtr)HTCAPTION)
                m.Result = (IntPtr)HTCLIENT;
        }

        // --- Sign-in guard ----------------------------------------------------
        // Until a student signs in, the client keeps the foreground: clicking another
        // window hands focus straight back. Combined with TopMost this stops the
        // machine being used without signing in, without covering the whole screen.
        //
        // Three deliberate ways it stands down, or the machine would be unusable:
        //   - once signed in (_hubClient is set), it never fires again;
        //   - while any window of ours is active, including our own dialogs, because
        //     ActiveForm is then non-null;
        //   - while the tray menu is open, which is the one route out - the menu is
        //     not a Form, so it needs its own flag.
        //
        // This is a deterrent, not a security boundary. Ctrl+Alt+Del and Task Manager
        // belong to Windows and still work. The intended exit is the tray's Exit.
        private bool ShouldHoldForeground() =>
            _hubClient is null && !_isClosing && !IsDisposed && Visible && !_trayMenuOpen;

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!ShouldHoldForeground()) return;
            // Deferred: taking the foreground back inside the deactivate handler
            // fights Windows mid-switch and the click is lost either way.
            BeginInvoke(() =>
            {
                if (ShouldHoldForeground()) ReclaimForeground();
            });
        }

        /// <summary>
        /// Takes the foreground back from another application.
        ///
        /// Form.Activate is not enough: Windows refuses to hand the foreground to a
        /// process that did not receive the last input event, so the call silently
        /// does nothing and the student keeps whatever they clicked. Attaching to
        /// the input queue of the thread that currently owns the foreground lifts
        /// that refusal for the moment it takes to ask.
        /// </summary>
        private void ReclaimForeground()
        {
            if (!IsHandleCreated) return;
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle) return;

            // A window of our own - a dialog, or the tray menu - is not an escape.
            var ownerThread = NativeMethods.GetWindowThreadProcessId(foreground, out var ownerPid);
            if (ownerPid == (uint)Environment.ProcessId) return;

            var self = NativeMethods.GetCurrentThreadId();
            var attached = ownerThread != self && NativeMethods.AttachThreadInput(self, ownerThread, true);
            try
            {
                NativeMethods.BringWindowToTop(Handle);
                NativeMethods.SetForegroundWindow(Handle);
                Activate();
            }
            finally
            {
                if (attached) NativeMethods.AttachThreadInput(self, ownerThread, false);
            }
        }

        // --- Desktop shield ---------------------------------------------------
        // The focus guard only reacts once a click has already landed somewhere.
        // The shield stops the click itself: a dim, top-most window over each
        // screen, so the desktop and every window behind it are unreachable until
        // a student signs in.
        //
        // It stops at the working area rather than covering the whole screen on
        // purpose. The taskbar carries the notification area, and the tray icon's
        // Exit is the one way out of the gate - covering it would seal the machine
        // with no exit at all. The focus guard covers what the shield leaves.
        private readonly List<Form> _shields = new();

        private void ShowDesktopShield()
        {
            if (_shields.Count > 0 || _hubClient is not null || _isClosing) return;
            foreach (var screen in Screen.AllScreens)
            {
                var shield = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = screen.WorkingArea,
                    BackColor = Color.Black,
                    Opacity = 0.55,
                    ShowInTaskbar = false,
                    TopMost = true,
                    ControlBox = false,
                    Cursor = Cursors.No
                };
                // A click on the shield leads back to the sign-in window rather
                // than leaving the student tapping a dimmed screen that ignores them.
                shield.Activated += (_, _) => { if (ShouldHoldForeground()) ReclaimForeground(); };
                shield.Click += (_, _) => { if (ShouldHoldForeground()) ReclaimForeground(); };
                _shields.Add(shield);
                shield.Show();
            }
            BringToFront();
            Activate();
        }

        private void HideDesktopShield()
        {
            foreach (var shield in _shields)
            {
                shield.Hide();
                shield.Dispose();
            }
            _shields.Clear();
        }

        /// <summary>Puts the window in the middle of the screen it is on.</summary>
        private void CentreOnScreen()
        {
            var area = Screen.FromControl(this).WorkingArea;
            Location = new Point(
                area.Left + Math.Max(0, (area.Width - Width) / 2),
                area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        /// <summary>
        /// Re-centres whenever the layout changes size - signing in swaps the 440
        /// wide login for the 660 wide session view, and without this the window
        /// would grow from its top-left corner and sit off-centre afterwards.
        /// </summary>
        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            if (IsHandleCreated && WindowState == FormWindowState.Normal) CentreOnScreen();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CentreOnScreen();
            ShowDesktopShield();
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
            // Signed in: the machine is released. The guard already stopped firing
            // the moment _hubClient was set; this drops the always-on-top with it,
            // so the session window behaves like any other window.
            TopMost = false;
            HideDesktopShield();
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
                       "Blocked websites are denied in CAMS browsers and browsers using Windows proxy settings." + Environment.NewLine +
                       "Your session timer is shown above."
            };

            var btnLogout = BrandButton("Log out", BrandDark);
            btnLogout.Dock = DockStyle.Bottom;
            btnLogout.Width = 150;
            btnLogout.Height = 38;
            btnLogout.FlatAppearance.BorderSize = 0;
            btnLogout.FlatAppearance.MouseOverBackColor = BrandDarker;
            btnLogout.Click += async (_, _) => await ForceLogout(true, quit: false);

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
            lblTimer.Text = _session.Display();
            lblTimer.ForeColor = _session.IsRunning ? OnDarkStrong : OnDarkMuted;
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
                ShowMessage("Sign in", "Please enter your Student ID and Password.", DialogTone.Warning);
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
                hubClient.RemoteInputReceived += message =>
                {
                    if (!_sessionPaused && !_restartRequested) InputSimulator.ProcessRemoteInput(message);
                };
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
                hubClient.RestartRequested += () => this.Invoke(async () => await OnRestartRequested());
                hubClient.RestrictionsReceived += rules => this.Invoke(() => OnRestrictionsReceived(rules));

                var login = await hubClient.LoginAsync(serverUrl, studentId, password, Environment.MachineName);
                await hubClient.StartAsync(serverUrl);
                await hubClient.FetchRestrictionsAsync();
                if (_restartRequested) return;

                StartWebsiteFiltering(new Uri(serverUrl));
                await _managedBrowserCollector.StartAsync();

                _hubClient = hubClient;
                _studentId = login.StudentId;
                _studentName = login.DisplayName;

                _isStreaming = true;
                _streamCts = new CancellationTokenSource();

                BuildToolbar();
                // The initial persisted state can arrive before toolbar controls
                // exist, including a reconnect into an already paused session.
                OnSessionStateChanged(new GlobalSessionMessage(_session.Status, _session.ElapsedSeconds, null));
                _countdownTimer.Start();

                _ = Task.Run(() => ScreenCaptureLoop(_streamCts.Token));
                _ = Task.Run(() => StatusReportLoop(_streamCts.Token));
                _ = Task.Run(() => RestrictionEnforcementLoop(_streamCts.Token));

                // Log out becomes reachable from the tray now there is a session
                // to end - the window it used to live in is about to disappear.
                if (_tray is not null) _tray.CanLogOut = true;

                // The session is live, so the window goes away. A student has no
                // reason to keep the session view on screen while they work; the
                // tray icon brings it back when they want to check the timer.
                // Hidden last, so everything above is wired before it disappears.
                HideToTray("Signed in",
                    "Your session has started. Double-click the CAMS icon here to see it, or right-click for Exit.");
            }
            catch (SocketException)
            {
                lblStatus.Text = "Status: Server not found";
                lblStatus.ForeColor = StatusDanger;
                btnLogin.Enabled = true;
                var choice = ShowMessage(
                    "Cannot reach the server",
                    "Make sure the teacher has started CAMS Server and that this computer is on the same network.\n\nYou can enter the server address yourself if you know it.",
                    DialogTone.Danger, MessageBoxButtons.YesNo,
                    affirmative: "Enter address", dismissive: "Not now");
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
                ShowMessage(
                    "Sign in was refused",
                    "Check the Student ID and password.\n\nThe account must be active, and this workstation must be free of another session.",
                    DialogTone.Danger, affirmative: "Try again");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lblStatus.Text = "Status: Login temporarily blocked";
                lblStatus.ForeColor = StatusDanger;
                btnLogin.Enabled = true;
                ShowMessage(
                    "Too many attempts",
                    "Too many sign-in attempts were made from this computer.\n\nWait one minute, then try again. Ask your teacher if you are not sure of your password.",
                    DialogTone.Warning);
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
                var choice = ShowMessage(
                    "Cannot reach the server",
                    message,
                    DialogTone.Danger, MessageBoxButtons.RetryCancel,
                    affirmative: "Search again", dismissive: "Enter address");
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
                var choice = ShowMessage(
                    "Connection failed",
                    $"{ex.Message}\n\nYou can enter the server address yourself if you know it.",
                    DialogTone.Danger, MessageBoxButtons.YesNo,
                    affirmative: "Enter address", dismissive: "Not now");
                if (choice == DialogResult.Yes)
                    ShowServerUrlDialog();
            }
            finally
            {
                if (pendingClient is not null && !ReferenceEquals(_hubClient, pendingClient))
                {
                    StopWebsiteFiltering();
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
            // Same chrome as ShowMessage: a brand header band over a light body.
            var prompt = new Form
            {
                Text = "Server address",
                ClientSize = new Size(470, 224),
                StartPosition = FormStartPosition.Manual,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                BackColor = SurfaceCard,
                Font = new Font("Segoe UI", 9.75f),
                TopMost = this.TopMost   // else the top-most gate covers it
            };
            var promptHeader = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = BrandDark };
            promptHeader.Controls.Add(new Label
            {
                Text = "Server address",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(22, 17)
            });
            prompt.Controls.Add(promptHeader);

            var lbl = new Label
            {
                Text = "Ask your teacher for this address. It looks like:\nhttps://192.168.1.100:5000/remoteMonitoringHub",
                Location = new Point(22, 80),
                MaximumSize = new Size(426, 0),
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = true
            };
            var txt = new TextBox
            {
                Text = "https://localhost:5000/remoteMonitoringHub",
                Location = new Point(22, 126),
                Width = 426,
                Height = 30,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SurfaceCard,
                ForeColor = TextMain,
                Font = new Font("Segoe UI", 10)
            };
            var btnOk = BrandButton("Save and retry", BrandEmerald);
            btnOk.Location = new Point(298, 170);
            btnOk.Size = new Size(150, 38);
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += (_, _) =>
            {
                if (!ClientSettingsStore.TryNormalizeServerUrl(txt.Text.Trim(), out var serverUrl, out var error))
                {
                    ShowMessage("That address is not valid", error, DialogTone.Warning);
                    return;
                }

                try
                {
                    new ClientSettingsStore().UpdateServerUrl(serverUrl);
                    ServerDiscoveryClient.ResetCache();
                }
                catch (Exception ex)
                {
                    ShowMessage("The address could not be saved", ex.Message, DialogTone.Danger);
                    return;
                }
                prompt.Close();
                BtnLogin_Click(null, EventArgs.Empty);
            };
            prompt.Controls.AddRange(new Control[] { lbl, txt, btnOk });
            CenterDialogOnScreen(prompt);
            prompt.ShowDialog(this);
        }

        private void OnSessionStateChanged(GlobalSessionMessage state)
        {
            if (_restartRequested || _isClosing) return;
            _session.Apply(state.Status, state.ElapsedSeconds);
            _sessionPaused = state.Status == LabSessionStatus.Paused;
            if (_sessionPaused)
            {
                CloseBroadcast();
                _sessionScreen.Show("Session paused",
                    "Please wait for your teacher to continue the session.",
                    $"Session time: {_session.Display()}  •  Your computer is temporarily paused");
                lblStatus.Text = _sessionScreen.InputGuardError is null
                    ? "Status: Session paused by teacher"
                    : "Status: Pause screen active; Windows input guard needs attention";
                lblStatus.ForeColor = StatusWarn;
            }
            else
            {
                _sessionScreen.Hide();
                lblStatus.Text = _isLocked ? "Status: Locked by teacher" : "Status: Connected & Streaming";
                lblStatus.ForeColor = _isLocked ? StatusWarn : StatusOk;
            }
            lblState.Text = state.Status;
            lblState.ForeColor = state.Status == LabSessionStatus.Running ? BrandMint
                : state.Status == LabSessionStatus.Paused ? OnDarkWarn
                : state.Status == LabSessionStatus.Ended ? OnDarkDanger : OnDarkMuted;
            RenderTimer();
        }

        private async Task OnSessionEnded()
        {
            // Explicit End sends RestartStudent first. Do not replace its screen
            // or race its restart request with the ordinary logout/expiry path.
            if (_restartRequested || _isClosing) return;
            _session.End();
            _sessionPaused = false;
            _sessionScreen.Hide();
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

        private async Task OnRestartRequested()
        {
            if (_restartScheduling || _restartScheduled || _isClosing) return;
            _restartRequested = true;
            _restartScheduling = true;
            _sessionPaused = false;
            _session.End();
            lblState.Text = LabSessionStatus.Ended;
            lblState.ForeColor = OnDarkDanger;
            RenderTimer();
            CloseBroadcast();
            _sessionScreen.Show("Restarting this computer",
                "Your teacher requested a restart. Please wait.",
                "CAMS is preparing the workstation to restart");
            try
            {
                // Restore networking before Windows terminates the client. Keep
                // the full-screen UI and hub alive until Windows shuts down.
                StopWebsiteFiltering(notifyStudent: false);
                using var process = Process.Start(new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r /f /t 10")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                }) ?? throw new InvalidOperationException("Windows did not start the restart command.");
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? $"Windows rejected the restart (code {process.ExitCode})." : error.Trim());

                _restartScheduled = true;
                _sessionScreen.Show("Restarting this computer",
                    "This computer will restart automatically in a few seconds.",
                    "Session ended  •  Please wait for the next class");
                _managedBrowserCollector.Dispose();
                lblStatus.Text = "Status: Restart scheduled by teacher";
                lblStatus.ForeColor = StatusWarn;
                _isStreaming = false;
                _streamCts?.Cancel();
                _countdownTimer.Stop();
                var hub = _hubClient;
                _hubClient = null;
                // End has already persisted on the server. Stop reconnecting so
                // this old client cannot create a new session during the countdown.
                if (hub is not null) await hub.DisposeAsync();
            }
            catch (Exception ex)
            {
                if (_restartScheduled)
                {
                    _sessionScreen.ShowNotice($"Restart scheduled. CAMS cleanup needs attention: {ex.Message}");
                    return;
                }
                lblStatus.Text = $"Restart failed: {ex.Message}";
                lblStatus.ForeColor = StatusDanger;
                _sessionScreen.Show("Restart needs attention",
                    "Windows could not restart this computer. Please ask your teacher for help.",
                    "Your teacher can retry Restart from the monitoring screen");
                _sessionScreen.ShowNotice(ex.Message);
            }
            finally { _restartScheduling = false; }
        }

        // ---------- Restriction enforcement ----------

        private void OnRestrictionsReceived(List<RestrictionRuleMessage> rules)
        {
            var snapshot = new List<RestrictionRuleMessage>();
            foreach (var rule in rules)
            {
                var type = rule.RuleType?.Trim().ToLowerInvariant() switch
                {
                    "application" or "blockapplication" or "process" => "Application",
                    "website" or "blockwebsite" or "domain" => "Website",
                    _ => null
                };
                if (type is null) continue;
                var target = type == "Application"
                    ? PolicyPatternMatcher.NormalizeApplication(rule.Target)
                    : PolicyPatternMatcher.NormalizeDomainPattern(rule.Target);
                if (string.IsNullOrWhiteSpace(target)) continue;
                snapshot.Add(rule with { RuleType = type, Target = target,
                    Mode = string.Equals(rule.Mode?.Trim(), "Allow", StringComparison.OrdinalIgnoreCase) ? "Allow" : "Block" });
            }
            // Publish the entire update at once; enforcement runs on another thread.
            Volatile.Write(ref _restrictionRules, snapshot.ToArray());
            _websiteProxy?.UpdateRules(snapshot);
        }

        private async Task RestrictionEnforcementLoop(CancellationToken token)
        {
            while (_isStreaming && !token.IsCancellationRequested)
            {
                try
                {
                    await EnforceOnce(token);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(() =>
                        {
                            lblStatus.Text = $"Restriction enforcement needs attention: {ex.Message}";
                            lblStatus.ForeColor = StatusDanger;
                        });
                }

                await Task.Delay(4000, token);
            }
        }

        private async Task EnforceOnce(CancellationToken token)
        {
            if (_hubClient == null || _restartRequested) return;
            _windowsProxy?.EnsureApplied();
            if (_websiteProxy is not { IsRunning: true })
                throw new InvalidOperationException("The website filter stopped. Restart CAMS before browsing.");

            foreach (var domain in _websiteProxy.DrainBlockedDomains())
                await ReportViolation("Website", domain, "browser", kill: false, token,
                    reportedTarget: domain, outcome: "CAMS denied the website connection.", notifyStudent: false);
            if (_isLocked) return;

            var rules = Volatile.Read(ref _restrictionRules);
            var appRules = rules.Where(r => r.RuleType == "Application").ToList();
            var hasApplicationAllowlist = appRules.Any(rule => rule.Mode == "Allow");
            foreach (var running in GetRunningApplications())
            {
                token.ThrowIfCancellationRequested();
                if (IsRequiredProcess(running.Name)) continue;
                var matchingApp = appRules.Where(rule => PolicyPatternMatcher.MatchesApplication(running.Name, rule.Target))
                    .OrderByDescending(rule => rule.Target.Count(c => c != '*'))
                    .ThenByDescending(rule => rule.Mode == "Allow")
                    .FirstOrDefault();
                if (matchingApp is not null && matchingApp.Mode != "Allow")
                    await HandleViolation(matchingApp, running.Name, running.Name, token);
                else if (hasApplicationAllowlist && matchingApp is null && running.HasWindow && !IsRequiredProcess(running.Name))
                    await ReportViolation("Application", running.Name, running.Name, kill: true, token, reportedTarget: running.Name);
            }

            var websiteRules = rules.Where(r => r.RuleType == "Website").ToList();
            // Enforcement is independent of telemetry timing and its alert cooldown.
            var closedTabs = await _managedBrowserCollector.EnforceWebsiteRulesAsync(websiteRules, token);
            foreach (var closed in closedTabs)
                await ReportViolation("Website", closed.Domain!, closed.Browser, kill: false, token,
                    reportedTarget: closed.Domain, outcome: "The restricted tab was closed in your CAMS-managed browser.");

            // Foreground observations supplement request-filter alerts for cached
            // pages and browsers that override the Windows proxy configuration.
            var website = BrowserUrlCollector.TryGetForegroundWebsite();
            if (website is not { Status: BrowserMonitoringStatus.Captured, Domain: not null }) return;
            var matchingWebsite = websiteRules.Where(r => PolicyPatternMatcher.MatchesDomain(website.Domain, r.Target))
                .OrderByDescending(r => r.Target.Count(c => c != '*')).ThenByDescending(r => r.Mode == "Allow").FirstOrDefault();
            if (matchingWebsite is not null && matchingWebsite.Mode != "Allow")
                await ReportViolation("Website", website.Domain, website.Browser, kill: false, token, reportedTarget: website.Domain);
            else if (websiteRules.Any(rule => rule.Mode == "Allow") && matchingWebsite is null)
                await ReportViolation("Website", website.Domain, website.Browser, kill: false, token, reportedTarget: website.Domain);
        }

        private async Task HandleViolation(RestrictionRuleMessage rule, string app, string processName, CancellationToken token)
        {
            // Kill the offending process for blocked applications/games
            bool kill = rule.RuleType == "Application";
            // Persist only a normalized process name or the matched rule target, never raw window titles.
            var reportedTarget = rule.RuleType == "Website" ? rule.Target : processName;
            await ReportViolation(rule.RuleType, app, processName, kill, token, reportedTarget);
        }

        private async Task ReportViolation(string targetType, string app, string processName, bool kill, CancellationToken token, string? reportedTarget = null, string? outcome = null, bool notifyStudent = true)
        {
            if (kill)
            {
                var closed = false;
                var failed = false;
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            if (process.HasExited) continue;
                            process.Kill();
                            closed = true;
                        }
                        catch (InvalidOperationException) { /* Process already exited. */ }
                        catch (System.ComponentModel.Win32Exception) { failed = true; }
                    }
                }
                outcome = failed ? "Windows did not allow CAMS to close every matching process. Ask your teacher for help."
                    : closed ? "The restricted application was closed." : "The restricted application is no longer running.";
            }

            // Throttle alerts only. Reopened apps/tabs must still be closed every pass.
            if (!_telemetry.ShouldReportInfraction(targetType, app, DateTime.UtcNow)) return;
            outcome ??= "This address is restricted. If it is still visible, restart your browser using Windows proxy settings or use the CAMS browser.";

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

            // Denied background resources are recorded without opening a popup
            // per resource. The browser already displays the connection denial.
            if (notifyStudent)
                this.Invoke(() => ShowPopup("Restricted Activity Detected", "Restricted activity",
                    $"'{app}' is restricted during laboratory sessions. {outcome}",
                    true));
        }

        private BrowserWebsiteObservation? _lastForegroundWebsite;
        // Not readonly: logging out disposes it, and the next student needs a live one.
        private ManagedBrowserCollector _managedBrowserCollector = CreateManagedBrowserCollector();
        private WebsiteRestrictionProxy? _websiteProxy;
        private WindowsSessionProxy? _windowsProxy;

        private void StartWebsiteFiltering(Uri server)
        {
            var proxy = new WebsiteRestrictionProxy();
            WindowsSessionProxy? windowsProxy = null;
            try
            {
                proxy.UpdateRules(Volatile.Read(ref _restrictionRules));
                proxy.Start();
                windowsProxy = new WindowsSessionProxy(proxy.Port, server);
                _managedBrowserCollector.ConfigureWebsiteProxy(proxy.Port);
                _websiteProxy = proxy;
                _windowsProxy = windowsProxy;
            }
            catch
            {
                try { windowsProxy?.Dispose(); }
                finally { proxy.Dispose(); }
                throw;
            }
        }

        private void StopWebsiteFiltering(bool notifyStudent = true)
        {
            try
            {
                _windowsProxy?.Dispose();
                _windowsProxy = null;
            }
            catch (Exception ex)
            {
                if (notifyStudent)
                    ShowMessage("Restore browser connection settings",
                        $"CAMS could not restore the previous Windows proxy settings. Restart CAMS to retry recovery.\n\n{ex.Message}",
                        DialogTone.Danger);
            }
            finally
            {
                _websiteProxy?.Dispose();
                _websiteProxy = null;
            }
        }

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

                        if (_telemetry.ShouldReportIdle(isIdle))
                        {
                            await _hubClient.ReportIdleStatusAsync(new IdleStatusMessage(
                                ConnectionId: "",
                                StudentId: "",
                                PcName: Environment.MachineName,
                                IsIdle: isIdle,
                                Timestamp: DateTime.UtcNow));
                        }

                        if (_telemetry.ShouldReportActiveApp(DateTime.UtcNow))
                        {
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
                                    _telemetry.ShouldReportWebsite(website.Browser, website.Domain))
                                {
                                    await _hubClient.ReportWebsiteActivityAsync(new WebsiteActivityMessage(
                                        "", "", Environment.MachineName, website.Domain, website.Browser, DateTime.UtcNow));
                                }
                                // Clearing the remembered site means returning to it
                                // after leaving reports again.
                                if (_lastForegroundWebsite is null) _telemetry.ShouldReportWebsite(null, null);
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
                if (!_telemetry.ShouldReportBrowserStatus(status.Identity, signature)) continue;
                await _hubClient.ReportBrowserMonitoringStatusAsync(new BrowserMonitoringStatusMessage(
                    "", "", Environment.MachineName, status.Identity, mode, DateTime.UtcNow, detail));
            }

            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(() => lblBrowserState.Text = $"Browser monitoring: {string.Join(" | ", summaries)}");
        }

        // Was "managed"/"fallback"/"unavailable", which described the same
        // three states in words the portal does not use.
        private static string ModeLabel(BrowserMonitoringMode mode) => BrowserMonitoringLabels.For(mode);

        /// <summary>How long capture may keep failing before the student is told rather than left with a silent stream.</summary>
        private const int CaptureFailuresBeforeReporting = 20;

        private async Task ScreenCaptureLoop(CancellationToken token)
        {
            var consecutiveFailures = 0;

            while (_isStreaming && !token.IsCancellationRequested)
            {
                // Pace on elapsed time. Capture, JPEG encode, base64 and the send
                // are all serial, so delaying afterwards made the real interval
                // the frame's cost plus the delay - well over the 50 ms target.
                // Measuring the work and waiting only the remainder makes the
                // interval mean what it says, and costs nothing when a frame is
                // slower than the target.
                var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

                try
                {
                    // While the workstation is locked, Windows is showing the
                    // secure desktop and CopyFromScreen cannot see it. Nothing is
                    // sent, and the teacher's view reports that frames have
                    // stopped rather than leaving the last image looking live.
                    if (_hubClient != null && !_isLocked)
                    {
                        var frame = new ScreenFrameMessage(
                            _studentId,
                            Environment.MachineName,
                            _screenCaptureService.CaptureBase64(),
                            DateTime.UtcNow);

                        await _hubClient.SendScreenFrameAsync(frame);
                    }

                    if (consecutiveFailures >= CaptureFailuresBeforeReporting)
                    {
                        ReportCaptureRecovered();
                    }
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException)
                {
                    return;   // Streaming was stopped; not a failure.
                }
                catch (Exception ex)
                {
                    // A single dropped frame is normal and not worth mentioning.
                    // A run of them is not: the stream has stopped without the
                    // student or the teacher being told, which is what used to
                    // happen behind a bare catch.
                    consecutiveFailures++;
                    if (consecutiveFailures == CaptureFailuresBeforeReporting)
                    {
                        ReportCaptureStalled(ex);
                    }
                }

                var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
                    * 1000d / System.Diagnostics.Stopwatch.Frequency;
                var remaining = (int)Math.Max(0, CaptureIntervalMs - elapsedMs);
                if (remaining > 0)
                {
                    await Task.Delay(remaining, token);
                }
            }
        }

        /// <summary>Target interval between frames. The real rate is lower whenever a frame costs more than this.</summary>
        private const int CaptureIntervalMs = 50;

        /// <summary>Surfaces a stalled capture on the status line instead of failing silently.</summary>
        private void ReportCaptureStalled(Exception cause)
        {
            if (_isClosing || IsDisposed) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (_isClosing || IsDisposed) return;
                    lblStatus.Text = "Status: Screen sharing interrupted";
                    lblStatus.ForeColor = StatusWarn;
                });
            }
            catch (InvalidOperationException)
            {
                // The window went away between the checks above and the marshal.
            }
            Debug.WriteLine($"[CAMS] Screen capture failing: {cause.Message}");
        }

        /// <summary>Puts the status line back once frames start flowing again.</summary>
        private void ReportCaptureRecovered()
        {
            if (_isClosing || IsDisposed) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (_isClosing || IsDisposed || _isLocked) return;
                    lblStatus.Text = "Status: Connected & Streaming";
                    lblStatus.ForeColor = StatusOk;
                });
            }
            catch (InvalidOperationException)
            {
                // The window went away between the checks above and the marshal.
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

        /// <param name="manual">The student ended it, rather than the teacher.</param>
        /// <param name="quit">
        /// Whether to close the application afterwards. A student logging out goes
        /// back to the sign-in gate instead: quitting would take the gate, the
        /// desktop shield and the website filter down with it and leave the
        /// workstation open until the next logon. Teacher-driven ends still quit,
        /// because they are followed by a restart that brings the gate back.
        /// </param>
        private async Task ForceLogout(bool manual, bool quit = true)
        {
            _isClosing = true;
            _sessionPaused = false;
            _sessionScreen.Hide();
            _isStreaming = false;
            _streamCts?.Cancel();
            _countdownTimer.Stop();
            _managedBrowserCollector.Dispose();
            StopWebsiteFiltering();
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
                ShowMessage(
                    "Session ended",
                    "Your teacher ended this session.\n\nCAMS will close now. Sign in again when your teacher starts the next session.",
                    DialogTone.Info);
            }

            if (quit)
            {
                Application.Exit();
                return;
            }

            // The callers are fire-and-forget, so a throw here would be swallowed
            // and leave the client hidden in the tray with no gate and no shield -
            // an unguarded machine with an invisible client, the worst state this
            // could fail into. Quitting instead is recoverable: the installer's
            // auto-start brings the gate back at the next logon.
            try
            {
                ReturnToSignIn();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAMS] Could not return to the sign-in gate: {ex.Message}");
                Application.Exit();
            }
        }

        /// <summary>
        /// Puts the client back where it started: the sign-in gate, top-most and
        /// centred, with the desktop shield up again. Everything the session owned
        /// is reset here, because the next student signs in through the same
        /// instance rather than a fresh process.
        /// </summary>
        private void ReturnToSignIn()
        {
            if (IsDisposed) return;      // the window went away mid-logout
            _isClosing = false;          // the teardown is over, guards are live again
            _streamCts = null;
            _studentId = "";
            _studentName = "";
            _session.End();
            _telemetry.Reset();
            _sessionPaused = false;
            _restartRequested = false;
            _trayNoticeShown = false;    // the next student gets the notice too
            if (_tray is not null) _tray.CanLogOut = false;

            // Disposed with the session above; the next one needs a live collector.
            _managedBrowserCollector = CreateManagedBrowserCollector();

            BuildUi();
            TopMost = true;
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            CentreOnScreen();
            ShowDesktopShield();
            Activate();
            BringToFront();
        }

        private void ShowBroadcast(BroadcastMessage msg)
        {
            if (_sessionPaused || _restartRequested) return;
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

        /// <summary>How serious a client message is.</summary>
        private enum DialogTone { Info, Warning, Danger }

        /// <summary>
        /// Rounds a control's corners. --radius-lg is 16px for cards and modals in
        /// the design system; WinForms has no radius property, so the shape is a
        /// clipping region.
        /// </summary>
        private static void RoundCorners(Control control, int radius)
        {
            var r = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var w = control.Width;
            var h = control.Height;
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(w - r - 1, 0, r, r, 270, 90);
            path.AddArc(w - r - 1, h - r - 1, r, r, 0, 90);
            path.AddArc(0, h - r - 1, r, r, 90, 90);
            path.CloseFigure();
            control.Region = new Region(path);
        }

        /// <summary>
        /// A pill button, the shape the design system uses everywhere outside compact
        /// table rows. Primary is always emerald: "one primary action per task area -
        /// if two things are emerald, one of them is wrong". Secondary is the neutral
        /// outline used for cancel and back.
        /// </summary>
        private static Button PillButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand,
                AutoSize = false,
                Height = 40,
                Margin = new Padding(8, 0, 0, 0),
                BackColor = primary ? BrandEmerald : SurfaceCard,
                ForeColor = primary ? Color.White : Color.FromArgb(68, 64, 60)
            };
            button.Width = Math.Max(116, TextRenderer.MeasureText(text, button.Font).Width + 40);
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = primary ? BrandEmerald : Color.FromArgb(218, 212, 201);
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(19, 107, 49)      // --accent-emerald-hover
                : Color.FromArgb(246, 243, 237);
            button.HandleCreated += (_, _) => RoundCorners(button, button.Height / 2);
            return button;
        }

        /// <summary>
        /// The chrome every CAMS client dialog shares, built to match the portal's
        /// themed modal: a brand gradient header, a body that opens with a severity
        /// pill, and a footer divided by a hairline.
        ///
        /// The header is the brand gradient whatever the severity. That is the
        /// portal's rule and it matters: severity travels in the pill, which carries
        /// a word as well as a colour, because "status is never carried by colour
        /// alone" - a red header alone says nothing to someone who cannot see red.
        /// </summary>
        private static Form BuildDialogShell(string heading, string message, DialogTone tone, out FlowLayoutPanel footer, bool topMost = false)
        {
            const int width = 470;
            const int pad = 22;          // --space-5, the modal body padding
            const int headerHeight = 64;

            var (pillText, pillInk, pillBack) = tone switch
            {
                DialogTone.Danger => ("Problem", Color.FromArgb(185, 28, 28), Color.FromArgb(253, 236, 236)),
                DialogTone.Warning => ("Check this", Color.FromArgb(180, 83, 9), Color.FromArgb(253, 243, 231)),
                _ => ("Information", Color.FromArgb(138, 97, 23), Color.FromArgb(251, 243, 227))
            };

            var dialog = new Form
            {
                Text = heading,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,   // the header below is the title bar
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = SurfaceCard,
                Font = new Font("Segoe UI", 9.75f),
                // Must match the owner. A top-most window outranks every ordinary
                // one - including its own modal child - so while the sign-in guard
                // holds the main window top-most, a plain dialog is drawn behind it
                // and the student sees a frozen screen with the message hidden.
                TopMost = topMost
            };

            // Header: linear-gradient(135deg, --sidebar-bg, --sidebar-active-bg)
            var header = new Panel { Dock = DockStyle.Top, Height = headerHeight };
            header.Paint += (_, pe) =>
            {
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    header.ClientRectangle, BrandDark, Color.FromArgb(29, 92, 46), 135f);
                pe.Graphics.FillRectangle(brush, header.ClientRectangle);
            };
            header.Controls.Add(new Label
            {
                Text = heading,
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                MaximumSize = new Size(width - (pad * 2), 0),
                Location = new Point(pad, 19)
            });

            // Severity pill: colour and a word, never colour on its own.
            var pill = new Label
            {
                Text = pillText,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = pillInk,
                BackColor = pillBack,
                AutoSize = false,
                Height = 24,
                Width = TextRenderer.MeasureText(pillText, new Font("Segoe UI", 8.5f, FontStyle.Bold)).Width + 26,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(pad, headerHeight + pad)
            };
            pill.HandleCreated += (_, _) => RoundCorners(pill, pill.Height / 2);

            var body = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = TextMain,
                AutoSize = true,
                MaximumSize = new Size(width - (pad * 2), 0),
                Location = new Point(pad, headerHeight + pad + 24 + 14)
            };

            var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(243, 239, 232) };
            footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 68,
                FlowDirection = FlowDirection.RightToLeft,   // first added sits rightmost
                BackColor = SurfaceCard,
                Padding = new Padding(pad, 14, pad, 16)
            };

            dialog.Controls.Add(pill);
            dialog.Controls.Add(body);
            dialog.Controls.Add(footer);
            dialog.Controls.Add(divider);
            dialog.Controls.Add(header);
            dialog.ClientSize = new Size(
                width,
                headerHeight + pad + 24 + 14 + Math.Max(40, body.Height) + 18 + 1 + 68);
            dialog.HandleCreated += (_, _) => RoundCorners(dialog, 16);   // --radius-lg
            CenterDialogOnScreen(dialog);
            return dialog;
        }

        private static void CenterDialogOnScreen(Form dialog)
        {
            // Capture the active monitor before the dialog takes focus. This also
            // works for modeless alerts while the main client is hidden in the tray.
            var foreground = NativeMethods.GetForegroundWindow();
            var screen = foreground == IntPtr.Zero
                ? Screen.FromPoint(Cursor.Position)
                : Screen.FromHandle(foreground);
            dialog.StartPosition = FormStartPosition.Manual;

            void Center()
            {
                var area = screen.WorkingArea;
                dialog.Location = new Point(
                    area.Left + Math.Max(0, (area.Width - dialog.Width) / 2),
                    area.Top + Math.Max(0, (area.Height - dialog.Height) / 2));
            }

            Center();
            // Repeat after Windows has applied the monitor's DPI and final layout.
            dialog.Load += (_, _) => Center();
            dialog.Shown += (_, _) => Center();
        }

        internal static void ShowStartupMessage(string message, bool error = false)
        {
            using var dialog = BuildDialogShell("CAMS", message,
                error ? DialogTone.Danger : DialogTone.Info, out var footer, topMost: true);
            var ok = PillButton("OK", primary: true);
            ok.DialogResult = DialogResult.OK;
            footer.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.CancelButton = ok;
            dialog.ShowDialog();
        }

        /// <summary>
        /// The client's message dialog, in place of the system MessageBox. Returns
        /// the same DialogResult values a MessageBox would, so call sites keep their
        /// logic and only gain buttons that say what they actually do.
        /// </summary>
        private DialogResult ShowMessage(
            string heading,
            string message,
            DialogTone tone = DialogTone.Info,
            MessageBoxButtons buttons = MessageBoxButtons.OK,
            string? affirmative = null,
            string? dismissive = null)
        {
            var dialog = BuildDialogShell(heading, message, tone, out var footer, topMost: this.TopMost);

            Button Action(string text, DialogResult value, bool primary)
            {
                var button = PillButton(text, primary);
                button.Click += (_, _) => { dialog.DialogResult = value; };
                return button;
            }

            Button accept, cancel;
            switch (buttons)
            {
                case MessageBoxButtons.YesNo:
                    accept = Action(affirmative ?? "Yes", DialogResult.Yes, primary: true);
                    cancel = Action(dismissive ?? "No", DialogResult.No, primary: false);
                    break;
                case MessageBoxButtons.RetryCancel:
                    accept = Action(affirmative ?? "Retry", DialogResult.Retry, primary: true);
                    cancel = Action(dismissive ?? "Cancel", DialogResult.Cancel, primary: false);
                    break;
                default:
                    accept = Action(affirmative ?? "OK", DialogResult.OK, primary: true);
                    cancel = accept;
                    break;
            }

            footer.Controls.Add(accept);
            if (!ReferenceEquals(cancel, accept)) footer.Controls.Add(cancel);
            dialog.AcceptButton = accept;
            dialog.CancelButton = cancel;   // Esc does the safe thing

            // A dialog of ours holding focus is not the student escaping the gate,
            // so the focus guard stands down while this is up.
            using (dialog)
                return dialog.ShowDialog(IsDisposed ? null : this);
        }

        private void ShowPopup(string title, string heading, string message, bool warning)
        {
            if (_sessionScreen.IsVisible)
            {
                _sessionScreen.ShowNotice($"{(string.IsNullOrWhiteSpace(heading) ? title : heading)}: {message}");
                return;
            }
            var dialog = BuildDialogShell(heading, message, warning ? DialogTone.Danger : DialogTone.Info, out var footer);
            dialog.Text = title;
            dialog.TopMost = true;

            var ok = PillButton("I understand", primary: true);
            ok.Click += (_, _) => dialog.Close();
            footer.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.Show(this);
        }


        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            // Do not delay a Windows restart with an asynchronous logout dialog.
            if (e.CloseReason == CloseReason.WindowsShutDown) _isClosing = true;
            if (!_isClosing && (_sessionPaused || _restartRequested) && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            // Before sign-in the window cannot be dismissed at all: no close, no
            // tray. The only way out is the tray icon's Exit, which sets
            // _exitRequested and so falls through to the teardown below.
            if (!_exitRequested && !_isClosing && _hubClient is null &&
                e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }

            // Once signed in, closing the window with the X (or Alt+F4) hides to
            // the tray and keeps the session running. A real quit comes only from
            // the tray's Exit, or from Windows shutting down.
            if (!_exitRequested && !_isClosing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

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
            StopWebsiteFiltering(notifyStudent: e.CloseReason != CloseReason.WindowsShutDown);
            _sessionScreen.Dispose();
            CloseBroadcast();
            HideDesktopShield();
            _tray?.Dispose();
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

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        // Windows refuses SetForegroundWindow from a process that did not receive
        // the last input, which is exactly our situation. Briefly sharing an input
        // queue with the thread that owns the foreground lifts that refusal - the
        // long-standing way to do this, and the only one that works.
        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
