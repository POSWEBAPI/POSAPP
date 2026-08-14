using iText.Kernel.Geom;
using POSAPP.Entity;
using POSAPP.Inventory;
using POSAPP.Invoice;
using POSAPP.Payment;
using POSAPP.Printer;
using POSAPP.Reports;
using POSAPP.Sales;
using POSAPP.Shift;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using static POSAPP.login;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace POSAPP
{
    public partial class Dashboard : Form
    {
        // ── Palette (shared with designer) ────────────────────────────────
        private static readonly Color NavDark = Color.FromArgb(10, 20, 45);
        private static readonly Color NavHover = Color.FromArgb(30, 76, 200);
        private static readonly Color AccentBlue = Color.FromArgb(41, 98, 225);
        private static readonly Color BgPage = Color.FromArgb(244, 247, 252);
        private static readonly Color CardWhite = Color.White;
        private static readonly Color TextDark = Color.FromArgb(10, 20, 45);
        private static readonly Color TextMuted = Color.FromArgb(108, 122, 148);

        private static readonly Color C_Online = Color.FromArgb(22, 178, 120);
        private static readonly Color C_Offline = Color.FromArgb(225, 55, 55);
        private static readonly Color C_Checking = Color.FromArgb(234, 145, 10);

        private const int ONLINE_CACHE_SECONDS = 30;
        private bool? _onlineCache;
        // Add this with your other private fields at the top of Dashboard.cs
        private (decimal salesToday, int orderCount, int unpaidCount, decimal returnsTotal) _dashStats;
        private DateTime _onlineChecked = DateTime.MinValue;

        private Panel _statusDot;
        private Label _statusLabel;

        private bool _drag;
        private Point _dragCursor, _dragForm;
        private Button _activeNav;
        private System.Windows.Forms.Timer _resizeDebounce;
        private int _selectedCompanyId;
        private int _selectedStoreId;
        private string _companyName = " ";
        private string _currencySymbol = "P";
        private SalesReturnForm _salesReturnFormInstance;

        private List<PaymentSlice> _paymentData;
        private string _paymentTotal;
        private Panel _paymentPanel;

        private Label lblCompanyTag;
        private Label lblStoreTag;
        private ComboBox cmbCompany, cmbStore;
        private Label lblDate;

        private System.Windows.Forms.Timer _glowTimer;
        private float _glowAlpha = 0f;
        private bool _glowRising = true;
        private Control _logoControl;
        private EventHandler _dashboardDataChangedHandler; // adjust the delegate type to match DataChanged's actual signature

        private string _companyAddress = "";
        private string _companyPhone = "";
        private string _companyVat = "";
        private string _companyWebsite = "";
        private string _salesOfficeInfo = "";

        private System.Windows.Forms.Timer _refreshTimer;
        private BufferedPanel _payCard;
        private BufferedPanel[] _statCards = new BufferedPanel[4];
        private SalesForm _salesFormInstance;


        private List<TopSellingProductDto> _topProductsData = new List<TopSellingProductDto>();
        private List<LowStockAlertDto> _lowStockData = new List<LowStockAlertDto>();
        private static readonly string _dbPath =
    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ── Animation state ──────────────────────────────────────────────────
        private float _pieAnimProgress = 1f;
        private float _topProdAnimProgress = 1f;
        private float _chartAnimProgress = 1f;
        private float _statAnimProgress = 1f;
        private System.Windows.Forms.Timer _pieAnimTimer;
        private System.Windows.Forms.Timer _topProdAnimTimer;
        private System.Windows.Forms.Timer _chartAnimTimer;
        private System.Windows.Forms.Timer _statAnimTimer;
        private decimal[] _salesChartVals = new decimal[7];
        private string[] _salesChartLabels = new string[7];
        private decimal _salesChartMax = 100m;
        private static float EaseOutCubic(float t) => 1f - (float)Math.Pow(1 - t, 3);

        

        private static readonly Color[] _pieColors =
        {
            Color.FromArgb(41,  98,  225),
            Color.FromArgb(22,  178, 120),
            Color.FromArgb(234, 145,  10),
            Color.FromArgb(124,  82, 230),
            Color.FromArgb(225,  55,  55),
            Color.FromArgb(20,  184, 166),
            Color.FromArgb(224,  62, 142),
            Color.FromArgb(220, 168,   8),
        };

        private System.Windows.Forms.Timer _clockTimer;

        // ══════════════════════════════════════════════════════════════════
        public Dashboard()
        {
            InitializeComponent();
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
        // ══════════════════════════════════════════════════════════════════
        // LOAD
        // ══════════════════════════════════════════════════════════════════
        private async void Form2_Load(object sender, EventArgs e)
        { 
            LoadCompanyInfo();

            int hour = DateTime.Now.Hour;
            string greet = hour < 12 ? "Good Morning"
                         : hour < 17 ? "Good Afternoon"
                         : "Good Evening";
            //if (lblGreeting != null)
            //    lblGreeting.Text = $"{greet}, Admin 👋";

            StyleGrid(dgvTransactions);
            SeedGridData();
            InitializeTopRightDropdowns();
            BuildStatusIndicator();
            UpdateDateTime();
            btnMaximize_Click(sender, e);
            SetActiveNav(btnNavDashboard);
            _clockTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _clockTimer.Tick += (s, _) =>
            {
                if (this.IsDisposed) return;
                UpdateDateTime();
            };
            _clockTimer.Start();

            this.Resize += (s, ev) =>
            {
                _resizeDebounce?.Stop();
                _resizeDebounce?.Dispose();
                _resizeDebounce = new System.Windows.Forms.Timer { Interval = 150 };
                _resizeDebounce.Tick += (ts, te) =>
                {
                    _resizeDebounce.Stop();
                    _resizeDebounce.Dispose();
                    if (panelContent?.Controls.Count > 0 && panelContent.Controls[0] is Panel inner)
                        RebuildInner(inner);
                };
                _resizeDebounce.Start();
            };

            foreach (Control c in new Control[] { this, panelMain, panelTopBar, panelContent })
                c.Click += (s, _) => panelProfileSubmenu.Visible = false;

            ShowStatus("Checking…", null);
            _onlineCache = await Task.Run(() => IsOnline());
            _onlineChecked = DateTime.UtcNow;
            ShowStatus(_onlineCache == true ? "Online" : "Offline", _onlineCache);

            try { SalesReturnRepository.EnsureSchema(); }
            catch (Exception ex) { Debug.WriteLine("ReturnSchema: " + ex.Message); }

            await LoadCompanyDataAsync();
            string sym = _currencySymbol;
            await Task.Run(() => LoadPaymentMethodData(sym));
            LoadPendingInvoicesGrid();
              await LoadDashboardWidgetsAsync();
            await LoadRecentTransactionsAsync();
            await LoadDashboardWidgetsAsync();

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _refreshTimer.Tick += async (s, _) =>
            {
                if (this.IsDisposed) return;
                await RefreshDashboardAsync();
            };
            _refreshTimer.Start();

            _dashboardDataChangedHandler = async (s, _) =>
            {
                if (this.IsDisposed) return;
                await RefreshDashboardAsync();
            };
            DashboardEventBus.DataChanged += _dashboardDataChangedHandler;
        }
        private string FormatAmount(decimal amount)
        {
            return $"{_currencySymbol} {amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        // ══════════════════════════════════════════════════════════════════
        // PAGE HOSTING — embed forms inside panelContent
        // ══════════════════════════════════════════════════════════════════
        private Control _currentPage;   // tracks what's showing

        private async Task<(decimal salesToday, int orderCount, int unpaidCount, decimal returnsTotal)> LoadDashboardStatsAsync()
        {
            var local = LoadDashboardStats();   // unpaid + returns always come from SQLite (no API for these yet)

            if (_onlineCache == true && _selectedCompanyId > 0)
            {
                try
                {
                    var apiRows = await SalesInvoiceApi.GetReprintInvoicesAsync(_selectedCompanyId, 1);
                    var todayRows = apiRows.Where(r => r.SaleDate.Date == DateTime.Today).ToList();
                    decimal apiSalesToday = todayRows.Sum(r => r.GrandTotal);
                    int apiOrderCount = todayRows.Count;
                    return (apiSalesToday, apiOrderCount, local.unpaidCount, local.returnsTotal);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("LoadDashboardStatsAsync API failed, falling back to SQLite: " + ex.Message);
                }
            }
            return local;
        }
        private async Task LoadRecentTransactionsAsync()
        {
            try
            {
                if (_onlineCache == true && _selectedCompanyId > 0)
                {
                    var apiRows = await SalesInvoiceApi.GetReprintInvoicesAsync(_selectedCompanyId, 1);
                    var todayRows = apiRows
                        .Where(r => r.SaleDate.Date == DateTime.Today)
                        .OrderByDescending(r => r.SaleDate)
                        .Take(50)
                        .ToList();

                    void BindApiRows()
                    {
                        dgvTransactions.Rows.Clear();

                        if (dgvTransactions.Columns.Count > 2)
                        {
                            dgvTransactions.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                            dgvTransactions.Columns[2].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                        }

                        if (todayRows.Count == 0)
                        {
                            dgvTransactions.Rows.Add("—", "No recent transactions", "", DateTime.Now.ToString("dd MMM hh:mm"), "—");
                        }
                        else
                        {
                            foreach (var r in todayRows)
                            {
                                decimal paid = r.PaidCash + r.PaidDigital + r.PaidCard;
                                string status = paid >= r.GrandTotal ? "Completed" : "Pending";
                                dgvTransactions.Rows.Add(
                                    r.InvoiceNo,
                                    string.IsNullOrWhiteSpace(r.CustomerName) ? "Walk-in" : r.CustomerName,
                                    FormatAmount(r.GrandTotal),
                                    r.SaleDate.ToString("dd MMM hh:mm tt"),
                                    status);
                            }
                        }
                    }

                    if (dgvTransactions.InvokeRequired) dgvTransactions.Invoke((Action)BindApiRows);
                    else BindApiRows();
                }
                else
                {
                    LoadPendingInvoicesGrid();   // offline fallback — existing SQLite path, unchanged
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadRecentTransactionsAsync failed, falling back to SQLite: " + ex.Message);
                LoadPendingInvoicesGrid();
            }
        }
        // ══════════════════════════════════════════════════════════════════
        // SHOW PAGE — embed forms/panels inside panelContent
        // ══════════════════════════════════════════════════════════════════
        private void ShowPage(Control page)
        {
            // Stop anything that might still touch dashboard-home controls
            _pieAnimTimer?.Stop();
            _topProdAnimTimer?.Stop();
            _statAnimTimer?.Stop();
            _chartAnimTimer?.Stop();

            // Tear down EVERYTHING currently in panelContent, not just _currentPage
            panelContent.SuspendLayout();
            foreach (Control c in panelContent.Controls.Cast<Control>().ToList())
            {
                panelContent.Controls.Remove(c);
                if (c is Form f)
                    f.Hide();                 // forms are reusable, don't dispose
                else if (!c.IsDisposed)
                    c.Dispose();              // dashboard-home panel & children
            }
            panelContent.ResumeLayout();

            // Null out ALL home-panel fields so any in-flight async callback bails out safely
            _payCard = null;
            _paymentPanel = null;
            _topProductsPanel = null;
            _statCards = new BufferedPanel[4];

            _currentPage = page;
            if (page == null) { ShowDashboardHome(); return; }

            if (page is Form form)
            {
                form.TopLevel = false;
                form.FormBorderStyle = FormBorderStyle.None;
                form.Dock = DockStyle.Fill;
                form.FormClosed += (s, e) => ShowDashboardHome();
            }
            else
            {
                page.Dock = DockStyle.Fill;
            }

            panelContent.Controls.Add(page);
            page.Show();
            page.BringToFront();
        }

        // ══════════════════════════════════════════════════════════════════
        // SAFE INVALIDATE HELPER
        // ══════════════════════════════════════════════════════════════════
        private void SafeInvalidate(Action invalidate)
        {
            if (this.IsDisposed) return;
            try { invalidate(); }
            catch (ObjectDisposedException) { /* control torn down mid-transition, ignore */ }
        }

        // ══════════════════════════════════════════════════════════════════
        // ANIMATE PROGRESS — now disposal-safe
        // ══════════════════════════════════════════════════════════════════
        private System.Windows.Forms.Timer AnimateProgress(
            System.Windows.Forms.Timer existingTimer,
            Action<float> setProgress,
            Action invalidate,
            int durationMs = 1700)
        {
            existingTimer?.Stop();
            existingTimer?.Dispose();
            setProgress(0f);
            SafeInvalidate(invalidate);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var timer = new System.Windows.Forms.Timer { Interval = 33 };
            timer.Tick += (s, e) =>
            {
                if (this.IsDisposed) { timer.Stop(); timer.Dispose(); return; }

                float t = Math.Min(1f, sw.ElapsedMilliseconds / (float)durationMs);
                setProgress(EaseOutCubic(t));
                SafeInvalidate(invalidate);
                if (t >= 1f) { timer.Stop(); timer.Dispose(); }
            };
            timer.Start();
            return timer;
        }

        // ══════════════════════════════════════════════════════════════════
        // REFRESH DASHBOARD — bail out if a page (not home) is currently showing
        // ══════════════════════════════════════════════════════════════════
        public async Task RefreshDashboardAsync()
        {
            if (this.IsDisposed || _currentPage != null) return; // don't touch dashboard-home widgets while a page is open

            try
            {
                string sym = _currencySymbol ?? "P";
                await Task.Run(() => LoadPaymentMethodData(sym));

                if (this.IsDisposed || _currentPage != null) return; // re-check after await

                await LoadDashboardWidgetsAsync();

                if (this.IsDisposed || _currentPage != null) return;

                var (salesVals, salesLabels) = await Task.Run(() => LoadSalesOverviewData());
                _salesChartVals = salesVals;
                _salesChartLabels = salesLabels;
                _salesChartMax = Math.Max(10m, _salesChartVals.Max() * 1.15m);

                if (this.IsDisposed || _currentPage != null) return;

                await LoadRecentTransactionsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RefreshDashboardAsync Error: " + ex.Message);
            }
        }

        private void ShowDashboardHome()
        {
            if (_currentPage == null) return;
            _currentPage = null;
            _statCards = new BufferedPanel[4];
            panelContent.Controls.Clear();
            BuildContent();
            _ = LoadRecentTransactionsAsync();
            SetActiveNav(btnNavDashboard);
        }
         

        // Helper to safely call RebuildInner
        // Remove the call to RebuildInnerIfExists from RefreshDashboardAsync (already done above)
        // Keep the method but it should only be called on manual full reloads, not auto-refresh


        // ── Dashboard home ────────────────────────────────────────────────
        private void btnNavDashboard_Click(object sender, EventArgs e)
        {
            SetActiveNav(btnNavDashboard);
            ShowDashboardHome();
        }

        // ── Sales ─────────────────────────────────────────────────────────
        private void btnNavSales_Click(object sender, EventArgs e)
        {
            if (_selectedCompanyId <= 0)
            {
                MessageBox.Show("Please select a company before opening Sales.",
                    "No Company Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetActiveNav((Button)sender);

            if (_salesFormInstance == null || _salesFormInstance.IsDisposed)
            {
                _salesFormInstance = new SalesForm(_selectedCompanyId);
                _salesFormInstance._useD365 = true;
            }
            ShowPage(_salesFormInstance);
        }

        // ── Inventory ─────────────────────────────────────────────────────
        private void btnNavInventory_Click(object sender, EventArgs e)
        {
            SetActiveNav((Button)sender);
            ShowPage(new ProductCatalogForm(ApiBaseUrl));
        }

        // ── Sales Return ──────────────────────────────────────────────────
        private void btnNavSalesReturn_Click(object sender, EventArgs e)
        {
            if (_selectedCompanyId <= 0)
            {
                MessageBox.Show("Please select a company before opening Sales Return.",
                    "No Company Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetActiveNav(btnNavSalesReturn);

            if (_salesReturnFormInstance == null || _salesReturnFormInstance.IsDisposed)
            {
                _salesReturnFormInstance = new SalesReturnForm(
                    _selectedCompanyId, _currencySymbol, _companyName,
                    _companyAddress ?? "", _companyPhone ?? "",
                    _companyVat ?? "", _companyWebsite ?? "", _salesOfficeInfo ?? "");
            }

            ShowPage(_salesReturnFormInstance);
        }

        // ── Pending Invoices ──────────────────────────────────────────────
        private void btnPending_Click(object sender, EventArgs e)
        {
            if (_selectedCompanyId <= 0)
            {
                MessageBox.Show("Please select a company first.",
                    "No Company Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_salesFormInstance == null || _salesFormInstance.IsDisposed)
            {
                _salesFormInstance = new SalesForm(_selectedCompanyId);
                _salesFormInstance._useD365 = true;
            }
            string sym = _salesFormInstance._currencySymbol;
            ShowPage(new PendingInvoicesForm(_selectedCompanyId, sym, _salesFormInstance));
        }

        // ── Float Entry ───────────────────────────────────────────────────
        private void btnNavFloat_Click(object sender, EventArgs e)
        {
            SetActiveNav(btnNavFloat);
            ShowPage(new FloatManagerForm(
                _selectedCompanyId, CurrentUser.UserInfo.UserID,
                _currencySymbol, _companyName));
        }

        // ── Tender Declaration ────────────────────────────────────────────
        private void btnNavTenderDeclaration_Click(object sender, EventArgs e)
        {
            SetActiveNav((Button)sender);
            ShowPage(new TenderDeclarationForm(CurrentUser.UserInfo.UserID, _currencySymbol ?? "P"));
        }

        // ── Close Shift ───────────────────────────────────────────────────
        private void btnNavCloseShift_Click(object sender, EventArgs e)
        {
            SetActiveNav(btnNavCloseShift);
            ShowPage(new CloseShiftForm(
                CurrentUser.UserInfo.UserID, _selectedCompanyId,
                _companyName, _currencySymbol ?? "P"));
            RefreshCloseShiftBtn();
        }

        // ── Settings ──────────────────────────────────────────────────────
        private void btnSettings_Click(object sender, EventArgs e)
        {
            SetActiveNav((Button)sender);
            ShowPage(new PrinterSettingsForm());
        }

        // ── Reports submenu ───────────────────────────────────────────────
        private void btnSubDayEnd_Click(object sender, EventArgs e)
        {
            ShowPage(new DayEndReportForm(
                DateTime.Today, _selectedCompanyId, _companyName, "P"));
        }

        private void btnSubReturnReport_Click(object sender, EventArgs e)
        {
            ShowPage(new SalesReturnReportForm(
                companyId: _selectedCompanyId,
                company: _companyName,
                currency: _currencySymbol,
                from: DateTime.Now.Date,
                to: DateTime.Now.Date.AddDays(1)));
        }

        // ══════════════════════════════════════════════════════════════════
        // STATUS INDICATOR  (refined pill style)
        // ══════════════════════════════════════════════════════════════════
        private void BuildStatusIndicator()
        {
            int logoBottom = 66;
            foreach (Control c in panelSidebar.Controls)
            {
                if (c.Name.Equals("picLogo", StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Equals("lblLogo", StringComparison.OrdinalIgnoreCase))
                { logoBottom = c.Bottom + 6; break; }
            }

            _statusDot = new Panel
            {
                Size = new Size(8, 8),
                BackColor = C_Checking,
                Location = new Point(50, logoBottom + 16),
                Tag = "statusDot"
            };
            _statusDot.Region = MakeCircleRegion(8);

            _statusLabel = new Label
            {
                Text = "Checking…",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = C_Checking,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(_statusDot.Right + 10, logoBottom + 8),
                Tag = "statusLabel"
            };

            panelSidebar.Controls.Add(_statusDot);
            panelSidebar.Controls.Add(_statusLabel);
            _statusDot.BringToFront();
            _statusLabel.BringToFront();
            BuildLogoGlow();
        }

        private void LoadCompanyInfo()
        {
        //    _companyName = "Radical Investment Pty Ltd";
        //    _companyAddress = "FLO-TEK Pipes & Irrigation, PO Box 10723, Lobatse Botswana, BWA";
        //    _companyPhone = "";
        //    _companyVat = "BW00000724614-00-05-17";
        //    _companyWebsite = "www.flotekafrica.com";
        //    _salesOfficeInfo =
        //        "Gaborone Sales office|Phone: +267 3972001/3/4|Fax: +267 3872014" +
        //        "||" +
        //        "Phakalane Sales Office|Phone: +267 3972001|Fax: +267 3872014" +
        //        "||" +
        //        "Francistown Sales office|Phone: +267 2410248|Fax: +267 2410249";
        }

        private void BuildLogoGlow()
        {
            foreach (Control c in panelSidebar.Controls)
            {
                if (c.Name.Equals("picLogo", StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Equals("lblLogo", StringComparison.OrdinalIgnoreCase))
                { _logoControl = c; break; }
            }
             
            // FIXED — only repaints the small glow area around the logo
            _glowTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _glowTimer.Tick += (s, _) =>
            {
                _glowAlpha += _glowRising ? 4f : -4f;
                if (_glowAlpha >= 180f) { _glowAlpha = 180f; _glowRising = false; }
                if (_glowAlpha <= 20f) { _glowAlpha = 20f; _glowRising = true; }

                if (_logoControl != null)
                {
                    int cx = _logoControl.Left + _logoControl.Width / 2;
                    int cy = _logoControl.Top + _logoControl.Height / 2;
                    const int r = 64;
                    panelSidebar.Invalidate(new Rectangle(cx - r, cy - r, r * 2, r * 2));
                }
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════
        private static string FormatRupee(decimal amount)
        {
            var culture = new System.Globalization.CultureInfo("en-IN");
            return "Rs " + amount.ToString("##,##,##0", culture);
        }

        private void LoadPendingInvoicesGrid()
        {
            //try
            //{
            //    if (dgvTransactions == null) return;

            //    using var conn = new SQLiteConnection($"Data Source={DbPath()};Version=3;");
            //    conn.Open();

            //    const string sql = @"
            //SELECT InvoiceNo, CustomerName, GrandTotal, SaleDate, Status
            //FROM PendingInvoice
            //WHERE CompanyID = @CompanyID
            //ORDER BY SaleDate DESC
            //LIMIT 50;";

            //    using var cmd = new SQLiteCommand(sql, conn);
            //    cmd.Parameters.AddWithValue("@CompanyID",
            //        _selectedCompanyId > 0 ? _selectedCompanyId : CurrentUser.CompanyID);

            //    using var reader = cmd.ExecuteReader();

            //    var rows = new List<(string, string, string, string, string)>();

            //    while (reader.Read())
            //    {
            //        string inv = reader["InvoiceNo"]?.ToString() ?? "";
            //        string cust = reader["CustomerName"]?.ToString() ?? "Walk-in";
            //        decimal tot = reader.IsDBNull("GrandTotal") ? 0m : Convert.ToDecimal(reader["GrandTotal"]);
            //        string amt = FormatAmount(tot);
            //        string rawDt = reader["SaleDate"]?.ToString() ?? "";
            //        string dt = DateTime.TryParse(rawDt, out var d)
            //            ? d.ToString("dd MMM hh:mm tt")
            //            : rawDt; 

            //        rows.Add((inv, cust, amt, dt));
            //    }

            //    // Bind on UI thread
            //    void BindRows()
            //    {
            //        dgvTransactions.Rows.Clear();

            //        // Right-align Amount column (index 2)
            //        if (dgvTransactions.Columns.Count > 2)
            //        {
            //            dgvTransactions.Columns[2].DefaultCellStyle.Alignment =
            //                DataGridViewContentAlignment.MiddleRight;
            //            dgvTransactions.Columns[2].HeaderCell.Style.Alignment =
            //                DataGridViewContentAlignment.MiddleRight;
            //        }

            //        if (rows.Count == 0)
            //            dgvTransactions.Rows.Add("—", "No recent transactions", "", DateTime.Now.ToString("dd MMM hh:mm"), "—");
            //        else
            //            foreach (var r in rows)
            //                dgvTransactions.Rows.Add(r.Item1, r.Item2, r.Item3, r.Item4, r.Item5);
            //    }

            //    if (dgvTransactions.InvokeRequired)
            //        dgvTransactions.Invoke((Action)BindRows);
            //    else
            //        BindRows();
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine("LoadPendingInvoicesGrid Error: " + ex.Message);
            //    // Fallback: don't leave grid completely empty
            //    if (dgvTransactions != null && !dgvTransactions.InvokeRequired)
            //    {
            //        dgvTransactions.Rows.Clear();
            //        dgvTransactions.Rows.Add("—", "Error loading data", "", "", "—");
            //    }
            //}
        }

        private async Task LoadTransactionsAsync()
        {
            try
            {
                if (_selectedCompanyId <= 0) return;
                var api = new ApiService();
                string url = $"api/payment/{_selectedCompanyId}/{_selectedStoreId}";
                string json = await api.GetAsync(url);
                if (string.IsNullOrEmpty(json)) return;
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var response = JsonSerializer.Deserialize<ApiResponse<List<PaymentTransactionDto>>>(json, options);
                if (response == null || !response.IsSuccess) return;
                var result = response.Data;
                if (result == null || result.Count == 0) return;
                if (dgvTransactions.InvokeRequired)
                    dgvTransactions.Invoke((Action)(() => dgvTransactions.DataSource = result));
                else
                    dgvTransactions.DataSource = result;
            }
            catch (Exception ex) { Debug.WriteLine("Transactions grid load failed: " + ex.Message); }
        }

        private static GraphicsPath MakeCirclePath(int cx, int cy, int radius)
        {
            var path = new GraphicsPath();
            path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
            return path;
        }

        private void ShowStatus(string message, bool? online)
        {
            Color dotColor = online == null ? C_Checking
                           : online.Value ? C_Online
                                           : C_Offline;
            void Apply()
            {
                if (_statusDot == null || _statusDot.IsDisposed ||
        _statusLabel == null || _statusLabel.IsDisposed) return;
                if (_statusDot == null || _statusLabel == null) return;
                _statusDot.BackColor = dotColor;
                _statusLabel.Text = message;
                _statusLabel.ForeColor = dotColor;

                if (_glowTimer != null)
                {
                    if (online == true) { _glowTimer.Stop(); _glowAlpha = 0f; panelSidebar.Invalidate(); }
                    else if (online == false) { _glowTimer.Interval = 18; _glowAlpha = 20f; _glowRising = true; _glowTimer.Tag = "offline"; _glowTimer.Start(); }
                    else { _glowTimer.Stop(); _glowAlpha = 0f; panelSidebar.Invalidate(); }
                }
                if (online.HasValue) PulseDot();
            }
            if (IsDisposed) return;
            if (InvokeRequired) Invoke((Action)Apply); else Apply();
            if (InvokeRequired) Invoke((Action)Apply); else Apply();
        }

        private void PulseDot()
        {
            if (_statusDot == null) return;
            Point orig = _statusDot.Location;
            _statusDot.Size = new Size(12, 12);
            _statusDot.Region = MakeCircleRegion(12);
            _statusDot.Location = new Point(orig.X - 2, orig.Y - 2);
            var t = new System.Windows.Forms.Timer { Interval = 250 };
            t.Tick += (s, _) =>
            {
                t.Stop(); t.Dispose();
                if (_statusDot == null) return;
                _statusDot.Size = new Size(8, 8);
                _statusDot.Region = MakeCircleRegion(8);
                _statusDot.Location = orig;
            };
            t.Start();
        }

        // ══════════════════════════════════════════════════════════════════
        // CONNECTIVITY
        // ══════════════════════════════════════════════════════════════════
        //private const string ApiBaseUrl = "https://localhost:7022";
        //private const string ApiBaseUrl = "https://purplemoonapi.mythitsolutions.co.in";

        //private const string ApiBaseUrl = "https://Shriposapi.mythitsolutions.co.in";

        private static string ApiBaseUrl => AppConfig.BaseUrl;



        private bool GetOnline()
        {
            if (_onlineCache.HasValue &&
                (DateTime.UtcNow - _onlineChecked).TotalSeconds < ONLINE_CACHE_SECONDS)
                return _onlineCache.Value;
            _onlineCache = IsOnline();
            _onlineChecked = DateTime.UtcNow;
            return _onlineCache.Value;
        }

        private static bool IsOnline()
        {
            try
            {
                var uri = new Uri(ApiBaseUrl);
                int port = uri.Port > 0 ? uri.Port
                         : uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
                using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var ar = sock.BeginConnect(uri.Host, port, null, null);
                bool connected = ar.AsyncWaitHandle.WaitOne(1_000);
                if (connected) sock.EndConnect(ar);
                return connected;
            }
            catch { return false; }
        }

        // ══════════════════════════════════════════════════════════════════
        // TOP-BAR DROPDOWNS
        // ══════════════════════════════════════════════════════════════════
        private void InitializeTopRightDropdowns()
        {
            lblCompanyTag = MakeTagLabel("Company");
            cmbCompany = MakeCombo();
            cmbCompany.SelectedIndexChanged += cmbCompany_SelectedIndexChanged;
            DB(cmbCompany);      // ← ADD
            DB(lblCompanyTag);

            lblStoreTag = MakeTagLabel("Store");
            cmbStore = MakeCombo();
            DB(cmbStore);        // ← ADD
            DB(lblStoreTag);
            cmbStore.SelectedIndexChanged += (s, _) =>
            {
                if (cmbStore.SelectedValue != null)
                    int.TryParse(cmbStore.SelectedValue.ToString(), out _selectedStoreId);
            };

            lblDate = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true
            };

            panelTopBar.Controls.AddRange(new Control[]
                { lblCompanyTag, cmbCompany, lblStoreTag, cmbStore, lblDate });

            panelTopBar.Resize += (s, _) => PositionTopRightControls();
            PositionTopRightControls();
        }

        private Label MakeTagLabel(string text) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 164, 194),
            BackColor = Color.Transparent,
            AutoSize = true
        };

        private ComboBox MakeCombo() => new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextDark,
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Width = 160,
            Height = 26
        };

        private void PositionTopRightControls()
        {
            if (cmbCompany == null || cmbStore == null) return;
            const int marginRight = 18, groupGap = 18, labelGap = 5;
            int barH = panelTopBar.Height;

            int dateX = panelTopBar.Width - lblDate.Width - marginRight;
            lblDate.Location = new Point(dateX, (barH - lblDate.Height) / 2);

            PlaceComboAndLabel(cmbStore, lblStoreTag, lblDate.Left - groupGap, barH, labelGap);
            PlaceComboAndLabel(cmbCompany, lblCompanyTag, lblStoreTag.Left - groupGap, barH, labelGap);
        }

        private static void PlaceComboAndLabel(ComboBox cmb, Label lbl, int rightEdge, int barH, int gap)
        {
            cmb.Location = new Point(rightEdge - cmb.Width, (barH - cmb.Height) / 2);
            lbl.Location = new Point(cmb.Left - gap - lbl.Width, (barH - lbl.Height) / 2 + 1);
        }

        private void AutoSizeCombo(ComboBox cmb)
        {
            if (cmb?.Items.Count == 0) return;
            int maxW = 0;
            using (var g = cmb.CreateGraphics())
                foreach (var item in cmb.Items)
                {
                    int w = (int)g.MeasureString(item.ToString(), cmb.Font).Width;
                    if (w > maxW) maxW = w;
                }
            cmb.Width = maxW + 42;
            PositionTopRightControls();
        }

        // ══════════════════════════════════════════════════════════════════
        // DATE / TIME
        // ══════════════════════════════════════════════════════════════════
        private void UpdateDateTime()
        {
            string dateStr = "\U0001f4c5  " + DateTime.Now.ToString("ddd, dd MMM yyyy   hh:mm tt");
            if (lblDate != null) lblDate.Text = dateStr;
            if (lblDateText != null) lblDateText.Text = DateTime.Now.ToString("ddd, dd MMM yyyy  HH:mm");
            PositionTopRightControls();
        }

        // ══════════════════════════════════════════════════════════════════
        // API — COMPANY
        // ══════════════════════════════════════════════════════════════════
        private async Task LoadCompanyDataAsync()
        {
            List<Company> companies = _onlineCache == true
                ? await TryFetchCompaniesFromApiAsync()
                : await LoadCompaniesFromSQLiteAsync();

            if (companies?.Count > 0)
            {
                BindCompaniesToCombo(companies);

                // SelectedIndexChanged may not fire if the first bound item is
                // already the selected one, so make sure store data still loads.
                if (_selectedCompanyId > 0)
                    await LoadStoreDataAsync(_selectedCompanyId);
            }
        }

        private async Task<List<Company>> LoadCompaniesFromSQLiteAsync()
        {
            string dbPath = DbPath();
            if (!System.IO.File.Exists(dbPath)) return null;
            try
            {
                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                await conn.OpenAsync();

                // Offline/local login often leaves CurrentUser.CompanyID as 0,
                // which caused "WHERE CompanyID = 0" to match nothing.
                // Fall back to loading all companies when we don't have a known ID yet.
                string sql = CurrentUser.CompanyID > 0
                    ? "SELECT CompanyID, Name FROM CompanyMaster WHERE CompanyID = @CompanyID;"
                    : "SELECT CompanyID, Name FROM CompanyMaster;";

                using var cmd = new SQLiteCommand(sql, conn);
                if (CurrentUser.CompanyID > 0)
                    cmd.Parameters.AddWithValue("@CompanyID", CurrentUser.CompanyID);

                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<Company>();
                while (await reader.ReadAsync())
                    list.Add(new Company
                    {
                        CompanyId = Convert.ToInt32(reader["CompanyID"]),
                        CompanyName = reader["Name"]?.ToString()?.Trim() ?? "Unknown"
                    });
                return list;
            }
            catch (Exception ex) { Debug.WriteLine("SQLite companies failed: " + ex); return null; }
        }

        private void BindCompaniesToCombo(List<Company> companies)
        {
            cmbCompany.DataSource = companies;
            cmbCompany.DisplayMember = "CompanyName";
            cmbCompany.ValueMember = "CompanyId";

            if (companies.Any(c => c.CompanyId == CurrentUser.CompanyID))
                cmbCompany.SelectedValue = CurrentUser.CompanyID;

            if (cmbCompany.SelectedItem is Company c)
            {
                _companyName = c.CompanyName ?? " ";
                _selectedCompanyId = c.CompanyId;   // set directly, don't rely only on SelectedIndexChanged
            }

            AutoSizeCombo(cmbCompany);
        }

        private async Task<List<Company>> TryFetchCompaniesFromApiAsync()
        {
            try
            {
                var api = new ApiService();
                string url = $"api/companies/get/{CurrentUser.CompanyID}";
                var response = await api.GetAsync(url);
                if (string.IsNullOrEmpty(response)) return null;
                var result = JsonSerializer.Deserialize<ApiResponse<Company>>(response,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result?.IsSuccess == true && result.Data != null
                    ? new List<Company> { result.Data } : null;
            }
            catch (Exception ex) { Debug.WriteLine("API companies failed: " + ex.Message); return null; }
        }
 

       

        // ══════════════════════════════════════════════════════════════════
        // API — STORE
        // ══════════════════════════════════════════════════════════════════
        private async Task LoadStoreDataAsync(int companyId)
        {
            if (companyId <= 0) return;
            List<Store> stores = _onlineCache == true
                ? await TryFetchStoresFromApiAsync(companyId)
                : await LoadStoresFromSQLiteAsync(companyId);
            if (stores?.Count > 0) BindStoresToCombo(stores);
        }

        private async Task<List<Store>> TryFetchStoresFromApiAsync(int companyId)
        {
            try
            {
                var api = new ApiService();
                string url = $"api/store/get/{CurrentUser.CompanyID}";
                var response = await api.GetAsync(url);
                if (string.IsNullOrEmpty(response)) return null;
                var result = JsonSerializer.Deserialize<ApiResponse<Store>>(response,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result?.IsSuccess == true && result.Data != null
                    ? new List<Store> { result.Data } : null;
            }
            catch (Exception ex) { Debug.WriteLine("API stores failed: " + ex.Message); return null; }
        }

        private async Task<List<Store>> LoadStoresFromSQLiteAsync(int companyId)
        {
            string dbPath = DbPath();
            if (!System.IO.File.Exists(dbPath)) return null;
            try
            {
                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                await conn.OpenAsync();
                const string sql = "SELECT StoreID, StoreName FROM Store WHERE CompanyID = @id;";
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", companyId);
                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<Store>();
                while (await reader.ReadAsync())
                    list.Add(new Store
                    {
                        StoreID = Convert.ToInt32(reader["StoreID"]),
                        StoreName = reader["StoreName"]?.ToString()?.Trim() ?? "Unknown"
                    });
                return list;
            }
            catch (Exception ex) { Debug.WriteLine("SQLite stores failed: " + ex); return null; }
        }

        private void BindStoresToCombo(List<Store> stores)
        {
            cmbStore.DataSource = stores;
            cmbStore.DisplayMember = "StoreName";
            cmbStore.ValueMember = "StoreID";
            if (cmbStore.SelectedValue != null)
                int.TryParse(cmbStore.SelectedValue.ToString(), out _selectedStoreId);
            AutoSizeCombo(cmbStore);
        }

        private async void cmbCompany_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbCompany.SelectedItem is not Company selected) return;
            _selectedCompanyId = selected.CompanyId;
            _companyName = selected.CompanyName ?? " ";
            await LoadStoreDataAsync(selected.CompanyId);
            await LoadRecentTransactionsAsync();
        }

        // ══════════════════════════════════════════════════════════════════
        // GRID STYLING
        // ══════════════════════════════════════════════════════════════════
        private void StyleGrid(DataGridView dg)
        {
            dg.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 255);
            dg.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            dg.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            dg.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(247, 250, 255);
            dg.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dg.ColumnHeadersHeight = 34;
            dg.EnableHeadersVisualStyles = false;

            dg.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            dg.DefaultCellStyle.ForeColor = TextDark;
            dg.DefaultCellStyle.BackColor = CardWhite;
            dg.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 244, 255);
            dg.DefaultCellStyle.SelectionForeColor = TextDark;
            dg.DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

            dg.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 251, 255);
            dg.RowTemplate.Height = 34;
            dg.BorderStyle = BorderStyle.None;
            dg.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dg.GridColor = Color.FromArgb(236, 240, 248);
            dg.BackgroundColor = CardWhite;
            dg.RowHeadersVisible = false;
            dg.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            dg.CellFormatting += (s, e) =>
            {
                if (dg.Columns[e.ColumnIndex].Name == "Status" && e.Value != null)
                {
                    switch (e.Value.ToString())
                    {
                        case "Completed": e.CellStyle.ForeColor = C_Green; break;
                        case "Processed": e.CellStyle.ForeColor = C_Purple; break;
                        case "Pending": e.CellStyle.ForeColor = C_Amber; break;
                    }
                    e.CellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                }
            };
            dg.ColumnAdded += (s, e) =>
            {
                if (e.Column.HeaderText == "Amount")
                {
                    e.Column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    e.Column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            };
        }

        private void SeedGridData()
        {
            var rows = new[]
            {
                ("07/06/2022", "Rs 200", "Sales",        "Completed"),
                ("07/05/2022", "Rs 250", "Sales",        "Completed"),
                ("07/04/2022", "Rs 180", "Sales Return", "Processed"),
                ("07/03/2022", "Rs 400", "Sales",        "Completed"),
                ("07/02/2022", "Rs 320", "Accounting",   "Pending"),
                ("07/01/2022", "Rs 150", "Sales Return", "Processed"),
            };
            foreach (var (d, a, t, st) in rows)
                dgvTransactions.Rows.Add(d, a, t, st);
        }

        // ══════════════════════════════════════════════════════════════════
        // PAINTING
        // ══════════════════════════════════════════════════════════════════
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.Clear(BgPage); }

        private void panelSidebar_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Gradient fill
            using var br = new LinearGradientBrush(
                new Point(0, 0), new Point(0, panelSidebar.Height),
                Color.FromArgb(16, 32, 72), Color.FromArgb(8, 16, 40));
            g.FillRectangle(br, panelSidebar.ClientRectangle);

            // Glow effect
            if (_glowTimer != null && _glowTimer.Enabled && _logoControl != null)
            {
                int cx = _logoControl.Left + _logoControl.Width / 2;
                int cy = _logoControl.Top + _logoControl.Height / 2;
                Color glowColor = _glowTimer.Tag as string == "offline" ? C_Offline : C_Online;
                int[] radii = { 52, 40, 28, 18 };
                float[] alphaDiv = { 5.5f, 3.8f, 2.4f, 1.5f };
                for (int i = 0; i < radii.Length; i++)
                {
                    int alpha = (int)(_glowAlpha / alphaDiv[i]);
                    alpha = Math.Max(0, Math.Min(255, alpha));
                    int r = radii[i];
                    using var glowBrush = new SolidBrush(Color.FromArgb(alpha, glowColor));
                    g.FillEllipse(glowBrush, cx - r, cy - r, r * 2, r * 2);
                }
            }

            // Separator lines
            using var pen = new Pen(Color.FromArgb(38, 255, 255, 255), 1f);
            g.DrawLine(pen, 14, 74, panelSidebar.Width - 14, 74);
            g.DrawLine(pen, 14, panelSidebar.Height - 58, panelSidebar.Width - 14, panelSidebar.Height - 58);
        }

        private void panelTopBar_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(CardWhite);
            using var pen = new Pen(Color.FromArgb(218, 226, 240), 1f);
            e.Graphics.DrawLine(pen, 0, panelTopBar.Height - 1, panelTopBar.Width, panelTopBar.Height - 1);
        }

        private void PaintStatCard(object sender, PaintEventArgs e) => PaintCard((Panel)sender, e, false);
        private void PaintGridCard(object sender, PaintEventArgs e) => PaintCard((Panel)sender, e, false);
        private void PaintModuleCard(object sender, PaintEventArgs e) => PaintCard((Panel)sender, e, true);

        private void PaintCard(Panel p, PaintEventArgs e, bool isModule)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(CardWhite);
            using var path = MakeRoundedPath(p.ClientRectangle, 12);
            if (isModule)
            {
                using var bg = new SolidBrush(CardWhite); g.FillPath(bg, path);
                using var acc = new SolidBrush(AccentBlue); g.FillRectangle(acc, 0, 0, p.Width, 4);
            }
            else { using var bg = new SolidBrush(CardWhite); g.FillPath(bg, path); }
            using var border = new Pen(Color.FromArgb(215, 224, 242), 1f);
            using var bPath = MakeRoundedPath(new Rectangle(1, 1, p.Width - 2, p.Height - 2), 12);
            g.DrawPath(border, bPath);
        }

        // ── Payment Methods ───────────────────────────────────────────────
        private void LoadPaymentMethodData(string currencySymbol)
        {
            try
            {
                string today = DateTime.Today.ToString("yyyy-MM-dd");

                using var conn = new SQLiteConnection($"Data Source={DbPath()};Version=3;");
                conn.Open();

                // Step 1: Get the authoritative total from SOInvoiceHeader (matches stat card exactly)
                decimal grandTotal = 0m;
                const string totalSql = @"
            SELECT COALESCE(SUM(TotalInvoiceAmount), 0)
            FROM SOInvoiceHeader
            WHERE PostingDate = @today AND SalesStatus != 0;";
                using (var cmd = new SQLiteCommand(totalSql, conn))
                {
                    cmd.Parameters.AddWithValue("@today", today);
                    grandTotal = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
                }

                // Step 2: Get payment breakdown slices
                var rows = new List<(string Method, decimal Amount)>();
                const string breakdownSql = @"
    SELECT p.PaymentType AS PaymentMethod, SUM(p.PaymentAmount) AS TotalAmount
    FROM SOInvoicePayment p
    INNER JOIN SOInvoiceHeader h ON h.InvoiceID = p.InvoiceID
    WHERE h.PostingDate = @today
      AND h.SalesStatus != 0
      AND p.PaymentDate LIKE @todayLike
    GROUP BY p.PaymentType
    ORDER BY TotalAmount DESC;";

                using (var cmd = new SQLiteCommand(breakdownSql, conn))
                {
                    cmd.Parameters.AddWithValue("@today", today);
                    cmd.Parameters.AddWithValue("@todayLike", today + "%");
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string method = reader["PaymentMethod"]?.ToString()?.Trim() ?? "Other";
                        decimal amount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0m
                                       : Convert.ToDecimal(reader["TotalAmount"]);
                        if (amount <= 0) continue;
                        rows.Add((method, amount));
                    }
                }

                // Step 3: If no breakdown available, show as single slice
                if (rows.Count == 0 && grandTotal > 0)
                    rows.Add(("Sales", grandTotal));
                // Step 4: Cap slice amounts so they never exceed grandTotal
                decimal paymentSum = rows.Sum(r => r.Amount);

                _paymentData = rows.Select((r, i) => new PaymentSlice
                {
                    Label = r.Method,
                    Amount = paymentSum > 0
         ? FormatAmount(Math.Round(r.Amount / paymentSum * grandTotal, 2))  // ✅ uses FormatAmount (fixed above)
         : FormatAmount(r.Amount),
                    Percentage = paymentSum > 0
         ? (double)Math.Round(r.Amount / paymentSum * 100, 1)
         : 0d
                }).ToList();

                _paymentTotal = FormatAmount(grandTotal);  // ✅ uses FormatAmount
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadPaymentMethodData: " + ex.Message);
                _paymentData = new List<PaymentSlice>();
                _paymentTotal = $"{_currencySymbol} 0.00";
            }

            void RepaintPay()
            {
                if (this.IsDisposed || _currentPage != null) return;
                _pieAnimTimer = AnimateProgress(_pieAnimTimer, v => _pieAnimProgress = v,
                    () => { _payCard?.Invalidate(); _paymentPanel?.Invalidate(); },
                    durationMs: 1700);
            }
            if (InvokeRequired) BeginInvoke((Action)RepaintPay);
            else RepaintPay();
        }

        private void PaintPaymentMethods(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bgBr = new SolidBrush(CardWhite);
            using var bgPath = MakeRoundedPath(p.ClientRectangle, 12);
            g.FillPath(bgBr, bgPath);
            g.DrawString("Payment Methods", F_H2, new SolidBrush(TextDark), new PointF(16, 16));

            if (_paymentData == null || _paymentData.Count == 0)
            {
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("No payment data", F_Body, new SolidBrush(TextMuted),
                    new RectangleF(0, 0, p.Width, p.Height), sf);
                return;
            }

            var pays = _paymentData.Select((d, i) => (
                Label: d.Label,
                Pct: d.Percentage,
                Color: _pieColors[i % _pieColors.Length],
                Amount: d.Amount
            )).ToArray();

            string total = _paymentTotal ?? "P 0";
            int cx = p.Width / 2, cy = 140, outerR = 58, innerR = 40;

            // ── Single pass: draw slice AND its arrow together ──────────────────
            float angle = -90f;
            float totalSweepAllowed = 360f * _pieAnimProgress;
            float sweepUsed = 0f;

            foreach (var slice in pays)
            {
                float fullSweep = (float)(slice.Pct * 3.6);
                float remaining = totalSweepAllowed - sweepUsed;
                float sweep = Math.Max(0, Math.Min(fullSweep, remaining));
                sweepUsed += fullSweep; // advance by full amount so later slices wait their turn

                if (sweep <= 0) { angle += fullSweep; continue; }

                using var br = new SolidBrush(slice.Color);
                g.FillPie(br, cx - outerR, cy - outerR, outerR * 2, outerR * 2, angle, sweep);

                // Only draw arrow/label once this slice has fully appeared
                if (sweep >= fullSweep - 0.5f)
                {
                    float midAngle = angle + fullSweep / 2f;
                    double rad = midAngle * Math.PI / 180.0;
                    float midR = (outerR + innerR) / 2f;
                    float sx = cx + (float)(Math.Cos(rad) * midR);
                    float sy = cy + (float)(Math.Sin(rad) * midR);
                    float elbowR = outerR + 14;
                    float ex = cx + (float)(Math.Cos(rad) * elbowR);
                    float ey = cy + (float)(Math.Sin(rad) * elbowR);
                    bool rightSide = Math.Cos(rad) >= 0;
                    float labelX = ex + (rightSide ? 22 : -22);
                    float labelY = ey;

                    using var linePen = new Pen(slice.Color, 1.4f);
                    g.DrawLine(linePen, sx, sy, ex, ey);
                    g.DrawLine(linePen, ex, ey, labelX, labelY);

                    float arrowSize = 5f;
                    var dir = new PointF((float)Math.Cos(rad), (float)Math.Sin(rad));
                    var inward = new PointF(-dir.X, -dir.Y);
                    var perp = new PointF(-dir.Y, dir.X);
                    var back1 = new PointF(sx - inward.X * arrowSize + perp.X * arrowSize * 0.6f,
                                            sy - inward.Y * arrowSize + perp.Y * arrowSize * 0.6f);
                    var back2 = new PointF(sx - inward.X * arrowSize - perp.X * arrowSize * 0.6f,
                                            sy - inward.Y * arrowSize - perp.Y * arrowSize * 0.6f);
                    var arrowTip = new PointF(sx + inward.X * arrowSize, sy + inward.Y * arrowSize);
                    using var arrowBrush = new SolidBrush(slice.Color);
                    g.FillPolygon(arrowBrush, new[] { arrowTip, back1, back2 });

                    string nameText = slice.Label;
                    var textSize = g.MeasureString(nameText, F_Micro);
                    float textX = rightSide ? labelX + 2 : labelX - textSize.Width - 2;
                    g.DrawString(nameText, F_Micro, new SolidBrush(slice.Color), new PointF(textX, labelY - textSize.Height / 2));
                }

                angle += fullSweep;
            }

            // Donut hole
            g.FillEllipse(new SolidBrush(CardWhite), cx - innerR, cy - innerR, innerR * 2, innerR * 2);
            using var cSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(total, new Font("Segoe UI", 7F, FontStyle.Bold), new SolidBrush(TextDark),
                new RectangleF(cx - innerR, cy - innerR, innerR * 2, innerR * 2), cSf);

            // Legend
            int ly = cy + outerR + 18;
            foreach (var slice in pays)
            {
                g.FillEllipse(new SolidBrush(slice.Color), 16, ly + 4, 9, 9);
                g.DrawString(slice.Label, F_Small, new SolidBrush(TextMuted), new PointF(30, ly));
                using var sfR = new StringFormat { Alignment = StringAlignment.Far };
                g.DrawString($"{slice.Amount}  {slice.Pct:F1}%", F_Small, new SolidBrush(TextDark),
                    new RectangleF(0, ly, p.Width - 14, 17), sfR);
                ly += 21;
            }
        }
        //private void PaintPaymentMethods(object sender, PaintEventArgs e)
        //{
        //    var p = (Panel)sender;
        //    var g = e.Graphics;
        //    g.SmoothingMode = SmoothingMode.AntiAlias;

        //    using var bgBr = new SolidBrush(CardWhite);
        //    using var bgPath = MakeRoundedPath(p.ClientRectangle, 12);
        //    g.FillPath(bgBr, bgPath);

        //    g.DrawString("Payment Methods", F_H2, new SolidBrush(TextDark), new PointF(16, 16));
        //    // g.DrawString("Breakdown by type", F_Small, new SolidBrush(TextMuted), new PointF(16, 40));

        //    if (_paymentData == null || _paymentData.Count == 0)
        //    {
        //        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        //        g.DrawString("No payment data", F_Body, new SolidBrush(TextMuted),
        //            new RectangleF(0, 0, p.Width, p.Height), sf);
        //        return;
        //    }

        //    var pays = _paymentData.Select((d, i) => (
        //        Label: d.Label,
        //        Pct: d.Percentage,
        //        Color: _pieColors[i % _pieColors.Length],
        //        Amount: d.Amount
        //    )).ToArray();

        //    string total = _paymentTotal ?? "P 0";
        //    int cx = p.Width / 2, cy = 140, outerR = 58, innerR = 40;
        //    float startAngle = -90f;

        //    // Pie slices
        //    foreach (var slice in pays)
        //    {
        //        float sweep = (float)(slice.Pct * 3.6);
        //        if (sweep <= 0) continue;
        //        using var br = new SolidBrush(slice.Color);
        //        g.FillPie(br, cx - outerR, cy - outerR, outerR * 2, outerR * 2, startAngle, sweep);
        //        startAngle += sweep;
        //    }

        //    // Donut hole
        //    g.FillEllipse(new SolidBrush(CardWhite), cx - innerR, cy - innerR, innerR * 2, innerR * 2);
        //    using var cSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        //    g.DrawString(total, new Font("Segoe UI", 7F, FontStyle.Bold), new SolidBrush(TextDark),
        //        new RectangleF(cx - innerR, cy - innerR, innerR * 2, innerR * 2), cSf);

        //    // Legend
        //    int ly = cy + outerR + 18;
        //    foreach (var slice in pays)
        //    {
        //        g.FillEllipse(new SolidBrush(slice.Color), 16, ly + 4, 9, 9);
        //        g.DrawString(slice.Label, F_Small, new SolidBrush(TextMuted), new PointF(30, ly));
        //        using var sfR = new StringFormat { Alignment = StringAlignment.Far };
        //        g.DrawString($"{slice.Amount}  {slice.Pct:F1}%", F_Small, new SolidBrush(TextDark),
        //            new RectangleF(0, ly, p.Width - 14, 17), sfR);
        //        ly += 21;
        //    }
        //}

        // ══════════════════════════════════════════════════════════════════
        // NAV
        // ══════════════════════════════════════════════════════════════════
        private void SetActiveNav(Button btn)
        {
            if (btn == null) return;
            if (_activeNav != null)
            {
                _activeNav.BackColor = Color.Transparent;
                _activeNav.ForeColor = Color.FromArgb(148, 175, 218);
                _activeNav.Tag = _activeNav.Text.Trim();
            }
            _activeNav = btn;
            btn.BackColor = NavHover;
            btn.ForeColor = Color.White;
            btn.Tag = "active";
        }

        private void NavBtn_Click(object sender, EventArgs e) => SetActiveNav((Button)sender);
        private void NavBtn_MouseEnter(object sender, EventArgs e) { var b = (Button)sender; if (b != _activeNav) b.BackColor = Color.FromArgb(24, 52, 96); }
        private void NavBtn_MouseLeave(object sender, EventArgs e) { var b = (Button)sender; if (b != _activeNav) b.BackColor = Color.Transparent; }

        //private void btnNavSales_Click(object sender, EventArgs e)
        //{
        //    if (_selectedCompanyId <= 0)
        //    {
        //        MessageBox.Show("Please select a company before opening Sales.",
        //            "No Company Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //        return;
        //    }
        //    SetActiveNav((Button)sender);
        //    if (_salesFormInstance == null || _salesFormInstance.IsDisposed)
        //    {
        //        _salesFormInstance = new SalesForm(_selectedCompanyId);
        //        _salesFormInstance._useD365 = true;
        //    }
        //    _salesFormInstance.Show();
        //    _salesFormInstance.BringToFront();
        //    _salesFormInstance.Focus();
        //    if (_salesFormInstance.WindowState == FormWindowState.Minimized)
        //        _salesFormInstance.WindowState = FormWindowState.Normal;
        //}

      

        private void btnNavProfile_Click(object sender, EventArgs e)
            => panelProfileSubmenu.Visible = !panelProfileSubmenu.Visible;

      

        

        //private void btnNavFloat_Click(object sender, EventArgs e)
        //{
        //    SetActiveNav(btnNavFloat);
        //    new FloatManagerForm(_selectedCompanyId, CurrentUser.UserInfo.UserID, _currencySymbol, _companyName).Show();
        //}
        //private void btnPending_Click(object sender, EventArgs e)
        //{
        //    if (_salesFormInstance == null || _salesFormInstance.IsDisposed)
        //    {
        //        if (_selectedCompanyId <= 0)
        //        {
        //            MessageBox.Show("Please select a company first.",
        //                "No Company Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //            return;
        //        }
        //        _salesFormInstance = new SalesForm(_selectedCompanyId);
        //        _salesFormInstance._useD365 = true;
        //    }

        //    string sym = _salesFormInstance._currencySymbol;
        //    new PendingInvoicesForm(_selectedCompanyId, sym, _salesFormInstance).ShowDialog(this);
        //}

        //private void btnNavSalesReturn_Click(object sender, EventArgs e)
        //{
        //    SetActiveNav(btnNavSalesReturn);
        //    var form = new SalesReturnForm(
        //        _selectedCompanyId, _currencySymbol, _companyName,
        //        _companyAddress ?? "", _companyPhone ?? "",
        //        _companyVat ?? "", _companyWebsite ?? "", _salesOfficeInfo ?? "");
        //    form.Show(this);
        //    form.BringToFront();
        //}

        //private void btnSubReturnReport_Click(object sender, EventArgs e)
        //{
        //    new SalesReturnReportForm(
        //        companyId: _selectedCompanyId,
        //        company: _companyName,
        //        currency: _currencySymbol,
        //        from: DateTime.Now.Date,
        //        to: DateTime.Now.Date.AddDays(1)
        //    ).Show(this);
        //}

        // ══════════════════════════════════════════════════════════════════
        // WINDOW CONTROLS
        // ══════════════════════════════════════════════════════════════════
        private void btnClose_Click(object sender, EventArgs e) => Application.Exit();
        private void btnMinimize_Click(object sender, EventArgs e) => WindowState = FormWindowState.Minimized;

        private void btnMaximize_Click(object sender, EventArgs e)
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
            btnMaximize.Text = WindowState == FormWindowState.Maximized ? "\u2750" : "\u25a1";
        }

        private void btnClose_MouseEnter(object sender, EventArgs e) => btnClose.BackColor = Color.FromArgb(185, 28, 50);
        private void btnClose_MouseLeave(object sender, EventArgs e) => btnClose.BackColor = NavDark;
        private void btnMinimize_MouseEnter(object sender, EventArgs e) => btnMinimize.BackColor = Color.FromArgb(32, 56, 100);
        private void btnMinimize_MouseLeave(object sender, EventArgs e) => btnMinimize.BackColor = NavDark;
        private void btnMaximize_MouseEnter(object sender, EventArgs e) => btnMaximize.BackColor = Color.FromArgb(32, 56, 100);
        private void btnMaximize_MouseLeave(object sender, EventArgs e) => btnMaximize.BackColor = NavDark;

        private void btnLogout_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to logout?", "Logout",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                login.CurrentUser.Clear();   // clear session

                var loginForm = new login();
                loginForm.Show();

                // Close dashboard AFTER login is fully shown
                BeginInvoke((Action)(() => this.Close()));
            }
        }

        private void btnNavReports_Click(object sender, EventArgs e) => ShowReportsPopup();

        //private void btnSubDayEnd_Click(object sender, EventArgs e)
        //{
        //    new DayEndReportForm(DateTime.Today, _selectedCompanyId, _companyName, "P").Show(this);
        //}

        //private void btnNavCloseShift_Click(object sender, EventArgs e)
        //{
        //    SetActiveNav(btnNavCloseShift);
        //    new CloseShiftForm(CurrentUser.UserInfo.UserID, _selectedCompanyId, _companyName, _currencySymbol ?? "P")
        //        .ShowDialog(this);
        //    RefreshCloseShiftBtn();
        //}

        private void RefreshCloseShiftBtn()
        {
            if (btnNavCloseShift == null) return;
            if (ShiftState.IsOpen)
            {
                btnNavCloseShift.ForeColor = Color.FromArgb(250, 155, 155);
                btnNavCloseShift.Text = "  🔒   Close Shift  ●";
            }
            else
            {
                btnNavCloseShift.ForeColor = Color.FromArgb(148, 175, 218);
                btnNavCloseShift.Text = "  🔒   Close Shift";
            }
        }

        private void btnSubLogout_Click(object sender, EventArgs e) => btnLogout_Click(sender, e);
        private void btnLogout_MouseEnter(object sender, EventArgs e) => btnSubLogout.BackColor = Color.FromArgb(185, 28, 50);
        private void btnLogout_MouseLeave(object sender, EventArgs e) => btnSubLogout.BackColor = Color.Transparent;

        // ══════════════════════════════════════════════════════════════════
        // DASHBOARD WIDGETS
        // ══════════════════════════════════════════════════════════════════
        private async Task LoadDashboardWidgetsAsync()
        {
            if (this.IsDisposed || _currentPage != null) return;

            try
            {
                var (topProds, lowStock) = await Task.Run(() =>
                {
                    var tp = DashboardRepository.GetTopSellingProducts(
                        companyId: _selectedCompanyId, topN: 5,
                        from: DateTime.Today.AddDays(-30), to: DateTime.Today.AddDays(1));
                    var ls = DashboardRepository.GetLowStockAlerts(
                        companyId: _selectedCompanyId, storeId: _selectedStoreId, maxRows: 8);
                    return (tp, ls);
                });

                if (this.IsDisposed || _currentPage != null) return;

                string sym = _currencySymbol ?? "P";
                foreach (var prod in topProds) prod.PriceFormatted = $"{sym} {prod.UnitPrice:N2}";

                var stats = await LoadDashboardStatsAsync();

                if (this.IsDisposed || _currentPage != null) return;

                if (InvokeRequired) Invoke((Action)(() => ApplyWidgetData(topProds, lowStock, stats)));
                else ApplyWidgetData(topProds, lowStock, stats);
            }
            catch (Exception ex) { Debug.WriteLine("LoadDashboardWidgetsAsync: " + ex.Message); }
        }

        private void ApplyWidgetData(
            List<TopSellingProductDto> topProds,
            List<LowStockAlertDto> lowStock,
            (decimal salesToday, int orderCount, int unpaidCount, decimal returnsTotal) stats)
        {
            if (this.IsDisposed || _currentPage != null || _statCards == null || panelContent.IsDisposed) return;

            _topProductsData = topProds ?? new List<TopSellingProductDto>();
            _lowStockData = lowStock ?? new List<LowStockAlertDto>();

            string sym = _currencySymbol ?? "P";
            foreach (var prod in topProds)
                prod.PriceFormatted = $"{sym} {prod.UnitPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}";

            _dashStats = stats;

            _statAnimTimer = AnimateProgress(_statAnimTimer, v => _statAnimProgress = v,
                () => { if (_statCards != null) foreach (var sc in _statCards) sc?.Invalidate(); },
                durationMs: 1700);
            _topProdAnimTimer = AnimateProgress(_topProdAnimTimer, v => _topProdAnimProgress = v,
                () => _topProductsPanel?.Invalidate(),
                durationMs: 1700);

            _payCard?.Invalidate();
            _paymentPanel?.Invalidate();
        }
         

        private void btnNavPurchaseOrder_Click(object sender, EventArgs e)
        {
            SetActiveNav(btnNavPurchaseOrder);
            ShowPage(new POSAPP.Sales.PurchaseOrderForm(_selectedCompanyId, _currencySymbol));
        }



        // REPLACE existing ApplyWidgetData
        // REPLACE existing ApplyWidgetData
        private void ApplyWidgetData(List<TopSellingProductDto> topProds, List<LowStockAlertDto> lowStock)
        {
            _topProductsData = topProds ?? new List<TopSellingProductDto>();
            _lowStockData = lowStock ?? new List<LowStockAlertDto>();

            string sym = _currencySymbol ?? "P";
            foreach (var prod in topProds)
                prod.PriceFormatted = $"{sym} {prod.UnitPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}";

            _dashStats = LoadDashboardStats();

            _statAnimTimer = AnimateProgress(_statAnimTimer, v => _statAnimProgress = v,
        () => { if (_statCards != null) foreach (var sc in _statCards) sc?.Invalidate(); },
        durationMs: 1700);
            _topProdAnimTimer = AnimateProgress(_topProdAnimTimer, v => _topProdAnimProgress = v,
    () => _topProductsPanel?.Invalidate(),
    durationMs: 1700);

            _payCard?.Invalidate();
            _paymentPanel?.Invalidate();
        }
        // ══════════════════════════════════════════════════════════════════
        // MODULE HOVER
        // ══════════════════════════════════════════════════════════════════
        private void ModuleCard_MouseEnter(object sender, EventArgs e)
        {
            var p = (Panel)sender;
            foreach (Control c in p.Controls)
                if (c is Label lbl && c.Font.Size > 16) lbl.ForeColor = Color.White;
            p.BackColor = AccentBlue; p.Invalidate();
        }
        private void ModuleCard_MouseLeave(object sender, EventArgs e)
        {
            var p = (Panel)sender;
            foreach (Control c in p.Controls)
                if (c is Label lbl && c.Font.Size > 16) lbl.ForeColor = AccentBlue;
            p.BackColor = CardWhite; p.Invalidate();
        }

        // ══════════════════════════════════════════════════════════════════
        // DRAG
        // ══════════════════════════════════════════════════════════════════
        private void panelTitleBar_MouseDown(object sender, MouseEventArgs e)
        { _drag = true; _dragCursor = Cursor.Position; _dragForm = Location; }
        private void panelTitleBar_MouseMove(object sender, MouseEventArgs e)
        { if (_drag) Location = Point.Add(_dragForm, new Size(Point.Subtract(Cursor.Position, new Size(_dragCursor)))); }
        private void panelTitleBar_MouseUp(object sender, MouseEventArgs e) => _drag = false;
        private void panelTitleBar_DoubleClick(object sender, EventArgs e) => btnMaximize_Click(sender, e);

        // ══════════════════════════════════════════════════════════════════
        // UTILS
        // ══════════════════════════════════════════════════════════════════
        private static string DbPath() =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

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

        private static Region MakeCircleRegion(int diameter)
        {
            var path = new GraphicsPath();
            path.AddEllipse(0, 0, diameter, diameter);
            return new Region(path);
        }


        private static void DB(Control c) =>
    typeof(Control).GetProperty("DoubleBuffered",
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Instance)
        ?.SetValue(c, true, null);
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { btnLogout_Click(null, null); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

     protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_dashboardDataChangedHandler != null)
                DashboardEventBus.DataChanged -= _dashboardDataChangedHandler;
            _clockTimer?.Stop(); _clockTimer?.Dispose();
    _refreshTimer?.Stop(); _refreshTimer?.Dispose();
    _pieAnimTimer?.Stop(); _pieAnimTimer?.Dispose();
    _topProdAnimTimer?.Stop(); _topProdAnimTimer?.Dispose();
    _chartAnimTimer?.Stop(); _chartAnimTimer?.Dispose();
    _statAnimTimer?.Stop(); _statAnimTimer?.Dispose();
            _resizeDebounce?.Stop(); _resizeDebounce?.Dispose();
            base.OnFormClosed(e);
}
        private (decimal salesToday, int orderCount, int unpaidCount, decimal returnsTotal) LoadDashboardStats()
        {
            decimal salesToday = 0m;
            int orderCount = 0;
            int unpaidCount = 0;
            decimal returnsTotal = 0m;
            string today = DateTime.Today.ToString("yyyy-MM-dd");

            if (!System.IO.File.Exists(_dbPath)) return (salesToday, orderCount, unpaidCount, returnsTotal);

            try
            {
                using var conn = new System.Data.SQLite.SQLiteConnection(
                    $"Data Source={_dbPath};Version=3;");
                conn.Open();

                // Total sales today — use PostingDate
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT COALESCE(SUM(TotalInvoiceAmount), 0)
                FROM SOInvoiceHeader
                WHERE PostingDate = @today
                  AND SalesStatus != 0;";
                    cmd.Parameters.AddWithValue("@today", today);
                    salesToday = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
                }

                // Order count today — use PostingDate
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT COUNT(*)
                FROM SOInvoiceHeader
                WHERE PostingDate = @today
                  AND SalesStatus != 0;";
                    cmd.Parameters.AddWithValue("@today", today);
                    orderCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                }

                // Unpaid invoice count from PendingInvoices
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT COUNT(*)
                FROM PendingInvoice
                WHERE Status = 'Unpaid';";
                    try { unpaidCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0); }
                    catch { unpaidCount = 0; }
                }

                // Sales returns total today
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT COALESCE(SUM(RefundTotal), 0)
                FROM SalesReturnHeader
                WHERE DATE(ReturnDate) = @today;";
                    cmd.Parameters.AddWithValue("@today", today);
                    try { returnsTotal = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m); }
                    catch { returnsTotal = 0m; }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadDashboardStats: " + ex.Message);
            }

            return (salesToday, orderCount, unpaidCount, returnsTotal);
        }

        private (decimal[] vals, string[] labels) LoadSalesOverviewData()
        {
            var vals = new decimal[7];
            var labels = new string[7];
            var today = DateTime.Today;

            for (int i = 0; i < 7; i++)
            {
                var day = today.AddDays(-6 + i);
                labels[i] = day.ToString("ddd");
            }

            if (!System.IO.File.Exists(_dbPath))
                return (vals, labels);

            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();

                const string sql = @"
            SELECT PostingDate, COALESCE(SUM(TotalInvoiceAmount), 0) AS DayTotal
            FROM SOInvoiceHeader
            WHERE PostingDate BETWEEN @fromDate AND @toDate
              AND SalesStatus != 0
            GROUP BY PostingDate;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@fromDate", today.AddDays(-6).ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@toDate", today.ToString("yyyy-MM-dd"));

                var byDate = new Dictionary<string, decimal>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string dateStr = reader["PostingDate"]?.ToString() ?? "";
                    decimal total = reader.IsDBNull(reader.GetOrdinal("DayTotal"))
                        ? 0m : Convert.ToDecimal(reader["DayTotal"]);
                    if (DateTime.TryParse(dateStr, out var d))
                        byDate[d.ToString("yyyy-MM-dd")] = total;
                }

                for (int i = 0; i < 7; i++)
                {
                    var day = today.AddDays(-6 + i);
                    string key = day.ToString("yyyy-MM-dd");
                    vals[i] = byDate.TryGetValue(key, out var v) ? v : 0m;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadSalesOverviewData: " + ex.Message);
            }

            return (vals, labels);
        }
        private void StatCard_Click(int index)
        {
            switch (index)
            {
                case 0: // Total Sales Today
                    ShowStatDetail("Sales Today",
                        @"SELECT InvoiceNo, InvoiceAccountName, TotalInvoiceAmount, PostingDate
                  FROM SOInvoiceHeader
                  WHERE PostingDate = @today AND SalesStatus != 0
                  ORDER BY CreatedDate DESC;",
                        new[] { "Invoice No", "Customer", "Amount", "Date" });
                    break;

                case 1: // Sales Orders
                    ShowStatDetail("Sales Orders Today",
                        @"SELECT InvoiceNo, InvoiceAccountName, TotalInvoiceAmount, SalesStatus
                  FROM SOInvoiceHeader
                  WHERE PostingDate = @today AND SalesStatus != 0
                  ORDER BY CreatedDate DESC;",
                        new[] { "Invoice No", "Customer", "Amount", "Status" });
                    break;

                case 2: // Unpaid Invoices
                    ShowStatDetail("Unpaid Invoices",
                        @"SELECT InvoiceNo, CustomerName, GrandTotal, SaleDate
                  FROM PendingInvoice
                  WHERE Status = 'Unpaid'
                  ORDER BY SaleDate DESC;",
                        new[] { "Invoice No", "Customer", "Amount", "Date" });
                    break;

                case 3: // Sales Returns
                    ShowStatDetail("Sales Returns Today",
                        @"SELECT ReturnInvoiceNo, CustomerName, RefundTotal, ReturnDate
                  FROM SalesReturnHeader
                  WHERE DATE(ReturnDate) = @today
                  ORDER BY ReturnDate DESC;",
                        new[] { "Return No", "Customer", "Refund", "Date" });
                    break;
            }
        }

        private void ShowStatDetail(string title, string sql, string[] columns)
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");

            var dlg = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = C_White,
                Size = new System.Drawing.Size(700, 480),
                ShowInTaskbar = false,
                KeyPreview = true
            };

            // ── Header ─────────────────────────────────────────────────────
            var pnlHead = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = C_Navy
            };
            pnlHead.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new System.Drawing.Size(600, 50),
                Location = new System.Drawing.Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new System.Drawing.Size(50, 50),
                Location = new System.Drawing.Point(650, 0),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (s, e) => dlg.Close();
            pnlHead.Controls.Add(btnX);
            dlg.Controls.Add(pnlHead);

            // ── Grid ───────────────────────────────────────────────────────
            var dgv = new DataGridView
            {
                Location = new System.Drawing.Point(12, 62),
                Size = new System.Drawing.Size(676, 370),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = C_White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(236, 240, 248),
                Font = F_Body,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = C_Navy,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(6, 7, 4, 7),
                SelectionBackColor = C_Navy,      // ADD
                SelectionForeColor = Color.White  // ADD
            };
            dgv.EnableHeadersVisualStyles = false;
            dgv.DefaultCellStyle = new DataGridViewCellStyle
            {
                SelectionBackColor = Color.FromArgb(235, 244, 255),
                SelectionForeColor = C_Navy,
                ForeColor = C_Navy,
                BackColor = C_White,
                Padding = new Padding(6, 6, 4, 6)
            };
            dgv.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgv.ColumnHeadersHeight = 36;
            dgv.RowTemplate.Height = 32;
            dgv.EnableHeadersVisualStyles = false;

            foreach (var col in columns)
                dgv.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = col,
                    FillWeight = 100f / columns.Length
                });

            // ── Load data ──────────────────────────────────────────────────
            try
            {
                if (System.IO.File.Exists(_dbPath))
                {
                    using var conn = new System.Data.SQLite.SQLiteConnection(
                        $"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    if (sql.Contains("@today"))
                        cmd.Parameters.AddWithValue("@today", today);

                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var row = new string[columns.Length];
                        for (int i = 0; i < columns.Length; i++)
                        {
                            if (rdr.IsDBNull(i)) { row[i] = ""; continue; }
                            var val = rdr.GetValue(i);

                            // Format decimal/amount columns nicely
                            if (val is decimal d)
                                row[i] = FormatAmount(d);  // ✅ uses FormatAmount (fixed above)
                            else if (decimal.TryParse(val.ToString(), out decimal pd) &&
                                     (columns[i] == "Amount" || columns[i] == "Refund"))
                                row[i] = FormatAmount(pd);  // ✅ uses FormatAmount
                            else
                                row[i] = val.ToString();
                        }
                        dgv.Rows.Add(row);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading detail: " + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // ── Row count label ────────────────────────────────────────────
            var lblCount = new Label
            {
                Text = $"{dgv.Rows.Count} record(s)",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = C_Slate,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new System.Drawing.Point(16, 440),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            dlg.Controls.Add(dgv);
            dlg.Controls.Add(lblCount);
            dlg.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dlg.Close(); };
            dlg.ShowDialog(this);
        }
        // ══════════════════════════════════════════════════════════════════
        // DATA MODELS
        // ══════════════════════════════════════════════════════════════════
        public class Company
        {
            public int CompanyId { get; set; }
            [JsonPropertyName("name")]
            public string CompanyName { get; set; }
        }

        public class Store
        {
            public int StoreID { get; set; }
            public int CompanyID { get; set; }
            public string StoreName { get; set; }
        }

        public class PaymentSummary
        {
            [JsonPropertyName("label")] public string Label { get; set; }
            [JsonPropertyName("percentage")] public int Percentage { get; set; }
            [JsonPropertyName("amount")] public string Amount { get; set; }
            [JsonPropertyName("totalAmount")] public string TotalAmount { get; set; }
        }

        public class PaymentMethodDto
        {
            public string Label { get; set; }
            public decimal Amount { get; set; }
            public string AmountFormatted { get; set; }
            public int Percentage { get; set; }
            public string TotalAmount { get; set; }
        }

        public class PaymentTransactionDto
        {
            public int TransactionId { get; set; }
            public string PaymentMode { get; set; }
            public decimal Amount { get; set; }
            public DateTime TransactionDate { get; set; }
            public string Status { get; set; }
            public string CustomerName { get; set; }
            public string InvoiceNumber { get; set; }
        }

        private class PaymentSlice
        {
            public string Label { get; set; }
            public double Percentage { get; set; }
            public string Amount { get; set; }
        }
    }
}