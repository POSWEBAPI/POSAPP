using POSAPP.Invoice;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace POSAPP.Sales
{
    // ════════════════════════════════════════════════════════════════════════
    //  SALES RETURN — v2, rebuilt as a LIST + DETAIL screen (Zoho-Books style)
    //  instead of the earlier stacked-card wizard.
    //
    //      LEFT  : searchable list of orders (one row per customer invoice),
    //              each showing customer name, order/invoice no + date,
    //              amount, and a status word (CONFIRMED / RETURNED), exactly
    //              the layout used in the reference screenshot's order list.
    //
    //      RIGHT : detail panel for the selected order.
    //              - Toolbar: Edit / Email / PDF·Print / Create ▾
    //                (Create ▾ opens a menu with a single "Sales Return" item,
    //                 mirroring the dropdown in the screenshot)
    //              - "Order Detail" view: order # header, status chips for
    //                Order / Invoice / Payment / Shipment, order meta
    //                (date, register, fulfillment, payment terms, channel),
    //                and Billing / Shipping address cards.
    //              - Clicking "Sales Return" swaps the same panel into the
    //                "Return Builder" view: the invoice's returnable lines
    //                (Return Qty + Add), the return cart, reason / RMA /
    //                disposition fields, refund totals, and Process/Cancel.
    //
    //  ASSUMPTIONS / REPOSITORY CONTRACT (same repository surface as before):
    //      SalesReturnRepository.GetActiveCustomersAsync(companyId)
    //      SalesReturnRepository.GetInvoicesForCustomerAsync(customerId)
    //      SalesReturnRepository.GetReturnedQtysAsync(invoiceNo)
    //      SalesReturnRepository.IsFullyReturned(invoiceNo)
    //      SalesReturnRepository.NextReturnInvoiceNo()
    //      SalesReturnRepository.EnsureSchema() / SaveReturn(record)
    //      CustomerFullDto           : CustomerID, CustomerName
    //      InvoiceWithLines          : Header (InvoiceHeader), Lines
    //      InvoiceHeader             : InvoiceNo, InvoiceDate, LineCount, Total
    //  Fields such as Payment/Invoice/Shipment status and Billing/Shipping
    //  address aren't tracked by this schema yet, so those are shown as
    //  reasonable static labels ("Paid", "Invoiced", "Fulfilled") — swap in
    //  real fields once the repository exposes them (marked with TODO below).
    // ════════════════════════════════════════════════════════════════════════
    public class SalesReturnForm : Form
    {
        // ── Palette (LIGHT / TEAL theme — matches the "Return Wizard" reference) ──
        private static readonly Color BgPage = Color.FromArgb(247, 245, 240);       // warm off-white page background
        private static readonly Color PanelWhite = Color.FromArgb(255, 255, 255);   // card / panel background
        private static readonly Color PanelAlt = Color.FromArgb(244, 244, 240);     // alt row / hover background
        private static readonly Color BorderColor = Color.FromArgb(226, 224, 216);
        private static readonly Color TextDark = Color.FromArgb(31, 41, 38);        // primary text (near-black, cool tint)
        private static readonly Color TextMid = Color.FromArgb(110, 122, 116);
        private static readonly Color TextLight = Color.FromArgb(160, 170, 164);
        private static readonly Color AccentBlue = Color.FromArgb(45, 149, 130);    // primary accent -> teal
        private static readonly Color AccentBlueLight = Color.FromArgb(222, 241, 236); // accent-tinted background
        private static readonly Color AccGreen = Color.FromArgb(45, 149, 130);
        private static readonly Color AccGreenLight = Color.FromArgb(222, 241, 236);
        private static readonly Color AccRed = Color.FromArgb(214, 69, 69);
        private static readonly Color AccRedLight = Color.FromArgb(252, 231, 231);
        private static readonly Color AccAmber = Color.FromArgb(214, 130, 30);
        private static readonly Color RowHoverBg = Color.FromArgb(248, 247, 243);
        private static readonly Color RowSelectedBg = Color.FromArgb(222, 241, 236);
        private static readonly Color InputBg = Color.FromArgb(250, 250, 248);
        private static readonly Color InputBorder = Color.FromArgb(210, 214, 206);

        // ── Reason / Disposition option lists ───────────────────────────────
        private static readonly string[] ReturnReasonOptions =
        {
            "Damaged Goods", "Wrong Item Shipped", "Excess Stock", "Quality Issue",
            "Expired Product", "Pricing Discrepancy", "Customer Cancellation", "Other"
        };
        private static readonly string[] DispositionOptions =
        {
            "Credit", "Scrap", "Return to Vendor", "Replace", "Repair", "Restock"
        };

        // ── Company / context ───────────────────────────────────────────────
        private readonly int _companyId;
        private string _currencySymbol = "P";
        private string _companyName = "";
        private string _companyAddress = "";
        private string _companyPhone = "";
        private string _companyVat = "";
        private string _companyWebsite = "";
        private string _salesOfficeInfo = "";

        // ── Order list state ────────────────────────────────────────────────
        private class OrderRow
        {
            public CustomerFullDto Customer;
            public InvoiceWithLines Invoice;
            public bool IsFullyReturned;
        }
        private List<OrderRow> _allOrders = new List<OrderRow>();
        private OrderRow _selectedOrder;
        private Panel _selectedRowPanel;

        // ── Return-in-progress state (same shape as before) ─────────────────
        private int _selectedCustomerId;
        private string _selectedCustomerName = "";
        private string _currentInvoiceNo = "";
        private List<InvoiceLineCandidate> _currentInvoiceLines = new List<InvoiceLineCandidate>();
        private List<ReturnCartLine> _cartLines = new List<ReturnCartLine>();
        private string _refundMethod = "cash";
        private string _returnReason = "";
        private string _rmaNumber = "";
        private string _dispositionCode = "";

        private bool _drag;
        private Point _dragCursor, _dragForm;
        private bool _isMaximized = false;
        private Rectangle _normalBounds;

        // ── Chrome ───────────────────────────────────────────────────────────
        private Panel panelHeader;
        private Label lblTitle;
        private Button btnMaximize;

        // ── Left list ────────────────────────────────────────────────────────
        private const int LIST_W_DEFAULT = 330;
        private const int LIST_W_MIN = 280;
        private Panel pnlLeftList;
        private TextBox txtOrderSearch;
        private ComboBox cmbCustomerFilter;
        private Panel pnlOrderRowsScroll;
        private Panel pnlOrderRows;
        private Label lblOrdersEmpty;

        // ── Right detail ─────────────────────────────────────────────────────
        private Panel pnlRightDetail;
        private Panel pnlDetailToolbar;
        private Label lblOrderNumberHeader;
        private Button btnEdit, btnEmail, btnPdfPrint, btnCreateMenu;
        private ContextMenuStrip mnuCreate;
        private Panel pnlDetailBody;               // hosts either order-detail or return-builder
        private Panel pnlOrderDetailView;
        private Panel pnlReturnBuilderView;
        private Label lblNoSelection;

        // Sticky footer (Process Return / Cancel) — lives OUTSIDE the scrollable
        // pnlDetailBody, docked to the bottom of pnlRightDetail, so the action
        // buttons are always reachable regardless of scroll position or window size.
        private Panel pnlBuilderFooter;

        // Return-builder inner controls
        private Label lblReturnBuilderInvoice;
        private Panel pnlInvoiceLinesHeader, panelInvoiceLineRows;
        private Panel panelCartRows;
        private Label lblCartEmpty;
        private ComboBox cmbReturnReason;
        private TextBox txtRma;
        private ComboBox cmbDisposition;
        private Button btnMethodCash;
        private Label _lblSummarySubtotal, _lblSummaryTax, _lblSummaryTotal;
        private Label lblBuilderStatus;
        private Button btnProcessReturn, btnCancelReturn;
        private Label lblRefundTotalFooter;

        private const int HEADER_H = 52;
        private const int CARD_RADIUS = 14;
        private const int CARD_PAD = 20;   // inner padding used consistently for card content placement

        // ══════════════════════════════════════════════════════════════════
        //  MODELS (unchanged from v1)
        // ══════════════════════════════════════════════════════════════════
        private class InvoiceLineCandidate
        {
            public string ItemName { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountPct { get; set; }
            public decimal TaxPct { get; set; }
            public string UOM { get; set; } = "EA";
            public string Barcode { get; set; }
            public int PurchasedQty { get; set; }
            public int AlreadyReturnedQty { get; set; }
            public int MaxReturnable => Math.Max(0, PurchasedQty - AlreadyReturnedQty);
            public int ReturnQty { get; set; } = 0;
            public bool Added { get; set; } = false;
        }

        private class ReturnCartLine
        {
            public string SourceInvoiceNo { get; set; }
            public string Name { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountPct { get; set; }
            public decimal TaxPct { get; set; }
            public int ReturnQty { get; set; }
            public string Barcode { get; set; }
            public string UOM { get; set; } = "EA";

            public decimal LineSubtotal => Math.Round(UnitPrice * ReturnQty * (1m - DiscountPct / 100m), 2);
            public decimal LineTax => Math.Round(LineSubtotal * TaxPct / 100m, 2);
            public decimal LineRefund => LineSubtotal + LineTax;
        }

        private class ReturnReceiptData
        {
            public string ReturnInvoiceNo { get; set; }
            public string OriginalInvoiceNos { get; set; }
            public string CustomerName { get; set; }
            public DateTime ReturnDate { get; set; }
            public string CashierName { get; set; }
            public string RefundMethod { get; set; }
            public string ReturnReason { get; set; }
            public string RmaNumber { get; set; }
            public string DispositionCode { get; set; }
            public decimal RefundSubtotal { get; set; }
            public decimal RefundTax { get; set; }
            public decimal RefundTotal { get; set; }
            public string CurrencySymbol { get; set; }
            public string CompanyName { get; set; } = "";
            public string CompanyAddress { get; set; } = "";
            public string CompanyPhone { get; set; } = "";
            public string CompanyVat { get; set; } = "";
            public string CompanyWebsite { get; set; } = "";
            public string SalesOfficeInfo { get; set; } = "";
            public List<ReturnReceiptLine> Lines { get; set; } = new List<ReturnReceiptLine>();
        }

        private class ReturnReceiptLine
        {
            public string OriginalInvoiceNo { get; set; }
            public string ItemName { get; set; }
            public int ReturnQty { get; set; }
            public string UOM { get; set; } = "EA";
            public decimal UnitPrice { get; set; }
            public decimal DiscountPct { get; set; }
            public decimal TaxPct { get; set; }
            public decimal Subtotal { get; set; }
            public decimal TaxAmt { get; set; }
            public decimal RefundAmt { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════
        public SalesReturnForm(
            int companyId,
            string currencySymbol = "P",
            string companyName = "",
            string companyAddress = "",
            string companyPhone = "",
            string companyVat = "",
            string companyWebsite = "",
            string salesOfficeInfo = "")
        {
            _companyId = companyId;
            _currencySymbol = string.IsNullOrWhiteSpace(currencySymbol) ? "P" : currencySymbol;
            _companyName = companyName;
            _companyAddress = companyAddress;
            _companyPhone = companyPhone;
            _companyVat = companyVat;
            _companyWebsite = companyWebsite;
            _salesOfficeInfo = salesOfficeInfo;
            InitUI();
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI BUILD
        // ══════════════════════════════════════════════════════════════════
        private void InitUI()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgPage;
            AutoScaleMode = AutoScaleMode.Dpi;                 // scale consistently across DPI / resolutions
            ClientSize = new Size(1180, 780);
            KeyPreview = true;
            Text = "Sales Return";
            MinimumSize = new Size(940, 620);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.OptimizedDoubleBuffer, true);

            BuildTitleBar();
            BuildLeftList();
            BuildRightDetail();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += (s, e) => { txtOrderSearch?.Focus(); };

            _ = LoadAllOrdersAsync();
        }

        // ── Title bar ────────────────────────────────────────────────────────
        private void BuildTitleBar()
        {
            panelHeader = new Panel { BackColor = PanelWhite, Dock = DockStyle.Top, Height = HEADER_H };
            panelHeader.Paint += (s, e) =>
            {
                e.Graphics.Clear(PanelWhite);
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, HEADER_H - 1, panelHeader.Width, HEADER_H - 1);
            };

            lblTitle = new Label
            {
                Text = "Sales Return",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(20, (HEADER_H - 20) / 2 - 1)
            };

            btnMaximize = MakeTitleBtn("□", new Point(ClientSize.Width - 92, 0));
            var btnClose = MakeTitleBtn("✕", new Point(ClientSize.Width - 46, 0));
            var btnMin = MakeTitleBtn("−", new Point(ClientSize.Width - 138, 0));
            btnClose.ForeColor = AccRed;
            btnClose.MouseEnter += (s, e) => btnClose.BackColor = AccRedLight;
            btnClose.MouseLeave += (s, e) => btnClose.BackColor = PanelWhite;
            btnClose.Click += (s, e) => Close();
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            btnMaximize.Click += (s, e) => ToggleMaximize();

            panelHeader.SizeChanged += (s, e) =>
            {
                btnClose.Location = new Point(panelHeader.Width - 46, 0);
                btnMaximize.Location = new Point(panelHeader.Width - 92, 0);
                btnMin.Location = new Point(panelHeader.Width - 138, 0);
            };

            panelHeader.Controls.AddRange(new Control[] { lblTitle, btnClose, btnMaximize, btnMin });
            panelHeader.MouseDown += Header_MouseDown;
            panelHeader.MouseMove += Header_MouseMove;
            panelHeader.MouseUp += (s, e) => _drag = false;
            Controls.Add(panelHeader);
        }

        private void ToggleMaximize()
        {
            if (_isMaximized)
            { Bounds = _normalBounds; _isMaximized = false; btnMaximize.Text = "□"; }
            else
            {
                _normalBounds = Bounds; _isMaximized = true; btnMaximize.Text = "❐";
                Bounds = Screen.FromControl(this).WorkingArea;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  LEFT — ORDER LIST  (now resizable % width + customer filter)
        // ══════════════════════════════════════════════════════════════════
        private void BuildLeftList()
        {
            pnlLeftList = new Panel
            {
                BackColor = PanelWhite,
                Dock = DockStyle.Left,
                Width = LIST_W_DEFAULT,
                MinimumSize = new Size(LIST_W_MIN, 0)
            };
            pnlLeftList.Paint += (s, e) =>
            {
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, pnlLeftList.Width - 1, 0, pnlLeftList.Width - 1, pnlLeftList.Height);
            };
            Controls.Add(pnlLeftList);

            // Header block hosts: caption + refresh (row 1), text search (row 2),
            // and a customer filter dropdown (row 3). Height is generous and every
            // row is on its own non-overlapping band so nothing can collide, and a
            // bottom divider line makes the boundary with the order list explicit —
            // the order list can never render "under" the filter controls.
            const int LIST_HEADER_H = 136;
            var pnlListHeader = new Panel { Dock = DockStyle.Top, Height = LIST_HEADER_H, BackColor = PanelWhite };
            pnlListHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, LIST_HEADER_H - 1, pnlListHeader.Width, LIST_HEADER_H - 1);
            };

            var lblOrdersCap = new Label
            {
                Text = "All Sales Orders",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(16, 12)
            };



            txtOrderSearch = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(16, 46),
                Size = new Size(pnlLeftList.Width - 32, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            AddPlaceholder(txtOrderSearch, "Search customer or order #");
            txtOrderSearch.TextChanged += (s, e) => ApplyOrderFilters();

            // Customer filter dropdown — populated once orders are loaded.
            cmbCustomerFilter = new ComboBox
            {
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextDark,
                BackColor = InputBg,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(16, 84),
                Size = new Size(pnlLeftList.Width - 32, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            cmbCustomerFilter.Items.Add("All Customers");
            cmbCustomerFilter.SelectedIndex = 0;
            cmbCustomerFilter.SelectedIndexChanged += (s, e) => ApplyOrderFilters();

            // Position the refresh button once we know the caption's width so the
            // two never sit on top of each other even with long localized captions.


            pnlListHeader.Controls.Add(lblOrdersCap);

            pnlListHeader.Controls.Add(txtOrderSearch);
            pnlListHeader.Controls.Add(cmbCustomerFilter);

            // Auto-refresh whenever the window regains focus, so stale data doesn't linger.
            this.Activated += async (s, e) => await LoadAllOrdersAsync();

            pnlOrderRowsScroll = new Panel { Dock = DockStyle.Fill, BackColor = PanelWhite, AutoScroll = true };

            // IMPORTANT: add the Top-docked header BEFORE the Fill-docked scroll
            // area. Docking is resolved in the order controls are added, so the
            // header must reserve its band first — otherwise the list can be laid
            // out starting at y = 0 and render underneath/behind the filter row.
            pnlLeftList.Controls.Add(pnlListHeader);
            pnlLeftList.Controls.Add(pnlOrderRowsScroll);

            pnlOrderRows = new Panel { BackColor = PanelWhite, AutoSize = true, Location = new Point(0, 0), Width = pnlLeftList.Width - 20 };
            pnlOrderRowsScroll.Controls.Add(pnlOrderRows);
            pnlOrderRowsScroll.SizeChanged += (s, e) =>
            {
                pnlOrderRows.Width = Math.Max(1, pnlOrderRowsScroll.ClientSize.Width);
                RebuildOrderRows(FilterOrders());
            };

            lblOrdersEmpty = new Label
            {
                Text = "Loading orders…",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextLight,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(16, 8)
            };
            pnlOrderRows.Controls.Add(lblOrdersEmpty);
        }

        private static void AddPlaceholder(TextBox tb, string placeholder)
        {
            tb.Tag = placeholder;
            tb.Text = placeholder;
            tb.ForeColor = TextLight;
            tb.Enter += (s, e) => { if (tb.Text == placeholder) { tb.Text = ""; tb.ForeColor = TextDark; } };
            tb.Leave += (s, e) => { if (string.IsNullOrWhiteSpace(tb.Text)) { tb.Text = placeholder; tb.ForeColor = TextLight; } };
        }

        private async Task LoadAllOrdersAsync()
        {
            lblOrdersEmpty.Text = "Loading orders…";
            var customers = await SalesReturnRepository.GetActiveCustomersAsync(_companyId);
            var rows = new List<OrderRow>();

            foreach (var cust in customers)
            {
                List<InvoiceWithLines> invoices;
                try { invoices = await SalesReturnRepository.GetInvoicesForCustomerAsync(cust.CustomerID); }
                catch { continue; }

                foreach (var inv in invoices)
                {
                    rows.Add(new OrderRow
                    {
                        Customer = cust,
                        Invoice = inv,
                        IsFullyReturned = SalesReturnRepository.IsFullyReturned(inv.Header.InvoiceNo)
                    });
                }
            }

            _allOrders = rows.OrderByDescending(r => r.Invoice.Header.InvoiceDate).ToList();

            // Populate the customer filter dropdown with distinct, sorted customer names.
            string currentSel = cmbCustomerFilter.SelectedItem as string;
            cmbCustomerFilter.SelectedIndexChanged -= CustomerFilterChanged;
            cmbCustomerFilter.Items.Clear();
            cmbCustomerFilter.Items.Add("All Customers");
            foreach (var name in _allOrders.Select(r => r.Customer.CustomerName)
                                            .Where(n => !string.IsNullOrWhiteSpace(n))
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .OrderBy(n => n))
                cmbCustomerFilter.Items.Add(name);

            int restoreIdx = !string.IsNullOrEmpty(currentSel) ? cmbCustomerFilter.Items.IndexOf(currentSel) : 0;
            cmbCustomerFilter.SelectedIndex = restoreIdx >= 0 ? restoreIdx : 0;
            cmbCustomerFilter.SelectedIndexChanged += CustomerFilterChanged;

            ApplyOrderFilters();
        }

        // Named handler so it can be safely detached/reattached while repopulating the combo.
        private void CustomerFilterChanged(object sender, EventArgs e) => ApplyOrderFilters();

        private List<OrderRow> FilterOrders()
        {
            IEnumerable<OrderRow> result = _allOrders;

            // Customer dropdown filter
            if (cmbCustomerFilter != null && cmbCustomerFilter.SelectedIndex > 0)
            {
                string custName = cmbCustomerFilter.SelectedItem as string;
                result = result.Where(r => string.Equals(r.Customer.CustomerName, custName, StringComparison.OrdinalIgnoreCase));
            }

            // Free-text search over customer name / order number
            string query = txtOrderSearch?.Text ?? "";
            string placeholder = (txtOrderSearch?.Tag as string) ?? "";
            if (!string.IsNullOrWhiteSpace(query) && query != placeholder)
            {
                query = query.Trim();
                result = result.Where(r =>
                    (r.Customer.CustomerName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (r.Invoice.Header.InvoiceNo ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return result.ToList();
        }

        private void ApplyOrderFilters() => RebuildOrderRows(FilterOrders());

        private void RebuildOrderRows(List<OrderRow> rows)
        {
            pnlOrderRows.SuspendLayout();
            foreach (Control c in pnlOrderRows.Controls) c.Dispose();
            pnlOrderRows.Controls.Clear();
            _selectedRowPanel = null;   // old panel instance was just disposed above

            if (rows == null || rows.Count == 0)
            {
                lblOrdersEmpty.Text = "No orders found.";
                lblOrdersEmpty.Location = new Point(16, 8);
                pnlOrderRows.Controls.Add(lblOrdersEmpty);
                pnlOrderRows.ResumeLayout();
                return;
            }

            int y = 0, rowH = 66;
            foreach (var row in rows)
            {
                var rowPanel = BuildOrderRow(row, y, rowH);
                pnlOrderRows.Controls.Add(rowPanel);

                // Re-attach _selectedRowPanel to the freshly built panel for the
                // still-selected invoice, so later ScrollControlIntoView calls
                // don't reference a disposed control.
                if (_selectedOrder != null &&
                    string.Equals(_selectedOrder.Invoice.Header.InvoiceNo, row.Invoice.Header.InvoiceNo, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedRowPanel = rowPanel;
                }

                y += rowH;
            }
            pnlOrderRows.Height = y;
            pnlOrderRows.ResumeLayout();

            if (_selectedRowPanel != null)
                pnlOrderRowsScroll.ScrollControlIntoView(_selectedRowPanel);
        }

        private Panel BuildOrderRow(OrderRow row, int y, int rowH)
        {
            bool selected = _selectedOrder != null &&
                string.Equals(_selectedOrder.Invoice.Header.InvoiceNo, row.Invoice.Header.InvoiceNo, StringComparison.OrdinalIgnoreCase);

            var panel = new Panel
            {
                BackColor = selected ? RowSelectedBg : PanelWhite,
                Size = new Size(Math.Max(1, pnlOrderRowsScroll.ClientSize.Width), rowH),
                Location = new Point(0, y),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Padding = new Padding(16, 8, 16, 8)
            };
            panel.Paint += (s, e) =>
            {
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, rowH - 1, panel.Width, rowH - 1);
                if (selected)
                {
                    using var accentPen = new Pen(AccentBlue, 3f);
                    e.Graphics.DrawLine(accentPen, 1, 0, 1, rowH);
                }
            };

            Color rowBg = panel.BackColor;
            var lblName = new Label
            {
                Text = row.Customer.CustomerName ?? "",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = rowBg,
                AutoSize = true,
                Location = new Point(16, 9)
            };
            var lblAmount = new Label
            {
                Text = Fmt(row.Invoice.Header.Total),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = AccGreen,
                BackColor = rowBg,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            lblAmount.Location = new Point(panel.Width - lblAmount.PreferredWidth - 16, 9);

            var lblMeta = new Label
            {
                Text = $"{row.Invoice.Header.InvoiceNo}  ·  {row.Invoice.Header.InvoiceDate:dd MMM yyyy}",
                Font = new Font("Segoe UI", 8.2F),
                ForeColor = TextMid,
                BackColor = rowBg,
                AutoSize = true,
                Location = new Point(16, 32)
            };

            string statusText = row.IsFullyReturned ? "RETURNED" : "CONFIRMED";
            Color statusColor = row.IsFullyReturned ? TextMid : AccAmber;
            var lblStatus = new Label
            {
                Text = statusText,
                Font = new Font("Segoe UI", 7.7F, FontStyle.Bold),
                ForeColor = statusColor,
                BackColor = rowBg,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            lblStatus.Location = new Point(panel.Width - lblStatus.PreferredWidth - 16, 34);

            panel.SizeChanged += (s, e) =>
            {
                lblAmount.Location = new Point(panel.Width - lblAmount.PreferredWidth - 16, 9);
                lblStatus.Location = new Point(panel.Width - lblStatus.PreferredWidth - 16, 34);
            };

            panel.Controls.AddRange(new Control[] { lblName, lblAmount, lblMeta, lblStatus });

            EventHandler clickHandler = (s, e) => SelectOrder(row, panel);
            panel.Click += clickHandler;
            panel.MouseEnter += (s, e) => { if (!selected) { panel.BackColor = RowHoverBg; foreach (Control c in panel.Controls) c.BackColor = RowHoverBg; } };
            panel.MouseLeave += (s, e) => { if (!selected) { panel.BackColor = PanelWhite; foreach (Control c in panel.Controls) c.BackColor = PanelWhite; } };
            foreach (Control child in panel.Controls)
            {
                child.Click += clickHandler;
                child.Cursor = Cursors.Hand;
            }

            return panel;
        }

        // ══════════════════════════════════════════════════════════════════
        //  RIGHT — DETAIL PANEL
        // ══════════════════════════════════════════════════════════════════
        private void BuildRightDetail()
        {
            pnlRightDetail = new Panel { Dock = DockStyle.Fill, BackColor = BgPage };
            Controls.Add(pnlRightDetail);
            pnlRightDetail.BringToFront();

            BuildDetailToolbar();
            BuildBuilderFooter();
            BuildDetailBodyHost();

            lblNoSelection = new Label
            {
                Text = "Select an order on the left to view its details.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                BackColor = BgPage,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlRightDetail.Controls.Add(lblNoSelection);
            lblNoSelection.BringToFront();
        }

        private void BuildDetailToolbar()
        {
            pnlDetailToolbar = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = PanelWhite, Visible = false };
            pnlDetailToolbar.Paint += (s, e) =>
            {
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, pnlDetailToolbar.Height - 1, pnlDetailToolbar.Width, pnlDetailToolbar.Height - 1);
            };

            lblOrderNumberHeader = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(24, 18)
            };

            btnEdit = MakeToolbarBtn("Edit", 0);
            btnEmail = MakeToolbarBtn("Email", 0);
            btnPdfPrint = MakeToolbarBtn("PDF/Print", 0);
            btnCreateMenu = MakeToolbarBtn("Create ▾", 0);
            btnCreateMenu.BackColor = AccGreen;
            btnCreateMenu.ForeColor = Color.White;

            btnEdit.Click += (s, e) => ShowBuilderStatusToast("Edit isn't available from the return screen.", false);
            btnEmail.Click += (s, e) => ShowBuilderStatusToast("Email isn't available from the return screen.", false);
            btnPdfPrint.Click += (s, e) => ShowBuilderStatusToast("Open a processed return to print its receipt.", false);

            mnuCreate = new ContextMenuStrip();
            var miReturn = new ToolStripMenuItem("Sales Return");
            miReturn.Click += (s, e) => ShowReturnBuilder();
            mnuCreate.Items.Add(miReturn);
            btnCreateMenu.Click += (s, e) => mnuCreate.Show(btnCreateMenu, new Point(0, btnCreateMenu.Height));

            pnlDetailToolbar.Controls.AddRange(new Control[] { lblOrderNumberHeader, btnEdit, btnEmail, btnPdfPrint, btnCreateMenu });
            pnlDetailToolbar.SizeChanged += (s, e) => LayoutToolbarButtons();
            pnlRightDetail.Controls.Add(pnlDetailToolbar);
        }

        // Sticky footer, always docked to the bottom of the detail panel. Hidden
        // while browsing order details; shown only while the return builder is
        // active, so Process Return / Cancel are always on-screen and reachable
        // no matter how tall the scrollable content above them is, or how small
        // the window/screen is.
        private void BuildBuilderFooter()
        {
            pnlBuilderFooter = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = PanelWhite, Visible = false };
            pnlBuilderFooter.Paint += (s, e) =>
            {
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, 0, pnlBuilderFooter.Width, 0);
            };

            lblBuilderStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = PanelWhite,
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
                Location = new Point(24, 8),
                Size = new Size(400, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblRefundTotalFooter = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = PanelWhite,
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
                Location = new Point(24, 30),
                Size = new Size(400, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _detailFooterStatusLine1 = lblBuilderStatus;
            _detailFooterStatusLine2 = lblRefundTotalFooter;

            btnCancelReturn = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextMid,
                BackColor = Color.FromArgb(238, 237, 231),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(90, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            btnCancelReturn.FlatAppearance.BorderSize = 0;
            btnCancelReturn.Region = MakeRoundedRegion(btnCancelReturn.Size, 6);
            btnCancelReturn.Click += (s, e) => BackToOrderDetail();

            btnProcessReturn = new Button
            {
                Text = "Process Return",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 36),
                Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            btnProcessReturn.FlatAppearance.BorderSize = 0;
            btnProcessReturn.Region = MakeRoundedRegion(btnProcessReturn.Size, 6);
            btnProcessReturn.Click += BtnProcessReturn_Click;

            void LayoutFooterButtons()
            {
                int midY = (pnlBuilderFooter.Height - 36) / 2;
                btnProcessReturn.Location = new Point(pnlBuilderFooter.Width - 24 - btnProcessReturn.Width, midY);
                btnCancelReturn.Location = new Point(btnProcessReturn.Left - 10 - btnCancelReturn.Width, midY);
                int labelW = Math.Max(80, btnCancelReturn.Left - 24 - 24);
                lblBuilderStatus.Width = labelW;
                lblRefundTotalFooter.Width = labelW;
            }
            pnlBuilderFooter.SizeChanged += (s, e) => LayoutFooterButtons();
            LayoutFooterButtons();

            pnlBuilderFooter.Controls.AddRange(new Control[] { lblBuilderStatus, lblRefundTotalFooter, btnCancelReturn, btnProcessReturn });
            pnlRightDetail.Controls.Add(pnlBuilderFooter);
        }
        private Label _detailFooterStatusLine1, _detailFooterStatusLine2;

        private void LayoutToolbarButtons()
        {
            int right = pnlDetailToolbar.Width - 20;
            right -= btnCreateMenu.Width; btnCreateMenu.Location = new Point(right, 14); right -= 10;
            right -= btnPdfPrint.Width; btnPdfPrint.Location = new Point(right, 14); right -= 10;
            right -= btnEmail.Width; btnEmail.Location = new Point(right, 14); right -= 10;
            right -= btnEdit.Width; btnEdit.Location = new Point(right, 14);
        }

        private Button MakeToolbarBtn(string text, int x)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = TextMid,
                BackColor = Color.FromArgb(238, 237, 231),
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Padding = new Padding(10, 4, 10, 4),
                Height = 34,
                Location = new Point(x, 14),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void BuildDetailBodyHost()
        {
            pnlDetailBody = new Panel { Dock = DockStyle.Fill, BackColor = BgPage, AutoScroll = true, Visible = false };
            pnlRightDetail.Controls.Add(pnlDetailBody);

            BuildOrderDetailView();
            BuildReturnBuilderView();
        }

        // ── Order detail view ───────────────────────────────────────────────
        private Label lblStatusOrder, lblStatusInvoice, lblStatusPayment, lblStatusShipment;
        private Label lblMetaOrderDate, lblMetaRegister, lblMetaFulfillment, lblMetaTerms, lblMetaChannel;
        private Label lblBillingName, lblShippingName, lblShippingHeading;
        private Panel _leftDetailCard, _rightDetailCard;
        private Label _lblSoNo;

        private void BuildOrderDetailView()
        {
            pnlOrderDetailView = new Panel { BackColor = BgPage, AutoSize = true, Location = new Point(0, 0), Width = 1, Visible = false };
            pnlDetailBody.Controls.Add(pnlOrderDetailView);
            pnlDetailBody.SizeChanged += (s, e) =>
            {
                pnlOrderDetailView.Width = Math.Max(1, pnlDetailBody.ClientSize.Width);
                pnlReturnBuilderView.Width = Math.Max(1, pnlDetailBody.ClientSize.Width);
                RelayoutOrderDetailCards();
            };

            // Left card — Sales Order summary. Width is now RECOMPUTED from the
            // available body width (percentage based) instead of a fixed 620px,
            // so the layout no longer breaks on narrower / higher-res screens.
            var leftCard = MakeDetailCard(new Point(24, 24), 620);
            _leftDetailCard = leftCard;
            AddCardHeading(leftCard, "SALES ORDER", TextLight, 8F);

            // "Sales Order# ..." — placed with generous clearance below the
            // heading, and given its own reserved band so it can never collide
            // with the status-chip row beneath it (previous bug: fixed y=20/56
            // offsets plus transparent AutoSize labels caused ghosted overlap
            // when text length changed between selections).
            _lblSoNo = new Label
            {
                Text = "",
                Name = "soNo",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(0, 22)
            };
            leftCard.Controls.Add(_lblSoNo);

            // status chip row — pushed down to a fixed band that starts AFTER
            // the tallest possible "Sales Order#" label (12pt bold "text" at
            // 150% DPI is ~34px), with extra breathing room.
            int chipY = 68;
            lblStatusOrder = AddStatusChip(leftCard, "ORDER", "CONFIRMED", AccAmber, 0, chipY);
            lblStatusInvoice = AddStatusChip(leftCard, "INVOICE", "Invoiced", AccentBlue, 150, chipY);
            lblStatusPayment = AddStatusChip(leftCard, "PAYMENT", "Paid", AccGreen, 300, chipY);
            lblStatusShipment = AddStatusChip(leftCard, "SHIPMENT", "Fulfilled", AccGreen, 450, chipY);

            int dividerY = chipY + 46;
            var divider = new Panel { BackColor = BorderColor, Size = new Size(1, 1), Location = new Point(0, dividerY), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            divider.Paint += (s, e) => { divider.Width = leftCard.Width - 40; };
            leftCard.Controls.Add(divider);

            int mY = dividerY + 14;
            lblMetaOrderDate = AddMetaRow(leftCard, "Order Date", ref mY);
            lblMetaRegister = AddMetaRow(leftCard, "Register Name", ref mY);
            lblMetaFulfillment = AddMetaRow(leftCard, "Fulfillment Status", ref mY);
            lblMetaTerms = AddMetaRow(leftCard, "Payment Terms", ref mY);
            lblMetaChannel = AddMetaRow(leftCard, "Sales Channel", ref mY);
            leftCard.Tag = mY + 20; // desired height

            // Right card — Billing / Shipping address
            var rightCard = MakeDetailCard(new Point(24 + 620 + 20, 24), 300);
            _rightDetailCard = rightCard;
            AddCardHeading(rightCard, "BILLING ADDRESS", TextLight, 8F);
            lblBillingName = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(0, 22),
                MaximumSize = new Size(260, 0)
            };
            rightCard.Controls.Add(lblBillingName);

            lblShippingHeading = new Label
            {
                Text = "SHIPPING ADDRESS",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextLight,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(0, 70)
            };
            rightCard.Controls.Add(lblShippingHeading);
            lblShippingName = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(0, 92),
                MaximumSize = new Size(260, 0)
            };
            rightCard.Controls.Add(lblShippingName);

            pnlOrderDetailView.Controls.Add(leftCard);
            pnlOrderDetailView.Controls.Add(rightCard);

            RelayoutOrderDetailCards();
        }

        // Recomputes card widths/heights/positions from the AVAILABLE WIDTH of
        // the body panel (percentage split) and from actual control sizes (not
        // fixed offsets), so:
        //   1) wrapped address text can never overlap the next label,
        //   2) the two cards reflow correctly at any window / screen resolution,
        //   3) the whole card + its children are repainted after every text
        //      change (fixes the "ghost text" overlap seen when switching
        //      between orders with different-length order numbers),
        //   4) on narrow windows the right (address) card drops BELOW the left
        //      card instead of being squeezed into negative/near-zero width,
        //      which is what was causing the order-detail area to look cropped.
        private void RelayoutOrderDetailCards()
        {
            if (_leftDetailCard == null || _rightDetailCard == null) return;

            int bodyW = Math.Max(1, pnlOrderDetailView.Width);
            int margin = 24;
            int gap = 20;

            const int STACK_BREAKPOINT = 760;
            bool stacked = bodyW < STACK_BREAKPOINT;

            int leftW, rightW;
            if (stacked)
            {
                // Narrow window: both cards take the full available width, stacked
                // vertically, so nothing gets clipped on the side.
                leftW = Math.Max(260, bodyW - margin * 2);
                rightW = leftW;
            }
            else
            {
                rightW = Math.Max(260, (int)(bodyW * 0.24));
                leftW = Math.Max(480, bodyW - margin * 2 - gap - rightW);
            }

            _leftDetailCard.Location = new Point(margin, margin);
            _leftDetailCard.Width = leftW;
            _leftDetailCard.Height = (int)_leftDetailCard.Tag;

            // Billing block, then shipping heading directly below it, then shipping name.
            const int addrGap = 18;
            lblShippingHeading.Location = new Point(0, lblBillingName.Bottom + addrGap);
            lblShippingName.Location = new Point(0, lblShippingHeading.Bottom + 6);

            if (stacked)
                _rightDetailCard.Location = new Point(margin, _leftDetailCard.Bottom + gap);
            else
                _rightDetailCard.Location = new Point(_leftDetailCard.Right + gap, margin);
            _rightDetailCard.Width = rightW;
            _rightDetailCard.Height = lblShippingName.Bottom + 20;

            pnlOrderDetailView.Height = Math.Max(_leftDetailCard.Bottom, _rightDetailCard.Bottom) + 24;

            // Force a full repaint of both cards so no stale glyph pixels from a
            // previous selection's longer/shorter text can linger underneath.
            _leftDetailCard.Invalidate(true);
            _rightDetailCard.Invalidate(true);
        }

        private Panel MakeDetailCard(Point loc, int width)
        {
            var p = new Panel { BackColor = PanelWhite, Location = loc, Size = new Size(width, 100), Padding = new Padding(CARD_PAD) };
            p.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(PanelWhite);
                using var pen = new Pen(BorderColor, 1f);
                using var path = RoundedPath(new Rectangle(0, 0, p.Width - 1, p.Height - 1), CARD_RADIUS);
                e.Graphics.DrawPath(pen, path);
            };
            // Children are positioned relative to (0,0) in code, so offset the
            // actual content by the card's padding via an inner host panel would
            // require re-plumbing every call site; instead we simply inset the
            // card's drawn border/background from its own edges and let content
            // start at CARD_PAD via each control's own Location math below.
            return p;
        }

        private void AddCardHeading(Panel card, string text, Color color, float size)
        {
            card.Controls.Add(new Label
            {
                Text = text,
                Font = new Font("Segoe UI", size, FontStyle.Bold),
                ForeColor = color,
                BackColor = card.BackColor,
                AutoSize = true,
                Location = new Point(0, 0)
            });
        }

        private Label AddStatusChip(Panel card, string caption, string value, Color valueColor, int x, int y)
        {
            card.Controls.Add(new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 7.2F, FontStyle.Bold),
                ForeColor = TextLight,
                BackColor = card.BackColor,
                AutoSize = true,
                Location = new Point(x, y)
            });
            var val = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = valueColor,
                BackColor = card.BackColor,
                AutoSize = true,
                Location = new Point(x, y + 16)
            };
            card.Controls.Add(val);
            return val;
        }

        private Label AddMetaRow(Panel card, string caption, ref int y)
        {
            card.Controls.Add(new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = card.BackColor,
                AutoSize = true,
                Location = new Point(0, y),
                Size = new Size(160, 18)
            });
            var val = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = card.BackColor,
                AutoSize = true,
                Location = new Point(170, y)
            };
            card.Controls.Add(val);
            y += 26;
            return val;
        }

        // ══════════════════════════════════════════════════════════════════
        //  SELECT ORDER  →  populate order-detail view
        // ══════════════════════════════════════════════════════════════════
        private void SelectOrder(OrderRow row, Panel rowPanel)
        {
            _selectedOrder = row;
            _selectedCustomerId = row.Customer.CustomerID;
            _selectedCustomerName = row.Customer.CustomerName;

            if (_selectedRowPanel != null)
            {
                _selectedRowPanel.BackColor = PanelWhite;
                foreach (Control c in _selectedRowPanel.Controls) c.BackColor = PanelWhite;
                _selectedRowPanel.Invalidate(true);
            }
            rowPanel.BackColor = RowSelectedBg;
            foreach (Control c in rowPanel.Controls) c.BackColor = RowSelectedBg;
            rowPanel.Invalidate(true);
            _selectedRowPanel = rowPanel;

            var inv = row.Invoice.Header;
            lblOrderNumberHeader.Text = inv.InvoiceNo;

            _lblSoNo.Text = $"Sales Order# {inv.InvoiceNo}";

            lblStatusOrder.Text = row.IsFullyReturned ? "RETURNED" : "CONFIRMED";
            lblStatusOrder.ForeColor = row.IsFullyReturned ? AccRed : AccAmber;

            lblMetaOrderDate.Text = inv.InvoiceDate.ToString("dd MMM yyyy");
            lblMetaRegister.Text = "Mobile POS";                 // TODO: wire real register name once tracked
            lblMetaFulfillment.Text = "Fulfilled";                // TODO: wire real fulfillment status
            lblMetaTerms.Text = "Due On Receipt";                 // TODO: wire real payment terms
            lblMetaChannel.Text = "Point of Sale";

            lblBillingName.Text = string.IsNullOrWhiteSpace(row.Customer.CustomerName)
                ? "Walk-in Customer" : row.Customer.CustomerName + "\n(address not on file)";
            lblShippingName.Text = lblBillingName.Text;

            // 1) Flip visibility and reset scroll BEFORE measuring/positioning anything —
            //    doing this after RelayoutOrderDetailCards() is what let stale bounds
            //    leak into the paint and caused the right card to overlap the toolbar.
            lblNoSelection.Visible = false;
            pnlDetailToolbar.Visible = true;
            pnlBuilderFooter.Visible = false;      // no footer while just viewing order details
            pnlDetailBody.Visible = true;
            pnlOrderDetailView.Visible = true;
            pnlReturnBuilderView.Visible = false;
            pnlDetailBody.AutoScrollPosition = Point.Empty;   // don't inherit scroll offset from a previous view

            // 2) Force the Dock/layout engine to settle NOW, so ClientSize/offsets
            //    used below are correct instead of reflecting the pre-selection state.
            pnlRightDetail.PerformLayout();
            pnlDetailBody.PerformLayout();

            // 3) Only now compute card positions/sizes from the settled layout.
            RelayoutOrderDetailCards();

            // 4) Repaint clean — clears any stale text/border pixels from the previous selection.
            pnlOrderDetailView.Invalidate(true);
            pnlDetailBody.Invalidate(true);

            ShowBuilderStatusToast("", true);

            // Make sure the row we just selected is actually visible in the left list,
            // not clipped under pnlListHeader.
            pnlOrderRowsScroll.ScrollControlIntoView(rowPanel);
        }

        // ══════════════════════════════════════════════════════════════════
        //  RETURN BUILDER VIEW
        // ══════════════════════════════════════════════════════════════════
        private Panel _linesCard, _cartCard, _detailsCard;

        private void BuildReturnBuilderView()
        {
            pnlReturnBuilderView = new Panel { BackColor = BgPage, AutoSize = true, Location = new Point(0, 0), Width = 1, Visible = false };
            pnlDetailBody.Controls.Add(pnlReturnBuilderView);

            var backBar = new Panel { BackColor = BgPage, Size = new Size(1, 36), Location = new Point(24, 16) };
            var btnBack = new LinkLabel
            {
                Text = "← Back to order",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                LinkColor = AccGreen,
                ActiveLinkColor = AccGreen,
                VisitedLinkColor = AccGreen,
                BackColor = BgPage,
                AutoSize = true,
                Location = new Point(0, 0)
            };
            btnBack.Click += (s, e) => BackToOrderDetail();
            backBar.Controls.Add(btnBack);
            pnlReturnBuilderView.Controls.Add(backBar);

            lblReturnBuilderInvoice = new Label
            {
                Text = "Return Items",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = BgPage,
                AutoSize = true,
                Location = new Point(24, 40)
            };
            pnlReturnBuilderView.Controls.Add(lblReturnBuilderInvoice);

            // Card: invoice lines
            var linesCard = MakeDetailCard(new Point(24, 76), 900);
            _linesCard = linesCard;
            AddCardHeading(linesCard, "INVOICE ITEMS", TextLight, 8F);
            var hdr = new Panel { BackColor = PanelAlt, Size = new Size(1, 26), Location = new Point(0, 22), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            hdr.Paint += (s, e) => { hdr.Width = linesCard.Width - 40; e.Graphics.Clear(PanelAlt); };
            string[] hdrs = { "Item Name", "Purchased", "UOM", "Price", "Return Qty", "" };
            int[] hdrX = { 0, 220, 290, 330, 410, 550 };
            foreach (var (t, x) in hdrs.Zip(hdrX, (a, b) => (a, b)))
                hdr.Controls.Add(new Label { Text = t, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold), ForeColor = TextMid, BackColor = PanelAlt, AutoSize = true, Location = new Point(x, 6) });
            linesCard.Controls.Add(hdr);

            panelInvoiceLineRows = new Panel { BackColor = PanelWhite, AutoSize = true, Location = new Point(0, 54), Width = 1 };
            linesCard.Controls.Add(panelInvoiceLineRows);
            pnlReturnBuilderView.Controls.Add(linesCard);

            // Card: cart
            var cartCard = MakeDetailCard(new Point(24, 0), 900);
            _cartCard = cartCard;
            AddCardHeading(cartCard, "ITEMS IN THIS RETURN", TextLight, 8F);
            var cartHdr = new Panel { BackColor = PanelAlt, Size = new Size(1, 26), Location = new Point(0, 22), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            cartHdr.Paint += (s, e) => { cartHdr.Width = cartCard.Width - 40; e.Graphics.Clear(PanelAlt); };
            string[] cHdrs = { "Invoice", "Item Name", "Qty", "UOM", "Refund", "" };
            int[] cHdrX = { 0, 120, 340, 380, 430, 550 };
            foreach (var (t, x) in cHdrs.Zip(cHdrX, (a, b) => (a, b)))
                cartHdr.Controls.Add(new Label { Text = t, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold), ForeColor = TextMid, BackColor = PanelAlt, AutoSize = true, Location = new Point(x, 6) });
            cartCard.Controls.Add(cartHdr);

            lblCartEmpty = new Label
            {
                Text = "No items added yet. Set a Return Qty above and click Add.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                BackColor = PanelWhite,
                AutoSize = true,
                Location = new Point(0, 60)
            };
            cartCard.Controls.Add(lblCartEmpty);

            panelCartRows = new Panel { BackColor = PanelWhite, AutoSize = true, Location = new Point(0, 54), Width = 1 };
            cartCard.Controls.Add(panelCartRows);
            pnlReturnBuilderView.Controls.Add(cartCard);

            // Card: return details
            var detailsCard = MakeDetailCard(new Point(24, 0), 440);
            _detailsCard = detailsCard;
            AddCardHeading(detailsCard, "RETURN DETAILS", TextLight, 8F);

            var lblReason = MkFieldLabel("Return Reason", 26);
            cmbReturnReason = MkCombo(ReturnReasonOptions, 44);
            cmbReturnReason.SelectedIndexChanged += (s, e) => _returnReason = cmbReturnReason.SelectedItem?.ToString() ?? "";

            var lblRma = MkFieldLabel("RMA Number", 84);
            txtRma = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(220, 28),
                Location = new Point(0, 102)
            };
            txtRma.TextChanged += (s, e) => _rmaNumber = txtRma.Text.Trim();

            var lblDisp = MkFieldLabel("Disposition", 140);
            cmbDisposition = MkCombo(DispositionOptions, 158);
            cmbDisposition.SelectedIndexChanged += (s, e) => _dispositionCode = cmbDisposition.SelectedItem?.ToString() ?? "";

            // Refund Method — only Cash is offered.
            var lblMethod = MkFieldLabel("Refund Method", 198);
            btnMethodCash = new Button
            {
                Text = "Cash",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(90, 32),
                Location = new Point(0, 216),
                Cursor = Cursors.Hand,
                Enabled = false // only option — shown as the fixed selected method
            };
            btnMethodCash.FlatAppearance.BorderSize = 0;
            btnMethodCash.Region = MakeRoundedRegion(btnMethodCash.Size, 6);

            var div2 = new Panel { BackColor = BorderColor, Size = new Size(220, 1), Location = new Point(0, 262) };
            var lblSub = MkSumLbl("Subtotal", 274); _lblSummarySubtotal = MkSumVal("", 274);
            var lblTax = MkSumLbl("Tax Refund", 296); _lblSummaryTax = MkSumVal("", 296);
            var div3 = new Panel { BackColor = BorderColor, Size = new Size(220, 1), Location = new Point(0, 320) };
            var lblTot = new Label { Text = "Total Refund", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = TextDark, BackColor = PanelWhite, AutoSize = true, Location = new Point(0, 330) };
            _lblSummaryTotal = new Label { Text = "", Font = new Font("Segoe UI", 15F, FontStyle.Bold), ForeColor = AccGreen, BackColor = PanelWhite, AutoSize = true, Location = new Point(0, 352) };

            detailsCard.Controls.AddRange(new Control[]
            {
                lblReason, cmbReturnReason, lblRma, txtRma, lblDisp, cmbDisposition,
                lblMethod, btnMethodCash, div2, lblSub, _lblSummarySubtotal, lblTax, _lblSummaryTax,
                div3, lblTot, _lblSummaryTotal
            });
            detailsCard.Tag = 400;
            pnlReturnBuilderView.Controls.Add(detailsCard);

            void RestackBuilder(object s, EventArgs e)
            {
                int bodyW = Math.Max(760, pnlReturnBuilderView.Width);
                int cardW = Math.Max(600, bodyW - 48);

                int y = 76;
                linesCard.Location = new Point(24, y);
                linesCard.Width = cardW;
                y = linesCard.Bottom + 16;
                cartCard.Location = new Point(24, y);
                cartCard.Width = cardW;
                y = cartCard.Bottom + 16;
                detailsCard.Location = new Point(24, y);
                // Extra bottom padding so the last card never sits flush against
                // (or under) the sticky footer — the footer lives outside this
                // scrollable view, so this is just breathing room at the end of
                // the scrollable content.
                pnlReturnBuilderView.Height = detailsCard.Bottom + 32;
            }
            linesCard.Resize += RestackBuilder;
            cartCard.Resize += RestackBuilder;
            detailsCard.Resize += RestackBuilder;
            pnlReturnBuilderView.SizeChanged += RestackBuilder;
            RestackBuilder(null, EventArgs.Empty);
        }

        private Label MkFieldLabel(string text, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = TextMid,
            BackColor = PanelWhite,
            AutoSize = true,
            Location = new Point(0, y)
        };

        private ComboBox MkCombo(string[] items, int y)
        {
            var cmb = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark,
                BackColor = InputBg,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 28),
                Location = new Point(0, y)
            };
            cmb.Items.AddRange(items);
            return cmb;
        }

        private Label MkSumLbl(string text, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextMid,
            BackColor = PanelWhite,
            AutoSize = true,
            Location = new Point(0, y)
        };
        private Label MkSumVal(string text, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextDark,
            BackColor = PanelWhite,
            AutoSize = true,
            Location = new Point(160, y)
        };

        // ══════════════════════════════════════════════════════════════════
        //  ENTER / EXIT RETURN BUILDER
        // ══════════════════════════════════════════════════════════════════
        private async void ShowReturnBuilder()
        {
            if (_selectedOrder == null) return;

            _currentInvoiceNo = _selectedOrder.Invoice.Header.InvoiceNo;
            lblReturnBuilderInvoice.Text = $"Return Items — {_currentInvoiceNo}";

            var returnedQtys = await SalesReturnRepository.GetReturnedQtysAsync(_currentInvoiceNo);

            _currentInvoiceLines = new List<InvoiceLineCandidate>();
            foreach (var r in _selectedOrder.Invoice.Lines)
            {
                int already = 0;
                string key = !string.IsNullOrWhiteSpace(r.Barcode) ? r.Barcode.Trim() : r.ItemName?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && returnedQtys.TryGetValue(key, out int rq))
                    already = rq;

                var candidate = new InvoiceLineCandidate
                {
                    ItemName = r.ItemName,
                    UnitPrice = r.UnitPrice,
                    DiscountPct = r.DiscountPct,
                    TaxPct = r.TaxPct,
                    UOM = string.IsNullOrWhiteSpace(r.UOM) ? "EA" : r.UOM,
                    Barcode = r.Barcode,
                    PurchasedQty = r.Qty,
                    AlreadyReturnedQty = already,
                    ReturnQty = 0
                };
                candidate.Added = _cartLines.Any(cl =>
                    string.Equals(cl.SourceInvoiceNo, _currentInvoiceNo, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(cl.Name, candidate.ItemName, StringComparison.OrdinalIgnoreCase));

                if (candidate.MaxReturnable > 0 || candidate.Added)
                    _currentInvoiceLines.Add(candidate);
            }

            RebuildInvoiceLineRows();
            RebuildCartRows();
            RecalcTotal();

            pnlOrderDetailView.Visible = false;
            pnlReturnBuilderView.Visible = true;
            pnlDetailBody.AutoScrollPosition = Point.Empty;
            pnlBuilderFooter.Visible = true;   // sticky footer appears only in builder mode
            btnCreateMenu.Enabled = false;
            pnlReturnBuilderView.Invalidate(true);

            ShowBuilderStatusToast(_currentInvoiceLines.Count > 0
                ? $"Set a Return Qty and click Add for each item you want to return from {_currentInvoiceNo}."
                : $"Invoice {_currentInvoiceNo} has no returnable items left.", _currentInvoiceLines.Count > 0);
        }

        private void BackToOrderDetail()
        {
            pnlReturnBuilderView.Visible = false;
            pnlOrderDetailView.Visible = true;
            pnlDetailBody.AutoScrollPosition = Point.Empty;
            pnlBuilderFooter.Visible = false;
            btnCreateMenu.Enabled = true;
            pnlOrderDetailView.Invalidate(true);
        }

        // ══════════════════════════════════════════════════════════════════
        //  INVOICE LINE ROWS
        // ══════════════════════════════════════════════════════════════════
        private void RebuildInvoiceLineRows()
        {
            foreach (Control c in panelInvoiceLineRows.Controls) c.Dispose();
            panelInvoiceLineRows.Controls.Clear();

            int y = 0;
            for (int i = 0; i < _currentInvoiceLines.Count; i++)
            {
                var row = BuildInvoiceLineRow(_currentInvoiceLines[i], y, i % 2 == 0);
                panelInvoiceLineRows.Controls.Add(row);
                y += 46;
            }
            panelInvoiceLineRows.Height = Math.Max(1, y);
        }

        private Panel BuildInvoiceLineRow(InvoiceLineCandidate line, int yOffset, bool alt)
        {
            const int ROW_H = 42;
            int rowW = Math.Max(1, panelInvoiceLineRows.Width);
            Color rowBg = alt ? PanelWhite : PanelAlt;

            var row = new Panel
            {
                BackColor = rowBg,
                Size = new Size(rowW, ROW_H),
                Location = new Point(0, yOffset),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            row.Paint += (s, e) =>
            {
                e.Graphics.Clear(rowBg);
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, ROW_H - 1, ((Panel)s).Width, ROW_H - 1);
            };

            row.Controls.Add(new Label { Text = line.ItemName, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextDark, BackColor = rowBg, Size = new Size(210, ROW_H), Location = new Point(0, 0), TextAlign = ContentAlignment.MiddleLeft });
            row.Controls.Add(new Label { Text = $"{line.MaxReturnable} / {line.PurchasedQty}", Font = new Font("Segoe UI", 8.5F), ForeColor = TextMid, BackColor = rowBg, Size = new Size(64, ROW_H), Location = new Point(220, 0), TextAlign = ContentAlignment.MiddleCenter });
            row.Controls.Add(new Label { Text = line.UOM, Font = new Font("Segoe UI", 8.5F), ForeColor = AccGreen, BackColor = rowBg, Size = new Size(36, ROW_H), Location = new Point(290, 0), TextAlign = ContentAlignment.MiddleCenter });
            row.Controls.Add(new Label { Text = Fmt(line.UnitPrice), Font = new Font("Segoe UI", 8.5F), ForeColor = TextMid, BackColor = rowBg, Size = new Size(76, ROW_H), Location = new Point(330, 0), TextAlign = ContentAlignment.MiddleLeft });

            var tbQty = new TextBox
            {
                Text = line.ReturnQty.ToString(),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                Size = new Size(60, 26),
                Location = new Point(410, (ROW_H - 26) / 2),
                MaxLength = 4,
                Enabled = !line.Added
            };
            tbQty.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            tbQty.Leave += (s, e) =>
            {
                if (!int.TryParse(tbQty.Text.Trim(), out int qty)) qty = 0;
                qty = Math.Max(0, Math.Min(qty, line.MaxReturnable));
                line.ReturnQty = qty;
                tbQty.Text = qty.ToString();
            };

            var btnAction = new Button
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(110, 28),
                Location = new Point(480, (ROW_H - 28) / 2),
                Cursor = Cursors.Hand
            };
            btnAction.FlatAppearance.BorderSize = 0;
            btnAction.Region = MakeRoundedRegion(btnAction.Size, 6);

            void RefreshActionButton()
            {
                if (line.Added)
                {
                    btnAction.Text = "✓ Added";
                    btnAction.BackColor = AccGreenLight;
                    btnAction.ForeColor = AccGreen;
                }
                else
                {
                    btnAction.Text = "Add to Return";
                    btnAction.BackColor = AccGreen;
                    btnAction.ForeColor = Color.White;
                }
            }
            RefreshActionButton();

            btnAction.Click += (s, e) =>
            {
                if (!int.TryParse(tbQty.Text.Trim(), out int qty)) qty = 0;
                qty = Math.Max(0, Math.Min(qty, line.MaxReturnable));

                if (!line.Added)
                {
                    if (qty <= 0)
                    { ShowBuilderStatusToast("Enter a Return Qty greater than zero before adding.", false); return; }

                    line.ReturnQty = qty;
                    _cartLines.Add(new ReturnCartLine
                    {
                        SourceInvoiceNo = _currentInvoiceNo,
                        Name = line.ItemName,
                        UnitPrice = line.UnitPrice,
                        DiscountPct = line.DiscountPct,
                        TaxPct = line.TaxPct,
                        UOM = line.UOM,
                        Barcode = line.Barcode,
                        ReturnQty = qty
                    });
                    line.Added = true;
                    tbQty.Enabled = false;
                    ShowBuilderStatusToast($"{line.ItemName} added to the return.", true);
                }
                else
                {
                    _cartLines.RemoveAll(cl =>
                        string.Equals(cl.SourceInvoiceNo, _currentInvoiceNo, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(cl.Name, line.ItemName, StringComparison.OrdinalIgnoreCase));
                    line.Added = false;
                    tbQty.Enabled = true;
                    ShowBuilderStatusToast($"{line.ItemName} removed from the return.", true);
                }

                RefreshActionButton();
                RebuildCartRows();
                RecalcTotal();
            };

            row.Controls.AddRange(new Control[] { tbQty, btnAction });
            return row;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CART ROWS
        // ══════════════════════════════════════════════════════════════════
        private void RebuildCartRows()
        {
            foreach (Control c in panelCartRows.Controls) c.Dispose();
            panelCartRows.Controls.Clear();
            lblCartEmpty.Visible = _cartLines.Count == 0;

            int y = 0;
            for (int i = 0; i < _cartLines.Count; i++)
            {
                var row = BuildCartRow(_cartLines[i], y, i % 2 == 0);
                panelCartRows.Controls.Add(row);
                y += 42;
            }
            panelCartRows.Height = Math.Max(1, y);
        }

        private Panel BuildCartRow(ReturnCartLine line, int yOffset, bool alt)
        {
            const int ROW_H = 38;
            int rowW = Math.Max(1, panelCartRows.Width);
            Color rowBg = alt ? PanelWhite : PanelAlt;

            var row = new Panel
            {
                BackColor = rowBg,
                Size = new Size(rowW, ROW_H),
                Location = new Point(0, yOffset),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            row.Paint += (s, e) =>
            {
                e.Graphics.Clear(rowBg);
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, ROW_H - 1, ((Panel)s).Width, ROW_H - 1);
            };

            row.Controls.Add(new Label { Text = line.SourceInvoiceNo, Font = new Font("Segoe UI", 8F), ForeColor = TextLight, BackColor = rowBg, Size = new Size(114, ROW_H), Location = new Point(0, 0), TextAlign = ContentAlignment.MiddleLeft });
            row.Controls.Add(new Label { Text = line.Name, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextDark, BackColor = rowBg, Size = new Size(210, ROW_H), Location = new Point(120, 0), TextAlign = ContentAlignment.MiddleLeft });
            row.Controls.Add(new Label { Text = line.ReturnQty.ToString(), Font = new Font("Segoe UI", 8.5F), ForeColor = TextMid, BackColor = rowBg, Size = new Size(36, ROW_H), Location = new Point(340, 0), TextAlign = ContentAlignment.MiddleCenter });
            row.Controls.Add(new Label { Text = line.UOM, Font = new Font("Segoe UI", 8.5F), ForeColor = AccGreen, BackColor = rowBg, Size = new Size(40, ROW_H), Location = new Point(380, 0), TextAlign = ContentAlignment.MiddleCenter });
            row.Controls.Add(new Label { Text = Fmt(line.LineRefund), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = AccGreen, BackColor = rowBg, Size = new Size(100, ROW_H), Location = new Point(428, 0), TextAlign = ContentAlignment.MiddleLeft });

            var btnRemove = new Button
            {
                Text = "Remove",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = AccRed,
                BackColor = AccRedLight,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(80, 26),
                Location = new Point(rowW - 90, (ROW_H - 26) / 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            btnRemove.FlatAppearance.BorderSize = 0;
            btnRemove.Region = MakeRoundedRegion(btnRemove.Size, 6);
            btnRemove.Click += (s, e) =>
            {
                _cartLines.Remove(line);
                if (string.Equals(line.SourceInvoiceNo, _currentInvoiceNo, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = _currentInvoiceLines.FirstOrDefault(c => string.Equals(c.ItemName, line.Name, StringComparison.OrdinalIgnoreCase));
                    if (candidate != null) candidate.Added = false;
                    RebuildInvoiceLineRows();
                }
                RebuildCartRows();
                RecalcTotal();
                ShowBuilderStatusToast($"{line.Name} removed from the return.", true);
            };

            row.Controls.Add(btnRemove);
            return row;
        }

        // ══════════════════════════════════════════════════════════════════
        //  TOTALS
        // ══════════════════════════════════════════════════════════════════
        private new void SetRefundMethod(string method)
        {
            _refundMethod = method;
            btnMethodCash.BackColor = AccGreen;
            btnMethodCash.ForeColor = Color.White;
        }

        private void RecalcTotal()
        {
            decimal subtotal = _cartLines.Sum(l => l.LineSubtotal);
            decimal tax = _cartLines.Sum(l => l.LineTax);
            decimal total = subtotal + tax;

            _lblSummarySubtotal.Text = subtotal > 0 ? Fmt(subtotal) : "—";
            _lblSummaryTax.Text = tax > 0 ? Fmt(tax) : "—";
            _lblSummaryTotal.Text = total > 0 ? Fmt(total) : "—";
            lblRefundTotalFooter.Text = total > 0 ? $"Refund: {Fmt(total)}" : "";

            btnProcessReturn.Enabled = _cartLines.Count > 0;
        }

        // ══════════════════════════════════════════════════════════════════
        //  PROCESS RETURN
        // ══════════════════════════════════════════════════════════════════
        private void BtnProcessReturn_Click(object sender, EventArgs e)
        {
            if (!_cartLines.Any())
            { ShowBuilderStatusToast("Add at least one item to the return before processing.", false); return; }
            if (string.IsNullOrWhiteSpace(_returnReason))
            { ShowBuilderStatusToast("Please select a Return Reason.", false); return; }
            if (string.IsNullOrWhiteSpace(_dispositionCode))
            { ShowBuilderStatusToast("Please select a Disposition for the returned items.", false); return; }

            decimal subtotal = _cartLines.Sum(l => l.LineSubtotal);
            decimal taxRefund = _cartLines.Sum(l => l.LineTax);
            decimal totalRefund = subtotal + taxRefund;

            if (totalRefund <= 0)
            { ShowBuilderStatusToast("Return amount is zero.", false); return; }

            string sourceInvoices = string.Join(", ", _cartLines.Select(l => l.SourceInvoiceNo).Distinct());

            var confirm = MessageBox.Show(
                $"Process return for {_cartLines.Count} item(s) across invoice(s) {sourceInvoices}?\n\n" +
                $"Customer   :  {_selectedCustomerName}\n" +
                $"Reason     :  {_returnReason}\n" +
                $"RMA Number :  {(_rmaNumber ?? "-")}\n" +
                $"Disposition:  {_dispositionCode}\n" +
                $"Subtotal   :  {Fmt(subtotal)}\n" +
                $"Tax Refund :  {Fmt(taxRefund)}\n" +
                $"Total      :  {Fmt(totalRefund)}\n" +
                $"Method     :  {RefundMethodLabel()}\n\n" +
                "This action cannot be undone.",
                "Confirm Return", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            try
            {
                string returnInvNo = SalesReturnRepository.NextReturnInvoiceNo();
                var returnRecord = new SalesReturnRecord
                {
                    ReturnInvoiceNo = returnInvNo,
                    OriginalInvoiceNo = sourceInvoices,
                    CustomerName = _selectedCustomerName,
                    RefundMethod = _refundMethod,
                    RefundTotal = totalRefund,
                    ReturnDate = DateTime.Now,
                    CompanyId = _companyId,
                    CashierName = "ADMIN",
                    Lines = _cartLines.Select(l => new SalesReturnLine
                    {
                        ItemName = l.Name,
                        UnitPrice = l.UnitPrice,
                        DiscountPct = l.DiscountPct,
                        TaxPct = l.TaxPct,
                        UOM = l.UOM,
                        ReturnQty = l.ReturnQty,
                        RefundAmt = l.LineRefund,
                        Barcode = l.Barcode,
                        OriginalInvoiceNo = l.SourceInvoiceNo
                    }).ToList()
                };

                SalesReturnRepository.EnsureSchema();
                SalesReturnRepository.SaveReturn(returnRecord);

                DashboardEventBus.Notify();
                PrintReturnReceipt(returnRecord);

                ShowBuilderStatusToast($"✓ Return {returnInvNo} processed — refund {Fmt(totalRefund)} via {RefundMethodLabel()}.", true);

                MessageBox.Show(
                    $"Return processed successfully!\n\nReturn Invoice:  {returnInvNo}\n" +
                    $"Source Invoice(s): {sourceInvoices}\n" +
                    $"Subtotal :       {Fmt(subtotal)}\n" +
                    $"Tax Refund :     {Fmt(taxRefund)}\n" +
                    $"Total Refund :   {Fmt(totalRefund)}\n" +
                    $"Method :         {RefundMethodLabel()}\n\nReturn receipt printed.",
                    "Return Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);

                _cartLines.Clear();
                _returnReason = ""; _rmaNumber = ""; _dispositionCode = "";
                cmbReturnReason.SelectedIndex = -1; txtRma.Text = ""; cmbDisposition.SelectedIndex = -1;
                BackToOrderDetail();
                _ = LoadAllOrdersAsync();
            }
            catch (Exception ex)
            {
                ShowBuilderStatusToast("Return error: " + ex.Message, false);
                MessageBox.Show("Failed to save return:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRINT RETURN RECEIPT — A4 layout (unchanged from v1; print output
        //  intentionally stays on a white physical page regardless of the
        //  on-screen theme)
        // ══════════════════════════════════════════════════════════════════
        private void PrintReturnReceipt(SalesReturnRecord r)
        {
            var data = BuildReturnReceiptData(r);
            var pd = new PrintDocument();
            pd.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
            pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            pd.PrintPage += (ps, pe) => DrawA4ReturnReceipt(pe.Graphics, data, pe.PageBounds, pe.Graphics.DpiX);

            var preview = new PrintPreviewDialog
            {
                Document = pd,
                WindowState = FormWindowState.Maximized,
                StartPosition = FormStartPosition.CenterParent
            };
            preview.ShowDialog(this);
        }

        private ReturnReceiptData BuildReturnReceiptData(SalesReturnRecord r)
        {
            decimal sub = r.Lines.Sum(l => Math.Round(l.UnitPrice * l.ReturnQty * (1m - l.DiscountPct / 100m), 2));
            decimal tax = r.Lines.Sum(l => Math.Round(l.UnitPrice * l.ReturnQty * (1m - l.DiscountPct / 100m) * l.TaxPct / 100m, 2));

            return new ReturnReceiptData
            {
                ReturnInvoiceNo = r.ReturnInvoiceNo,
                OriginalInvoiceNos = string.Join(", ", r.Lines.Select(l => l.OriginalInvoiceNo).Distinct()),
                CustomerName = r.CustomerName,
                ReturnDate = r.ReturnDate,
                CashierName = r.CashierName,
                RefundMethod = RefundMethodLabel(r.RefundMethod),
                ReturnReason = _returnReason,
                RmaNumber = _rmaNumber,
                DispositionCode = _dispositionCode,
                RefundSubtotal = sub,
                RefundTax = tax,
                RefundTotal = r.RefundTotal,
                CurrencySymbol = _currencySymbol,
                CompanyName = _companyName,
                CompanyAddress = _companyAddress,
                CompanyPhone = _companyPhone,
                CompanyVat = _companyVat,
                CompanyWebsite = _companyWebsite,
                SalesOfficeInfo = _salesOfficeInfo,
                Lines = r.Lines.Select(l => new ReturnReceiptLine
                {
                    OriginalInvoiceNo = l.OriginalInvoiceNo,
                    ItemName = l.ItemName,
                    ReturnQty = l.ReturnQty,
                    UOM = string.IsNullOrWhiteSpace(l.UOM) ? "EA" : l.UOM,
                    UnitPrice = l.UnitPrice,
                    DiscountPct = l.DiscountPct,
                    TaxPct = l.TaxPct,
                    Subtotal = Math.Round(l.UnitPrice * l.ReturnQty * (1m - l.DiscountPct / 100m), 2),
                    TaxAmt = Math.Round(l.UnitPrice * l.ReturnQty * (1m - l.DiscountPct / 100m) * l.TaxPct / 100m, 2),
                    RefundAmt = l.RefundAmt
                }).ToList()
            };
        }

        private static void DrawA4ReturnReceipt(Graphics g, ReturnReceiptData d, Rectangle bounds, float dpi)
        {
            float sc = bounds.Width / 794f;
            float bx = bounds.X, by = bounds.Y, bw = bounds.Width, bh = bounds.Height;
            string sym = string.IsNullOrWhiteSpace(d.CurrencySymbol) ? "P" : d.CurrencySymbol;

            var fBigBold = new Font("Arial", 13f * sc, FontStyle.Bold);
            var fBold = new Font("Arial", 9f * sc, FontStyle.Bold);
            var fNorm = new Font("Arial", 8.5f * sc);
            var fSmall = new Font("Arial", 7.8f * sc);
            var fTiny = new Font("Arial", 6.8f * sc);
            var fUnderBold = new Font("Arial", 8f * sc, FontStyle.Bold | FontStyle.Underline);

            var penThk = new Pen(Color.Black, 1.4f * sc);
            var penBlk = new Pen(Color.Black, 0.7f * sc);
            var bkBlack = Brushes.Black;
            var bkLight = new SolidBrush(Color.FromArgb(220, 220, 220));
            var bkTcBg = new SolidBrush(Color.FromArgb(255, 245, 245));
            var bkReturnBanner = new SolidBrush(Color.FromArgb(254, 226, 226));

            var cFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var lFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            var rFmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var lTopFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.LineLimit };
            var wrapFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

            float margin = 28f * sc;
            float left = bx + margin;
            float fullW = bw - margin * 2f;
            float y = by + 18f * sc;

            void Txt(string s, Font f, Brush br, float x, float yt, float w, float h, StringFormat sf = null)
                => g.DrawString(s, f, br, new RectangleF(x, yt, w, h), sf ?? lFmt);

            float logoW = fullW * 0.34f;
            float compX = left + logoW + 5f * sc;
            float compW = fullW * 0.33f;
            float officeX = compX + compW + 3f * sc;
            float officeW = left + fullW - officeX;

            string companyName = d.CompanyName ?? "ABC";
            string companyPhone = d.CompanyPhone ?? "";
            string companyVat = d.CompanyVat ?? "";
            string companyWebsite = d.CompanyWebsite ?? "";
            string officeInfo = d.SalesOfficeInfo ?? "";

            var officeBlocks = string.IsNullOrWhiteSpace(officeInfo)
                ? Array.Empty<string>()
                : officeInfo.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);

            float offLinH = fSmall.GetHeight(g) + 3f * sc;
            float offH = 10f * sc;
            foreach (var blk in officeBlocks)
            {
                var parts = blk.Split('|');
                offH += fUnderBold.GetHeight(g) + 3f * sc;
                offH += (parts.Length - 1) * offLinH;
                offH += 5f * sc;
            }
            offH += 6f * sc;

            float cInnerW = compW - 14f * sc;
            float compH_content = 8f * sc;
            compH_content += fBold.GetHeight(g) + 5f * sc;
            if (!string.IsNullOrWhiteSpace(companyPhone)) compH_content += fSmall.GetHeight(g) + 4f * sc;
            if (!string.IsNullOrWhiteSpace(companyVat)) compH_content += fSmall.GetHeight(g) + 4f * sc;
            if (!string.IsNullOrWhiteSpace(companyWebsite)) compH_content += fSmall.GetHeight(g) + 4f * sc;
            compH_content += 6f * sc;

            float headerH = Math.Max(120f * sc, Math.Max(compH_content, offH));

            string[] logoPaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "flo.jpg"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "flo.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "flo.jpg"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "flo.png"),
            };
            string logoFile = logoPaths.FirstOrDefault(File.Exists);
            if (logoFile != null)
            {
                try
                {
                    using var logo = Image.FromFile(logoFile);
                    float pad = 8f * sc;
                    float maxW = logoW - pad * 2f;
                    float maxH = headerH - pad * 2f;
                    float ratio = Math.Min(maxW / logo.Width, maxH / logo.Height);
                    float lw = logo.Width * ratio;
                    float lh = logo.Height * ratio;
                    g.DrawImage(logo, left + pad, y + (headerH - lh) / 2f, lw, lh);
                }
                catch { Txt(companyName, fBigBold, bkBlack, left, y, logoW, headerH, cFmt); }
            }
            else
                Txt(companyName, fBigBold, bkBlack, left, y, logoW, headerH, cFmt);

            g.DrawRectangle(penThk, compX, y, compW, headerH);
            {
                float cx = compX + 7f * sc, cw = cInnerW, cy = y + 8f * sc;
                g.DrawString(companyName, fBold, bkBlack, new RectangleF(cx, cy, cw, fBold.GetHeight(g) + 2f), lFmt);
                cy += fBold.GetHeight(g) + 5f * sc;
                if (!string.IsNullOrWhiteSpace(companyPhone))
                { g.DrawString("Phone: " + companyPhone, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt); cy += fSmall.GetHeight(g) + 4f * sc; }
                if (!string.IsNullOrWhiteSpace(companyVat))
                { g.DrawString("Vat : " + companyVat, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt); cy += fSmall.GetHeight(g) + 4f * sc; }
                if (!string.IsNullOrWhiteSpace(companyWebsite))
                    g.DrawString("Website : " + companyWebsite, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt);
            }

            g.DrawRectangle(penThk, officeX, y, officeW, headerH);
            {
                float ox = officeX + 7f * sc, ow = officeW - 14f * sc, oy = y + 8f * sc;
                foreach (var blk in officeBlocks)
                {
                    var parts = blk.Split('|');
                    g.DrawString(parts[0].Trim(), fUnderBold, bkBlack, new RectangleF(ox, oy, ow, fUnderBold.GetHeight(g) + 2f), lFmt);
                    oy += fUnderBold.GetHeight(g) + 3f * sc;
                    for (int pi = 1; pi < parts.Length; pi++)
                    {
                        string part = parts[pi].Trim();
                        if (string.IsNullOrWhiteSpace(part)) continue;
                        g.DrawString(part, fSmall, bkBlack, new RectangleF(ox, oy, ow, fSmall.GetHeight(g) + 2f), lFmt);
                        oy += fSmall.GetHeight(g) + 3f * sc;
                    }
                    oy += 5f * sc;
                }
            }
            y += headerH + 10f * sc;

            float bannerW = fullW * 0.48f;
            float metaX = left + bannerW + 6f * sc;
            float metaW = left + fullW - metaX;
            float metaLineH = fSmall.GetHeight(g) + 4f * sc;
            float metaBoxH = 7f * metaLineH + 14f * sc;
            float returnHdrH = Math.Max(96f * sc, metaBoxH);

            g.FillRectangle(bkReturnBanner, left, y, bannerW, returnHdrH);
            g.DrawRectangle(penBlk, left, y, bannerW, returnHdrH);
            g.DrawString("SALES RETURN", fBigBold, bkBlack,
                new RectangleF(left + 12f * sc, y + (returnHdrH - fBigBold.GetHeight(g)) / 2f, bannerW - 16f * sc, fBigBold.GetHeight(g) + 4f), lFmt);

            g.DrawRectangle(penThk, metaX, y, metaW, returnHdrH);
            {
                float cx = metaX + 7f * sc, cw = metaW - 14f * sc, cy = y + 6f * sc, lw = cw * 0.42f;
                void MetaRow(string lbl, string val)
                {
                    g.DrawString(lbl, fBold, bkBlack, new RectangleF(cx, cy, lw, metaLineH), lFmt);
                    g.DrawString(val, fSmall, bkBlack, new RectangleF(cx + lw, cy, cw - lw, metaLineH), lFmt);
                    cy += metaLineH;
                }
                MetaRow("Return Invoice :", d.ReturnInvoiceNo ?? "");
                MetaRow("Source Invoice(s):", d.OriginalInvoiceNos ?? "");
                MetaRow("Date / Time    :", d.ReturnDate.ToString("dd/MM/yyyy HH:mm"));
                MetaRow("Cashier        :", d.CashierName ?? "");
                MetaRow("Reason         :", d.ReturnReason ?? "");
                MetaRow("RMA / Disposition:", $"{(string.IsNullOrWhiteSpace(d.RmaNumber) ? "-" : d.RmaNumber)} / {d.DispositionCode ?? "-"}");
                MetaRow("Refund Method  :", d.RefundMethod ?? "Cash");
            }
            y += returnHdrH + 10f * sc;

            float custW = fullW * 0.50f;
            float custInnerW = custW - 14f * sc;
            float custBoxH = Math.Max(60f * sc, 8f * sc + fBold.GetHeight(g) + 5f * sc + fNorm.GetHeight(g) + 4f * sc + 6f * sc);

            g.DrawRectangle(penThk, left, y, custW, custBoxH);
            {
                float cx = left + 7f * sc, cy = y + 8f * sc;
                g.DrawString("Customer", fBold, bkBlack, new RectangleF(cx, cy, custInnerW, fBold.GetHeight(g) + 2f), lFmt);
                cy += fBold.GetHeight(g) + 5f * sc;
                g.DrawString(string.IsNullOrWhiteSpace(d.CustomerName) ? "Walk-in" : d.CustomerName,
                    fNorm, bkBlack, new RectangleF(cx, cy, custInnerW, fNorm.GetHeight(g) + 2f), lFmt);
            }
            y += custBoxH + 10f * sc;

            float[] iPcts = { 0.04f, 0.11f, 0.22f, 0.06f, 0.06f, 0.11f, 0.06f, 0.06f, 0.11f, 0.17f };
            string[] iHdrs = { "#", "Invoice", "Description", "Ret Qty", "Unit", "Unit Price", "Disc%", "Tax%", "Tax Amt", "Refund Amt" };
            bool[] iRight = { false, false, false, true, false, true, true, true, true, true };
            float[] iWidths = iPcts.Select(p => fullW * p).ToArray();

            float iHdrH = fBold.GetHeight(g) * 2.0f + 10f * sc;
            float minRowH = fSmall.GetHeight(g) + 12f * sc;

            g.FillRectangle(bkLight, left, y, fullW, iHdrH);
            g.DrawRectangle(penThk, left, y, fullW, iHdrH);
            float ix = left;
            for (int i = 0; i < iHdrs.Length; i++)
            {
                if (i > 0) g.DrawLine(penBlk, ix, y, ix, y + iHdrH);
                var sf = new StringFormat { Alignment = iRight[i] ? StringAlignment.Far : StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(iHdrs[i], fBold, bkBlack, new RectangleF(ix + 3f * sc, y, iWidths[i] - 6f * sc, iHdrH), sf);
                ix += iWidths[i];
            }
            y += iHdrH;

            int rowNum = 1;
            foreach (var li in d.Lines)
            {
                float descH = g.MeasureString(li.ItemName ?? "", fSmall, new SizeF(iWidths[2] - 8f * sc, 999f), lTopFmt).Height;
                float rowH = Math.Max(minRowH, descH + 10f * sc);

                g.DrawRectangle(penBlk, left, y, fullW, rowH);

                string uom = string.IsNullOrWhiteSpace(li.UOM) ? "EA" : li.UOM;
                string disc = li.DiscountPct > 0 ? li.DiscountPct.ToString("F2") : "0.00";
                string taxP = li.TaxPct > 0 ? li.TaxPct.ToString("F2") : "0.00";

                string[] iVals =
                {
                    rowNum++.ToString(), li.OriginalInvoiceNo ?? "", li.ItemName ?? "", li.ReturnQty.ToString(), uom,
                    $"{sym} {li.UnitPrice:F2}", disc, taxP, $"{sym} {li.TaxAmt:F2}", $"{sym} {li.RefundAmt:F2}"
                };

                ix = left;
                for (int i = 0; i < iHdrs.Length; i++)
                {
                    if (i > 0) g.DrawLine(penBlk, ix, y, ix, y + rowH);
                    var sf = i == 2 ? lTopFmt
                           : iRight[i]
                             ? new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center }
                             : new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    g.DrawString(iVals[i], fSmall, bkBlack, new RectangleF(ix + 3f * sc, y + 2f * sc, iWidths[i] - 6f * sc, rowH - 4f * sc), sf);
                    ix += iWidths[i];
                }
                y += rowH;
            }
            g.DrawLine(penThk, left, y, left + fullW, y);

            float sigW = fullW * 0.58f;
            float totW = fullW * 0.38f;
            float totX = left + fullW - totW;

            float tRowH = fNorm.GetHeight(g) + 9f * sc;
            float totalsH = tRowH * 4f + fBold.GetHeight(g) + 9f * sc + 4f * sc;

            float sigLineH = fBold.GetHeight(g) + 14f * sc;
            float sig3H = sigLineH * 3f;

            string tc = "All returned goods are subject to inspection. Refunds — including applicable " +
                        "tax — are processed in accordance with the original payment method where " +
                        "applicable. This return is subject to our standard terms & conditions of sale, " +
                        "available on request.";
            float tcInnerW = sigW - 14f * sc;
            var tcSz = g.MeasureString(tc, fTiny, new SizeF(tcInnerW, 999f), wrapFmt);
            float tcH = tcSz.Height + 16f * sc;

            float footerH = Math.Max(sig3H + 6f * sc + tcH, totalsH) + 10f * sc;
            float pageBot = by + bh - 20f * sc;
            float footerY = Math.Max(y + 16f * sc, pageBot - footerH);

            float tv = footerY + 4f * sc;
            float tLblW = totW * 0.62f;
            float tValW = totW * 0.36f;

            void TotalRow(string label, string val, bool bold = false, bool topLine = false)
            {
                Font tf = bold ? fBold : fNorm;
                float rh = tf.GetHeight(g) + 9f * sc;
                if (topLine) g.DrawLine(penThk, totX, tv, totX + totW, tv);
                g.DrawString(label, tf, bkBlack, new RectangleF(totX + 5f * sc, tv + 3f * sc, tLblW - 5f * sc, rh), lFmt);
                g.DrawString(val, tf, bkBlack, new RectangleF(totX + tLblW, tv + 3f * sc, tValW - 4f * sc, rh), rFmt);
                tv += rh;
            }

            TotalRow("Items returned :", d.Lines.Sum(l => l.ReturnQty).ToString());
            TotalRow("Line items :", d.Lines.Count.ToString());
            TotalRow($"Subtotal ({sym}) :", $"{sym} {d.RefundSubtotal:F2}", topLine: true);
            TotalRow($"Tax Refund ({sym}) :", $"{sym} {d.RefundTax:F2}");
            TotalRow($"Total Refund ({sym}) :", $"{sym} {d.RefundTotal:F2}", bold: true, topLine: true);

            g.DrawRectangle(penThk, totX, footerY, totW, tv - footerY + 4f * sc);

            float sy = footerY;
            void SigRow(string label)
            {
                g.DrawString(label, fBold, bkBlack, new RectangleF(left, sy + sigLineH * 0.12f, sigW * 0.34f, sigLineH), lFmt);
                g.DrawLine(penBlk, left + sigW * 0.36f, sy + sigLineH - 3f * sc, left + sigW * 0.92f, sy + sigLineH - 3f * sc);
                sy += sigLineH;
            }
            SigRow("Received By :");
            SigRow("Signature   :");
            SigRow("Date        :");
            sy += 6f * sc;

            g.DrawRectangle(penBlk, left, sy, sigW, tcH);
            g.FillRectangle(bkTcBg, left + 1, sy + 1, sigW - 2, tcH - 2);
            g.DrawString(tc, fTiny, bkBlack, new RectangleF(left + 7f * sc, sy + 7f * sc, tcInnerW, tcH - 10f * sc), wrapFmt);

            fBigBold.Dispose(); fBold.Dispose(); fNorm.Dispose();
            fSmall.Dispose(); fTiny.Dispose(); fUnderBold.Dispose();
            bkLight.Dispose(); bkTcBg.Dispose(); bkReturnBanner.Dispose();
            penThk.Dispose(); penBlk.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
        private string Fmt(decimal v) => $"{_currencySymbol} {v:N2}";

        private string RefundMethodLabel(string m = null)
        {
            // Only Cash is supported as a refund method.
            return "Cash";
        }

        private void ShowBuilderStatusToast(string msg, bool ok)
        {
            if (lblBuilderStatus == null) return;
            lblBuilderStatus.Text = msg;
            lblBuilderStatus.ForeColor = ok ? AccGreen : AccRed;
        }

        private Region MakeRoundedRegion(Size size, int r) => new Region(RoundedPath(new Rectangle(0, 0, size.Width, size.Height), r));

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

        private Button MakeTitleBtn(string text, Point loc)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextMid,
                BackColor = PanelWhite,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(46, HEADER_H),
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BgPage);
        }

        private void Header_MouseDown(object sender, MouseEventArgs e)
        {
            if (_isMaximized) return;
            _drag = true;
            _dragCursor = Cursor.Position;
            _dragForm = Location;
        }

        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drag)
                Location = Point.Add(_dragForm, new Size(Point.Subtract(Cursor.Position, new Size(_dragCursor))));
        }
    }
}