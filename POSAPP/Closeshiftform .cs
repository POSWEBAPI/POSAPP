using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace POSAPP.Shift
{
    public class CloseShiftForm : Form
    {
        // ── Palette ───────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(28, 32, 42);
        private static readonly Color CardDark = Color.FromArgb(38, 42, 54);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);

        // ── State ─────────────────────────────────────────────────────────
        private readonly int _companyId;
        private readonly int _userId;
        private readonly string _currencySymbol;

        // ── Panels ────────────────────────────────────────────────────────
        private Panel _pnlOpen;
        private Panel _pnlClose;
        private Panel _pnlCenter;      // centred content column

        // ── Open controls ─────────────────────────────────────────────────
        private Label _lblFloatConfirm;

        // ── Close controls ────────────────────────────────────────────────
        private Label _lblShiftOpened;
        private Label _lblShiftUser;
        private Label _lblFloatVal;
        private Label _lblTxCount;
        private Label _lblCashRec;
        private Label _lblCardRec;
        private Label _lblBankRec;
        private Label _lblChangeGiven;
        private Label _lblExpectedDrawer;
        private Panel _pnlTenderStatus;
        private Label _lblTenderStatus;
        private Button _btnCloseShift;

        // ── Header ────────────────────────────────────────────────────────
        private Label _lblBadge;

        // ── Timer ─────────────────────────────────────────────────────────
        private System.Windows.Forms.Timer _refreshTimer;

        // ══════════════════════════════════════════════════════════════════
        public CloseShiftForm(int companyId, int userId, string currencySymbol)
        {
            _companyId = companyId;
            _userId = userId;
            _currencySymbol = string.IsNullOrWhiteSpace(currencySymbol)
                              ? "P" : currencySymbol.Trim();

            InitForm();
            BuildUI();

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
            _refreshTimer.Tick += async (s, e) => await RefreshStateAsync();
            _refreshTimer.Start();

            Load += async (s, e) => await RefreshStateAsync();
        }

        public CloseShiftForm(int userId, int companyId,
                              string companyName, string currencySymbol)
            : this(companyId, userId, currencySymbol) { }

        private void InitForm()
        {
            // Full-screen docked — no border, fills panelContent
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = BgDark;
            Dock = DockStyle.Fill;
            KeyPreview = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer, true);
            UpdateStyles();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosed += (s, e) => _refreshTimer?.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════
        //  BUILD UI
        // ══════════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            // ── Top header bar ────────────────────────────────────────────
            var header = new Panel
            {
                BackColor = PanelDark,
                Dock = DockStyle.Top,
                Height = 58
            };

            header.Controls.Add(new Label
            {
                Text = "🔄  Shift Manager",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Dock = DockStyle.Left,
                Width = 320,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 0, 0)
            });

            _lblBadge = new Label
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(340, 20)
            };
            header.Controls.Add(_lblBadge);

            // Bottom border on header
            header.Paint += (s, pe) =>
            {
                using var pen = new Pen(Border, 1f);
                pe.Graphics.DrawLine(pen, 0, header.Height - 1,
                                     header.Width, header.Height - 1);
            };

            Controls.Add(header);

            // ── Scrollable body ───────────────────────────────────────────
            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgDark,
                AutoScroll = true
            };
            Controls.Add(body);

            // Centre column — fixed 560 px wide, horizontally centred
            _pnlCenter = new Panel
            {
                BackColor = Color.Transparent,
                Width = 580,
                Top = 28
            };

            // Anchor to centre on resize
            body.Resize += (s, e) =>
            {
                _pnlCenter.Left = Math.Max(0, (body.Width - _pnlCenter.Width) / 2);
            };

            body.Controls.Add(_pnlCenter);

            // ── Build the two state panels ────────────────────────────────
            _pnlOpen = BuildOpenPanel();
            _pnlClose = BuildClosePanel();

            _pnlOpen.Width = 580;
            _pnlClose.Width = 580;
            _pnlOpen.Location = Point.Empty;
            _pnlClose.Location = Point.Empty;

            _pnlCenter.Controls.Add(_pnlOpen);
            _pnlCenter.Controls.Add(_pnlClose);

            // BringToFront order matters for overlapping panels
            _pnlOpen.BringToFront();
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPEN PANEL
        // ══════════════════════════════════════════════════════════════════
        private Panel BuildOpenPanel()
        {
            var pnl = new Panel { BackColor = Color.Transparent, Width = 580 };
            int x = 0, w = 580, y = 0;

            // Icon + title
            pnl.Controls.Add(MakeIcon("📂", new Point(x, y), 36));
            pnl.Controls.Add(MakeLbl("Open Shift", new Point(x + 46, y + 6),
                TextWhite, 14F, w - 46, ContentAlignment.MiddleLeft, FontStyle.Bold));
            y += 54;

            // Info card
            var infoCard = Card(x, y, w, 56);
            infoCard.Controls.Add(new Label
            {
                Text = "No active shift. Set the opening float in Float Entry, then open the shift here.",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Italic),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(w - 28, 56),
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            pnl.Controls.Add(infoCard);
            y += 68;

            // Float confirmation row
            var floatRow = new Panel
            {
                BackColor = Color.FromArgb(40, 34, 197, 94),
                Size = new Size(w, 48),
                Location = new Point(x, y)
            };
            floatRow.Region = RoundedRegion(floatRow.Size, 8);
            floatRow.Controls.Add(new Label
            {
                Text = "Opening float",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(200, 48),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            _lblFloatConfirm = new Label
            {
                Text = "—",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(350, 48),
                Location = new Point(202, 0),
                TextAlign = ContentAlignment.MiddleRight
            };
            floatRow.Controls.Add(_lblFloatConfirm);
            pnl.Controls.Add(floatRow);
            y += 62;

            // Divider
            pnl.Controls.Add(Divider(x, y, w, AccBlue));
            y += 14;

            // Open button
            var btnOpen = MakeBtn("▶  Open Shift", AccGreen, new Point(x, y), new Size(w, 52));
            btnOpen.Click += (s, e) => OpenShift();
            pnl.Controls.Add(btnOpen);
            y += 64;

            pnl.Controls.Add(MakeLbl(
                "Save the float in Float Entry first, then open the shift here  |  Esc to close",
                new Point(x, y), TextMuted, 8.5F, w,
                ContentAlignment.MiddleCenter, FontStyle.Italic));
            y += 28;

            pnl.Height = y;
            return pnl;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CLOSE PANEL
        // ══════════════════════════════════════════════════════════════════
        private Panel BuildClosePanel()
        {
            var pnl = new Panel { BackColor = Color.Transparent, Width = 580 };
            int x = 0, w = 580, y = 0;

            // Icon + title
            pnl.Controls.Add(MakeIcon("🔒", new Point(x, y), 36));
            pnl.Controls.Add(MakeLbl("Close Shift", new Point(x + 46, y + 6),
                TextWhite, 14F, w - 46, ContentAlignment.MiddleLeft, FontStyle.Bold));
            y += 54;

            // ── Shift meta card ───────────────────────────────────────────
            var metaCard = Card(x, y, w, 96);
            _lblShiftOpened = RowLabel(metaCard, "Opened at", 10);
            _lblShiftUser = RowLabel(metaCard, "Opened by", 36);
            _lblFloatVal = RowLabel(metaCard, "Opening float", 62);
            pnl.Controls.Add(metaCard);
            y += 108;

            // ── Session summary card ──────────────────────────────────────
            pnl.Controls.Add(SectionLabel("Session Summary", x, y));
            y += 26;

            var sumCard = Card(x, y, w, 122);
            _lblTxCount = RowLabel(sumCard, "Transactions", 10);
            _lblCashRec = RowLabel(sumCard, "Cash received", 34);
            _lblCardRec = RowLabel(sumCard, "Card received", 58);
            _lblBankRec = RowLabel(sumCard, "Bank received", 82);
            _lblChangeGiven = RowLabel(sumCard, "Change given", 106);
            pnl.Controls.Add(sumCard);
            y += 134;

            // ── Expected in drawer bar ────────────────────────────────────
            var drawerBar = new Panel
            {
                BackColor = Color.FromArgb(40, 34, 197, 94),
                Size = new Size(w, 52),
                Location = new Point(x, y)
            };
            drawerBar.Region = RoundedRegion(drawerBar.Size, 8);
            drawerBar.Controls.Add(new Label
            {
                Text = "Expected in drawer",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(220, 52),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            _lblExpectedDrawer = new Label
            {
                Text = "—",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(330, 52),
                Location = new Point(232, 0),
                TextAlign = ContentAlignment.MiddleRight
            };
            drawerBar.Controls.Add(_lblExpectedDrawer);
            pnl.Controls.Add(drawerBar);
            y += 64;

            // ── Tender declaration status ─────────────────────────────────
            _pnlTenderStatus = new Panel
            {
                Size = new Size(w, 48),
                Location = new Point(x, y)
            };
            _pnlTenderStatus.Region = RoundedRegion(_pnlTenderStatus.Size, 8);
            _lblTenderStatus = new Label
            {
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(w - 28, 48),
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _pnlTenderStatus.Controls.Add(_lblTenderStatus);
            pnl.Controls.Add(_pnlTenderStatus);
            y += 60;

            // ── Divider ───────────────────────────────────────────────────
            pnl.Controls.Add(Divider(x, y, w, AccRed));
            y += 14;

            // ── Close Shift button ────────────────────────────────────────
            _btnCloseShift = MakeBtn("■  Close Shift", AccRed,
                                     new Point(x, y), new Size(w, 52));
            _btnCloseShift.Click += (s, e) => CloseShift();
            pnl.Controls.Add(_btnCloseShift);
            y += 64;

            pnl.Controls.Add(MakeLbl(
                "Complete the Tender Declaration first to enable closing  |  Esc to cancel",
                new Point(x, y), TextMuted, 8.5F, w,
                ContentAlignment.MiddleCenter, FontStyle.Italic));
            y += 28;

            pnl.Height = y;
            return pnl;
        }

        // ══════════════════════════════════════════════════════════════════
        //  REFRESH STATE
        // ══════════════════════════════════════════════════════════════════
        private async Task RefreshStateAsync()
        {
            await ShiftState.RefreshShiftStatusAsync(_companyId, _userId);
            if (InvokeRequired) { BeginInvoke(new Action(ApplyState)); return; }
            ApplyState();
        }

        private void ApplyState()
        {
            bool open = ShiftState.IsOpen;

            _lblBadge.Text = open
                ? $"● Shift Open  (User {ShiftState.OpenedByUserId})"
                : "● No shift open";
            _lblBadge.ForeColor = open ? TextGreen : AccRed;

            _pnlOpen.Visible = !open;
            _pnlClose.Visible = open;

            // Resize centre column to the visible panel
            _pnlCenter.Height = open ? _pnlClose.Height : _pnlOpen.Height;

            if (!open)
            {
                // Float confirmation
                bool hasFloat = ShiftState.PendingFloat > 0;
                _lblFloatConfirm.Text = hasFloat
                    ? $"{_currencySymbol} {ShiftState.PendingFloat:F2}  ✔"
                    : "Not set — go to Float Entry first";
                _lblFloatConfirm.ForeColor = hasFloat ? TextGreen : AccOrange;
            }
            else
            {
                // Meta
                SetRow(_lblShiftOpened,
                    ShiftState.OpenedAt.ToString("hh:mm tt  dd-MMM-yyyy"));
                SetRow(_lblShiftUser, ShiftState.OpenedByUserId.ToString());
                SetRow(_lblFloatVal,
                    $"{_currencySymbol} {ShiftState.OpeningFloat:F2}");

                // Summary
                SetRow(_lblTxCount, ShiftState.TxCount.ToString());
                SetRow(_lblCashRec,
                    $"{_currencySymbol} {ShiftState.CashReceived:F2}");
                SetRow(_lblCardRec,
                    $"{_currencySymbol} {ShiftState.CardReceived:F2}");
                SetRow(_lblBankRec,
                    $"{_currencySymbol} {ShiftState.BankReceived:F2}");
                SetRow(_lblChangeGiven,
                    $"{_currencySymbol} {ShiftState.ChangeGiven:F2}");

                decimal expected = ShiftState.OpeningFloat
                                 + ShiftState.CashReceived
                                 + ShiftState.BankReceived
                                 + ShiftState.CardReceived
                                 - ShiftState.ChangeGiven;
                _lblExpectedDrawer.Text =
                    $"{_currencySymbol} {expected:F2}";

                // Tender gate
                int shiftId = ShiftState.ShiftId > 0 ? ShiftState.ShiftId : 1;
                var td = ShiftState.GetExistingTenderDeclaration(shiftId);

                if (td.Exists)
                {
                    _pnlTenderStatus.BackColor = Color.FromArgb(45, 34, 197, 94);
                    _lblTenderStatus.Text =
                        $"✔  Tender declaration confirmed  —  " +
                        $"Grand total: {_currencySymbol} " +
                        $"{(td.CashCounted + td.CardAmount + td.BankAmount):F2}";
                    _lblTenderStatus.ForeColor = TextGreen;
                    _btnCloseShift.Enabled = true;
                    _btnCloseShift.BackColor = AccRed;
                }
                else
                {
                    _pnlTenderStatus.BackColor = Color.FromArgb(50, 251, 146, 60);
                    _lblTenderStatus.Text =
                        "⚠  Tender declaration not yet confirmed. Complete it before closing.";
                    _lblTenderStatus.ForeColor = AccOrange;
                    _btnCloseShift.Enabled = false;
                    _btnCloseShift.BackColor = Color.FromArgb(55, 58, 68);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPEN SHIFT
        // ══════════════════════════════════════════════════════════════════
        private async void OpenShift()
        {
            if (ShiftState.IsOpen)
            {
                MessageBox.Show("A shift is already open. Close it first.",
                    "Shift Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (ShiftState.PendingFloat <= 0)
            {
                MessageBox.Show(
                    "No float amount found.\n\n" +
                    "Please go to Float Entry, set the opening float, and save it first.",
                    "Float Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            decimal amt = ShiftState.PendingFloat;
            bool opened = await ShiftState.OpenShift(_userId, _companyId, amt);

            if (!opened)
            {
                MessageBox.Show("Could not open shift. A shift may already be active.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show(
                $"Shift opened successfully.\n" +
                $"User: {_userId}     Float: {_currencySymbol} {amt:F2}",
                "Shift Opened", MessageBoxButtons.OK, MessageBoxIcon.Information);

            await RefreshStateAsync();
        }

        // ══════════════════════════════════════════════════════════════════
        //  CLOSE SHIFT
        // ══════════════════════════════════════════════════════════════════
        private void CloseShift()
        {
            if (!ShiftState.IsOpen)
            {
                MessageBox.Show("No active shift to close.", "No Shift",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int shiftId = ShiftState.ShiftId > 0 ? ShiftState.ShiftId : 1;
            var td = ShiftState.GetExistingTenderDeclaration(shiftId);
            if (!td.Exists)
            {
                MessageBox.Show(
                    "Tender declaration has not been confirmed for this shift.\n\n" +
                    "Please complete the Tender Declaration before closing.",
                    "Tender Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            decimal expected = ShiftState.OpeningFloat
                             + ShiftState.CashReceived
                             + ShiftState.BankReceived
                             + ShiftState.CardReceived
                             - ShiftState.ChangeGiven;
            decimal declared = td.CashCounted + td.CardAmount + td.BankAmount;

            var confirm = MessageBox.Show(
                $"Close this shift?\n\n" +
                $"Opened at    : {ShiftState.OpenedAt:hh:mm tt  dd-MMM-yyyy}\n" +
                $"Transactions : {ShiftState.TxCount}\n" +
                $"Expected     : {_currencySymbol} {expected:F2}\n" +
                $"Declared     : {_currencySymbol} {declared:F2}\n" +
                $"Variance     : {_currencySymbol} {(declared - expected):F2}",
                "Confirm Close Shift",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            bool closed = ShiftState.CloseShift(
                _userId, td.CashCounted, 0m, td.CardAmount, td.BankAmount);

            if (!closed)
            {
                MessageBox.Show(
                    "Failed to close the shift. Please check the tender declaration.",
                    "Close Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("Shift closed successfully.", "Done",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            ApplyState();
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI FACTORY HELPERS
        // ══════════════════════════════════════════════════════════════════
        private Panel Card(int x, int y, int w, int h)
        {
            var p = new Panel
            {
                BackColor = CardDark,
                Size = new Size(w, h),
                Location = new Point(x, y)
            };
            p.Region = RoundedRegion(p.Size, 10);
            return p;
        }

        private Label RowLabel(Panel parent, string caption, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(200, 22),
                Location = new Point(16, y),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var val = new Label
            {
                Text = "—",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(340, 22),
                Location = new Point(216, y),
                TextAlign = ContentAlignment.MiddleRight
            };
            parent.Controls.Add(val);
            return val;
        }

        private Label SectionLabel(string text, int x, int y) => new Label
        {
            Text = $"── {text} " + new string('─', 46),
            Font = new Font("Segoe UI", 8F),
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
            AutoSize = false,
            Size = new Size(580, 20),
            Location = new Point(x, y),
            TextAlign = ContentAlignment.MiddleLeft
        };

        private Panel Divider(int x, int y, int w, Color color) => new Panel
        {
            BackColor = color,
            Size = new Size(w, 2),
            Location = new Point(x, y)
        };

        private Label MakeIcon(string emoji, Point loc, int size) => new Label
        {
            Text = emoji,
            Font = new Font("Segoe UI Emoji", size * 0.55F),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = loc
        };

        private void SetRow(Label lbl, string text)
        {
            if (lbl == null) return;
            lbl.Text = text;
            lbl.ForeColor = TextWhite;
        }

        private Label MakeLbl(string text, Point loc, Color color, float sz,
            int width = 0,
            ContentAlignment align = ContentAlignment.TopLeft,
            FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", sz, style),
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = width == 0,
                Size = width > 0 ? new Size(width, 24) : Size.Empty,
                Location = loc,
                TextAlign = align
            };
        }

        private Button MakeBtn(string text, Color bg, Point loc, Size size)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Size = size,
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = RoundedRegion(size, 8);
            b.MouseEnter += (s, e) =>
            {
                if (b.Enabled) b.BackColor = ControlPaint.Dark(bg, 0.12f);
            };
            b.MouseLeave += (s, e) =>
            {
                if (b.Enabled) b.BackColor = bg;
            };
            return b;
        }

        private static Region RoundedRegion(Size s, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(s.Width - d, 0, d, d, 270, 90);
            path.AddArc(s.Width - d, s.Height - d, d, d, 0, 90);
            path.AddArc(0, s.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BgDark);
        }
    }
}