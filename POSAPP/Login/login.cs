using POSAPP.SqlLite;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        private const int PIN_LENGTH = 5;

        // ── Drag ──────────────────────────────────────────────────────────────
        private bool _drag;
        private Point _dragCursor, _dragForm;

        // ── PIN state ─────────────────────────────────────────────────────────
        private string _pin = "";

        // ── Status toast ──────────────────────────────────────────────────────
        private Panel _statusCard;
        private Label _statusIcon;
        private Label _statusText;
        private System.Windows.Forms.Timer _dotTimer;
        private int _dotCount = 0;
        private string _baseMessage = "";

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
            LayoutCards();
            BuildStatusCard();
            lblShiftInfo.Text = "Shift: Morning | Terminal ID: #01"; // adjust/wire from config as needed
            btnMaximize_Click(null, null);

            _ = Task.Run(() => { try { new SyncService().SyncAll(); } catch { } });

           // _ = CheckForUpdateAsync();
        }
        private async Task CheckForUpdateAsync()
        {
            try
            {
                var updater = new UpdateService();
                var info = await updater.CheckForUpdateAsync();
                if (info == null) return; // already on latest version, or check failed silently

                var result = MessageBox.Show(
                    $"Version {info.Version} is available.\n\n{info.ReleaseNotes}\n\nUpdate now?",
                    "Update Available",
                    info.Mandatory ? MessageBoxButtons.OK : MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (info.Mandatory || result == DialogResult.Yes)
                {
                    ShowStatus("Downloading update...", StatusType.Loading);
                    string zip = await updater.DownloadUpdateAsync(info);
                    string staging = updater.ExtractUpdate(zip, info.Version);
                    updater.LaunchUpdaterAndExit(staging);
                }
            }
            catch
            {
                // Never let update logic crash the login screen.
            }
        }
        private void LoadLogo()
        {
            string[] names = { "logo1.png", "shripos.png", "ShriPOS.png", "logo1.jpg", "logo1.jpeg" };
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

            // No logo file found — designer already set Text="EUROTEX",
            // full-width Size, and centered Location. Just show it.
            picLogo.Visible = false;
            lblLogoFallback.Visible = true;
        }

        private void LayoutCards()
        {
            int titleH = panelTitleBar.Height;
            int totalW = this.ClientSize.Width;
            int totalH = this.ClientSize.Height;

            int cardW = panelRight.Width;
            int cardH = panelRight.Height;

            int cardX = (totalW - cardW) / 2;
            int cardY = titleH + (totalH - titleH - cardH) / 2;
            panelRight.Location = new Point(cardX, cardY);

            int footerW = lblFooter.Width;
            lblFooter.Location = new Point((totalW - footerW) / 2, totalH - 34);
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
            HideStatus();
        }

        private void UpdatePinDots()
        {
            for (int i = 0; i < pinDots.Length; i++)
            {
                pinDots[i].Tag = i < _pin.Length;
                pinDots[i].Invalidate();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // STATUS TOAST
        // ═════════════════════════════════════════════════════════════════════
        private void BuildStatusCard()
        {
            _statusCard = new Panel
            {
                Size = new Size(320, 44),
                BackColor = Color.FromArgb(239, 246, 255),
                Visible = false,
                Cursor = Cursors.Default
            };
            _statusCard.Region = MakeRoundedRegion(_statusCard.Size, 10);

            _statusIcon = new Label
            {
                Font = new Font("Segoe UI", 13F),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(38, 44),
                Location = new Point(8, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _statusText = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = C_Info,
                AutoSize = false,
                Size = new Size(270, 44),
                Location = new Point(48, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _statusCard.Controls.Add(_statusIcon);
            _statusCard.Controls.Add(_statusText);
            this.Controls.Add(_statusCard);
            _statusCard.BringToFront();

            _dotTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _dotTimer.Tick += (s, _) =>
            {
                _dotCount = (_dotCount + 1) % 4;
                if (_statusText != null && !_statusText.IsDisposed)
                    _statusText.Text = _baseMessage + new string('.', _dotCount);
            };
        }

        private enum StatusType { Loading, Success, Error, Warning }

        private void ShowStatus(string message, StatusType type)
        {
            void Apply()
            {
                if (_statusCard == null || _statusCard.IsDisposed) return;
                _statusCard.Location = new Point(
     this.ClientSize.Width - _statusCard.Width - 14, 50);

                _dotTimer.Stop(); _dotCount = 0; _baseMessage = message;

                switch (type)
                {
                    case StatusType.Loading:
                        _statusCard.BackColor = Color.FromArgb(239, 246, 255);
                        _statusText.ForeColor = C_Info;
                        _statusIcon.Text = "⏳";
                        _statusText.Text = message;
                        _dotTimer.Start();
                        break;
                    case StatusType.Success:
                        _statusCard.BackColor = Color.FromArgb(240, 253, 244);
                        _statusText.ForeColor = C_Success;
                        _statusIcon.Text = "✅";
                        _statusText.Text = message;
                        break;
                    case StatusType.Error:
                        _statusCard.BackColor = Color.FromArgb(254, 242, 242);
                        _statusText.ForeColor = C_Error;
                        _statusIcon.Text = "❌";
                        _statusText.Text = message;
                        break;
                    case StatusType.Warning:
                        _statusCard.BackColor = Color.FromArgb(255, 251, 235);
                        _statusText.ForeColor = C_Warning;
                        _statusIcon.Text = "⚠️";
                        _statusText.Text = message;
                        break;
                }

                _statusCard.Region = MakeRoundedRegion(_statusCard.Size, 10);
                _statusCard.Visible = true;
                _statusCard.BringToFront();
                _statusCard.Invalidate();
            }
            if (InvokeRequired) Invoke((Action)Apply); else Apply();
        }

        private void HideStatus()
        {
            _dotTimer?.Stop();
            if (_statusCard != null && !_statusCard.IsDisposed)
            {
                if (InvokeRequired) Invoke((Action)(() => _statusCard.Visible = false));
                else _statusCard.Visible = false;
            }
        }

        private void SetLoginBusy(bool busy)
        {
            void Apply()
            {
                btnUnlock.Enabled = !busy;
                btnUnlock.Text = busy ? "PLEASE WAIT…" : "UNLOCK TILL  →";
                btnUnlock.BackColor = busy
                    ? Color.FromArgb(148, 163, 184)
                    : Color.FromArgb(30, 27, 75);
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
            if (string.IsNullOrWhiteSpace(_pin))
            {
                ShowStatus("Enter PIN.", StatusType.Warning);
                return;
            }
            if (_pin.Length < PIN_LENGTH)
            {
                ShowStatus("Enter all 4 digits.", StatusType.Warning);
                return;
            }

            SetLoginBusy(true);

            bool online = await Task.Run(() => IsServerReachable()).ConfigureAwait(true);

            if (!online)
            {
                ShowStatus("Offline — trying local login...", StatusType.Warning);
                var localUser = LocalAuthService.ValidateUserByPassword(_pin);
                if (localUser != null)
                {
                    ShowStatus("Offline login successful!", StatusType.Success);
                    await Task.Delay(400);
                    ApplyLoginResult(localUser, token: null);
                }
                else
                {
                    ShowStatus("Login failed — incorrect PIN.", StatusType.Error);
                    ResetPin();
                    SetLoginBusy(false);
                }
                return;
            }

            ShowStatus("Logging in", StatusType.Loading);
            try
            {
                var api = new ApiService();
                string json = await api.LoginAsync(_pin);
                var result = JsonSerializer.Deserialize<LoginResponse>(json);
                if (result != null && result.IsSuccess)
                {
                    ShowStatus("Logged in successfully!", StatusType.Success);

                    try { await Task.Run(() => LocalAuthService.SaveUser(result.User, _pin)); }
                    catch (Exception ex) { Debug.WriteLine("SaveUser (offline cache) failed: " + ex.Message); }

                    await Task.Delay(400);
                    ApplyLoginResult(result.User, result.Token);
                }
                else
                {
                    ShowStatus(result?.Message ?? "Invalid PIN.", StatusType.Error);
                    ResetPin();
                    SetLoginBusy(false);
                }
            }
            catch
            {
                ShowStatus("Offline — trying local login...", StatusType.Warning);
                var local = LocalAuthService.ValidateUserByPassword(_pin);
                if (local != null)
                {
                    ShowStatus("Offline login successful!", StatusType.Success);
                    await Task.Delay(400);
                    ApplyLoginResult(local, token: null);
                }
                else
                {
                    ShowStatus("Login failed — incorrect PIN.", StatusType.Error);
                    ResetPin();
                    SetLoginBusy(false);
                }
            }
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

        private static string GetApiBaseUrl()
        {
            try
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                if (File.Exists(cfg))
                {
                    string url = File.ReadAllText(cfg).Trim();
                    if (!string.IsNullOrEmpty(url)) return url;
                }
            }
            catch { }
            return "https://purplemoonapi.mythitsolutions.co.in";
        }

        private void ApplyLoginResult(UserInfo user, string token)
        {
            CurrentUser.Token = token;
            CurrentUser.UserInfo = user;
            CurrentUser.CompanyID = user.CompanyID;
            CurrentUser.StoreID = user.StoreID;
            CurrentUser.RoleID = user.RoleID;
            new Dashboard().Show();
            this.Hide();
        }

        // ═════════════════════════════════════════════════════════════════════
        // TITLE BAR
        // ═════════════════════════════════════════════════════════════════════
        private void RepositionTitleButtons()
        {
            int w = panelTitleBar.Width;
            btnClose.Location = new Point(w - 46, 0);
            btnMaximize.Location = new Point(w - 92, 0);
            btnMinimize.Location = new Point(w - 138, 0);
        }

        private void panelTitleBar_Resize(object sender, EventArgs e)
            => RepositionTitleButtons();

        private void panelTitleBar_DoubleClick(object sender, EventArgs e)
            => btnMaximize_Click(sender, e);

        private void btnMinimize_Click(object sender, EventArgs e)
            => this.WindowState = FormWindowState.Minimized;

        private void btnMaximize_Click(object sender, EventArgs e)
        {
            this.WindowState = this.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
            btnMaximize.Text = this.WindowState == FormWindowState.Maximized ? "❐" : "□";
            RepositionTitleButtons();
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
            if (panelRight != null) LayoutCards();
            if (panelTitleBar != null) RepositionTitleButtons();
            if (_statusCard != null && _statusCard.Visible)
                _statusCard.Location = new Point(
                    this.ClientSize.Width - _statusCard.Width - 14, 50);
            this.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
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
            _dotTimer?.Stop();
            _dotTimer?.Dispose();
            base.OnFormClosed(e);
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════
        private Region MakeRoundedRegion(Size size, int r)
            => new Region(MakeRoundedPath(new Rectangle(0, 0, size.Width, size.Height), r));

        private GraphicsPath MakeRoundedPath(Rectangle rect, int r)
        {
            int d = r * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
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