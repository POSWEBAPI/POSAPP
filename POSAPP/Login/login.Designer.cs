using System.Drawing.Drawing2D;

namespace POSAPP
{
    partial class login
    {
        private System.ComponentModel.IContainer components = null;

        private Panel panelTitleBar;
        private Label lblAppTitle;
        private Button btnMinimize, btnMaximize, btnClose;

        private Panel panelRight;
        private PictureBox picLogo;
        private Label lblLogoFallback;
        private Label lblBrandTagline;

        private Label lblWelcome;
        private Label lblSubtitle;

        private Panel panelPinDots;
        private Label[] pinDots = new Label[5];

        private TableLayoutPanel panelKeypad; 

        private Button btnUnlock;
        private Label lblShiftInfo;
        private Label lblFooter;
        private KeypadButton[] keypadButtons = new KeypadButton[12];

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // ── Form ──────────────────────────────────────────────
            this.ClientSize = new Size(1000, 940);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.Load += new EventHandler(this.Form1_Load);
            this.Paint += Form_BackgroundPaint;

            const int titleBarHeight = 40;

            // ── Title bar ─────────────────────────────────────────
            panelTitleBar = new Panel
            {
                Height = titleBarHeight,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent
            };
            lblAppTitle = new Label
            {
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(14, 10)
            };

            btnMinimize = MakeTitleBtn("─", new Point(860, 0));
            btnMaximize = MakeTitleBtn("□", new Point(900, 0));
            btnClose = MakeTitleBtn("✕", new Point(940, 0));

            btnMinimize.Click += btnMinimize_Click;
            btnMaximize.Click += btnMaximize_Click;
            btnClose.Click += btnClose_Click;
            btnClose.MouseEnter += btnClose_MouseEnter;
            btnClose.MouseLeave += btnClose_MouseLeave;
            btnMinimize.MouseEnter += btnMinimize_MouseEnter;
            btnMinimize.MouseLeave += btnMinimize_MouseLeave;
            btnMaximize.MouseEnter += btnMaximize_MouseEnter;
            btnMaximize.MouseLeave += btnMaximize_MouseLeave;
            panelTitleBar.MouseDown += panelTitleBar_MouseDown;
            panelTitleBar.MouseMove += panelTitleBar_MouseMove;
            panelTitleBar.MouseUp += panelTitleBar_MouseUp;
            panelTitleBar.DoubleClick += panelTitleBar_DoubleClick;
            panelTitleBar.Resize += panelTitleBar_Resize;

            panelTitleBar.Controls.AddRange(new Control[]
                { lblAppTitle, btnMinimize, btnMaximize, btnClose });

            // ── CENTERED CARD ───────────────────────────────────────
            // ── CENTERED CARD ─── increase height to fit larger keypad comfortably ──
            const int cardWidth = 440;
            const int cardHeight = 820;   // was 660 — more room for bigger keypad
            int cardX = (1000 - cardWidth) / 2;
            int cardY = (760 - cardHeight) / 2 + 10;

            panelRight = new Panel
            {
                Size = new Size(cardWidth, cardHeight),
                Location = new Point(cardX, cardY),
                BackColor = Color.White
            };
            panelRight.Paint += (s, ev) =>
            {
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RoundedRect(new Rectangle(0, 0, panelRight.Width - 1, panelRight.Height - 1), 20);
                panelRight.Region = new Region(path);
                using var border = new Pen(Color.FromArgb(226, 232, 240), 1f);
                ev.Graphics.DrawPath(border, path);

                ev.Graphics.SetClip(path);
                using var accent = new LinearGradientBrush(
                    new Rectangle(0, 0, panelRight.Width, 6),
                    Color.FromArgb(96, 165, 250),
                    Color.FromArgb(147, 51, 234), 0f);
                ev.Graphics.FillRectangle(accent, 0, 0, panelRight.Width, 6);
                ev.Graphics.ResetClip();
            };

            const int padX = 40;
            const int innerWidth = cardWidth - padX * 2;
            int y = 40;

            // ── Logo ──────────────────────────────────────────────
            const int logoSize = 76;
            int logoX = (cardWidth - logoSize) / 2;
            picLogo = new PictureBox
            {
                Size = new Size(logoSize, logoSize),
                Location = new Point(logoX, y),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Visible = false
            };
            lblLogoFallback = new Label
            {
                Text = "EUROTEX",
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 27, 75),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(innerWidth, 50),
                Location = new Point(padX, y),
                TextAlign = ContentAlignment.MiddleCenter
            };
            // No Paint handler here — plain text, no circular clip/fill
            y += 50 + 12;

            lblBrandTagline = new Label
            {
                Text = "Smart  •  Efficient  •  Point of Sale",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(96, 130, 210),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(innerWidth, 22),
                Location = new Point(padX, y),
                TextAlign = ContentAlignment.MiddleCenter
            };
            y += 22 + 18;

            var divider = new Panel
            {
                Size = new Size(innerWidth, 1),
                Location = new Point(padX, y),
                BackColor = Color.FromArgb(219, 228, 249)
            };
            y += 1 + 20;

            lblWelcome = new Label
            {
                Text = "Enter PIN",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 27, 75),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(innerWidth, 36),
                Location = new Point(padX, y),
                TextAlign = ContentAlignment.MiddleCenter
            };
            y += 36 + 2;

            lblSubtitle = new Label
            {
                Text = "Touch the numeric keys to unlock the till",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(innerWidth, 22),
                Location = new Point(padX, y),
                TextAlign = ContentAlignment.MiddleCenter
            };
            y += 22 + 22;

            // ── PIN dots ──────────────────────────────────────────
            // ── PIN dots ──────────────────────────────────────────
            const int dotSize = 18;
            const int dotGap = 20;
            int dotsWidth = dotSize * 5 + dotGap * 4;
            int dotsX = (cardWidth - dotsWidth) / 2;

            panelPinDots = new Panel
            {
                Size = new Size(dotsWidth, dotSize),
                Location = new Point(dotsX, y),
                BackColor = Color.Transparent
            };
            for (int i = 0; i < 5; i++)
            {
                var dot = new Label
                {
                    Size = new Size(dotSize, dotSize),
                    Location = new Point(i * (dotSize + dotGap), 0),
                    BackColor = Color.Transparent,
                    Tag = false
                };
                dot.Paint += PinDot_Paint;
                panelPinDots.Controls.Add(dot);
                pinDots[i] = dot;
            }
            y += dotSize + 26;

            // ── Keypad ────────────────────────────────────────────
            // ── Keypad ────────────────────────────────────────────
            // ── Keypad ────────────────────────────────────────────
            // ── Keypad ────────────────────────────────────────────
            const int keyW = 100, keyH = 78, keyGap = 18;
            int keypadWidth = keyW * 3 + keyGap * 2;
            int keypadX = padX + (innerWidth - keypadWidth) / 2;

            panelKeypad = new TableLayoutPanel
            {
                Size = new Size(keypadWidth, keyH * 4 + keyGap * 3),
                Location = new Point(keypadX, y),
                ColumnCount = 3,
                RowCount = 4,
                BackColor = Color.White,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            for (int c = 0; c < 3; c++)
                panelKeypad.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, keyW));
            for (int r = 0; r < 4; r++)
                panelKeypad.RowStyles.Add(new RowStyle(SizeType.Absolute, keyH));

            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLEAR", "0", "⌫" };

            for (int i = 0; i < 12; i++)
            {
                bool isClear = keys[i] == "CLEAR";
                bool isBack = keys[i] == "⌫";
                bool isUtility = isClear || isBack;

                var btn = new KeypadButton
                {
                    Text = keys[i],
                    Font = new Font("Segoe UI", isClear ? 9.5F : (isBack ? 14F : 18F), FontStyle.Bold),
                    Size = new Size(keyW, keyH),
                    Margin = new Padding(0),
                    // Utility keys (CLEAR/⌫) sit on a soft neutral fill so they read as
                    // secondary actions; digits stay crisp white with the navy brand text.
                    BackColor = isUtility ? Color.FromArgb(248, 249, 252) : Color.White,
                    ForeColor = isClear ? Color.FromArgb(220, 38, 38)
                              : isBack ? Color.FromArgb(100, 116, 139)
                              : Color.FromArgb(30, 27, 75),
                    BorderColor = isClear ? Color.FromArgb(252, 202, 202) : Color.FromArgb(228, 231, 238),
                    HoverBackColor = isClear ? Color.FromArgb(254, 236, 236) : Color.FromArgb(240, 242, 247),
                    PressedBackColor = isClear ? Color.FromArgb(252, 220, 220) : Color.FromArgb(226, 229, 240),
                    Tag = keys[i]
                };
                btn.Click += KeypadButton_Click;
                keypadButtons[i] = btn;
                panelKeypad.Controls.Add(btn, i % 3, i / 3);
            }
            y += panelKeypad.Height + 24;

            // ── Unlock button ─────────────────────────────────────
            btnUnlock = new Button
            {
                Text = "UNLOCK TILL  →",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Size = new Size(innerWidth, 46),
                Location = new Point(padX, y),
                BackColor = Color.FromArgb(30, 27, 75),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnUnlock.FlatAppearance.BorderSize = 0;
            btnUnlock.Region = new Region(RoundedRect(new Rectangle(0, 0, innerWidth, 46), 23));
            btnUnlock.Click += btnUnlock_Click;
            btnUnlock.MouseEnter += btnUnlock_MouseEnter;
            btnUnlock.MouseLeave += btnUnlock_MouseLeave;
            y += 46 + 20;

            // ── Shift / terminal info ──────────────────────────────
            lblShiftInfo = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(innerWidth, 18),
                Location = new Point(padX, y),
                TextAlign = ContentAlignment.MiddleCenter
            };

            panelRight.Controls.AddRange(new Control[]
            {
                picLogo, lblLogoFallback, lblBrandTagline,
                divider, lblWelcome, lblSubtitle,
                panelPinDots, panelKeypad, btnUnlock, lblShiftInfo
            });

            // ── Footer (outside the card) ───────────────────────────
            lblFooter = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(400, 20),
                Location = new Point((1000 - 400) / 2, 940 - 34),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // ── Z-order ───────────────────────────────────────────
            this.Controls.Add(lblFooter);
            this.Controls.Add(panelRight);
            this.Controls.Add(panelTitleBar);
            panelTitleBar.BringToFront();

            this.ResumeLayout(false);
        }

        private void PinDot_Paint(object sender, PaintEventArgs e)
        {
            var lbl = (Label)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool filled = lbl.Tag is bool b && b;
            int size = lbl.Width;
            using var brush = new SolidBrush(filled ? Color.FromArgb(30, 27, 75) : Color.White);
            using var pen = new Pen(Color.FromArgb(203, 213, 225), 1.5f);
            e.Graphics.FillEllipse(brush, 1, 1, size - 3, size - 3);
            e.Graphics.DrawEllipse(pen, 1, 1, size - 3, size - 3);
        }

        private void Form_BackgroundPaint(object sender, PaintEventArgs e)
        {
            using var brush = new LinearGradientBrush(
                this.ClientRectangle,
                Color.FromArgb(15, 23, 42),
                Color.FromArgb(30, 41, 70),
                45f);
            e.Graphics.FillRectangle(brush, this.ClientRectangle);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private Button MakeTitleBtn(string text, Point loc)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(40, 40),
                Location = loc,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(236, 241, 249),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
    }
}