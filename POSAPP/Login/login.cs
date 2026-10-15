using POSAPP.SqlLite;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace POSAPP
{
    public partial class login : Form
    {
        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color C_Info = Color.FromArgb(37, 99, 235);
        private static readonly Color C_Success = Color.FromArgb(22, 163, 74);
        private static readonly Color C_Error = Color.FromArgb(220, 38, 38);
        private static readonly Color C_Warning = Color.FromArgb(217, 119, 6);
        private bool _updateInProgress;
        private const int PIN_LENGTH = 5;

        // ── JSON options (API returns camelCase, our DTOs are PascalCase) ────
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // ── Drag ──────────────────────────────────────────────────────────────
        private bool _drag;
        private Point _dragCursor, _dragForm;

        // ── PIN state ─────────────────────────────────────────────────────────
        private string _pin = "";

        // ── Dot animation state (used by PinDot_Paint in the Designer) ───────
        private Color? _dotTint;            // shake colour (red / amber)
        private int _loadIdx = -1;          // chasing dot while logging in
        private System.Windows.Forms.Timer _loadTimer;
        private int? _dotsBaseX;
        private bool _shaking, _busy;

        // ═════════════════════════════════════════════════════════════════════
        public login()
        {
            InitializeComponent();
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.DoubleBuffer, true);
            this.UpdateStyles();
        }

        // ═════════════════════════════════════════════════════════════════════
        // LOAD
        // ═════════════════════════════════════════════════════════════════════
        private void Form1_Load(object sender, EventArgs e)
        {
            LoadLogo();
            lblShiftInfo.Text = $"Shift: Morning | Terminal ID: #01  ";
            btnMaximize_Click(null, null);
        }

        private void LoadLogo()
        {
            string[] names = { "logo.png", "shripos.png", "ShriPOS.png", "logo.jpg", "logo.jpeg" };
            foreach (string n in names)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, n);
                if (File.Exists(path))
                {
                    try
                    {
                        picLogo.Image = Image.FromFile(path);
                        picLogo.Visible = true;
                        lblLogoFallback.Visible = false;
                    }
                    catch { }
                    return;
                }
            }

            picLogo.Visible = false;
            lblLogoFallback.Visible = true;
        }

        protected override void OnPaint(PaintEventArgs e) => base.OnPaint(e);

        // ═════════════════════════════════════════════════════════════════════
        // PIN ENTRY
        // ═════════════════════════════════════════════════════════════════════
        private void KeypadButton_Click(object sender, EventArgs e)
        {
            var btn = (Button)sender;
            string key = btn.Tag?.ToString() ?? "";

            switch (key)
            {
                case "CLEAR":
                    _pin = "";
                    break;
                case "⌫":
                    if (_pin.Length > 0)
                        _pin = _pin.Substring(0, _pin.Length - 1);
                    break;
                default:
                    if (_pin.Length < PIN_LENGTH && key.Length == 1 && char.IsDigit(key[0]))
                        _pin += key;
                    break;
            }

            UpdatePinDots();
        }

        private void UpdatePinDots()
        {
            for (int i = 0; i < pinDots.Length; i++)
            {
                pinDots[i].Tag = i < _pin.Length;
                pinDots[i].Invalidate();
            }

            if (_pin.Length > 0) HidePinError();
        } 

        private void SetLoginBusy(bool busy)
        {
            void Apply()
            {
                _busy = busy;
                btnUnlock.Enabled = !busy;
                btnUnlock.Text = busy ? "PLEASE WAIT…" : "UNLOCK TILL  →";
                btnUnlock.BackColor = busy ? Color.FromArgb(148, 163, 184) : Color.FromArgb(30, 27, 75);
                foreach (var k in keypadButtons)
                    if (k != null) k.Enabled = !busy;
            }
            if (InvokeRequired) Invoke((Action)Apply); else Apply();
        }

        // ═════════════════════════════════════════════════════════════════════
        // LOGIN
        // ═════════════════════════════════════════════════════════════════════
        private async void btnUnlock_Click(object sender, EventArgs e)
        {
            if (_updateInProgress || _busy) return;

            if (_pin.Length < PIN_LENGTH)           // empty or incomplete
            {
                await ShakeDotsAsync(C_Warning);
                await ShowPinErrorAsync($"Enter all {PIN_LENGTH} digits", C_Warning);
                return;
            }

            HidePinError();
            SetLoginBusy(true);

            bool online = await Task.Run(() => IsServerReachable()).ConfigureAwait(true);
            if (!online)
            {
                await TryOfflineLoginAsync();
                return;
            }

            StartLoadingDots();
            try
            {
                var api = new ApiService();
                string json = await api.LoginAsync(_pin);
                var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOpts);
                StopLoadingDots();

                if (result != null && result.IsSuccess)
                {
                    try { await Task.Run(() => LocalAuthService.SaveUser(result.User, _pin)); }
                    catch (Exception ex) { Debug.WriteLine("SaveUser (offline cache) failed: " + ex.Message); }

                    ApplyLoginResult(result.User, result.Token);
                }
                else
                {
                    await FailLoginAsync();
                }
            }
            catch
            {
                StopLoadingDots();
                await TryOfflineLoginAsync();
            }
        }

        private async Task TryOfflineLoginAsync()
        {
            var local = LocalAuthService.ValidateUserByPassword(_pin);
            if (local != null) ApplyLoginResult(local, token: null);
            else await FailLoginAsync();
        }

        private async Task FailLoginAsync()
        {
            await ShakeDotsAsync(C_Error);
            ResetPin();
            await ShowPinErrorAsync("Incorrect PIN", C_Error);
            SetLoginBusy(false);
        }

        private void ResetPin()
        {
            _pin = "";
            UpdatePinDots();
        }

        private static bool IsServerReachable()
        {
            try
            {
                var url = GetApiBaseUrl()?.Trim();
                if (string.IsNullOrWhiteSpace(url)) return false;
                if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

                int port = uri.Port > 0 ? uri.Port
                         : uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;

                using var sock = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
                var result = sock.BeginConnect(uri.Host, port, null, null);
                bool connected = result.AsyncWaitHandle.WaitOne(1200);
                if (connected) sock.EndConnect(result);
                return connected;
            }
            catch { return false; }
        }

        private static string GetApiBaseUrl() => AppConfig.BaseUrl;

        private async void ApplyLoginResult(UserInfo user, string token)
        {
            CurrentUser.Token = token;
            CurrentUser.UserInfo = user;
            CurrentUser.CompanyID = user.CompanyID;
            CurrentUser.StoreID = user.StoreID;
            CurrentUser.RoleID = user.RoleID;

            // Rights load in the background while the animation plays
            var rightsTask = POSAPP.Security.RightsManager.LoadAsync(user.RoleID, isOnline: token != null);
            await PlaySuccessAnimationAsync(token == null ? "Offline mode — opening your till…" : null);
            await rightsTask;

            new Dashboard().Show();
            this.Hide();
        }

        // ═════════════════════════════════════════════════════════════════════
        // DOT ANIMATIONS (loading chase + error/warning shake)
        // ═════════════════════════════════════════════════════════════════════
        private void InvalidateDots()
        {
            foreach (var d in pinDots) d?.Invalidate();
        }

        private void StartLoadingDots()
        {
            _loadIdx = 0;
            _loadTimer ??= new System.Windows.Forms.Timer { Interval = 110 };
            _loadTimer.Tick -= LoadTimer_Tick;
            _loadTimer.Tick += LoadTimer_Tick;
            _loadTimer.Start();
            InvalidateDots();
        }

        private void LoadTimer_Tick(object sender, EventArgs e)
        {
            _loadIdx = (_loadIdx + 1) % PIN_LENGTH;
            InvalidateDots();
        }

        private void StopLoadingDots()
        {
            _loadTimer?.Stop();
            _loadIdx = -1;
            InvalidateDots();
        }

        private async Task ShakeDotsAsync(Color tint)
        {
            if (_shaking) return;
            _shaking = true;

            int baseX = _dotsBaseX ??= panelPinDots.Left;
            _dotTint = tint;
            InvalidateDots();

            const int dur = 450;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < dur)
            {
                float t = sw.ElapsedMilliseconds / (float)dur;
                panelPinDots.Left = baseX + (int)(Math.Sin(t * Math.PI * 6) * 14 * (1 - t));
                await Task.Delay(15);
            }
            panelPinDots.Left = baseX;

            await Task.Delay(150);          // let the colour linger briefly
            _dotTint = null;
            InvalidateDots();
            _shaking = false;
        }
        private async Task ShowPinErrorAsync(string text, Color color)
        {
            lblPinError.Text = text;
            lblPinError.Visible = true;

            // Fade in by blending white -> colour (WinForms labels have no alpha)
            const int dur = 250;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < dur)
            {
                float t = sw.ElapsedMilliseconds / (float)dur;
                lblPinError.ForeColor = Color.FromArgb(
                    255 + (int)((color.R - 255) * t),
                    255 + (int)((color.G - 255) * t),
                    255 + (int)((color.B - 255) * t));
                await Task.Delay(15);
            }
            lblPinError.ForeColor = color;
        }

        private void HidePinError()
        {
            if (lblPinError != null) lblPinError.Visible = false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // TITLE BAR
        // ═════════════════════════════════════════════════════════════════════
        private void panelTitleBar_DoubleClick(object sender, EventArgs e)
            => btnMaximize_Click(sender, e);

        private void btnMinimize_Click(object sender, EventArgs e)
            => this.WindowState = FormWindowState.Minimized;

        private void btnMaximize_Click(object sender, EventArgs e)
        {
            this.WindowState = this.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
            btnMaximize.Text = this.WindowState == FormWindowState.Maximized ? "❐" : "□";
        }

        private void btnClose_Click(object sender, EventArgs e) => this.Close();

        private void btnClose_MouseEnter(object sender, EventArgs e)
            => btnClose.BackColor = Color.FromArgb(254, 226, 226);
        private void btnClose_MouseLeave(object sender, EventArgs e)
            => btnClose.BackColor = Color.White;
        private void btnMinimize_MouseEnter(object sender, EventArgs e)
            => btnMinimize.BackColor = Color.FromArgb(241, 245, 249);
        private void btnMinimize_MouseLeave(object sender, EventArgs e)
            => btnMinimize.BackColor = Color.White;
        private void btnMaximize_MouseEnter(object sender, EventArgs e)
            => btnMaximize.BackColor = Color.FromArgb(241, 245, 249);
        private void btnMaximize_MouseLeave(object sender, EventArgs e)
            => btnMaximize.BackColor = Color.White;

        private void btnUnlock_MouseEnter(object sender, EventArgs e)
            => btnUnlock.BackColor = Color.FromArgb(49, 46, 129);
        private void btnUnlock_MouseLeave(object sender, EventArgs e)
            => btnUnlock.BackColor = Color.FromArgb(30, 27, 75);

        // ═════════════════════════════════════════════════════════════════════
        // DRAG
        // ═════════════════════════════════════════════════════════════════════
        private void panelTitleBar_MouseDown(object sender, MouseEventArgs e)
        { _drag = true; _dragCursor = Cursor.Position; _dragForm = this.Location; }

        private void panelTitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drag)
                this.Location = Point.Add(_dragForm,
                    new Size(Point.Subtract(Cursor.Position, new Size(_dragCursor))));
        }

        private void panelTitleBar_MouseUp(object sender, MouseEventArgs e)
            => _drag = false;

        // ═════════════════════════════════════════════════════════════════════
        // OTHER EVENTS
        // ═════════════════════════════════════════════════════════════════════
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_updateInProgress) return true;

            // While logging in / playing the success animation, ignore input
            if (_busy)
                return keyData == Keys.Escape ? base.ProcessCmdKey(ref msg, keyData) : true;

            if (keyData == Keys.Enter) { btnUnlock_Click(null, null); return true; }
            if (keyData == Keys.Escape) { this.Close(); return true; }
            if (keyData == Keys.Back)
            {
                if (_pin.Length > 0) _pin = _pin.Substring(0, _pin.Length - 1);
                UpdatePinDots();
                return true;
            }
            if (keyData >= Keys.D0 && keyData <= Keys.D9)
            {
                if (_pin.Length < PIN_LENGTH) _pin += ((char)('0' + (keyData - Keys.D0)));
                UpdatePinDots();
                return true;
            }
            if (keyData >= Keys.NumPad0 && keyData <= Keys.NumPad9)
            {
                if (_pin.Length < PIN_LENGTH) _pin += ((char)('0' + (keyData - Keys.NumPad0)));
                UpdatePinDots();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _loadTimer?.Stop();
            _loadTimer?.Dispose();
            base.OnFormClosed(e);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LOGIN SUCCESS ANIMATION
        // ═════════════════════════════════════════════════════════════════════
        private sealed class SuccessOverlay : Control
        {
            public float Progress;                 // 0..1
            public string Title = "Login successful";
            public string Sub = "Opening your till…";

            private readonly Font _titleFont = new Font("Segoe UI", 22F, FontStyle.Bold);
            private readonly Font _subFont = new Font("Segoe UI", 11.5F);

            public SuccessOverlay()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw, true);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _titleFont.Dispose(); _subFont.Dispose(); }
                base.Dispose(disposing);
            }

            private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
            private static float EaseOut(float t) => 1 - (float)Math.Pow(1 - t, 3);
            private static float EaseOutBack(float t)
            {
                const float c1 = 1.70158f, c3 = c1 + 1f;
                return 1 + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                if (Width <= 0 || Height <= 0) return;

                // Same navy gradient as the login screen, covering the whole window
                using (var bg = new LinearGradientBrush(ClientRectangle,
                    Color.FromArgb(15, 23, 42), Color.FromArgb(30, 41, 70), 45f))
                    g.FillRectangle(bg, ClientRectangle);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                // AntiAlias (not ClearType) so semi-transparent text stays crisp while fading
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                float cx = Width / 2f, cy = Height / 2f - 50, r = 64;
                var green = Color.FromArgb(34, 197, 94);

                float pRing = EaseOut(Clamp01(Progress / 0.45f));
                float pCheck = EaseOut(Clamp01((Progress - 0.40f) / 0.25f));
                float pPop = Clamp01((Progress - 0.40f) / 0.30f);
                float pPulse = Clamp01((Progress - 0.60f) / 0.40f);
                float pText = EaseOut(Clamp01((Progress - 0.55f) / 0.30f));

                // soft glow behind the circle
                float glowR = r + 26;
                using (var glow = new SolidBrush(Color.FromArgb((int)(28 * pRing), green)))
                    g.FillEllipse(glow, cx - glowR, cy - glowR, glowR * 2, glowR * 2);

                // inner fill (pops in slightly with the check)
                float fr = r * (0.85f + 0.15f * EaseOutBack(pPop));
                using (var fill = new SolidBrush(Color.FromArgb((int)(55 * pCheck), green)))
                    g.FillEllipse(fill, cx - fr, cy - fr, fr * 2, fr * 2);

                // faint track
                using (var track = new Pen(Color.FromArgb(60, green), 6))
                    g.DrawEllipse(track, cx - r, cy - r, r * 2, r * 2);

                // sweeping ring
                using (var ring = new Pen(green, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    if (pRing > 0)
                        g.DrawArc(ring, cx - r, cy - r, r * 2, r * 2, -90, 360 * pRing);

                // checkmark drawn progressively
                var a = new PointF(cx - 26, cy + 2);
                var b = new PointF(cx - 8, cy + 20);
                var c = new PointF(cx + 28, cy - 18);
                float l1 = Dist(a, b), l2 = Dist(b, c);
                float drawn = (l1 + l2) * pCheck;
                using (var pen = new Pen(Color.White, 8)
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (drawn > 0)
                    {
                        if (drawn <= l1)
                            g.DrawLine(pen, a, Lerp(a, b, drawn / l1));
                        else
                        {
                            g.DrawLine(pen, a, b);
                            g.DrawLine(pen, b, Lerp(b, c, (drawn - l1) / l2));
                        }
                    }
                }

                // expanding pulse ring
                if (pPulse > 0 && pPulse < 1)
                {
                    float pr = r + 40 * EaseOut(pPulse);
                    using (var pulse = new Pen(Color.FromArgb((int)(160 * (1 - pPulse)), green), 3))
                        g.DrawEllipse(pulse, cx - pr, cy - pr, pr * 2, pr * 2);
                }

                // text (fades in and slides up a little). Rectangles are tall enough
                // for the font so descenders ("g", "p") are never clipped.
                float lift = (1 - pText) * 12;
                using (var b1 = new SolidBrush(Color.FromArgb((int)(255 * pText), 255, 255, 255)))
                using (var b2 = new SolidBrush(Color.FromArgb((int)(255 * pText), 148, 163, 184)))
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Near,
                    FormatFlags = StringFormatFlags.NoWrap,
                    Trimming = StringTrimming.None
                })
                {
                    g.DrawString(Title, _titleFont, b1,
                        new RectangleF(0, cy + r + 30 + lift, Width, 56), sf);
                    g.DrawString(Sub, _subFont, b2,
                        new RectangleF(0, cy + r + 86 + lift, Width, 32), sf);
                }
            }

            private static float Dist(PointF p, PointF q)
                => (float)Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            private static PointF Lerp(PointF p, PointF q, float t)
                => new PointF(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
        }

        private async Task PlaySuccessAnimationAsync(string sub = null, int durationMs = 1500)
        {
            // Not docked: a plain full-size control on top of everything,
            // so it also covers the title bar and footer.
            var overlay = new SuccessOverlay
            {
                Location = Point.Empty,
                Size = this.ClientSize,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            if (sub != null) overlay.Sub = sub;

            this.Controls.Add(overlay);
            overlay.BringToFront();

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < durationMs)
            {
                overlay.Progress = sw.ElapsedMilliseconds / (float)durationMs;
                overlay.Invalidate();
                await Task.Delay(15);
            }

            overlay.Progress = 1f;
            overlay.Invalidate();
            await Task.Delay(250);      // brief hold so the finished state is readable
        }

        // ═════════════════════════════════════════════════════════════════════
        // DTOs
        // ═════════════════════════════════════════════════════════════════════
        public class LoginResponse
        {
            [JsonPropertyName("isSuccess")] public bool IsSuccess { get; set; }
            [JsonPropertyName("message")] public string Message { get; set; }
            [JsonPropertyName("user")] public UserInfo User { get; set; }
            [JsonPropertyName("token")] public string Token { get; set; }
        }

        public class UserDto
        {
            public int UserID { get; set; }
            public int CompanyID { get; set; }
            public int StoreID { get; set; }
            public int RoleID { get; set; }
        }

        public static class CurrentUser
        {
            public static string Token { get; set; }
            public static UserInfo UserInfo { get; set; }
            public static int CompanyID { get; set; }
            public static int StoreID { get; set; }
            public static int RoleID { get; set; }

            public static void Clear()
            {
                Token = null;
                UserInfo = null;
                CompanyID = 0;
                StoreID = 0;
                RoleID = 0;
            }
        }
    }
}