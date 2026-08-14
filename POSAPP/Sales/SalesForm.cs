
using iText.Layout.Properties;
using Microsoft.Data.Sqlite;
using POSAPP.Invoice;
using POSAPP.Payment;
using POSAPP.Reports;
using POSAPP.Shift;
using QRCoder;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;
using static POSAPP.login;
using static POSAPP.Reports.InvoiceData;
using static System.Net.WebRequestMethods;
namespace POSAPP
{
    public partial class SalesForm : Form
    {
        // ── Palette ────────────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color PanelDark2 = Color.FromArgb(42, 46, 56);
        private static readonly Color PanelDark3 = Color.FromArgb(38, 42, 52);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color AccPurple = Color.FromArgb(167, 92, 237);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccCyan = Color.FromArgb(20, 184, 166);
        private static readonly Color AccYellow = Color.FromArgb(234, 179, 8);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);
        private static readonly Color InputBg = Color.FromArgb(28, 32, 42);

        // ── State ──────────────────────────────────────────────────────────────
        private decimal _subtotal = 0m;
        private decimal _taxRate = 0.14m;
        private bool _drag;
        private Point _dragCursor, _dragForm;
        private decimal _splitCash = 0m;
        private decimal _splitUpi = 0m;
        private decimal _splitCard = 0m;
        private string _activeSplit = "cash";
        private string _numpadBuffer = "";
        private bool _isCreditSale = false;
        private Panel _pnlSaleTypeToggle;
        private Panel _btnNormalSaleToggle;
        private Panel _btnCreditSaleToggle;
        private Label _lblNormalSaleToggle;
        private Label _lblCreditSaleToggle;
        private string _barcodeBuffer = "";
        private System.Windows.Forms.Timer _barcodeTimer;
        private bool _isSelecting = false;
        private bool _isTendering = false;
        private bool? _onlineCache = null;
        private DateTime _onlineChecked = DateTime.MinValue;
        private const int ONLINE_CACHE_SECONDS = 30;
        private string _selectedUpiMethodName = "UPI / QR";
        private string _customerNameValue = "";
        private List<CustomerFullDto> _customersList = new();
        private CustomerFullDto _selectedCustomer;
        private ComboBox _cmbCustomer;
        private const string DEFAULT_CUSTOMER_NAME = "Walk-in";
        private string _customerAddressValue = "";
        private decimal _customerDiscountPct = 0m;
        private string _salesOfficeInfo = "";
        private string _customerVatValue = "";
        private decimal _roundingIncrement = 0.05m;
        private List<UomDto> _uomMaster = new();
        private List<TaxDto> _taxMaster = new();
        private TaxDto _selectedTax = null;      // order-level tax, replaces fixed _taxRate
        private ComboBox _cmbTax;
        private System.Windows.Forms.Timer _stockSyncTimer;
        private bool _stockSyncInProgress = false;
        private const int STOCK_SYNC_INTERVAL_MS = 5 * 60 * 1000; // 5 minutes// order-level dropdown, sits near nudDiscount
        public bool IsQuotation { get; set; } = false;
        private bool IsEmbedded => !this.TopLevel;

        private System.Windows.Forms.Timer _productSyncTimer;
        private System.Windows.Forms.Timer _offlineOrderSyncTimer;
        private bool _productSyncInProgress = false;
        private bool _offlineSyncInProgress = false;


        private bool _lastOrderQueuedOffline = false;

        private const int PRODUCT_SYNC_INTERVAL_MS = 5 * 60 * 1000;      // 5 minutes
        private const int OFFLINE_ORDER_SYNC_INTERVAL_MS = 5 * 60 * 1000; // 5 minutes

        // Add this anywhere near the top of SalesForm class, with the other properties
        // public int CompanyId => _companyId;

        private const int DEFAULT_STORE_ID = 1;   // ← change to your real StoreID if needed
        private int _storeId = DEFAULT_STORE_ID;
        private Dictionary<string, int> _salesFrequency = new(StringComparer.OrdinalIgnoreCase);
        // Add this field at the top of SalesForm class with other state fields:
        private bool _wasCompletedFromPendingInvoice = false;
        private System.Windows.Forms.Timer _searchDebounce;
        // ── Hot items tooltip ──────────────────────────────────────────────────────
        private ToolTip _hotItemsTooltip;
        // ── Customer ───────────────────────────────────────────────────────────────
        // private string _customerAddressValue = "";
        private Panel _customerAddressWrapper;
        private TextBox txtCustomerAddress; // add this field


        // ── Product source toggle ──────────────────────────────────────────────
        // true  = load from D365 API  |  false = load from local API / SQLite
        internal bool _useD365;
        private bool _isD365Mode = false;
        private System.Windows.Forms.Timer _resizeDebounce;
        // ── Pending invoice mode ───────────────────────────────────────────────
        // When true: payment restricted to Cash / Card / Bank Transfer only
        internal bool _isPendingInvoiceMode = false;
        // When true, GrandTotal()/UpdateTotals() skip applying _taxRate — used for
        // Sales-Order-sourced pending invoices whose total already includes tax.
        private bool _taxAlreadyIncluded = false;
        private string _currentPendingSourceKey = null; // immune to lblInvoiceNo.Text being overwritten elsewhere

        // ── Float cash ────────────────────────────────────────────────────────
        private Label lblFloatDisplay;

        // ── Company settings ───────────────────────────────────────────────────
        internal string _currencySymbol = "P";
        private int _currencyId = 1;
        private decimal _defaultDiscountPct = 0m;
        private string _companyName = "";
        private string _companyVat = "";
        private string _companyWebsite;
        private List<ChargeDto> _chargesMaster = new();
        private List<SaleCharge> _charges = new();
        private bool _chargesAllocated = false;
        private Button btnCharges;

        // ── Merchant credentials ───────────────────────────────────────────────
        private string _snapScanCode = "";
        private string _yocoSlug = "";
        private string _zapperQrUrl = "";
        private string _mtnMoMoCode = "";
        private string _fnbEWalletNo = "";
        private string _orangeCode = "";
        private string _capitecMerch = "";
        private string _vodaPayCode = "";
        private string _eftBankName = "";
        private string _eftAccountNo = "";
        private string _companyAddress = "Company Address";
        private string _companyPhone = "Phonenumber";
        private Button btnPrintLast;
        private List<SalesOrderApi.BankAccountDto> _bankAccounts = new();
        private SalesOrderApi.BankAccountDto _selectedBankAccount = null;

        // ── Last sale reprint tracking ─────────────────────────────────────
        private ReceiptData _lastReceiptData = null;
        private bool _lastSaleWasPrinted = false;

        // ── Payment methods ────────────────────────────────────────────────────
        private List<PaymentMethodDto> _digitalPaymentMethods = new List<PaymentMethodDto>();

        // ── Customer names ─────────────────────────────────────────────────────
        private List<string> _customerNames = new List<string>();

        // ── Cart ───────────────────────────────────────────────────────────────
        private class CartItem
        {
            public string Name { get; set; }
            public int ItemId { get; set; }
            public decimal OriginalPrice { get; set; }
            public decimal Price { get; set; }
            public decimal Qty { get; set; }
            public decimal DiscountPct { get; set; }
            public string Barcode { get; set; }
            public int UOM { get; set; } = 1;
            public string UOMName { get; set; } = "";
            public List<UomDto> AvailableUOMs { get; set; } = new();

            // ── NEW: per-line tax ─────────────────────────────
            public int TaxId { get; set; } = 0;
            public string TaxCode { get; set; } = "";
            public decimal TaxPercentage { get; set; } = 0m;

            public decimal Total => Math.Round(Price * Qty * (1m - DiscountPct / 100m), 2);
            public decimal DiscountAmt => Math.Round(Price * Qty * (DiscountPct / 100m), 2);

            // Tax computed on the discounted (taxable) amount — mirrors calculateLine() in React
            public decimal TaxAmt => Math.Round(Total * (TaxPercentage / 100m), 2);
            public decimal TotalWithTax => Total + TaxAmt; 
            public decimal Charges { get; set; } = 0m;
             
        }
        private List<CartItem> _cart = new List<CartItem>();

        private class Product
        {
            public string Name { get; set; }
            public int ItemId { get; set; }
            public decimal Price { get; set; }
            public string Barcode { get; set; }
            public string Category { get; set; }
            public int UOM { get; set; } = 1;
            public List<UomDto> AvailableUOMs { get; set; } = new();
            public int SalesTaxID { get; set; }
        }
        private class SaleCharge
        {
            public int ChargesID { get; set; }
            public string ChargesName { get; set; } = "";
            public decimal Amount { get; set; }
            public int Type { get; set; } = 1;   // 1 Fixed, 2 ByQty, 3 Equally
        }
        private class D365ProductDetail
        {
            public string DataAreaId { get; set; } = "";
            public string ItemId { get; set; } = "";
            public string NameAlias { get; set; } = "";
            public string OnHandModifiedDateTime { get; set; } = "";
            public decimal AvailPhysical { get; set; }
            public string InventLocationId { get; set; } = "";
            public decimal Amount { get; set; }
            public string InventSiteId { get; set; } = "";
            public string WMSLocationId { get; set; } = "";
            public string AccountRelation { get; set; } = "";
            public string ODataEtag { get; set; } = "";
        }
        private Dictionary<string, List<D365ProductDetail>> _d365Details =
        new Dictionary<string, List<D365ProductDetail>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Product> _barcodeMap =
            new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        // ── Stock cache (from API, not local SQLite) ──────────────────────────
        private Dictionary<string, (decimal onHand, decimal reserved)> _stockCache =
            new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
        private bool _stockCacheLoaded = false;
        private List<Product> _catalog = new List<Product>();
        internal readonly int _companyId;

        // ── Wrappers / dropdown ────────────────────────────────────────────────
        private ListBox _customerDropdown;
        private Panel _customerWrapper;
        private Panel _searchWrapper;
        private Panel _barcodeWrapper;
        private DayEndScheduler _scheduler;
        private Label lblNumpadDisplay;

        // ── DB path ───────────────────────────────────────────────────────────
        private static readonly string _dbPath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ── API URL ───────────────────────────────────────────────────────────
        private static string ApiBaseUrl => AppConfig.BaseUrl.TrimEnd('/');

        // ── Constructor ───────────────────────────────────────────────────────
        public SalesForm(int companyId)
        {
            _companyId = companyId;
            InitializeComponent();
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.DoubleBuffer, true);
            this.UpdateStyles();
            _barcodeTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _barcodeTimer.Tick += BarcodeTimer_Tick;
        }
        public SalesForm() : this(0) { }

        // ══════════════════════════════════════════════════════════════════════
        //  LOAD
        // ══════════════════════════════════════════════════════════════════════
        private async void SalesForm_Load(object sender, EventArgs e)
        {

            //if (IsEmbedded)
            //{
            //    panelHeader.Visible = false;
            //    tblRoot.Dock = DockStyle.Fill;   // reclaim the header's vertical space
            //                                     // Move the search/barcode boxes into the search card instead, since
            //                                     // there's no header bar to host them when embedded
            //    MoveSearchControlsIntoLeftPanel();
            //}
            SalesOrderApi.BaseUrl = ApiBaseUrl;
            lblOperator.Text = "ADMIN";
            POSAPP.Printer.StockSettings.Load();
            if (string.IsNullOrWhiteSpace(lblInvoiceNo.Text) || lblInvoiceNo.Text == "INV-")
                lblInvoiceNo.Text = "";
            this.BeginInvoke(new Action(() =>
            {
                BuildAutocomplete();
                BuildHotItems();
                SetD365Mode(true);


                // ENSURE invoice label matches mode after products load
                if (lblInvoiceNo.Text.StartsWith("INV-"))
                    lblInvoiceNo.Text = "";   // ← ADD THIS
            }));

            // BuildHotItems() lays out its grid using panelHotItems.ClientSize.Width
            // at call time, but the cards it creates are plain Top|Left-anchored
            // panels, so they don't reflow on their own when the left column is
            // resized (e.g. dragging/maximizing the window). Debounce a rebuild
            // on resize so the grid stays correctly columned at any width,
            // without rebuilding on every intermediate pixel while dragging.
            int lastHotItemsWidth = panelHotItems.ClientSize.Width;
            var hotItemsResizeDebounce = new System.Windows.Forms.Timer { Interval = 150 };
            hotItemsResizeDebounce.Tick += (s, ev) =>
            {
                hotItemsResizeDebounce.Stop();
                if (panelHotItems.ClientSize.Width != lastHotItemsWidth)
                {
                    lastHotItemsWidth = panelHotItems.ClientSize.Width;
                    BuildHotItems();
                }
            };
            panelHotItems.Resize += (s, ev) =>
            {
                hotItemsResizeDebounce.Stop();
                hotItemsResizeDebounce.Start();
            };

            lblTime.Text = DateTime.Now.ToString("HH:mm");
            lblDate.Text = DateTime.Now.ToString("ddd, dd MMM");

            var clock = new System.Windows.Forms.Timer { Interval = 30000 };
            clock.Tick += (s, ev) =>
            {
                lblTime.Text = DateTime.Now.ToString("HH:mm");
                lblDate.Text = DateTime.Now.ToString("ddd, dd MMM");
            };
            clock.Start();

            txtSearch.TextChanged += TxtSearch_TextChanged;
            txtSearch.KeyDown += TxtSearch_KeyDown;
            txtBarcode.KeyDown += TxtBarcode_KeyDown;
            txtBarcode.KeyPress += TxtBarcode_KeyPress;
            txtCustomer.TextChanged += TxtCustomer_TextChanged;
            txtCustomer.KeyDown += TxtCustomer_KeyDown;

            ApplyModernStyle(txtSearch, "Search products…", AccBlue, out _searchWrapper);
            ApplyModernStyle(txtBarcode, "Scan barcode…", AccCyan, out _barcodeWrapper);
            ApplyModernStyle(txtCustomer, "Search customer…", AccPurple, out _customerWrapper);
            _ = LoadStockCacheAsync();
            _ = LoadUomMasterAsync();
            _ = LoadTaxMasterAsync();
            _ = LoadChargesMasterAsync();
            _ = LoadBankAccountsAsync();
            BuildCustomerSelectDropdown();
            //panelDiscountCard.Resize += (s, e) =>
            //{
            //    int w = panelDiscountCard.Width - 20;
            //    if (_cmbCustomer != null) _cmbCustomer.Width = w;
            //    if (_cmbTax != null) _cmbTax.Width = w;
            //    if (_pnlSaleTypeToggle != null)
            //    {
            //        _pnlSaleTypeToggle.Width = w;
            //        int half = w / 2;
            //        _btnNormalSaleToggle.Width = half;
            //        _btnCreditSaleToggle.Location = new Point(half, 0);
            //        _btnCreditSaleToggle.Width = w - half;
            //    }
            //};
            _ = LoadCustomersAsync();
            this.Controls.Add(listSearchResults);
            listSearchResults.BringToFront();

            BuildCustomerDropdown();
            SetActiveSplit("cash");
            LoadCompanyInfo();

            if (!System.IO.File.Exists(_dbPath))
            {
                ShowStatus($"Database not found: {_dbPath}", false);
                MessageBox.Show(
                    $"Database file not found!\n\nExpected:\n{_dbPath}\n\n" +
                    "Copy ShriPOS.db next to POSAPP.exe and restart.",
                    "Database Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // ── Phase 1: fast local load — form is interactive after this line ────────
            ShowStatus("Loading products…", true);
            await Task.Run(() => LoadSalesFrequency());
            await LoadProductsFromLocalCacheAsync();

            // ── Phase 2: refresh from API in background, then keep syncing every 5 min ─
            _ = RefreshProductsFromApiAsync();
            StartProductSyncTimer();
            //StartOfflineOrderSyncTimer();
            StartStockSyncTimer();
            nudDiscount.Value = _defaultDiscountPct;
            RefreshCart();
            UpdateTotals();
            BuildGrandTotalBigLabel();
            BuildStockReductionLabel();
            RepositionTitleButtons();
            panelLeft.Resize += (s, e) =>
            {
                int fixedH = panelSearchCard.Height + panelDiscountCard.Height + 12; // margins
                int availH = panelLeft.ClientSize.Height - fixedH;
                if (availH < 200) return;

                // Hot items gets ~55% of what's left, recent sales gets the rest via Dock=Fill
                int hotH = Math.Max(180, (int)(availH * 0.55));
                panelHotCard.Height = hotH;
            };
            this.Resize += (s, e) =>
            {
                _resizeDebounce?.Stop();
                _resizeDebounce?.Dispose();
                _resizeDebounce = new System.Windows.Forms.Timer { Interval = 150 };
                _resizeDebounce.Tick += (ts, te) =>
                {
                    _resizeDebounce.Stop();
                    _resizeDebounce.Dispose();
                    _resizeDebounce = null;

                    if (this.IsDisposed || !this.IsHandleCreated) return;

                    RepositionTitleButtons();
                    RepositionDropdown();
                    RepositionSearchResults();
                };
                _resizeDebounce.Start();
            };
            this.KeyPreview = true;
            btnMax_Click(sender, e);

            try { SalesRepository.EnsureSchema(); }
            catch (Exception ex) { ShowStatus("DB schema error: " + ex.Message, false); }

            try { SalesRepository.EnsurePendingInvoiceSchema(); }
            catch (Exception ex) { ShowStatus("Pending schema error: " + ex.Message, false); }

            try
            {
                SalesRepository.EnsureRecentSalesSchema();
                LoadTodayRecentSales();
            }
            catch (Exception ex) { ShowStatus("Recent sales load error: " + ex.Message, false); }

            //try
            //{
            //    _scheduler = new DayEndScheduler(this, _companyId, _companyName, _currencySymbol);
            //    _scheduler.Start();
            //}
            //catch (Exception ex) { ShowStatus("Scheduler error: " + ex.Message, false); }

            // In SalesForm_Load, find where btnPrintLast is added and add click handler:
            // In SalesForm_Load — REPLACE the btnPrintLast creation block:
            btnPrintLast = new Button
            {
                Text = "🖨  Reprint",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 200),
                BackColor = Color.FromArgb(55, 60, 78),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(104, 28),
                Cursor = Cursors.Hand,
                Visible = true,
                Name = "btnPrintLast"
            };
            btnPrintLast.FlatAppearance.BorderSize = 0;
            btnPrintLast.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 76, 96);
            btnPrintLast.Click += (s, e) => ShowReprintHistory();
            try
            {
                ShiftState.LoadFromDb(_companyId);
            }
            catch (Exception ex) { ShowStatus("Shift load error: " + ex.Message, false); }

            BuildFloatFooterLabel();
            panelFooterBar.Controls.Add(btnPrintLast);
            panelFooterBar.SizeChanged += (s, ev) => PositionFooterButtons();
            PositionFooterButtons();
            BuildNumpadDisplay();
            BuildChargesButton();
            // panelLeft.PerformLayout();
            // _ = SyncProductsFromApiInBackgroundAsync();   // syncs API → SQLite silently
        }
        private void BuildChargesButton()
        {
            btnCharges = new Button
            {
                Text = "➕ Charges",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(55, 60, 78),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(120, 28),
                Cursor = Cursors.Hand,
                Name = "btnCharges"
            };
            btnCharges.FlatAppearance.BorderSize = 0;
            btnCharges.Click += (s, e) => ShowChargesDialog();
            panelFooterBar.Controls.Add(btnCharges);
            RefreshChargesButtonLabel();
        }

        private void RefreshChargesButtonLabel()
        {
            if (btnCharges == null) return;
            decimal total = _charges.Sum(c => c.Amount);
            btnCharges.Text = _charges.Count == 0 ? "➕ Charges"
                : (_chargesAllocated ? $"✓ Charges {Fmt(total)}" : $"⚠ Charges {Fmt(total)}");
            btnCharges.BackColor = _charges.Count == 0 ? Color.FromArgb(55, 60, 78)
                : (_chargesAllocated ? AccGreen : AccOrange);
        }
        //private void MoveSearchControlsIntoLeftPanel()
        //{
        //    // Detach from the (now-hidden) header
        //    panelHeader.Controls.Remove(txtSearch);
        //    panelHeader.Controls.Remove(txtBarcode);
        //    panelHeader.Controls.Remove(lblSearchHeader);
        //    panelHeader.Controls.Remove(lblBarcodeHeader);
        //    panelHeader.Controls.Remove(lblSearchSep);
        //    panelHeader.Controls.Remove(lblBarcodeSep);

        //    // Detach lblStatus from wherever it currently lives
        //    panelSearchCard.Controls.Remove(lblStatus);
        //    panelSearchCard.Controls.Clear(); // wipe anything left over from the old single-row layout

        //    panelSearchCard.Padding = new Padding(10, 8, 10, 8);

        //    // Build three explicit row panels, each Dock=Top, added in reverse
        //    // visual order (last docked-Top control added ends up at the TOP).
        //    var rowStatus = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
        //    lblStatus.Dock = DockStyle.Fill;
        //    lblStatus.AutoSize = false;
        //    lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        //    rowStatus.Controls.Add(lblStatus);

        //    var rowBarcode = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 0) };
        //    txtBarcode.Dock = DockStyle.Fill;
        //    txtBarcode.BorderStyle = BorderStyle.FixedSingle;
        //    txtBarcode.Font = new Font("Consolas", 9F);
        //    rowBarcode.Controls.Add(txtBarcode);

        //    var rowSearch = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 0) };
        //    txtSearch.Dock = DockStyle.Fill;
        //    txtSearch.BorderStyle = BorderStyle.FixedSingle;
        //    txtSearch.Font = new Font("Segoe UI", 9F);
        //    rowSearch.Controls.Add(txtSearch);

        //    // Add in order: search (top), barcode (middle), status (bottom)
        //    panelSearchCard.Controls.Add(rowStatus);
        //    panelSearchCard.Controls.Add(rowBarcode);
        //    panelSearchCard.Controls.Add(rowSearch);

        //    // Panel auto-sizes to its docked children — no manual height math needed
        //    panelSearchCard.Height =
        //        panelSearchCard.Padding.Vertical
        //        + rowSearch.Height + rowBarcode.Height + rowStatus.Height
        //        + rowBarcode.Margin.Top + rowStatus.Margin.Top;
        //}
        private Dictionary<string, int> _nameToItemId = new(StringComparer.OrdinalIgnoreCase);

        private async Task LoadBankAccountsAsync()
        {
            try
            {
                _bankAccounts = await SalesOrderApi.GetAllBanksAsync(_companyId).ConfigureAwait(true);
                Debug.WriteLine($"LoadBankAccountsAsync: {_bankAccounts.Count} bank accounts loaded.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadBankAccountsAsync: " + ex.Message);
            }
        }
        private async Task LoadChargesMasterAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                var resp = await http.GetAsync($"{ApiBaseUrl}/api/charges/by-company?companyId={_companyId}")
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);
                JsonElement arr = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;

                _chargesMaster = JsonSerializer.Deserialize<List<ChargeDto>>(arr.GetRawText(), opts) ?? new();
            }
            catch (Exception ex) { Debug.WriteLine("LoadChargesMasterAsync: " + ex.Message); }
        }
        private async Task LoadItemIdMapAsync()
        {
            try
            {
                var idByName = await SalesOrderApi.GetItemNameMapAsync().ConfigureAwait(true);

                if (idByName != null && idByName.Count > 0)
                {
                    _nameToItemId = idByName
                        .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

                    Debug.WriteLine($"LoadItemIdMapAsync: {_nameToItemId.Count} item ids mapped (from API).");

                    // Persist for offline use
                    await SaveItemIdMapToLocalAsync(_nameToItemId).ConfigureAwait(true);
                    return;
                }

                throw new Exception("API returned empty item map.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadItemIdMapAsync (API failed): " + ex.Message);

                // ── Offline / API failure fallback — use last-known local snapshot ──
                _nameToItemId = await LoadItemIdMapFromLocalAsync().ConfigureAwait(true);
                Debug.WriteLine($"LoadItemIdMapAsync: {_nameToItemId.Count} item ids mapped (from local cache fallback).");
            }
        }

        private int ResolveItemId(string name, string barcode)
        {
            if (_nameToItemId.TryGetValue(name ?? "", out int id)) return id;
            return 0;   // unresolved — do NOT guess using barcode, it's not the same numbering
        }
        private void BuildCustomerSelectDropdown()
        {
            _cmbCustomer = new ComboBox
            {
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(20, 20, 24),
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "CustomerName",
                ValueMember = "CustomerID",
                Size = new Size(panelDiscountCard.Width - 20, 36),
                Location = new Point(10, 8),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _cmbCustomer.SelectedIndexChanged += CmbCustomer_SelectedIndexChanged;

            panelDiscountCard.Controls.Add(_cmbCustomer);
            _cmbCustomer.BringToFront();
        }
        private void BuildTaxDropdown()
        {
            if (_cmbTax != null) return; // already built

            int comboTop = (_cmbCustomer?.Bottom ?? 8) + 8;

            _cmbTax = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(panelDiscountCard.Width - 20, 32),
                Location = new Point(10, comboTop),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };

            _cmbTax.Items.Add("No Tax");
            foreach (var t in _taxMaster)
                _cmbTax.Items.Add($"{t.TaxCode} ({t.TaxPercentage:F1}%)");

            _cmbTax.SelectedIndexChanged += (s, e) =>
            {
                int idx = _cmbTax.SelectedIndex;
                _selectedTax = (idx <= 0 || idx - 1 >= _taxMaster.Count) ? null : _taxMaster[idx - 1];
                _taxRate = (_selectedTax?.TaxPercentage ?? 0m) / 100m;
                UpdateTotals();
            };

            panelDiscountCard.Controls.Add(_cmbTax);
            _cmbTax.BringToFront();

            // ── Grow the panel to actually contain the new combo, and push
            //    whatever sits below it (Quick Add / Hot Items) down to match ──
            int neededHeight = _cmbTax.Bottom + 8;
            int delta = neededHeight - panelDiscountCard.Height;
            if (delta > 0)
            {
                panelDiscountCard.Height = neededHeight;

                if (panelHotItems != null
                    && panelHotItems.Parent == panelDiscountCard.Parent
                    && panelHotItems.Top >= panelDiscountCard.Top)
                {
                    panelHotItems.Top += delta;
                    panelHotItems.Height = Math.Max(60, panelHotItems.Height - delta);
                }
            }

            _cmbTax.SelectedIndex = 0;
            BuildSaleTypeToggle(); // set after layout so the change handler fires cleanly
        }

        private void BuildSaleTypeToggle()
        {
            if (_pnlSaleTypeToggle != null) return; // already built
            if (_cmbTax == null) return;

            int top = _cmbTax.Bottom + 10;

            _pnlSaleTypeToggle = new Panel
            {
                Size = new Size(panelDiscountCard.Width - 20, 36),
                Location = new Point(10, top),
                BackColor = Color.FromArgb(20, 22, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _pnlSaleTypeToggle.Region = MakeRoundedRegion(_pnlSaleTypeToggle.Size, 9);

            int halfW = _pnlSaleTypeToggle.Width / 2;

            _btnNormalSaleToggle = new Panel
            {
                Size = new Size(halfW, 36),
                Location = new Point(0, 0),
                BackColor = AccGreen,
                Cursor = Cursors.Hand
            };
            _lblNormalSaleToggle = new Label
            {
                Text = "💰 Payment",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnNormalSaleToggle.Controls.Add(_lblNormalSaleToggle);

            _btnCreditSaleToggle = new Panel
            {
                Size = new Size(_pnlSaleTypeToggle.Width - halfW, 36),
                Location = new Point(halfW, 0),
                BackColor = Color.FromArgb(20, 22, 28),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _lblCreditSaleToggle = new Label
            {
                Text = "🧾 Credit",              // shortened — "Sale" is redundant next to "Payment"
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnCreditSaleToggle.Controls.Add(_lblCreditSaleToggle);

            EventHandler selectNormal = (s, e) => SetCreditSaleMode(false);
            EventHandler selectCredit = (s, e) => SetCreditSaleMode(true);

            _btnNormalSaleToggle.Click += selectNormal;
            _lblNormalSaleToggle.Click += selectNormal;
            _btnCreditSaleToggle.Click += selectCredit;
            _lblCreditSaleToggle.Click += selectCredit;

            _pnlSaleTypeToggle.Controls.Add(_btnNormalSaleToggle);
            _pnlSaleTypeToggle.Controls.Add(_btnCreditSaleToggle);
            panelDiscountCard.Controls.Add(_pnlSaleTypeToggle);
            _pnlSaleTypeToggle.BringToFront();

            RefreshSaleTypeToggleVisual();

            // Grow the panel to fit, push whatever sits below it down to match
            int neededHeight = _pnlSaleTypeToggle.Bottom + 8;
            int delta = neededHeight - panelDiscountCard.Height;
            if (delta > 0)
            {
                panelDiscountCard.Height = neededHeight;
                if (panelHotItems != null
                    && panelHotItems.Parent == panelDiscountCard.Parent
                    && panelHotItems.Top >= panelDiscountCard.Top)
                {
                    panelHotItems.Top += delta;
                    panelHotItems.Height = Math.Max(60, panelHotItems.Height - delta);
                }
            }
        }

        private void RefreshSaleTypeToggleVisual()
        {
            if (_btnNormalSaleToggle == null || _btnCreditSaleToggle == null) return;

            if (_isCreditSale)
            {
                _btnCreditSaleToggle.BackColor = AccPurple;
                _lblCreditSaleToggle.ForeColor = Color.White;
                _btnNormalSaleToggle.BackColor = Color.FromArgb(20, 22, 28);
                _lblNormalSaleToggle.ForeColor = TextMuted;
            }
            else
            {
                _btnNormalSaleToggle.BackColor = AccGreen;
                _lblNormalSaleToggle.ForeColor = Color.White;
                _btnCreditSaleToggle.BackColor = Color.FromArgb(20, 22, 28);
                _lblCreditSaleToggle.ForeColor = TextMuted;
            }
        }

        private void SetCreditSaleMode(bool creditSale)
        {
            _isCreditSale = creditSale;
            _splitCash = 0m; _splitUpi = 0m; _splitCard = 0m; _numpadBuffer = "";
            _selectedBankAccount = null;

            RefreshSaleTypeToggleVisual();
            UpdateGrandTotalBigDisplay();

            ShowStatus(_isCreditSale
                ? "🧾 Credit Sale — Tender creates SO + Invoice only, no payment collected."
                : "Normal sale — payment will be collected in a popup at Tender.", true);
        }
        private const string DEFAULT_CUSTOMER_CODE = "WALKIN";   // ← adjust to match your real default customer code
        private void EnsureOfflineSyncSchema()
        {
            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath} ");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS D365ItemCache (
                        CompanyID     INTEGER NOT NULL,
                        Barcode       TEXT    NOT NULL,
                        ItemName      TEXT,
                        SellingPrice  REAL,
                        Category      TEXT,
                        LastSyncUtc   TEXT,
                        PRIMARY KEY (CompanyID, Barcode)
                    );

                    CREATE TABLE IF NOT EXISTS PendingSalesOrders (
                        PKId            INTEGER PRIMARY KEY AUTOINCREMENT,
                        CompanyID       INTEGER NOT NULL,
                        InvoiceNo       TEXT    NOT NULL,
                        PayloadJson     TEXT    NOT NULL,
                        CreatedUtc      TEXT    NOT NULL,
                        Synced          INTEGER NOT NULL DEFAULT 0,
                        LastAttemptUtc  TEXT,
                        LastError       TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IX_PendingSalesOrders_Unsynced
                        ON PendingSalesOrders(CompanyID, Synced);
CREATE TABLE IF NOT EXISTS PendingSOInvoices (
    PKId            INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyID       INTEGER NOT NULL,
    SONumber        TEXT    NOT NULL,
    CreatedUtc      TEXT    NOT NULL,
    Synced          INTEGER NOT NULL DEFAULT 0,
    LastAttemptUtc  TEXT,
    LastError       TEXT
);
CREATE INDEX IF NOT EXISTS IX_PendingSOInvoices_Unsynced
    ON PendingSOInvoices(CompanyID, Synced);

CREATE TABLE IF NOT EXISTS PendingStockUpdates (
    PKId            INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyID       INTEGER NOT NULL,
    ItemID          INTEGER NOT NULL,
    SaleQty         REAL    NOT NULL,
    CreatedUtc      TEXT    NOT NULL,
    Synced          INTEGER NOT NULL DEFAULT 0,
    LastAttemptUtc  TEXT,
    LastError       TEXT
);
CREATE INDEX IF NOT EXISTS IX_PendingStockUpdates_Unsynced
    ON PendingStockUpdates(CompanyID, Synced);
CREATE TABLE IF NOT EXISTS StoreStockCache (
    CompanyID     INTEGER NOT NULL,
    ItemID        TEXT    NOT NULL,
    OnHandQty     REAL    NOT NULL DEFAULT 0,
    ReservedQty   REAL    NOT NULL DEFAULT 0,
    LastSyncUtc   TEXT,
    PRIMARY KEY (CompanyID, ItemID)
);
CREATE INDEX IF NOT EXISTS IX_StoreStockCache_Item
    ON StoreStockCache(CompanyID, ItemID);
CREATE TABLE IF NOT EXISTS D365ItemCache (
    CompanyID     INTEGER NOT NULL,
    Barcode       TEXT    NOT NULL,
    ItemName      TEXT,
    SellingPrice  REAL,
    Category      TEXT,
    BaseUOM       INTEGER NOT NULL DEFAULT 1,
    BaseUOMName   TEXT,
    PackUomJson   TEXT,
    LastSyncUtc   TEXT,
    PRIMARY KEY (CompanyID, Barcode)
);

CREATE TABLE IF NOT EXISTS UomMasterCache (
    CompanyID     INTEGER NOT NULL,
    UomId         INTEGER NOT NULL,
    UomDescription TEXT,
    LastSyncUtc   TEXT,
    PRIMARY KEY (CompanyID, UomId)
);
CREATE TABLE IF NOT EXISTS TaxMasterCache (
    CompanyID       INTEGER NOT NULL,
    TaxID           INTEGER NOT NULL,
    TaxCode         TEXT,
    TaxPercentage   REAL,
    LastSyncUtc     TEXT,
    PRIMARY KEY (CompanyID, TaxID)
);
CREATE TABLE IF NOT EXISTS ItemIdMapCache (
    CompanyID     INTEGER NOT NULL,
    ItemName      TEXT    NOT NULL,
    ItemID        INTEGER NOT NULL,
    LastSyncUtc   TEXT,
    PRIMARY KEY (CompanyID, ItemName)
);
CREATE INDEX IF NOT EXISTS IX_ItemIdMapCache_Name
    ON ItemIdMapCache(CompanyID, ItemName);
CREATE TABLE IF NOT EXISTS PendingCustomerPayments (
    PKId            INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyID       INTEGER NOT NULL,
    SONumber        TEXT    NOT NULL,
    PayloadJson     TEXT    NOT NULL,
    PaymentId       INTEGER,           -- filled once Save succeeds, so a retry doesn't double-save
    ModifiedBy      INTEGER NOT NULL,
    BankAccountId   INTEGER,
    CreatedUtc      TEXT    NOT NULL,
    Synced          INTEGER NOT NULL DEFAULT 0,
    LastAttemptUtc  TEXT,
    LastError       TEXT
);
CREATE INDEX IF NOT EXISTS IX_PendingCustomerPayments_Unsynced
    ON PendingCustomerPayments(CompanyID, Synced);";
                // Migrate older DBs that already have D365ItemCache without the new UOM columns.
                void TryAddColumn(string table, string columnDef)
                {
                    try
                    {
                        using var alter = conn.CreateCommand();
                        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {columnDef};";
                        alter.ExecuteNonQuery();
                    }
                    catch { /* column already exists — ignore, same pattern used elsewhere in this app */ }
                }
                TryAddColumn("D365ItemCache", "BaseUOM INTEGER NOT NULL DEFAULT 1");
                TryAddColumn("D365ItemCache", "BaseUOMName TEXT");
                TryAddColumn("D365ItemCache", "PackUomJson TEXT");
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EnsureOfflineSyncSchema: " + ex.Message);
            }
        }

        private void StartStockSyncTimer()
        {
            if (_stockSyncTimer != null) return;

            _stockSyncTimer = new System.Windows.Forms.Timer { Interval = STOCK_SYNC_INTERVAL_MS };
            _stockSyncTimer.Tick += async (s, e) => await RefreshStockCacheAsync().ConfigureAwait(true);
            _stockSyncTimer.Start();
        }

        private async Task RefreshStockCacheAsync()
        {
            if (_stockSyncInProgress) return;
            if (!GetOnline()) return; // don't bother trying while offline — next tick will retry

            _stockSyncInProgress = true;
            try
            {
                await LoadStockCacheAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RefreshStockCacheAsync: " + ex.Message);
            }
            finally
            {
                _stockSyncInProgress = false;
            }
        }
        private async Task SaveCustomersToLocalCacheAsync(List<CustomerFullDto> customers)
        {
            if (customers == null || customers.Count == 0) return;
            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var tx = conn.BeginTransaction();

                    using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM CustomerCache WHERE CompanyID = @cid;";
                        del.Parameters.AddWithValue("@cid", _companyId);
                        del.ExecuteNonQuery();
                    }

                    string nowUtc = DateTime.UtcNow.ToString("o");
                    foreach (var c in customers)
                    {
                        using var ins = conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = @"
                    INSERT INTO CustomerCache
                        (CompanyID, CustomerID, CustomerCode, CustomerName, Address, City, Country, Mobile, Email, Status, LastSyncUtc)
                    VALUES (@cid, @id, @code, @name, @addr, @city, @country, @mobile, @email, @status, @sync)
                    ON CONFLICT(CompanyID, CustomerID) DO UPDATE SET
                        CustomerCode = excluded.CustomerCode,
                        CustomerName = excluded.CustomerName,
                        Address      = excluded.Address,
                        City         = excluded.City,
                        Country      = excluded.Country,
                        Mobile       = excluded.Mobile,
                        Email        = excluded.Email,
                        Status       = excluded.Status,
                        LastSyncUtc  = excluded.LastSyncUtc;";
                        ins.Parameters.AddWithValue("@cid", _companyId);
                        ins.Parameters.AddWithValue("@id", c.CustomerID);
                        ins.Parameters.AddWithValue("@code", c.CustomerCode ?? "");
                        ins.Parameters.AddWithValue("@name", c.CustomerName ?? "");
                        ins.Parameters.AddWithValue("@addr", c.Address ?? "");
                        ins.Parameters.AddWithValue("@city", c.City ?? "");
                        ins.Parameters.AddWithValue("@country", c.Country ?? "");
                        ins.Parameters.AddWithValue("@mobile", c.Mobile ?? "");
                        ins.Parameters.AddWithValue("@email", c.Email ?? "");
                        ins.Parameters.AddWithValue("@status", c.Status ? 1 : 0);
                        ins.Parameters.AddWithValue("@sync", nowUtc);
                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                    Debug.WriteLine($"SaveCustomersToLocalCacheAsync: cached {customers.Count} customers.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveCustomersToLocalCacheAsync: " + ex.Message);
            }
        }

        private async Task<List<CustomerFullDto>> LoadCustomersFromLocalCacheAsync()
        {
            var list = new List<CustomerFullDto>();
            try
            {
                EnsureOfflineSyncSchema();
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT CustomerID, CustomerCode, CustomerName, Address, City, Country, Mobile, Email, Status
                FROM CustomerCache
                WHERE CompanyID = @cid AND Status = 1
                ORDER BY CustomerName;";
                    cmd.Parameters.AddWithValue("@cid", _companyId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        list.Add(new CustomerFullDto
                        {
                            CustomerID = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0),
                            CustomerCode = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                            CustomerName = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                            Address = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                            City = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                            Country = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                            Mobile = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
                            Email = rdr.IsDBNull(7) ? "" : rdr.GetString(7),
                            Status = rdr.IsDBNull(8) ? true : rdr.GetInt32(8) == 1
                        });
                    }
                }).ConfigureAwait(false);

                Debug.WriteLine($"LoadCustomersFromLocalCacheAsync: {list.Count} customers loaded from cache.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadCustomersFromLocalCacheAsync: " + ex.Message);
            }
            return list;
        }
        private async Task LoadUomMasterAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                var resp = await http.GetAsync($"{ApiBaseUrl}/api/uom").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    await LoadUomMasterFromLocalAsync().ConfigureAwait(false);
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);
                JsonElement arr = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;

                _uomMaster = JsonSerializer.Deserialize<List<UomDto>>(arr.GetRawText(), opts) ?? new();
                Debug.WriteLine($"LoadUomMasterAsync: {_uomMaster.Count} UOMs loaded.");

                await SaveUomMasterToLocalAsync(_uomMaster).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadUomMasterAsync: " + ex.Message);
                await LoadUomMasterFromLocalAsync().ConfigureAwait(false);
            }
        }

        private async Task SaveUomMasterToLocalAsync(List<UomDto> uoms)
        {
            if (uoms == null || uoms.Count == 0) return;
            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var tx = conn.BeginTransaction();

                    using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM UomMasterCache WHERE CompanyID = @cid;";
                        del.Parameters.AddWithValue("@cid", _companyId);
                        del.ExecuteNonQuery();
                    }

                    string nowUtc = DateTime.UtcNow.ToString("o");
                    foreach (var u in uoms)
                    {
                        using var ins = conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = @"
                    INSERT INTO UomMasterCache (CompanyID, UomId, UomDescription, LastSyncUtc)
                    VALUES (@cid, @id, @desc, @sync)
                    ON CONFLICT(CompanyID, UomId) DO UPDATE SET
                        UomDescription = excluded.UomDescription,
                        LastSyncUtc    = excluded.LastSyncUtc;";
                        ins.Parameters.AddWithValue("@cid", _companyId);
                        ins.Parameters.AddWithValue("@id", u.UomId);
                        ins.Parameters.AddWithValue("@desc", u.UomDescription ?? "");
                        ins.Parameters.AddWithValue("@sync", nowUtc);
                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                    Debug.WriteLine($"SaveUomMasterToLocalAsync: persisted {uoms.Count} UOMs.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveUomMasterToLocalAsync: " + ex.Message);
            }
        }
        private async Task SaveItemIdMapToLocalAsync(Dictionary<string, int> nameToId)
        {
            if (nameToId == null || nameToId.Count == 0) return;
            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var tx = conn.BeginTransaction();

                    using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM ItemIdMapCache WHERE CompanyID = @cid;";
                        del.Parameters.AddWithValue("@cid", _companyId);
                        del.ExecuteNonQuery();
                    }

                    string nowUtc = DateTime.UtcNow.ToString("o");
                    foreach (var kvp in nameToId)
                    {
                        using var ins = conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = @"
                    INSERT INTO ItemIdMapCache (CompanyID, ItemName, ItemID, LastSyncUtc)
                    VALUES (@cid, @name, @id, @sync)
                    ON CONFLICT(CompanyID, ItemName) DO UPDATE SET
                        ItemID      = excluded.ItemID,
                        LastSyncUtc = excluded.LastSyncUtc;";
                        ins.Parameters.AddWithValue("@cid", _companyId);
                        ins.Parameters.AddWithValue("@name", kvp.Key);
                        ins.Parameters.AddWithValue("@id", kvp.Value);
                        ins.Parameters.AddWithValue("@sync", nowUtc);
                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                    Debug.WriteLine($"SaveItemIdMapToLocalAsync: persisted {nameToId.Count} item-id mappings.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveItemIdMapToLocalAsync: " + ex.Message);
            }
        }

        private async Task<Dictionary<string, int>> LoadItemIdMapFromLocalAsync()
        {
            var loaded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                EnsureOfflineSyncSchema();
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT ItemName, ItemID FROM ItemIdMapCache
                WHERE CompanyID = @cid;";
                    cmd.Parameters.AddWithValue("@cid", _companyId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        string name = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                        int id = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);
                        if (!string.IsNullOrWhiteSpace(name) && id > 0)
                            loaded[name] = id;
                    }
                }).ConfigureAwait(false);

                Debug.WriteLine($"LoadItemIdMapFromLocalAsync: {loaded.Count} item-id mappings loaded from local snapshot.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadItemIdMapFromLocalAsync: " + ex.Message);
            }
            return loaded;
        }
        private async Task LoadUomMasterFromLocalAsync()
        {
            try
            {
                EnsureOfflineSyncSchema();
                var loaded = new List<UomDto>();

                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT UomId, UomDescription FROM UomMasterCache
                WHERE CompanyID = @cid ORDER BY UomId;";
                    cmd.Parameters.AddWithValue("@cid", _companyId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        loaded.Add(new UomDto
                        {
                            UomId = rdr.GetInt32(0),
                            UomDescription = rdr.IsDBNull(1) ? "" : rdr.GetString(1)
                        });
                    }
                }).ConfigureAwait(false);

                if (loaded.Count > 0)
                {
                    _uomMaster = loaded;
                    Debug.WriteLine($"LoadUomMasterFromLocalAsync: {loaded.Count} UOMs loaded from local snapshot.");
                }
                else
                {
                    Debug.WriteLine("LoadUomMasterFromLocalAsync: no local UOM snapshot found.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadUomMasterFromLocalAsync: " + ex.Message);
            }
        }

        private async Task SaveTaxMasterToLocalAsync(List<TaxDto> taxes)
        {
            if (taxes == null || taxes.Count == 0) return;
            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var tx = conn.BeginTransaction();

                    using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM TaxMasterCache WHERE CompanyID = @cid;";
                        del.Parameters.AddWithValue("@cid", _companyId);
                        del.ExecuteNonQuery();
                    }

                    string nowUtc = DateTime.UtcNow.ToString("o");
                    foreach (var t in taxes)
                    {
                        using var ins = conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = @"
                    INSERT INTO TaxMasterCache (CompanyID, TaxID, TaxCode, TaxPercentage, LastSyncUtc)
                    VALUES (@cid, @id, @code, @pct, @sync)
                    ON CONFLICT(CompanyID, TaxID) DO UPDATE SET
                        TaxCode       = excluded.TaxCode,
                        TaxPercentage = excluded.TaxPercentage,
                        LastSyncUtc   = excluded.LastSyncUtc;";
                        ins.Parameters.AddWithValue("@cid", _companyId);
                        ins.Parameters.AddWithValue("@id", t.TaxId);
                        ins.Parameters.AddWithValue("@code", t.TaxCode ?? "");
                        ins.Parameters.AddWithValue("@pct", t.TaxPercentage);
                        ins.Parameters.AddWithValue("@sync", nowUtc);
                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                    Debug.WriteLine($"SaveTaxMasterToLocalAsync: persisted {taxes.Count} taxes.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveTaxMasterToLocalAsync: " + ex.Message);
            }
        }

        private async Task LoadTaxMasterFromLocalAsync()
        {
            try
            {
                EnsureOfflineSyncSchema();
                var loaded = new List<TaxDto>();

                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT TaxID, TaxCode, TaxPercentage FROM TaxMasterCache
                WHERE CompanyID = @cid ORDER BY TaxCode;";
                    cmd.Parameters.AddWithValue("@cid", _companyId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        loaded.Add(new TaxDto
                        {
                            TaxId = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0),
                            TaxCode = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                            TaxPercentage = rdr.IsDBNull(2) ? 0m : Convert.ToDecimal(rdr.GetDouble(2))
                        });
                    }
                }).ConfigureAwait(false);

                if (loaded.Count > 0)
                {
                    _taxMaster = loaded;
                    Debug.WriteLine($"LoadTaxMasterFromLocalAsync: {loaded.Count} taxes loaded from local snapshot.");
                }
                else
                {
                    Debug.WriteLine("LoadTaxMasterFromLocalAsync: no local tax snapshot found.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadTaxMasterFromLocalAsync: " + ex.Message);
            }
        }

        private async Task LoadTaxMasterAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                // RIGHT — matches [HttpGet("by-company")] GetByCompany(int companyId)
                var resp = await http.GetAsync($"{ApiBaseUrl}/api/tax/by-company?companyId={_companyId}").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    await LoadTaxMasterFromLocalAsync().ConfigureAwait(false);
                    this.BeginInvoke(new Action(BuildTaxDropdown));
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);
                JsonElement arr = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;

                _taxMaster = JsonSerializer.Deserialize<List<TaxDto>>(arr.GetRawText(), opts) ?? new();
                Debug.WriteLine($"LoadTaxMasterAsync: {_taxMaster.Count} taxes loaded.");

                await SaveTaxMasterToLocalAsync(_taxMaster).ConfigureAwait(false);

                this.BeginInvoke(new Action(BuildTaxDropdown));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadTaxMasterAsync: " + ex.Message);
                await LoadTaxMasterFromLocalAsync().ConfigureAwait(false);
                this.BeginInvoke(new Action(BuildTaxDropdown));
            }
        }
        private async Task SaveCatalogToLocalCacheAsync(List<Product> catalog)
        {
            if (catalog == null || catalog.Count == 0) return;

            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();

                    using var conn = new SqliteConnection($"Data Source={_dbPath}");
                    conn.Open();
                    using var tx = conn.BeginTransaction();

                    using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM D365ItemCache WHERE CompanyID = @cid;";
                        del.Parameters.AddWithValue("@cid", _companyId);
                        del.ExecuteNonQuery();
                    }

                    string nowUtc = DateTime.UtcNow.ToString("o");

                    foreach (var p in catalog)
                    {
                        if (string.IsNullOrWhiteSpace(p.Barcode)) continue;

                        // Serialize pack-size UOMs (everything beyond the base UOM) as JSON
                        string packJson = "";
                        try
                        {
                            var packOnly = (p.AvailableUOMs ?? new List<UomDto>())
                                .Where(u => u.UomId != p.UOM)
                                .ToList();
                            packJson = JsonSerializer.Serialize(packOnly);
                        }
                        catch { packJson = "[]"; }

                        using var ins = conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = @"
                    INSERT INTO D365ItemCache
                        (CompanyID, Barcode, ItemName, SellingPrice, Category, BaseUOM, BaseUOMName, PackUomJson, LastSyncUtc)
                    VALUES
                        (@cid, @bc, @name, @price, @cat, @uom, @uomName, @packJson, @sync)
                    ON CONFLICT(CompanyID, Barcode) DO UPDATE SET
                        ItemName     = excluded.ItemName,
                        SellingPrice = excluded.SellingPrice,
                        Category     = excluded.Category,
                        BaseUOM      = excluded.BaseUOM,
                        BaseUOMName  = excluded.BaseUOMName,
                        PackUomJson  = excluded.PackUomJson,
                        LastSyncUtc  = excluded.LastSyncUtc;";
                        ins.Parameters.AddWithValue("@cid", _companyId);
                        ins.Parameters.AddWithValue("@bc", p.Barcode);
                        ins.Parameters.AddWithValue("@name", p.Name ?? "");
                        ins.Parameters.AddWithValue("@price", p.Price);
                        ins.Parameters.AddWithValue("@cat", p.Category ?? "");
                        ins.Parameters.AddWithValue("@uom", p.UOM > 0 ? p.UOM : 1);
                        ins.Parameters.AddWithValue("@uomName",
                            p.AvailableUOMs?.FirstOrDefault(u => u.UomId == p.UOM)?.UomDescription ?? "");
                        ins.Parameters.AddWithValue("@packJson", packJson);
                        ins.Parameters.AddWithValue("@sync", nowUtc);
                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                    Debug.WriteLine($"SaveCatalogToLocalCacheAsync: cached {catalog.Count} products (with UOM).");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveCatalogToLocalCacheAsync: " + ex.Message);
            }
        }
        private async Task LoadProductsFromLocalCacheAsync()
        {
            try
            {
                EnsureOfflineSyncSchema();

                // Make sure we have a UOM master to display names, even offline.
                if (_uomMaster == null || _uomMaster.Count == 0)
                    await LoadUomMasterFromLocalAsync().ConfigureAwait(true);

                var localCatalog = new List<Product>();
                var localMap = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);

                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT Barcode, ItemName, SellingPrice, Category, BaseUOM, BaseUOMName, PackUomJson
                FROM D365ItemCache
                WHERE CompanyID = @cid
                ORDER BY ItemName;";
                    cmd.Parameters.AddWithValue("@cid", _companyId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        string barcode = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                        if (string.IsNullOrWhiteSpace(barcode)) continue;

                        decimal itemPrice = rdr.IsDBNull(2) ? 0m : Convert.ToDecimal(rdr.GetDouble(2));   // ← read early
                        int baseUom = rdr.IsDBNull(4) ? 1 : rdr.GetInt32(4);
                        string baseUomName = rdr.IsDBNull(5) ? "" : rdr.GetString(5);
                        string packJson = rdr.IsDBNull(6) ? "" : rdr.GetString(6);

                        var availableUOMs = new List<UomDto>
    {
        new UomDto { UomId = baseUom, UomDescription = baseUomName, UnitsPerPack = 1, RetailPrice = itemPrice }
    };

                        if (!string.IsNullOrWhiteSpace(packJson))
                        {
                            try
                            {
                                var packs = JsonSerializer.Deserialize<List<UomDto>>(packJson);
                                if (packs != null) availableUOMs.AddRange(packs);   // pack UOMs already carry RetailPrice/UnitsPerPack from cache
                            }
                            catch { /* ignore malformed pack JSON */ }
                        }

                        var prod = new Product
                        {
                            Barcode = barcode,
                            Name = rdr.IsDBNull(1) ? "Unknown" : rdr.GetString(1),
                            Price = itemPrice,
                            Category = rdr.IsDBNull(3) ? "General" : rdr.GetString(3),
                            UOM = baseUom,
                            AvailableUOMs = availableUOMs
                        };
                        // ...rest unchanged

                        localCatalog.Add(prod);
                        string padded = barcode.PadLeft(13, '0');
                        if (!localMap.ContainsKey(barcode)) localMap[barcode] = prod;
                        if (!localMap.ContainsKey(padded)) localMap[padded] = prod;
                    }
                }).ConfigureAwait(false);

                if (localCatalog.Count == 0)
                {
                    Debug.WriteLine("LoadProductsFromLocalCacheAsync: cache empty, falling back to legacy Item table.");
                    await LoadProductsFromSQLite().ConfigureAwait(true);
                    return;
                }

                _catalog = localCatalog;
                _barcodeMap = localMap;

                await LoadItemIdMapAsync().ConfigureAwait(true);
                foreach (var p in _catalog)
                    p.ItemId = ResolveItemId(p.Name, p.Barcode);

                this.BeginInvoke(new Action(() =>
                {
                    BuildAutocomplete();
                    BuildHotItems();
                    ShowStatus($"✓ {localCatalog.Count} products loaded (local cache).", true);
                    SetD365Mode(true);
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadProductsFromLocalCacheAsync: " + ex.Message);
                ShowStatus("Local product cache load failed: " + ex.Message, false);
            }
        }
        private void StartProductSyncTimer()
        {
            if (_productSyncTimer != null) return; // already running

            _productSyncTimer = new System.Windows.Forms.Timer { Interval = PRODUCT_SYNC_INTERVAL_MS };
            _productSyncTimer.Tick += async (s, e) => await RefreshProductsFromApiAsync().ConfigureAwait(true);
            _productSyncTimer.Start();
        }
        private async Task RefreshProductsFromApiAsync()
        {
            if (_productSyncInProgress) return;
            _productSyncInProgress = true;
            try
            {
                await LoadProductsFromD365Async().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RefreshProductsFromApiAsync: " + ex.Message);
            }
            finally
            {
                _productSyncInProgress = false;
            }
        }
        private async Task SyncOfflineStockUpdatesAsync()
        {
            if (!GetOnline()) return;

            var pending = new List<(long PkId, int ItemId, decimal Qty)>();
            await Task.Run(() =>
            {
                EnsureOfflineSyncSchema();
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
            SELECT PKId, ItemID, SaleQty
            FROM PendingStockUpdates
            WHERE CompanyID = @cid AND Synced = 0
            ORDER BY PKId;";
                cmd.Parameters.AddWithValue("@cid", _companyId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    pending.Add((rdr.GetInt64(0), rdr.GetInt32(1), Convert.ToDecimal(rdr.GetDouble(2))));
            }).ConfigureAwait(false);

            if (pending.Count == 0) return;

            int synced = 0, failed = 0;
            foreach (var row in pending)
            {
                try
                {// in SyncOfflineStockUpdatesAsync — use the same refKey when re-queuing/re-sending
                    string refKey = $"{row.PkId}-{row.ItemId}"; // or better: store InvoiceNo alongside the queue row
                    bool ok = await ProcessSaleStockAsync(row.ItemId, _companyId, row.Qty, refKey).ConfigureAwait(true);
                    if (ok)
                    {
                        await MarkOfflineStockSyncedAsync(row.PkId).ConfigureAwait(true);
                        synced++;
                    }
                    else
                    {
                        await MarkOfflineStockAttemptFailedAsync(row.PkId, "API returned failure").ConfigureAwait(true);
                        failed++;
                        if (!GetOnline()) break;
                    }
                }
                catch (Exception ex)
                {
                    await MarkOfflineStockAttemptFailedAsync(row.PkId, ex.Message).ConfigureAwait(true);
                    failed++;
                    if (!GetOnline()) break;
                }
            }

            if (synced > 0 || failed > 0)
                Debug.WriteLine($"SyncOfflineStockUpdatesAsync: {synced} synced, {failed} still pending.");
        }

        private async Task SyncOfflineSOInvoicesAsync()
        {
            if (!GetOnline()) return;

            var pending = new List<(long PkId, string SONumber)>();
            await Task.Run(() =>
            {
                EnsureOfflineSyncSchema();
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
            SELECT PKId, SONumber
            FROM PendingSOInvoices
            WHERE CompanyID = @cid AND Synced = 0
            ORDER BY PKId;";
                cmd.Parameters.AddWithValue("@cid", _companyId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    pending.Add((rdr.GetInt64(0), rdr.GetString(1)));
            }).ConfigureAwait(false);

            if (pending.Count == 0) return;

            int synced = 0, failed = 0;
            foreach (var row in pending)
            {
                try
                {
                    var so = await SalesOrderApi.GetSalesOrderBySoNumberAsync(row.SONumber).ConfigureAwait(true);
                    if (so == null)
                    {
                        // SO itself may not have synced yet (e.g. still in PendingSalesOrders) — try again next tick.
                        await MarkOfflineSOInvoiceAttemptFailedAsync(row.PkId, "SO not found yet").ConfigureAwait(true);
                        failed++;
                        continue;
                    }

                    bool ok = (await CreateAndConfirmSOInvoiceAsync(so).ConfigureAwait(true)).Success;
                    if (ok)
                    {
                        await MarkOfflineSOInvoiceSyncedAsync(row.PkId).ConfigureAwait(true);
                        synced++;
                    }
                    else
                    {
                        await MarkOfflineSOInvoiceAttemptFailedAsync(row.PkId, "Invoice creation/confirm failed").ConfigureAwait(true);
                        failed++;
                        if (!GetOnline()) break;
                    }
                }
                catch (Exception ex)
                {
                    await MarkOfflineSOInvoiceAttemptFailedAsync(row.PkId, ex.Message).ConfigureAwait(true);
                    failed++;
                    if (!GetOnline()) break;
                }
            }

            if (synced > 0)
                ShowStatus($"✓ Auto-synced {synced} pending SO Invoice(s).", true);
            if (synced > 0 || failed > 0)
                Debug.WriteLine($"SyncOfflineSOInvoicesAsync: {synced} synced, {failed} still pending.");
        }

        private async Task SyncOfflineCustomerPaymentsAsync()
        {
            if (!GetOnline()) return;

            var pending = new List<(long PkId, string SONumber, string Json, int? PaymentId, int ModifiedBy, int? BankAccountId)>();

            await Task.Run(() =>
            {
                EnsureOfflineSyncSchema();
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
            SELECT PKId, SONumber, PayloadJson, PaymentId, ModifiedBy, BankAccountId
            FROM PendingCustomerPayments
            WHERE CompanyID = @cid AND Synced = 0
            ORDER BY PKId;";
                cmd.Parameters.AddWithValue("@cid", _companyId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    pending.Add((
                        rdr.GetInt64(0),
                        rdr.GetString(1),
                        rdr.GetString(2),
                        rdr.IsDBNull(3) ? (int?)null : rdr.GetInt32(3),
                        rdr.GetInt32(4),
                        rdr.IsDBNull(5) ? (int?)null : rdr.GetInt32(5)
                    ));
                }
            }).ConfigureAwait(false);

            if (pending.Count == 0) return;

            int synced = 0, failed = 0;

            foreach (var row in pending)
            {
                try
                {
                    int? paymentId = row.PaymentId;

                    // ── Phase 1: Save (only if not already saved by a previous partial attempt) ──
                    if (!paymentId.HasValue || paymentId.Value <= 0)
                    {
                        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var payload = JsonSerializer.Deserialize<SalesOrderApi.SaveCustomerPaymentPayload>(row.Json, opts);
                        if (payload == null)
                        {
                            await MarkOfflineCustomerPaymentAttemptFailedAsync(row.PkId, "Payload deserialize failed").ConfigureAwait(true);
                            failed++;
                            continue;
                        }

                        var saveResult = await SalesOrderApi.SaveCustomerPaymentAsync(payload).ConfigureAwait(true);
                        if (!saveResult.Success || !saveResult.PaymentId.HasValue)
                        {
                            await MarkOfflineCustomerPaymentAttemptFailedAsync(row.PkId, "Save failed").ConfigureAwait(true);
                            failed++;
                            if (!GetOnline()) break;
                            continue;
                        }

                        paymentId = saveResult.PaymentId.Value;
                        await MarkOfflineCustomerPaymentPaymentIdAsync(row.PkId, paymentId.Value).ConfigureAwait(true);
                    }

                    // ── Phase 2: Post ──
                    bool posted = await SalesOrderApi.PostCustomerPaymentAsync(
                        paymentId.Value, row.ModifiedBy, row.BankAccountId).ConfigureAwait(true);

                    if (posted)
                    {
                        await MarkOfflineCustomerPaymentSyncedAsync(row.PkId).ConfigureAwait(true);
                        synced++;
                    }
                    else
                    {
                        await MarkOfflineCustomerPaymentAttemptFailedAsync(row.PkId, "Post failed").ConfigureAwait(true);
                        failed++;
                        if (!GetOnline()) break;
                    }
                }
                catch (Exception ex)
                {
                    await MarkOfflineCustomerPaymentAttemptFailedAsync(row.PkId, ex.Message).ConfigureAwait(true);
                    failed++;
                    if (!GetOnline()) break;
                }
            }

            if (synced > 0)
                ShowStatus($"✓ Auto-synced {synced} pending cash payment(s).", true);
            if (synced > 0 || failed > 0)
                Debug.WriteLine($"SyncOfflineCustomerPaymentsAsync: {synced} synced, {failed} still pending.");
        }

        private async Task<bool> QueueOfflineSalesOrderAsync(string invoiceNo, CreateSalesOrderPayload payload)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload);

                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO PendingSalesOrders (CompanyID, InvoiceNo, PayloadJson, CreatedUtc, Synced)
                        VALUES (@cid, @inv, @json, @created, 0);";
                    cmd.Parameters.AddWithValue("@cid", _companyId);
                    cmd.Parameters.AddWithValue("@inv", invoiceNo);
                    cmd.Parameters.AddWithValue("@json", json);
                    cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);

                Debug.WriteLine($"QueueOfflineSalesOrderAsync: queued {invoiceNo} for later sync.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("QueueOfflineSalesOrderAsync: " + ex.Message);
                return false;
            }
        }
        private async Task<bool> QueueOfflineStockUpdateAsync(int itemId, int companyId, decimal saleQty)
        {
            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                INSERT INTO PendingStockUpdates (CompanyID, ItemID, SaleQty, CreatedUtc, Synced)
                VALUES (@cid, @item, @qty, @created, 0);";
                    cmd.Parameters.AddWithValue("@cid", companyId);
                    cmd.Parameters.AddWithValue("@item", itemId);
                    cmd.Parameters.AddWithValue("@qty", (double)saleQty);
                    cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);

                Debug.WriteLine($"QueueOfflineStockUpdateAsync: queued item {itemId} qty {saleQty} for later sync.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("QueueOfflineStockUpdateAsync: " + ex.Message);
                return false;
            }
        }

        private async Task<bool> QueueOfflineSOInvoiceAsync(int companyId, string soNumber)
        {
            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                INSERT INTO PendingSOInvoices (CompanyID, SONumber, CreatedUtc, Synced)
                VALUES (@cid, @so, @created, 0);";
                    cmd.Parameters.AddWithValue("@cid", companyId);
                    cmd.Parameters.AddWithValue("@so", soNumber);
                    cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);

                Debug.WriteLine($"QueueOfflineSOInvoiceAsync: queued SO {soNumber} for later invoicing.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("QueueOfflineSOInvoiceAsync: " + ex.Message);
                return false;
            }
        }

        private async Task MarkOfflineStockSyncedAsync(long pkId)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingStockUpdates SET Synced = 1, LastAttemptUtc = @t WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineStockSyncedAsync: " + ex.Message); }
        }

        private async Task MarkOfflineStockAttemptFailedAsync(long pkId, string error)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingStockUpdates SET LastAttemptUtc = @t, LastError = @err WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@err", error ?? "");
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineStockAttemptFailedAsync: " + ex.Message); }
        }

        private async Task<bool> QueueOfflineCustomerPaymentAsync(
    int companyId, string soNumber, SalesOrderApi.SaveCustomerPaymentPayload payload,
    int modifiedBy, int? bankAccountId)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload);

                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                INSERT INTO PendingCustomerPayments
                    (CompanyID, SONumber, PayloadJson, PaymentId, ModifiedBy, BankAccountId, CreatedUtc, Synced)
                VALUES
                    (@cid, @so, @json, NULL, @modBy, @bank, @created, 0);";
                    cmd.Parameters.AddWithValue("@cid", companyId);
                    cmd.Parameters.AddWithValue("@so", soNumber ?? "");
                    cmd.Parameters.AddWithValue("@json", json);
                    cmd.Parameters.AddWithValue("@modBy", modifiedBy);
                    cmd.Parameters.AddWithValue("@bank", (object)bankAccountId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);

                Debug.WriteLine($"QueueOfflineCustomerPaymentAsync: queued cash payment for SO {soNumber}.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("QueueOfflineCustomerPaymentAsync: " + ex.Message);
                return false;
            }
        }

        private async Task MarkOfflineCustomerPaymentPaymentIdAsync(long pkId, int paymentId)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingCustomerPayments SET PaymentId = @pid WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@pid", paymentId);
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineCustomerPaymentPaymentIdAsync: " + ex.Message); }
        }

        private async Task MarkOfflineCustomerPaymentSyncedAsync(long pkId)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingCustomerPayments SET Synced = 1, LastAttemptUtc = @t WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineCustomerPaymentSyncedAsync: " + ex.Message); }
        }

        private async Task MarkOfflineCustomerPaymentAttemptFailedAsync(long pkId, string error)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingCustomerPayments SET LastAttemptUtc = @t, LastError = @err WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@err", error ?? "");
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineCustomerPaymentAttemptFailedAsync: " + ex.Message); }
        }

        private async Task MarkOfflineSOInvoiceSyncedAsync(long pkId)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingSOInvoices SET Synced = 1, LastAttemptUtc = @t WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineSOInvoiceSyncedAsync: " + ex.Message); }
        }

        private async Task MarkOfflineSOInvoiceAttemptFailedAsync(long pkId, string error)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingSOInvoices SET LastAttemptUtc = @t, LastError = @err WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@err", error ?? "");
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineSOInvoiceAttemptFailedAsync: " + ex.Message); }
        }
        //private void StartOfflineOrderSyncTimer()
        //{
        //    if (_offlineOrderSyncTimer != null) return; // already running

        //    _offlineOrderSyncTimer = new System.Windows.Forms.Timer { Interval = OFFLINE_ORDER_SYNC_INTERVAL_MS };
        //    _offlineOrderSyncTimer.Tick += async (s, e) => await SyncAllOfflineDataAsync().ConfigureAwait(true);
        //    _offlineOrderSyncTimer.Start();

        //    // Also try once shortly after startup, in case items were queued
        //    // in a previous offline session.
        //    _ = SyncAllOfflineDataAsync();
        //}

        // ── Runs every offline queue in dependency order: Sales Orders first
        //    (so newly-created SOs exist before we try to invoice them), then
        //    SO Invoices, then any stock updates that still need pushing. ──
        private async Task SyncAllOfflineDataAsync()
        {
            if (_offlineSyncInProgress) return;
            if (!GetOnline()) return;

            _offlineSyncInProgress = true;
            try
            {
                await SyncOfflineSalesOrdersInnerAsync().ConfigureAwait(true);
                await SyncOfflineCustomerPaymentsAsync().ConfigureAwait(true);
                await SyncOfflineSOInvoicesAsync().ConfigureAwait(true);
                await SyncOfflineStockUpdatesAsync().ConfigureAwait(true);
            }
            finally
            {
                _offlineSyncInProgress = false;
            }
        }
        private async Task SyncOfflineSalesOrdersInnerAsync()
        {
            if (!GetOnline()) return; // don't even try while offline

            var pending = new List<(long PkId, string InvoiceNo, string Json)>();

            await Task.Run(() =>
            {
                EnsureOfflineSyncSchema();
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
            SELECT PKId, InvoiceNo, PayloadJson
            FROM PendingSalesOrders
            WHERE CompanyID = @cid AND Synced = 0
            ORDER BY PKId;";
                cmd.Parameters.AddWithValue("@cid", _companyId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    pending.Add((rdr.GetInt64(0), rdr.GetString(1), rdr.GetString(2)));
            }).ConfigureAwait(false);

            if (pending.Count == 0) return;

            ShowStatus($"🔄 Syncing {pending.Count} offline sale(s)…", true);

            int synced = 0, failed = 0;

            foreach (var row in pending)
            {
                try
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var payload = JsonSerializer.Deserialize<CreateSalesOrderPayload>(row.Json, opts);
                    if (payload == null) { failed++; continue; }

                    var result = await SalesOrderApi.CreateSalesOrderAsync(payload).ConfigureAwait(true);

                    // ============================================================
                    // 2. SYNC OFFLINE SALES ORDERS
                    //    DO NOT reduce stock here if the online sale already
                    //    reduced it. Only sync the Sales Order.
                    // ============================================================

                    if (result.Success)
                    {
                        // Stock is reduced server-side by SalesOrderBL.CreateAsync when the SO
                        // is created (called via SalesOrderApi.CreateSalesOrderAsync above).
                        // Do NOT call ProcessSaleStockAsync here — that was double-reducing.

                        if (!string.IsNullOrWhiteSpace(result.SoNumber) &&
                            string.Equals(payload.Status, "Confirm", StringComparison.OrdinalIgnoreCase))
                        {
                            await QueueOfflineSOInvoiceAsync(_companyId, result.SoNumber).ConfigureAwait(true);
                        }

                        await MarkOfflineOrderSyncedAsync(row.PkId).ConfigureAwait(true);
                        synced++;
                    }
                    else
                    {
                        await MarkOfflineOrderAttemptFailedAsync(
                            row.PkId,
                            "API returned failure"
                        ).ConfigureAwait(true);

                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SyncOfflineSalesOrdersInnerAsync: {row.InvoiceNo} failed — {ex.Message}");
                    await MarkOfflineOrderAttemptFailedAsync(row.PkId, ex.Message).ConfigureAwait(true);
                    failed++;
                    // If this failure is because we just went offline again, stop the batch.
                    if (!GetOnline()) break;
                }
            }

            if (synced > 0 || failed > 0)
                ShowStatus($"✓ Offline sync: {synced} sale(s) pushed" + (failed > 0 ? $", {failed} still pending." : "."), synced > 0);
        }
        private async Task MarkOfflineOrderSyncedAsync(long pkId)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingSalesOrders SET Synced = 1, LastAttemptUtc = @t WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineOrderSyncedAsync: " + ex.Message); }
        }

        private async Task MarkOfflineOrderAttemptFailedAsync(long pkId, string error)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE PendingSalesOrders SET LastAttemptUtc = @t, LastError = @err WHERE PKId = @id;";
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@err", error ?? "");
                    cmd.Parameters.AddWithValue("@id", pkId);
                    cmd.ExecuteNonQuery();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("MarkOfflineOrderAttemptFailedAsync: " + ex.Message); }
        }
        private async Task LoadCustomersAsync()
        {
            List<CustomerFullDto> list = null;

            try
            {
                if (GetOnline())
                {
                    using var http = new System.Net.Http.HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(15);
                    var resp = await http.GetAsync($"{ApiBaseUrl}/api/customers").ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var result = JsonSerializer.Deserialize<CustomerListDto>(json, opts);

                        list = (result?.Data ?? new List<CustomerFullDto>())
                            .Where(c => c.Status)
                            .OrderBy(c => c.CustomerName)
                            .ToList();

                        if (list.Count > 0)
                            await SaveCustomersToLocalCacheAsync(list).ConfigureAwait(true);
                    }
                    else
                    {
                        ShowStatus($"Customer API error {(int)resp.StatusCode} — using local cache.", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadCustomersAsync (API): " + ex.Message);
            }

            if (list == null || list.Count == 0)
            {
                list = await LoadCustomersFromLocalCacheAsync().ConfigureAwait(true);
                if (list.Count == 0)
                {
                    ShowStatus("No customers available (offline, no cache).", false);
                    return;
                }
                ShowStatus($"📴 Offline — loaded {list.Count} customers from cache.", true);
            }

            this.BeginInvoke(new Action(() =>
            {
                _customersList = list;
                _cmbCustomer.Items.Clear();
                foreach (var c in list) _cmbCustomer.Items.Add(c);

                var defaultCustomer = list.FirstOrDefault(c =>
                        c.CustomerCode.Equals(DEFAULT_CUSTOMER_CODE, StringComparison.OrdinalIgnoreCase))
                    ?? list.FirstOrDefault(c =>
                        c.CustomerName.Equals("Walk-in", StringComparison.OrdinalIgnoreCase))
                    ?? list.FirstOrDefault();

                if (defaultCustomer != null)
                {
                    _cmbCustomer.SelectedItem = defaultCustomer;
                    _selectedCustomer = defaultCustomer;
                    _customerNameValue = defaultCustomer.CustomerName;
                    _customerAddressValue = defaultCustomer.Address;
                }

                if (list == _customersList)
                    ShowStatus($"✓ Loaded {list.Count} customers.", true);
            }));
        }

        private void CmbCustomer_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmbCustomer.SelectedItem is not CustomerFullDto c) return;

            _selectedCustomer = c;
            _customerNameValue = c.CustomerName;
            _customerAddressValue = c.Address;

            ShowStatus($"👤 Customer: {c.CustomerName}", true);
        }
        private void ShowReprintHistory()
        {
            var frm = new ReprintHistoryForm(
                _companyId,
                _currencySymbol,
                _companyName,
                _companyAddress,
                _companyPhone,
                _companyVat,
                _companyWebsite,
                _salesOfficeInfo,
                this);
            frm.ShowDialog(this);
        }











        private void SyncD365ToSQLite(Dictionary<string, List<D365ProductDetail>> details)
        {
            //if (!System.IO.File.Exists(_dbPath)) return;
            //try
            //{
            //    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
            //    conn.Open();

            //    // ── DDL ───────────────────────────────────────────────────────────────
            //    using (var ddl = conn.CreateCommand())
            //    {
            //        ddl.CommandText = @"
            //    CREATE TABLE IF NOT EXISTS D365Products (
            //        ItemId       TEXT PRIMARY KEY,
            //        NameAlias    TEXT,
            //        InventSiteId TEXT
            //    );
            //    CREATE TABLE IF NOT EXISTS D365ProductDetails (
            //        RowId                  INTEGER PRIMARY KEY AUTOINCREMENT,
            //        DataAreaId             TEXT,
            //        ItemId                 TEXT,
            //        NameAlias              TEXT,
            //        OnHandModifiedDateTime TEXT,
            //        AvailPhysical          REAL,
            //        InventLocationId       TEXT,
            //        Amount                 REAL,
            //        InventSiteId           TEXT,
            //        WMSLocationId          TEXT,
            //        AccountRelation        TEXT,
            //        ODataEtag              TEXT,
            //        UNIQUE(ItemId, InventLocationId, AccountRelation)
            //    );
            //    CREATE TABLE IF NOT EXISTS StoreStock (
            //        PKStoreStockID INTEGER PRIMARY KEY AUTOINCREMENT,
            //        ItemID         TEXT    NOT NULL,
            //        StoreID        INTEGER NOT NULL DEFAULT 1,
            //        OnHandQty      REAL    NOT NULL DEFAULT 0,
            //        ReservedQty    REAL             DEFAULT 0,
            //        LastSyncQty    REAL             DEFAULT 0,
            //        UNIQUE(ItemID, StoreID)
            //    );
            //    CREATE INDEX IF NOT EXISTS IX_StoreStock_ItemStore
            //        ON StoreStock(ItemID, StoreID);
            //    CREATE TABLE IF NOT EXISTS POS_SyncControl (
            //        SyncType         TEXT PRIMARY KEY,
            //        LastSyncDateTime TEXT
            //    );";
            //        ddl.ExecuteNonQuery();
            //    }

            //    // ── Read existing etags to skip unchanged rows ────────────────────────
            //    var existingEtags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            //    using (var cmd = conn.CreateCommand())
            //    {
            //        cmd.CommandText =
            //            "SELECT ItemId || '|' || IFNULL(InventLocationId,'') || '|' || IFNULL(AccountRelation,''), " +
            //            "       ODataEtag " +
            //            "FROM D365ProductDetails;";
            //        using var r = cmd.ExecuteReader();
            //        while (r.Read())
            //            existingEtags[r.IsDBNull(0) ? "" : r.GetString(0)] =
            //                            r.IsDBNull(1) ? "" : r.GetString(1);
            //    }

            //    using var tx = conn.BeginTransaction();
            //    int upsertedDetails = 0, upsertedStock = 0;

            //    foreach (var kvp in details)
            //    {
            //        string itemId = kvp.Key;
            //        var rows = kvp.Value;
            //        if (rows.Count == 0) continue;

            //        // ── Master product ─────────────────────────────────────────────────
            //        using (var cmd = conn.CreateCommand())
            //        {
            //            cmd.Transaction = tx;
            //            cmd.CommandText = @"
            //        INSERT INTO D365Products (ItemId, NameAlias, InventSiteId)
            //        VALUES (@id, @name, @site)
            //        ON CONFLICT(ItemId) DO UPDATE SET
            //            NameAlias    = excluded.NameAlias,
            //            InventSiteId = excluded.InventSiteId;";
            //            cmd.Parameters.AddWithValue("@id", itemId);
            //            cmd.Parameters.AddWithValue("@name", rows[0].NameAlias);
            //            cmd.Parameters.AddWithValue("@site", rows[0].InventSiteId);
            //            cmd.ExecuteNonQuery();
            //        }

            //        // ── Detail rows ────────────────────────────────────────────────────
            //        foreach (var d in rows)
            //        {
            //            string key = $"{d.ItemId}|{d.InventLocationId}|{d.AccountRelation}";
            //            if (!string.IsNullOrWhiteSpace(d.ODataEtag) &&
            //                existingEtags.TryGetValue(key, out string ex) &&
            //                ex == d.ODataEtag)
            //                continue;

            //            using var cmd = conn.CreateCommand();
            //            cmd.Transaction = tx;
            //            cmd.CommandText = @"
            //        INSERT INTO D365ProductDetails
            //            (DataAreaId, ItemId, NameAlias, OnHandModifiedDateTime,
            //             AvailPhysical, InventLocationId, Amount,
            //             InventSiteId, WMSLocationId, AccountRelation, ODataEtag)
            //        VALUES
            //            (@da,@id,@name,@ohd,@avail,@loc,@amt,@site,@wms,@acct,@etag)
            //        ON CONFLICT(ItemId, InventLocationId, AccountRelation) DO UPDATE SET
            //            NameAlias              = excluded.NameAlias,
            //            OnHandModifiedDateTime = excluded.OnHandModifiedDateTime,
            //            AvailPhysical          = excluded.AvailPhysical,
            //            Amount                 = excluded.Amount,
            //            InventSiteId           = excluded.InventSiteId,
            //            WMSLocationId          = excluded.WMSLocationId,
            //            ODataEtag              = excluded.ODataEtag;";
            //            cmd.Parameters.AddWithValue("@da", d.DataAreaId);
            //            cmd.Parameters.AddWithValue("@id", d.ItemId);
            //            cmd.Parameters.AddWithValue("@name", d.NameAlias);
            //            cmd.Parameters.AddWithValue("@ohd", d.OnHandModifiedDateTime);
            //            cmd.Parameters.AddWithValue("@avail", d.AvailPhysical);
            //            cmd.Parameters.AddWithValue("@loc", d.InventLocationId);
            //            cmd.Parameters.AddWithValue("@amt", d.Amount);
            //            cmd.Parameters.AddWithValue("@site", d.InventSiteId);
            //            cmd.Parameters.AddWithValue("@wms", d.WMSLocationId);
            //            cmd.Parameters.AddWithValue("@acct", d.AccountRelation);
            //            cmd.Parameters.AddWithValue("@etag", d.ODataEtag);
            //            cmd.ExecuteNonQuery();
            //            upsertedDetails++;
            //        }

            //        // ── StoreStock — sum AvailPhysical across all locations ───────────
            //        //   OnHandQty  = total available from D365 (refreshed on every sync)
            //        //   LastSyncQty = same snapshot — baseline for drift comparison
            //        //   ReservedQty is intentionally NOT reset; it accumulates from sales
            //        decimal totalAvail = rows.Sum(d => d.AvailPhysical);

            //        using (var cmd = conn.CreateCommand())
            //        {
            //            cmd.Transaction = tx;
            //            cmd.CommandText = @"
            //        INSERT INTO StoreStock (ItemID, StoreID, OnHandQty, LastSyncQty)
            //        VALUES (@item, @store, @qty, @qty)
            //        ON CONFLICT(ItemID, StoreID) DO UPDATE SET
            //            OnHandQty   = excluded.OnHandQty,
            //            LastSyncQty = excluded.LastSyncQty;";
            //            //  NOTE: ReservedQty is excluded from the UPDATE intentionally —
            //            //  we never want a D365 sync to wipe out the local sales tally.
            //            cmd.Parameters.AddWithValue("@item", itemId);
            //            cmd.Parameters.AddWithValue("@store", _storeId);
            //            cmd.Parameters.AddWithValue("@qty", (double)totalAvail);
            //            cmd.ExecuteNonQuery();
            //            upsertedStock++;
            //        }
            //    }

            //    UpsertSyncControl(conn, tx as SQLiteTransaction, "SalesForm");
            //    tx.Commit();
            //    Debug.WriteLine(
            //        $"SyncD365ToSQLite: {upsertedDetails} detail rows, {upsertedStock} stock rows upserted.");
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine("SyncD365ToSQLite: " + ex.Message);
            //}
        }
        // ══════════════════════════════════════════════════════════════════════
        //  STOCK — LIVE SINGLE-ITEM CHECK  (GET /api/stock/item?itemId=X&companyId=Y)
        // ══════════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════════
        //  STOCK — LOAD FULL CACHE FROM API  (GET /api/stock?companyId=X)
        // ══════════════════════════════════════════════════════════════════════
        private async Task LoadStockCacheAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);

                var resp = await http.GetAsync($"{ApiBaseUrl}/api/stock?companyId={_companyId}").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"LoadStockCacheAsync: API returned {(int)resp.StatusCode}");
                    await LoadStockCacheFromLocalAsync().ConfigureAwait(false); // fall back to last-known snapshot
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);

                JsonElement arr = root;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl))
                    arr = dataEl;

                if (arr.ValueKind != JsonValueKind.Array)
                {
                    await LoadStockCacheFromLocalAsync().ConfigureAwait(false);
                    return;
                }

                var newCache = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in arr.EnumerateArray())
                {
                    string itemId = GetStrProp(row, "itemID", "ItemID", "itemId", "ItemId");
                    if (string.IsNullOrWhiteSpace(itemId)) continue;

                    decimal onHand = GetDecProp(row, "onHandQty", "OnHandQty", "onHand", "OnHand");
                    decimal reserved = GetDecProp(row, "reservedQty", "ReservedQty", "reserved", "Reserved");

                    newCache[itemId] = (onHand, reserved);
                }

                _stockCache = newCache;
                _stockCacheLoaded = true;
                Debug.WriteLine($"LoadStockCacheAsync: {newCache.Count} stock rows cached (in-memory).");

                await SaveStockCacheToLocalAsync(newCache).ConfigureAwait(false);

                // NEW — keep quick-add in sync with live stock
                if (this.IsHandleCreated && !this.IsDisposed)
                    this.BeginInvoke(new Action(BuildHotItems));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadStockCacheAsync: " + ex.Message);
                // API unreachable — try to at least populate from the last known snapshot.
                await LoadStockCacheFromLocalAsync().ConfigureAwait(false);
            }
        }

        // ── Writes the freshly-fetched cache into StoreStockCache (full refresh for this company). ──
        private async Task SaveStockCacheToLocalAsync(Dictionary<string, (decimal onHand, decimal reserved)> cache)
        {
            if (cache == null || cache.Count == 0) return;
            try
            {
                await Task.Run(() =>
                {
                    EnsureOfflineSyncSchema();
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var tx = conn.BeginTransaction();

                    using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM StoreStockCache WHERE CompanyID = @cid;";
                        del.Parameters.AddWithValue("@cid", _companyId);
                        del.ExecuteNonQuery();
                    }

                    string nowUtc = DateTime.UtcNow.ToString("o");
                    foreach (var kvp in cache)
                    {
                        using var ins = conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = @"
                    INSERT INTO StoreStockCache (CompanyID, ItemID, OnHandQty, ReservedQty, LastSyncUtc)
                    VALUES (@cid, @item, @onhand, @res, @sync)
                    ON CONFLICT(CompanyID, ItemID) DO UPDATE SET
                        OnHandQty   = excluded.OnHandQty,
                        ReservedQty = excluded.ReservedQty,
                        LastSyncUtc = excluded.LastSyncUtc;";
                        ins.Parameters.AddWithValue("@cid", _companyId);
                        ins.Parameters.AddWithValue("@item", kvp.Key);
                        ins.Parameters.AddWithValue("@onhand", (double)kvp.Value.Item1);
                        ins.Parameters.AddWithValue("@res", (double)kvp.Value.Item2);
                        ins.Parameters.AddWithValue("@sync", nowUtc);
                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                    Debug.WriteLine($"SaveStockCacheToLocalAsync: persisted {cache.Count} stock rows.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveStockCacheToLocalAsync: " + ex.Message);
            }
        }

        // ── Loads the last-known snapshot from SQLite into _stockCache — used at
        //    startup-while-offline, or whenever a live API refresh fails. ──
        private async Task LoadStockCacheFromLocalAsync()
        {
            try
            {
                EnsureOfflineSyncSchema();
                var loaded = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);

                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT ItemID, OnHandQty, ReservedQty
                FROM StoreStockCache
                WHERE CompanyID = @cid;";
                    cmd.Parameters.AddWithValue("@cid", _companyId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        string itemId = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                        if (string.IsNullOrWhiteSpace(itemId)) continue;
                        decimal onHand = rdr.IsDBNull(1) ? 0m : Convert.ToDecimal(rdr.GetDouble(1));
                        decimal reserved = rdr.IsDBNull(2) ? 0m : Convert.ToDecimal(rdr.GetDouble(2));
                        loaded[itemId] = (onHand, reserved);
                    }
                }).ConfigureAwait(false);

                if (loaded.Count > 0)
                {
                    // Only overwrite in-memory cache if we don't already have a fresher one.
                    if (_stockCache == null || _stockCache.Count == 0)
                    {
                        _stockCache = loaded;
                        _stockCacheLoaded = true;
                    }
                    Debug.WriteLine($"LoadStockCacheFromLocalAsync: {loaded.Count} stock rows loaded from local snapshot.");
                }
                else
                {
                    Debug.WriteLine("LoadStockCacheFromLocalAsync: no local stock snapshot found.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadStockCacheFromLocalAsync: " + ex.Message);
            }
        }

        private static string GetStrProp(JsonElement el, params string[] names)
        {
            foreach (var prop in el.EnumerateObject())
            {
                foreach (var n in names)
                {
                    if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                        return prop.Value.ValueKind == JsonValueKind.Number
                            ? prop.Value.GetRawText()
                            : prop.Value.GetString() ?? "";
                }
            }
            return "";
        }

        private static decimal GetDecProp(JsonElement el, params string[] names)
        {
            foreach (var prop in el.EnumerateObject())
            {
                foreach (var n in names)
                {
                    if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number)
                            return prop.Value.GetDecimal();
                        // Tolerate stock values sent back as strings, e.g. "25.00"
                        if (prop.Value.ValueKind == JsonValueKind.String
                            && decimal.TryParse(prop.Value.GetString(), out decimal parsed))
                            return parsed;
                    }
                }
            }
            return 0m;
        }
        private async Task<(decimal onHand, decimal reserved)> GetLiveStoreStockAsync(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return (0m, 0m);
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);

                var resp = await http.GetAsync($"{ApiBaseUrl}/api/stock/item?itemId={Uri.EscapeDataString(barcode)}&companyId={_companyId}")
                    .ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode) return (0m, 0m);

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);

                JsonElement obj = root;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl))
                    obj = dataEl;

                if (obj.ValueKind == JsonValueKind.Array && obj.GetArrayLength() > 0)
                    obj = obj[0];

                decimal onHand = GetDecProp(obj, "onHandQty", "OnHandQty", "onHand", "OnHand");
                decimal reserved = GetDecProp(obj, "reservedQty", "ReservedQty", "reserved", "Reserved");

                // Keep the bulk cache in sync too
                _stockCache[barcode] = (onHand, reserved);

                return (onHand, reserved);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetLiveStoreStockAsync: " + ex.Message);
                return (0m, 0m);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  STOCK AVAILABILITY CHECK
        //  Returns the qty still available to sell for this barcode, accounting
        //  for on-hand minus already-reserved (server-side) minus what's already
        //  sitting in the current cart for the same item (excluding the line
        //  being checked, if any).
        // ── Fast, cache-based lookup — used for UI display (tooltips, search rows) ──
        // ── Cache-based lookup — used for UI display (tooltips, search rows) ──
        private (decimal onHand, decimal reserved) GetStoreStock(int itemId)
        {
            if (itemId <= 0) return (0m, 0m);
            if (_stockCache.TryGetValue(itemId.ToString(), out var v)) return v;
            return (0m, 0m);
        }


        // ── Live, authoritative single-item check  (GET /api/stock/item?itemId=X&companyId=Y) ──
        private async Task<(decimal onHand, decimal reserved, bool found)> GetLiveStoreStockAsync(int itemId)
        {
            if (itemId <= 0) return (0m, 0m, false);
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);

                var resp = await http.GetAsync($"{ApiBaseUrl}/api/stock/item?itemId={itemId}&companyId={_companyId}")
                    .ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"GetLiveStoreStockAsync: API returned {(int)resp.StatusCode} for item {itemId}");
                    return (0m, 0m, false);
                }

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);

                JsonElement obj = root;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl))
                    obj = dataEl;

                if (obj.ValueKind == JsonValueKind.Array)
                {
                    if (obj.GetArrayLength() == 0) return (0m, 0m, false);
                    obj = obj[0];
                }
                else if (obj.ValueKind == JsonValueKind.Null)
                {
                    return (0m, 0m, false);
                }

                decimal onHand = GetDecProp(obj, "onHandQty", "OnHandQty", "onHand", "OnHand");
                decimal reserved = GetDecProp(obj, "reservedQty", "ReservedQty", "reserved", "Reserved");

                _stockCache[itemId.ToString()] = (onHand, reserved);

                return (onHand, reserved, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetLiveStoreStockAsync: " + ex.Message);
                return (0m, 0m, false);
            }
        }

        // ── Live, authoritative availability — used for actual enforcement ──
        private async Task<decimal> GetLiveAvailableStockAsync(int itemId, string excludeCartItemName = null, int? excludeUom = null)
        {
            if (itemId <= 0)
            {
                Debug.WriteLine($"GetLiveAvailableStockAsync: itemId not resolved for '{excludeCartItemName}' — treating as 0 available.");
                return 0m;
            }

            string barcode = _cart.FirstOrDefault(c => c.ItemId == itemId)?.Barcode
                           ?? _catalog.FirstOrDefault(p => p.ItemId == itemId)?.Barcode;

            decimal onHand, reserved;
            bool found;

            if (GetOnline())
            {
                (onHand, reserved, found) = await GetLiveStoreStockAsync(itemId).ConfigureAwait(true);
                if (!found)
                    (onHand, reserved, found) = TryGetCachedStock(itemId, barcode);
            }
            else
            {
                (onHand, reserved, found) = TryGetCachedStock(itemId, barcode);
                if (found)
                    Debug.WriteLine($"GetLiveAvailableStockAsync: offline — using cached stock for item {itemId} ({onHand}/{reserved}).");
            }

            if (!found)
            {
                Debug.WriteLine($"GetLiveAvailableStockAsync: no stock record found (online or cached) for item {itemId} — treating as 0 available.");
                return 0m;
            }

            decimal remaining = Math.Max(0m, onHand - reserved);

            decimal alreadyInCart = _cart
                .Where(c => c.ItemId == itemId
                         && !(excludeCartItemName != null
                              && c.Name.Equals(excludeCartItemName, StringComparison.OrdinalIgnoreCase)
                              && (excludeUom == null || c.UOM == excludeUom.Value)))
                .Sum(c => c.Qty * GetUnitsPerPackForCartItem(c));

            decimal queuedOfflineQty = GetQueuedOfflineQtyForItem(itemId);

            return Math.Max(0m, remaining - alreadyInCart - queuedOfflineQty);
        }

        // ── Sums Qty for this ItemId across all not-yet-synced PendingSalesOrders payloads. ──
        private decimal GetQueuedOfflineQtyForItem(int itemId)
        {
            try
            {
                EnsureOfflineSyncSchema();
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
            SELECT PayloadJson FROM PendingSalesOrders
            WHERE CompanyID = @cid AND Synced = 0;";
                cmd.Parameters.AddWithValue("@cid", _companyId);
                using var rdr = cmd.ExecuteReader();

                decimal total = 0m;
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                while (rdr.Read())
                {
                    string json = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    try
                    {
                        var payload = JsonSerializer.Deserialize<CreateSalesOrderPayload>(json, opts);
                        if (payload?.Lines == null) continue;
                        foreach (var line in payload.Lines)
                            if (line.ItemId == itemId)
                                total += line.Qty;   // already base units — don't multiply again  // ← same bug
                    }
                    catch { /* skip malformed rows */ }
                }

                return total;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetQueuedOfflineQtyForItem: " + ex.Message);
                return 0m;
            }
        }

        // ── Reads from the in-memory cache populated by LoadStockCacheAsync while online. ──
        private (decimal onHand, decimal reserved, bool found) TryGetCachedStock(int itemId, string barcode = null)
        {
            if (itemId <= 0 && string.IsNullOrWhiteSpace(barcode)) return (0m, 0m, false);

            // 1. In-memory cache first (fast path, populated by LoadStockCacheAsync this session)
            if (itemId > 0 && _stockCache.TryGetValue(itemId.ToString(), out var byId))
                return (byId.Item1, byId.Item2, true);

            if (!string.IsNullOrWhiteSpace(barcode))
            {
                if (_stockCache.TryGetValue(barcode, out var byBarcode))
                    return (byBarcode.Item1, byBarcode.Item2, true);

                string padded = barcode.PadLeft(13, '0');
                if (_stockCache.TryGetValue(padded, out var byPadded))
                    return (byPadded.Item1, byPadded.Item2, true);
            }

            // 2. In-memory missed — check the persisted SQLite snapshot
            //    (covers the case where the app just restarted offline and
            //    LoadStockCacheAsync's fire-and-forget call hasn't populated
            //    _stockCache yet, or never got the chance to run at all).
            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
            SELECT OnHandQty, ReservedQty FROM StoreStockCache
            WHERE CompanyID = @cid AND ItemID = @item
            LIMIT 1;";
                cmd.Parameters.AddWithValue("@cid", _companyId);
                cmd.Parameters.AddWithValue("@item", itemId > 0 ? itemId.ToString() : barcode);
                Debug.WriteLine($"TryGetCachedStock query params: cid={_companyId}, item={(itemId > 0 ? itemId.ToString() : barcode)}");
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    decimal onHand = rdr.IsDBNull(0) ? 0m : Convert.ToDecimal(rdr.GetDouble(0));
                    decimal reserved = rdr.IsDBNull(1) ? 0m : Convert.ToDecimal(rdr.GetDouble(1));
                    // Warm the in-memory cache too, so subsequent lookups this session are fast.
                    _stockCache[itemId > 0 ? itemId.ToString() : barcode] = (onHand, reserved);
                    return (onHand, reserved, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TryGetCachedStock (SQLite fallback): " + ex.Message);
            }

            Debug.WriteLine($"TryGetCachedStock: MISS for itemId={itemId}, barcode='{barcode}' (checked memory + SQLite).");
            return (0m, 0m, false);
        }





        // ══════════════════════════════════════════════════════════════════════
        //  PRODUCT LOADING — D365 / API TOGGLE
        // ══════════════════════════════════════════════════════════════════════
        // ─────────────────────────────────────────────────────────────────────────────
        //  FULL REPLACEMENT — LoadProductsFromD365Async + helpers
        //
        //  Sync logic:
        //    • First run  (no POS_SyncControl row)  → insert ALL rows
        //    • Subsequent → only rows whose OnHandModifiedDateTime (UTC) > lastSync (UTC)
        //    • Rows with blank OnHandModifiedDateTime → always upsert (safe fallback)
        //    • POS_SyncControl stores time in "dd-MM-yyyy HH.mm" (local) for display,
        //      but comparison is done in UTC to match the API's timestamps
        // ─────────────────────────────────────────────────────────────────────────────

        //private async Task LoadProductsFromD365Async()

        //{

        //    //if (!_useD365)

        //    //{

        //    //    SetD365Mode(false);

        //    //    await LoadProductsFromApiAsync();

        //    //    return;

        //    //}

        //    try

        //    {

        //        ShowStatus("Loading products from D365...", true);

        //        using var http = new System.Net.Http.HttpClient();

        //        http.Timeout = TimeSpan.FromSeconds(60);

        //        string apiUrl = $"{ApiBaseUrl}/api/Product";

        //        var resp = await http.GetAsync(apiUrl).ConfigureAwait(false);

        //        if (!resp.IsSuccessStatusCode)

        //        {

        //            string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        //            ShowStatus($"API error {(int)resp.StatusCode}: {err}", false);

        //            await LoadProductsFromD365SQLiteAsync();

        //            SetD365Mode(true);

        //            return;

        //        }

        //        string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        //        JsonElement root;

        //        try { root = JsonSerializer.Deserialize<JsonElement>(json); }

        //        catch

        //        {

        //            ShowStatus("D365 response parse error.", false);

        //            await LoadProductsFromD365SQLiteAsync();

        //            SetD365Mode(true);

        //            return;

        //        }

        //        JsonElement doc = root;

        //        if (root.ValueKind == JsonValueKind.String)

        //        {

        //            string inner = root.GetString() ?? "";

        //            doc = JsonSerializer.Deserialize<JsonElement>(inner);

        //        }

        //        if (!doc.TryGetProperty("value", out var values))

        //        {

        //            ShowStatus("D365 returned no products.", false);

        //            await LoadProductsFromD365SQLiteAsync();

        //            SetD365Mode(true);

        //            return;

        //        }

        //        // ── Parse JSON ─────────────────────────────────────────────────────

        //        var localMap = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);

        //        var localCatalog = new List<Product>();

        //        var localDetailMap = new Dictionary<string, List<D365ProductDetail>>(StringComparer.OrdinalIgnoreCase);

        //        foreach (var item in values.EnumerateArray())

        //        {

        //            string dataArea = item.TryGetProperty("dataAreaId", out var da) ? da.GetString() ?? "" : "";

        //            string itemId = item.TryGetProperty("ItemId", out var id) ? id.GetString() ?? "" : "";

        //            string name = item.TryGetProperty("NameAlias", out var na) ? na.GetString() ?? "" : "";

        //            string site = item.TryGetProperty("InventSiteId", out var si) ? si.GetString() ?? "" : "";

        //            string location = item.TryGetProperty("InventLocationId", out var lo) ? lo.GetString() ?? "" : "";

        //            string wms = item.TryGetProperty("wMSLocationId", out var wm) ? wm.GetString() ?? "" : "";

        //            string acct = item.TryGetProperty("AccountRelation", out var ar) ? ar.GetString() ?? "" : "";

        //            string etag = item.TryGetProperty("@odata.etag", out var et) ? et.GetString() ?? "" : "";

        //            string onHand = item.TryGetProperty("OnHandModifiedDateTime", out var oh) ? oh.GetString() ?? "" : "";

        //            decimal price = 0m;

        //            if (item.TryGetProperty("Amount", out var amt))

        //                price = amt.ValueKind == JsonValueKind.Number ? amt.GetDecimal() : 0m;

        //            decimal avail = 0m;

        //            if (item.TryGetProperty("AvailPhysical", out var av))

        //                avail = av.ValueKind == JsonValueKind.Number ? av.GetDecimal() : 0m;

        //            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(itemId)) continue;

        //            // One Product per ItemId — first/lowest-price row wins

        //            if (!localMap.ContainsKey(itemId))

        //            {

        //                var prod = new Product { Name = name, Price = price, Barcode = itemId, Category = site };

        //                string padded = itemId.PadLeft(13, '0');

        //                localCatalog.Add(prod);

        //                localMap[itemId] = prod;

        //                localMap[padded] = prod;

        //            }

        //            if (!localDetailMap.ContainsKey(itemId))

        //                localDetailMap[itemId] = new List<D365ProductDetail>();

        //            localDetailMap[itemId].Add(new D365ProductDetail

        //            {

        //                DataAreaId = dataArea,

        //                ItemId = itemId,

        //                NameAlias = name,

        //                OnHandModifiedDateTime = onHand,

        //                AvailPhysical = avail,

        //                InventLocationId = location,

        //                Amount = price,

        //                InventSiteId = site,

        //                WMSLocationId = wms,

        //                AccountRelation = acct,

        //                ODataEtag = etag

        //            });

        //        }

        //        // ── Commit to in-memory state ──────────────────────────────────────

        //        _barcodeMap = localMap;

        //        _catalog = localCatalog;

        //        _d365Details = localDetailMap;

        //        // ── Persist to ShriPOS.db in background (fire-and-forget) ──────────

        //        //   _ = Task.Run(() => SyncD365ToShriPOSDbAsync(_d365Details));

        //        this.BeginInvoke(new Action(() =>

        //        {

        //            BuildAutocomplete();

        //            BuildHotItems();

        //            ShowStatus($"✓ Loaded {localCatalog.Count} products from D365.", true);

        //            SetD365Mode(true);

        //        }));

        //    }

        //    catch (Exception ex)

        //    {

        //        ShowStatus("D365 load failed: " + ex.Message, false);

        //        Debug.WriteLine("LoadProductsFromD365Async: " + ex);

        //        await LoadProductsFromD365SQLiteAsync();

        //        SetD365Mode(true);

        //    }

        //}

        //// ─────────────────────────────────────────────────────────────────────────────
        ////  HELPER: read LastSyncDateTime as UTC DateTime.
        ////
        ////  Stored format in DB : "dd-MM-yyyy HH.mm"  (local time, for display)
        ////  Returned             : UTC DateTime        (for comparison with API timestamps)
        ////  Returns DateTime.MinValue (UTC) if no record found yet.
        //// ─────────────────────────────────────────────────────────────────────────────


        //private async Task LoadProductsFromD365SQLiteAsync()
        //{
        //    try
        //    {
        //        ShowStatus("Loading products from local cache…", true);

        //        if (!System.IO.File.Exists(_dbPath))
        //        {
        //            ShowStatus($"Database not found: {_dbPath}", false);
        //            return;
        //        }

        //        var localMap = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        //        var localCatalog = new List<Product>();
        //        var localDetailMap = new Dictionary<string, List<D365ProductDetail>>(StringComparer.OrdinalIgnoreCase);

        //        using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;Foreign Keys=True;");
        //        await conn.OpenAsync().ConfigureAwait(false);

        //        // Guard: tables may not exist on first run
        //        using (var chk = conn.CreateCommand())
        //        {
        //            chk.CommandText =
        //                "SELECT COUNT(*) FROM sqlite_master " +
        //                "WHERE type='table' AND name='D365Products';";
        //            long exists = (long)(await chk.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
        //            if (exists == 0)
        //            {
        //                ShowStatus("No local product cache — waiting for API sync…", true);
        //                // Still set D365 mode so the button label is correct
        //                _isD365Mode = true;
        //                this.BeginInvoke(new Action(() => SetD365Mode(true)));
        //                return;
        //            }
        //        }

        //        // Load master products
        //        using (var cmd = conn.CreateCommand())
        //        {
        //            cmd.CommandText =
        //                "SELECT ItemId, NameAlias, InventSiteId " +
        //                "FROM D365Products ORDER BY NameAlias;";

        //            using var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        //            while (await rdr.ReadAsync().ConfigureAwait(false))
        //            {
        //                string itemId = rdr.IsDBNull(0) ? "" : Convert.ToString(rdr.GetValue(0)) ?? "";
        //                string name = rdr.IsDBNull(1) ? "" : Convert.ToString(rdr.GetValue(1)) ?? "";
        //                string site = rdr.IsDBNull(2) ? "" : Convert.ToString(rdr.GetValue(2)) ?? "";

        //                if (string.IsNullOrWhiteSpace(itemId)) continue;

        //                var prod = new Product { Name = name, Barcode = itemId, Category = site, Price = 0m };
        //                string padded = itemId.PadLeft(13, '0');
        //                localCatalog.Add(prod);
        //                localMap[itemId] = prod;
        //                localMap[padded] = prod;
        //                localDetailMap[itemId] = new List<D365ProductDetail>();
        //            }
        //        }

        //        if (localCatalog.Count == 0)
        //        {
        //            ShowStatus("Local cache is empty — waiting for API sync…", true);
        //            _isD365Mode = true;
        //            this.BeginInvoke(new Action(() => SetD365Mode(true)));
        //            return;
        //        }

        //        // Load detail rows (ORDER BY Amount → lowest price first)
        //        using (var cmd = conn.CreateCommand())
        //        {
        //            cmd.CommandText = @"
        //        SELECT DataAreaId, ItemId, NameAlias, OnHandModifiedDateTime,
        //               AvailPhysical, InventLocationId, Amount,
        //               InventSiteId, WMSLocationId, AccountRelation, ODataEtag
        //        FROM   D365ProductDetails
        //        ORDER  BY ItemId, Amount;";

        //            using var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        //            while (await rdr.ReadAsync().ConfigureAwait(false))
        //            {
        //                string itemId = rdr.IsDBNull(1) ? "" : Convert.ToString(rdr.GetValue(1)) ?? "";
        //                if (!localDetailMap.ContainsKey(itemId)) continue;

        //                decimal availPhysical = 0m, amount = 0m;
        //                try { if (!rdr.IsDBNull(4)) availPhysical = Convert.ToDecimal(rdr.GetValue(4)); } catch { }
        //                try { if (!rdr.IsDBNull(6)) amount = Convert.ToDecimal(rdr.GetValue(6)); } catch { }

        //                var detail = new D365ProductDetail
        //                {
        //                    DataAreaId = rdr.IsDBNull(0) ? "" : Convert.ToString(rdr.GetValue(0)) ?? "",
        //                    ItemId = itemId,
        //                    NameAlias = rdr.IsDBNull(2) ? "" : Convert.ToString(rdr.GetValue(2)) ?? "",
        //                    OnHandModifiedDateTime = rdr.IsDBNull(3) ? "" : Convert.ToString(rdr.GetValue(3)) ?? "",
        //                    AvailPhysical = availPhysical,
        //                    InventLocationId = rdr.IsDBNull(5) ? "" : Convert.ToString(rdr.GetValue(5)) ?? "",
        //                    Amount = amount,
        //                    InventSiteId = rdr.IsDBNull(7) ? "" : Convert.ToString(rdr.GetValue(7)) ?? "",
        //                    WMSLocationId = rdr.IsDBNull(8) ? "" : Convert.ToString(rdr.GetValue(8)) ?? "",
        //                    AccountRelation = rdr.IsDBNull(9) ? "" : Convert.ToString(rdr.GetValue(9)) ?? "",
        //                    ODataEtag = rdr.IsDBNull(10) ? "" : Convert.ToString(rdr.GetValue(10)) ?? "",
        //                };

        //                localDetailMap[itemId].Add(detail);

        //                // First row (lowest price) sets the Product.Price
        //                if (localMap.TryGetValue(itemId, out var prod) && prod.Price == 0m)
        //                    prod.Price = detail.Amount;
        //            }
        //        }

        //        // Commit to in-memory state on the background thread — safe for reads,
        //        // UI thread only reads these after BeginInvoke fires.
        //        _barcodeMap = localMap;
        //        _catalog = localCatalog;
        //        _d365Details = localDetailMap;

        //        // ── KEY FIX: set the mode flag HERE (background thread), before the
        //        //    UI dispatch, so that if the user clicks the Save button in the
        //        //    tiny window between this line and BeginInvoke executing, the flag
        //        //    is already correct and they won't see "Insufficient balance". ────
        //        _isD365Mode = true;
        //        _useD365 = true;

        //        // Update UI controls on the UI thread
        //        this.BeginInvoke(new Action(() =>
        //        {
        //            BuildAutocomplete();
        //            BuildHotItems();
        //            SetD365Mode(true);    // refreshes button label / status text
        //            ShowStatus($"✓ {localCatalog.Count} products loaded.", true);
        //        }));
        //    }
        //    catch (Exception ex)
        //    {
        //        ShowStatus("Cache load failed: " + ex.Message, false);
        //        Debug.WriteLine("LoadProductsFromD365SQLiteAsync: " + ex);
        //    }
        //}
        private async Task LoadProductsFromD365Async()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(60);
                string apiUrl = $"{ApiBaseUrl}/api/item";
                var resp = await http.GetAsync(apiUrl).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    ShowStatus($"API error {(int)resp.StatusCode}: {err}", false);
                    await LoadProductsFromSQLite();
                    SetD365Mode(true);
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                JsonElement root;
                try { root = JsonSerializer.Deserialize<JsonElement>(json); }
                catch
                {
                    ShowStatus("D365 response parse error.", false);
                    await LoadProductsFromSQLite();
                    SetD365Mode(true);
                    return;
                }

                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                {
                    ShowStatus("API returned no products.", false);
                    await LoadProductsFromSQLite();
                    SetD365Mode(true);
                    return;
                }

                // ── Parse items into catalog/barcode map ───────────────────────────
                var localMap = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
                var localCatalog = new List<Product>();

                foreach (var item in root.EnumerateArray())
                {
                    string name = item.TryGetProperty("itemName", out var n) ? n.GetString() ?? ""
                                 : item.TryGetProperty("ItemName", out var n2) ? n2.GetString() ?? "" : "";

                    string barcode = item.TryGetProperty("barCode", out var bc) ? bc.GetString() ?? ""
                                    : item.TryGetProperty("BarCode", out var bc2) ? bc2.GetString() ?? "" : "";

                    decimal price = 0m;
                    if (item.TryGetProperty("sellingPrice", out var p) && p.ValueKind == JsonValueKind.Number)
                        price = p.GetDecimal();
                    else if (item.TryGetProperty("SellingPrice", out var p2) && p2.ValueKind == JsonValueKind.Number)
                        price = p2.GetDecimal();
                    int salesTaxId = 0;
                    if (item.TryGetProperty("salesTaxID", out var stx) && stx.ValueKind == JsonValueKind.Number)
                        salesTaxId = stx.GetInt32();
                    else if (item.TryGetProperty("SalesTaxID", out var stx2) && stx2.ValueKind == JsonValueKind.Number)
                        salesTaxId = stx2.GetInt32();

                    string category = "";
                    if (item.TryGetProperty("category", out var cat))
                        category = cat.ValueKind == JsonValueKind.Number ? cat.GetInt32().ToString() : cat.GetString() ?? "";
                    else if (item.TryGetProperty("Category", out var cat2))
                        category = cat2.ValueKind == JsonValueKind.Number ? cat2.GetInt32().ToString() : cat2.GetString() ?? "";

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    int baseUomId = item.TryGetProperty("baseUOM", out var bu) && bu.ValueKind == JsonValueKind.Number
      ? bu.GetInt32() : 1;
                    string baseUomName = item.TryGetProperty("baseUOMName", out var bun) ? bun.GetString() ?? "" : "";

                    var availableUOMs = new List<UomDto>
{
    new UomDto
    {
        UomId = baseUomId,
        UomDescription = baseUomName,
        UnitsPerPack = 1,
        RetailPrice = price          // ← base UOM sells at the item's normal selling price
    }
};

                    if (item.TryGetProperty("packSizes", out var packs) && packs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pack in packs.EnumerateArray())
                        {
                            int puom = pack.TryGetProperty("uomid", out var pu) ? pu.GetInt32() : 0;
                            var master = _uomMaster.FirstOrDefault(u => u.UomId == puom);

                            if (master != null)
                            {
                                decimal packRetail = pack.TryGetProperty("retailPrice", out var rp) && rp.ValueKind == JsonValueKind.Number
                                    ? rp.GetDecimal() : price;

                                int packUnits = 1;
                                if (pack.TryGetProperty("unitsPerPack", out var up) && up.ValueKind == JsonValueKind.Number)
                                {
                                    if (!up.TryGetInt32(out packUnits))
                                    {
                                        // Value is a number but not an "integer-shaped" token (e.g. "1.0", "2.00") —
                                        // GetInt32()/TryGetInt32() reject that even though ValueKind is Number.
                                        packUnits = up.TryGetDecimal(out decimal upDec)
                                            ? (int)Math.Round(upDec, MidpointRounding.AwayFromZero)
                                            : 1;
                                    }
                                }

                                availableUOMs.Add(new UomDto
                                {
                                    UomId = master.UomId,
                                    UomDescription = master.UomDescription,
                                    UnitsPerPack = packUnits,
                                    RetailPrice = packRetail
                                });
                            }
                        }
                    }

                    var prod = new Product
                    {
                        Name = name,
                        Price = price,
                        Barcode = barcode,
                        Category = category,
                        UOM = baseUomId,
                        AvailableUOMs = availableUOMs,
                        SalesTaxID = salesTaxId
                    };

                    localCatalog.Add(prod);

                    if (!string.IsNullOrWhiteSpace(prod.Barcode))
                    {
                        string padded = prod.Barcode.PadLeft(13, '0');
                        if (!localMap.ContainsKey(prod.Barcode)) localMap[prod.Barcode] = prod;
                        if (!localMap.ContainsKey(padded)) localMap[padded] = prod;
                    }
                }

                if (localCatalog.Count == 0)
                {
                    ShowStatus("API returned empty catalog.", false);
                    await LoadProductsFromSQLite();
                    SetD365Mode(true);
                    return;
                }

                // ── Commit BEFORE dispatching to UI ─────────────────────────────────
                _catalog = localCatalog;
                _barcodeMap = localMap;
                await SaveCatalogToLocalCacheAsync(localCatalog).ConfigureAwait(true);
                // Resolve ItemId for every product now that the catalog is loaded
                await LoadItemIdMapAsync().ConfigureAwait(true);
                foreach (var p in _catalog)
                    p.ItemId = ResolveItemId(p.Name, p.Barcode);

                this.BeginInvoke(new Action(() =>
                {
                    BuildAutocomplete();
                    BuildHotItems();
                    ShowStatus($"✓ Loaded {localCatalog.Count} products.", true);
                    SetD365Mode(true);
                }));
            }
            catch (Exception ex)
            {
                ShowStatus("D365 load failed: " + ex.Message, false);
                Debug.WriteLine("LoadProductsFromD365Async: " + ex);
                //await LoadProductsFromSQLite();
                await LoadProductsFromLocalCacheAsync();
                SetD365Mode(true);
            }
        }
        private async Task LoadProductsFromSQLite()
        {
            if (!System.IO.File.Exists(_dbPath)) { ShowStatus($"DB missing: {_dbPath}", false); return; }
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    using (var pragma = new SQLiteCommand("PRAGMA journal_mode=WAL;", conn))
                        pragma.ExecuteNonQuery();
                    using (var chk = new SQLiteCommand(
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Item';", conn))
                    {
                        long exists = (long)(chk.ExecuteScalar() ?? 0L);
                        if (exists == 0) { ShowStatus("Table 'Item' not found in database!", false); return; }
                    }
                    const string sql = @"
                        SELECT ItemID, ItemName, SellingPrice, BarCode, CategoryID
                        FROM Item WHERE CompanyID = @CompanyID ORDER BY ItemName;";
                    using var cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@CompanyID", _companyId);
                    using var reader = cmd.ExecuteReader();
                    var localMap = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
                    var localCatalog = new List<Product>();
                    while (reader.Read())
                    {
                        decimal price = reader.IsDBNull(reader.GetOrdinal("SellingPrice"))
                            ? 0m : Convert.ToDecimal(reader["SellingPrice"]);
                        var prod = new Product
                        {
                            Name = reader["ItemName"]?.ToString()?.Trim() ?? "Unknown",
                            Price = price,
                            Barcode = reader["BarCode"]?.ToString()?.Trim() ?? "",
                            Category = reader["CategoryID"]?.ToString() ?? "General"
                        };
                        if (string.IsNullOrWhiteSpace(prod.Name)) continue;
                        localCatalog.Add(prod);
                        if (!string.IsNullOrEmpty(prod.Barcode))
                        {
                            string raw = prod.Barcode, padded = raw.PadLeft(13, '0');
                            if (!localMap.ContainsKey(raw)) localMap[raw] = prod;
                            if (!localMap.ContainsKey(padded)) localMap[padded] = prod;
                        }
                    }
                    _barcodeMap = localMap;
                    _catalog = localCatalog;
                }).ConfigureAwait(false);

                this.BeginInvoke(new Action(() =>
                    ShowStatus($"✓ Loaded {_catalog.Count} products from SQLite.", true)));
            }
            catch (Exception ex) { ShowStatus($"DB load error: {ex.Message}", false); }
        }
        private DateTime GetLastSyncDateTimeUtc(string syncType)
        {
            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT LastSyncDateTime FROM POS_SyncControl WHERE SyncType = @t LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@t", syncType);
                object val = cmd.ExecuteScalar();

                if (val != null && val != DBNull.Value)
                {
                    string stored = val.ToString();

                    // Primary format: "dd-MM-yyyy HH.mm"  (local time)
                    if (DateTime.TryParseExact(
                            stored,
                            "dd-MM-yyyy HH.mm",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime localDt))
                    {
                        // Convert local → UTC so it matches the API's UTC timestamps
                        return localDt.ToUniversalTime();
                    }

                    // Fallback: ISO 8601 (old records written with ToString("o"))
                    if (DateTime.TryParse(stored, null,
                            System.Globalization.DateTimeStyles.RoundtripKind,
                            out DateTime isoDt))
                    {
                        return isoDt.ToUniversalTime();
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("GetLastSyncDateTimeUtc: " + ex.Message); }

            return DateTime.MinValue;   // triggers full sync
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  HELPER: write / update POS_SyncControl row.
        //  SyncType         = screen name, e.g. "SalesForm"
        //  LastSyncDateTime = local time in "dd-MM-yyyy HH.mm" for human readability
        // ─────────────────────────────────────────────────────────────────────────────
        private static void UpsertSyncControl(
            SQLiteConnection conn, SQLiteTransaction tx,
            string syncType)
        {
            string formatted = DateTime.Now.ToString("dd-MM-yyyy HH.mm");

            using var cmd = new SQLiteCommand(@"
        INSERT INTO POS_SyncControl (SyncType, LastSyncDateTime)
        VALUES (@type, @dt)
        ON CONFLICT(SyncType) DO UPDATE SET LastSyncDateTime = excluded.LastSyncDateTime",
                conn, tx);
            cmd.Parameters.AddWithValue("@type", syncType);
            cmd.Parameters.AddWithValue("@dt", formatted);
            cmd.ExecuteNonQuery();
        }


        // ══════════════════════════════════════════════════════════════════════
        //  D365 → ShriPOS.db SYNC
        //  Writes into D365Products + D365ProductDetails (created by
        //  DatabaseInitializer).  Skips rows whose OnHandModifiedDateTime
        //  hasn't changed since last sync.
        // ══════════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════════
        //  LOAD D365 PRODUCTS FROM ShriPOS.db  (offline / API-unreachable fallback)
        //  Reads D365Products + D365ProductDetails — written by SyncD365ToShriPOSDbAsync
        //  or by SyncService.SyncD365ProductsAsync.
        // ══════════════════════════════════════════════════════════════════════


        private void SetD365Mode(bool d365)
        {
            _isD365Mode = d365;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetD365Mode(d365)));
                return;
            }

            PositionFooterButtons();

            // Payment is now collected via a popup at Tender time — the legacy
            // on-screen split/numpad controls stay hidden permanently.
            Control[] paymentControls = new Control[]
            {
        panelSplitCash, panelSplitUpi, panelSplitCard,
        panelNumpad, lblSplitBalance, btnSplitExact,
            };

            foreach (var ctrl in paymentControls)
                if (ctrl != null) { ctrl.Visible = false; ctrl.Enabled = false; }

            if (_pnlSaleTypeToggle != null) _pnlSaleTypeToggle.Enabled = true;

            if (lblInvoiceNo.Text.StartsWith("QUO-"))
                lblInvoiceNo.Text = SalesRepository.NextInvoiceNo();
        }
        private Control FindControlByName(Control parent, string name)
        {
            foreach (Control c in parent.Controls)
            {
                if (c.Name == name) return c;
                var found = FindControlByName(c, name);
                if (found != null) return found;
            }
            return null;
        }
        // ── In SetPendingPaymentMode() — show and enable the button ────────
        private void SetPendingPaymentMode()
        {
            if (lblSplitBalance != null)
                lblSplitBalance.Visible = false;

            // Customer dropdown only shows during pending-invoice payment collection
            if (_cmbCustomer != null)
            {
                _cmbCustomer.Visible = true;
                _cmbCustomer.Enabled = false;   // locked — customer was already set when SO was created
            }

            if (_searchWrapper != null)
            {
                _searchWrapper.Enabled = false;
                _searchWrapper.BackColor = Color.FromArgb(28, 32, 40);
            }
            if (_barcodeWrapper != null)
            {
                _barcodeWrapper.Enabled = false;
                _barcodeWrapper.BackColor = Color.FromArgb(28, 32, 40);
            }

            txtSearch.Enabled = false;
            txtBarcode.Enabled = false;
            panelHotItems.Enabled = false;
            panelHotItems.BackColor = Color.FromArgb(28, 32, 40);
            if (nudDiscount != null) nudDiscount.Enabled = false;

            LockCartRows();
            ShowPendingBanner();

            if (btnTenderSale != null)
            {
                btnTenderSale.Text = "✅  Tender Sale  (F1)";
                btnTenderSale.BackColor = Color.FromArgb(34, 197, 94);
                btnTenderSale.Visible = true;
                btnTenderSale.Enabled = true;
            }

            ShowStatus("📋 Pending invoice — collect Cash / Card / Bank Transfer.", true);
        }

        private void LockCartRows()
        {
            foreach (Control row in panelCartItems.Controls)
            {
                // Disable all buttons within each cart row
                foreach (Control c in row.Controls)
                    if (c is Button) c.Enabled = false;

                // Remove click-popup handler by making cursor default
                row.Cursor = Cursors.Default;
                // Remove all Click handlers on the row by resetting them
                // (simplest approach: just disable the whole row for interaction)
                row.Enabled = false;
            }
        }

        private Label _pendingBanner;
        private void ShowPendingBanner()
        {
            if (_pendingBanner != null && !_pendingBanner.IsDisposed)
                _pendingBanner.Dispose();

            _pendingBanner = new Label
            {
                Text = "📋  PENDING INVOICE — Payment Only Mode",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 146, 60),
                BackColor = Color.FromArgb(50, 38, 18),
                AutoSize = false,
                Size = new Size(panelCartItems.Width - 4, 28),
                Location = new Point(2, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Insert at top of cart panel (shift existing rows down)
            foreach (Control c in panelCartItems.Controls)
                c.Location = new Point(c.Left, c.Top + 30);

            panelCartItems.Controls.Add(_pendingBanner);
            _pendingBanner.BringToFront();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PENDING INVOICE — SAVE
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Serializes the current cart and saves it as an Unpaid pending invoice.
        /// Call this from a "Save / Hold" button.
        /// </summary>



        // ══════════════════════════════════════════════════════════════════════
        //  PENDING INVOICE — RESTORE INTO CART
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Restores a saved pending invoice back into the cart.
        /// Called from PendingInvoicesForm when the cashier clicks "Open & Pay".
        /// </summary> 
        // ══════════════════════════════════════════════════════════════════════
        //  FLOAT CASH
        // ══════════════════════════════════════════════════════════════════════
        private void BuildFloatFooterLabel()
        {
            lblFloatDisplay = new Label
            {
                Text = FloatText(),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = ShiftState.IsOpen ? AccYellow : AccRed,
                BackColor = Color.Transparent,
                AutoSize = true,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            lblFloatDisplay.Click += (s, e) => OpenFloatManager();
            var tip = new ToolTip();
            tip.SetToolTip(lblFloatDisplay, "Click to open Float Entry");
            panelFooterBar.Controls.Add(lblFloatDisplay);
            lblFloatDisplay.BringToFront();
        }

        private void BuildStockReductionLabel()
        {
            if (lblStockReduction != null) return; // already built

            lblStockReduction = new Label
            {
                Text = "📉 Stock to reduce: 0 unit(s)",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Size = new Size(lblGrandTotal.Width, 20),
                Location = new Point(lblGrandTotal.Left, lblGrandTotal.Bottom + 4)
            };

            // Same parent panel that already holds lblSubtotalVal/lblDiscountVal/lblTaxVal/lblGrandTotal
            lblGrandTotal.Parent.Controls.Add(lblStockReduction);
            lblStockReduction.BringToFront();

            // Push the cash/upi/card split panels down to make room, so it doesn't overlap
            int shiftDown = lblStockReduction.Height + 4;
            if (panelSplitCash != null) panelSplitCash.Top += shiftDown;
            if (panelSplitUpi != null) panelSplitUpi.Top += shiftDown;
            if (panelSplitCard != null) panelSplitCard.Top += shiftDown;
            if (panelNumpad != null) panelNumpad.Top += shiftDown;
            if (lblSplitBalance != null) lblSplitBalance.Top += shiftDown;
        }
        private void RefreshFloatLabel()
        {
            if (lblFloatDisplay == null || lblFloatDisplay.IsDisposed) return;
            lblFloatDisplay.Text = FloatText();
            lblFloatDisplay.ForeColor = ShiftState.IsOpen
                ? (ShiftState.CurrentFloat <= 0 ? AccRed : AccYellow)
                : AccRed;
        }

        private string FloatText() =>
            ShiftState.IsOpen
                ? $"🪙 Float: {Fmt(ShiftState.CurrentFloat)}"
                : "🪙 No shift open";

        private void OpenFloatManager()
        {
            var fm = new FloatManagerForm(_companyId, CurrentUser.UserInfo.UserID, _currencySymbol, _companyName);
            fm.FormClosed += (s, e) => RefreshFloatLabel();
            fm.Show(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  FOOTER BUTTONS POSITIONING
        // ══════════════════════════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════════════════════════════
        //  REPLACE the existing PositionFooterButtons() method with this version
        // ═══════════════════════════════════════════════════════════════════════════

        // REPLACE entire PositionFooterButtons():
        private void PositionFooterButtons()
        {
            if (panelFooterBar == null) return;

            int h = panelFooterBar.Height;
            int btnH = 28;
            int btnY = Math.Max(2, (h - btnH) / 2);
            int x = 8;

            // Left side — source toggle
            var btnToggle = panelFooterBar.Controls
                .OfType<Button>()
                .FirstOrDefault(b => b.Name == "btnToggleSource");
            if (btnToggle != null)
            {
                btnToggle.Size = new Size(btnToggle.Width, btnH);
                btnToggle.Location = new Point(x, btnY);
                x += btnToggle.Width + 6;
            }

            // Right side — work from right edge inward
            int rx = panelFooterBar.Width - 8;

            // Float label (rightmost)
            if (lblFloatDisplay != null)
            {
                lblFloatDisplay.Location = new Point(
                    rx - lblFloatDisplay.Width,
                    (h - lblFloatDisplay.Height) / 2);
                rx -= lblFloatDisplay.Width + 10;
            }

            // Reprint button — just left of float label
            if (btnPrintLast != null)
            {
                btnPrintLast.Size = new Size(104, btnH);
                btnPrintLast.Location = new Point(rx - 104, btnY);
                rx -= 110;
            }
            if (btnCharges != null)
            {
                btnCharges.Size = new Size(120, btnH);
                btnCharges.Location = new Point(rx - 120, btnY);
                rx -= 126;
            }

            // Day End button — just left of reprint
            var btnDayEnd = panelFooterBar.Controls
                .OfType<Button>()
                .FirstOrDefault(b => b.Text.Contains("Day End"));
            if (btnDayEnd != null)
            {
                btnDayEnd.Size = new Size(130, btnH);
                btnDayEnd.Location = new Point(rx - 130, btnY);
            }
        }
        private void ShowChargesDialog()
        {
            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(22, 26, 36),
                ClientSize = new Size(520, 420),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 14);

            var pnlHead = new Panel { BackColor = Color.FromArgb(42, 46, 58), Size = new Size(520, 50), Location = Point.Empty };
            pnlHead.Controls.Add(new Label
            {
                Text = "💰  Order Charges",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(420, 50),
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
                Size = new Size(44, 50),
                Location = new Point(476, 0),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (s, e) => dlg.Close();
            pnlHead.Controls.Add(btnX);
            dlg.Controls.Add(pnlHead);

            var listPanel = new Panel { Location = new Point(16, 60), Size = new Size(488, 260), AutoScroll = true, BackColor = Color.FromArgb(28, 32, 42) };
            dlg.Controls.Add(listPanel);

            void RenderRows()
            {
                listPanel.SuspendLayout();
                listPanel.Controls.Clear();
                int y = 4;
                for (int i = 0; i < _charges.Count; i++)
                {
                    var chg = _charges[i];
                    int idx = i;
                    var row = new Panel { Size = new Size(464, 40), Location = new Point(4, y), BackColor = Color.FromArgb(36, 40, 52) };

                    var cmbType0 = new ComboBox
                    {
                        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                        ForeColor = TextWhite,
                        BackColor = Color.FromArgb(44, 48, 60),
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        FlatStyle = FlatStyle.Flat,
                        Size = new Size(160, 28),
                        Location = new Point(0, 6)
                    };
                    foreach (var c in _chargesMaster) cmbType0.Items.Add(c.ChargesName);
                    int sel = _chargesMaster.FindIndex(c => c.ChargesID == chg.ChargesID);
                    cmbType0.SelectedIndex = sel >= 0 ? sel : -1;
                    cmbType0.SelectedIndexChanged += (s, e) =>
                    {
                        if (cmbType0.SelectedIndex < 0) return;
                        var picked = _chargesMaster[cmbType0.SelectedIndex];
                        _charges[idx].ChargesID = picked.ChargesID;
                        _charges[idx].ChargesName = picked.ChargesName;
                        _chargesAllocated = false;
                    };

                    var txtAmt = new TextBox
                    {
                        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                        ForeColor = TextWhite,
                        BackColor = Color.FromArgb(44, 48, 60),
                        BorderStyle = BorderStyle.FixedSingle,
                        Size = new Size(90, 28),
                        Location = new Point(168, 6),
                        Text = chg.Amount > 0 ? chg.Amount.ToString("F2") : ""
                    };
                    txtAmt.TextChanged += (s, e) =>
                    {
                        decimal.TryParse(txtAmt.Text, out decimal amt);
                        _charges[idx].Amount = amt;
                        _chargesAllocated = false;
                    };

                    var cmbDist = new ComboBox
                    {
                        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                        ForeColor = TextWhite,
                        BackColor = Color.FromArgb(44, 48, 60),
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        FlatStyle = FlatStyle.Flat,
                        Size = new Size(120, 28),
                        Location = new Point(264, 6)
                    };
                    cmbDist.Items.AddRange(new object[] { "Fixed", "By Quantity", "Equally" });
                    cmbDist.SelectedIndex = chg.Type - 1;
                    cmbDist.SelectedIndexChanged += (s, e) =>
                    {
                        _charges[idx].Type = cmbDist.SelectedIndex + 1;
                        _chargesAllocated = false;
                    };

                    var btnDel = new Button
                    {
                        Text = "🗑",
                        Font = new Font("Segoe UI", 10F),
                        ForeColor = AccRed,
                        BackColor = Color.Transparent,
                        FlatStyle = FlatStyle.Flat,
                        Size = new Size(30, 28),
                        Location = new Point(392, 6),
                        Cursor = Cursors.Hand
                    };
                    btnDel.FlatAppearance.BorderSize = 0;
                    btnDel.Click += (s, e) =>
                    {
                        _charges.RemoveAt(idx);
                        foreach (var l in _cart) l.Charges = 0m;
                        _chargesAllocated = false;
                        RenderRows();
                        RefreshCart(); UpdateTotals(); RefreshChargesButtonLabel();
                    };

                    row.Controls.AddRange(new Control[] { cmbType0, txtAmt, cmbDist, btnDel });
                    listPanel.Controls.Add(row);
                    y += 46;
                }
                listPanel.ResumeLayout();
            }
            RenderRows();

            var btnAdd = new Button
            {
                Text = "+ Add Charge",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 34),
                Location = new Point(16, 330),
                Cursor = Cursors.Hand
            };
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Click += (s, e) => { _charges.Add(new SaleCharge { Type = 1 }); _chargesAllocated = false; RenderRows(); };
            dlg.Controls.Add(btnAdd);

            var btnAllocate = new Button
            {
                Text = "⚡ Allocate",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 34),
                Location = new Point(180, 330),
                Cursor = Cursors.Hand
            };
            btnAllocate.FlatAppearance.BorderSize = 0;
            btnAllocate.Click += (s, e) => { AllocateCharges(); RefreshChargesButtonLabel(); };
            dlg.Controls.Add(btnAllocate);

            var btnDone = new Button
            {
                Text = "Done",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 34),
                Location = new Point(344, 330),
                Cursor = Cursors.Hand
            };
            btnDone.FlatAppearance.BorderSize = 0;
            btnDone.Click += (s, e) => dlg.Close();
            dlg.Controls.Add(btnDone);

            dlg.Controls.Add(new Label
            {
                Text = "Fixed → added to Grand Total only.  By Quantity / Equally → distributed across line items on Allocate.",
                Font = new Font("Segoe UI", 7.5F, FontStyle.Italic),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(488, 30),
                Location = new Point(16, 376)
            });

            dlg.ShowDialog(this);
            RefreshChargesButtonLabel();
        }

        private void AllocateCharges()
        {
            if (_charges.Count == 0) { ShowStatus("No charges to allocate.", false); return; }

            foreach (var c in _charges)
            {
                if (c.ChargesID <= 0) { ShowStatus("Please select a charge type for every row.", false); return; }
                if (c.Amount <= 0) { ShowStatus("Please enter a valid amount for every charge.", false); return; }
            }

            if (_cart.Count == 0 || _cart.All(l => l.Qty <= 0))
            {
                ShowStatus("Add items with quantity before allocating charges.", false);
                return;
            }

            foreach (var l in _cart) l.Charges = 0m;

            var activeLines = _cart.Where(l => l.Qty > 0).ToList();
            decimal totalQty = activeLines.Sum(l => l.Qty * GetUnitsPerPackForCartItem(l));
            int numLines = activeLines.Count;

            foreach (var charge in _charges)
            {
                if (charge.Amount <= 0) continue;
                if (charge.Type == 1) continue; // Fixed — added to Grand Total directly, not lines

                if (charge.Type == 2 && totalQty > 0)
                {
                    foreach (var l in activeLines)
                    {
                        decimal qty = l.Qty * GetUnitsPerPackForCartItem(l);
                        l.Charges = Math.Round(l.Charges + (charge.Amount * qty) / totalQty, 2);
                    }
                }
                else if (charge.Type == 3 && numLines > 0)
                {
                    decimal share = charge.Amount / numLines;
                    foreach (var l in activeLines)
                        l.Charges = Math.Round(l.Charges + share, 2);
                }
            }

            _chargesAllocated = true;
            RefreshCart();
            UpdateTotals();
            ShowStatus("✓ Charges allocated.", true);
        }
        // ══════════════════════════════════════════════════════════════════════
        //  ONLINE CHECK
        // ══════════════════════════════════════════════════════════════════════
        private static bool IsOnline()
        {
            try
            {
                var url = ApiBaseUrl?.Trim();

                if (string.IsNullOrWhiteSpace(url))
                    return false;

                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return false;

                int port = uri.Port > 0 ? uri.Port
                         : uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;

                using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var result = sock.BeginConnect(uri.Host, port, null, null);

                bool connected = result.AsyncWaitHandle.WaitOne(1500);
                if (connected) sock.EndConnect(result);

                return connected;
            }
            catch
            {
                return false;
            }
        }

        private bool GetOnline()
        {
            if (_onlineCache.HasValue &&
                (DateTime.UtcNow - _onlineChecked).TotalSeconds < ONLINE_CACHE_SECONDS)
                return _onlineCache.Value;
            _onlineCache = IsOnline();
            _onlineChecked = DateTime.UtcNow;
            return _onlineCache.Value;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SETTINGS / CURRENCY
        // ══════════════════════════════════════════════════════════════════════
        //private async Task LoadCompanySettingsAsync()
        //{
        //    try
        //    {
        //        if (GetOnline())
        //        {
        //            try
        //            {
        //                var api = new ApiService();
        //                string ep = _companyId > 0 ? $"api/Currency/{_companyId}" : "api/Currency";
        //                string json = await api.GetAsync(ep).ConfigureAwait(false);
        //                if (!string.IsNullOrEmpty(json))
        //                {
        //                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        //                    var currencies = JsonSerializer.Deserialize<List<CurrencyDto>>(json, opts);
        //                    if (currencies?.Count > 0)
        //                    {
        //                        var c = currencies[0];
        //                        if (!string.IsNullOrWhiteSpace(c.CurrencySymbol))
        //                        {
        //                            _currencySymbol = c.CurrencySymbol.Trim();
        //                            _currencyId = c.CurrencyID;
        //                        }
        //                    }
        //                }
        //            }
        //            catch (Exception ex) { Debug.WriteLine("API Currency: " + ex.Message); }
        //        }
        //        else
        //        {
        //            await LoadCurrencyFromSQLite().ConfigureAwait(false);
        //            this.BeginInvoke(new Action(RefreshAllCurrencyLabels));
        //        }
        //    }
        //    catch (Exception ex) { Debug.WriteLine("LoadCompanySettingsAsync: " + ex.Message); }
        //}

        //private async Task LoadCurrencyFromSQLite()
        //{
        //    if (!System.IO.File.Exists(_dbPath)) return;
        //    try
        //    {
        //        await Task.Run(() =>
        //        {
        //            using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
        //            conn.Open();
        //            const string sql = @"
        //                SELECT CurrencyID, CurrencySymbol
        //                FROM CurrencyMaster
        //                WHERE CompanyID = @CompanyID LIMIT 1;";
        //            using var cmd = new SQLiteCommand(sql, conn);
        //            cmd.Parameters.AddWithValue("@CompanyID", _companyId);
        //            using var r = cmd.ExecuteReader();
        //            if (r.Read())
        //            {
        //                _currencyId = Convert.ToInt32(r["CurrencyID"]);
        //                string sym = r["CurrencySymbol"]?.ToString()?.Trim();
        //                if (!string.IsNullOrWhiteSpace(sym)) _currencySymbol = sym;
        //            }
        //        }).ConfigureAwait(false);
        //    }
        //    catch (Exception ex) { Debug.WriteLine("LoadCurrencyFromSQLite: " + ex.Message); }
        //}

        //private void LoadCompanyInfo()
        //{
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
        //}

        private void LoadCompanyInfo()
        {
            try
            {
                _companyName = "EuroTex";
                _companyAddress = "Address";
                _companyPhone = "23456765432";
            }
            catch { }
        }


        // ══════════════════════════════════════════════════════════════════════
        //  RECEIPT DATA
        // ══════════════════════════════════════════════════════════════════════
        private ReceiptData PrepareReceiptData()
        {
            var data = new ReceiptData
            {
                InvoiceNo = lblInvoiceNo.Text,
                CompanyName = _companyName ?? "ABC",
                CompanyAddress = _companyAddress ?? "",
                CompanyPhone = _companyPhone ?? "",
                CustomerVat = _customerVatValue,
                CustomerName = string.IsNullOrWhiteSpace(_customerNameValue) ? "Walk-in" : _customerNameValue,
                CustomerAddress = _customerAddressValue,
                CashierName = lblOperator.Text ?? "ADMIN",
                SaleDate = DateTime.Now,
                CurrencySymbol = _currencySymbol,
                Subtotal = _subtotal,
                DiscountTotal = _cart.Sum(i => i.DiscountAmt),
                TaxTotal = _cart.Sum(i => i.TaxAmt),
                GrandTotal = GrandTotal(),
                PaidCash = _splitCash,
                SalesOfficeInfo = _salesOfficeInfo,
                PaidDigital = _splitUpi,
                DigitalMethodName = "Bank Transfer",
                PaidCard = _splitCard,
                Change = (_splitCash + _splitUpi + _splitCard) - GrandTotal(),
                IsQuotation = false,

            };
            foreach (var item in _cart)
                data.Lines.Add(new ReceiptLine
                {
                    StockCode = item.Barcode,
                    Name = item.Name,
                    Qty = item.Qty,
                    UOM = item.UOMName,
                    UnitPrice = item.Price,
                    DiscountPct = item.DiscountPct,
                    LineTotal = item.Total
                });
            return data;
        }


        private string Fmt(decimal v) => $"{_currencySymbol} {v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}";
        private string FmtShort(decimal v) => $"{_currencySymbol} {v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}";



        // ══════════════════════════════════════════════════════════════════════
        //  CUSTOMER SEARCH
        // ══════════════════════════════════════════════════════════════════════
        private async Task SearchCustomersAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) { HideCustomerDropdown(); return; }
            try
            {
                _customerNames.Clear();
                bool loaded = false;
                if (GetOnline())
                {
                    try
                    {
                        var api = new ApiService();
                        string json = await api.GetAsync($"api/companies/search?companyId={_companyId}").ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(json))
                        {
                            var all = JsonSerializer.Deserialize<List<string>>(json,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<string>();
                            _customerNames = all
                                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
                            if (_customerNames.Count > 0) { BindCustomerDropdown(); loaded = true; }
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine("API Customer: " + ex.Message); }
                }
                if (!loaded)
                    await SearchCustomersFromSQLite(query).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("SearchCustomersAsync: " + ex.Message); HideCustomerDropdown(); }
        }

        private async Task SearchCustomersFromSQLite(string query)
        {
            if (!System.IO.File.Exists(_dbPath)) { HideCustomerDropdown(); return; }
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    const string sql = @"
                        SELECT DISTINCT CustomerName FROM CustomerMaster
                        WHERE CustomerName LIKE @q AND Status = 1
                        ORDER BY CustomerName LIMIT 50;";
                    using var cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@q", "%" + query + "%");
                    using var r = cmd.ExecuteReader();
                    _customerNames.Clear();
                    while (r.Read())
                    {
                        string name = r["CustomerName"]?.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(name)) _customerNames.Add(name);
                    }
                }).ConfigureAwait(false);
                if (_customerNames.Count > 0) BindCustomerDropdown(); else HideCustomerDropdown();
            }
            catch (Exception ex) { Debug.WriteLine("SQLite Customer: " + ex.Message); HideCustomerDropdown(); }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  UPI / DIGITAL PAYMENT PANEL
        //  In pending-invoice mode: shows Bank Transfer dialog only.
        //  In normal mode: shows full digital payment dialog.
        // ══════════════════════════════════════════════════════════════════════
        private void panelSplitUpi_Click(object sender, EventArgs e)
        {
            SetActiveSplit("upi");
            decimal grand = GrandTotal();
            decimal others = _splitCash + _splitCard;
            decimal upiAmt = _splitUpi > 0 ? _splitUpi : Math.Max(0, grand - others);
            if (upiAmt <= 0) upiAmt = grand;

            ShowBankTransferOnlyDialog(upiAmt);
            UpdateSplitDisplay();
        }

        // ── Bank Transfer dialog (pending invoice mode) ────────────────────────
        // ── Bank Transfer dialog (pending invoice mode) ────────────────────────
        private async void ShowBankTransferOnlyDialog(decimal amount)
        {
            if (_bankAccounts == null || _bankAccounts.Count == 0)
                _bankAccounts = await SalesOrderApi.GetAllBanksAsync(_companyId).ConfigureAwait(true);

            string invoice = lblInvoiceNo.Text;

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(420, 340),
                ShowInTaskbar = false,
                KeyPreview = true
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            // ── Header ───────────────────────────────────────────────────────
            var pnlHead = new Panel { BackColor = Color.FromArgb(42, 46, 58), Size = new Size(420, 50), Location = Point.Empty };
            pnlHead.Controls.Add(new Label
            {
                Text = "🏦  Bank Transfer",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(370, 50),
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
                Size = new Size(44, 50),
                Location = new Point(376, 0),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (s, ev) => dlg.Close();
            pnlHead.Controls.Add(btnX);
            dlg.Controls.Add(pnlHead);

            // ── Amount ───────────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = Fmt(amount),
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(420, 54),
                Location = new Point(0, 54),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Bank selector ────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "Select Bank",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 118)
            });

            var cmbBank = new ComboBox
            {
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(380, 32),
                Location = new Point(20, 140)
            };

            if (_bankAccounts.Count == 0)
            {
                cmbBank.Items.Add("No bank accounts configured");
                cmbBank.Enabled = false;
            }
            else
            {
                foreach (var b in _bankAccounts) cmbBank.Items.Add(b.BankName);
            }
            dlg.Controls.Add(cmbBank);

            var lblDetails = new Label
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 90),
                Location = new Point(20, 180),
                TextAlign = ContentAlignment.TopLeft
            };
            dlg.Controls.Add(lblDetails);

            void RefreshDetails()
            {
                if (_bankAccounts.Count == 0 || cmbBank.SelectedIndex < 0 || cmbBank.SelectedIndex >= _bankAccounts.Count)
                {
                    lblDetails.Text = "";
                    _selectedBankAccount = null;
                    return;
                }
                var b = _bankAccounts[cmbBank.SelectedIndex];
                _selectedBankAccount = b;
                lblDetails.Text =
                    $"Account No: {b.AccountNumber}\n" +
                    $"Branch:     {b.Branch}\n" +
                    $"Ref:        {invoice}   Amt: {Fmt(amount)}";
            }
            cmbBank.SelectedIndexChanged += (s, ev) => RefreshDetails();
            if (_bankAccounts.Count > 0) cmbBank.SelectedIndex = 0;

            // ── Buttons ──────────────────────────────────────────────────────
            var btnConfirm = new Button
            {
                Text = "✓  Confirm Bank Transfer",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(240, 40),
                Location = new Point(20, 284),
                Cursor = Cursors.Hand
            };
            btnConfirm.FlatAppearance.BorderSize = 0;
            btnConfirm.Click += (s, ev) =>
            {
                if (_bankAccounts.Count > 0 && _selectedBankAccount == null)
                {
                    ShowStatus("⛔ Please select a bank account.", false);
                    return;
                }
                _splitUpi = amount;
                _selectedUpiMethodName = _selectedBankAccount?.BankName ?? "Bank Transfer";
                UpdateSplitDisplay();
                dlg.Close();
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(130, 40),
                Location = new Point(272, 284),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, ev) => dlg.Close();

            dlg.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Escape) dlg.Close();
                if (ev.KeyCode == Keys.Enter) btnConfirm.PerformClick();
            };

            dlg.Controls.AddRange(new Control[] { btnConfirm, btnCancel });
            dlg.ShowDialog(this);
        }

        // ── Full digital payment dialog (normal mode) ──────────────────────────


        // change to 0.10m, 1.00m etc. as needed

        private decimal RoundGrandTotal(decimal value)
        {
            if (_roundingIncrement <= 0) return Math.Round(value, 2);
            decimal rounded = Math.Round(value / _roundingIncrement, 0, MidpointRounding.AwayFromZero) * _roundingIncrement;
            return Math.Round(rounded, 2);
        }

        private decimal GrandTotal()
        {
            decimal gross = _cart.Sum(i => i.Price * i.Qty);
            decimal discAmt = _cart.Sum(i => i.DiscountAmt);
            decimal after = gross - discAmt;
            decimal tax = _taxAlreadyIncluded ? 0m : _cart.Sum(i => i.TaxAmt);
            decimal allocatedCharges = _cart.Sum(i => i.Charges);
            decimal fixedCharges = _charges.Where(c => c.Type == 1).Sum(c => c.Amount);
            decimal raw = after + tax + allocatedCharges + fixedCharges;
            return RoundGrandTotal(raw);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MODERN INPUT STYLING
        // ══════════════════════════════════════════════════════════════════════
        private void ApplyModernStyle(TextBox tb, string placeholder, Color accent, out Panel wrapper)
        {
            Point originalPos = tb.Location;
            Size originalSize = tb.Size;
            var parent = tb.Parent;

            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = InputBg;
            tb.ForeColor = TextMuted;
            tb.Font = new Font("Segoe UI", 10.5f);
            tb.Text = placeholder;
            bool isPlaceholder = true;

            const int padH = 4, padV = 4, iconSpace = 26;
            int wrapX = originalPos.X;
            int wrapY = Math.Max(2, originalPos.Y - padV);
            int wrapW = originalSize.Width + padH * 2;
            int wrapH = originalSize.Height + (originalPos.Y - wrapY) + padV;

            var wp = new Panel
            {
                BackColor = InputBg,
                Location = new Point(wrapX, wrapY),
                Size = new Size(wrapW, wrapH),
                Cursor = Cursors.IBeam,
                Anchor = tb.Anchor
            };
            wp.Region = MakeRoundedRegion(wp.Size, 8);

            tb.Enter += (s, ev) =>
            {
                if (isPlaceholder) { isPlaceholder = false; tb.Text = ""; tb.ForeColor = TextWhite; }
                wp.Invalidate();
            };
            tb.Leave += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                { isPlaceholder = true; tb.Text = placeholder; tb.ForeColor = TextMuted; }
                wp.Invalidate();
            };

            parent.Controls.Remove(tb);
            wp.Controls.Add(tb);
            int tbY = Math.Max(padV, (wrapH - tb.PreferredHeight) / 2);
            tb.Location = new Point(iconSpace + 4, tbY);
            tb.Width = wp.Width - iconSpace - 10;

            var icon = new Label
            {
                Text = placeholder.ToLower().Contains("search") ? "🔍"
                          : placeholder.ToLower().Contains("customer") ? "👤" : "📷",
                Font = new Font("Segoe UI Emoji", 9f),
                ForeColor = accent,
                BackColor = Color.Transparent,
                Size = new Size(iconSpace, wrapH),
                Location = new Point(4, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            icon.Click += (s, ev) => tb.Focus();
            wp.Controls.Add(icon);

            wp.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color bc = tb.Focused ? accent : Color.FromArgb(60, 65, 80);
                float bt = tb.Focused ? 2f : 1f;
                using (var pen = new Pen(bc, bt))
                using (var path = RoundedPath(new Rectangle(1, 1, wp.Width - 3, wp.Height - 3), 8))
                    pe.Graphics.DrawPath(pen, path);
            };

            parent.Controls.Add(wp);
            wp.BringToFront();
            wrapper = wp;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CUSTOMER DROPDOWN
        // ══════════════════════════════════════════════════════════════════════
        private void BuildCustomerDropdown()
        {
            _customerDropdown = new ListBox
            {
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(36, 40, 52),
                BorderStyle = BorderStyle.None,
                Visible = false,
                IntegralHeight = false
            };
            _customerDropdown.Click += (s, e) => SelectCustomer();
            _customerDropdown.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) SelectCustomer(); };
            this.Controls.Add(_customerDropdown);
            _customerDropdown.BringToFront();
        }

        private void RepositionDropdown()
        {
            if (_customerDropdown == null || _customerDropdown.IsDisposed) return;
            if (_customerWrapper == null || _customerWrapper.IsDisposed) return;
            if (this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                Point screenPt = _customerWrapper.PointToScreen(new Point(0, _customerWrapper.Height + 2));
                Point formPt = this.PointToClient(screenPt);
                _customerDropdown.Location = formPt;
                _customerDropdown.Width = _customerWrapper.Width;
                if (_customerDropdown.Height > 0)
                    _customerDropdown.Region = MakeRoundedRegion(
                        new Size(_customerDropdown.Width, _customerDropdown.Height), 8);
            }
            catch (ObjectDisposedException)
            {
                // Controls were torn down mid-resize (e.g. right before close) — safe to ignore.
            }
        }
        private void RepositionSearchResults()
        {
            if (_searchWrapper == null || _searchWrapper.IsDisposed) return;
            if (listSearchResults == null || listSearchResults.IsDisposed) return;
            if (this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                Point screenPt = _searchWrapper.PointToScreen(new Point(0, _searchWrapper.Height + 2));
                Point formPt = this.PointToClient(screenPt);

                int w = _searchWrapper.Width;
                int x = Math.Max(0, formPt.X);

                if (x + w > this.ClientSize.Width - 4)
                    w = this.ClientSize.Width - 4 - x;

                listSearchResults.Location = new Point(x, formPt.Y);
                listSearchResults.Width = w;
            }
            catch (ObjectDisposedException) { }
        }

        private async void TxtCustomer_TextChanged(object sender, EventArgs e)
        {
            if (_isSelecting) return;
            string q = GetRealText(txtCustomer);
            if (q.Length >= 2) await SearchCustomersAsync(q);
            else HideCustomerDropdown();
        }

        private void TxtCustomer_KeyDown(object sender, KeyEventArgs e)
        {
            if (_customerDropdown == null || !_customerDropdown.Visible) return;
            if (e.KeyCode == Keys.Down)
            {
                _customerDropdown.Focus();
                if (_customerDropdown.Items.Count > 0) _customerDropdown.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape) { HideCustomerDropdown(); e.Handled = true; }
        }

        private void BindCustomerDropdown()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(BindCustomerDropdown)); return; }
            _customerDropdown.Items.Clear();
            foreach (var name in _customerNames) _customerDropdown.Items.Add(name);
            int dropH = Math.Min(_customerNames.Count * 24, 180);
            _customerDropdown.Height = dropH;
            RepositionDropdown();
            _customerDropdown.Region = MakeRoundedRegion(new Size(_customerDropdown.Width, dropH), 8);
            _customerDropdown.Visible = true;
            _customerDropdown.BringToFront();
        }

        private void SelectCustomer()
        {
            if (_customerDropdown == null || _customerDropdown.SelectedIndex < 0) return;
            _isSelecting = true;
            txtCustomer.Text = _customerDropdown.SelectedItem.ToString();
            txtCustomer.ForeColor = TextWhite;
            HideCustomerDropdown();
            _isSelecting = false;
            txtCustomer.Focus();
            txtCustomer.SelectionStart = txtCustomer.Text.Length;
        }

        private void HideCustomerDropdown()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(HideCustomerDropdown)); return; }
            if (_customerDropdown != null) _customerDropdown.Visible = false;
        }

        private string GetRealText(TextBox tb)
        {
            string t = tb.Text.Trim();
            if (t.Contains("…") || t.Contains("🔍") || t.Contains("📷") || t.Contains("👤")) return "";
            return t;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  AUTOCOMPLETE / HOT ITEMS / RECENT SALES
        // ══════════════════════════════════════════════════════════════════════
        private void BuildAutocomplete()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(BuildAutocomplete)); return; }
            try
            {
                // DISABLE native Windows autocomplete — it creates the ugly black dropdown
                // We use our custom listSearchResults ListBox instead
                txtSearch.AutoCompleteMode = AutoCompleteMode.None;
                txtSearch.AutoCompleteSource = AutoCompleteSource.None;
                txtSearch.AutoCompleteCustomSource = null;
            }
            catch (Exception ex) { Debug.WriteLine("AutoComplete: " + ex.Message); }
        }


        // ══════════════════════════════════════════════════════════════════════
        private void BuildHotItems()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(BuildHotItems)); return; }
            if (_catalog == null || _catalog.Count == 0) return;

            panelHotItems.SuspendLayout();

            // Dispose all existing controls
            var oldControls = panelHotItems.Controls.Cast<Control>().ToList();
            panelHotItems.Controls.Clear();
            foreach (var c in oldControls) c.Dispose();

            panelHotItems.AutoScroll = true;
            panelHotItems.Padding = new Padding(4, 4, 4, 4);
            panelHotItems.BackColor = Color.FromArgb(22, 24, 30);

            // Dispose old tooltip
            _hotItemsTooltip?.Dispose();
            _hotItemsTooltip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // Sort: top sellers first, then alphabetical
            var sorted = _catalog
     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
     .Select(g => g.First())
     .Where(p =>
     {
         var (onHand, reserved) = GetStoreStock(p.ItemId);
         return Math.Max(0m, onHand - reserved) > 0m;   // ← only show items that actually have stock
     })
     .OrderByDescending(p =>
         _salesFrequency.TryGetValue(p.Name, out int freq) ? freq : 0)
     .ThenBy(p => p.Name)
     .Take(10)
     .ToList();

            // ── Layout constants ──────────────────────────────────────────────
            const int COLS = 4;
            const int BTN_H = 44;   // compact height (was 58)
            const int GAP_X = 3;
            const int GAP_Y = 3;
            const int PAD = 4;    // panel padding

            int availW = panelHotItems.ClientSize.Width - PAD * 2 - SystemInformation.VerticalScrollBarWidth;
            int btnW = Math.Max(50, (availW - (COLS - 1) * GAP_X) / COLS);

            int col = 0, row = 0;

            foreach (var (p, i) in sorted.Select((p, i) => (p, i)))
            {
                bool isTopSeller = _salesFrequency.TryGetValue(p.Name, out int cnt) && cnt > 0;

                // Truncate name tightly for compact card
                string name = p.Name.Length > 22 ? p.Name[..20] + "…" : p.Name;

                // ── Card panel (replaces the raw Button) ──────────────────────
                Color accent = CardColorByIndex(i);
                Color accentD = ControlPaint.Dark(accent, 0.18f);   // darker bottom strip

                int x = PAD + col * (btnW + GAP_X);
                int y = PAD + row * (BTN_H + GAP_Y);

                var card = new Panel
                {
                    Size = new Size(btnW, BTN_H),
                    Location = new Point(x, y),
                    BackColor = Color.FromArgb(32, 35, 44),   // neutral base
                    Cursor = Cursors.Hand,
                    Tag = p
                };
                card.Region = MakeRoundedRegion(card.Size, 6);

                // ── Coloured left accent bar (4 px wide) ─────────────────────
                var bar = new Panel
                {
                    Size = new Size(4, BTN_H),
                    Location = Point.Empty,
                    BackColor = accent
                };
                // round only left corners
                using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int r = 6;
                    gp.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    gp.AddLine(r, 0, 4, 0);
                    gp.AddLine(4, 0, 4, BTN_H);
                    gp.AddLine(4, BTN_H, r, BTN_H);
                    gp.AddArc(0, BTN_H - r * 2, r * 2, r * 2, 90, 90);
                    gp.CloseFigure();
                    bar.Region = new Region(gp);
                }

                // ── Name label ────────────────────────────────────────────────
                string nameDisplay = isTopSeller ? "⭐ " + name : name;
                var lblName = new Label
                {
                    Text = nameDisplay,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(220, 228, 245),
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(btnW - 8, 22),
                    Location = new Point(8, 3),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand,
                    // Clip long text with ellipsis
                    AutoEllipsis = true
                };

                // ── Price label ───────────────────────────────────────────────
                var lblPrice = new Label
                {
                    Text = FmtShort(p.Price),
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    ForeColor = accent,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(btnW - 8, 16),
                    Location = new Point(8, 24),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand
                };

                card.Controls.Add(bar);
                card.Controls.Add(lblName);
                card.Controls.Add(lblPrice);

                // ── Hover effect ──────────────────────────────────────────────
                void SetHover(bool on)
                {
                    card.BackColor = on ? Color.FromArgb(44, 48, 62) : Color.FromArgb(32, 35, 44);
                    bar.BackColor = on ? ControlPaint.Light(accent, 0.1f) : accent;
                }

                card.MouseEnter += (s, e) => SetHover(true);
                card.MouseLeave += (s, e) => SetHover(false);
                lblName.MouseEnter += (s, e) => SetHover(true);
                lblName.MouseLeave += (s, e) => SetHover(false);
                lblPrice.MouseEnter += (s, e) => SetHover(true);
                lblPrice.MouseLeave += (s, e) => SetHover(false);

                // ── Tooltip ───────────────────────────────────────────────────
                string stockInfo = "";
                try
                {
                    var (ssOnHand, ssReserved) = GetStoreStock(p.ItemId);
                    decimal ssRemaining = Math.Max(0m, ssOnHand - ssReserved);
                    if (ssOnHand > 0 || ssReserved > 0)
                    {
                        string icon = ssRemaining > 5 ? "✅" : ssRemaining > 0 ? "⚠️" : "❌";
                        stockInfo =
                            $"\n\n📦 On-Hand:   {ssOnHand:F0} units" +
                            $"\n🛒 Reserved:  {ssReserved:F0} units" +
                            $"\n{icon} Available: {ssRemaining:F0} units";
                    }
                }
                catch { }

                string barcodeInfo = !string.IsNullOrWhiteSpace(p.Barcode)
                    ? $"\n🔖 Item ID: {p.Barcode}" : "";

                _hotItemsTooltip.SetToolTip(card, $"{p.Name}{barcodeInfo}{stockInfo}");
                _hotItemsTooltip.SetToolTip(lblName, $"{p.Name}{barcodeInfo}{stockInfo}");
                _hotItemsTooltip.SetToolTip(lblPrice, $"{p.Name}{barcodeInfo}{stockInfo}");

                // ── Click handlers ────────────────────────────────────────────
                EventHandler onClick = (s, e) => HotItemBtn_Click(card, e);
                card.Click += onClick;
                lblName.Click += onClick;
                lblPrice.Click += onClick;
                bar.Click += onClick;

                panelHotItems.Controls.Add(card);

                col++;
                if (col >= COLS) { col = 0; row++; }
            }

            panelHotItems.ResumeLayout(true);
        }
        private void LoadSalesFrequency()
        {
            _salesFrequency.Clear();
            if (!System.IO.File.Exists(_dbPath)) return;
            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();

                using var chk = new SQLiteCommand(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SOInvoiceLine';", conn);
                long exists = (long)(chk.ExecuteScalar() ?? 0L);
                if (exists == 0) return;

                using var cmd = new SQLiteCommand(@"
            SELECT ItemName, SUM(Qty) AS TotalQty
            FROM   SOInvoiceLine
            GROUP  BY ItemName
            ORDER  BY TotalQty DESC
            LIMIT  500;", conn);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    string name = rdr.IsDBNull(0) ? "" : Convert.ToString(rdr.GetValue(0)) ?? "";
                    int qty = rdr.IsDBNull(1) ? 0 : Convert.ToInt32(rdr.GetValue(1));
                    if (!string.IsNullOrWhiteSpace(name))
                        _salesFrequency[name] = qty;
                }
                Debug.WriteLine($"LoadSalesFrequency: {_salesFrequency.Count} items loaded.");
            }
            catch (Exception ex) { Debug.WriteLine("LoadSalesFrequency: " + ex.Message); }
        }


        private static readonly Color[] CardColors =
        {
            Color.FromArgb(37, 99, 235),  Color.FromArgb(16, 185, 129), Color.FromArgb(217, 119,  6),
            Color.FromArgb(124, 58, 237), Color.FromArgb(219,  39, 119), Color.FromArgb(20, 184, 166),
            Color.FromArgb(239, 68,  68), Color.FromArgb(34, 197,   94), Color.FromArgb(251, 146,  60),
            Color.FromArgb(99, 102, 241), Color.FromArgb(236,  72, 153), Color.FromArgb(14, 165, 233)
        };
        private Color CardColorByIndex(int i) => CardColors[i % CardColors.Length];
        //private void HotItemBtn_Click(object sender, EventArgs e) => AddToCart((Product)((Button)sender).Tag, 1);
        private async void HotItemBtn_Click(object sender, EventArgs e)
        {
            Control c = sender as Control;
            Product prod = null;

            while (c != null && prod == null)
            {
                if (c.Tag is Product p) prod = p;
                else c = c.Parent;
            }

            if (prod == null) return;

            if (_isD365Mode && _d365Details.ContainsKey(prod.Barcode))
                ShowProductDetailPopup(prod);
            else if (prod.AvailableUOMs != null && prod.AvailableUOMs.Count > 1)
            {
                var picked = await ShowUomQtyPicker(prod);
                if (picked.HasValue)
                    await AddToCart(prod, picked.Value.Qty, picked.Value.UomId);
            }
            else
                await AddToCart(prod, 1);
        }
        // REPLACE the existing AddToRecentSales() method entirely
        private void AddToRecentSales(string invoice, decimal amount) => _ = AddToRecentSalesAsync(invoice, amount);

        private async Task AddToRecentSalesAsync(string invoice, decimal amount)
        {
            // Update UI instantly — don't wait on disk I/O
            AppendRecentSaleRow(invoice, amount, DateTime.Now);

            // Persist off the UI thread
            await Task.Run(() =>
            {
                try { SalesRepository.SaveRecentSale(invoice, amount, _companyId); }
                catch (Exception ex) { Debug.WriteLine("AddToRecentSales save: " + ex.Message); }
            }).ConfigureAwait(true);
        }

        // NEW method — call once from SalesForm_Load after EnsureSchema
        private void LoadTodayRecentSales()
        {
            try
            {
                var rows = SalesRepository.GetTodayRecentSales(_companyId);
                // rows come back newest-first; add them oldest-first so
                // newest ends up at top after all prepends
                foreach (var (inv, total, date) in rows.AsEnumerable().Reverse())
                    AppendRecentSaleRow(inv, total, date);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadTodayRecentSales: " + ex.Message);
            }
        }

        // NEW helper — builds one row and prepends it so newest is always on top
        private void AppendRecentSaleRow(string invoice, decimal amount, DateTime saleDate)
        {
            if (panelRecentSales == null || panelRecentSales.IsDisposed) return;

            if (panelRecentSales.InvokeRequired)
            {
                panelRecentSales.BeginInvoke(
                    new Action(() => AppendRecentSaleRow(invoice, amount, saleDate)));
                return;
            }

            const int ROW_H = 28;
            const int ROW_GAP = 32;

            // Shift all existing rows down to make room at the top
            int headerOffset = 32; // leave space for any header label inside the panel
            foreach (Control c in panelRecentSales.Controls)
            {
                if (c is Panel) c.Location = new Point(c.Left, c.Top + ROW_GAP);
            }

            var rp = new Panel
            {
                Size = new Size(panelRecentSales.Width - 12, ROW_H),
                Location = new Point(4, headerOffset),
                BackColor = Color.FromArgb(42, 46, 56),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            rp.Controls.Add(new Label
            {
                Text = invoice,
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(130, ROW_H),
                Location = new Point(6, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            rp.Controls.Add(new Label
            {
                Text = saleDate.ToString("HH:mm"),
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(40, ROW_H),
                Location = new Point(138, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            rp.Controls.Add(new Label
            {
                Text = Fmt(amount),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(100, ROW_H),
                Location = new Point(180, 0),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            });

            panelRecentSales.Controls.Add(rp);
            panelRecentSales.ScrollControlIntoView(rp);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SEARCH
        // ══════════════════════════════════════════════════════════════════════
        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            if (_searchDebounce == null)
            {
                _searchDebounce = new System.Windows.Forms.Timer { Interval = 180 };
                _searchDebounce.Tick += (s, ev) =>
                {
                    _searchDebounce.Stop();
                    RunProductSearch(GetRealText(txtSearch));
                };
            }
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }
        private void RunProductSearch(string raw)
        {
            listSearchResults.Items.Clear();

            if (string.IsNullOrEmpty(raw))
            {
                listSearchResults.Visible = false;
                return;
            }

            string q = raw.ToLower();

            // Match on Name OR Barcode/ItemID
            var matches = _catalog
                .Where(p => p.Name.ToLower().Contains(q)
                         || (!string.IsNullOrWhiteSpace(p.Barcode)
                             && p.Barcode.ToLower().Contains(q)))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(50)
                .ToList();

            if (matches.Count == 0)
            {
                listSearchResults.Visible = false;
                return;
            }

            // Batch all StoreStock reads in ONE SQLite connection
            // Batch stock lookups from the live in-memory cache (same source used for
            // AddToCart / GetLiveAvailableStockAsync) instead of the stale legacy
            // StoreStock SQLite table, which is no longer kept in sync.
            var stockMap = new Dictionary<string, (decimal onH, decimal rem)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var p in matches)
            {
                if (string.IsNullOrWhiteSpace(p.Barcode)) continue;
                var (onHand, reserved) = GetStoreStock(p.ItemId);
                stockMap[p.Barcode] = (onHand, Math.Max(0m, onHand - reserved));
            }

            // Build display strings — no ItemID suffix to keep rows narrow
            foreach (var x in matches)
            {
                string stockLabel = "";
                if (!string.IsNullOrWhiteSpace(x.Barcode)
                    && stockMap.TryGetValue(x.Barcode, out var st))
                {
                    string icon = st.rem > 5 ? "✅" : st.rem > 0 ? "⚠" : "❌";
                    stockLabel = $"  {icon} {st.rem:F0}";
                }

                listSearchResults.Items.Add(x.Name + " — " + Fmt(x.Price) + stockLabel);
            }

            // Width matches the search wrapper exactly — never wider
            int w = _searchWrapper?.Width ?? 460;
            int rows = Math.Min(matches.Count, 8);

            listSearchResults.Height = rows * listSearchResults.ItemHeight + 6;
            listSearchResults.Width = w;
            listSearchResults.Region = MakeRoundedRegion(
                new Size(listSearchResults.Width, listSearchResults.Height), 10);

            RepositionSearchResults();
            listSearchResults.Visible = true;
            listSearchResults.BringToFront();
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && listSearchResults.Visible && listSearchResults.Items.Count > 0)
            { listSearchResults.Focus(); listSearchResults.SelectedIndex = 0; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter)
            { AddProductByName(GetRealText(txtSearch)); e.Handled = true; e.SuppressKeyPress = true; }
        }

        private void listSearchResults_KeyDown(object sender, KeyEventArgs e)
        { if (e.KeyCode == Keys.Enter && listSearchResults.SelectedIndex >= 0) SelectSearchResult(); }
        private void listSearchResults_DoubleClick(object sender, EventArgs e) => SelectSearchResult();
        private void listSearchResults_Click(object sender, EventArgs e) => SelectSearchResult();

        private void SelectSearchResult()
        {
            if (listSearchResults.SelectedIndex < 0) return;
            string raw = listSearchResults.SelectedItem.ToString();
            // The item format is:  "Name — P xx.xx  ✅ 12"
            // We only want the Name part (everything before " — ")
            string name = raw.Split(new[] { " — " }, StringSplitOptions.None)[0].Trim();
            AddProductByName(name);
        }

        private async void AddProductByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var prod = _catalog.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? _catalog.FirstOrDefault(p => p.Name.ToLower().Contains(name.ToLower()));

            if (prod == null) { ShowStatus("Product not found: " + name, false); return; }

            txtSearch.ForeColor = TextMuted;
            listSearchResults.Visible = false;
            listSearchResults.Items.Clear();

            if (_isD365Mode && _d365Details.ContainsKey(prod.Barcode))
                ShowProductDetailPopup(prod);
            else if (prod.AvailableUOMs != null && prod.AvailableUOMs.Count > 1)
            {
                var picked = await ShowUomQtyPicker(prod);
                if (picked.HasValue)
                    await AddToCart(prod, picked.Value.Qty, picked.Value.UomId);
            }
            else
                await AddToCart(prod, 1);

            this.ActiveControl = null;
        }
        private async Task<bool> ShowPriceGroupAuthDialogAsync(Form owner, string newGroup, decimal newPrice)
        {
            bool result = false;

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(420, 310),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            // ── Header ─────────────────────────────────────────────────────────────
            var pnlHead = new Panel
            {
                BackColor = Color.FromArgb(42, 46, 58),
                Size = new Size(420, 50),
                Location = Point.Empty
            };
            pnlHead.Controls.Add(new Label
            {
                Text = "🔒  Supervisor Authorisation Required",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(420, 50),
                Location = new Point(10, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            dlg.Controls.Add(pnlHead);

            // ── Info line ─────────────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = $"Changing price group to:  \"{newGroup}\"  →  {Fmt(newPrice)}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 24),
                Location = new Point(20, 60),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // ── Username ──────────────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "Username",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 96)
            });
            var txtUser = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 116)
            };
            dlg.Controls.Add(txtUser);

            // ── Password ──────────────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "Password",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 156)
            });
            var txtPass = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 176),
                UseSystemPasswordChar = true
            };
            dlg.Controls.Add(txtPass);

            // ── Status label ──────────────────────────────────────────────────────
            var lblStatus2 = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 20),
                Location = new Point(20, 214)
            };
            dlg.Controls.Add(lblStatus2);

            // ── Authorise button ──────────────────────────────────────────────────
            var btnAuth = new Button
            {
                Text = "✓  Authorise",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(200, 40),
                Location = new Point(20, 244),
                Cursor = Cursors.Hand
            };
            btnAuth.FlatAppearance.BorderSize = 0;

            // ── Cancel button ─────────────────────────────────────────────────────
            var btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(170, 40),
                Location = new Point(230, 244),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, ev) => dlg.Close();

            // ── Auth logic — identical to ShowDeleteAuthDialog ────────────────────
            async void DoAuth()
            {
                string username = txtUser.Text.Trim();
                string password = txtPass.Text;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    lblStatus2.Text = "Username and password are required.";
                    lblStatus2.ForeColor = AccRed;
                    return;
                }

                btnAuth.Enabled = false;
                lblStatus2.ForeColor = TextMuted;
                lblStatus2.Text = "Verifying…";

                try
                {
                    bool authorized = false;
                    bool canChange = false;
                    string role = "";

                    if (GetOnline())
                    {
                        try
                        {
                            var api = new ApiService();
                            string json = await api.GetAsync("api/POSPermission/authorize-price-override?username=" + username + "&password=" + password)
                                                   .ConfigureAwait(true);
                            if (!string.IsNullOrEmpty(json))
                            {
                                var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                                var res = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<bool>>(json, opts);
                                if (res?.IsSuccess == true && res.Data)
                                {
                                    authorized = true;
                                    canChange = true;
                                    role = "Supervisor";
                                }
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine("PriceGroupAuth API: " + ex.Message); }
                    }
                    else
                    {
                        var res = await VerifyRoleFromSQLite(username, password).ConfigureAwait(true);
                        authorized = res.authorized;
                        role = res.role;
                        canChange = res.authorized;
                    }

                    if (!authorized)
                    {
                        lblStatus2.ForeColor = AccRed;
                        lblStatus2.Text = "⛔  Invalid username or password.";
                        return;
                    }
                    if (!canChange)
                    {
                        lblStatus2.ForeColor = AccRed;
                        lblStatus2.Text = $"⛔  Role '{role}' cannot change price groups.";
                        return;
                    }

                    lblStatus2.ForeColor = TextGreen;
                    lblStatus2.Text = $"✓  Authorised{(!string.IsNullOrEmpty(role) ? $" as {role}" : "")}";

                    await Task.Delay(450);
                    result = true;           // signal success to the caller
                    dlg.Close();
                }
                catch (Exception ex)
                {
                    lblStatus2.ForeColor = AccRed;
                    lblStatus2.Text = "Error: " + ex.Message;
                }
                finally { btnAuth.Enabled = true; }
            }

            btnAuth.Click += (s, ev) => DoAuth();
            dlg.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter) { ev.Handled = true; DoAuth(); }
                if (ev.KeyCode == Keys.Escape) { ev.Handled = true; dlg.Close(); }
            };

            dlg.Controls.AddRange(new Control[] { btnAuth, btnCancel });
            dlg.Shown += (s, ev) => txtUser.Focus();
            dlg.ShowDialog(owner);     // blocks until closed

            return result;
        }
        private void ShowProductDetailPopup(Product prod)
        {
            // ── Resolve D365 detail records for this product ──────────────────────
            _d365Details.TryGetValue(prod.Barcode, out var details);

            // If no D365 detail rows, fall back to plain cart-add
            if (details == null || details.Count == 0)
            {
                AddToCart(prod, 1);
                return;
            }

            // ── Resolve default group BEFORE anything else ────────────────────────
            // True "(default)" = blank AccountRelation; fallback = first row
            //var defaultGroupDetail = details.FirstOrDefault(d =>
            //    string.IsNullOrWhiteSpace(d.AccountRelation)) ?? details[0];
            var defaultGroupDetail = details.FirstOrDefault(d =>
    d.AccountRelation == "A") ?? details[0];

            // Working copy — changes as the user switches price group
            D365ProductDetail cur = defaultGroupDetail;

            // ── Popup form ────────────────────────────────────────────────────────
            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(500, 490),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 14);

            // ── Header bar ────────────────────────────────────────────────────────
            var pnlHead = new Panel
            {
                BackColor = Color.FromArgb(42, 46, 58),
                Size = new Size(500, 54),
                Location = Point.Empty
            };
            pnlHead.Controls.Add(new Label
            {
                Text = "📦  Product Detail",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(440, 54),
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(44, 54),
                Location = new Point(456, 0),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (s, ev) => dlg.Close();
            pnlHead.Controls.Add(btnX);
            dlg.Controls.Add(pnlHead);

            // ── Product name + item ID banner ─────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = cur.NameAlias,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 32),
                Location = new Point(20, 62),
                TextAlign = ContentAlignment.MiddleLeft
            });
            dlg.Controls.Add(new Label
            {
                Text = $"Item ID:  {cur.ItemId}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 94)
            });

            // ── Separator ─────────────────────────────────────────────────────────
            dlg.Controls.Add(new Panel
            {
                BackColor = Color.FromArgb(50, 54, 66),
                Size = new Size(460, 1),
                Location = new Point(20, 116)
            });

            // ── Helper: info row ──────────────────────────────────────────────────
            int fieldY = 126;
            Label AddInfoRow(string icon, string caption, string value, Color valColor)
            {
                dlg.Controls.Add(new Label
                {
                    Text = icon + "  " + caption,
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(200, 28),
                    Location = new Point(20, fieldY),
                    TextAlign = ContentAlignment.MiddleLeft
                });
                var lv = new Label
                {
                    Text = value,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = valColor,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(240, 28),
                    Location = new Point(230, fieldY),
                    TextAlign = ContentAlignment.MiddleRight
                };
                dlg.Controls.Add(lv);
                fieldY += 32;
                return lv;
            }

            // Info rows use cur which is already set to the default group
            var lvAvail = AddInfoRow("📦", "Available (Physical)", $"{cur.AvailPhysical:F0} units", cur.AvailPhysical > 0 ? TextGreen : AccRed);
            var lvSite = AddInfoRow("🏭", "Inventory Site", cur.InventSiteId, AccBlue);
            var lvLocation = AddInfoRow("📍", "Inventory Location", cur.InventLocationId, AccCyan);
            var lvWMS = AddInfoRow("🗂️", "WMS Location", cur.WMSLocationId, TextMuted);
            var lvAmount = AddInfoRow("💰", "Unit Price", Fmt(cur.Amount), TextGreen);

            // ── Separator ─────────────────────────────────────────────────────────
            dlg.Controls.Add(new Panel
            {
                BackColor = Color.FromArgb(50, 54, 66),
                Size = new Size(460, 1),
                Location = new Point(20, fieldY + 4)
            });
            fieldY += 14;

            // ── Price Group section label ─────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "PRICE GROUP  (AccountRelation)",
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 90, 110),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, fieldY)
            });
            fieldY += 20;

            // ── Price Group dropdown ───────────────────────────────────────────────
            var cmbPriceGroup = new ComboBox
            {
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(460, 30),
                Location = new Point(20, fieldY)
            };
            fieldY += 38;

            // Populate — "(default)" always first, then alphabetical
            var groups = details
                .Select(d => string.IsNullOrWhiteSpace(d.AccountRelation) ? "(default)" : d.AccountRelation)
                .Distinct()
                .OrderBy(g => g == "(default)" ? "\0" : g)   // \0 sorts before any letter
                .ToList();

            foreach (var g in groups) cmbPriceGroup.Items.Add(g);

            // Pre-select the group that matches the resolved default detail row
            string defaultGroupName = string.IsNullOrWhiteSpace(defaultGroupDetail.AccountRelation)
                ? "(default)"
                : defaultGroupDetail.AccountRelation;

            int defaultIdx = groups.IndexOf(defaultGroupName);
            cmbPriceGroup.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;

            dlg.Controls.Add(cmbPriceGroup);

            // ── Auth-status badge ─────────────────────────────────────────────────
            var lblAuthBadge = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 20),
                Location = new Point(20, fieldY),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            dlg.Controls.Add(lblAuthBadge);
            fieldY += 22;

            // ── Guard state ───────────────────────────────────────────────────────
            bool _authInProgress = false;
            int _lastGoodIndex = cmbPriceGroup.SelectedIndex;   // default group index

            // ── Price group change — requires supervisor auth ──────────────────────
            cmbPriceGroup.SelectedIndexChanged += async (s, ev) =>
            {
                if (_authInProgress) return;

                int newIndex = cmbPriceGroup.SelectedIndex;
                if (newIndex == _lastGoodIndex) return;

                string newGroup = cmbPriceGroup.SelectedItem?.ToString() ?? "";
                var targetDetail = details.FirstOrDefault(d =>
                    (string.IsNullOrWhiteSpace(d.AccountRelation) ? "(default)" : d.AccountRelation)
                    .Equals(newGroup, StringComparison.OrdinalIgnoreCase));

                if (targetDetail == null) return;

                // Require auth to change price group
                bool authorized = await ShowPriceGroupAuthDialogAsync(dlg, newGroup, targetDetail.Amount);

                if (!authorized)
                {
                    _authInProgress = true;
                    cmbPriceGroup.SelectedIndex = _lastGoodIndex;
                    _authInProgress = false;

                    lblAuthBadge.Text = "⛔  Authorisation failed — price group unchanged.";
                    lblAuthBadge.ForeColor = AccRed;
                    lblAuthBadge.Visible = true;
                    return;
                }

                // Auth passed — apply new detail row
                _lastGoodIndex = newIndex;
                cur = targetDetail;

                lvAvail.Text = $"{cur.AvailPhysical:F0} units";
                lvAvail.ForeColor = cur.AvailPhysical > 0 ? TextGreen : AccRed;
                lvSite.Text = cur.InventSiteId;
                lvLocation.Text = cur.InventLocationId;
                lvWMS.Text = cur.WMSLocationId;
                lvAmount.Text = Fmt(cur.Amount);

                lblAuthBadge.Text = $"✓  Authorised — Price Group set to '{newGroup}'  ({Fmt(cur.Amount)})";
                lblAuthBadge.ForeColor = TextGreen;
                lblAuthBadge.Visible = true;
            };

            // ── Separator ─────────────────────────────────────────────────────────
            dlg.Controls.Add(new Panel
            {
                BackColor = Color.FromArgb(50, 54, 66),
                Size = new Size(460, 1),
                Location = new Point(20, fieldY)
            });
            fieldY += 10;

            // ── Quantity row ──────────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "Quantity",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(200, 30),
                Location = new Point(20, fieldY),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var nudQty = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 9999,
                Value = 1,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                Size = new Size(100, 30),
                Location = new Point(380, fieldY),
                TextAlign = System.Windows.Forms.HorizontalAlignment.Right
            };
            ((System.ComponentModel.ISupportInitialize)nudQty).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudQty).EndInit();
            dlg.Controls.Add(nudQty);
            fieldY += 40;

            // ── Buttons ───────────────────────────────────────────────────────────
            var btnAdd = new Button
            {
                Text = "✓  Add to Cart",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 42),
                Location = new Point(20, fieldY),
                Cursor = Cursors.Hand
            };
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Region = MakeRoundedRegion(btnAdd.Size, 8);

            var btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(218, 42),
                Location = new Point(252, fieldY),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Region = MakeRoundedRegion(btnCancel.Size, 8);
            btnCancel.Click += (s, ev) => dlg.Close();
            btnAdd.Click += async (s, ev) =>
            {
                int qty = (int)nudQty.Value;

                var selectedProd = new Product
                {
                    Name = cur.NameAlias,
                    ItemId = prod.ItemId,
                    Price = cur.Amount,
                    Barcode = cur.ItemId,
                    Category = cur.InventSiteId,
                    UOM = prod.UOM,                       // ← preserve real UOM
                    AvailableUOMs = prod.AvailableUOMs     // ← preserve pack sizes (with correct UnitsPerPack)
                };

                dlg.Close();
                await AddToCart(selectedProd, qty, prod.UOM);   // ← pass uomId explicitly so it isn't re-resolved to base
            };

            dlg.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter) { ev.Handled = true; btnAdd.PerformClick(); }
                if (ev.KeyCode == Keys.Escape) { ev.Handled = true; dlg.Close(); }
            };

            dlg.Controls.AddRange(new Control[] { btnAdd, btnCancel });

            // Resize form to fit all controls
            dlg.ClientSize = new Size(500, fieldY + 60);
            dlg.Region = MakeRoundedRegion(dlg.ClientSize, 14);

            dlg.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  BARCODE
        // ══════════════════════════════════════════════════════════════════════
        private void TxtBarcode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                string code = GetRealText(txtBarcode);
                if (code.Length > 0)
                {
                    ProcessBarcode(code);
                    txtBarcode.ForeColor = TextMuted;
                    txtBarcode.Text = "📷  Scan barcode…";
                }
                e.Handled = true; e.SuppressKeyPress = true;
            }
        }

        private void TxtBarcode_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsLetterOrDigit(e.KeyChar) && e.KeyChar != '-')
                e.Handled = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (txtSearch.Focused || txtCustomer.Focused || txtBarcode.Focused)
            { base.OnKeyPress(e); return; }

            char c = e.KeyChar;
            if (c == '\r' || c == '\n')
            {
                _barcodeTimer.Stop();
                if (_barcodeBuffer.Length > 0)
                {
                    ProcessBarcode(_barcodeBuffer);
                    txtBarcode.ForeColor = TextMuted;
                    txtBarcode.Text = "📷  Scan barcode…";
                    _barcodeBuffer = "";
                }
                e.Handled = true; return;
            }
            if (char.IsLetterOrDigit(c) || c == '-')
            {
                _barcodeBuffer += c;
                _barcodeTimer.Stop(); _barcodeTimer.Start();
                if (txtBarcode.ForeColor != TextWhite) txtBarcode.ForeColor = TextWhite;
                txtBarcode.Text = _barcodeBuffer;
                e.Handled = true;
            }
            base.OnKeyPress(e);
        }

        private void BarcodeTimer_Tick(object sender, EventArgs e)
        {
            _barcodeTimer.Stop();
            if (_barcodeBuffer.Length > 0)
            {
                ProcessBarcode(_barcodeBuffer);
                _barcodeBuffer = "";
                txtBarcode.ForeColor = TextMuted;
                txtBarcode.Text = "📷  Scan barcode…";
            }
        }

        private async void ProcessBarcode(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            string trimmed = code.TrimStart('0'), padded = code.PadLeft(13, '0');
            if (!_barcodeMap.TryGetValue(code, out var prod))
                if (!_barcodeMap.TryGetValue(padded, out prod))
                    _barcodeMap.TryGetValue(trimmed, out prod);

            if (prod != null) { await AddToCart(prod, 1); ShowStatus("Scanned: " + prod.Name, true); }
            else ShowStatus("Barcode not found: " + code, false);

            this.ActiveControl = null;
        }
        // ── UOMs already used by this item's cart lines — mirrors getUOMOptionsForItem()
        //    in the React PO screen, which excludes UOMs already used on other lines. ──
        private HashSet<int> GetUsedUomsForItem(Product prod)
        {
            return _cart
                .Where(c => c.Name.Equals(prod.Name, StringComparison.OrdinalIgnoreCase)
                         && c.Barcode == prod.Barcode)
                .Select(c => c.UOM)
                .ToHashSet();
        }
        // ── Lets the cashier pick a specific UOM (and qty) when the item has more
        //    than one available. Excludes UOMs already used in the cart for this
        //    item, and caps quantity to live on-hand stock (converted to the
        //    selected UOM's pack size). Returns null if cancelled or nothing left. ──
        private async Task<(int UomId, int Qty)?> ShowUomQtyPicker(Product prod)
        {
            var allUoms = (prod.AvailableUOMs != null && prod.AvailableUOMs.Count > 0)
                ? prod.AvailableUOMs
                : _uomMaster;

            if (allUoms == null || allUoms.Count == 0)
                return (prod.UOM > 0 ? prod.UOM : 1, 1);

            // ── Remove UOMs already used by this item's existing cart lines ──
            var usedUoms = GetUsedUomsForItem(prod);
            var availUoms = allUoms.Where(u => !usedUoms.Contains(u.UomId)).ToList();

            if (availUoms.Count == 0)
            {
                ShowStatus($"⚠ All UOM variants of {prod.Name} are already in the cart.", false);
                return null;
            }

            // ── Live available stock (base units) — same call AddToCart uses ──
            // ── Live available stock (base units) — this call is for adding a NEW
            //    UOM, so every existing cart line for this item should count. ──
            decimal availableBase = await GetLiveAvailableStockAsync(prod.ItemId).ConfigureAwait(true);

            (int UomId, int Qty)? result = null;

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(380, 276),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            var pnlHead = new Panel { BackColor = Color.FromArgb(42, 46, 58), Size = new Size(380, 50), Location = Point.Empty };
            pnlHead.Controls.Add(new Label
            {
                Text = "📦  " + prod.Name,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(316, 50),          // shortened to make room for the close button
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var btnUomClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(44, 50),
                Location = new Point(336, 0),
                Cursor = Cursors.Hand
            };
            btnUomClose.FlatAppearance.BorderSize = 0;
            btnUomClose.Click += (s, ev) => dlg.Close();
            pnlHead.Controls.Add(btnUomClose);
            dlg.Controls.Add(pnlHead);

            dlg.Controls.Add(new Label
            {
                Text = "Select UOM",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 62)
            });

            var cmbUom = new ComboBox
            {
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(340, 32),
                Location = new Point(20, 84)
            };
            foreach (var u in availUoms) cmbUom.Items.Add(u.UomDescription);
            dlg.Controls.Add(cmbUom);

            dlg.Controls.Add(new Label
            {
                Text = "Quantity",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 126)
            });

            var nudQty = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 9999,   // recalculated per UOM below
                Value = 1,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                Size = new Size(340, 32),
                Location = new Point(20, 148)
            };
            dlg.Controls.Add(nudQty);

            var lblStockHint = new Label
            {
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(340, 18),
                Location = new Point(20, 184),
                TextAlign = ContentAlignment.MiddleLeft
            };
            dlg.Controls.Add(lblStockHint);

            var lblReduceHint = new Label
            {
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(340, 18),
                Location = new Point(20, 202),
                TextAlign = ContentAlignment.MiddleLeft
            };
            dlg.Controls.Add(lblReduceHint);

            var btnAdd = new Button
            {
                Text = "✓  Add to Cart",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(340, 38),
                Location = new Point(20, 224),
                Cursor = Cursors.Hand
            };
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Region = MakeRoundedRegion(btnAdd.Size, 8);

            void RefreshReduceHint()
            {
                if (cmbUom.SelectedIndex < 0) return;
                var chosen = availUoms[cmbUom.SelectedIndex];
                decimal unitsPerPack = chosen.UnitsPerPack > 0 ? chosen.UnitsPerPack : 1m;
                decimal qty = nudQty.Value;
                decimal reduceBy = qty * unitsPerPack;

                lblReduceHint.Text = $"📉 Will reduce stock by {reduceBy:F0} unit(s)  ({qty:F0} × {chosen.UomDescription})";
            }
            // ── Recompute max allowed qty whenever the UOM changes — max = floor(available / unitsPerPack) ──
            void RefreshMaxForSelectedUom()
            {
                if (cmbUom.SelectedIndex < 0) return;
                var chosen = availUoms[cmbUom.SelectedIndex];
                decimal unitsPerPack = chosen.UnitsPerPack > 0 ? chosen.UnitsPerPack : 1m;
                decimal maxQty = Math.Floor(availableBase / unitsPerPack);

                if (POSAPP.Printer.StockSettings.AllowOutOfStockSale)
                {
                    nudQty.Maximum = 9999;
                    nudQty.Value = 1;
                    btnAdd.Enabled = true;
                    lblStockHint.Text = $"Available: {availableBase:F0} unit(s) on hand ({maxQty:F0} × {chosen.UomDescription})";
                    lblStockHint.ForeColor = TextMuted;
                    return;
                }

                if (maxQty < 1)
                {
                    nudQty.Maximum = 1;
                    nudQty.Value = 1;
                    nudQty.Enabled = false;
                    btnAdd.Enabled = false;
                    lblStockHint.Text = $"⛔ Out of stock for {chosen.UomDescription}.";
                    lblStockHint.ForeColor = AccRed;
                }
                else
                {
                    nudQty.Enabled = true;
                    nudQty.Maximum = maxQty;
                    if (nudQty.Value > maxQty) nudQty.Value = maxQty;
                    btnAdd.Enabled = true;
                    lblStockHint.Text = $"Max: {maxQty:F0} × {chosen.UomDescription} ({availableBase:F0} unit(s) available)";
                    lblStockHint.ForeColor = TextMuted;
                }
                RefreshReduceHint();
            }

            cmbUom.SelectedIndexChanged += (s, e) => RefreshMaxForSelectedUom();
            nudQty.ValueChanged += (s, e) => RefreshReduceHint();

            // ── NEW: live update while typing, before the value commits ──
            nudQty.TextChanged += (s, e) =>
            {
                if (cmbUom.SelectedIndex < 0) return;   // guard — nothing chosen yet
                if (decimal.TryParse(nudQty.Text, out decimal typed))
                {
                    var chosen = availUoms[cmbUom.SelectedIndex];
                    decimal unitsPerPack = chosen.UnitsPerPack > 0 ? chosen.UnitsPerPack : 1m;
                    lblReduceHint.Text = $"📉 Will reduce stock by {typed * unitsPerPack:F0} unit(s)  ({typed:F0} × {chosen.UomDescription})";
                }
            };
            cmbUom.SelectedIndex = 0; // triggers RefreshMaxForSelectedUom via event

            btnAdd.Click += (s, e) =>
            {
                // Don't trust nudQty.Value blindly — if Enter triggered this via
                // dlg.KeyDown without the control losing focus, Value can be stale
                // while the displayed Text shows an unvalidated number.
                decimal typedQty = decimal.TryParse(nudQty.Text, out decimal parsed) ? parsed : nudQty.Value;

                if (typedQty > nudQty.Maximum) typedQty = nudQty.Maximum;
                if (typedQty < nudQty.Minimum) typedQty = nudQty.Minimum;
                nudQty.Value = typedQty;  // commit clamped value back into the control

                if (typedQty <= 0 || !btnAdd.Enabled) return;

                var chosen = availUoms[cmbUom.SelectedIndex];
                result = (chosen.UomId, (int)typedQty);
                dlg.Close();
            };

            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; if (btnAdd.Enabled) btnAdd.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
            };

            dlg.Controls.Add(btnAdd);
            dlg.Shown += (s, e) => cmbUom.Focus();
            dlg.ShowDialog(this);
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CART
        // ══════════════════════════════════════════════════════════════════════
        private async Task AddToCart(Product prod, int qty, int? uomId = null)
        {
            if (_isPendingInvoiceMode)
            {
                ShowStatus("⛔ Payment only — cannot add items to a pending invoice.", false);
                return;
            }

            bool cartWasEmpty = _cart.Count == 0;   // ← NEW: capture before mutating cart

            var availUoms = (prod.AvailableUOMs != null && prod.AvailableUOMs.Count > 0)
                ? prod.AvailableUOMs
                : _uomMaster;

            int resolvedUom = (uomId.HasValue && availUoms.Any(u => u.UomId == uomId.Value))
                ? uomId.Value
                : (availUoms.Count > 0 ? availUoms[0].UomId : (prod.UOM > 0 ? prod.UOM : 1));

            decimal unitsPerPack = availUoms.FirstOrDefault(u => u.UomId == resolvedUom)?.UnitsPerPack ?? 1m;
            if (unitsPerPack <= 0) unitsPerPack = 1m;

            var ex = _cart.Find(c => c.Name == prod.Name
                                   && c.UOM == resolvedUom
                                   && c.Barcode == prod.Barcode);
            decimal currentQtyInCart = ex?.Qty ?? 0m;
            decimal requestedTotalQty = currentQtyInCart + qty;
            decimal requestedTotalBaseQty = requestedTotalQty * unitsPerPack;
            decimal available = await GetLiveAvailableStockAsync(
                prod.ItemId, excludeCartItemName: prod.Name, excludeUom: resolvedUom).ConfigureAwait(true);

            if (!POSAPP.Printer.StockSettings.AllowOutOfStockSale && requestedTotalBaseQty > available)
            {
                if (available <= 0)
                {
                    ShowStatus($"⛔ {prod.Name} — out of stock.", false);
                    return;
                }

                decimal maxPackQty = Math.Floor(available / unitsPerPack);
                if (maxPackQty <= currentQtyInCart)
                {
                    ShowStatus($"⛔ {prod.Name} — no more stock available for this UOM.", false);
                    return;
                }

                string uomName = availUoms.FirstOrDefault(u => u.UomId == resolvedUom)?.UomDescription ?? "";
                ShowStatus($"⚠ Only {maxPackQty:F0} × {uomName} of {prod.Name} available — added max allowed.", false);
                requestedTotalQty = maxPackQty;
                qty = (int)(maxPackQty - currentQtyInCart);
                if (qty <= 0) return;
            }

            var defaultTax = _taxMaster.FirstOrDefault(t => t.TaxId == prod.SalesTaxID);
            if (ex != null) ex.Qty = requestedTotalQty;
            else _cart.Add(new CartItem
            {
                Name = prod.Name,
                ItemId = prod.ItemId,
                OriginalPrice = prod.Price,
                Price = availUoms.FirstOrDefault(u => u.UomId == resolvedUom)?.RetailPrice > 0
                    ? availUoms.First(u => u.UomId == resolvedUom).RetailPrice
                    : prod.Price,
                Qty = requestedTotalQty,
                DiscountPct = _defaultDiscountPct,
                Barcode = prod.Barcode,
                UOM = resolvedUom,
                UOMName = availUoms.FirstOrDefault(u => u.UomId == resolvedUom)?.UomDescription ?? "",
                AvailableUOMs = availUoms,
                   TaxId = defaultTax?.TaxId ?? 0,
                TaxCode = defaultTax?.TaxCode ?? "",
                TaxPercentage = defaultTax?.TaxPercentage ?? 0m
            });

            RefreshCart();
            UpdateTotals();

            // NEW: warn immediately when the cashier starts a sale with low float
            if (cartWasEmpty && ShiftState.IsOpen && ShiftState.CurrentFloat < 50)
                ShowStatus($"⚠ Float low: {Fmt(ShiftState.CurrentFloat)} — open Float Entry (F8).", false);
        }   // ← end of AddToCart — the float check above must be inside this closing brace

        private void RefreshCart()
        {
            if (panelCartItems == null) return;
            panelCartItems.SuspendLayout();
            panelCartItems.Controls.Clear();
            int y = 0;
            for (int i = 0; i < _cart.Count; i++)
            {
                panelCartItems.Controls.Add(BuildCartRow(_cart[i], y, i % 2 == 0));
                y += 70;
            }
            panelCartItems.ResumeLayout();
        }

        private void BuildGrandTotalBigLabel()
        {
            if (lblGrandTotalBig != null) return; // already built

            lblGrandTotalBig = new Label
            {
                Text = Fmt(GrandTotal()),
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Size = new Size(panelCartItems.Width, 50),
                Location = new Point(0, -54)   // sits just above the cart panel
            };

            // Place it in the same parent as panelCartItems, right above it
            var parent = panelCartItems.Parent;
            parent.Controls.Add(lblGrandTotalBig);
            lblGrandTotalBig.Location = new Point(panelCartItems.Left, panelCartItems.Top - 54);
            lblGrandTotalBig.Width = panelCartItems.Width;
            lblGrandTotalBig.BringToFront();
        }

        private Panel BuildCartRow(CartItem item, int yOffset, bool alt)
        {
            const int ROW_H = 50;
            var row = new Panel
            {
                MinimumSize = new Size(360, ROW_H),
                Size = new Size(Math.Max(360, panelCartItems.ClientSize.Width - 4), ROW_H),
                Location = new Point(2, yOffset),
                BackColor = alt ? PanelDark2 : PanelDark3,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Cursor = Cursors.Hand
                // No Region — avoids GDI handle exhaustion
            };

            var badge = new Label
            {
                Text = (_cart.IndexOf(item) + 1).ToString(),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(50, 55, 70),
                Size = new Size(22, 22),
                Location = new Point(6, (ROW_H - 22) / 2),
                TextAlign = ContentAlignment.MiddleCenter
            };
            badge.Region = MakeRoundedRegion(badge.Size, 5);  // small badge is fine

            string discText = item.DiscountPct > 0 ? $"  {item.DiscountPct:F0}% off" : "";
            var lblName = new Label { Text = item.Name, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = TextWhite, BackColor = Color.Transparent, AutoSize = false, Size = new Size(160, ROW_H), Location = new Point(34, 0), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
            var lblPrice = new Label { Name = "lblCartPrice", Text = $"{_currencySymbol} {item.Price:F2}{discText}", Font = new Font("Segoe UI", 8F), ForeColor = item.DiscountPct > 0 ? AccCyan : TextMuted, BackColor = Color.Transparent, AutoSize = false, Size = new Size(120, ROW_H), Location = new Point(34, 14), TextAlign = ContentAlignment.BottomLeft, Cursor = Cursors.Hand, Tag = "price" };
            var lblTotal = new Label { Name = "lblRowTotal", Text = Fmt(item.Total), Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = TextGreen, BackColor = Color.Transparent, AutoSize = false, Size = new Size(80, ROW_H), TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right, Tag = "total" };

            var btnMinus = MakeSmallBtn("−", Point.Empty, AccRed);
            var lblQty = new Label { Name = "lblQty", Text = item.Qty.ToString(), Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = TextWhite, BackColor = Color.Transparent, Size = new Size(50, 26), TextAlign = ContentAlignment.MiddleCenter, Tag = "qty" };
            var btnPlus = MakeSmallBtn("+", Point.Empty, AccGreen);
            btnMinus.Tag = "minus"; btnPlus.Tag = "plus";
            btnMinus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnPlus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblQty.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var captured = item;
            btnMinus.Click += (s, e) =>
            {
                if (captured.Qty > 1) ShowQtyReduceAuthDialog(captured);
                else ShowDeleteAuthDialog(captured);
            };
            btnPlus.Click += async (s, e) =>
            {
                decimal unitsPerPack = GetUnitsPerPackForCartItem(captured);
                decimal available = await GetLiveAvailableStockAsync(
     captured.ItemId, excludeCartItemName: captured.Name, excludeUom: captured.UOM).ConfigureAwait(true);
                if (!POSAPP.Printer.StockSettings.AllowOutOfStockSale && (captured.Qty + 1) * unitsPerPack > available)
                {
                    decimal maxPackQty = Math.Floor(available / unitsPerPack);
                    ShowStatus($"⛔ Cannot exceed available stock for {captured.Name} (max {maxPackQty:F0} × {captured.UOMName}).", false);
                    return;
                }
                captured.Qty++;
                RefreshCart();
                UpdateTotals();
            };

            EventHandler openPopup = (s, e) => ShowCartItemDialog(captured);
            row.Click += openPopup; badge.Click += openPopup;
            lblName.Click += openPopup; lblPrice.Click += openPopup; lblTotal.Click += openPopup;

            row.Controls.AddRange(new Control[] { badge, lblName, lblPrice, lblTotal, btnMinus, lblQty, btnPlus });
            row.HandleCreated += (s, e) => { if (s is Panel p) LayoutCartRowRight(p); };
            row.Resize += (s, e) => { if (s is Panel p) LayoutCartRowRight(p); };
            return row;
        }

        // REPLACE the entire LayoutCartRowRight method:
        private static void LayoutCartRowRight(Panel row)
        {
            const int ROW_H = 50;
            const int MARGIN = 6;
            const int BTN_W = 24;
            const int BTN_H = 24;
            const int QTY_W = 56;
            const int GAP = 4;
            const int TOTAL_W = 118;
            const int LEFT_TEXT_X = 34;
            const int LEFT_GAP = 8;

            int w = Math.Max(row.Width, row.MinimumSize.Width);
            int btnY = (ROW_H - BTN_H) / 2;

            int xPlus = w - MARGIN - BTN_W;
            int xQty = xPlus - GAP - QTY_W;
            int xMinus = xQty - GAP - BTN_W;
            int xTotal = xMinus - GAP - TOTAL_W;
            int textWidth = Math.Max(80, xTotal - LEFT_TEXT_X - LEFT_GAP);
            int nameHeight = 28;

            foreach (Control c in row.Controls)
            {
                string tag = c.Tag?.ToString() ?? "";
                string name = c.Name ?? "";

                if (tag == "minus" || name == "")
                {
                    if (tag == "minus") c.SetBounds(xMinus, btnY, BTN_W, BTN_H);
                }

                switch (tag)
                {
                    case "minus": c.SetBounds(xMinus, btnY, BTN_W, BTN_H); break;
                    case "qty": c.SetBounds(xQty, btnY, QTY_W, BTN_H); break;
                    case "plus": c.SetBounds(xPlus, btnY, BTN_W, BTN_H); break;
                    case "total": c.SetBounds(xTotal, 0, TOTAL_W, ROW_H); break;
                }

                // Belt-and-suspenders: catch controls that rely on Name instead of Tag
                if (name == "lblRowTotal") c.SetBounds(xTotal, 0, TOTAL_W, ROW_H);
                if (name == "lblQty") c.SetBounds(xQty, btnY, QTY_W, BTN_H);
                if (name == "lblCartPrice") c.SetBounds(LEFT_TEXT_X, 26, textWidth, 18);
                if (c is Label lbl && name == "" && c.Tag == null && c.Left >= LEFT_TEXT_X)
                    lbl.SetBounds(LEFT_TEXT_X, 0, textWidth, nameHeight);
            }
        }

        private async Task<string> GetCurrentUserRoleAsync()
        {
            try
            {
                int userId = CurrentUser.UserInfo.UserID;

                var api = new ApiService();
                string json = await api.GetAsync($"api/POSPermission/user-role?userId={userId}")
                    .ConfigureAwait(true);

                if (!string.IsNullOrEmpty(json))
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<ApiResponse<string>>(json, opts);
                    if (result?.IsSuccess == true && !string.IsNullOrWhiteSpace(result.Data))
                    {
                        string role = result.Data.Trim();
                        Debug.WriteLine($"GetCurrentUserRoleAsync: found role = '{role}'");
                        return role;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetCurrentUserRoleAsync: " + ex.Message);
            }
            return "Cashier";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CANCEL SALE — WITH TIERED AUTH
        // ══════════════════════════════════════════════════════════════════════
        private void btnCancelSale_Click(object sender, EventArgs e)
        {
            if (_cart.Count == 0)
            {
                if (_lastReceiptData != null && !_lastSaleWasPrinted)
                    ShowReprintDialog();
                return;
            }

            ShowCancelAuthDialog();
        }

        private async void ShowCancelAuthDialog()
        {
            string currentUserRole = await GetCurrentUserRoleAsync().ConfigureAwait(true);
            bool isCashier = string.IsNullOrEmpty(currentUserRole)
                             || currentUserRole.Equals("Cashier", StringComparison.OrdinalIgnoreCase);

            string requiredRoleDisplay = isCashier ? "Supervisor or Store Manager" : "Store Manager";

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(420, 340),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            var pnlHead = new Panel
            {
                BackColor = Color.FromArgb(55, 22, 22),
                Size = new Size(420, 50),
                Location = Point.Empty
            };
            pnlHead.Controls.Add(new Label
            {
                Text = $"🔒  {requiredRoleDisplay} Authorisation — Cancel Sale",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(400, 50),
                Location = new Point(10, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            decimal grand = GrandTotal();
            decimal lineCount = _cart.Sum(i => i.Qty);

            var lblSummary = new Label
            {
                Text = $"Cancelling {lineCount} item(s) — {Fmt(grand)} total",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 24),
                Location = new Point(20, 62),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblRoleBadge = new Label
            {
                Text = $"⚠  Requires: {requiredRoleDisplay}",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.FromArgb(50, 38, 18),
                AutoSize = false,
                Size = new Size(380, 24),
                Location = new Point(20, 88),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

            var lblUser = new Label
            {
                Text = "Username",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 122)
            };
            var txtUser = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 142)
            };
            var lblPass = new Label
            {
                Text = "Password",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 182)
            };
            var txtPass = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 202),
                UseSystemPasswordChar = true
            };
            var lblAuthStatus = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 20),
                Location = new Point(20, 242)
            };

            var btnAuth = new Button
            {
                Text = "✓  Authorise & Cancel Sale",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccRed,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 40),
                Location = new Point(20, 270),
                Cursor = Cursors.Hand
            };
            btnAuth.FlatAppearance.BorderSize = 0;
            btnAuth.Region = MakeRoundedRegion(btnAuth.Size, 8);

            var btnClose = new Button
            {
                Text = "Back",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 40),
                Location = new Point(250, 270),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Region = MakeRoundedRegion(btnClose.Size, 8);
            btnClose.Click += (s, e) => dlg.Close();

            async void DoAuth()
            {
                string username = txtUser.Text.Trim();
                string password = txtPass.Text;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    lblAuthStatus.Text = "Username and password are required.";
                    lblAuthStatus.ForeColor = AccRed;
                    return;
                }

                btnAuth.Enabled = false;
                lblAuthStatus.ForeColor = TextMuted;
                lblAuthStatus.Text = "Verifying…";

                try
                {
                    bool authorized = false;
                    string role = "";

                    try
                    {
                        var api = new ApiService();
                        string json = await api.GetAsync(
                            $"api/POSPermission/authorize-price-override?username={username}&password={password}")
                            .ConfigureAwait(true);

                        if (!string.IsNullOrEmpty(json))
                        {
                            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            using var doc = JsonDocument.Parse(json);
                            var dataKind = doc.RootElement.GetProperty("data").ValueKind;

                            if (dataKind == JsonValueKind.Object)
                            {
                                var resultWithRole = JsonSerializer.Deserialize<ApiResponse<AuthRoleDto>>(json, opts);
                                if (resultWithRole?.IsSuccess == true && resultWithRole.Data != null
                                    && !string.IsNullOrWhiteSpace(resultWithRole.Data.Role))
                                {
                                    authorized = true;
                                    role = resultWithRole.Data.Role.Trim();
                                }
                            }
                            else if (dataKind == JsonValueKind.True || dataKind == JsonValueKind.False)
                            {
                                var resultBool = JsonSerializer.Deserialize<ApiResponse<bool>>(json, opts);
                                if (resultBool?.IsSuccess == true && resultBool.Data)
                                    authorized = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("API Auth: " + ex.Message);
                        lblAuthStatus.ForeColor = AccRed;
                        lblAuthStatus.Text = "⛔  Could not reach server. Check connection.";
                        return;
                    }

                    // If API authorized but didn't return a role, look it up via API
                    if (authorized && string.IsNullOrWhiteSpace(role))
                    {
                        role = await GetRoleFromApiAsync(username).ConfigureAwait(true);
                    }

                    Debug.WriteLine($"DoAuth final: authorized={authorized}, role='{role}'");

                    if (!authorized)
                    {
                        lblAuthStatus.ForeColor = AccRed;
                        lblAuthStatus.Text = "⛔  Invalid username or password.";
                        return;
                    }

                    bool isSupervisor = role.Equals("Supervisor", StringComparison.OrdinalIgnoreCase);
                    bool isStoreManager = role.Equals("StoreManager", StringComparison.OrdinalIgnoreCase)
                                       || role.Equals("Store Manager", StringComparison.OrdinalIgnoreCase);

                    bool hasPermission;
                    if (isCashier)
                    {
                        hasPermission = isSupervisor || isStoreManager;
                        if (!hasPermission)
                        {
                            lblAuthStatus.ForeColor = AccRed;
                            lblAuthStatus.Text = $"⛔  Role '{role}' cannot authorise this action.";
                            return;
                        }
                    }
                    else
                    {
                        hasPermission = isStoreManager;
                        if (!hasPermission)
                        {
                            lblAuthStatus.ForeColor = AccRed;
                            lblAuthStatus.Text = isSupervisor
                                ? "⛔  Supervisors need Store Manager approval."
                                : $"⛔  Role '{role}' cannot authorise this action.";
                            return;
                        }
                    }

                    lblAuthStatus.ForeColor = TextGreen;
                    lblAuthStatus.Text = $"✓  Authorised as {role}";
                    await Task.Delay(500);
                    dlg.Close();

                    ShowStatus($"✓ Authorised by {username} ({role}).", true);
                    _cart.Clear();
                    RefreshCart();
                }
                catch (Exception ex)
                {
                    lblAuthStatus.ForeColor = AccRed;
                    lblAuthStatus.Text = "Error: " + ex.Message;
                }
                finally { btnAuth.Enabled = true; }
            }

            btnAuth.Click += (s, e) => DoAuth();
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; DoAuth(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
            };
            dlg.Controls.AddRange(new Control[]
            {
        pnlHead, lblSummary, lblRoleBadge,
        lblUser, txtUser, lblPass, txtPass,
        lblAuthStatus, btnAuth, btnClose
            });
            dlg.Shown += (s, e) => txtUser.Focus();
            dlg.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  DELETE ITEM — WITH TIERED AUTH
        // ══════════════════════════════════════════════════════════════════════
        private async void ShowDeleteAuthDialog(CartItem item)
        {
            string currentUserRole = await GetCurrentUserRoleAsync().ConfigureAwait(true);
            bool isCashier = string.IsNullOrEmpty(currentUserRole)
                             || currentUserRole.Equals("Cashier", StringComparison.OrdinalIgnoreCase)
                             || (!currentUserRole.Equals("Supervisor", StringComparison.OrdinalIgnoreCase)
                              && !currentUserRole.Equals("StoreManager", StringComparison.OrdinalIgnoreCase)
                              && !currentUserRole.Equals("Store Manager", StringComparison.OrdinalIgnoreCase));

            string requiredRoleDisplay = isCashier ? "Supervisor or Store Manager" : "Store Manager";

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(420, 340),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            var pnlHead = new Panel
            {
                BackColor = Color.FromArgb(42, 46, 58),
                Size = new Size(420, 50),
                Location = Point.Empty
            };
            pnlHead.Controls.Add(new Label
            {
                Text = $"🔒  {requiredRoleDisplay} Authorisation Required",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(400, 50),
                Location = new Point(10, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var lblItem = new Label
            {
                Text = $"Removing: {item.Name}  ×{item.Qty}",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 24),
                Location = new Point(20, 62),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblRoleBadge = new Label
            {
                Text = $"⚠  Requires: {requiredRoleDisplay}",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.FromArgb(50, 38, 18),
                AutoSize = false,
                Size = new Size(380, 24),
                Location = new Point(20, 88),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

            var lblUser = new Label
            {
                Text = "Username",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 122)
            };
            var txtUser = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 142)
            };
            var lblPass = new Label
            {
                Text = "Password",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 182)
            };
            var txtPass = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 202),
                UseSystemPasswordChar = true
            };
            var lblAuthStatus = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 20),
                Location = new Point(20, 242)
            };

            var btnAuth = new Button
            {
                Text = "✓  Authorise & Remove",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccRed,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 40),
                Location = new Point(20, 270),
                Cursor = Cursors.Hand
            };
            btnAuth.FlatAppearance.BorderSize = 0;
            btnAuth.Region = MakeRoundedRegion(btnAuth.Size, 8);

            var btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 40),
                Location = new Point(250, 270),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Region = MakeRoundedRegion(btnCancel.Size, 8);
            btnCancel.Click += (s, e) => dlg.Close();

            async void DoAuth()
            {
                string username = txtUser.Text.Trim();
                string password = txtPass.Text;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    lblAuthStatus.Text = "Username and password are required.";
                    lblAuthStatus.ForeColor = AccRed;
                    return;
                }

                btnAuth.Enabled = false;
                lblAuthStatus.ForeColor = TextMuted;
                lblAuthStatus.Text = "Verifying…";

                try
                {
                    bool authorized = false;
                    bool hasPermission = false;
                    string role = "";

                    try
                    {
                        var api = new ApiService();
                        string json = await api.GetAsync(
                            $"api/POSPermission/authorize-price-override?username={username}&password={password}")
                            .ConfigureAwait(true);

                        if (!string.IsNullOrEmpty(json))
                        {
                            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            using var doc = JsonDocument.Parse(json);
                            var dataKind = doc.RootElement.GetProperty("data").ValueKind;

                            if (dataKind == JsonValueKind.Object)
                            {
                                var resultWithRole = JsonSerializer.Deserialize<ApiResponse<AuthRoleDto>>(json, opts);
                                if (resultWithRole?.IsSuccess == true && resultWithRole.Data != null
                                    && !string.IsNullOrWhiteSpace(resultWithRole.Data.Role))
                                {
                                    authorized = true;
                                    role = resultWithRole.Data.Role.Trim();
                                }
                            }
                            else if (dataKind == JsonValueKind.True || dataKind == JsonValueKind.False)
                            {
                                var resultBool = JsonSerializer.Deserialize<ApiResponse<bool>>(json, opts);
                                if (resultBool?.IsSuccess == true && resultBool.Data)
                                    authorized = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("API Auth: " + ex.Message);
                        lblAuthStatus.ForeColor = AccRed;
                        lblAuthStatus.Text = "⛔  Could not reach server. Check connection.";
                        return;
                    }

                    if (authorized && string.IsNullOrWhiteSpace(role))
                    {
                        role = await GetRoleFromApiAsync(username).ConfigureAwait(true);
                    }

                    if (!authorized)
                    {
                        lblAuthStatus.ForeColor = AccRed;
                        lblAuthStatus.Text = "⛔  Invalid username or password.";
                        return;
                    }

                    bool isSupervisor = role.Equals("Supervisor", StringComparison.OrdinalIgnoreCase);
                    bool isStoreManager = role.Equals("StoreManager", StringComparison.OrdinalIgnoreCase)
                                       || role.Equals("Store Manager", StringComparison.OrdinalIgnoreCase);

                    if (isCashier)
                    {
                        hasPermission = isSupervisor || isStoreManager;
                        if (!hasPermission)
                        {
                            lblAuthStatus.ForeColor = AccRed;
                            lblAuthStatus.Text = $"⛔  Role '{role}' cannot authorise item removal.";
                            return;
                        }
                    }
                    else
                    {
                        hasPermission = isStoreManager;
                        if (!hasPermission)
                        {
                            lblAuthStatus.ForeColor = AccRed;
                            lblAuthStatus.Text = isSupervisor
                                ? "⛔  Supervisors need Store Manager approval to remove items."
                                : $"⛔  Role '{role}' cannot authorise this removal.";
                            return;
                        }
                    }

                    lblAuthStatus.ForeColor = TextGreen;
                    lblAuthStatus.Text = $"✓  Authorised as {role}";
                    await Task.Delay(500);
                    dlg.Close();

                    _cart.Remove(item);
                    RefreshCart();
                    UpdateTotals();
                    ShowStatus($"✓ Item removed — authorised by {username} ({role}).", true);
                }
                catch (Exception ex)
                {
                    lblAuthStatus.ForeColor = AccRed;
                    lblAuthStatus.Text = "Error: " + ex.Message;
                }
                finally { btnAuth.Enabled = true; }
            }

            btnAuth.Click += (s, e) => DoAuth();
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; DoAuth(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
            };
            dlg.Controls.AddRange(new Control[]
            {
        pnlHead, lblItem, lblRoleBadge,
        lblUser, txtUser, lblPass, txtPass,
        lblAuthStatus, btnAuth, btnCancel
            });
            dlg.Shown += (s, e) => txtUser.Focus();
            dlg.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  QUANTITY REDUCE — WITH TIERED AUTH (fires on every − click, not just
        //  when the item would be fully removed)
        // ══════════════════════════════════════════════════════════════════════
        private async void ShowQtyReduceAuthDialog(CartItem item)
        {
            string currentUserRole = await GetCurrentUserRoleAsync().ConfigureAwait(true);
            bool isCashier = string.IsNullOrEmpty(currentUserRole)
                             || currentUserRole.Equals("Cashier", StringComparison.OrdinalIgnoreCase)
                             || (!currentUserRole.Equals("Supervisor", StringComparison.OrdinalIgnoreCase)
                              && !currentUserRole.Equals("StoreManager", StringComparison.OrdinalIgnoreCase)
                              && !currentUserRole.Equals("Store Manager", StringComparison.OrdinalIgnoreCase));

            string requiredRoleDisplay = isCashier ? "Supervisor or Store Manager" : "Store Manager";

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(420, 340),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            var pnlHead = new Panel
            {
                BackColor = Color.FromArgb(42, 46, 58),
                Size = new Size(420, 50),
                Location = Point.Empty
            };
            pnlHead.Controls.Add(new Label
            {
                Text = $"🔒  {requiredRoleDisplay} Authorisation Required",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(400, 50),
                Location = new Point(10, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var lblItem = new Label
            {
                Text = $"Reducing: {item.Name}   {item.Qty} → {item.Qty - 1}",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 24),
                Location = new Point(20, 62),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblRoleBadge = new Label
            {
                Text = $"⚠  Requires: {requiredRoleDisplay}",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.FromArgb(50, 38, 18),
                AutoSize = false,
                Size = new Size(380, 24),
                Location = new Point(20, 88),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

            var lblUser = new Label
            {
                Text = "Username",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 122)
            };
            var txtUser = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 142)
            };
            var lblPass = new Label
            {
                Text = "Password",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 182)
            };
            var txtPass = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(380, 30),
                Location = new Point(20, 202),
                UseSystemPasswordChar = true
            };
            var lblAuthStatus = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 20),
                Location = new Point(20, 242)
            };

            var btnAuth = new Button
            {
                Text = "✓  Authorise & Reduce",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccRed,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 40),
                Location = new Point(20, 270),
                Cursor = Cursors.Hand
            };
            btnAuth.FlatAppearance.BorderSize = 0;
            btnAuth.Region = MakeRoundedRegion(btnAuth.Size, 8);

            var btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 40),
                Location = new Point(250, 270),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Region = MakeRoundedRegion(btnCancel.Size, 8);
            btnCancel.Click += (s, e) => dlg.Close();

            async void DoAuth()
            {
                string username = txtUser.Text.Trim();
                string password = txtPass.Text;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    lblAuthStatus.Text = "Username and password are required.";
                    lblAuthStatus.ForeColor = AccRed;
                    return;
                }

                btnAuth.Enabled = false;
                lblAuthStatus.ForeColor = TextMuted;
                lblAuthStatus.Text = "Verifying…";

                try
                {
                    bool authorized = false;
                    bool hasPermission = false;
                    string role = "";

                    try
                    {
                        var api = new ApiService();
                        string json = await api.GetAsync(
                            $"api/POSPermission/authorize-price-override?username={username}&password={password}")
                            .ConfigureAwait(true);

                        if (!string.IsNullOrEmpty(json))
                        {
                            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            using var doc = JsonDocument.Parse(json);
                            var dataKind = doc.RootElement.GetProperty("data").ValueKind;

                            if (dataKind == JsonValueKind.Object)
                            {
                                var resultWithRole = JsonSerializer.Deserialize<ApiResponse<AuthRoleDto>>(json, opts);
                                if (resultWithRole?.IsSuccess == true && resultWithRole.Data != null
                                    && !string.IsNullOrWhiteSpace(resultWithRole.Data.Role))
                                {
                                    authorized = true;
                                    role = resultWithRole.Data.Role.Trim();
                                }
                            }
                            else if (dataKind == JsonValueKind.True || dataKind == JsonValueKind.False)
                            {
                                var resultBool = JsonSerializer.Deserialize<ApiResponse<bool>>(json, opts);
                                if (resultBool?.IsSuccess == true && resultBool.Data)
                                    authorized = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("API Auth: " + ex.Message);
                        lblAuthStatus.ForeColor = AccRed;
                        lblAuthStatus.Text = "⛔  Could not reach server. Check connection.";
                        return;
                    }

                    if (authorized && string.IsNullOrWhiteSpace(role))
                        role = await GetRoleFromApiAsync(username).ConfigureAwait(true);

                    if (!authorized)
                    {
                        lblAuthStatus.ForeColor = AccRed;
                        lblAuthStatus.Text = "⛔  Invalid username or password.";
                        return;
                    }

                    bool isSupervisor = role.Equals("Supervisor", StringComparison.OrdinalIgnoreCase);
                    bool isStoreManager = role.Equals("StoreManager", StringComparison.OrdinalIgnoreCase)
                                       || role.Equals("Store Manager", StringComparison.OrdinalIgnoreCase);

                    if (isCashier)
                    {
                        hasPermission = isSupervisor || isStoreManager;
                        if (!hasPermission)
                        {
                            lblAuthStatus.ForeColor = AccRed;
                            lblAuthStatus.Text = $"⛔  Role '{role}' cannot authorise quantity reduction.";
                            return;
                        }
                    }
                    else
                    {
                        hasPermission = isStoreManager;
                        if (!hasPermission)
                        {
                            lblAuthStatus.ForeColor = AccRed;
                            lblAuthStatus.Text = isSupervisor
                                ? "⛔  Supervisors need Store Manager approval to reduce quantity."
                                : $"⛔  Role '{role}' cannot authorise this reduction.";
                            return;
                        }
                    }

                    lblAuthStatus.ForeColor = TextGreen;
                    lblAuthStatus.Text = $"✓  Authorised as {role}";
                    await Task.Delay(400);
                    dlg.Close();

                    item.Qty--;
                    RefreshCart();
                    UpdateTotals();
                    ShowStatus($"✓ Quantity reduced — authorised by {username} ({role}).", true);
                }
                catch (Exception ex)
                {
                    lblAuthStatus.ForeColor = AccRed;
                    lblAuthStatus.Text = "Error: " + ex.Message;
                }
                finally { btnAuth.Enabled = true; }
            }

            btnAuth.Click += (s, e) => DoAuth();
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; DoAuth(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
            };
            dlg.Controls.AddRange(new Control[]
            {
        pnlHead, lblItem, lblRoleBadge,
        lblUser, txtUser, lblPass, txtPass,
        lblAuthStatus, btnAuth, btnCancel
            });
            dlg.Shown += (s, e) => txtUser.Focus();
            dlg.ShowDialog(this);
        }
        // ══════════════════════════════════════════════════════════════════════
        //  Shared helper: resolve role by username via API
        // ══════════════════════════════════════════════════════════════════════
        private async Task<string> GetRoleFromApiAsync(string username)
        {
            try
            {
                var api = new ApiService();
                string json = await api.GetAsync($"api/POSPermission/role-by-username?username={username}")
                    .ConfigureAwait(true);

                if (!string.IsNullOrEmpty(json))
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<ApiResponse<string>>(json, opts);
                    if (result?.IsSuccess == true && !string.IsNullOrWhiteSpace(result.Data))
                        return result.Data.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetRoleFromApiAsync: " + ex.Message);
            }
            return "";
        }
        private async Task<(bool authorized, string role)> VerifyRoleFromSQLite(string userId, string password)
        {
            if (!System.IO.File.Exists(_dbPath)) return (false, "");
            try
            {
                return await Task.Run(() =>
                {
                    using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                    conn.Open();
                    const string sql = @"
                SELECT p.RoleName FROM Users u
                INNER JOIN POSPermission p ON u.RoleID = p.RoleID
                WHERE u.Email = @UserID AND u.Password = @Password LIMIT 1;";
                    using var cmd = new SQLiteCommand(sql, conn);
                    if (int.TryParse(userId, out int idInt))
                        cmd.Parameters.Add("@UserID", System.Data.DbType.Int32).Value = idInt;
                    else
                        cmd.Parameters.AddWithValue("@UserID", userId);
                    cmd.Parameters.AddWithValue("@Password", password.Trim());
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        string roleName = result.ToString().Trim();
                        // Any role that exists in POSPermission and has VoidLine=1 is authorized
                        bool isAuthorized =
                            roleName.Equals("Cashier", StringComparison.OrdinalIgnoreCase)
                         || roleName.Equals("Supervisor", StringComparison.OrdinalIgnoreCase)
                         || roleName.Equals("StoreManager", StringComparison.OrdinalIgnoreCase)
                         || roleName.Equals("Store Manager", StringComparison.OrdinalIgnoreCase);
                        return (isAuthorized, roleName);
                    }
                    return (false, "");
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("VerifyRole: " + ex.ToString()); return (false, ""); }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CART ITEM POPUP
        // ══════════════════════════════════════════════════════════════════════
        private void ShowCartItemDialog(CartItem item)
        {
            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(420, 420),
                KeyPreview = true,
                ShowInTaskbar = false
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);
            dlg.Paint += (sp, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(60, 65, 90), 1.5f))
                using (var path = RoundedPath(new Rectangle(1, 1, dlg.Width - 2, dlg.Height - 2), 12))
                    pe.Graphics.DrawPath(pen, path);
            };

            // ── Header ────────────────────────────────────────────────────────────────
            var pnlHead = new Panel { BackColor = Color.FromArgb(42, 46, 58), Size = new Size(420, 50), Location = Point.Empty };
            var lblTitle = new Label
            {
                Text = item.Name,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(350, 50),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var btnClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 11F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(42, 50),
                Location = new Point(378, 0),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => dlg.Close();
            pnlHead.Controls.AddRange(new Control[] { lblTitle, btnClose });

            // ── Helper: underlined field ──────────────────────────────────────────────
            int fieldY = 66;
            TextBox AddField(string label, string value, Color accent)
            {
                dlg.Controls.Add(new Label
                {
                    Text = label,
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    Size = new Size(120, 32),
                    Location = new Point(20, fieldY),
                    TextAlign = ContentAlignment.MiddleLeft
                });
                var tb = new TextBox
                {
                    Text = value,
                    Font = new Font("Segoe UI", 11F),
                    ForeColor = TextWhite,
                    BackColor = Color.FromArgb(38, 42, 54),
                    BorderStyle = BorderStyle.FixedSingle,
                    Size = new Size(260, 30),
                    Location = new Point(140, fieldY + 1),
                    TextAlign = System.Windows.Forms.HorizontalAlignment.Right
                };
                var bar = new Panel
                {
                    BackColor = Color.FromArgb(55, 60, 78),
                    Size = new Size(260, 2),
                    Location = new Point(140, fieldY + 31)
                };
                tb.Enter += (s, e) => { bar.BackColor = accent; tb.SelectAll(); };
                tb.Leave += (s, e) => bar.BackColor = Color.FromArgb(55, 60, 78);
                dlg.Controls.AddRange(new Control[] { tb, bar });
                fieldY += 42;
                return tb;
            }

            var tbPrice = AddField($"Price ({_currencySymbol})", item.Price.ToString("F2"), AccOrange);
            tbPrice.ReadOnly = true;
            tbPrice.TabStop = false;
            tbPrice.Cursor = Cursors.Default;
            tbPrice.BackColor = Color.FromArgb(30, 33, 42);
            tbPrice.ForeColor = TextMuted;

            var tbDisc = AddField("Discount %", item.DiscountPct.ToString("F1"), AccCyan); 

            var tbQty = AddField("Quantity", item.Qty.ToString(), AccBlue);

            dlg.Controls.Add(new Label
            {
                Text = "Tax",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Size = new Size(120, 32),
                Location = new Point(20, fieldY),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var cmbLineTax = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(260, 30),
                Location = new Point(140, fieldY + 1)
            };
            cmbLineTax.Items.Add("No Tax");
            foreach (var t in _taxMaster) cmbLineTax.Items.Add($"{t.TaxCode} ({t.TaxPercentage:F1}%)");

            int curTaxIdx = item.TaxId > 0 ? _taxMaster.FindIndex(t => t.TaxId == item.TaxId) + 1 : 0;
            cmbLineTax.SelectedIndex = curTaxIdx >= 0 ? curTaxIdx : 0;
            dlg.Controls.Add(cmbLineTax);
            fieldY += 42;

            // ── NEW: per-line tax picker ─────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "Tax",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Size = new Size(120, 32),
                Location = new Point(20, fieldY),
                TextAlign = ContentAlignment.MiddleLeft
            });
             
            cmbLineTax.Items.Add("No Tax");
            foreach (var t in _taxMaster)
                cmbLineTax.Items.Add($"{t.TaxCode} ({t.TaxPercentage:F1}%)");

        
            cmbLineTax.SelectedIndex = curTaxIdx >= 0 ? curTaxIdx : 0;

            dlg.Controls.Add(cmbLineTax);
            fieldY += 42;   // keep layout consistent with AddField's spacing

            // ── Price Group section (D365 mode only) ─────────────────────────────────
            bool hasGroups = _isD365Mode
                             && !string.IsNullOrWhiteSpace(item.Barcode)
                             && _d365Details.ContainsKey(item.Barcode)
                             && _d365Details[item.Barcode].Count > 0;

            ComboBox cmbPriceGroup = null;
            Label lblAuthBadge = null;

            if (hasGroups)
            {
                var details = _d365Details[item.Barcode];

                dlg.Controls.Add(new Panel
                {
                    BackColor = Color.FromArgb(50, 54, 66),
                    Size = new Size(380, 1),
                    Location = new Point(20, fieldY + 4)
                });
                fieldY += 14;

                dlg.Controls.Add(new Label
                {
                    Text = "PRICE GROUP  (AccountRelation)",
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(80, 90, 110),
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(20, fieldY)
                });
                fieldY += 20;

                cmbPriceGroup = new ComboBox
                {
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = TextWhite,
                    BackColor = Color.FromArgb(38, 42, 54),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(380, 30),
                    Location = new Point(20, fieldY)
                };
                fieldY += 38;

                var groups = details
                    .Select(d => string.IsNullOrWhiteSpace(d.AccountRelation) ? "(default)" : d.AccountRelation)
                    .Distinct()
                    .OrderBy(g => g)
                    .ToList();
                foreach (var g in groups) cmbPriceGroup.Items.Add(g);

                int matchIdx = 0;
                for (int i = 0; i < details.Count; i++)
                {
                    if (details[i].Amount == item.Price)
                    {
                        string grp = string.IsNullOrWhiteSpace(details[i].AccountRelation)
                                     ? "(default)" : details[i].AccountRelation;
                        matchIdx = groups.IndexOf(grp);
                        break;
                    }
                }
                cmbPriceGroup.SelectedIndex = Math.Max(0, matchIdx);
                dlg.Controls.Add(cmbPriceGroup);

                lblAuthBadge = new Label
                {
                    Text = "",
                    Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                    ForeColor = AccRed,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(380, 20),
                    Location = new Point(20, fieldY),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Visible = false
                };
                dlg.Controls.Add(lblAuthBadge);
                fieldY += 24;

                bool _authInProgress = false;
                int _lastGoodIndex = cmbPriceGroup.SelectedIndex;

                cmbPriceGroup.SelectedIndexChanged += async (s, ev) =>
                {
                    if (_authInProgress) return;
                    int newIndex = cmbPriceGroup.SelectedIndex;
                    if (newIndex == _lastGoodIndex) return;

                    string newGroup = cmbPriceGroup.SelectedItem?.ToString() ?? "";
                    var targetDetail = details.FirstOrDefault(d =>
                        (string.IsNullOrWhiteSpace(d.AccountRelation) ? "(default)" : d.AccountRelation)
                        .Equals(newGroup, StringComparison.OrdinalIgnoreCase));
                    if (targetDetail == null) return;

                    bool authorized = await ShowPriceGroupAuthDialogAsync(dlg, newGroup, targetDetail.Amount);

                    if (!authorized)
                    {
                        _authInProgress = true;
                        cmbPriceGroup.SelectedIndex = _lastGoodIndex;
                        _authInProgress = false;

                        lblAuthBadge.Text = "⛔  Authorisation failed — price group unchanged.";
                        lblAuthBadge.ForeColor = AccRed;
                        lblAuthBadge.Visible = true;
                        return;
                    }

                    _lastGoodIndex = newIndex;
                    tbPrice.Text = targetDetail.Amount.ToString("F2");

                    lblAuthBadge.Text = $"✓  Authorised — Price Group '{newGroup}'  ({Fmt(targetDetail.Amount)})";
                    lblAuthBadge.ForeColor = TextGreen;
                    lblAuthBadge.Visible = true;
                };
            }


            // ── Divider + hint ────────────────────────────────────────────────────────
            dlg.Controls.Add(new Panel
            {
                BackColor = Color.FromArgb(50, 55, 70),
                Size = new Size(380, 1),
                Location = new Point(20, fieldY + 2)
            });
            fieldY += 12;
            dlg.Controls.Add(new Label
            {
                Text = "Enter = Save    Esc = Cancel",
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = Color.FromArgb(80, 90, 110),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, fieldY)
            });
            fieldY += 22;

            // ── Action buttons ────────────────────────────────────────────────────────
            Button MakeBtn(string text, Color bg, int x, int width)
            {
                var b = new Button
                {
                    Text = text,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    ForeColor = TextWhite,
                    BackColor = bg,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(width, 38),
                    Location = new Point(x, fieldY),
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                b.Region = MakeRoundedRegion(b.Size, 7);
                return b;
            }

            var btnSave = MakeBtn("Save  ✓", AccGreen, 20, 185);
            var btnDelete = MakeBtn("Delete  🗑", AccRed, 215, 185);

            // ── Save logic ────────────────────────────────────────────────────────────
            async void DoSave()
            {
                bool ok = true;

                if (decimal.TryParse(tbDisc.Text.Trim(), out decimal nd) && nd >= 0 && nd <= 100)
                    item.DiscountPct = nd;
                else { ok = false; tbDisc.BackColor = Color.FromArgb(80, 30, 30); tbDisc.Focus(); }

                if (ok && int.TryParse(tbQty.Text.Trim(), out int nq) && nq > 0)
                {
                    decimal unitsPerPack = GetUnitsPerPackForCartItem(item);
                    decimal available = await GetLiveAvailableStockAsync(
                        item.ItemId, excludeCartItemName: item.Name, excludeUom: item.UOM).ConfigureAwait(true);
                    if (!POSAPP.Printer.StockSettings.AllowOutOfStockSale && nq * unitsPerPack > available)
                    {
                        ok = false;
                        tbQty.BackColor = Color.FromArgb(80, 30, 30);
                        tbQty.Focus();
                        decimal maxPackQty = Math.Floor(available / unitsPerPack);
                        ShowStatus($"⛔ Only {maxPackQty:F0} × {item.UOMName} of {item.Name} available.", false);
                    }
                    else
                    {
                        item.Qty = nq;
                    }
                }
                else if (ok) { ok = false; tbQty.BackColor = Color.FromArgb(80, 30, 30); tbQty.Focus(); }

                if (ok && hasGroups && cmbPriceGroup != null)
                {
                    string selectedGroup = cmbPriceGroup.SelectedItem?.ToString() ?? "";
                    var matchedDetail = _d365Details[item.Barcode].FirstOrDefault(d =>
                        (string.IsNullOrWhiteSpace(d.AccountRelation) ? "(default)" : d.AccountRelation)
                        .Equals(selectedGroup, StringComparison.OrdinalIgnoreCase));

                    if (matchedDetail != null)
                        item.Price = matchedDetail.Amount;
                }


                if (ok)
                {
                    if (cmbLineTax.SelectedIndex <= 0)
                    {
                        item.TaxId = 0; item.TaxCode = ""; item.TaxPercentage = 0m;
                    }
                    else
                    {
                        var t = _taxMaster[cmbLineTax.SelectedIndex - 1];
                        item.TaxId = t.TaxId; item.TaxCode = t.TaxCode; item.TaxPercentage = t.TaxPercentage;
                    } 

                    RefreshCart();
                    UpdateTotals();
                    dlg.Close();
                }
            }

            btnSave.Click += (s, e) => DoSave();
            btnDelete.Click += (s, e) => { dlg.Close(); ShowDeleteAuthDialog(item); };
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; DoSave(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
            };

            dlg.ClientSize = new Size(420, fieldY + 52);
            dlg.Region = MakeRoundedRegion(dlg.ClientSize, 12);

            tbPrice.TabIndex = 0;
            tbDisc.TabIndex = 1;
            tbQty.TabIndex = 2;
            btnSave.TabIndex = 3;
            btnDelete.TabIndex = 4;

            dlg.Controls.Add(pnlHead);
            dlg.Controls.Add(btnSave);
            dlg.Controls.Add(btnDelete);
            dlg.Shown += (s, e) => { tbDisc.Focus(); tbDisc.SelectAll(); };

            dlg.ShowDialog(this);
            dlg.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SMALL BUTTON
        // ══════════════════════════════════════════════════════════════════════
        private Button MakeSmallBtn(string text, Point loc, Color bg)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(24, 24),
                Location = loc,
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                UseCompatibleTextRendering = true
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg);
            b.Region = MakeRoundedRegion(b.Size, 5);
            return b;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  NUMPAD DISPLAY
        // ══════════════════════════════════════════════════════════════════════
        private void BuildNumpadDisplay()
        {
            txtSplitCash.BackColor = Color.FromArgb(42, 46, 56);
            txtSplitUpi.BackColor = Color.FromArgb(42, 46, 56);
            txtSplitCard.BackColor = Color.FromArgb(42, 46, 56);
            txtSplitCash.Font = new Font("Consolas", 13F, FontStyle.Bold);
            txtSplitUpi.Font = new Font("Consolas", 13F, FontStyle.Bold);
            txtSplitCard.Font = new Font("Consolas", 13F, FontStyle.Bold);
        }

        // ══════════════════════════════════════════════════════════════════════
        private void UpdateTotals()
        {
            decimal gross = _cart.Sum(i => i.Price * i.Qty);
            decimal discAmt = _cart.Sum(i => i.DiscountAmt);
            decimal after = gross - discAmt;
            decimal tax = _taxAlreadyIncluded ? 0m : _cart.Sum(i => i.TaxAmt);
            decimal allocatedCharges = _cart.Sum(i => i.Charges);
            decimal fixedCharges = _charges.Where(c => c.Type == 1).Sum(c => c.Amount);
            decimal grand = after + tax + allocatedCharges + fixedCharges;
            _subtotal = gross;

            lblSubtotalVal.Text = Fmt(gross);
            lblDiscountVal.Text = "- " + Fmt(discAmt);
            lblTaxVal.Text = Fmt(tax);
            lblGrandTotal.Text = Fmt(grand);
            lblItemCount.Text = _cart.Sum(i => i.Qty) + " item(s)";

            UpdateSplitDisplay();
            UpdateGrandTotalBigDisplay();
            UpdateStockReductionDisplay();
        }
        private void UpdateStockReductionDisplay()
        {
            if (lblStockReduction == null) return;

            if (_cart.Count == 0)
            {
                lblStockReduction.Text = "📉 Stock to reduce: 0 unit(s)";
                lblStockReduction.ForeColor = TextMuted;
                return;
            }

            decimal totalQty = _cart.Sum(i => i.Qty * GetUnitsPerPackForCartItem(i));

            lblStockReduction.Text = $"📉 Stock to reduce: {totalQty:F0} unit(s) across {_cart.Count} line(s)";
            lblStockReduction.ForeColor = AccOrange;
        }
        // ── Shows remaining amount due (or change/paid state) in the big centre banner ──
        private void UpdateGrandTotalBigDisplay()
        {
            if (lblGrandTotalBig == null) return;

            decimal grand = GrandTotal();
            decimal paid = _splitCash + _splitUpi + _splitCard;
            decimal remaining = grand - paid;

            if (_cart.Count == 0)
            {
                lblGrandTotalBig.Text = "P 0.00";
                lblGrandTotalBig.ForeColor = Color.FromArgb(130, 140, 158); // muted grey
            }
            else if (_isCreditSale)                                   // ← ADD HERE
            {
                lblGrandTotalBig.Text = "CREDIT: " + Fmt(grand);
                lblGrandTotalBig.ForeColor = AccPurple;
            }
            else if (remaining > 0.001m)
            {
                lblGrandTotalBig.Text = "DUE: " + Fmt(remaining);
                lblGrandTotalBig.ForeColor = Color.FromArgb(251, 146, 60); // orange — still owing
            }
            else if (remaining < -0.001m)
            {
                decimal change = -remaining;
                lblGrandTotalBig.Text = "CHANGE: " + Fmt(change);
                lblGrandTotalBig.ForeColor = Color.FromArgb(52, 211, 153); // green — change due back
            }
            else
            {
                lblGrandTotalBig.Text = "PAID: " + Fmt(grand);
                lblGrandTotalBig.ForeColor = Color.FromArgb(52, 211, 153); // green — exact
            }
        }

        //private decimal GrandTotal()
        //{
        //    decimal gross = _cart.Sum(i => i.Price * i.Qty);
        //    decimal discAmt = _cart.Sum(i => i.DiscountAmt);
        //    decimal after = gross - discAmt;
        //    return after + Math.Round(after * _taxRate, 2);
        //}

        private void nudDiscount_ValueChanged(object sender, EventArgs e)
        {
            decimal pct = nudDiscount.Value;
            foreach (var i in _cart) i.DiscountPct = pct;
            RefreshCart(); UpdateTotals();
            ShowStatus($"Applied {pct:F1}% discount to all items.", true);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SPLIT PAYMENT
        // ══════════════════════════════════════════════════════════════════════
        private void SetActiveSplit(string field)
        {
            _activeSplit = field;
            _numpadBuffer = "";

            Color normal = PanelDark2, active = Color.FromArgb(30, 60, 45);
            panelSplitCash.BackColor = field == "cash" ? active : normal;
            panelSplitUpi.BackColor = field == "upi" ? active : normal;
            panelSplitCard.BackColor = field == "card" ? active : normal;
            lblCashTitle.ForeColor = field == "cash" ? AccGreen : TextMuted;
            lblUpiTitle.ForeColor = field == "upi" ? AccGreen : TextMuted;
            lblCardTitle.ForeColor = field == "card" ? AccGreen : TextMuted;

            if (field == "cash") { _splitCash = 0m; txtSplitCash.Text = ""; }
            else if (field == "upi") { _splitUpi = 0m; txtSplitUpi.Text = ""; }
            else if (field == "card") { _splitCard = 0m; txtSplitCard.Text = ""; }

            this.ActiveControl = null;

            UpdateSplitDisplay();
            UpdateGrandTotalBigDisplay();
        }

        private void panelSplitCash_Click(object sender, EventArgs e) => SetActiveSplit("cash");
        private void panelSplitCard_Click(object sender, EventArgs e)
        {
            SetActiveSplit("card");
        }



        // AFTER
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool inTextBox = txtSearch.Focused || txtCustomer.Focused || txtBarcode.Focused;
            if (!inTextBox)
            {
                if (keyData == Keys.F8) { OpenFloatManager(); return true; }
            }
            if (keyData == Keys.Escape) { this.Close(); return true; }
            if (keyData == Keys.F1)
            {
                // Always forward to the click handler — it has its own guards
                // (empty cart, already-processing) and will show status messages
                // if it can't proceed. Don't silently swallow F1 here.
                btnTenderSale_Click(null, null);
                return true;
            }
            if (keyData == Keys.F5) { txtSearch.Focus(); return true; }
            if (keyData == Keys.F6) { txtBarcode.Focus(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }


        private void NumpadPress(string key)
        {
            // ── Back / clear always allowed ────────────────────────────────────
            if (key == "back")
            {
                if (_numpadBuffer.Length > 0)
                    _numpadBuffer = _numpadBuffer[..^1];

                decimal.TryParse(_numpadBuffer, out decimal clearedVal);
                if (_activeSplit == "cash") _splitCash = clearedVal;
                else if (_activeSplit == "upi") _splitUpi = clearedVal;
                else if (_activeSplit == "card") _splitCard = clearedVal;

                UpdateSplitDisplay();
                UpdateGrandTotalBigDisplay();   // ← ADD
                return;
            }

            if (key == "clear")
            {
                _numpadBuffer = "";
                if (_activeSplit == "cash") _splitCash = 0m;
                else if (_activeSplit == "upi") _splitUpi = 0m;
                else if (_activeSplit == "card") _splitCard = 0m;

                UpdateSplitDisplay();
                UpdateGrandTotalBigDisplay();   // ← ADD
                return;
            }

            // ── Calculate what others have already committed ───────────────────
            decimal otherTotal = _activeSplit == "cash" ? _splitUpi + _splitCard
                               : _activeSplit == "upi" ? _splitCash + _splitCard
                               : _splitCash + _splitUpi;

            decimal grand = GrandTotal();
            decimal maxAllow = Math.Max(0m, grand - otherTotal);

            // ── Block new digit if this split already covers the remaining ─────
            if (_activeSplit != "cash" && otherTotal >= grand - 0.001m)
            {
                ShowStatus("✓ Payment already covered — press Tender.", true);
                return;
            }

            // ── Build the new buffer ───────────────────────────────────────────
            if (key == ".")
            {
                if (!_numpadBuffer.Contains(".") && _numpadBuffer.Length < 10)
                    _numpadBuffer += ".";
            }
            else
            {
                if (_numpadBuffer.Length < 10)
                    _numpadBuffer += key;
            }

            // ── Parse typed value ──────────────────────────────────────────────
            decimal.TryParse(_numpadBuffer, out decimal typed);

            // ── Clamp: Bank/Card cannot exceed remaining (Cash can overpay) ────
            if (_activeSplit != "cash" && typed > maxAllow + 0.001m)
            {
                _numpadBuffer = maxAllow.ToString("F2");
                typed = maxAllow;
                ShowStatus($"⚠ Max for this method: {Fmt(maxAllow)}", false);
            }

            // ── Commit to the correct split field ─────────────────────────────
            if (_activeSplit == "cash") _splitCash = typed;
            else if (_activeSplit == "upi") _splitUpi = typed;
            else if (_activeSplit == "card") _splitCard = typed;

            // ── Block if total is now complete ────────────────────────────────
            // ── Block if total is now complete ────────────────────────────────
            decimal newTotal = _splitCash + _splitUpi + _splitCard;
            if (newTotal >= grand - 0.001m && _activeSplit != "cash")
                ShowStatus("✓ Payment complete — press Tender to finalise.", true);

            UpdateSplitDisplay();
            UpdateGrandTotalBigDisplay();
        }
        // AFTER
        private void NumpadBtn_Click(object sender, EventArgs e)
        {
            if (_cart.Count == 0) { ShowStatus("Cart is empty.", false); return; }

            var b = (Button)sender; NumpadPress(b.Tag?.ToString() ?? b.Text);
        }

        private void UpdateSplitDisplay()
        {
            // ── Balance display is always shown now — payment happens on this screen ──
            if (lblSplitBalance != null)
                lblSplitBalance.Visible = true;

            decimal grand = GrandTotal();
            decimal cash = _splitCash;
            decimal upi = _splitUpi;
            decimal card = _splitCard;
            decimal totalEntered = cash + upi + card;
            decimal remaining = grand - totalEntered;
            decimal change = totalEntered - grand;
            decimal floatBal = ShiftState.IsOpen ? ShiftState.CurrentFloat : 0m;

            // ── Update textboxes ─────────────────────────────────────────────
            txtSplitCash.Text = (_activeSplit == "cash" && _numpadBuffer.Length > 0)
                ? _numpadBuffer : (cash > 0 ? cash.ToString("F2") : "");
            txtSplitUpi.Text = (_activeSplit == "upi" && _numpadBuffer.Length > 0)
                ? _numpadBuffer : (upi > 0 ? upi.ToString("F2") : "");
            txtSplitCard.Text = (_activeSplit == "card" && _numpadBuffer.Length > 0)
                ? _numpadBuffer : (card > 0 ? card.ToString("F2") : "");

            // ... rest of the method stays exactly as it was ...

            // ── Textbox colors ─────────────────────────────────────────────────
            txtSplitCash.ForeColor = cash > 0 ? AccGreen : Color.FromArgb(70, 80, 90);
            txtSplitUpi.ForeColor = upi > 0 ? AccBlue : Color.FromArgb(70, 80, 90);
            txtSplitCard.ForeColor = card > 0 ? AccPurple : Color.FromArgb(70, 80, 90);

            // ── Panel highlight colors ─────────────────────────────────────────
            panelSplitCash.BackColor = _activeSplit == "cash"
                ? (_numpadBuffer.Length > 0 ? Color.FromArgb(20, 80, 40) : Color.FromArgb(30, 60, 45))
                : PanelDark2;
            panelSplitUpi.BackColor = _activeSplit == "upi"
                ? (_numpadBuffer.Length > 0 ? Color.FromArgb(20, 50, 100) : Color.FromArgb(25, 45, 80))
                : PanelDark2;
            panelSplitCard.BackColor = _activeSplit == "card"
                ? (_numpadBuffer.Length > 0 ? Color.FromArgb(70, 30, 110) : Color.FromArgb(55, 25, 90))
                : PanelDark2;

            // ── Title colors ───────────────────────────────────────────────────
            lblCashTitle.ForeColor = _activeSplit == "cash" ? AccGreen : TextMuted;
            lblUpiTitle.ForeColor = _activeSplit == "upi" ? AccBlue : TextMuted;
            lblCardTitle.ForeColor = _activeSplit == "card" ? AccPurple : TextMuted;

            // ── Lock panels that are already filled and total is met ───────────
            bool isPaid = totalEntered >= grand - 0.001m;

            panelSplitCash.Enabled = !isPaid || _activeSplit == "cash" || cash > 0;
            panelSplitUpi.Enabled = !isPaid || _activeSplit == "upi" || upi > 0;
            panelSplitCard.Enabled = !isPaid || _activeSplit == "card" || card > 0;

            if (isPaid)
            {
                if (cash == 0) panelSplitCash.BackColor = Color.FromArgb(28, 30, 38);
                if (upi == 0) panelSplitUpi.BackColor = Color.FromArgb(28, 30, 38);
                if (card == 0) panelSplitCard.BackColor = Color.FromArgb(28, 30, 38);
            }

            // ── Build balance display ──────────────────────────────────────────
            // ── Only show what's been entered per method — no remaining/change text here ──
            var parts = new List<string>();
            if (cash > 0) parts.Add($"💵 Cash {Fmt(cash)}");
            if (upi > 0) parts.Add($"🏦 Bank {Fmt(upi)}");
            if (card > 0) parts.Add($"💳 Card {Fmt(card)}");

            lblSplitBalance.Text = parts.Count == 0 ? "" : string.Join("  +  ", parts) + $"  =  {Fmt(totalEntered)}";
            lblSplitBalance.ForeColor = TextMuted;
            lblSplitBalance.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblSplitBalance.Height = 44;

            if (lblSplitBalance.Parent != null)
            {
                int availW = lblSplitBalance.Parent.ClientSize.Width
                             - lblSplitBalance.Parent.Padding.Left
                             - lblSplitBalance.Parent.Padding.Right
                             - 20;
                lblSplitBalance.Width = Math.Max(300, availW);
            }
        }


        private void btnSplitExact_Click(object sender, EventArgs e)
        {
            decimal grand = GrandTotal();
            decimal others = _activeSplit == "cash" ? _splitUpi + _splitCard
                           : _activeSplit == "upi" ? _splitCash + _splitCard
                           : _splitCash + _splitUpi;
            decimal fill = Math.Max(0, grand - others);
            _numpadBuffer = fill.ToString("F0");
            if (_activeSplit == "cash") _splitCash = fill;
            else if (_activeSplit == "upi") _splitUpi = fill;
            else if (_activeSplit == "card") _splitCard = fill;
            UpdateSplitDisplay();
            UpdateGrandTotalBigDisplay();
        }


        private void ApplyNumpad()
        {
            decimal.TryParse(_numpadBuffer, out decimal val);
            if (_activeSplit == "cash") _splitCash = val;
            else if (_activeSplit == "upi") _splitUpi = val;
            else if (_activeSplit == "card") _splitCard = val;
            UpdateSplitDisplay();
            UpdateGrandTotalBigDisplay();
        }
        // Fire-and-forget wrapper so existing callers that expect a void method still compile
        // Fire-and-forget wrapper so existing callers that expect a void method still compile
        public void SaveAsPendingInvoice() => _ = SaveAsPendingInvoiceAsync();

        public async Task<(bool Success, string InvoiceNo, decimal Grand)> SaveAsPendingInvoiceAsync()
        {
            if (_cart.Count == 0) { ShowStatus("Cart is empty.", false); return (false, null, 0m); }

            decimal grand = GrandTotal();

            // Create the Sales Order on the server — this IS the pending record now.
            // No local copy is saved; PendingInvoicesForm reads SO rows live from the API.
            string customer = string.IsNullOrWhiteSpace(_customerNameValue) ? DEFAULT_CUSTOMER_NAME : _customerNameValue;
            int customerId = _selectedCustomer?.CustomerID ?? 0;

            var soResult = await CreateSalesOrderFromCartAsync(
                SalesRepository.NextInvoiceNo(), customer, customerId, grand).ConfigureAwait(true);
            if (!soResult.Success)
            {
                ShowStatus("⚠ Failed to save Sales Order. Please check connection and try again.", false);
                return (false, null, 0m);   // cart stays intact — nothing was persisted anywhere
            }

            string invoiceNo = !string.IsNullOrWhiteSpace(soResult.SoNumber)
                ? soResult.SoNumber
                : SalesRepository.NextInvoiceNo();

            AddToRecentSales(invoiceNo, grand);
            ShowStatus($"✓ Sales Order saved: {invoiceNo}", true);

            ResetSale();

            return (true, invoiceNo, grand);
        }

        // ── Builds the payload and posts to /api/SalesOrder ─────────────────────────
        private async Task<(bool Success, int? SoId, string? SoNumber)> CreateSalesOrderFromCartAsync(
           string invoiceNo, string customerName, int customerId, decimal grandTotal)
        {
            try
            {
                if (customerId <= 0)
                    customerId = await SalesOrderApi.GetCustomerIdByNameAsync(customerName).ConfigureAwait(true);

                var idByName = await SalesOrderApi.GetItemNameMapAsync().ConfigureAwait(true);

                var nameToId = idByName
                    .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

                var lines = new List<CreateSOLinePayload>();
                decimal totalLineTax = 0m;

                // ── NEW: collect problems instead of silently sending bad lines ──
                var invalidItems = new List<string>();

                foreach (var item in _cart)
                {
                    // ── NEW: item name itself missing/blank — can't even attempt lookup ──
                    if (string.IsNullOrWhiteSpace(item.Name))
                    {
                        invalidItems.Add($"(blank item name, barcode '{item.Barcode}')");
                        continue;
                    }

                    int itemId;
                    if (!nameToId.TryGetValue(item.Name, out itemId))
                        int.TryParse(item.Barcode, out itemId);

                    // ── NEW: neither the name map nor the barcode fallback resolved an ID ──
                    if (itemId <= 0)
                    {
                        invalidItems.Add(item.Name);
                        continue;
                    }

                    decimal lineTax = _taxAlreadyIncluded ? 0m : item.TaxAmt;
                    totalLineTax += lineTax;

                    int resolvedUom = item.UOM > 0 ? item.UOM : 1;
                    decimal unitsPerPack = GetUnitsPerPackForCartItem(item);
                    decimal qtyInBaseUnits = item.Qty * unitsPerPack;

                    lines.Add(new CreateSOLinePayload
                    {
                        ItemId = itemId,
                        Qty = qtyInBaseUnits,
                        UOM = resolvedUom,
                        UnitPrice = item.Price,
                        DiscountPercent = item.DiscountPct,
                        DiscountAmount = item.DiscountAmt,
                        TaxID = item.TaxId,                 // ← FIXED
                        TaxPercentage = item.TaxPercentage, // ← FIXED
                        Charges = item.Charges,             // ← wired
                        Tax = lineTax,
                        Total = item.Total
                    });
                }

                // ── NEW: abort before hitting the API if any line couldn't be resolved ──
                if (invalidItems.Count > 0)
                {
                    string msg = "⛔ Cannot create Sales Order — Item ID/Name missing for: "
                               + string.Join(", ", invalidItems);
                    Debug.WriteLine("CreateSalesOrderFromCartAsync: " + msg);
                    ShowStatus(msg, false);
                    return (false, null, null);
                }

                // ── NEW: guard against an empty cart producing a line-less SO ──
                if (lines.Count == 0)
                {
                    ShowStatus("⛔ Cannot create Sales Order — no valid items to save.", false);
                    return (false, null, null);
                }

                var payload = new CreateSalesOrderPayload
                {
                    CompanyID = _companyId,
                    StoreID = _storeId,
                    CustomerId = customerId,
                    PaymentTermID = 0,
                    SOAmount = (int)Math.Round(grandTotal, MidpointRounding.AwayFromZero),
                    Currency = string.IsNullOrWhiteSpace(_currencySymbol) ? "P" : _currencySymbol,
                    SOType = "Sales Order",
                    SODiscountAmt = _cart.Sum(i => i.DiscountAmt),
                    SOTax = totalLineTax,
                    SOCharges = _charges.Sum(c => c.Amount),
                    CurrencyID = _currencyId,
                    SODiscountID = 0,
                    SOTaxID = _selectedTax?.TaxId ?? 0,
                    DeliveryAddress = string.IsNullOrWhiteSpace(_customerAddressValue) ? "N/A" : _customerAddressValue,
                    DeliveryDate = DateTime.Now,
                    Status = "Confirm",
                    WinPos="Y",
                    Lines = lines,
                    Charges = _charges.Select(c => new CreateSOChargePayload
                    {
                        ChargesID = c.ChargesID,
                        Amount = c.Amount,
                        CurrencyID = _currencyId,
                        Type = c.Type,
                        ApplyTo = 2
                    }).ToList()
                };
                _lastOrderQueuedOffline = false;
                var result = await SalesOrderApi.CreateSalesOrderAsync(payload).ConfigureAwait(true);

                if (!result.Success)
                {
                    Debug.WriteLine($"CreateSalesOrderFromCartAsync: API call failed for {invoiceNo} — queuing offline.");
                    bool queued = await QueueOfflineSalesOrderAsync(invoiceNo, payload).ConfigureAwait(true);
                    if (queued)
                    {
                        _lastOrderQueuedOffline = true;
                        ShowStatus($"📴 Offline — Sales Order {invoiceNo} queued, will sync automatically.", true);
                        return (true, null, invoiceNo);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("CreateSalesOrderFromCartAsync: " + ex.Message);
                return (false, null, null);
            }
        }

        // ── Mirrors processSaleStock() in SalesOrderEntry.jsx — reduces server-side stock ──
        // Prefer injecting IHttpClientFactory or using a static/shared client
        private static readonly HttpClient SharedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60) // adjust as needed
        };

        private async Task<bool> ProcessSaleStockAsync(int itemId, int companyId, decimal saleQty, string refKey)
        {
            try
            {
                var payload = new
                {
                    ItemID = itemId,
                    CompanyID = companyId,
                    SaleQty = saleQty,
                    RequestRef = refKey   // NEW — stable per invoice+line
                };

                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Optional: pass a CancellationToken if the caller has one
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                var resp = await SharedHttp.PostAsync(
                    $"{ApiBaseUrl}/api/stock/sale",
                    content,
                    cts.Token).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.WriteLine($"ProcessSaleStockAsync failed for item {itemId}: {(int)resp.StatusCode} {err}");
                    return false;
                }

                return true;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                Debug.WriteLine($"ProcessSaleStockAsync timed out for item {itemId}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessSaleStockAsync: {ex}");
                return false;
            }
        }

        // ── Mirrors getUnitsPerPackForLine() in the React PO screen — resolves the
        //    base-unit conversion factor for a cart line's selected UOM. Base UOM = 1,
        //    any pack UOM (Case/Box/etc.) = its UnitsPerPack value. ──
        private decimal GetUnitsPerPackForCartItem(CartItem item)
        {
            if (item?.AvailableUOMs == null || item.AvailableUOMs.Count == 0) return 1m;

            var match = item.AvailableUOMs.FirstOrDefault(u => u.UomId == item.UOM);
            return (match != null && match.UnitsPerPack > 0) ? match.UnitsPerPack : 1m;
        }

        // ── Same lookup, but for offline-queued payload lines where we only have
        //    ItemId + UOM (no CartItem) — resolves via the loaded catalog instead. ──
        private decimal GetUnitsPerPackForItemUom(int itemId, int uom)
        {
            if (itemId <= 0) return 1m;

            var prod = _catalog?.FirstOrDefault(p => p.ItemId == itemId);
            if (prod?.AvailableUOMs == null || prod.AvailableUOMs.Count == 0) return 1m;

            var match = prod.AvailableUOMs.FirstOrDefault(u => u.UomId == uom);
            return (match != null && match.UnitsPerPack > 0) ? match.UnitsPerPack : 1m;
        }
        // ══════════════════════════════════════════════════════════════════════
        //  TENDER — WITH FLOAT VALIDATION + PENDING INVOICE MARK-PAID
        // ══════════════════════════════════════════════════════════════════════
        // ══════════════════════════════════════════════════════════════════════
        //  TENDER — WITH FLOAT VALIDATION + SALES ORDER CREATION
        // ══════════════════════════════════════════════════════════════════════

        // ── Shared helper — creates + confirms an SOInvoice for a given SalesOrderApiRow ──
        // ── Shared helper — creates + confirms an SOInvoice for a given SalesOrderApiRow ──
        private async Task<(bool Success, int? InvoiceId)> CreateAndConfirmSOInvoiceAsync(SalesOrderApiRow so)
        {
            if (so == null || string.IsNullOrWhiteSpace(so.SONumber)) return (false, null);

            var (newInvoiceId, soInvoiceError) = await SalesOrderApi.CreateSOInvoiceFromSalesOrderAsync(so);
            if (newInvoiceId == null)
            {
                Debug.WriteLine($"CreateAndConfirmSOInvoiceAsync: SO Invoice creation failed for {so.SONumber} — {soInvoiceError}");
                return (false, null);
            }

            SalesOrderApi.InvoicedSoNumbers.Add(so.SONumber);

            bool confirmed = await SalesOrderApi.ConfirmSOInvoiceAsync(newInvoiceId.Value).ConfigureAwait(true);
            if (!confirmed)
            {
                Debug.WriteLine($"CreateAndConfirmSOInvoiceAsync: SO Invoice #{newInvoiceId} created but confirm failed for {so.SONumber}.");
                return (false, newInvoiceId);
            }

            ShowStatus($"✓ SO Invoice #{newInvoiceId} posted.", true);
            return (true, newInvoiceId);
        }
        private async Task<bool> ShowPaymentCollectionDialogAsync(decimal grandTotal)
        {
            bool confirmed = false;
            decimal localCash = 0m, localUpi = 0m, localCard = 0m;
            string localActive = "cash";
            SalesOrderApi.BankAccountDto localBankAccount = null;
            if (_bankAccounts == null || _bankAccounts.Count == 0)
                _bankAccounts = await SalesOrderApi.GetAllBanksAsync(_companyId).ConfigureAwait(true);

            const int W = 420;
            const int MARGIN = 24;
            const int CONTENT_W = W - MARGIN * 2;

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(22, 26, 36),
                KeyPreview = true,
                ShowInTaskbar = false
            };

            // ── Header ───────────────────────────────────────────────────────────
            var pnlHead = new Panel { BackColor = Color.FromArgb(38, 42, 54), Size = new Size(W, 56), Location = Point.Empty };
            pnlHead.Controls.Add(new Label
            {
                Text = "Collect Payment",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(320, 56),
                Location = new Point(MARGIN, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(44, 56),
                Location = new Point(W - 44, 0),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 60, 78);
            btnX.Click += (s, ev) => dlg.Close();
            pnlHead.Controls.Add(btnX);
            dlg.Controls.Add(pnlHead);

            var headerRule = new Panel { BackColor = Color.FromArgb(50, 55, 70), Size = new Size(W, 1), Location = new Point(0, 56) };
            dlg.Controls.Add(headerRule);

            // ── Sequential layout cursor ─────────────────────────────────────────
            int y = 57;

            // ── Amount due ───────────────────────────────────────────────────────
            y += 20;
            dlg.Controls.Add(new Label
            {
                Text = "AMOUNT DUE",
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false, 
                Size = new Size(W, 16), 
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter
            });
            y += 20;

            var lblGrandDue = new Label
            {
                Text = Fmt(grandTotal),
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = AccGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(W, 56),          // increased from 48 → no more cropping
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter
            };
            dlg.Controls.Add(lblGrandDue);
            y = lblGrandDue.Bottom + 20;

            // ── Method tiles ─────────────────────────────────────────────────────
            const int TILE_H = 68;
            const int TILE_GAP = 12;
            int tileW = (CONTENT_W - TILE_GAP * 2) / 3;
            int tileY = y;

            Panel MakeTile(string icon, string label, Color accent, int x)
            {
                var t = new Panel
                {
                    Size = new Size(tileW, TILE_H),
                    Location = new Point(x, tileY),
                    BackColor = Color.FromArgb(32, 36, 48),
                    Cursor = Cursors.Hand
                };
                t.Region = MakeRoundedRegion(t.Size, 10);

                var lblIcon = new Label
                {
                    Text = icon,
                    Font = new Font("Segoe UI Emoji", 15F),
                    ForeColor = accent,
                    BackColor = Color.Transparent,
                    Size = new Size(tileW, 34),
                    Location = new Point(0, 8),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                var lblLbl = new Label
                {
                    Text = label,
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    Size = new Size(tileW, 20),
                    Location = new Point(0, 42),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                t.Controls.Add(lblIcon);
                t.Controls.Add(lblLbl);
                t.Tag = new object[] { lblIcon, lblLbl, accent };
                return t;
            }

            var tileCash = MakeTile("💵", "CASH", AccGreen, MARGIN);
            var tileUpi = MakeTile("🏦", "BANK", AccBlue, MARGIN + tileW + TILE_GAP);
            var tileCard = MakeTile("💳", "CARD", AccPurple, MARGIN + (tileW + TILE_GAP) * 2);
            dlg.Controls.AddRange(new Control[] { tileCash, tileUpi, tileCard });
            y = tileY + TILE_H + 20;

            // ── Bank account (reserved fixed slot) ───────────────────────────────
            int bankBlockTop = y;
            var lblBankCaption = new Label
            {
                Text = "Bank Account",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(CONTENT_W, 16),
                Location = new Point(MARGIN, bankBlockTop),
                Visible = false
            };
            var cmbBank = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(CONTENT_W, 32),
                Location = new Point(MARGIN, bankBlockTop + 18),
                Visible = false
            };
            foreach (var b in _bankAccounts) cmbBank.Items.Add(b.BankName);
            cmbBank.SelectedIndexChanged += (s, ev) =>
            {
                if (cmbBank.SelectedIndex >= 0 && cmbBank.SelectedIndex < _bankAccounts.Count)
                    localBankAccount = _bankAccounts[cmbBank.SelectedIndex];
            };
            dlg.Controls.Add(lblBankCaption);
            dlg.Controls.Add(cmbBank);

            const int BANK_BLOCK_H = 58;
            y = bankBlockTop + BANK_BLOCK_H;

            // ── Amount input ─────────────────────────────────────────────────────
            // ── Amount input ─────────────────────────────────────────────────────
            var lblAmountCaption = new Label
            {
                Text = "Amount",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(CONTENT_W, 16),
                Location = new Point(MARGIN, y)
            };
            dlg.Controls.Add(lblAmountCaption);
            y += 18;

            var amountWrap = new Panel
            {
                Size = new Size(CONTENT_W, 46),
                Location = new Point(MARGIN, y),
                BackColor = Color.Transparent
            };
            // NO Region – we paint everything ourselves

            var lblCurrency = new Label
            {
                Text = _currencySymbol,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Size = new Size(36, 46),
                Location = new Point(10, 0),          // inset from left edge
                TextAlign = ContentAlignment.MiddleCenter
            };

            var txtAmount = new TextBox
            {
                Font = new Font("Consolas", 17F, FontStyle.Bold),
                ForeColor = AccGreen,
                BackColor = Color.FromArgb(30, 34, 46),
                BorderStyle = BorderStyle.None,
                Size = new Size(CONTENT_W - 62, 34),  // leave room for border + currency
                Location = new Point(48, 6),          // inset from left + top
                TextAlign = System.Windows.Forms.HorizontalAlignment.Right
            };

            amountWrap.Controls.Add(lblCurrency);
            amountWrap.Controls.Add(txtAmount);

           // Color amountBorderColor = AccGreen;

            amountWrap.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var rect = new Rectangle(1, 1, amountWrap.Width - 3, amountWrap.Height - 3);

                // 1. Fill rounded background
                using (var bgPath = RoundedPath(rect, 8))
                using (var bgBrush = new SolidBrush(Color.FromArgb(30, 34, 46)))
                    pe.Graphics.FillPath(bgBrush, bgPath);

                // 2. Draw accent border (thicker when focused)
               // float borderWidth = txtAmount.Focused ? 2.5f : 1.8f;
               //// using var pen = new Pen(txtAmount.Focused ? amountBorderColor : Color.FromArgb(55, 60, 76), borderWidth);
               // using var borderPath = RoundedPath(rect, 8);
               // pe.Graphics.DrawPath(pen, borderPath);
            };

            txtAmount.Enter += (s, e) => amountWrap.Invalidate();
            txtAmount.Leave += (s, e) => amountWrap.Invalidate();

            dlg.Controls.Add(amountWrap);
            y = amountWrap.Bottom + 16;

            // ── Fill Exact Remaining ─────────────────────────────────────────────
            var btnExact = new Panel
            {
                Size = new Size(CONTENT_W, 34),
                Location = new Point(MARGIN, y),
                BackColor = Color.FromArgb(28, 31, 41),
                Cursor = Cursors.Hand
            };
            btnExact.Region = MakeRoundedRegion(btnExact.Size, 8);
            btnExact.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(62, 68, 86), 1f);
                using var path = RoundedPath(new Rectangle(0, 0, btnExact.Width - 1, btnExact.Height - 1), 8);
                pe.Graphics.DrawPath(pen, path);
            };
            var lblExact = new Label
            {
                Text = "Fill Exact Remaining",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnExact.Controls.Add(lblExact);
            btnExact.MouseEnter += (s, e) => { btnExact.BackColor = Color.FromArgb(36, 40, 52); };
            btnExact.MouseLeave += (s, e) => { btnExact.BackColor = Color.FromArgb(28, 31, 41); };
            dlg.Controls.Add(btnExact);
            y = btnExact.Bottom + 16;

            // ── Balance pill ─────────────────────────────────────────────────────
            var lblBalance = new Label
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(40, 34, 20),
                AutoSize = false,
                Size = new Size(CONTENT_W, 34),
                Location = new Point(MARGIN, y),
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblBalance.Region = MakeRoundedRegion(lblBalance.Size, 8);
            dlg.Controls.Add(lblBalance);
            y = lblBalance.Bottom + 18;

            // ── Confirm ──────────────────────────────────────────────────────────
            var btnConfirm = new Button
            {
                Text = "Confirm Payment",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(CONTENT_W, 46),
                Location = new Point(MARGIN, y),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnConfirm.FlatAppearance.BorderSize = 0;
            btnConfirm.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(AccGreen, 0.08f);
            btnConfirm.Region = MakeRoundedRegion(btnConfirm.Size, 10);
            dlg.Controls.Add(btnConfirm);
            y = btnConfirm.Bottom;

            dlg.ClientSize = new Size(W, y + MARGIN);
            dlg.Region = MakeRoundedRegion(dlg.ClientSize, 16);

            // ── Local logic ──────────────────────────────────────────────────────
            bool _suppressTextChanged = false;

            void RefreshTileVisuals()
            {
                foreach (var t in new[] { tileCash, tileUpi, tileCard })
                {
                    var arr = (object[])t.Tag;
                    var lblIcon = (Label)arr[0];
                    var lblLbl = (Label)arr[1];
                    var accent = (Color)arr[2];
                    bool active = (t == tileCash && localActive == "cash")
                               || (t == tileUpi && localActive == "upi")
                               || (t == tileCard && localActive == "card");
                    t.BackColor = active ? accent : Color.FromArgb(32, 36, 48);
                    lblIcon.ForeColor = active ? Color.White : accent;
                    lblLbl.ForeColor = active ? Color.White : TextMuted;
                }

                bool isBank = localActive == "upi";
                lblBankCaption.Visible = isBank;
                cmbBank.Visible = isBank;

                //amountBorderColor = localActive == "cash" ? AccGreen
                //                  : localActive == "upi" ? AccBlue
                //                  : AccPurple;
                //txtAmount.ForeColor = amountBorderColor;
                amountWrap.Invalidate();
            }

            void LoadAmountIntoTextbox()
            {
                decimal shown = localActive == "cash" ? localCash
                              : localActive == "upi" ? localUpi
                              : localCard;
                _suppressTextChanged = true;
                txtAmount.Text = shown > 0 ? shown.ToString("F2") : "";
                _suppressTextChanged = false;
            }

            void RefreshBalance()
            {
                decimal total = localCash + localUpi + localCard;
                decimal remaining = grandTotal - total;

                if (remaining > 0.001m)
                {
                    lblBalance.Text = $"Remaining {Fmt(remaining)}";
                    lblBalance.ForeColor = AccOrange;
                    lblBalance.BackColor = Color.FromArgb(50, 38, 18);
                    btnConfirm.Enabled = false;
                }
                else if (remaining < -0.001m)
                {
                    lblBalance.Text = $"Change {Fmt(-remaining)}";
                    lblBalance.ForeColor = AccGreen;
                    lblBalance.BackColor = Color.FromArgb(18, 55, 35);
                    btnConfirm.Enabled = true;
                }
                else
                {
                    lblBalance.Text = "✓ Fully Paid";
                    lblBalance.ForeColor = AccGreen;
                    lblBalance.BackColor = Color.FromArgb(18, 55, 35);
                    btnConfirm.Enabled = true;
                }
            }

            void SelectMethod(string method)
            {
                localActive = method;
                RefreshTileVisuals();
                LoadAmountIntoTextbox();
                txtAmount.Focus();
                txtAmount.SelectAll();
            }

            void CommitTypedAmount()
            {
                decimal.TryParse(txtAmount.Text, out decimal typed);
                if (typed < 0) typed = 0;
                if (localActive == "cash") localCash = typed;
                else if (localActive == "upi") localUpi = typed;
                else localCard = typed;
                RefreshBalance();
            }

            EventHandler tileCashClick = (s, e) => SelectMethod("cash");
            EventHandler tileUpiClick = (s, e) => SelectMethod("upi");
            EventHandler tileCardClick = (s, e) => SelectMethod("card");

            foreach (Control c in tileCash.Controls) c.Click += tileCashClick;
            tileCash.Click += tileCashClick;
            foreach (Control c in tileUpi.Controls) c.Click += tileUpiClick;
            tileUpi.Click += tileUpiClick;
            foreach (Control c in tileCard.Controls) c.Click += tileCardClick;
            tileCard.Click += tileCardClick;

            txtAmount.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                if (char.IsDigit(e.KeyChar)) return;
                if (e.KeyChar == '.' && !txtAmount.Text.Contains(".")) return;
                e.Handled = true;
            };

            txtAmount.TextChanged += (s, e) =>
            {
                if (_suppressTextChanged) return;
                CommitTypedAmount();
            };

            EventHandler exactClick = (s, e) =>
            {
                decimal others = localActive == "cash" ? localUpi + localCard
                                : localActive == "upi" ? localCash + localCard
                                : localCash + localUpi;
                decimal fill = Math.Max(0m, grandTotal - others);
                _suppressTextChanged = true;
                txtAmount.Text = fill.ToString("F2");
                _suppressTextChanged = false;
                CommitTypedAmount();
            };
            btnExact.Click += exactClick;
            lblExact.Click += exactClick;

            btnConfirm.Click += (s, e) =>
            {
                if (localUpi > 0 && localBankAccount == null && _bankAccounts.Count > 0)
                {
                    ShowStatus("⛔ Please select a bank account for the Bank Transfer amount.", false);
                    cmbBank.Focus();
                    return;
                }
                confirmed = true;
                dlg.Close();
            };

            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.Handled = true;
                    dlg.Close();
                }
            };

            txtAmount.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    if (btnConfirm.Enabled) btnConfirm.PerformClick();
                }
            };

            SelectMethod("cash");
            RefreshBalance();
            dlg.Shown += (s, e) => txtAmount.Focus();
            dlg.ShowDialog(this);

            if (confirmed)
            {
                _splitCash = localCash;
                _splitUpi = localUpi;
                _splitCard = localCard;
                _selectedBankAccount = localBankAccount;
                _selectedUpiMethodName = localBankAccount?.BankName ?? "Bank Transfer";
            }
            return confirmed;
        }
        private async void btnTenderSale_Click(object sender, EventArgs e)
        {
            if (_cart.Count == 0) { ShowStatus("Cart is empty.", false); return; }

            if (_isTendering)
            {
                ShowStatus("⏳ Still processing the previous sale — please wait.", false);
                return;
            }
            _isTendering = true;

            if (btnTenderSale != null) btnTenderSale.Enabled = false;
            string originalBtnText = btnTenderSale?.Text ?? "✅  Tender Sale  (F1)";
            if (btnTenderSale != null) btnTenderSale.Text = "⏳  Processing…";
            this.Cursor = Cursors.WaitCursor;

            try
            {
                // ── Final stock validation before tendering ─────────────────────────
                var overStockItems = new List<string>();
                bool hasOutOfStockItems = false;
                foreach (var item in _cart)
                {
                    decimal unitsPerPack = GetUnitsPerPackForCartItem(item);
                    decimal requestedBaseQty = item.Qty * unitsPerPack;

                    decimal available = await GetLiveAvailableStockAsync(
                        item.ItemId, excludeCartItemName: item.Name, excludeUom: item.UOM).ConfigureAwait(true);

                    if (requestedBaseQty > available)
                    {
                        hasOutOfStockItems = true;
                        decimal availableInThisUom = unitsPerPack > 0 ? Math.Floor(available / unitsPerPack) : available;
                        overStockItems.Add($"{item.Name} (have {availableInThisUom:F0} × {item.UOMName}, cart has {item.Qty:F0})");
                    }
                }

                if (overStockItems.Count > 0 && !POSAPP.Printer.StockSettings.AllowOutOfStockSale)
                {
                    MessageBox.Show(
                        "The following items exceed available stock:\n\n" + string.Join("\n", overStockItems) +
                        "\n\nPlease adjust quantities before completing the sale.",
                        "⛔ Insufficient Stock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ShowStatus("⛔ Sale blocked — insufficient stock.", false);
                    return;
                }

                // ── NEW: bank account must be selected if a bank-transfer amount was entered ──
                decimal grandTotal = GrandTotal();

                if (_isCreditSale)
                {
                    // Credit sale — no payment popup, no shift/cash checks.
                    _splitCash = 0m; _splitUpi = 0m; _splitCard = 0m;
                    _selectedBankAccount = null;
                }
                else
                {
                    // Collect payment via the modern popup instead of the old on-screen numpad.
                    bool paymentOk = await ShowPaymentCollectionDialogAsync(grandTotal).ConfigureAwait(true);
                    if (!paymentOk)
                    {
                        ShowStatus("Payment cancelled.", false);
                        return;
                    }

                    if (_splitCash > 0 && !ShiftState.IsOpen)
                    {
                        MessageBox.Show(
                            "No shift is open.\n\nOpen a shift via Float Entry (F8) before processing cash.",
                            "⚠ No Shift Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                decimal splitSum = _isCreditSale ? 0m : _splitCash + _splitUpi + _splitCard;
                decimal change = _isCreditSale ? 0m : splitSum - grandTotal;

                var receiptData = PrepareReceiptData();
                receiptData.IsQuotation = false;

                string originalPendingNo = _currentPendingSourceKey ?? lblInvoiceNo.Text;
                string invNo = SalesRepository.NextInvoiceNo();
                receiptData.InvoiceNo = invNo;

                AddToRecentSales(invNo, grandTotal);

                string customer = string.IsNullOrWhiteSpace(_customerNameValue) ? DEFAULT_CUSTOMER_NAME : _customerNameValue;
                int customerId = _selectedCustomer?.CustomerID ?? 0;

                var soResult = await CreateSalesOrderFromCartAsync(invNo, customer, customerId, grandTotal).ConfigureAwait(true);
                if (!soResult.Success)
                {
                    ShowStatus("⚠ Sale saved locally, but Sales Order could not be created on server. Check connection.", false);
                }
                else if (_lastOrderQueuedOffline)
                {
                    ShowStatus($"📴 Sale completed offline — {invNo} will sync automatically.", true);
                }
                else
                {
                //    var failedStockItems = new List<string>();
                //    var queuedStockItems = new List<string>();
                //    foreach (var item in _cart)
                //    {
                //        if (item.ItemId <= 0)
                //        {
                //            failedStockItems.Add($"{item.Name} (item id not resolved)");
                //            continue;
                //        }

                //        // Reduce by qty × pack size of the sold UOM (e.g. 2 Cases of 12 = 24 units reduced).
                //        decimal unitsPerPack = GetUnitsPerPackForCartItem(item);
                //        decimal qtyToReduce = item.Qty * unitsPerPack;

                //        // in btnTenderSale_Click loop
                //        // ============================================================
                //        // 1. ONLINE SALE - replace your existing stock-processing block
                //        // ============================================================

                //        string refKey = $"{invNo}-{item.ItemId}";

                //        bool ok = await ProcessSaleStockAsync(
                //            item.ItemId,
                //            _companyId,
                //            qtyToReduce,
                //            refKey);

                //        if (!ok)
                //        {
                //            bool queued = await QueueOfflineStockUpdateAsync(
                //                item.ItemId,
                //                _companyId,
                //                qtyToReduce);

                //            if (queued)
                //                queuedStockItems.Add(item.Name);
                //            else
                //                failedStockItems.Add(item.Name);
                //        }
                //    }

                //    if (queuedStockItems.Count > 0)
                //        Debug.WriteLine("Stock updates queued for auto-sync: " + string.Join(", ", queuedStockItems));

                //    if (failedStockItems.Count > 0)
                //    {
                //        ShowStatus("⚠ Stock NOT reduced on server for: " + string.Join(", ", failedStockItems), false);
                //        Debug.WriteLine("Stock reduction failures: " + string.Join(", ", failedStockItems));
                //    }
                }

                try
                {
                    await Task.Run(() =>
                    {
                        SalesRepository.ConsumeInvoiceNo();
                        SalesRepository.SaveSale(receiptData, _companyId);
                    }).ConfigureAwait(true);

                    _ = LoadStockCacheAsync();   // fire-and-forget, non-blocking
                }
                catch (Exception ex) { ShowStatus("Sale save error: " + ex.Message, false); }


                if (PendingSalesOrderCache.Cache.ContainsKey(originalPendingNo))
                    PendingSalesOrderCache.Cache.Remove(originalPendingNo);

                if (soResult.Success && !_lastOrderQueuedOffline && !string.IsNullOrWhiteSpace(soResult.SoNumber) && hasOutOfStockItems)
                {
                    // Only reduce manually here if the SO for out-of-stock sales is created
                    // WITHOUT triggering the server-side reduction (e.g. different Status).
                    // If it's created as "Confirm" like a normal sale, remove this block too —
                    // the server already handled it and this would double-reduce.
                    var failedStockItems = new List<string>();
                    var queuedStockItems = new List<string>();
                    foreach (var item in _cart)
                    {
                        if (item.ItemId <= 0) { failedStockItems.Add($"{item.Name} (item id not resolved)"); continue; }
                        decimal unitsPerPack = GetUnitsPerPackForCartItem(item);
                        decimal qtyToReduce = item.Qty * unitsPerPack;
                        string refKey = $"{invNo}-{item.ItemId}";
                        bool ok = await ProcessSaleStockAsync(item.ItemId, _companyId, qtyToReduce, refKey).ConfigureAwait(true);
                        if (!ok)
                        {
                            bool queued = await QueueOfflineStockUpdateAsync(item.ItemId, _companyId, qtyToReduce).ConfigureAwait(true);
                            if (queued) queuedStockItems.Add(item.Name); else failedStockItems.Add(item.Name);
                        }
                    }
                    if (failedStockItems.Count > 0)
                        ShowStatus("⚠ Stock NOT reduced on server for: " + string.Join(", ", failedStockItems), false);

                    ShowStatus($"📦 Sales Order {soResult.SoNumber} saved — invoice withheld (insufficient stock).", true);
                    try { SalesRepository.MarkInvoicePaid(originalPendingNo); } catch { }
                    try { SalesRepository.MarkInvoicePaid(invNo); } catch { }
                }
                else if (soResult.Success && !_lastOrderQueuedOffline && !string.IsNullOrWhiteSpace(soResult.SoNumber))
                {
                    var freshSo = await SalesOrderApi.GetSalesOrderBySoNumberAsync(soResult.SoNumber).ConfigureAwait(true);
                    int? postedInvoiceId = null;
                    bool invoiced = false;

                    if (freshSo != null)
                    {
                        var invoiceResult = await CreateAndConfirmSOInvoiceAsync(freshSo).ConfigureAwait(true);
                        invoiced = invoiceResult.Success;
                        postedInvoiceId = invoiceResult.InvoiceId;
                    }

                    if (!invoiced)
                    {
                        bool queued = await QueueOfflineSOInvoiceAsync(_companyId, soResult.SoNumber).ConfigureAwait(true);
                        ShowStatus(queued
                            ? $"📴 SO Invoice for {soResult.SoNumber} queued — will auto-post shortly."
                            : $"⚠ Could not create or queue SO Invoice for {soResult.SoNumber}. Create it manually.", queued);
                    }

                    // ── UPDATED: handle Cash and/or Bank Transfer, bind real BankAccountID ──
                    // ── UPDATED: handle Cash and/or Bank Transfer, bind real BankAccountID ──
                    // Credit sales never post a Customer Payment — invoice stays outstanding on account.
                    if (!_isCreditSale && invoiced && (_splitCash > 0 || _splitUpi > 0))
                    {
                        int? bankAccountId = _splitUpi > 0 ? _selectedBankAccount?.BankAccountID : (int?)null;

                        var savePayload = new SalesOrderApi.SaveCustomerPaymentPayload
                        {
                            CompanyId = _companyId,
                            CustomerId = customerId,
                            PaymentDate = DateTime.Now,
                            PaymentMethod = _splitUpi > 0 ? "Bank Transfer" : "Cash",
                            BankAccountId = bankAccountId ?? 0,
                            ReferenceNo = _splitUpi > 0 ? (_selectedBankAccount?.AccountNumber ?? "") : "",
                            CurrencyCode = _currencySymbol,
                            ExchangeRate = 1m,
                            Description = "POS Sale",
                            Comments = $"Auto-settled from POS invoice {soResult.SoNumber}",
                            PaymentStatus = "Draft",
                            CreatedBy = CurrentUser.UserInfo.UserID,
                            Settlements = new List<SalesOrderApi.CustomerPaymentSettlementDto>
                    {
                        new SalesOrderApi.CustomerPaymentSettlementDto
                        {
                            InvoiceID = postedInvoiceId ?? 0,
                            InvoiceNo = soResult.SoNumber,
                            InvoiceAmount = grandTotal,
                            AmountToSettle = _splitCash + _splitUpi,
                            RetentionAmount = 0m,
                            DiscountAmount = 0m,
                            WhtAmount = 0m
                        }
                    }
                        };

                        try
                        {
                            var saveResult = await SalesOrderApi.SaveCustomerPaymentAsync(savePayload).ConfigureAwait(true);
                            if (saveResult.Success && saveResult.PaymentId.HasValue)
                            {
                                bool posted = await SalesOrderApi.PostCustomerPaymentAsync(
                                    saveResult.PaymentId.Value, CurrentUser.UserInfo.UserID, bankAccountId).ConfigureAwait(true);

                                if (posted)
                                {
                                    ShowStatus($"✓ {savePayload.PaymentMethod} payment {saveResult.PaymentNo} posted against {soResult.SoNumber}.", true);
                                }
                                else
                                {
                                    // Saved but couldn't post — queue so it retries the post step only.
                                    bool queued = await QueueOfflineCustomerPaymentAsync(
                                        _companyId, soResult.SoNumber, savePayload, CurrentUser.UserInfo.UserID, bankAccountId).ConfigureAwait(true);
                                    ShowStatus(queued
                                        ? $"📴 {savePayload.PaymentMethod} payment saved but posting failed for {soResult.SoNumber} — queued to retry."
                                        : $"⚠ {savePayload.PaymentMethod} payment saved but posting failed for {soResult.SoNumber}.", queued);
                                }
                            }
                            else
                            {
                                // Couldn't even save — queue full save+post.
                                bool queued = await QueueOfflineCustomerPaymentAsync(
                                    _companyId, soResult.SoNumber, savePayload, CurrentUser.UserInfo.UserID, bankAccountId).ConfigureAwait(true);
                                ShowStatus(queued
                                    ? $"📴 Offline — {savePayload.PaymentMethod} payment for {soResult.SoNumber} queued, will sync automatically."
                                    : $"⚠ Could not save or queue {savePayload.PaymentMethod} payment for {soResult.SoNumber}.", queued);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Auto payment: " + ex.Message);
                            bool queued = await QueueOfflineCustomerPaymentAsync(
                                _companyId, soResult.SoNumber, savePayload, CurrentUser.UserInfo.UserID, bankAccountId).ConfigureAwait(true);
                            ShowStatus(queued
                                ? $"📴 Offline — {savePayload.PaymentMethod} payment for {soResult.SoNumber} queued, will sync automatically."
                                : "⚠ Could not auto-post payment. Settle manually in AR.", queued);
                        }
                    }

                    try { SalesRepository.MarkInvoicePaid(originalPendingNo); } catch { }
                    try { SalesRepository.MarkInvoicePaid(invNo); } catch { }

                }
                else
                {
                    try { SalesRepository.MarkInvoicePaid(originalPendingNo); } catch { }
                    try { SalesRepository.MarkInvoicePaid(invNo); } catch { }
                }


                ShiftState.RecordSale(_splitCash, change > 0 ? change : 0m, _splitUpi, _splitCard);
                RefreshFloatLabel();

                _wasCompletedFromPendingInvoice = _isPendingInvoiceMode;
                string postedInvoiceNo = !string.IsNullOrWhiteSpace(soResult.SoNumber) ? soResult.SoNumber : invNo;
                receiptData.InvoiceNo = postedInvoiceNo;

                _lastReceiptData = receiptData;
                _lastSaleWasPrinted = false;

                bool wantsPrint = ShowPrintConfirmDialog(postedInvoiceNo, grandTotal);

                if (wantsPrint)
                {
                    PrintReceiptDialog.Show(this, receiptData);
                    _lastSaleWasPrinted = PrintReceiptDialog.LastPrintWasSuccessful;
                    if (_lastSaleWasPrinted && CashDrawer.IsAvailable())
                    {
                        _ = Task.Run(() =>
                        {
                            var (drawerOk, drawerMsg) = CashDrawer.OpenAuto();
                            Debug.WriteLine(drawerOk ? "CashDrawer: " + drawerMsg : "CashDrawer FAILED: " + drawerMsg);
                            if (!drawerOk)
                                this.BeginInvoke(new Action(() => ShowStatus("⚠ " + drawerMsg, false)));
                        });
                    }
                    else if (_lastSaleWasPrinted)
                    {
                        Debug.WriteLine("CashDrawer: no drawer detected — skipping auto-open.");
                    }
                }
                else
                {
                    _lastSaleWasPrinted = false;
                    ShowStatus($"✓ Sale {postedInvoiceNo} saved — not printed.", true);
                }

                ResetSale(generateNewInvoiceNo: true);
                DashboardEventBus.Notify();
                this.Close();
            }
            finally
            {
                this.Cursor = Cursors.Default;
                _isTendering = false;
                if (btnTenderSale != null && !btnTenderSale.IsDisposed)
                {
                    btnTenderSale.Enabled = true;
                    btnTenderSale.Text = originalBtnText;
                }
            }

        }



        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            //// Offer reprint if last receipt was never printed
            //if (_lastReceiptData != null && !_lastSaleWasPrinted)
            //{
            //{
            //    var result = MessageBox.Show(
            //        $"Invoice {_lastReceiptData.InvoiceNo} was not printed.\n\nPrint before closing?",
            //        "⚠  Unprinted Receipt",
            //        MessageBoxButtons.YesNoCancel,
            //        MessageBoxIcon.Warning);

            //    if (result == DialogResult.Cancel)
            //    {
            //        e.Cancel = true;   // stay open
            //        return;
            //    }

            //    if (result == DialogResult.Yes)
            //        PrintReceiptDialog.Show(this, _lastReceiptData);
            //    // No = close without printing
            //}

            _scheduler?.Dispose();
            _hotItemsTooltip?.Dispose();
            base.OnFormClosing(e);
            _productSyncTimer?.Stop(); _productSyncTimer?.Dispose();
            _offlineOrderSyncTimer?.Stop(); _offlineOrderSyncTimer?.Dispose();
            _stockSyncTimer?.Stop(); _stockSyncTimer?.Dispose();
            _resizeDebounce?.Stop();
            _resizeDebounce?.Dispose();
            _resizeDebounce = null;
        }
        private void ShowReprintDialog()
        {
            if (_lastReceiptData == null) return;

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(22, 26, 36),
                ClientSize = new Size(440, 320),
                ShowInTaskbar = false,
                KeyPreview = true
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 16);

            // ── Top accent bar (orange = warning) ─────────────────────────
            dlg.Controls.Add(new Panel
            {
                Size = new Size(440, 5),
                Location = Point.Empty,
                BackColor = AccOrange
            });

            // ── Warning icon circle ────────────────────────────────────────
            var iconPanel = new Panel
            {
                Size = new Size(64, 64),
                Location = new Point((440 - 64) / 2, 24),
                BackColor = Color.FromArgb(60, 45, 18)
            };
            iconPanel.Region = MakeRoundedRegion(iconPanel.Size, 32);
            iconPanel.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var f = new Font("Segoe UI Emoji", 24F);
                using var br = new SolidBrush(AccOrange);
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                pe.Graphics.DrawString("🖨", f, br,
                    new RectangleF(0, 0, iconPanel.Width, iconPanel.Height), sf);
            };
            dlg.Controls.Add(iconPanel);

            // ── Title ──────────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "Reprint Last Receipt?",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 245, 255),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(440, 32),
                Location = new Point(0, 100),
                TextAlign = ContentAlignment.MiddleCenter
            });
            dlg.Controls.Add(new Label
            {
                Text = "This invoice was not printed after the last sale.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(120, 130, 155),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(440, 22),
                Location = new Point(0, 134),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Info card ──────────────────────────────────────────────────
            var card = new Panel
            {
                Size = new Size(380, 82),
                Location = new Point(30, 164),
                BackColor = Color.FromArgb(30, 34, 48)
            };
            card.Region = MakeRoundedRegion(card.Size, 10);
            card.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(50, 55, 78), 1f);
                using var path = RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10);
                pe.Graphics.DrawPath(pen, path);
            };

            void AddRow(string label, string value, Color valColor, int y)
            {
                card.Controls.Add(new Label
                {
                    Text = label,
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = Color.FromArgb(110, 120, 145),
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(130, 26),
                    Location = new Point(16, y),
                    TextAlign = ContentAlignment.MiddleLeft
                });
                card.Controls.Add(new Label
                {
                    Text = value,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = valColor,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(220, 26),
                    Location = new Point(148, y),
                    TextAlign = ContentAlignment.MiddleRight
                });
            }

            var d = _lastReceiptData;
            AddRow("Invoice No", d.InvoiceNo, Color.FromArgb(99, 179, 255), 10);
            AddRow("Customer", d.CustomerName, Color.FromArgb(200, 210, 230), 36);
            AddRow("Total", Fmt(d.GrandTotal), TextGreen, 62);

            dlg.Controls.Add(card);

            // ── Buttons ────────────────────────────────────────────────────
            var btnPrint = new Button
            {
                Text = "🖨  Print Now",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccOrange,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(186, 42),
                Location = new Point(30, 262),
                Cursor = Cursors.Hand
            };
            btnPrint.FlatAppearance.BorderSize = 0;
            btnPrint.Region = MakeRoundedRegion(btnPrint.Size, 10);
            btnPrint.MouseEnter += (s, e) => btnPrint.BackColor = ControlPaint.Dark(AccOrange, 0.1f);
            btnPrint.MouseLeave += (s, e) => btnPrint.BackColor = AccOrange;
            btnPrint.Click += (s, e) =>
            {
                dlg.Close();
                PrintReceiptDialog.Show(this, _lastReceiptData);
                _lastSaleWasPrinted = true;
            };

            var btnSkip = new Button
            {
                Text = "Skip",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(186, 42),
                Location = new Point(224, 262),
                Cursor = Cursors.Hand
            };
            btnSkip.FlatAppearance.BorderSize = 0;
            btnSkip.Region = MakeRoundedRegion(btnSkip.Size, 10);
            btnSkip.Click += (s, e) =>
            {
                _lastSaleWasPrinted = true;   // suppress future prompts for this sale
                dlg.Close();
            };

            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; btnPrint.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; btnSkip.PerformClick(); }
            };

            dlg.Controls.AddRange(new Control[] { btnPrint, btnSkip });
            dlg.Shown += (s, e) => btnPrint.Focus();
            dlg.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PRINT CONFIRMATION — styled Yes/No popup, shown AFTER the sale/invoice
        //  has been fully posted to the server.
        // ══════════════════════════════════════════════════════════════════════
        private bool ShowPrintConfirmDialog(string invoiceNo, decimal total)
        {
            bool result = false;

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(22, 26, 36),
                ClientSize = new Size(440, 320),
                ShowInTaskbar = false,
                KeyPreview = true
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 16);

            // ── Top accent bar (green = success) ──────────────────────────────
            dlg.Controls.Add(new Panel
            {
                Size = new Size(440, 5),
                Location = Point.Empty,
                BackColor = AccGreen
            });

            // ── Success icon circle ─────────────────────────────────────────────
            var iconPanel = new Panel
            {
                Size = new Size(64, 64),
                Location = new Point((440 - 64) / 2, 24),
                BackColor = Color.FromArgb(18, 55, 35)
            };
            iconPanel.Region = MakeRoundedRegion(iconPanel.Size, 32);
            iconPanel.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var f = new Font("Segoe UI Emoji", 24F);
                using var br = new SolidBrush(AccGreen);
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                pe.Graphics.DrawString("✅", f, br,
                    new RectangleF(0, 0, iconPanel.Width, iconPanel.Height), sf);
            };
            dlg.Controls.Add(iconPanel);

            // ── Title ──────────────────────────────────────────────────────────
            dlg.Controls.Add(new Label
            {
                Text = "Sale Completed",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 245, 255),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(440, 32),
                Location = new Point(0, 100),
                TextAlign = ContentAlignment.MiddleCenter
            });
            dlg.Controls.Add(new Label
            {
                Text = "Would you like to print the invoice now?",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(120, 130, 155),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(440, 22),
                Location = new Point(0, 134),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Info card ──────────────────────────────────────────────────────
            var card = new Panel
            {
                Size = new Size(380, 60),
                Location = new Point(30, 164),
                BackColor = Color.FromArgb(30, 34, 48)
            };
            card.Region = MakeRoundedRegion(card.Size, 10);
            card.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(50, 55, 78), 1f);
                using var path = RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10);
                pe.Graphics.DrawPath(pen, path);
            };

            void AddRow(string label, string value, Color valColor, int y)
            {
                card.Controls.Add(new Label
                {
                    Text = label,
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = Color.FromArgb(110, 120, 145),
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(130, 26),
                    Location = new Point(16, y),
                    TextAlign = ContentAlignment.MiddleLeft
                });
                card.Controls.Add(new Label
                {
                    Text = value,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    ForeColor = valColor,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(220, 26),
                    Location = new Point(148, y),
                    TextAlign = ContentAlignment.MiddleRight
                });
            }

            AddRow("Invoice No", invoiceNo, Color.FromArgb(99, 179, 255), 8);
            AddRow("Total", Fmt(total), TextGreen, 34);

            dlg.Controls.Add(card);

            // ── Buttons ───────────────────────────────────────────────────────
            var btnYes = new Button
            {
                Text = "🖨  Yes, Print",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(186, 42),
                Location = new Point(30, 246),
                Cursor = Cursors.Hand
            };
            btnYes.FlatAppearance.BorderSize = 0;
            btnYes.Region = MakeRoundedRegion(btnYes.Size, 10);
            btnYes.MouseEnter += (s, e) => btnYes.BackColor = ControlPaint.Dark(AccGreen, 0.1f);
            btnYes.MouseLeave += (s, e) => btnYes.BackColor = AccGreen;
            btnYes.Click += (s, e) => { result = true; dlg.Close(); };

            var btnNo = new Button
            {
                Text = "No, Skip",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(186, 42),
                Location = new Point(224, 246),
                Cursor = Cursors.Hand
            };
            btnNo.FlatAppearance.BorderSize = 0;
            btnNo.Region = MakeRoundedRegion(btnNo.Size, 10);
            btnNo.MouseEnter += (s, e) => btnNo.BackColor = Color.FromArgb(56, 60, 74);
            btnNo.MouseLeave += (s, e) => btnNo.BackColor = Color.FromArgb(44, 48, 60);
            btnNo.Click += (s, e) => { result = false; dlg.Close(); };

            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; btnYes.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; btnNo.PerformClick(); }
            };

            dlg.Controls.AddRange(new Control[] { btnYes, btnNo });
            dlg.Shown += (s, e) => btnYes.Focus();
            dlg.ShowDialog(this);   // blocks until Yes/No clicked

            return result;
        }


        //private void btnCancelSale_Click(object sender, EventArgs e)
        //{
        //    // If cart is empty but we have an unprinted last sale — offer reprint
        //    if (_cart.Count == 0)
        //    {
        //        if (_lastReceiptData != null && !_lastSaleWasPrinted)
        //            ShowReprintDialog();
        //        return;
        //    }

        //    if (MessageBox.Show("Cancel this sale?", "Confirm",
        //        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        //        ResetSale();
        //}

        // ── In ResetSale() — hide and disable button when sale resets ─────
        private void ResetSale(bool generateNewInvoiceNo = true)
        {
            bool comingFromPending = _wasCompletedFromPendingInvoice;
            _wasCompletedFromPendingInvoice = false;

            _cart.Clear();
            _splitCash = 0m; _splitUpi = 0m; _splitCard = 0m; _numpadBuffer = "";
            _charges.Clear();
            _chargesAllocated = false;
            RefreshChargesButtonLabel();
            _selectedUpiMethodName = "Bank Transfer";
            _selectedBankAccount = null;
            _isCreditSale = false;
            RefreshSaleTypeToggleVisual();
            // in ResetSale(), near where _selectedUpiMethodName / _cardRefNumber are reset:
            if (!string.IsNullOrWhiteSpace(lblInvoiceNo.Text))
                PendingSalesOrderCache.Cache.Remove(lblInvoiceNo.Text);

            _isPendingInvoiceMode = false;
            _taxAlreadyIncluded = false;
            // ── Hide Customer Details button on reset ──────────────────────
            if (_cmbCustomer != null)
            {
                _cmbCustomer.Visible = false;
                _cmbCustomer.Enabled = true;

                var defaultCustomer = _customersList.FirstOrDefault(c =>
                        c.CustomerCode.Equals(DEFAULT_CUSTOMER_CODE, StringComparison.OrdinalIgnoreCase))
                    ?? _customersList.FirstOrDefault(c =>
                        c.CustomerName.Equals("Walk-in", StringComparison.OrdinalIgnoreCase))
                    ?? _customersList.FirstOrDefault();

                if (defaultCustomer != null)
                    _cmbCustomer.SelectedItem = defaultCustomer;
            }
            if (_pendingBanner != null && !_pendingBanner.IsDisposed)
            {
                panelCartItems.Controls.Remove(_pendingBanner);
                _pendingBanner.Dispose();
                _pendingBanner = null;
                foreach (Control c in panelCartItems.Controls)
                    c.Location = new Point(c.Left, c.Top - 30);
            }

            if (txtSearch != null) txtSearch.Enabled = true;
            if (txtBarcode != null) txtBarcode.Enabled = true;
            if (_searchWrapper != null) { _searchWrapper.Enabled = true; _searchWrapper.BackColor = InputBg; }
            if (_barcodeWrapper != null) { _barcodeWrapper.Enabled = true; _barcodeWrapper.BackColor = InputBg; }
            if (nudDiscount != null) nudDiscount.Enabled = true;
            if (panelHotItems != null) { panelHotItems.Enabled = true; panelHotItems.BackColor = PanelDark; }

            lblUpiTitle.Text = "Bank Transfer";
            lblUpiTitle.ForeColor = TextMuted;
            txtCustomer.ForeColor = TextMuted;
            txtCustomer.Text = "👤  Search customer…";
            txtSearch.ForeColor = TextMuted;
            txtSearch.Text = "  Search products…";
            txtBarcode.ForeColor = TextMuted;
            txtBarcode.Text = "  Scan barcode…";
            listSearchResults.Visible = false;
            HideCustomerDropdown();
            nudDiscount.Value = _defaultDiscountPct;
            RefreshCart();
            UpdateTotals();
            _chargesAllocated = false;        // ← mark charges as no longer valid
            RefreshChargesButtonLabel();

            if (generateNewInvoiceNo)
                lblInvoiceNo.Text = _isD365Mode
                    ? SalesRepository.NextInvoiceNo()
                    : SalesRepository.NextInvoiceNo();

            SetActiveSplit("cash");
            _isPendingInvoiceMode = false;
            SetD365Mode(_useD365);

            if (!comingFromPending && _catalog?.Count > 0)
            {
                BuildHotItems();
                BuildAutocomplete();
            }
            if (lblSplitBalance != null)
                lblSplitBalance.Visible = false;
            _customerNameValue = "";
            _customerAddressValue = "";
            _customerVatValue = "";
            _customerDiscountPct = 0m;

            // Button already hidden above — just reset its label


            if (ShiftState.IsOpen && ShiftState.CurrentFloat < 50)
                ShowStatus($"⚠ Float low: {Fmt(ShiftState.CurrentFloat)} — open Float Entry (F8).", false);
            // Reset unit prices display

            // Reset split balance visibility - show if not in pending mode
            if (lblSplitBalance != null)
            {
                lblSplitBalance.Visible = !_isPendingInvoiceMode;  // Show unless pending
                                                                   // lblSplitBalance.Text = "TOTAL: Ready for items";
                lblSplitBalance.ForeColor = TextMuted;
            }
        }

        private void ShowStatus(string msg, bool ok)
        {
            if (lblStatus.IsDisposed) return;
            if (lblStatus.InvokeRequired)
                lblStatus.BeginInvoke(new Action(() =>
                {
                    if (!lblStatus.IsDisposed)
                    { lblStatus.Text = msg; lblStatus.ForeColor = ok ? TextGreen : AccRed; }
                }));
            else
            { lblStatus.Text = msg; lblStatus.ForeColor = ok ? TextGreen : AccRed; }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PAINT / TITLE BAR
        // ══════════════════════════════════════════════════════════════════════
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); e.Graphics.Clear(BgDark); }

        private void panelHeader_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(PanelDark);
            using (var pen = new Pen(Border, 1f))
                e.Graphics.DrawLine(pen, 0, panelHeader.Height - 1, panelHeader.Width, panelHeader.Height - 1);
        }

        private void PaintDarkCard(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender; var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(PanelDark);
            using (var pen = new Pen(Border, 1f))
            using (var path = RoundedPath(new Rectangle(1, 1, p.Width - 2, p.Height - 2), 10))
                g.DrawPath(pen, path);
        }

        private void panelFooterBar_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(PanelDark);
            using (var pen = new Pen(Border, 1f))
                e.Graphics.DrawLine(pen, 0, 0, panelFooterBar.Width, 0);
        }

        private void RepositionTitleButtons()
        {
            if (panelHeader == null || panelHeader.IsDisposed) return;
            if (this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                int w = panelHeader.Width;
                btnClose.Location = new Point(w - 46, 0);
                btnMax.Location = new Point(w - 92, 0);
                btnMin.Location = new Point(w - 138, 0);

                if (lblShortcuts != null)
                {
                    int maxLblRight = btnMin.Left - 10;
                    lblShortcuts.MaximumSize = new Size(Math.Max(100, maxLblRight - lblShortcuts.Left), 20);
                }

                int rightEdge = btnMin.Left - 20;
                if (txtBarcode != null)
                {
                    txtBarcode.Location = new Point(rightEdge - txtBarcode.Width, 10);
                    if (lblBarcodeHeader != null)
                        lblBarcodeHeader.Location = new Point(txtBarcode.Left - 16, 12);
                    if (lblBarcodeSep != null)
                        lblBarcodeSep.Location = new Point(txtBarcode.Left - 34, 10);
                }

                if (txtSearch != null && lblBarcodeSep != null)
                {
                    int searchRight = lblBarcodeSep.Left - 10;
                    txtSearch.Width = Math.Max(200, searchRight - txtSearch.Left);
                }
            }
            catch (ObjectDisposedException) { }
        }

        private void panelHeader_Resize(object sender, EventArgs e) => RepositionTitleButtons();
        private void panelHeader_DoubleClick(object sender, EventArgs e) => btnMax_Click(sender, e);
        private void btnClose_Click(object sender, EventArgs e) => this.Close();
        private void btnMin_Click(object sender, EventArgs e) => this.WindowState = FormWindowState.Minimized;
        private void btnMax_Click(object sender, EventArgs e)
        {
            this.WindowState = this.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
            btnMax.Text = this.WindowState == FormWindowState.Maximized ? "❐" : "□";
            RepositionTitleButtons();
        }

        private void btnClose_MouseEnter(object sender, EventArgs e) => btnClose.BackColor = Color.FromArgb(196, 30, 58);
        private void btnClose_MouseLeave(object sender, EventArgs e) => btnClose.BackColor = PanelDark;
        private void btnMax_MouseEnter(object sender, EventArgs e) => btnMax.BackColor = Color.FromArgb(55, 62, 78);
        private void btnMax_MouseLeave(object sender, EventArgs e) => btnMax.BackColor = PanelDark;
        private void btnMin_MouseEnter(object sender, EventArgs e) => btnMin.BackColor = Color.FromArgb(55, 62, 78);
        private void btnMin_MouseLeave(object sender, EventArgs e) => btnMin.BackColor = PanelDark;

        private void panelHeader_MouseDown(object sender, MouseEventArgs e)
        { _drag = true; _dragCursor = Cursor.Position; _dragForm = this.Location; }
        private void panelHeader_MouseMove(object sender, MouseEventArgs e)
        { if (_drag) this.Location = Point.Add(_dragForm, new Size(Point.Subtract(Cursor.Position, new Size(_dragCursor)))); }
        private void panelHeader_MouseUp(object sender, MouseEventArgs e) => _drag = false;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (panelHeader != null) RepositionTitleButtons();
            RepositionDropdown();
            RepositionSearchResults();
        }


        // ══════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════
        private Region MakeRoundedRegion(Size size, int r) =>
            new Region(RoundedPath(new Rectangle(0, 0, size.Width, size.Height), r));

        private GraphicsPath RoundedPath(Rectangle rect, int r)
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _scheduler?.Dispose();
            _hotItemsTooltip?.Dispose();
            base.OnFormClosed(e);
            _productSyncTimer?.Stop(); _productSyncTimer?.Dispose();
            _offlineOrderSyncTimer?.Stop(); _offlineOrderSyncTimer?.Dispose();
            _stockSyncTimer?.Stop(); _stockSyncTimer?.Dispose();
            _resizeDebounce?.Stop();
            _resizeDebounce?.Dispose();
            _resizeDebounce = null;
        }
        // ══════════════════════════════════════════════════════════════════════
        //  SEARCH RESULTS — OWNER-DRAW (modern dark dropdown)
        // ══════════════════════════════════════════════════════════════════════
        private void listSearchResults_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= listSearchResults.Items.Count) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool hot = (e.State & DrawItemState.HotLight) != 0;

            // Row background
            Color rowBg = selected || hot
                ? Color.FromArgb(45, 110, 255)          // accent blue when selected
                : e.Index % 2 == 0
                    ? Color.FromArgb(30, 34, 46)        // even row
                    : Color.FromArgb(36, 40, 54);       // odd row — subtle stripe

            using (var bgBrush = new SolidBrush(rowBg))
                g.FillRectangle(bgBrush, e.Bounds);

            // Left accent bar on selected row
            if (selected || hot)
            {
                using var accentBrush = new SolidBrush(Color.FromArgb(99, 179, 255));
                g.FillRectangle(accentBrush, new Rectangle(e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height));
            }

            // Parse "Product Name — P xx.xx"
            string fullText = listSearchResults.Items[e.Index].ToString();
            string[] parts = fullText.Split(new[] { " — " }, StringSplitOptions.None);
            string name = parts.Length > 0 ? parts[0].Trim() : fullText;
            string price = parts.Length > 1 ? parts[1].Trim() : "";

            // Search icon
            using (var iconFont = new Font("Segoe UI Emoji", 9F))
            using (var iconBrush = new SolidBrush(selected ? Color.White : Color.FromArgb(59, 130, 246)))
                g.DrawString("🔍", iconFont, iconBrush,
                    new RectangleF(e.Bounds.X + 6, e.Bounds.Y, 22, e.Bounds.Height),
                    new StringFormat { LineAlignment = StringAlignment.Center });

            // Product name
            using (var nameFont = new Font("Segoe UI", 9.5F, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(selected ? Color.White : Color.FromArgb(220, 228, 245)))
            {
                var nameRect = new RectangleF(e.Bounds.X + 32, e.Bounds.Y, e.Bounds.Width - 110, e.Bounds.Height);
                var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(name, nameFont, nameBrush, nameRect, sf);
            }

            // Price — right-aligned
            if (!string.IsNullOrEmpty(price))
            {
                using (var priceFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                using (var priceBrush = new SolidBrush(selected ? Color.FromArgb(180, 255, 200) : Color.FromArgb(52, 211, 153)))
                {
                    var priceRect = new RectangleF(e.Bounds.Right - 100, e.Bounds.Y, 96, e.Bounds.Height);
                    var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    g.DrawString(price, priceFont, priceBrush, priceRect, sf);
                }
            }

            // Bottom separator line
            if (!selected)
            {
                using var sepPen = new Pen(Color.FromArgb(45, 50, 68), 1f);
                g.DrawLine(sepPen, e.Bounds.Left + 32, e.Bounds.Bottom - 1,
                                    e.Bounds.Right - 8, e.Bounds.Bottom - 1);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DTOs
    // ══════════════════════════════════════════════════════════════════════════
    public class CompanySettingsDto
    {
        public string CurrencySymbol { get; set; } = "";
        public int CurrencyId { get; set; } = 1;
        public decimal DefaultDiscountPct { get; set; } = 0m;
        public decimal TaxRate { get; set; } = 8m;
        public string CompanyName { get; set; } = "";
    }

    public class ItemDto
    {
        public int ItemID { get; set; }
        public int CompanyID { get; set; }
        public string SKU { get; set; }
        public string ItemName { get; set; }
        public int BaseUOM { get; set; }
        public int Department { get; set; }
        public int Category { get; set; }
        public int SubCategory { get; set; }
        public bool IsPackSizeEnabled { get; set; }
        public bool IsBatchEnabled { get; set; }
        public bool IsSerialNoEnabled { get; set; }
        public bool IsLotNoEnabled { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SellingPrice { get; set; }
        public int PurchaseTax { get; set; }
        public int SalesTax { get; set; }
        public string BarCode { get; set; }
        public bool Status { get; set; }
    }

    public class PaymentMethodDto
    {
        public int PaymentMethodID { get; set; }
        public string PayMethodShort { get; set; }
        public string PayMethodDescription { get; set; }
        public int CurrencyID { get; set; }
        public bool Status { get; set; }
        public string Name => PayMethodShort;
        public string Description => PayMethodDescription;
        public string Icon { get; set; } = "💳";
    }

    public class ApiResponse<T>
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public T? Data { get; set; }
    }

    public class CurrencyDto
    {
        public int CurrencyID { get; set; }
        public string CurrencyName { get; set; }
        public string CurrencySymbol { get; set; }
    }

    public class AuthRoleDto
    {
        public string Username { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Used to serialize/deserialize cart items for pending invoice storage.
    /// </summary>
    public class CartItemDto
    {
        public string Name { get; set; }
        public decimal OriginalPrice { get; set; }
        public decimal Price { get; set; }
        public decimal Qty { get; set; }
        public decimal DiscountPct { get; set; }
        public string Barcode { get; set; }
        public int UOM { get; set; } = 1;          // ← ADDED
        public string UOMName { get; set; } = "";
    }
    public class CustomerListDto
    {
        [JsonPropertyName("isSuccess")] public bool IsSuccess { get; set; }
        [JsonPropertyName("data")] public List<CustomerFullDto> Data { get; set; } = new();
    }

    public class CustomerFullDto
    {
        [JsonPropertyName("customerID")] public int CustomerID { get; set; }
        [JsonPropertyName("customerCode")] public string CustomerCode { get; set; } = "";
        [JsonPropertyName("customerName")] public string CustomerName { get; set; } = "";
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("city")] public string City { get; set; } = "";
        [JsonPropertyName("country")] public string Country { get; set; } = "";
        [JsonPropertyName("mobile")] public string Mobile { get; set; } = "";
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("status")] public bool Status { get; set; }
    }

    public class TaxDto
    {
        [JsonPropertyName("taxid")] public int TaxId { get; set; }
        [JsonPropertyName("taxCode")] public string TaxCode { get; set; } = "";
        [JsonPropertyName("taxPercentage")] public decimal TaxPercentage { get; set; }
    }
    public class UomDto
    {
        [JsonPropertyName("uomid")] public int UomId { get; set; }
        [JsonPropertyName("uomDescription")] public string UomDescription { get; set; } = "";
        [JsonPropertyName("unitsPerPack")] public int UnitsPerPack { get; set; } = 1;   // ← NEW
        [JsonPropertyName("retailPrice")] public decimal RetailPrice { get; set; } = 0m; // ← NEW
    }

}




