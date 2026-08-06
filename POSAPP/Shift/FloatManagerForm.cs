using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace POSAPP.Shift
{
    /// <summary>
    /// Float Manager — records the opening float amount.
    /// Once saved, the amount is editable but requires a confirmation prompt to unlock.
    /// Fully locked (read-only) while a shift is open.
    /// </summary>
    public class FloatManagerForm : Form
    {
        // ── Palette ───────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark2 = Color.FromArgb(42, 46, 56);
        private static readonly Color CardDark = Color.FromArgb(32, 35, 44);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color InputBg = Color.FromArgb(28, 32, 42);
        private static readonly Color InputLock = Color.FromArgb(24, 26, 34);

        // ── State ─────────────────────────────────────────────────────────
        private readonly int _companyId;
        private readonly int _userId;
        private readonly string _currencySymbol;
        private readonly string _dbPath;
        private bool _editUnlocked = false;

        public decimal SavedFloatAmount { get; private set; }

        // ── Drag ──────────────────────────────────────────────────────────
        private bool _drag;
        private Point _dragCursor, _dragForm;

        // ── Controls ──────────────────────────────────────────────────────
        private TextBox _txtFloat;
        private Button _btnSave;
        private Button _btnEdit;
        private Panel _lockBanner;
        private Panel _savedBanner;

        // ══════════════════════════════════════════════════════════════════
        public FloatManagerForm(int companyId, int userId, string currencySymbol, string dbPath = null)
        {
            _companyId = companyId;
            _userId = userId;
            _currencySymbol = string.IsNullOrWhiteSpace(currencySymbol) ? "P" : currencySymbol.Trim();
            _dbPath = dbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

            InitForm();
            BuildUI();
            ApplyState();
        }

        private void InitForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ClientSize = new Size(440, 320);
            KeyPreview = true;
            Region = Rounded(Size, 14);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            UpdateStyles();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private void BuildUI()
        {
            // ── Header ────────────────────────────────────────────────────
            var header = new Panel { BackColor = PanelDark2, Size = new Size(440, 52), Location = Point.Empty };
            header.MouseDown += (s, e) => { _drag = true; _dragCursor = Cursor.Position; _dragForm = Location; };
            header.MouseMove += (s, e) => { if (_drag) Location = Point.Add(_dragForm, new Size(Point.Subtract(Cursor.Position, new Size(_dragCursor)))); };
            header.MouseUp += (s, e) => _drag = false;
            header.Controls.Add(new Label
            {
                Text = "🪙  Float Entry",
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(370, 52),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(44, 52),
                Location = new Point(396, 0),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (s, e) => Close();
            btnX.MouseEnter += (s, e) => btnX.BackColor = Color.FromArgb(196, 30, 58);
            btnX.MouseLeave += (s, e) => btnX.BackColor = Color.Transparent;
            header.Controls.Add(btnX);
            Controls.Add(header);

            int x = 20, w = 400, y = 62;

            // ── Shift-open lock banner ─────────────────────────────────────
            _lockBanner = MakeBanner(
                "🔒  A shift is currently open. Float entry is locked until the shift is closed.",
                Color.FromArgb(55, 251, 146, 60), AccOrange, new Point(x, y), new Size(w, 46));
            _lockBanner.Visible = false;
            Controls.Add(_lockBanner);

            // ── Already-saved banner ───────────────────────────────────────
            _savedBanner = MakeBanner(
                $"✔  Float already saved for this session. Click Edit to change.",
                Color.FromArgb(40, 34, 197, 94), TextGreen, new Point(x, y), new Size(w, 46));
            _savedBanner.Visible = false;
            Controls.Add(_savedBanner);

            y += 56;

            Controls.Add(new Label
            {
                Text = "Enter the opening float for this session.",
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(w, 22),
                Location = new Point(x, y)
            });
            y += 28;

            Controls.Add(new Label
            {
                Text = $"Opening float amount  ({_currencySymbol})",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(x, y)
            });
            y += 22;

            _txtFloat = new TextBox
            {
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = InputBg,
                BorderStyle = BorderStyle.None,
                Text = ShiftState.PendingFloat > 0 ? ShiftState.PendingFloat.ToString("F2") : "0",
                Size = new Size(w, 50),
                Location = new Point(x, y),
                TextAlign = HorizontalAlignment.Center
            };
            _txtFloat.Enter += (s, e) => { if (_txtFloat.Text == "0") _txtFloat.Text = ""; _txtFloat.SelectAll(); };
            _txtFloat.Leave += (s, e) => { if (string.IsNullOrWhiteSpace(_txtFloat.Text)) _txtFloat.Text = "0"; };
            _txtFloat.KeyPress += NumericOnly;
            _txtFloat.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; SaveFloat(); } };
            Controls.Add(_txtFloat);
            y += 52;

            Controls.Add(new Panel { BackColor = AccBlue, Size = new Size(w, 2), Location = new Point(x, y) });
            y += 12;

            _btnSave = MakeBtn("💾  Save Float", AccGreen, new Point(x, y), new Size(w, 46));
            _btnSave.Click += (s, e) => SaveFloat();
            Controls.Add(_btnSave);

            // Edit button sits on top of Save, shown when float is already saved
            _btnEdit = MakeBtn("✏  Edit Float", Color.FromArgb(55, 65, 85), new Point(x, y), new Size(w, 46));
            _btnEdit.Click += (s, e) => UnlockForEdit();
            Controls.Add(_btnEdit);
            y += 56;

            Controls.Add(new Label
            {
                Text = "Press Enter to save  |  Esc to close",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(w, 20),
                Location = new Point(x, y),
                TextAlign = ContentAlignment.MiddleCenter
            });
            y += 28;

            ClientSize = new Size(440, y);
            Region = Rounded(Size, 14);
            _txtFloat.Focus();
        }

        // ══════════════════════════════════════════════════════════════════
        //  STATE MACHINE
        // ══════════════════════════════════════════════════════════════════
        private void ApplyState()
        {
            bool shiftOpen = ShiftState.IsOpen;
            bool hasSaved = ShiftState.PendingFloat > 0;

            // Reset banner positions — only one shows at a time
            _lockBanner.Visible = false;
            _savedBanner.Visible = false;

            if (shiftOpen)
            {
                // LOCKED — shift is open, nothing can change
                _lockBanner.Visible = true;
                _txtFloat.ReadOnly = true;
                _txtFloat.BackColor = InputLock;
                _txtFloat.ForeColor = TextMuted;
                _txtFloat.Text = ShiftState.OpeningFloat > 0
                                         ? ShiftState.OpeningFloat.ToString("F2")
                                         : (hasSaved ? ShiftState.PendingFloat.ToString("F2") : "0");
                _btnSave.Visible = false;
                _btnEdit.Visible = false;
            }
            else if (hasSaved && !_editUnlocked)
            {
                // SAVED, not yet unlocked for editing
                _savedBanner.Visible = true;
                _txtFloat.ReadOnly = true;
                _txtFloat.BackColor = InputLock;
                _txtFloat.ForeColor = TextMuted;
                _txtFloat.Text = ShiftState.PendingFloat.ToString("F2");
                _btnSave.Visible = false;
                _btnEdit.Visible = true;
            }
            else
            {
                // EDITABLE — either no float saved yet, or user clicked Edit
                _txtFloat.ReadOnly = false;
                _txtFloat.BackColor = InputBg;
                _txtFloat.ForeColor = TextGreen;
                _btnSave.Visible = true;
                _btnEdit.Visible = false;
                if (!_editUnlocked) _txtFloat.Focus();
            }
        }

        private void UnlockForEdit()
        {
            var result = MessageBox.Show(
                "The float for this session has already been saved.\n\n" +
                "Are you sure you want to change it?",
                "Edit Float", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            _editUnlocked = true;
            ApplyState();
            _txtFloat.Focus();
            _txtFloat.SelectAll();
        }

        private void SaveFloat()
        {
            if (ShiftState.IsOpen)
            {
                MessageBox.Show("A shift is already open. Close the shift before changing the float.",
                    "Shift Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!decimal.TryParse(_txtFloat.Text, out decimal amt) || amt <= 0)
            {
                MessageBox.Show("Please enter a valid float amount greater than zero.",
                    "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtFloat.Focus();
                return;
            }

            SavedFloatAmount = amt;
            ShiftState.SetPendingFloat(amt);
            _editUnlocked = false;

            MessageBox.Show(
                $"Float amount of {_currencySymbol} {amt:F2} has been saved.\n" +
                "Go to the Shift screen to open the shift.",
                "Float Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

            ApplyState();
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
        private Panel MakeBanner(string text, Color bg, Color fg, Point loc, Size size)
        {
            var pnl = new Panel { BackColor = bg, Size = size, Location = loc };
            pnl.Region = Rounded(size, 8);
            pnl.Controls.Add(new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = fg,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(size.Width - 24, size.Height),
                Location = new Point(12, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            return pnl;
        }

        private static void NumericOnly(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.') e.Handled = true;
            if (e.KeyChar == '.' && ((TextBox)sender).Text.Contains('.')) e.Handled = true;
        }

        private Button MakeBtn(string text, Color bg, Point loc, Size size)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Size = size,
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = Rounded(size, 8);
            b.MouseEnter += (s, e) => { if (b.Enabled) b.BackColor = ControlPaint.Dark(bg, 0.12f); };
            b.MouseLeave += (s, e) => { if (b.Enabled) b.BackColor = bg; };
            return b;
        }

        private static Region Rounded(Size s, int r)
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

        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.Clear(BgDark); }
    }
}