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
        private Form? _passwordDialog;
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
        private static readonly Color BrandDark = Color.FromArgb(40, 103, 68);     // --sidebar-bg      #286744
        private static readonly Color BrandDarker = Color.FromArgb(29, 80, 52);    // --accent-emerald-hover #1D5034
        private static readonly Color BrandEmerald = Color.FromArgb(40, 103, 68);  // --accent-emerald  #286744
        private static readonly Color BrandMint = Color.FromArgb(220, 234, 221);   // --sidebar-ink     #DCEADD, 5.35:1 on the brand bar
        private static readonly Color SurfaceBody = Color.FromArgb(246, 248, 244); // --body-bg         #F6F8F4
        private static readonly Color SurfaceCard = Color.White;                   // --card-bg         #FFFFFF
        private static readonly Color BorderSubtle = Color.FromArgb(223, 229, 220);// --card-border     #DFE5DC
        private static readonly Color TextMain = Color.FromArgb(38, 61, 48);       // --text-main       #263D30
        private static readonly Color TextMuted = Color.FromArgb(104, 119, 108);   // --text-muted      #68776C
        private static readonly Color StatusOk = Color.FromArgb(47, 125, 79);      // --cams-success    #2F7D4F, 4.76:1 on white
        private static readonly Color StatusWarn = Color.FromArgb(180, 83, 9);     // --cams-warning    #B45309, 5.02:1 on white
        private static readonly Color StatusDanger = Color.FromArgb(185, 28, 28);  // --cams-danger     #B91C1C, 6.47:1 on white

        // Variants for text sitting on the dark brand bar, where the on-white
        // status colours above would not meet a readable contrast. Agent only;
        // the portal has no dark surface carrying status text.
        private static readonly Color OnDarkStrong = Color.White;                  //                   #FFFFFF, 13.4:1 on the brand bar
        private static readonly Color OnDarkMuted = Color.FromArgb(195, 218, 200); // --sidebar-ink-muted #C3DAC8, 4.50:1
        private static readonly Color OnDarkWarn = Color.FromArgb(252, 211, 77);   //                   #FCD34D, 8.15:1
        private static readonly Color OnDarkDanger = Color.FromArgb(254, 202, 202);//                   #FECACA, 4.60:1

        // Sign-in screen, taken from the approved "CAMS Portal" mockup. A deeper
        // green than the portal tokens above, on a pale mint header and footer.
        private static readonly Color SignInTint = Color.FromArgb(235, 243, 236);   //                   #EBF3EC header and footer
        private static readonly Color SignInInk = Color.FromArgb(5, 70, 45);        //                   #05462D title, captions, icons
        private static readonly Color SignInButton = Color.FromArgb(24, 92, 55);    //                   #185C37, 7.6:1 white text
        private static readonly Color SignInButtonHover = Color.FromArgb(17, 72, 42);//                  #11482A
        private static readonly Color SignInMuted = Color.FromArgb(85, 107, 96);    //                   #556B60, 5.0:1 on the tint
        private static readonly Color SignInDot = Color.FromArgb(119, 156, 140);    //                   #779C8C, decorative
        private static readonly Color SignInFieldBorder = Color.FromArgb(137, 165, 149); //               #89A595

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

        /// <summary>The CAMS mark from inside the assembly; see <see cref="BrandIcon"/>.</summary>
        private static Icon? LoadBrandIcon(Size? size = null) => BrandIcon.Load(size);

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
            // The shield is ours too, but it is only the backdrop: if it ever ends
            // up with the foreground, the sign-in form takes it straight back, or
            // the student is left typing into a dimmed screen that ignores them.
            var ownerThread = NativeMethods.GetWindowThreadProcessId(foreground, out var ownerPid);
            if (ownerPid == (uint)Environment.ProcessId)
            {
                if (_shields.Any(s => s.IsHandleCreated && s.Handle == foreground)) Activate();
                return;
            }

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

        /// <summary>
        /// The dimmed backdrop. It can never become the active window, so a click
        /// on it leaves the keyboard in the sign-in form. Before this, a click
        /// outside the form activated the shield, the focus guard saw a window of
        /// our own and stood down, and the student had to Alt+Tab or press the
        /// taskbar icon to get back to the Username box.
        /// </summary>
        private sealed class ShieldForm : Form
        {
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int WS_EX_TOOLWINDOW = 0x00000080;   // and not an Alt+Tab entry

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    return cp;
                }
            }
        }

        private void ShowDesktopShield()
        {
            if (_shields.Count > 0 || _hubClient is not null || _isClosing) return;
            foreach (var screen in Screen.AllScreens)
            {
                var shield = new ShieldForm
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

        /// <summary>
        /// Pixels at 100% scaling, converted for this display. The sign-in screen
        /// places its parts at fixed positions, and fonts grow with the display
        /// scale, so every measurement goes through here or the text would outgrow
        /// its boxes at 125% and 150%.
        /// </summary>
        private int Dp(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96f);

        /// <summary>A sign-in text box in its rounded outline. The boxes are
        /// deliberately large: the people typing into them are primary school
        /// pupils, often hunting for keys one at a time.</summary>
        private RoundedField SignInField(TextBox box, int rightInset) =>
            new(box)
            {
                BackColor = SurfaceCard,
                BorderColor = SignInFieldBorder,
                FocusColor = SignInButton,
                Radius = Dp(8),
                Padding = new Padding(Dp(12), 0, rightInset, 0),
                Height = Dp(42)
            };

        /// <summary>
        /// A password box with the eye that shows what was typed, so a pupil can
        /// check their typing before pressing the button rather than finding out
        /// from a refusal.
        /// </summary>
        private RoundedField PasswordField(TextBox box, int width)
        {
            int eyeSize = Dp(20);
            var field = SignInField(box, Dp(12) + eyeSize + Dp(12));
            field.Width = width;
            var eye = new LineIcon
            {
                Glyph = LineGlyph.Eye,
                ForeColor = SignInInk,
                BackColor = SurfaceCard,
                Size = new Size(eyeSize, eyeSize),
                Location = new Point(width - Dp(12) - eyeSize, (field.Height - eyeSize) / 2),
                Cursor = Cursors.Hand,
                AccessibleName = "Show password",
                AccessibleRole = AccessibleRole.PushButton
            };
            eye.Click += (_, _) =>
            {
                bool reveal = box.UseSystemPasswordChar;
                box.UseSystemPasswordChar = !reveal;
                eye.Glyph = reveal ? LineGlyph.EyeOff : LineGlyph.Eye;
                eye.AccessibleName = reveal ? "Hide password" : "Show password";
                box.Focus();
            };
            field.Controls.Add(eye);
            return field;
        }

        /// <summary>The icon and caption that sit above a sign-in field.</summary>
        private Control[] SignInCaption(LineGlyph glyph, string text, int left, int top)
        {
            int iconSize = Dp(18);
            var icon = new LineIcon
            {
                Glyph = glyph,
                ForeColor = SignInInk,
                Bounds = new Rectangle(left, top, iconSize, iconSize)
            };
            var caption = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = SignInInk,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            caption.Location = new Point(icon.Right + Dp(8), top + (iconSize - caption.PreferredHeight) / 2);
            return new Control[] { icon, caption };
        }

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
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = SurfaceCard;
            Font = new Font("Segoe UI", 9.75f);

            int width = Dp(440);
            int inset = Dp(40);
            int fieldWidth = width - inset * 2;

            // --- Header: school seal, product name, and who this client is for --
            int headerHeight = Dp(136);
            var header = new Panel { Dock = DockStyle.Top, Height = headerHeight, BackColor = SignInTint };

            int logoSize = Dp(84);
            var logo = new PictureBox
            {
                Size = new Size(logoSize, logoSize),
                Location = new Point(Dp(36), (headerHeight - logoSize) / 2),
                BackColor = SignInTint,
                Image = SchoolLogo.Load(logoSize),
                AccessibleName = "Pardo Elementary School seal"
            };

            var lblBrand = new Label
            {
                Text = "CAMS Portal",
                AutoSize = true,
                ForeColor = SignInInk,
                Font = new Font("Segoe UI", 17f, FontStyle.Bold)
            };

            var taglineFont = new Font("Segoe UI", 9f);
            Label TaglinePart(string text) => new()
            {
                Text = text,
                AutoSize = true,
                ForeColor = SignInMuted,
                Font = taglineFont,
                Margin = Padding.Empty
            };
            var lblClient = TaglinePart("Student Client");
            int dotSize = Dp(6);
            var dot = new Panel
            {
                Size = new Size(dotSize, dotSize),
                Margin = new Padding(Dp(9), (lblClient.PreferredHeight - dotSize) / 2, Dp(9), 0),
                BackColor = SignInTint
            };
            dot.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(SignInDot);
                e.Graphics.FillEllipse(brush, 0, 0, dotSize - 1, dotSize - 1);
            };
            var tagline = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                BackColor = SignInTint,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            tagline.Controls.AddRange(new Control[] { lblClient, dot, TaglinePart("Pardo Elementary School") });

            // The title and tagline as one block, centred on the seal.
            int textLeft = logo.Right + Dp(22);
            int gap = Dp(2);
            int blockTop = (headerHeight - (lblBrand.PreferredHeight + gap + lblClient.PreferredHeight)) / 2;
            lblBrand.Location = new Point(textLeft, blockTop);
            tagline.Location = new Point(textLeft + Dp(2), blockTop + lblBrand.PreferredHeight + gap);
            header.Controls.AddRange(new Control[] { logo, lblBrand, tagline });

            // --- Body: the two fields and the Login button -----------------------
            // Fresh boxes every time the gate is built. They used to be reused, so
            // after a logout the next pupil at this machine found the previous
            // one's username and password already filled in.
            txtStudentId = new TextBox
            {
                Font = new Font("Segoe UI", 10f),
                ForeColor = TextMain,
                PlaceholderText = "Enter your username",
                TabIndex = 0
            };
            txtPassword = new TextBox
            {
                Font = new Font("Segoe UI", 10f),
                ForeColor = TextMain,
                PlaceholderText = "Enter your password",
                UseSystemPasswordChar = true,
                TabIndex = 0
            };

            var body = new Panel { Dock = DockStyle.Fill, BackColor = SurfaceCard };
            // A soft shadow where the header meets the white body.
            body.Paint += (_, e) =>
            {
                var strip = new Rectangle(0, 0, body.Width, Dp(8));
                using var shade = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, -1, body.Width, strip.Height + 2),
                    Color.FromArgb(26, SignInInk), Color.FromArgb(0, SignInInk), 90f);
                e.Graphics.FillRectangle(shade, strip);
            };

            int y = Dp(26);
            body.Controls.AddRange(SignInCaption(LineGlyph.Person, "Username", inset + Dp(4), y));
            y += Dp(26);
            var idField = SignInField(txtStudentId, Dp(12));
            idField.Location = new Point(inset, y);
            idField.Width = fieldWidth;
            idField.TabIndex = 0;
            y += idField.Height + Dp(20);

            body.Controls.AddRange(SignInCaption(LineGlyph.Lock, "Password", inset + Dp(4), y));
            y += Dp(26);
            var passwordField = PasswordField(txtPassword, fieldWidth);
            passwordField.Location = new Point(inset, y);
            passwordField.TabIndex = 1;
            y += passwordField.Height + Dp(28);

            btnLogin = new RoundedButton
            {
                Text = "Login",
                BackColor = SignInButton,
                HoverColor = SignInButtonHover,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Radius = Dp(8),
                Bounds = new Rectangle(inset, y, fieldWidth, Dp(46)),
                TabIndex = 2
            };
            btnLogin.Click += BtnLogin_Click;
            y += btnLogin.Height + Dp(38);

            body.Controls.AddRange(new Control[] { idField, passwordField, btnLogin });

            // --- Footer: connection status and where the account comes from -----
            int footerHeight = Dp(74);
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = footerHeight,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(inset, Dp(17), inset, 0),
                BackColor = SignInTint
            };
            lblStatus = new Label
            {
                Text = "Status: Not Connected",
                AutoSize = true,
                MaximumSize = new Size(fieldWidth, 0),
                ForeColor = SignInMuted,
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0, 0, 0, Dp(4))
            };
            var lblHint = new Label
            {
                Text = "Use the account issued by your teacher.",
                AutoSize = true,
                MaximumSize = new Size(fieldWidth, 0),
                ForeColor = SignInMuted,
                Font = new Font("Segoe UI", 9f),
                Margin = Padding.Empty
            };
            footer.Controls.AddRange(new Control[] { lblStatus, lblHint });

            ClientSize = new Size(width, headerHeight + y + footerHeight);

            // Fill order matters: the header and footer dock around the filled body.
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
            AcceptButton = btnLogin;
            ActiveControl = txtStudentId;
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
            lblBrowserState = new Label { Text = "Browser monitoring: waiting for a browser", ForeColor = TextMuted, Font = new Font("Segoe UI", 9.75f) };

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
            btnLogout.Size = new Size(150, 46);
            btnLogout.Margin = Padding.Empty;
            btnLogout.FlatAppearance.BorderSize = 0;
            btnLogout.FlatAppearance.MouseOverBackColor = BrandDarker;
            btnLogout.Click += async (_, _) => await ForceLogout(true, quit: false);

            // The one account task a student can do for themselves; the web
            // portal does not let students in.
            var btnPassword = BrandButton("Change password", SurfaceCard);
            btnPassword.ForeColor = BrandDark;
            btnPassword.Size = new Size(170, 46);
            btnPassword.Margin = new Padding(10, 0, 0, 0);
            btnPassword.FlatAppearance.BorderSize = 1;
            btnPassword.FlatAppearance.BorderColor = SignInFieldBorder;
            btnPassword.FlatAppearance.MouseOverBackColor = SignInTint;
            btnPassword.Click += (_, _) => ShowChangePasswordDialog();

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SurfaceBody
            };
            actions.Controls.AddRange(new Control[] { btnLogout, btnPassword });

            // Added last-to-first so docking stacks in the intended order.
            content.Controls.Add(lblInfo);
            content.Controls.Add(card);
            content.Controls.Add(actions);

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
                ShowMessage("Sign in", "Please enter your username and password.", DialogTone.Warning);
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
                hubClient.ForceLogoutRequested += () => this.Invoke(async () => await ForceLogout(false, quit: false,
                    gateStatus: "Status: Signed out by your teacher."));
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
                    "Check the username and password.\n\nThe account must be active, and this workstation must be free of another session.",
                    DialogTone.Danger, affirmative: "Try again");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // The server only lets students in while the teacher has a lab
                // session running. Nothing is wrong with what they typed.
                lblStatus.Text = "Status: No session running yet";
                lblStatus.ForeColor = StatusWarn;
                btnLogin.Enabled = true;
                ShowMessage(
                    "No session yet",
                    "Your teacher has not started the lab session yet.\n\nWait for your teacher to start it, then press Login again.",
                    DialogTone.Warning, affirmative: "OK");
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
            // No dialog: the workstation goes straight back behind the sign-in
            // gate and its desktop shield, which is the lock. It used to show two
            // stacked messages, quit, and fall back on the Windows lock screen -
            // which a pupil who knows the Windows password simply unlocks, into a
            // machine with CAMS no longer running.
            await ForceLogout(false, quit: false,
                gateStatus: "Status: Session ended. Wait for your teacher to start the next one.");
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
                await ReportViolation("Website", domain, token,
                    reportedTarget: domain, outcome: "CAMS denied the website connection.", notifyStudent: false);
            if (_isLocked) return;

            var rules = Volatile.Read(ref _restrictionRules);
            // Applications are monitored, never policed: CAMS does not close
            // anything on the desktop, and application rules raise no violation.
            // Only websites are enforced, by the session proxy and the
            // foreground check below.
            var websiteRules = rules.Where(r => r.RuleType == "Website").ToList();

            // Foreground observations supplement request-filter alerts for cached
            // pages and browsers that override the Windows proxy configuration.
            var website = BrowserUrlCollector.TryGetForegroundWebsite();
            if (website is not { Status: BrowserMonitoringStatus.Captured, Domain: not null }) return;
            // Only a rule that blocks makes this a violation; a site nobody wrote a
            // rule about is not one, even when allow rules exist.
            if (PolicyDecision.FindMatch(websiteRules, website.Domain, isDomain: true) is { } matchingWebsite &&
                matchingWebsite.Mode != "Allow")
                await ReportViolation("Website", website.Domain, token, reportedTarget: website.Domain);
        }

        private async Task ReportViolation(string targetType, string app, CancellationToken token, string? reportedTarget = null, string? outcome = null, bool notifyStudent = true)
        {
            // Throttle alerts only. A reopened tab must still be reported each pass.
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
                                var website = BrowserUrlCollector.TryGetForegroundWebsite();
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
            if (_hubClient == null || observation is null) return;

            // Only the browser in front is observed. CAMS used to launch its own
            // instrumented Chrome, Brave and Edge so it could read tabs in the
            // background, which opened browser windows nobody asked for. The
            // address bar of whichever browser the student chose is read instead.
            var detail = observation.Status == BrowserMonitoringStatus.Captured
                ? "Foreground URL captured"
                : "Foreground browser detected; URL unavailable";
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(() => lblBrowserState.Text = $"Browser monitoring: {observation.Browser}: {ModeLabel(observation.Mode)}");
            if (!_telemetry.ShouldReportBrowserStatus(observation.Browser, $"{observation.Mode}:{detail}")) return;
            await _hubClient.ReportBrowserMonitoringStatusAsync(new BrowserMonitoringStatusMessage(
                "", "", Environment.MachineName, observation.Browser, observation.Mode, DateTime.UtcNow, detail));
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
        /// Whether to close the application afterwards. Only a real Exit, or
        /// Windows shutting down, quits. Everything else - the student logging
        /// out, the teacher signing them out, the session ending - goes back to
        /// the sign-in gate, because quitting would take the gate, the desktop
        /// shield and the website filter down with it and leave the workstation
        /// open until the next logon.
        /// </param>
        /// <param name="gateStatus">What the gate's status line says afterwards.</param>
        private async Task ForceLogout(bool manual, bool quit = true, string? gateStatus = null)
        {
            // A password dialog must not outlive the session it belongs to.
            if (_passwordDialog is { IsDisposed: false } passwordDialog) passwordDialog.Close();
            _isClosing = true;
            _sessionPaused = false;
            _sessionScreen.Hide();
            _isStreaming = false;
            _streamCts?.Cancel();
            _countdownTimer.Stop();
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
                if (gateStatus is not null)
                {
                    lblStatus.Text = gateStatus;
                    lblStatus.ForeColor = StatusWarn;
                }
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

        /// <summary>Lets the signed-in student replace their own password.</summary>
        private void ShowChangePasswordDialog()
        {
            if (_hubClient is null || _passwordDialog is { IsDisposed: false }) return;

            var dialog = BuildChangePasswordDialog();
            _passwordDialog = dialog;
            DialogResult result;
            try
            {
                result = dialog.ShowDialog(this);
            }
            finally
            {
                _passwordDialog = null;
                dialog.Dispose();
            }

            if (result == DialogResult.OK && !IsDisposed && _hubClient is not null)
                ShowMessage("Password changed",
                    "Your password has been changed. Use your new password the next time you log in.");
        }

        /// <summary>
        /// The change-password dialog, built from the sign-in screen's parts - same
        /// account, same pupils typing. The rules are checked here first so a slip
        /// is caught at once; the server checks the current password and the rules
        /// again, and its answer is the one that counts. Closes itself with OK once
        /// the server has accepted the change.
        /// </summary>
        private Form BuildChangePasswordDialog()
        {
            int width = Dp(420);
            int inset = Dp(28);
            int fieldWidth = width - inset * 2;

            var dialog = new Form
            {
                Text = "Change password",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowIcon = false,
                ShowInTaskbar = false,
                BackColor = SurfaceCard,
                Font = new Font("Segoe UI", 9.75f),
                TopMost = TopMost
            };

            // --- Header: what this is, and the one rule to know -------------------
            int headerHeight = Dp(82);
            var header = new Panel { Dock = DockStyle.Top, Height = headerHeight, BackColor = SignInTint };
            int iconSize = Dp(28);
            var icon = new LineIcon
            {
                Glyph = LineGlyph.Lock,
                ForeColor = SignInInk,
                Bounds = new Rectangle(inset, (headerHeight - iconSize) / 2, iconSize, iconSize)
            };
            var title = new Label
            {
                Text = "Change your password",
                AutoSize = true,
                ForeColor = SignInInk,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold)
            };
            var subtitle = new Label
            {
                Text = $"Use at least {StudentPasswordRules.MinimumLength} characters.",
                AutoSize = true,
                MaximumSize = new Size(width - inset - (icon.Right + Dp(14)), 0),
                ForeColor = SignInMuted,
                Font = new Font("Segoe UI", 9f)
            };
            int textLeft = icon.Right + Dp(14);
            int blockTop = (headerHeight - (title.PreferredHeight + Dp(2) + subtitle.PreferredHeight)) / 2;
            title.Location = new Point(textLeft, blockTop);
            subtitle.Location = new Point(textLeft + Dp(1), blockTop + title.PreferredHeight + Dp(2));
            header.Controls.AddRange(new Control[] { icon, title, subtitle });

            // --- Body: current, new, and the new one again -------------------------
            TextBox PasswordBox(string placeholder) => new()
            {
                Font = new Font("Segoe UI", 10f),
                ForeColor = TextMain,
                PlaceholderText = placeholder,
                UseSystemPasswordChar = true,
                MaxLength = StudentPasswordRules.MaximumLength
            };
            var txtCurrent = PasswordBox("Enter your current password");
            var txtNew = PasswordBox("Enter a new password");
            var txtConfirm = PasswordBox("Type the new password again");

            var body = new Panel { Dock = DockStyle.Fill, BackColor = SurfaceCard, TabIndex = 0 };
            int y = Dp(22);
            void AddRow(string caption, TextBox box, int tabIndex)
            {
                body.Controls.AddRange(SignInCaption(LineGlyph.Lock, caption, inset + Dp(4), y));
                y += Dp(26);
                var field = PasswordField(box, fieldWidth);
                field.Location = new Point(inset, y);
                field.TabIndex = tabIndex;
                body.Controls.Add(field);
                y += field.Height + Dp(16);
            }
            AddRow("Current password", txtCurrent, 0);
            AddRow("New password", txtNew, 1);
            AddRow("Confirm new password", txtConfirm, 2);

            // Space kept for a line or two of explanation, so the dialog does not
            // jump when something needs fixing.
            var lblProblem = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(fieldWidth, 0),
                ForeColor = StatusDanger,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(inset, y - Dp(4))
            };
            body.Controls.Add(lblProblem);
            y += Dp(40);

            // --- Footer: Cancel, and the one primary action ------------------------
            int footerHeight = Dp(74);
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = footerHeight,
                FlowDirection = FlowDirection.RightToLeft,   // first added sits rightmost
                WrapContents = false,
                Padding = new Padding(inset, Dp(15), inset, 0),
                BackColor = SignInTint,
                TabIndex = 1
            };
            var btnSave = new RoundedButton
            {
                Text = "Change password",
                BackColor = SignInButton,
                HoverColor = SignInButtonHover,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Radius = Dp(8),
                Size = new Size(Dp(178), Dp(44)),
                Margin = new Padding(Dp(10), 0, 0, 0),
                TabIndex = 0
            };
            var btnCancel = new RoundedButton
            {
                Text = "Cancel",
                BackColor = SurfaceCard,
                HoverColor = Color.FromArgb(244, 249, 245),
                BorderColor = SignInFieldBorder,
                ForeColor = SignInInk,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Radius = Dp(8),
                Size = new Size(Dp(112), Dp(44)),
                Margin = Padding.Empty,
                DialogResult = DialogResult.Cancel,
                TabIndex = 1
            };
            footer.Controls.AddRange(new Control[] { btnSave, btnCancel });

            dialog.Controls.Add(body);
            dialog.Controls.Add(footer);
            dialog.Controls.Add(header);
            dialog.ClientSize = new Size(width, headerHeight + y + footerHeight);
            dialog.AcceptButton = btnSave;    // Enter changes it
            dialog.CancelButton = btnCancel;  // Esc does the safe thing
            dialog.ActiveControl = txtCurrent;
            CenterDialogOnScreen(dialog);

            void ShowProblem(string message, TextBox? fix)
            {
                if (dialog.IsDisposed) return;
                lblProblem.Text = message;
                if (fix is null) return;
                fix.Focus();
                fix.SelectAll();
            }

            void SetBusy(bool busy)
            {
                if (dialog.IsDisposed) return;
                foreach (var box in new[] { txtCurrent, txtNew, txtConfirm }) box.ReadOnly = busy;
                btnSave.Enabled = !busy;
                btnCancel.Enabled = !busy;
                btnSave.Text = busy ? "Saving…" : "Change password";
                dialog.UseWaitCursor = busy;
            }

            btnSave.Click += async (_, _) =>
            {
                string current = txtCurrent.Text, next = txtNew.Text, confirm = txtConfirm.Text;
                if (current.Length == 0)
                {
                    ShowProblem("Enter your current password.", txtCurrent);
                    return;
                }
                if (next.Length < StudentPasswordRules.MinimumLength)
                {
                    ShowProblem($"Your new password must be at least {StudentPasswordRules.MinimumLength} characters long.", txtNew);
                    return;
                }
                if (next == current)
                {
                    ShowProblem("Your new password must be different from your current one.", txtNew);
                    return;
                }
                if (next != confirm)
                {
                    ShowProblem("The new passwords do not match.", txtConfirm);
                    return;
                }

                var client = _hubClient;
                if (client is null)
                {
                    dialog.Close();
                    return;
                }

                lblProblem.Text = "";
                SetBusy(true);
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await client.ChangePasswordAsync(current, next, timeout.Token);
                    if (!dialog.IsDisposed) dialog.DialogResult = DialogResult.OK;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    SetBusy(false);
                    ShowProblem("Your sign-in has expired. Log out and log in again, then try once more.", null);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest)
                {
                    // The server's own words: a wrong current password, or a rule.
                    SetBusy(false);
                    ShowProblem(ex.Message,
                        ex.Message.Contains("current password", StringComparison.OrdinalIgnoreCase) ? txtCurrent : txtNew);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests)
                {
                    SetBusy(false);
                    ShowProblem(ex.Message, null);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
                {
                    SetBusy(false);
                    ShowProblem("CAMS could not change your password right now. Try again in a moment.", null);
                }
            };

            return dialog;
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
