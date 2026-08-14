// ProductCatalogForm.cs
//
// NuGet packages required:
//   ClosedXML   -> Excel export   (Install-Package ClosedXML)
//   itext7      -> PDF export     (Install-Package itext7)
//
// This form now loads data exactly the way the React "StoreStock" page does:
//   - Promise.all-style parallel fetch of GET /api/stock?companyId={id} and
//     GET /api/Item (mirrors getStocks(companyId) + getItems()).
//   - Same isSuccess:false / data-unwrap guarding as the React fetchStocks().
//   - Same join: build an item map keyed by itemID, then map over the raw
//     stock array attaching sku/itemName/uom from the matching item (with
//     the same "ITEM-{id}" / "Item #{id}" fallbacks the React code uses).
//   - The old /api/Product-based load, and the Site/Warehouse/Price Group
//     filters and Unit Price column that only existed because of it, have
//     been removed — there's no equivalent data from /api/stock or /api/Item.
//   - Cost Price stays as a local, user-editable column (it was never wired
//     to an API in the original code either).
//   - Adds a Close button next to Refresh.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClosedXML.Excel;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace POSAPP.Inventory
{
    public partial class ProductCatalogForm : Form
    {
        // ── Config ───────────────────────────────────────────────────────────
        private readonly string _apiBaseUrl;
        private readonly int _companyId;

        // Currently loaded / filtered rows
        private List<StockItemRow> _allRows = new();
        private List<StockItemRow> _displayRows = new();

        // For debouncing search
        private System.Windows.Forms.Timer _searchTimer;

        // ── Low / out-of-stock thresholds (mirrors React: <=50 low, ==0 out) ──
        private const int LOW_STOCK_THRESHOLD = 50;

        // ──────────────────────────────────────────────────────────────────────
        public ProductCatalogForm(string apiBaseUrl, int companyId = 2)
        {
            _apiBaseUrl = apiBaseUrl;
            _companyId = companyId;
            InitializeComponent();
            SetupSearchTimer();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Designer-replacement: build everything in code so no .Designer.cs needed
        // ══════════════════════════════════════════════════════════════════════
        private DataGridView dgv;
        private TextBox txtSearch;
        private Label lblSearch, lblStatus;
        private Button btnRefresh, btnClose, btnExportExcel, btnExportPdf;
        private Panel pnlTop;
        private TableLayoutPanel pnlCards;

        // Analytics card value labels (refreshed whenever data loads/filters)
        private Label lblTotalSkuValue, lblTotalQtyValue, lblLowStockValue, lblOutOfStockValue;

        private void InitializeComponent()
        {
            SuspendLayout();

            // ── Form ─────────────────────────────────────────────────────────
            Text = "Inventory";
            Size = new Size(1300, 760);
            MinimumSize = new Size(950, 550);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(245, 247, 250);

            // ── Top panel ────────────────────────────────────────────────────
            pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.FromArgb(30, 60, 114),
                Padding = new Padding(10, 0, 10, 0)
            };

            var lblTitle = new Label
            {
                Text = "📦  Current Stock",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 14)
            };

            // As with the export buttons below, Location was computed from
            // pnlTop.Width before pnlTop was Docked (so it read the WinForms
            // default width, not the real one) — wrap in a fixed-width
            // Dock=Right container so Dock (not a mis-anchored margin)
            // positions these correctly at any form width.
            const int topBtnGap = 10;
            var pnlTopBtns = new Panel
            {
                Dock = DockStyle.Right,
                Width = 95 + topBtnGap + 90,
                BackColor = Color.Transparent
            };

            btnRefresh = new Button
            {
                Text = "⟳  Refresh",
                Size = new Size(95, 32),
                Location = new Point(0, 14),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 100, 170),
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(80, 130, 200);
            btnRefresh.Click += async (s, e) => await LoadStockAsync();

            btnClose = new Button
            {
                Text = "✕  Close",
                Size = new Size(90, 32),
                Location = new Point(95 + topBtnGap, 14),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(200, 60, 60),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(220, 90, 90);
            btnClose.Click += (s, e) => Close();

            pnlTopBtns.Controls.AddRange(new Control[] { btnRefresh, btnClose });

            pnlTop.Controls.AddRange(new Control[] { lblTitle, pnlTopBtns });

            // ── Analytics cards (mirrors React AnalyticsCard row) ──────────────
            pnlCards = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 100,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding = new Padding(6, 10, 6, 6),
                ColumnCount = 4,
                RowCount = 1
            };
            for (int i = 0; i < 4; i++)
                pnlCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            pnlCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Cards are Dock=Fill inside equal-percent TableLayoutPanel columns
            // (instead of fixed 245px-wide panels at hardcoded x-offsets), so
            // the row scales correctly at any window width — including the
            // form's own MinimumSize, where the old fixed x=790 4th card would
            // have been clipped off the right edge.
            var cardTotalSku = MakeAnalyticsCard("Total SKU", "All stock keeping units",
                Color.FromArgb(37, 99, 235), out lblTotalSkuValue);
            var cardTotalQty = MakeAnalyticsCard("Total Qty", "Total available quantity",
                Color.FromArgb(13, 148, 136), out lblTotalQtyValue);
            var cardLowStock = MakeAnalyticsCard("Low Stock", "Items below stock level",
                Color.FromArgb(217, 119, 6), out lblLowStockValue);
            var cardOutOfStock = MakeAnalyticsCard("Out Of Stock", "Items currently unavailable",
                Color.FromArgb(220, 38, 38), out lblOutOfStockValue);

            var cardGap = new Padding(4, 0, 4, 0);
            cardTotalSku.Margin = new Padding(0, 0, 4, 0);
            cardTotalQty.Margin = cardGap;
            cardLowStock.Margin = cardGap;
            cardOutOfStock.Margin = new Padding(4, 0, 0, 0);

            pnlCards.Controls.Add(cardTotalSku, 0, 0);
            pnlCards.Controls.Add(cardTotalQty, 1, 0);
            pnlCards.Controls.Add(cardLowStock, 2, 0);
            pnlCards.Controls.Add(cardOutOfStock, 3, 0);

            // ── Search / export bar ─────────────────────────────────────────
            var pnlFilter = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.White,
                Padding = new Padding(10, 8, 10, 8)
            };
            pnlFilter.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(220, 220, 220)),
                    0, pnlFilter.Height - 1, pnlFilter.Width, pnlFilter.Height - 1);
            };

            int x = 10;
            lblSearch = MakeLabel("Search:", ref x, pnlFilter.Height);
            txtSearch = MakeTextBox(ref x, 260, pnlFilter.Height);
            txtSearch.PlaceholderText = "SKU / Item / UOM…";
            txtSearch.TextChanged += (s, e) => { _searchTimer.Stop(); _searchTimer.Start(); };

            // These two buttons used to compute Location from pnlFilter.Width
            // and rely on Anchor=Top|Right — but pnlFilter.Width is still just
            // the WinForms default at this point (pnlFilter hasn't been Docked
            // yet), so the anchor margin got captured against the wrong width
            // and could put the buttons off-screen once the form reached its
            // real size. Wrapping them in a small Dock=Right container (whose
            // own Width is a known fixed constant, not dependent on the
            // parent's eventual docked size) sidesteps that entirely — the
            // container is always positioned correctly by Dock, and the
            // buttons inside it use fixed offsets relative to that known width.
            const int btnW = 90, btnGap = 8;
            var pnlExportBtns = new Panel
            {
                Dock = DockStyle.Right,
                Width = btnW * 2 + btnGap,
                BackColor = Color.Transparent
            };

            btnExportExcel = new Button
            {
                Text = "Excel",
                Size = new Size(btnW, 32),
                Location = new Point(0, (pnlFilter.Height - 32) / 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnExportExcel.FlatAppearance.BorderColor = Color.FromArgb(15, 120, 55);
            btnExportExcel.Click += (s, e) => ExportExcel();

            btnExportPdf = new Button
            {
                Text = "PDF",
                Size = new Size(btnW, 32),
                Location = new Point(btnW + btnGap, (pnlFilter.Height - 32) / 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnExportPdf.FlatAppearance.BorderColor = Color.FromArgb(180, 30, 30);
            btnExportPdf.Click += (s, e) => ExportPdf();

            pnlExportBtns.Controls.AddRange(new Control[] { btnExportExcel, btnExportPdf });

            pnlFilter.Controls.AddRange(new Control[] { lblSearch, txtSearch, pnlExportBtns });

            // ── Status bar ───────────────────────────────────────────────────
            var pnlStatus = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Color.FromArgb(245, 247, 250)
            };
            pnlStatus.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Color.FromArgb(220, 220, 220)), 0, 0, pnlStatus.Width, 0);

            lblStatus = new Label
            {
                Text = "Ready",
                AutoSize = true,
                Location = new Point(12, 6),
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            pnlStatus.Controls.Add(lblStatus);

            // ── DataGridView ─────────────────────────────────────────────────
            dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Color.White,
                GridColor = Color.FromArgb(230, 230, 230),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 36,
                RowTemplate = { Height = 30 },
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                AutoGenerateColumns = false
            };

            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 60, 114);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            dgv.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgv.EnableHeadersVisualStyles = false;

            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(200, 220, 255);
            dgv.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 255);

            dgv.CellFormatting += Dgv_CellFormatting;

            BuildColumns();

            // ── Assemble ─────────────────────────────────────────────────────
            Controls.Add(dgv);
            Controls.Add(pnlFilter);
            Controls.Add(pnlCards);
            Controls.Add(pnlTop);
            Controls.Add(pnlStatus);

            ResumeLayout(false);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Analytics card factory (mirrors React <AnalyticsCard />)
        // ──────────────────────────────────────────────────────────────────────
        private Panel MakeAnalyticsCard(string title, string subtitle, Color accent,
            out Label valueLabel)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            card.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(230, 230, 230));
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var stripe = new Panel
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = accent
            };

            var lblTitle = new Label
            {
                Text = title.ToUpperInvariant(),
                Location = new Point(16, 12),
                AutoSize = true,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120)
            };

            valueLabel = new Label
            {
                Text = "0",
                Location = new Point(16, 30),
                AutoSize = true,
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30)
            };

            var lblSub = new Label
            {
                Text = subtitle,
                Location = new Point(16, 62),
                AutoSize = true,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(150, 150, 150)
            };

            card.Controls.AddRange(new Control[] { stripe, lblTitle, valueLabel, lblSub });
            return card;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Column definitions — mirrors the React StoreStock table
        // ──────────────────────────────────────────────────────────────────────
        private void BuildColumns()
        {
            dgv.Columns.Clear();

            dgv.Columns.Add(MakeCol("Sku", "SKU", 100));
            dgv.Columns.Add(MakeCol("ItemName", "SKU Name", 220));
            dgv.Columns.Add(MakeNumCol("Uom", "UOM", 70));
            dgv.Columns.Add(MakeNumCol("Opening", "Opening", 80));
            dgv.Columns.Add(MakeNumCol("Received", "Received", 80));
            dgv.Columns.Add(MakeNumCol("Sales", "Sales", 80));
            dgv.Columns.Add(MakeNumCol("SalesReturn", "Sales Return", 90));
            dgv.Columns.Add(MakeNumCol("PurchaseReturn", "Purchase Return", 100));

            var colOnHand = new DataGridViewTextBoxColumn
            {
                Name = "OnHand",
                HeaderText = "On Hand",
                DataPropertyName = "OnHand",
                ReadOnly = true,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
                FillWeight = 80
            };
            dgv.Columns.Add(colOnHand);
        }

        private static DataGridViewTextBoxColumn MakeCol(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                DataPropertyName = name,
                ReadOnly = true,
                MinimumWidth = width,
                FillWeight = width
            };
        }

        private static DataGridViewTextBoxColumn MakeNumCol(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                DataPropertyName = name,
                ReadOnly = true,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
                MinimumWidth = width,
                FillWeight = width
            };
        }

        // Color-code the On Hand cell: red = out, orange = low (<=50), green = ok
        private void Dgv_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (dgv.Columns[e.ColumnIndex].Name != "OnHand") return;
            if (e.RowIndex < 0 || e.RowIndex >= _displayRows.Count) return;

            var onHand = _displayRows[e.RowIndex].OnHand;
            e.CellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);

            if (onHand == 0)
            {
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(220, 38, 38);
            }
            else if (onHand <= LOW_STOCK_THRESHOLD)
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
                e.CellStyle.ForeColor = Color.FromArgb(217, 119, 6);
            }
            else
            {
                e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                e.CellStyle.ForeColor = Color.FromArgb(22, 163, 74);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Load — mirrors React fetchStocks():
        //    const [stockRes, itemsRes] = await Promise.all([getStocks(companyId), getItems()]);
        // ──────────────────────────────────────────────────────────────────────
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await LoadStockAsync();
        }

        private async Task LoadStockAsync()
        {
            SetStatus("Loading stock…", Color.Orange);
            btnRefresh.Enabled = false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

                string stockUrl = $"{_apiBaseUrl}/api/stock?companyId={_companyId}";
                string itemsUrl = $"{_apiBaseUrl}/api/Item";

                // Promise.all([getStocks(companyId), getItems()])
                var stockTask = http.GetAsync(stockUrl);
                var itemsTask = http.GetAsync(itemsUrl);
                await Task.WhenAll(stockTask, itemsTask).ConfigureAwait(false);

                var stockResp = await stockTask;
                var itemsResp = await itemsTask;

                // ── Stock response ──────────────────────────────────────────
                if (!stockResp.IsSuccessStatusCode)
                {
                    string errBody = await stockResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    SetStatus($"Stock API error {(int)stockResp.StatusCode}: {errBody}", Color.Red);
                    Invoke(() => { _allRows = new(); ApplyFilter(); });
                    return;
                }

                string stockJson = await stockResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var stockRaw = ParseStockRaw(stockJson, out string? stockErrorMessage);

                // Mirrors: if (stockBody.isSuccess === false) { toast.error(...); setStockData([]); return; }
                if (stockErrorMessage != null)
                {
                    SetStatus(stockErrorMessage, Color.Red);
                    Invoke(() => { _allRows = new(); ApplyFilter(); });
                    return;
                }

                // ── Items response ──────────────────────────────────────────
                var itemMap = new Dictionary<string, ItemDto>(StringComparer.OrdinalIgnoreCase);
                if (itemsResp.IsSuccessStatusCode)
                {
                    string itemsJson = await itemsResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var items = ParseItems(itemsJson);
                    foreach (var it in items)
                        itemMap[it.ItemId] = it; // itemMap[item.itemID] = item;
                }
                else
                {
                    string errBody = await itemsResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Console.WriteLine($"Item API error {(int)itemsResp.StatusCode}: {errBody}");
                }

                // ── Join, same shape as the React `formatted` map ───────────
                var formatted = stockRaw.Select(s =>
                {
                    itemMap.TryGetValue(s.ItemId, out var details);

                    string sku = !string.IsNullOrEmpty(details?.Sku) ? details!.Sku
                               : !string.IsNullOrEmpty(s.ItemCode) ? s.ItemCode
                               : $"ITEM-{s.ItemId}";

                    string itemName = !string.IsNullOrEmpty(details?.ItemName) ? details!.ItemName
                                     : !string.IsNullOrEmpty(s.ItemName) ? s.ItemName
                                     : $"Item #{s.ItemId}";

                    return new StockItemRow
                    {
                        Id = s.PkStockId,
                        ItemId = s.ItemId,
                        Sku = sku,
                        ItemName = itemName,
                        Uom = string.IsNullOrEmpty(s.Uom) ? "-" : s.Uom,
                        Opening = s.Opening,
                        Received = s.Received,
                        Sales = s.Sales,
                        SalesReturn = s.SalesReturn,
                        PurchaseReturn = s.PurchaseReturn,
                        OnHand = s.OnHand
                    };
                }).ToList();

                Invoke(() =>
                {
                    _allRows = formatted;
                    ApplyFilter();
                    SetStatus($"✓ {formatted.Count} stock records loaded.", Color.FromArgb(0, 128, 0));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Stock fetch error: " + ex.Message);
                SetStatus("Failed to load stock data: " + ex.Message, Color.Red);
                Invoke(() => { _allRows = new(); ApplyFilter(); });
            }
            finally
            {
                Invoke(() => btnRefresh.Enabled = true);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Parse JSON → raw StockDto list
        //  Mirrors the guards in the React fetchStocks():
        //    { isSuccess: false, message }  -> error, empty list
        //    { data: [...] } or { isSuccess: true, data: [...] } -> unwrap
        //    [...]                          -> plain array
        // ──────────────────────────────────────────────────────────────────────
        private static List<StockDto> ParseStockRaw(string json, out string? errorMessage)
        {
            errorMessage = null;
            var list = new List<StockDto>();

            JsonElement root;
            try { root = JsonSerializer.Deserialize<JsonElement>(json); }
            catch { errorMessage = "Unexpected response from stock API"; return list; }

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("isSuccess", out var successEl) &&
                successEl.ValueKind == JsonValueKind.False)
            {
                errorMessage = root.TryGetProperty("message", out var msgEl)
                    ? (msgEl.GetString() ?? "Failed to load stock data")
                    : "Failed to load stock data";
                return list;
            }

            JsonElement arr = root;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl))
                arr = dataEl;

            if (arr.ValueKind != JsonValueKind.Array)
            {
                errorMessage = "Unexpected response from stock API";
                return list;
            }

            foreach (var item in arr.EnumerateArray())
            {
                string S(string key) =>
                    item.TryGetProperty(key, out var p)
                        ? (p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.ToString())
                        : "";

                decimal D(string key) =>
                    item.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number
                        ? p.GetDecimal() : 0m;

                list.Add(new StockDto
                {
                    PkStockId = S("pkStockID"),
                    ItemId = S("itemID"),
                    ItemCode = S("itemCode"),
                    ItemName = S("itemName"),
                    Uom = S("uom"),
                    Opening = D("openingStk"),
                    Received = D("receivedQty"),
                    Sales = D("saleQty"),
                    SalesReturn = D("salesReturnQty"),
                    PurchaseReturn = D("purchaseReturnQty"),
                    OnHand = D("onHandQty"),
                });
            }

            return list;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Parse JSON → ItemDto list
        //  Mirrors: const items = itemsRes?.data?.data || itemsRes?.data || [];
        // ──────────────────────────────────────────────────────────────────────
        private static List<ItemDto> ParseItems(string json)
        {
            var list = new List<ItemDto>();

            JsonElement root;
            try { root = JsonSerializer.Deserialize<JsonElement>(json); }
            catch { return list; }

            JsonElement arr = root;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl))
                arr = dataEl;

            if (arr.ValueKind != JsonValueKind.Array) return list;

            foreach (var item in arr.EnumerateArray())
            {
                string S(string key) =>
                    item.TryGetProperty(key, out var p) ? p.GetString() ?? "" : "";
                string SAny(string key) =>
                    item.TryGetProperty(key, out var p)
                        ? (p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.ToString())
                        : "";

                list.Add(new ItemDto
                {
                    ItemId = SAny("itemID"),
                    Sku = S("sku"),
                    ItemName = S("itemName"),
                });
            }

            return list;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Filter — mirrors the React filteredData useMemo
        // ──────────────────────────────────────────────────────────────────────
        private void ApplyFilter()
        {
            string term = txtSearch.Text.Trim();

            var display = string.IsNullOrEmpty(term)
                ? _allRows.ToList()
                : _allRows.Where(item =>
                    item.Sku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.ItemName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Uom.Contains(term, StringComparison.OrdinalIgnoreCase))
                  .ToList();

            _displayRows = display;

            dgv.DataSource = null;
            dgv.DataSource = display;

            UpdateAnalytics();

            lblStatus.Text = $"Showing {display.Count} of {_allRows.Count} items";
        }

        // Recomputes the 4 analytics cards (mirrors React `stats` useMemo, over ALL rows not just filtered)
        private void UpdateAnalytics()
        {
            int totalSku = _allRows.Count;
            decimal totalQty = _allRows.Sum(i => i.OnHand);
            int lowStock = _allRows.Count(i => i.OnHand > 0 && i.OnHand <= LOW_STOCK_THRESHOLD);
            int outOfStock = _allRows.Count(i => i.OnHand == 0);

            lblTotalSkuValue.Text = totalSku.ToString("N0");
            lblTotalQtyValue.Text = totalQty.ToString("N0");
            lblLowStockValue.Text = lowStock.ToString("N0");
            lblOutOfStockValue.Text = outOfStock.ToString("N0");
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Export — Excel (ClosedXML), mirrors exportExcel() in React
        // ──────────────────────────────────────────────────────────────────────
        private void ExportExcel()
        {
            if (_displayRows.Count == 0)
            {
                MessageBox.Show("No data to export.", "Export Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = "Store_Stock_Report.xlsx"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Store Stock");

                string[] headers =
                {
                    "S.No", "SKU", "Item", "UOM", "Opening", "Received",
                    "Sales", "Sales Return", "Purchase Return", "On Hand"
                };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = headers[c];
                    ws.Cell(1, c + 1).Style.Font.Bold = true;
                    ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(0, 40, 120);
                    ws.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
                }

                int row = 2;
                int sno = 1;
                foreach (var item in _displayRows)
                {
                    ws.Cell(row, 1).Value = sno++;
                    ws.Cell(row, 2).Value = item.Sku;
                    ws.Cell(row, 3).Value = item.ItemName;
                    ws.Cell(row, 4).Value = item.Uom;
                    ws.Cell(row, 5).Value = item.Opening;
                    ws.Cell(row, 6).Value = item.Received;
                    ws.Cell(row, 7).Value = item.Sales;
                    ws.Cell(row, 8).Value = item.SalesReturn;
                    ws.Cell(row, 9).Value = item.PurchaseReturn;
                    ws.Cell(row, 10).Value = item.OnHand;
                    row++;
                }

                ws.Columns().AdjustToContents();
                wb.SaveAs(sfd.FileName);

                MessageBox.Show("Excel exported successfully", "Export Excel",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Excel export failed: " + ex.Message, "Export Excel",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Export — PDF (iText7), mirrors exportPDF() in React
        // ──────────────────────────────────────────────────────────────────────
        private void ExportPdf()
        {
            if (_displayRows.Count == 0)
            {
                MessageBox.Show("No data to export.", "Export PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                FileName = "Store_Stock_Report.pdf"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            try
            {
                using var writer = new PdfWriter(sfd.FileName);
                using var pdf = new PdfDocument(writer);
                var doc = new Document(pdf, iText.Kernel.Geom.PageSize.A4.Rotate());

                var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

                doc.Add(new Paragraph("Store Stock Report")
                    .SetFontSize(16)
                    .SetFont(boldFont));

                string[] headers =
                {
                    "S.No", "SKU", "Item", "UOM", "Opening", "Received",
                    "Sales", "Sales Return", "Purchase Return", "On Hand"
                };

                var table = new Table(headers.Length).UseAllAvailableWidth();
                var headerColor = new iText.Kernel.Colors.DeviceRgb(0, 40, 120);

                foreach (var h in headers)
                {
                    table.AddHeaderCell(new Cell()
                        .Add(new Paragraph(h).SetFontColor(iText.Kernel.Colors.ColorConstants.WHITE).SetFontSize(8))
                        .SetBackgroundColor(headerColor));
                }

                int sno = 1;
                foreach (var item in _displayRows)
                {
                    table.AddCell(new Cell().Add(new Paragraph(sno++.ToString()).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.Sku).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.ItemName).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.Uom).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.Opening.ToString("N0")).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.Received.ToString("N0")).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.Sales.ToString("N0")).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.SalesReturn.ToString("N0")).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.PurchaseReturn.ToString("N0")).SetFontSize(8)));
                    table.AddCell(new Cell().Add(new Paragraph(item.OnHand.ToString("N0")).SetFontSize(8)));
                }

                doc.Add(table);
                doc.Close();

                MessageBox.Show("PDF exported successfully", "Export PDF",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("PDF export failed: " + ex.Message, "Export PDF",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────────
        private void SetupSearchTimer()
        {
            _searchTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); ApplyFilter(); };
        }

        private void SetStatus(string msg, Color color)
        {
            if (InvokeRequired) { Invoke(() => SetStatus(msg, color)); return; }
            lblStatus.Text = msg;
            lblStatus.ForeColor = color;
        }

        private static Label MakeLabel(string text, ref int x, int panelH)
        {
            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, (panelH - 15) / 2),
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            x += lbl.PreferredWidth + 4;
            return lbl;
        }

        private static TextBox MakeTextBox(ref int x, int width, int panelH)
        {
            var tb = new TextBox
            {
                Size = new Size(width, 26),
                Location = new Point(x, (panelH - 26) / 2),
                BorderStyle = BorderStyle.FixedSingle
            };
            x += width + 4;
            return tb;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Data models
    // ══════════════════════════════════════════════════════════════════════════

    // Raw shape returned by GET /api/stock?companyId={id}
    public class StockDto
    {
        public string PkStockId { get; set; } = "";
        public string ItemId { get; set; } = "";
        public string ItemCode { get; set; } = "";   // fallback if not found in item map
        public string ItemName { get; set; } = "";   // fallback if not found in item map
        public string Uom { get; set; } = "-";
        public decimal Opening { get; set; }
        public decimal Received { get; set; }
        public decimal Sales { get; set; }
        public decimal SalesReturn { get; set; }
        public decimal PurchaseReturn { get; set; }
        public decimal OnHand { get; set; }
    }

    // Raw shape returned by GET /api/Item
    public class ItemDto
    {
        public string ItemId { get; set; } = "";
        public string Sku { get; set; } = "";
        public string ItemName { get; set; } = "";
    }

    // Final joined row shown in the grid — mirrors React's `formatted` object
    public class StockItemRow
    {
        public string Id { get; set; } = "";
        public string ItemId { get; set; } = "";
        public string Sku { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string Uom { get; set; } = "-";
        public decimal Opening { get; set; }
        public decimal Received { get; set; }
        public decimal Sales { get; set; }
        public decimal SalesReturn { get; set; }
        public decimal PurchaseReturn { get; set; }
        public decimal OnHand { get; set; }
    }
}