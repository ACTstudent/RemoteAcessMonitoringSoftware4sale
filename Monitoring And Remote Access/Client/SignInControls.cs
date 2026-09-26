using System.Drawing.Drawing2D;

namespace Client
{
    // Owner-drawn pieces of the sign-in screen. Stock WinForms text boxes and
    // buttons are square-cornered and carry no icons, so the rounded fields,
    // the rounded Login button and the outline icons are painted here instead.
    // Everything is drawn relative to the control's own size, so the screen
    // scales with the display rather than with fixed pixel artwork.

    internal static class Shapes
    {
        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Mix(Color a, Color b, float amountOfB) => Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * amountOfB),
            (int)Math.Round(a.G + (b.G - a.G) * amountOfB),
            (int)Math.Round(a.B + (b.B - a.B) * amountOfB));
    }

    /// <summary>
    /// A borderless text box inside a rounded outline. The outline turns the
    /// brand colour while the box has focus, since the caret alone is easy for
    /// a child to lose. Padding sets the text inset; a wider right padding
    /// leaves room for a trailing control such as the show-password eye.
    /// </summary>
    internal sealed class RoundedField : Panel
    {
        public TextBox Box { get; }
        public Color BorderColor { get; set; } = Color.Gray;
        public Color FocusColor { get; set; } = Color.Black;
        public float Radius { get; set; } = 8;

        public RoundedField(TextBox box)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = box;
            box.BorderStyle = BorderStyle.None;
            box.GotFocus += (_, _) => Invalidate();
            box.LostFocus += (_, _) => Invalidate();
            Controls.Add(box);
            Cursor = Cursors.IBeam;
            // The outline is bigger than the text line inside it; a click
            // anywhere within it should land in the box.
            Click += (_, _) => box.Focus();
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            Box.BackColor = BackColor;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Box.SetBounds(Padding.Left, (Height - Box.Height) / 2,
                          Math.Max(0, Width - Padding.Horizontal), Box.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool focused = Box.Focused;
            float stroke = focused ? 2f : 1.2f;
            var bounds = new RectangleF(stroke / 2, stroke / 2, Width - stroke - 1, Height - stroke - 1);
            using var path = Shapes.RoundedRect(bounds, Radius);
            using var fill = new SolidBrush(BackColor);
            using var pen = new Pen(focused ? FocusColor : BorderColor, stroke);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// A flat button with rounded corners. It stays a real Button, so it can be
    /// the form's AcceptButton and Enter still signs in.
    /// </summary>
    internal sealed class RoundedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public float Radius { get; set; } = 8;
        public Color HoverColor { get; set; } = Color.Black;

        /// <summary>An outline, for a light secondary button that would otherwise
        /// vanish into a white surface. None when empty.</summary>
        public Color BorderColor { get; set; } = Color.Empty;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Disabled while a sign-in is in flight: washed out, so a pupil can
            // see their press was taken and does not keep pressing.
            var fill = !Enabled ? Shapes.Mix(BackColor, Color.White, 0.4f)
                     : _hover || _pressed ? HoverColor
                     : BackColor;
            using (var path = Shapes.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), Radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (!BorderColor.IsEmpty)
                {
                    using var outline = new Pen(BorderColor, 1.2f);
                    g.DrawPath(outline, path);
                }
            }

            if (Focused && ShowFocusCues)
            {
                // White on a dark button; on a light one it would not show.
                var ringColor = BackColor.GetBrightness() > 0.6f
                    ? Color.FromArgb(170, ForeColor)
                    : Color.FromArgb(200, Color.White);
                using var ring = Shapes.RoundedRect(new RectangleF(3, 3, Width - 7, Height - 7), Math.Max(2, Radius - 3));
                using var pen = new Pen(ringColor, 1.5f);
                g.DrawPath(pen, ring);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    internal enum LineGlyph { Person, Lock, Eye, EyeOff }

    /// <summary>
    /// Small outline icons drawn with GDI+ on a 24-unit grid. Drawn rather than
    /// taken from an icon font, because a lab image without Segoe MDL2 Assets
    /// would otherwise show empty boxes beside the fields.
    /// </summary>
    internal sealed class LineIcon : Control
    {
        private LineGlyph _glyph;

        public LineGlyph Glyph
        {
            get => _glyph;
            set { _glyph = value; Invalidate(); }
        }

        public LineIcon()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float unit = Math.Min(Width, Height) / 24f;
            g.TranslateTransform((Width - 24 * unit) / 2, (Height - 24 * unit) / 2);
            g.ScaleTransform(unit, unit);

            using var pen = new Pen(ForeColor, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            switch (_glyph)
            {
                case LineGlyph.Person:
                    g.DrawEllipse(pen, 8, 3, 8, 8);
                    g.DrawArc(pen, 4, 14, 16, 14, 180, 180);
                    break;

                case LineGlyph.Lock:
                    using (var body = Shapes.RoundedRect(new RectangleF(4, 11, 16, 10), 2))
                        g.DrawPath(pen, body);
                    g.DrawArc(pen, 8, 3, 8, 10, 180, 180);
                    g.DrawLine(pen, 8, 8, 8, 11);
                    g.DrawLine(pen, 16, 8, 16, 11);
                    break;

                case LineGlyph.Eye:
                case LineGlyph.EyeOff:
                    using (var eye = new GraphicsPath())
                    {
                        eye.AddBezier(2, 12, 6, 5, 18, 5, 22, 12);
                        eye.AddBezier(22, 12, 18, 19, 6, 19, 2, 12);
                        eye.CloseFigure();
                        g.DrawPath(pen, eye);
                    }
                    g.DrawEllipse(pen, 9, 9, 6, 6);
                    if (_glyph == LineGlyph.EyeOff) g.DrawLine(pen, 4, 4, 20, 20);
                    break;
            }
        }
    }
}
