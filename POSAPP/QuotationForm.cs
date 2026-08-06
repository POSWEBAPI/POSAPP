// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  QuotationForm.cs  — Quotation entry (mirrors SalesForm, no payments)  ║
// ╚══════════════════════════════════════════════════════════════════════════╝
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace POSAPP
{
    public partial class QuotationForm : Form
    {
        // ── Palette ────────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color PanelDark2 = Color.FromArgb(42, 46, 56);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color AccPurple = Color.FromArgb(167, 92, 237);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccCyan = Color.FromArgb(20, 184, 166);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);
        private static readonly Color InputBg = Color.FromArgb(28, 32, 42);
        public static bool IsQuotation { get; set; } = false;

        // ── State ──────────────────────────────────────────────────────────
        private readonly int _companyId;
        private readonly string _currencySymbol;
        private readonly string _companyName, _companyVat;
        private readonly string _companyAddress, _companyPhone;
        private readonly string _companyWebsite, _salesOfficeInfo;
        private readonly decimal _taxRate = 0.14m;
        private bool _manualMode = true;
        private bool _productsLoaded = false;
        private string? _lastSavedQno = null;   // set after successful save

        // ── Cart ───────────────────────────────────────────────────────────
        private class QuoteItem
        {
            public string StockCode { get; set; } = "";
            public string Name { get; set; } = "";
            public string UOM { get; set; } = "Ea";
            public decimal Qty { get; set; } = 1m;
            public decimal UnitPrice { get; set; }
            public decimal DiscountPct { get; set; }
            public string PriceGroup { get; set; } = "";
            public decimal LineTotal => Math.Round(UnitPrice * Qty * (1m - DiscountPct / 100m), 2);
        }
        private List<QuoteItem> _cart = new();

        // ── Catalog ────────────────────────────────────────────────────────
        private class Product
        {
            public string Name { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string Category { get; set; } = "";
            public decimal Price { get; set; }
        }
        private List<Product> _catalog = new();
        private Dictionary<string, List<D365PriceGroup>> _priceGroups =
            new(StringComparer.OrdinalIgnoreCase);

        private class D365PriceGroup
        {
            public string AccountRelation { get; set; } = "";
            public decimal Amount { get; set; }
            public string InventSiteId { get; set; } = "";
            public string InventLocationId { get; set; } = "";
        }

        private static readonly string _dbPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ── Controls ───────────────────────────────────────────────────────
        private Label lblQuoteNo, lblDate, lblStatus;
        private Label lblItemCount, lblSubtotal, lblDiscount, lblTax, lblGrand;
        private TextBox txtCustomer, txtCustomerAddr, txtCustomerVat, txtSearch;
        private Panel panelCart;
        private ListBox listSearch;
        private Button btnModeManual, btnModeGroup, btnSave, btnPrint, btnClear;
        private NumericUpDown nudDiscount;

        // ── Constructor ────────────────────────────────────────────────────
        public QuotationForm(int companyId, string currencySymbol,
            string companyName, string companyVat,
            string companyAddress, string companyPhone,
            string companyWebsite, string salesOfficeInfo)
        {
            _companyId = companyId;
            _currencySymbol = currencySymbol;
            _companyName = companyName;
            _companyVat = companyVat;
            _companyAddress = companyAddress;
            _companyPhone = companyPhone;
            _companyWebsite = companyWebsite;
            _salesOfficeInfo = salesOfficeInfo;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            Size = new Size(1280, 760);
            KeyPreview = true;

            BuildUI();
            UpdateTotals();

            // Load products asynchronously after form is shown
            Shown += async (s, e) =>
            {
                SetStatus("Loading products…", true);
                await LoadProductsAsync();
                SetStatus(_catalog.Count > 0
                    ? $"✓ {_catalog.Count} products ready — search above."
                    : "No products found — check D365 sync.", _catalog.Count > 0);
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI BUILD
        // ══════════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            // ── Title bar ─────────────────────────────────────────────────
            var header = new Panel
            {
                BackColor = PanelDark,
                Size = new Size(Width, 52),
                Location = Point.Empty
            };

            header.Controls.Add(new Label
            {
                Text = "📄  New Quotation",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 52),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            lblQuoteNo = new Label
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(410, 17)
            };
            header.Controls.Add(lblQuoteNo);

            lblDate = new Label
            {
                Text = DateTime.Now.ToString("dd MMM yyyy"),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(700, 19)
            };
            header.Controls.Add(lblDate);

            // Window buttons
            var btnClose = MakeTitleBtn("✕", new Point(Width - 46, 0));
            btnClose.Click += (s, e) => Close();
            btnClose.MouseEnter += (s, e) => btnClose.BackColor = AccRed;
            btnClose.MouseLeave += (s, e) => btnClose.BackColor = Color.Transparent;
            header.Controls.Add(btnClose);

            var btnMin = MakeTitleBtn("—", new Point(Width - 92, 0));
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            header.Controls.Add(btnMin);

            // Drag
            bool drag = false;
            Point dragPt = Point.Empty;
            header.MouseDown += (s, e) => { drag = true; dragPt = e.Location; };
            header.MouseMove += (s, e) => { if (drag) Location = new Point(Location.X + e.X - dragPt.X, Location.Y + e.Y - dragPt.Y); };
            header.MouseUp += (s, e) => drag = false;
            Controls.Add(header);

            // ── LEFT — customer + search + cart ───────────────────────────
            int leftW = Width - 320;
            int topY = 60;

            // Customer card
            var custCard = MakeCard(new Rectangle(10, topY, leftW - 10, 110));

            custCard.Controls.Add(MakeLbl("👤 Customer", new Font("Segoe UI", 8F, FontStyle.Bold), TextMuted, new Point(10, 6)));

            txtCustomer = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(260, 30),
                Location = new Point(10, 26),
                PlaceholderText = "Customer name…"
            };
            custCard.Controls.Add(txtCustomer);

            txtCustomerAddr = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(leftW - 520, 30),
                Location = new Point(280, 26),
                PlaceholderText = "Customer address (optional)…"
            };
            custCard.Controls.Add(txtCustomerAddr);

            txtCustomerVat = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(170, 30),
                Location = new Point(leftW - 230, 26),
                PlaceholderText = "VAT No…"
            };
            custCard.Controls.Add(txtCustomerVat);

            // Row 2: Discount + mode toggles
            custCard.Controls.Add(MakeLbl("Discount %", new Font("Segoe UI", 8F), TextMuted, new Point(10, 66)));
            nudDiscount = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                DecimalPlaces = 1,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextWhite,
                BackColor = InputBg,
                Size = new Size(80, 28),
                Location = new Point(90, 62)
            };
            nudDiscount.ValueChanged += (s, e) =>
            {
                foreach (var i in _cart) i.DiscountPct = nudDiscount.Value;
                RefreshCart();
                UpdateTotals();
            };
            custCard.Controls.Add(nudDiscount);

            btnModeManual = MakeToggleBtn("✏ Manual Price", new Point(190, 62), true);
            btnModeGroup = MakeToggleBtn("📦 Price Group", new Point(330, 62), false);
            btnModeManual.Click += (s, e) => SetMode(true);
            btnModeGroup.Click += (s, e) => SetMode(false);
            custCard.Controls.Add(btnModeManual);
            custCard.Controls.Add(btnModeGroup);

            // Search bar
            int searchY = topY + 120;
            var searchCard = MakeCard(new Rectangle(10, searchY, leftW - 10, 44));

            searchCard.Controls.Add(MakeLbl("🔍", new Font("Segoe UI Emoji", 11F), AccBlue, new Point(8, 10)));

            txtSearch = new TextBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextMuted,
                BackColor = InputBg,
                BorderStyle = BorderStyle.None,
                Size = new Size(leftW - 60, 32),
                Location = new Point(36, 6),
                PlaceholderText = "Search product or type item name…"
            };
            txtSearch.TextChanged += TxtSearch_Changed;
            txtSearch.KeyDown += TxtSearch_KeyDown;
            searchCard.Controls.Add(txtSearch);

            // Search results dropdown
            listSearch = new ListBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(36, 40, 52),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                IntegralHeight = false
            };
            listSearch.Click += (s, e) => SelectSearchResult();
            listSearch.DoubleClick += (s, e) => SelectSearchResult();
            listSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) SelectSearchResult();
                if (e.KeyCode == Keys.Escape) { listSearch.Visible = false; txtSearch.Focus(); }
            };
            Controls.Add(listSearch);

            // Cart
            int cartY = searchY + 54;
            panelCart = new Panel
            {
                BackColor = BgDark,
                AutoScroll = true,
                Size = new Size(leftW - 10, Height - cartY - 56),
                Location = new Point(10, cartY),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom
            };
            Controls.Add(panelCart);

            // ── RIGHT — totals + buttons ──────────────────────────────────
            int rx = leftW + 14;
            int rw = Width - rx - 14;

            var totalsCard = MakeCard(new Rectangle(rx, topY, rw, 300));
            totalsCard.Controls.Add(MakeLbl("QUOTATION TOTALS",
                new Font("Segoe UI", 7.5F, FontStyle.Bold), TextMuted, new Point(10, 10)));

            int ty = 34;
            lblItemCount = AddTotalRow(totalsCard, "Items", "0 item(s)", ref ty, TextMuted);
            AddSep(totalsCard, ty); ty += 10;
            lblSubtotal = AddTotalRow(totalsCard, "Subtotal", Fmt(0), ref ty, TextWhite);
            lblDiscount = AddTotalRow(totalsCard, "Discount", "- " + Fmt(0), ref ty, AccCyan);
            lblTax = AddTotalRow(totalsCard, "VAT 14%", Fmt(0), ref ty, TextMuted);
            AddSep(totalsCard, ty); ty += 10;
            totalsCard.Controls.Add(MakeLbl("GRAND TOTAL",
                new Font("Segoe UI", 9F, FontStyle.Bold), TextMuted, new Point(10, ty)));
            ty += 22;
            lblGrand = MakeLbl(Fmt(0), new Font("Segoe UI", 16F, FontStyle.Bold), TextGreen,
                new Point(10, ty));
            lblGrand.AutoSize = true;
            totalsCard.Controls.Add(lblGrand);

            // Buttons
            int by2 = topY + 308;
            btnSave = MakeBigBtn("💾  Save Quotation", AccGreen,
                new Rectangle(rx, by2, rw, 46));
            btnSave.Click += (s, e) => SaveQuotation();
            Controls.Add(btnSave);

            btnPrint = MakeBigBtn("🖨  Print Quotation", AccBlue,
                new Rectangle(rx, by2 + 54, rw, 40));
            btnPrint.Enabled = false;
            btnPrint.Click += (s, e) => PrintLastSaved();
            Controls.Add(btnPrint);

            btnClear = MakeBigBtn("🗑  Clear Cart", AccRed,
                new Rectangle(rx, by2 + 102, rw, 36));
            btnClear.Click += (s, e) =>
            {
                if (_cart.Count > 0 &&
                    MessageBox.Show("Clear all items?", "Confirm",
                        MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _cart.Clear();
                    RefreshCart();
                    UpdateTotals();
                }
            };
            Controls.Add(btnClear);

            // Status
            lblStatus = new Label
            {
                Text = "Ready.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(rw, 50),
                Location = new Point(rx, by2 + 148),
                TextAlign = ContentAlignment.TopLeft
            };
            Controls.Add(lblStatus);

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRODUCT LOAD
        // ══════════════════════════════════════════════════════════════════
        private async Task LoadProductsAsync()
        {
            if (!File.Exists(_dbPath)) return;

            var catTmp = new List<Product>();
            var pgTmp = new Dictionary<string, List<D365PriceGroup>>(
                StringComparer.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                try
                {
                    using var conn = new SQLiteConnection(
                        $"Data Source={_dbPath};Version=3;");
                    conn.Open();

                    // Guard
                    using (var chk = conn.CreateCommand())
                    {
                        chk.CommandText =
                            "SELECT COUNT(*) FROM sqlite_master " +
                            "WHERE type='table' AND name='D365Products';";
                        if ((long)(chk.ExecuteScalar() ?? 0L) == 0) return;
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT ItemId, NameAlias, InventSiteId " +
                            "FROM D365Products ORDER BY NameAlias;";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            string id = r[0]?.ToString() ?? "";
                            string name = r[1]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(id)) continue;
                            catTmp.Add(new Product
                            {
                                Barcode = id,
                                Name = name,
                                Category = r[2]?.ToString() ?? "",
                                Price = 0m
                            });
                        }
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT ItemId, AccountRelation, Amount,
                                   InventSiteId, InventLocationId
                            FROM D365ProductDetails ORDER BY ItemId, Amount;";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            string id = r[0]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(id)) continue;
                            if (!pgTmp.ContainsKey(id))
                                pgTmp[id] = new List<D365PriceGroup>();
                            decimal amt = r.IsDBNull(2)
                                ? 0m : Convert.ToDecimal(r.GetValue(2));
                            pgTmp[id].Add(new D365PriceGroup
                            {
                                AccountRelation = r[1]?.ToString() ?? "(default)",
                                Amount = amt,
                                InventSiteId = r[3]?.ToString() ?? "",
                                InventLocationId = r[4]?.ToString() ?? ""
                            });
                            var prod = catTmp.FirstOrDefault(p => p.Barcode == id);
                            if (prod != null && prod.Price == 0m)
                                prod.Price = amt;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("QuotationForm.LoadProductsAsync: " + ex.Message);
                }
            });

            _catalog = catTmp;
            _priceGroups = pgTmp;
            _productsLoaded = true;
        }

        // ══════════════════════════════════════════════════════════════════
        //  MODE
        // ══════════════════════════════════════════════════════════════════
        private void SetMode(bool manual)
        {
            _manualMode = manual;
            btnModeManual.BackColor = manual
                ? AccGreen : Color.FromArgb(44, 48, 60);
            btnModeGroup.BackColor = manual
                ? Color.FromArgb(44, 48, 60) : AccPurple;
            SetStatus(manual
                ? "Manual mode — type any price per line."
                : "Price Group mode — select from D365 price groups.", true);
        }

        // ══════════════════════════════════════════════════════════════════
        //  SEARCH
        // ══════════════════════════════════════════════════════════════════
        private void TxtSearch_Changed(object sender, EventArgs e)
        {
            string q = txtSearch.Text.Trim();
            listSearch.Items.Clear();

            if (string.IsNullOrEmpty(q))
            {
                listSearch.Visible = false;
                return;
            }

            // Exact name / barcode match
            var matches = _catalog
                .Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || p.Barcode.Contains(q, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(30)
                .ToList();

            if (matches.Count == 0)
            {
                listSearch.Items.Add($"➕  Add custom item: \"{q}\"");
                listSearch.Tag = q;
            }
            else
            {
                listSearch.Tag = null;
                foreach (var m in matches)
                    listSearch.Items.Add($"{m.Name}  —  {Fmt(m.Price)}");
            }

            // Position dropdown below search bar
            Point screenPt = txtSearch.PointToScreen(new Point(0, txtSearch.Height + 2));
            Point formPt = PointToClient(screenPt);

            listSearch.Location = formPt;
            listSearch.Width = txtSearch.Parent?.Width ?? 600;
            listSearch.Height = Math.Min(
                (matches.Count == 0 ? 1 : matches.Count) * 22 + 6, 220);
            listSearch.Visible = true;
            listSearch.BringToFront();
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && listSearch.Visible
                && listSearch.Items.Count > 0)
            {
                listSearch.Focus();
                listSearch.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (listSearch.Visible && listSearch.Items.Count > 0)
                {
                    listSearch.SelectedIndex = 0;
                    SelectSearchResult();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                listSearch.Visible = false;
                e.Handled = true;
            }
        }

        private void SelectSearchResult()
        {
            if (listSearch.Items.Count == 0) return;
            if (listSearch.SelectedIndex < 0) listSearch.SelectedIndex = 0;
            if (listSearch.SelectedIndex < 0) return;

            string raw = listSearch.SelectedItem.ToString() ?? "";
            listSearch.Visible = false;
            txtSearch.Clear();
            txtSearch.Focus();

            if (raw.StartsWith("➕"))
            {
                string name = listSearch.Tag?.ToString() ?? raw;
                AddCustomItem(name);
                return;
            }

            string productName = raw.Split(new[] { "  —  " },
                StringSplitOptions.None)[0].Trim();
            var prod = _catalog.FirstOrDefault(p =>
                p.Name.Equals(productName, StringComparison.OrdinalIgnoreCase));
            if (prod == null) return;

            if (!_manualMode && _priceGroups.ContainsKey(prod.Barcode)
                && _priceGroups[prod.Barcode].Count > 0)
                ShowPriceGroupDialog(prod);
            else
                ShowManualAddDialog(prod);
        }

        // ══════════════════════════════════════════════════════════════════
        //  ADD DIALOGS
        // ══════════════════════════════════════════════════════════════════
        private void AddCustomItem(string rawName)
        {
            ShowManualAddDialog(new Product
            { Name = rawName, Price = 0m, Barcode = "", Category = "" });
        }

        private void ShowManualAddDialog(Product prod)
        {
            var dlg = MakeDialog(400, 320, $"Add: {prod.Name}");
            int fy = 56;
            var tbPrice = AddDialogField(dlg, $"Unit Price ({_currencySymbol})",
                prod.Price > 0 ? prod.Price.ToString("F2") : "0.00", ref fy, AccOrange);
            var tbQty = AddDialogField(dlg, "Quantity",
                "1", ref fy, AccBlue);
            var tbDisc = AddDialogField(dlg, "Discount %",
                nudDiscount.Value.ToString("F1"), ref fy, AccCyan);
            var tbUom = AddDialogField(dlg, "UOM",
                "Ea", ref fy, TextMuted);

            var btnAdd = MakeBigBtn("✓  Add to Quotation", AccGreen,
                new Rectangle(20, fy + 8, 360, 42));
            dlg.Controls.Add(btnAdd);

            btnAdd.Click += (s, e) =>
            {
                if (!decimal.TryParse(tbPrice.Text.Trim(), out decimal price)
                    || price < 0)
                { tbPrice.BackColor = Color.FromArgb(80, 30, 30); tbPrice.Focus(); return; }
                if (!decimal.TryParse(tbQty.Text.Trim(), out decimal qty)
                    || qty <= 0)
                { tbQty.BackColor = Color.FromArgb(80, 30, 30); tbQty.Focus(); return; }
                decimal.TryParse(tbDisc.Text.Trim(), out decimal disc);
                disc = Math.Max(0, Math.Min(100, disc));

                _cart.Add(new QuoteItem
                {
                    StockCode = prod.Barcode,
                    Name = prod.Name,
                    UOM = tbUom.Text.Trim().Length > 0 ? tbUom.Text.Trim() : "Ea",
                    Qty = qty,
                    UnitPrice = price,
                    DiscountPct = disc,
                    PriceGroup = "Manual"
                });
                dlg.Close();
                RefreshCart();
                UpdateTotals();
                SetStatus($"✓ Added: {prod.Name}", true);
            };

            dlg.ClientSize = new Size(400, fy + 62);
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; btnAdd.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
            };
            dlg.Shown += (s, e) => { tbPrice.SelectAll(); tbPrice.Focus(); };
            dlg.ShowDialog(this);
        }

        private void ShowPriceGroupDialog(Product prod)
        {
            var groups = _priceGroups.ContainsKey(prod.Barcode)
                ? _priceGroups[prod.Barcode]
                : new List<D365PriceGroup>();

            var dlg = MakeDialog(440, 340, $"Price Group: {prod.Name}");

            dlg.Controls.Add(MakeLbl("Select Price Group",
                new Font("Segoe UI", 8.5F), TextMuted, new Point(20, 56)));

            var cmb = new ComboBox
            {
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = InputBg,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(400, 30),
                Location = new Point(20, 76)
            };
            foreach (var g in groups)
            {
                string acct = string.IsNullOrWhiteSpace(g.AccountRelation)
                    ? "(default)" : g.AccountRelation;
                cmb.Items.Add($"{acct}  —  {Fmt(g.Amount)}");
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            dlg.Controls.Add(cmb);

            int fy = 118;
            var tbQty = AddDialogField(dlg, "Quantity",
                "1", ref fy, AccBlue);
            var tbDisc = AddDialogField(dlg, "Discount %",
                nudDiscount.Value.ToString("F1"), ref fy, AccCyan);

            var lblPrice = MakeLbl(
                groups.Count > 0 ? Fmt(groups[0].Amount) : Fmt(0),
                new Font("Segoe UI", 14F, FontStyle.Bold), TextGreen,
                new Point(20, fy));
            lblPrice.AutoSize = true;
            dlg.Controls.Add(lblPrice);

            cmb.SelectedIndexChanged += (s, e) =>
            {
                if (cmb.SelectedIndex >= 0 && cmb.SelectedIndex < groups.Count)
                    lblPrice.Text = Fmt(groups[cmb.SelectedIndex].Amount);
            };

            var btnAdd = MakeBigBtn("✓  Add to Quotation", AccGreen,
                new Rectangle(20, fy + 36, 400, 42));
            dlg.Controls.Add(btnAdd);

            btnAdd.Click += (s, e) =>
            {
                if (cmb.SelectedIndex < 0 || cmb.SelectedIndex >= groups.Count) return;
                if (!decimal.TryParse(tbQty.Text.Trim(), out decimal qty) || qty <= 0)
                { tbQty.BackColor = Color.FromArgb(80, 30, 30); tbQty.Focus(); return; }
                decimal.TryParse(tbDisc.Text.Trim(), out decimal disc);
                disc = Math.Max(0, Math.Min(100, disc));

                var g = groups[cmb.SelectedIndex];
                string acct = string.IsNullOrWhiteSpace(g.AccountRelation)
                    ? "(default)" : g.AccountRelation;

                _cart.Add(new QuoteItem
                {
                    StockCode = prod.Barcode,
                    Name = prod.Name,
                    UOM = "Ea",
                    Qty = qty,
                    UnitPrice = g.Amount,
                    DiscountPct = disc,
                    PriceGroup = acct
                });
                dlg.Close();
                RefreshCart();
                UpdateTotals();
                SetStatus($"✓ Added: {prod.Name}  [{acct}]", true);
            };

            dlg.ClientSize = new Size(440, fy + 96);
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; btnAdd.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
            };
            dlg.Shown += (s, e) => { tbQty.SelectAll(); tbQty.Focus(); };
            dlg.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════
        //  CART
        // ══════════════════════════════════════════════════════════════════
        private void RefreshCart()
        {
            panelCart.SuspendLayout();
            foreach (Control c in panelCart.Controls) c.Dispose();
            panelCart.Controls.Clear();

            if (_cart.Count == 0)
            {
                panelCart.Controls.Add(MakeLbl(
                    "Cart is empty — search a product above.",
                    new Font("Segoe UI", 10F), TextMuted, new Point(10, 20)));
                panelCart.ResumeLayout();
                return;
            }

            // Header row
            var hdr = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Size = new Size(panelCart.Width - 4, 28),
                Location = new Point(2, 0)
            };
            void H(string t, int x, int w, ContentAlignment a = ContentAlignment.MiddleLeft)
                => hdr.Controls.Add(new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(w, 28),
                    Location = new Point(x, 0),
                    TextAlign = a
                });
            H("#", 6, 24);
            H("Description", 34, 200);
            H("UOM", 240, 50);
            H("Qty", 294, 70);
            H("Unit Price", 368, 100);
            H("Disc %", 472, 60);
            H("Line Total", 536, 100, ContentAlignment.MiddleRight);
            H("", 642, 30);
            panelCart.Controls.Add(hdr);

            int y = 32;
            for (int i = 0; i < _cart.Count; i++)
            {
                panelCart.Controls.Add(BuildCartRow(_cart[i], i, y));
                y += 54;
            }
            panelCart.ResumeLayout();
        }

        private Panel BuildCartRow(QuoteItem item, int idx, int yOffset)
        {
            const int RH = 48;
            var row = new Panel
            {
                BackColor = idx % 2 == 0 ? PanelDark2 : Color.FromArgb(36, 40, 52),
                Size = new Size(panelCart.Width - 8, RH),
                Location = new Point(4, yOffset),
                Cursor = Cursors.Default
            };

            void L(string t, int x, int w, Font f, Color fc,
                ContentAlignment a = ContentAlignment.MiddleLeft)
                => row.Controls.Add(new Label
                {
                    Text = t,
                    Font = f,
                    ForeColor = fc,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(w, RH),
                    Location = new Point(x, 0),
                    TextAlign = a
                });

            var fN = new Font("Segoe UI", 8.5F);
            var fB = new Font("Segoe UI", 9F, FontStyle.Bold);

            L((idx + 1).ToString(), 6, 24, new Font("Segoe UI", 7.5F, FontStyle.Bold),
                TextMuted, ContentAlignment.MiddleCenter);
            L(item.Name, 34, 200, fB, TextWhite);
            L(item.UOM, 240, 50, fN, TextMuted, ContentAlignment.MiddleCenter);

            // Editable Qty
            var tbQty = new TextBox
            {
                Text = item.Qty.ToString("F2"),
                Font = fN,
                ForeColor = TextWhite,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(66, 24),
                Location = new Point(294, (RH - 24) / 2),
                TextAlign = HorizontalAlignment.Center
            };
            var capItem = item;
            tbQty.Leave += (s, e) =>
            {
                if (decimal.TryParse(tbQty.Text, out decimal q) && q > 0)
                { capItem.Qty = q; UpdateTotals(); }
                else tbQty.Text = capItem.Qty.ToString("F2");
            };
            row.Controls.Add(tbQty);

            // Editable Unit Price
            var tbPrice = new TextBox
            {
                Text = item.UnitPrice.ToString("F2"),
                Font = fN,
                ForeColor = AccOrange,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(90, 24),
                Location = new Point(368, (RH - 24) / 2),
                TextAlign = HorizontalAlignment.Right,
                ReadOnly = !_manualMode && !string.IsNullOrEmpty(item.StockCode)
            };
            tbPrice.Leave += (s, e) =>
            {
                if (decimal.TryParse(tbPrice.Text, out decimal p) && p >= 0)
                { capItem.UnitPrice = p; UpdateTotals(); }
                else tbPrice.Text = capItem.UnitPrice.ToString("F2");
            };
            row.Controls.Add(tbPrice);

            L(item.DiscountPct > 0 ? $"{item.DiscountPct:F1}%" : "—",
                472, 60, fN, AccCyan, ContentAlignment.MiddleCenter);
            L(Fmt(item.LineTotal), 536, 100, fB, TextGreen, ContentAlignment.MiddleRight);

            // Delete
            var btnDel = new Button
            {
                Text = "🗑",
                Font = new Font("Segoe UI", 9F),
                ForeColor = AccRed,
                BackColor = Color.FromArgb(50, 28, 28),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(28, 28),
                Location = new Point(642, (RH - 28) / 2),
                Cursor = Cursors.Hand
            };
            btnDel.FlatAppearance.BorderSize = 0;
            var cap2 = item;
            btnDel.Click += (s, e) =>
            {
                _cart.Remove(cap2);
                RefreshCart();
                UpdateTotals();
            };
            row.Controls.Add(btnDel);

            row.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(row.Width, 1),
                Location = new Point(0, RH - 1)
            });
            return row;
        }

        // ══════════════════════════════════════════════════════════════════
        //  TOTALS
        // ══════════════════════════════════════════════════════════════════
        private void UpdateTotals()
        {
            decimal gross = _cart.Sum(i => i.UnitPrice * i.Qty);
            decimal discAmt = _cart.Sum(i => i.UnitPrice * i.Qty * i.DiscountPct / 100m);
            decimal after = gross - discAmt;
            decimal tax = Math.Round(after * _taxRate, 2);
            decimal grand = after + tax;

            lblItemCount.Text = $"{_cart.Count} item(s)  /  {_cart.Sum(i => i.Qty):F0} unit(s)";
            lblSubtotal.Text = Fmt(gross);
            lblDiscount.Text = "- " + Fmt(discAmt);
            lblTax.Text = Fmt(tax);
            lblGrand.Text = Fmt(grand);
        }

        private decimal GrandTotal()
        {
            decimal gross = _cart.Sum(i => i.UnitPrice * i.Qty);
            decimal discAmt = _cart.Sum(i => i.UnitPrice * i.Qty * i.DiscountPct / 100m);
            decimal after = gross - discAmt;
            return after + Math.Round(after * _taxRate, 2);
        }

        // ══════════════════════════════════════════════════════════════════
        //  SAVE
        // ══════════════════════════════════════════════════════════════════
        private void SaveQuotation()
        {
            if (_cart.Count == 0)
            { SetStatus("Cart is empty — add items first.", false); return; }

            string customer = txtCustomer.Text.Trim();
            if (string.IsNullOrWhiteSpace(customer)) customer = "Walk-in";

            try
            {
                QuotationRepository.EnsureSchema();
                string qNo = QuotationRepository.NextQuotationNo();

                decimal gross = _cart.Sum(i => i.UnitPrice * i.Qty);
                decimal discAmt = _cart.Sum(i => i.UnitPrice * i.Qty * i.DiscountPct / 100m);
                decimal after = gross - discAmt;
                decimal tax = Math.Round(after * _taxRate, 2);
                decimal grand = after + tax;

                var dto = new QuotationDto
                {
                    QuotationNo = qNo,
                    CustomerName = customer,
                    CustomerAddress = txtCustomerAddr.Text.Trim(),
                    CustomerVat = txtCustomerVat.Text.Trim(),
                    CurrencySymbol = _currencySymbol,
                    QuoteDate = DateTime.Now,
                    ValidUntil = DateTime.Now.AddDays(30),
                    Subtotal = gross,
                    DiscountTotal = discAmt,
                    TaxTotal = tax,
                    GrandTotal = grand,
                    Lines = _cart.Select(i => new QuotationLineDto
                    {
                        StockCode = i.StockCode,
                        Description = i.Name,
                        UOM = i.UOM,
                        Qty = i.Qty,
                        UnitPrice = i.UnitPrice,
                        DiscountPct = i.DiscountPct,
                        PriceGroup = i.PriceGroup
                    }).ToList()
                };

                QuotationRepository.SaveQuotation(dto, _companyId);

                _lastSavedQno = qNo;
                lblQuoteNo.Text = qNo;
                btnPrint.Enabled = true;

                SetStatus($"✓ Saved as {qNo}", true);
                ShowSavedDialog(qNo, customer, grand, dto);
            }
            catch (Exception ex)
            {
                SetStatus("Save error: " + ex.Message, false);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  SAVED DIALOG
        // ══════════════════════════════════════════════════════════════════
        private void ShowSavedDialog(string qNo, string customer,
            decimal grand, QuotationDto dto)
        {
            var dlg = MakeDialog(440, 300, "Quotation Saved");

            dlg.Controls.Add(MakeLbl("✅  Saved successfully!",
                new Font("Segoe UI", 11F, FontStyle.Bold), TextGreen,
                new Point(20, 56)));

            int ly = 96;
            void Row2(string l, string v, Color vc)
            {
                dlg.Controls.Add(MakeLbl(l + ":",
                    new Font("Segoe UI", 9F), TextMuted, new Point(20, ly)));
                dlg.Controls.Add(MakeLbl(v,
                    new Font("Segoe UI", 9F, FontStyle.Bold), vc, new Point(160, ly)));
                ly += 26;
            }
            Row2("Quotation No", qNo, AccOrange);
            Row2("Customer", customer, TextWhite);
            Row2("Total", Fmt(grand), TextGreen);
            Row2("Valid Until", DateTime.Now.AddDays(30)
                .ToString("dd MMM yyyy"), TextMuted);

            var btnPrintNow = MakeBigBtn("🖨  Print Quotation", AccBlue,
                new Rectangle(20, ly + 10, 190, 40));
            btnPrintNow.Click += (s, e) =>
            {
                dlg.Close();
                PrintQuotationDto(dto);
                ClearCart();
            };

            var btnDone = MakeBigBtn("✓  Done — New Quote", AccGreen,
                new Rectangle(222, ly + 10, 198, 40));
            btnDone.Click += (s, e) => { dlg.Close(); ClearCart(); };

            dlg.ClientSize = new Size(440, ly + 66);
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);
            dlg.Controls.AddRange(new Control[] { btnPrintNow, btnDone });
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
                { dlg.Close(); ClearCart(); }
            };
            dlg.ShowDialog(this);
        }

        private void ClearCart()
        {
            _cart.Clear();
            RefreshCart();
            UpdateTotals();
            txtCustomer.Clear();
            txtCustomerAddr.Clear();
            txtCustomerVat.Clear();
            nudDiscount.Value = 0;
            btnPrint.Enabled = false;
            _lastSavedQno = null;
            lblQuoteNo.Text = "";
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRINT
        // ══════════════════════════════════════════════════════════════════
        private void PrintLastSaved()
        {
            if (string.IsNullOrEmpty(_lastSavedQno)) return;
            var dto = QuotationRepository.GetFull(_lastSavedQno);
            if (dto != null) PrintQuotationDto(dto);
        }

        internal void PrintQuotationDto(QuotationDto dto)
        {
            var rd = QuotationPrintHelper.BuildReceiptData(dto,
                _companyName, _companyAddress, _companyPhone,
                _companyVat, _companyWebsite, _salesOfficeInfo);
            POSAPP.Invoice.PrintReceiptDialog.Show(this, rd);
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
        private string Fmt(decimal v) => $"{_currencySymbol} {v:F2}";

        private void SetStatus(string msg, bool ok)
        {
            if (lblStatus == null) return;
            lblStatus.Text = msg;
            lblStatus.ForeColor = ok ? TextGreen : AccRed;
        }

        private Label MakeLbl(string t, Font f, Color fc, Point loc = default)
        {
            var l = new Label
            {
                Text = t,
                Font = f,
                ForeColor = fc,
                BackColor = Color.Transparent,
                AutoSize = true
            };
            if (loc != default) l.Location = loc;
            return l;
        }

        private Panel MakeCard(Rectangle r)
        {
            var p = new Panel { BackColor = PanelDark, Bounds = r };
            Controls.Add(p);
            return p;
        }

        private Button MakeTitleBtn(string text, Point loc)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(46, 52),
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private Button MakeToggleBtn(string text, Point loc, bool active)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = active ? AccGreen : Color.FromArgb(44, 48, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(128, 28),
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = MakeRoundedRegion(b.Size, 6);
            return b;
        }

        private Button MakeBigBtn(string text, Color bg, Rectangle r)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Bounds = r,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = MakeRoundedRegion(b.Size, 8);
            return b;
        }

        private Label AddTotalRow(Panel p, string label, string val,
            ref int y, Color vc)
        {
            p.Controls.Add(new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(130, 22),
                Location = new Point(10, y),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var lv = new Label
            {
                Text = val,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = vc,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(130, 22),
                Location = new Point(140, y),
                TextAlign = ContentAlignment.MiddleRight
            };
            p.Controls.Add(lv);
            y += 26;
            return lv;
        }

        private void AddSep(Panel p, int y)
            => p.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(p.Width - 20, 1),
                Location = new Point(10, y)
            });

        private Form MakeDialog(int w, int h, string title)
        {
            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(w, h),
                ShowInTaskbar = false,
                KeyPreview = true
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            var pHead = new Panel
            {
                BackColor = PanelDark,
                Size = new Size(w, 48),
                Location = Point.Empty
            };
            pHead.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(w - 44, 48),
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var bx = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(44, 48),
                Location = new Point(w - 44, 0)
            };
            bx.FlatAppearance.BorderSize = 0;
            bx.Click += (s, e) => dlg.Close();
            pHead.Controls.Add(bx);
            dlg.Controls.Add(pHead);
            return dlg;
        }

        private TextBox AddDialogField(Form dlg, string label, string val,
            ref int y, Color accent)
        {
            dlg.Controls.Add(new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, y)
            });
            var tb = new TextBox
            {
                Text = val,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = accent,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(dlg.ClientSize.Width - 40, 30),
                Location = new Point(20, y + 18)
            };
            tb.Enter += (s, e) => tb.SelectAll();
            dlg.Controls.Add(tb);
            var bar = new Panel
            {
                BackColor = Color.FromArgb(55, 60, 80),
                Size = new Size(dlg.ClientSize.Width - 40, 2),
                Location = new Point(20, y + 49)
            };
            tb.Enter += (s, e) => bar.BackColor = accent;
            tb.Leave += (s, e) => bar.BackColor = Color.FromArgb(55, 60, 80);
            dlg.Controls.Add(bar);
            y += 58;
            return tb;
        }

        private Region MakeRoundedRegion(Size size, int r)
        {
            int d = r * 2;
            var path = new GraphicsPath();
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(size.Width - d, 0, d, d, 270, 90);
            path.AddArc(size.Width - d, size.Height - d, d, d, 0, 90);
            path.AddArc(0, size.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  QuotationPrintHelper
    // ══════════════════════════════════════════════════════════════════════
    public static class QuotationPrintHelper
    {
        public static POSAPP.Invoice.ReceiptData BuildReceiptData(
            QuotationDto dto,
            string companyName, string companyAddress, string companyPhone,
            string companyVat, string companyWebsite, string salesOfficeInfo)
        {
            var rd = new POSAPP.Invoice.ReceiptData
            {
                InvoiceNo = dto.QuotationNo,
                CompanyName = companyName,
                CompanyAddress = companyAddress,
                CompanyPhone = companyPhone,
                CompanyVat = companyVat,
                CompanyWebsite = companyWebsite,
                SalesOfficeInfo = salesOfficeInfo,
                CustomerName = dto.CustomerName,
                CustomerAddress = dto.CustomerAddress,
                CustomerVat = dto.CustomerVat,
                CashierName = "Quotation",
                SaleDate = dto.QuoteDate,
                CurrencySymbol = dto.CurrencySymbol,
                Subtotal = dto.Subtotal,
                DiscountTotal = dto.DiscountTotal,
                TaxTotal = dto.TaxTotal,
                GrandTotal = dto.GrandTotal,
                FooterLine1 = $"Valid Until: {dto.ValidUntil?.ToString("dd MMM yyyy") ?? "30 days"}",
                FooterLine2 = "This is a quotation, not a tax invoice."
            };

            foreach (var l in dto.Lines)
                rd.Lines.Add(new POSAPP.Invoice.ReceiptLine
                {
                    StockCode = l.StockCode,
                    Name = l.Description,
                    Qty = (int)l.Qty,
                    UnitPrice = l.UnitPrice,
                    DiscountPct = l.DiscountPct,
                    LineTotal = l.LineTotal,
                    UOM = l.UOM,
                    ListPrice = l.UnitPrice
                });

            return rd;
        }
    }
}