using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Client.Services
{
    /// <summary>
    /// Owns the client's presence in the Windows notification area (the system
    /// tray): a visible icon, its right-click menu, and its click-to-restore
    /// behaviour.
    ///
    /// The point of the tray is to let the window get out of the way — minimised
    /// or "closed" — while the agent keeps running and the student stays
    /// connected. That is deliberate for a lab monitor, and it is done in the
    /// open: the icon is <b>always visible</b> the whole time the app runs, and
    /// its menu <b>always offers Exit</b>. This tucks the window away; it never
    /// hides the fact that CAMS is running from the person at the machine.
    ///
    /// The controller knows nothing about the form. It raises three events —
    /// <see cref="RestoreRequested"/>, <see cref="StatusRequested"/> and
    /// <see cref="ExitRequested"/> — and the owner decides what each means. All
    /// UI plumbing (the <see cref="NotifyIcon"/>, the menu, the generated icon)
    /// lives here so the form does not carry it.
    /// </summary>
    public sealed class TrayIconController : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _logout;
        private readonly Icon _icon;
        private bool _disposed;

        /// <summary>The user asked to bring the window back (menu, left-click or double-click).</summary>
        public event Action? RestoreRequested;

        /// <summary>The user asked what the agent is currently doing.</summary>
        public event Action? StatusRequested;

        /// <summary>The user asked to end their session from the tray.</summary>
        public event Action? LogoutRequested;

        /// <summary>The user asked to quit for real, not just hide.</summary>
        public event Action? ExitRequested;

        /// <summary>Offers Log out only once a student is actually signed in.</summary>
        public bool CanLogOut
        {
            get => _logout.Enabled;
            set => _logout.Enabled = value;
        }

        /// <summary>
        /// The tray menu opened or closed. The sign-in gate holds the foreground
        /// while nobody is signed in, and it has to stand down for this menu -
        /// otherwise the one way out of the gate would slam shut as it opened.
        /// </summary>
        public event Action? MenuOpened;
        public event Action? MenuClosed;

        /// <param name="appName">Shown as the icon's hover tooltip and menu header.</param>
        /// <param name="icon">An icon to use; when null a branded one is drawn at runtime.</param>
        public TrayIconController(string appName, Icon? icon = null)
        {
            _icon = icon ?? BuildBrandIcon();

            var restore = new ToolStripMenuItem("Open CAMS", null, (_, _) => RestoreRequested?.Invoke())
            {
                // Bold marks the default action — what a plain double-click does.
                Font = new Font(SystemFonts.MenuFont ?? new Font("Segoe UI", 9f), FontStyle.Bold)
            };
            var status = new ToolStripMenuItem("Check Status", null, (_, _) => StatusRequested?.Invoke());
            // Greyed out until a student signs in - there is nothing to log out of
            // before that, and an item that does nothing is worse than no item.
            _logout = new ToolStripMenuItem("Log out", null, (_, _) => LogoutRequested?.Invoke()) { Enabled = false };
            var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke());

            _menu = new ContextMenuStrip();
            _menu.Opened += (_, _) => MenuOpened?.Invoke();
            _menu.Closed += (_, _) => MenuClosed?.Invoke();
            _menu.Items.Add(restore);
            _menu.Items.Add(status);
            _menu.Items.Add(_logout);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exit);

            _notifyIcon = new NotifyIcon
            {
                Icon = _icon,
                Text = Clamp(appName),
                ContextMenuStrip = _menu,
                Visible = true
            };

            // Left single-click and double-click both restore. Restoring an
            // already-visible window is a no-op on the owner's side, so the two
            // firing together for one double-click does no harm.
            _notifyIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreRequested?.Invoke(); };
            _notifyIcon.DoubleClick += (_, _) => RestoreRequested?.Invoke();
        }

        /// <summary>Updates the hover tooltip to reflect the current state.</summary>
        public void SetTooltip(string text)
        {
            if (_disposed) return;
            _notifyIcon.Text = Clamp(text);
        }

        /// <summary>Pops a short balloon from the tray icon — used for status and the hidden notice.</summary>
        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int milliseconds = 4000)
        {
            if (_disposed) return;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(milliseconds);
        }

        /// <summary>Draws a small green CAMS emblem so the build needs no packaged .ico.</summary>
        private static Icon BuildBrandIcon()
        {
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                using var disc = new SolidBrush(Color.FromArgb(23, 128, 58)); // --accent-emerald
                g.FillEllipse(disc, 1, 1, 30, 30);

                using var font = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("C", font, Brushes.White, new RectangleF(0, 0, 32, 33), format);
            }

            // GetHicon hands back an unmanaged HICON we own. Clone into a managed
            // Icon so we can free the handle immediately and leak nothing.
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using var shared = Icon.FromHandle(handle);
                return (Icon)shared.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        // The tooltip is a fixed-size buffer; keep well within it rather than throw.
        private static string Clamp(string text) =>
            string.IsNullOrEmpty(text) ? "CAMS"
            : text.Length <= 63 ? text
            : text.Substring(0, 60) + "…";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
