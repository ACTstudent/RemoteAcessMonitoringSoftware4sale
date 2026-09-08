using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Client.Services;

/// <summary>A teacher-controlled screen over every monitor, with no student dismiss action.</summary>
public sealed class SessionScreenGuard : IDisposable
{
    private readonly List<SessionScreen> _screens = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private SessionInputGuard? _input;
    private string _heading = "Session paused";
    private string _message = "Please wait for your teacher to continue the session.";
    private string _detail = "";
    private string _notice = "";
    private string _displayLayout = "";
    private bool _disposed;

    public bool IsVisible => _screens.Count > 0;
    public string? InputGuardError => _input?.Error;

    public SessionScreenGuard() => _timer.Tick += (_, _) => MaintainScreens();

    public void Show(string heading, string message, string detail)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_heading != heading) _notice = "";
        _heading = heading;
        _message = message;
        _detail = detail;
        _input ??= new SessionInputGuard();
        MaintainScreens();
        _timer.Start();
    }

    public void ShowNotice(string message)
    {
        _notice = message;
        RefreshText();
    }

    private void MaintainScreens()
    {
        var displays = Screen.AllScreens;
        var layout = string.Join(";", displays.Select(screen => $"{screen.DeviceName}:{screen.Bounds}"));
        if (layout != _displayLayout || _screens.Count == 0)
        {
            var oldScreens = _screens.ToArray();
            _screens.Clear();
            foreach (var display in displays)
            {
                var screen = new SessionScreen { Bounds = display.Bounds };
                _screens.Add(screen);
                screen.Show();
            }
            foreach (var old in oldScreens) old.Dispose();
            _displayLayout = layout;
        }
        RefreshText();
        foreach (var screen in _screens)
        {
            if (!screen.Visible) screen.Show();
            screen.BringToFront();
        }
        // Input stays blocked even when Windows declines a foreground request.
        var active = _screens.FirstOrDefault(screen => screen.Bounds.Contains(Cursor.Position)) ?? _screens.FirstOrDefault();
        if (active is not null && Form.ActiveForm != active) active.Activate();
    }

    private void RefreshText()
    {
        var detail = InputGuardError is null ? _detail : "Tell your teacher: Windows could not enable the input guard.";
        foreach (var screen in _screens) screen.SetMessage(_heading, _message, detail, _notice);
    }

    public void Hide()
    {
        _timer.Stop();
        // Release input first, even when tearing down a screen fails.
        _input?.Dispose();
        _input = null;
        foreach (var screen in _screens) screen.Dispose();
        _screens.Clear();
        _displayLayout = "";
        _notice = "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hide();
        _timer.Dispose();
    }

    private sealed class SessionScreen : Form
    {
        private string _heading = "Session paused", _message = "", _detail = "", _notice = "";

        public SessionScreen()
        {
            Text = "CAMS — Teacher session control";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            ControlBox = false;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(15, 49, 33);
            AccessibleRole = AccessibleRole.Pane;
        }

        public void SetMessage(string heading, string message, string detail, string notice)
        {
            if ((_heading, _message, _detail, _notice) == (heading, message, detail, notice)) return;
            (_heading, _message, _detail, _notice) = (heading, message, detail, notice);
            AccessibleName = heading;
            AccessibleDescription = $"{message} {detail} {notice}";
            Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => true;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(13, 43, 30), Color.FromArgb(28, 91, 57), 35f);
            g.FillRectangle(background, ClientRectangle);
            var scale = Math.Min(DeviceDpi / 96f, Math.Min(ClientSize.Width / 760f, ClientSize.Height / 620f));
            int S(int value) => Math.Max(1, (int)(value * scale));
            var card = new Rectangle((Width - S(660)) / 2, (Height - S(440)) / 2, S(660), S(440));
            using var outline = RoundedRectangle(card, S(24));
            using var surface = new SolidBrush(Color.FromArgb(252, 253, 250));
            g.FillPath(surface, outline);
            var ink = Color.FromArgb(22, 64, 42);
            var muted = Color.FromArgb(93, 109, 99);
            void TextLine(string text, int y, int height, float size, FontStyle style, Color color)
            {
                using var font = new Font("Segoe UI", size * scale, style, GraphicsUnit.Pixel);
                TextRenderer.DrawText(g, text, font,
                    new Rectangle(card.Left + S(34), card.Top + S(y), card.Width - S(68), S(height)),
                    color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            TextLine("PARDO ELEMENTARY SCHOOL  •  CAMS", 24, 28, 13, FontStyle.Bold, muted);
            var icon = new Rectangle(card.Left + (card.Width - S(72)) / 2, card.Top + S(72), S(72), S(72));
            using var iconFill = new SolidBrush(Color.FromArgb(229, 242, 234));
            using var iconInk = new SolidBrush(ink);
            g.FillEllipse(iconFill, icon);
            g.FillRectangle(iconInk, icon.Left + S(24), icon.Top + S(21), S(8), S(30));
            g.FillRectangle(iconInk, icon.Left + S(40), icon.Top + S(21), S(8), S(30));
            TextLine(_heading, 160, 56, 34, FontStyle.Bold, ink);
            TextLine(_message, 225, 68, 19, FontStyle.Regular, ink);
            TextLine(_notice, 301, 60, 15, FontStyle.Regular, muted);
            TextLine(_detail, 375, 42, 14, FontStyle.Bold, muted);
        }

        private static GraphicsPath RoundedRectangle(Rectangle box, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Input hooks run on their own message loop, never on the busy capture/UI thread.</summary>
    private sealed class SessionInputGuard : IDisposable
    {
        private readonly TaskCompletionSource<string?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HookProc _callback;
        private readonly Thread _thread;
        private volatile bool _blocked = true;
        private Control? _dispatcher;
        private string? _error;
        public string? Error => Volatile.Read(ref _error);

        public SessionInputGuard()
        {
            _callback = FilterInput;
            _thread = new Thread(Run) { IsBackground = true, Name = "CAMS session input guard" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Task.GetAwaiter().GetResult();
        }

        private void Run()
        {
            IntPtr keyboard = IntPtr.Zero, mouse = IntPtr.Zero;
            try
            {
                using var dispatcher = new Control();
                _ = dispatcher.Handle;
                _dispatcher = dispatcher;
                ReleaseHeldInput();
                var module = GetModuleHandle(null);
                keyboard = SetWindowsHookEx(13, _callback, module, 0);
                var keyboardError = keyboard == IntPtr.Zero ? new Win32Exception(Marshal.GetLastWin32Error()).Message : null;
                mouse = SetWindowsHookEx(14, _callback, module, 0);
                var mouseError = mouse == IntPtr.Zero ? new Win32Exception(Marshal.GetLastWin32Error()).Message : null;
                Volatile.Write(ref _error, keyboardError ?? mouseError);
                _ready.TrySetResult(Error);
                Application.Run();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _error, ex.Message);
                _ready.TrySetResult(ex.Message);
            }
            finally
            {
                if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
                if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
                _blocked = false;
                ReleaseHeldInput();
            }
        }

        private static void ReleaseHeldInput()
        {
            // Do not leave a key/button held in another application when its
            // physical release happened while the pause guard was swallowing input.
            for (var key = 8; key < 255; key++)
                if ((GetAsyncKeyState(key) & 0x8000) != 0)
                    keybd_event((byte)key, 0, 0x0002, UIntPtr.Zero);
            if ((GetAsyncKeyState(1) & 0x8000) != 0) mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            if ((GetAsyncKeyState(2) & 0x8000) != 0) mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
            if ((GetAsyncKeyState(4) & 0x8000) != 0) mouse_event(0x0040, 0, 0, 0, UIntPtr.Zero);
        }

        private IntPtr FilterInput(int code, IntPtr message, IntPtr data) =>
            code >= 0 && _blocked ? new IntPtr(1) : CallNextHookEx(IntPtr.Zero, code, message, data);

        public void Dispose()
        {
            _blocked = false;
            try { _dispatcher?.BeginInvoke(() => Application.ExitThread()); }
            catch (InvalidOperationException) { }
            _thread.Join(TimeSpan.FromSeconds(1));
        }

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? module);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
    }
}
