using System.Drawing.Drawing2D;

namespace POSAPP
{
    /// <summary>
    /// Fully owner-drawn circular keypad button with a soft shadow and
    /// press-state feedback, styled after modern PIN-pad UIs.
    /// </summary>
    public class KeypadButton : Button
    {
        public Color BorderColor { get; set; } = Color.FromArgb(226, 232, 240);
        public Color HoverBackColor { get; set; } = Color.FromArgb(243, 244, 248);
        public Color PressedBackColor { get; set; } = Color.FromArgb(230, 232, 240);

        private bool _hover;
        private bool _pressed;

        public KeypadButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        { _hover = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e)
        { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs mevent)
        { _pressed = true; Invalidate(); base.OnMouseDown(mevent); }

        protected override void OnMouseUp(MouseEventArgs mevent)
        { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            // Clear the full rectangular bounds to the parent's background so
            // no stray pixels remain outside the circular shape.
            g.Clear(Parent?.BackColor ?? Color.White);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int d = Math.Min(Width, Height) - 6; // circle diameter, small margin for shadow
            var circleRect = new Rectangle((Width - d) / 2, (Height - d) / 2 + 1, d, d);

            // Soft drop shadow beneath the circle for a subtle "elevated key" look
            if (!_pressed)
            {
                using var shadowPath = new GraphicsPath();
                shadowPath.AddEllipse(circleRect.X, circleRect.Y + 2, circleRect.Width, circleRect.Height);
                using var shadowBrush = new SolidBrush(Color.FromArgb(18, 15, 23, 42));
                g.FillPath(shadowBrush, shadowPath);
            }

            var drawRect = _pressed
                ? new Rectangle(circleRect.X, circleRect.Y + 1, circleRect.Width, circleRect.Height)
                : circleRect;

            Color fill = _pressed ? PressedBackColor : (_hover ? HoverBackColor : BackColor);

            using var path = new GraphicsPath();
            path.AddEllipse(drawRect);

            using (var bg = new SolidBrush(fill))
                g.FillPath(bg, path);

            using (var border = new Pen(BorderColor, 1.2f))
                g.DrawPath(border, path);

            TextRenderer.DrawText(
                g, Text, Font, drawRect, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int dd = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, dd, dd, 180, 90);
            path.AddArc(bounds.Right - dd, bounds.Y, dd, dd, 270, 90);
            path.AddArc(bounds.Right - dd, bounds.Bottom - dd, dd, dd, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - dd, dd, dd, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}