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
    //  SALES RETURN — rebuilt around the same concept used in the React POS
    //  "Return Order" flow:
    //
    //      1. Pick a CUSTOMER (search, not an invoice number).
    //      2. Browse that customer's INVOICES.
    //      3. Open an invoice, set a Return Qty per line, and explicitly
    //         "Add to Return" each line you want (nothing is pre-selected).
    //      4. The RETURN CART can contain lines pulled from MULTIPLE invoices
    //         for that same customer — exactly like the React flow lets you
    //         flip between invoices and keep adding lines.
    //      5. Header-level Return Reason / RMA Number / Disposition Code
    //         (Credit, Scrap, Return to Vendor, Replace, Repair, Restock),
    //         mirroring the React form's fields + disposition step.
    //      6. Process Return saves one return record whose lines each carry
    //         their own originating invoice number, then prints a receipt.
    //
    //  NOTE ON REPOSITORY: this file assumes SalesReturnRepository exposes
    //  two additional read methods beyond what it already had:
    //      List<CustomerLite>  SearchCustomers(string query, int companyId)
    //      List<InvoiceLite>   GetInvoicesForCustomer(int customerId, int companyId)
    //  and that SalesReturnLine gained an OriginalInvoiceNo string property
    //  (since one return can now reference more than one source invoice).
    //  Add these if they don't already exist in the shared POSAPP.Invoice /
    //  repository layer.
    // ════════════════════════════════════════════════════════════════════════
    public class SalesReturnForm : Form
    {
        // ── Palette  (dark theme) ──────────────────────────────────────────────
        private static readonly Color BgPage = Color.FromArgb(22, 24, 30);
        private static readonly Color CardWhite = Color.FromArgb(32, 35, 44);
        private static readonly Color CardBorder = Color.FromArgb(50, 54, 66);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccCyan = Color.FromArgb(20, 184, 166);
        private static readonly Color TextDark = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMid = Color.FromArgb(160, 170, 188);
        private static readonly Color TextLight = Color.FromArgb(100, 110, 130);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color HeaderBg = Color.FromArgb(32, 35, 44);
        private static readonly Color HeaderBorder = Color.FromArgb(50, 54, 66);
        private static readonly Color InputBorder = Color.FromArgb(60, 65, 80);
        private static readonly Color InputFocus = Color.FromArgb(59, 130, 246);
        private static readonly Color RowAlt = Color.FromArgb(38, 42, 52);
        private static readonly Color SummaryBg = Color.FromArgb(28, 32, 42);
        private static readonly Color BadgeAdded = Color.FromArgb(20, 60, 30);
        private static readonly Color BadgeAddedT = Color.FromArgb(52, 211, 153);

        // ── Reason / Disposition option lists (mirrors the React form) ─────────
        private static readonly string[] ReturnReasonOptions =
        {
            "Damaged Goods", "Wrong Item Shipped", "Excess Stock", "Quality Issue",
            "Expired Product", "Pricing Discrepancy", "Customer Cancellation", "Other"
        };
        private static readonly string[] DispositionOptions =
        {
            "Credit", "Scrap", "Return to Vendor", "Replace", "Repair", "Restock"
        };

        // ── State ──────────────────────────────────────────────────────────────
        private readonly int _companyId;
        private string _currencySymbol = "P";
        private string _companyName = "";
        private string _companyAddress = "";
        private string _companyPhone = "";
        private string _companyVat = "";
        private string _companyWebsite = "";
        private string _salesOfficeInfo = "";
        private List<InvoiceWithLines> _customerInvoiceData = new List<InvoiceWithLines>();


        private int _selectedCustomerId;
        private string _selectedCustomerName = "";
        // was: private List<CustomerLite> _customerResults = new List<CustomerLite>();
        private List<CustomerFullDto> _customerResults = new List<CustomerFullDto>();

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

        // ── Controls ───────────────────────────────────────────────────────────
        private Panel panelHeader, panelFooter, panelContent;
        private Label lblTitle, lblStatus, lblRefundTotal;
        private Button btnProcessReturn, btnCancel;
        private Button btnMaximize;
        private Panel _scrollOuter;

        private ComboBox cmbCustomerSearch;
        private Label lblSelectedCustomer;

        private Panel panelInvoiceCards;
        private Label lblInvoiceCardsHint;

        private Panel panelInvoiceLineRows;
        private Label lblCurrentInvoiceHead;

        private Panel panelCartRows;
        private Label lblCartEmpty;

        private ComboBox cmbReturnReason;
        private TextBox txtRma;
        private ComboBox cmbDisposition;
        private Button btnMethodCash;
        private Label _lblSummarySubtotal, _lblSummaryTax, _lblSummaryTotal;

        // ── Layout constants ───────────────────────────────────────────────────
        private const int HEADER_H = 60;
        private const int FOOTER_H = 68;
        private const int CARD_RADIUS = 12;
        private const int CARD_MARGIN = 16;
        private const int CARD_PAD = 20;

        // ══════════════════════════════════════════════════════════════════════
        //  MODELS
        // ══════════════════════════════════════════════════════════════════════
      

        /// <summary>One line of the invoice currently being browsed, before it is added to the cart.</summary>
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
            public int ReturnQty { get; set; } = 0;   // starts at 0 — nothing pre-selected, like the React table
            public bool Added { get; set; } = false;
        }

        /// <summary>A line that has been explicitly added to the return cart. Carries its own source invoice.</summary>
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

        // ── A4 receipt DTOs ────────────────────────────────────────────────────
        private class ReturnReceiptData
        {
            public string ReturnInvoiceNo { get; set; }
            public string OriginalInvoiceNos { get; set; }   // comma-joined; a return can span invoices now
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

        // ══════════════════════════════════════════════════════════════════════
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

        // ══════════════════════════════════════════════════════════════════════
        //  UI BUILD
        // ══════════════════════════════════════════════════════════════════════
        private void InitUI()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgPage;
            ClientSize = new Size(900, 900);
            KeyPreview = true;
            Text = "Sales Return";
            MinimumSize = new Size(760, 620);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer, true);

            BuildHeader();
            BuildFooter();
            BuildScrollArea();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += (s, e) => { LayoutFooterButtons(); cmbCustomerSearch?.Focus(); };
        }

        // ── Header ─────────────────────────────────────────────────────────────
        private void BuildHeader()
        {
            panelHeader = new Panel { BackColor = HeaderBg, Dock = DockStyle.Top, Height = HEADER_H };
            panelHeader.Paint += (s, e) =>
            {
                e.Graphics.Clear(HeaderBg);
                using var pen = new Pen(HeaderBorder, 1f);
                e.Graphics.DrawLine(pen, 0, HEADER_H - 1, panelHeader.Width, HEADER_H - 1);
            };

            lblTitle = new Label
            {
                Text = "Sales Return",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(44, (HEADER_H - 20) / 2 - 1)
            };

            btnMaximize = MakeTitleBtn("□", new Point(ClientSize.Width - 92, 0));
            var btnClose = MakeTitleBtn("✕", new Point(ClientSize.Width - 46, 0));
            var btnMin = MakeTitleBtn("−", new Point(ClientSize.Width - 138, 0));

            btnClose.ForeColor = AccRed;
            btnClose.MouseEnter += (s, e) => btnClose.BackColor = Color.FromArgb(254, 226, 226);
            btnClose.MouseLeave += (s, e) => btnClose.BackColor = Color.Transparent;
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
            { Bounds = _normalBounds; _isMaximized = false; btnMaximize.Text = "□"; Region = null; }
            else
            {
                _normalBounds = Bounds; _isMaximized = true; btnMaximize.Text = "❐";
                Bounds = Screen.FromControl(this).WorkingArea; Region = null;
            }
            LayoutFooterButtons();
        }

        // ── Footer ─────────────────────────────────────────────────────────────
        private void BuildFooter()
        {
            panelFooter = new Panel { BackColor = CardWhite, Dock = DockStyle.Bottom, Height = FOOTER_H };
            panelFooter.Paint += (s, e) =>
            {
                e.Graphics.Clear(CardWhite);
                using var pen = new Pen(HeaderBorder, 1f);
                e.Graphics.DrawLine(pen, 0, 0, panelFooter.Width, 0);
            };

            lblStatus = new Label
            {
                Text = "Search for a customer to begin a return.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(420, FOOTER_H),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnCancel = MakeFooterBtn("Cancel", Color.FromArgb(229, 231, 235), TextMid, new Size(100, 40));
            btnCancel.Click += (s, e) => Close();

            btnProcessReturn = MakeFooterBtn("Process Return", AccGreen, Color.White, new Size(160, 40));
            btnProcessReturn.Enabled = false;
            btnProcessReturn.Click += BtnProcessReturn_Click;

            lblRefundTotal = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(280, FOOTER_H),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            panelFooter.Controls.AddRange(new Control[] { lblStatus, btnCancel, btnProcessReturn, lblRefundTotal });
            panelFooter.SizeChanged += (s, e) => LayoutFooterButtons();
            Controls.Add(panelFooter);
        }

        // ── Scroll area ────────────────────────────────────────────────────────
        private void BuildScrollArea()
        {
            _scrollOuter = new Panel
            {
                BackColor = BgPage,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(0)
            };
            Controls.Add(_scrollOuter);

            panelContent = new Panel { BackColor = BgPage, AutoSize = true, Width = _scrollOuter.ClientSize.Width };
            _scrollOuter.Controls.Add(panelContent);
            _scrollOuter.SizeChanged += (s, e) =>
            {
                panelContent.Width = _scrollOuter.ClientSize.Width;
                RelayoutCards();
            };

            BuildCustomerCard();
            BuildInvoicesCard();
            BuildInvoiceLinesCard();
            BuildCartCard();
            BuildDetailsCard();
            RelayoutCards();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 1 — CUSTOMER SEARCH  (replaces the old invoice-number search)
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardCustomer;
        private void BuildCustomerCard()
        {
            _cardCustomer = MakeCard();
            AddCardLabel(_cardCustomer, "Customer", 0, new Font("Segoe UI", 9F, FontStyle.Bold), TextDark);

            cmbCustomerSearch = new ComboBox
            {
                Font = new Font("Segoe UI", 11F),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(28, 32, 42),
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,   // back to list-only, no typing
                DisplayMember = "CustomerName",               // CHANGED — bind display text to the DTO property
                Size = new Size(340, 32),
                Location = new Point(0, 45)
            };
            cmbCustomerSearch.SelectedIndexChanged += CmbCustomerSearch_SelectedIndexChanged;
            _cardCustomer.Controls.Add(cmbCustomerSearch);
            lblSelectedCustomer = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = true,
                Visible = false,
                Location = new Point(0, 90)
            };
            _cardCustomer.Controls.Add(lblSelectedCustomer);

            panelContent.Controls.Add(_cardCustomer);

            _ = LoadAllCustomersAsync();   // CHANGED — populate the dropdown immediately
        }

        private async Task LoadAllCustomersAsync()
        {
            ShowStatus("Loading customers...", true);
            _customerResults = await SalesReturnRepository.GetActiveCustomersAsync(_companyId);

            cmbCustomerSearch.Items.Clear();
            foreach (var c in _customerResults)
                cmbCustomerSearch.Items.Add(c);          // CHANGED — bind the object itself, not a string

            ShowStatus(_customerResults.Count > 0
                ? "Select a customer to begin a return."
                : "No customers found.", _customerResults.Count > 0);
        }


        private void CmbCustomerSearch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbCustomerSearch.SelectedItem is not CustomerFullDto cust) return;
            SelectCustomer(cust);
        }

        private async void SelectCustomer(CustomerFullDto cust)
        {
            _selectedCustomerId = cust.CustomerID;
            _selectedCustomerName = cust.CustomerName;

            lblSelectedCustomer.Text = $"✓ {cust.CustomerName}";
            lblSelectedCustomer.Visible = true;

            _currentInvoiceNo = "";
            _currentInvoiceLines.Clear();
            _cartLines.Clear();
            RebuildInvoiceLineRows();
            RebuildCartRows();
            RecalcTotal();

            await LoadCustomerInvoicesAsync();
            ShowStatus($"Loaded invoices for {cust.CustomerName}. Pick one to view returnable items.", true);
        }

        private async Task LoadCustomerInvoicesAsync()
        {
            _customerInvoiceData = await SalesReturnRepository.GetInvoicesForCustomerAsync(_selectedCustomerId);
            RebuildInvoiceCards();
            _cardInvoices.Visible = true;
            RelayoutCards();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 2 — CUSTOMER'S INVOICES
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardInvoices;
        private void BuildInvoicesCard()
        {
            _cardInvoices = MakeCard();
            _cardInvoices.Visible = false;

            AddCardLabel(_cardInvoices, "Invoices", 0, new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            lblInvoiceCardsHint = new Label
            {
                Text = "Select an invoice to view its items.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 26)
            };
            _cardInvoices.Controls.Add(lblInvoiceCardsHint);

            panelInvoiceCards = new Panel
            {
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 52),
                Width = 1
            };
            _cardInvoices.Controls.Add(panelInvoiceCards);
            panelContent.Controls.Add(_cardInvoices);
        }



        private void RebuildInvoiceCards()
        {
            foreach (Control c in panelInvoiceCards.Controls) c.Dispose();
            panelInvoiceCards.Controls.Clear();

            // NEW — hide invoices that have already been fully returned instead of
            // showing them disabled in the grid.
            var visibleInvoices = _customerInvoiceData
                .Where(entry => !SalesReturnRepository.IsFullyReturned(entry.Header.InvoiceNo))
                .ToList();

            if (visibleInvoices.Count == 0)
            {
                lblInvoiceCardsHint.Text = _customerInvoiceData.Count == 0
                    ? "No invoices found for this customer."
                    : "All invoices for this customer have already been fully returned.";
                panelInvoiceCards.Height = 1;
                RelayoutCards();
                return;
            }
            lblInvoiceCardsHint.Text = "Select an invoice to view its items.";

            int colW = 220, colGap = 12, rowH = 78;
            int cols = Math.Max(1, (Math.Max(colW, panelInvoiceCards.Width) + colGap) / (colW + colGap));
            int x = 0, y = 0, col = 0;

            foreach (var entry in visibleInvoices)
            {
                var inv = entry.Header;
                bool isCurrent = string.Equals(inv.InvoiceNo, _currentInvoiceNo, StringComparison.OrdinalIgnoreCase);

                var card = new Panel
                {
                    BackColor = isCurrent ? Color.FromArgb(30, 46, 74) : RowAlt,
                    Size = new Size(colW, rowH),
                    Location = new Point(x, y),
                    Cursor = Cursors.Hand
                };
                DrawRoundedBorder(card, isCurrent ? InputFocus : CardBorder, 8);

                var lblNo = new Label
                {
                    Text = inv.InvoiceNo,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    ForeColor = TextDark,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(10, 8)
                };
                var lblDate = new Label
                {
                    Text = inv.InvoiceDate.ToString("dd MMM yyyy"),
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = TextMid,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(10, 30)
                };
                var lblLines = new Label
                {
                    Text = $"{inv.LineCount} item(s) · {Fmt(inv.Total)}",
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = TextLight,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(10, 50)
                };

                card.Controls.AddRange(new Control[] { lblNo, lblDate, lblLines });
                var entryRef = entry;
                //EventHandler clickHandler = (s, e) => SelectInvoice(entryRef);
                //card.Click += clickHandler;
                //foreach (Control child in card.Controls) child.Click += clickHandler;

                panelInvoiceCards.Controls.Add(card);

                col++;
                if (col >= cols) { col = 0; x = 0; y += rowH + colGap; }
                else x += colW + colGap;
            }
            panelInvoiceCards.Height = y + rowH;
            RelayoutCards();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 3 — CURRENT INVOICE'S RETURNABLE LINES
        //  Columns: Item | Purchased | Return Qty | UOM | Price | Action
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardInvoiceLines;
        private void BuildInvoiceLinesCard()
        {
            _cardInvoiceLines = MakeCard();
            _cardInvoiceLines.Visible = false;

            lblCurrentInvoiceHead = new Label
            {
                Text = "Invoice Items",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 0)
            };
            _cardInvoiceLines.Controls.Add(lblCurrentInvoiceHead);

            var hdr = new Panel
            {
                BackColor = SummaryBg,
                Size = new Size(1, 28),
                Location = new Point(0, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            hdr.Paint += (s, e) => { hdr.Width = _cardInvoiceLines.Width - CARD_PAD * 2; e.Graphics.Clear(SummaryBg); };

            string[] hdrs = { "Item Name", "Purchased", "UOM", "Price", "Return Qty", "" };
            int[] hdrX = { 0, 210, 280, 320, 400, 540 };
            foreach (var (t, x) in hdrs.Zip(hdrX, (a, b) => (a, b)))
                hdr.Controls.Add(new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = TextLight,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(x, 6)
                });
            _cardInvoiceLines.Controls.Add(hdr);

            panelInvoiceLineRows = new Panel
            {
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 62),
                Width = 1
            };
            _cardInvoiceLines.Controls.Add(panelInvoiceLineRows);
            panelContent.Controls.Add(_cardInvoiceLines);
        }

        //private void SelectInvoice(InvoiceWithLines entry)
        //{
        //    var inv = entry.Header;
        //    _currentInvoiceNo = inv.InvoiceNo;
        //    RebuildInvoiceCards();

        //    // NEW — pull qty already returned against this invoice so lines that
        //    // were returned in a PREVIOUS transaction don't show up as freshly
        //    // returnable again.
        //    var returnedQtys = SalesReturnRepository.GetReturnedQtys(inv.InvoiceNo);

        //    _currentInvoiceLines = new List<InvoiceLineCandidate>();
        //    foreach (var r in entry.Lines)
        //    {
        //        int already = 0;
        //        string key = !string.IsNullOrWhiteSpace(r.Barcode) ? r.Barcode.Trim() : r.ItemName?.Trim();
        //        if (!string.IsNullOrWhiteSpace(key) && returnedQtys.TryGetValue(key, out int rq))
        //            already = rq;

        //        var candidate = new InvoiceLineCandidate
        //        {
        //            ItemName = r.ItemName,
        //            UnitPrice = r.UnitPrice,
        //            DiscountPct = r.DiscountPct,
        //            TaxPct = r.TaxPct,
        //            UOM = string.IsNullOrWhiteSpace(r.UOM) ? "EA" : r.UOM,
        //            Barcode = r.Barcode,
        //            PurchasedQty = r.Qty,
        //            AlreadyReturnedQty = already,     // was: 0
        //            ReturnQty = 0
        //        };

        //        candidate.Added = _cartLines.Any(cl =>
        //            string.Equals(cl.SourceInvoiceNo, inv.InvoiceNo, StringComparison.OrdinalIgnoreCase) &&
        //            string.Equals(cl.Name, candidate.ItemName, StringComparison.OrdinalIgnoreCase));

        //        // Skip lines with nothing left to return, unless it's already sitting
        //        // in the current cart (keep the "✓ Added" row visible in that case).
        //        if (candidate.MaxReturnable > 0 || candidate.Added)
        //            _currentInvoiceLines.Add(candidate);
        //    }

        //    lblCurrentInvoiceHead.Text = $"Invoice Items — {inv.InvoiceNo}";
        //    RebuildInvoiceLineRows();
        //    _cardInvoiceLines.Visible = _currentInvoiceLines.Count > 0;
        //    if (_currentInvoiceLines.Count == 0)
        //        ShowStatus($"Invoice {inv.InvoiceNo} has no returnable items left.", false);
        //    else
        //        ShowStatus($"Set a Return Qty and click Add for each item you want to return from {inv.InvoiceNo}.", true);

        //    RelayoutCards();
        //}

        private void RebuildInvoiceLineRows()
        {
            foreach (Control c in panelInvoiceLineRows.Controls) c.Dispose();
            panelInvoiceLineRows.Controls.Clear();

            int y = 0;
            for (int i = 0; i < _currentInvoiceLines.Count; i++)
            {
                var row = BuildInvoiceLineRow(_currentInvoiceLines[i], y, i % 2 == 0);
                panelInvoiceLineRows.Controls.Add(row);
                y += 48;
            }
            panelInvoiceLineRows.Height = Math.Max(1, y);
            RelayoutCards();
        }

        private Panel BuildInvoiceLineRow(InvoiceLineCandidate line, int yOffset, bool alt)
        {
            const int ROW_H = 44;
            int rowW = Math.Max(1, panelInvoiceLineRows.Width);

            var row = new Panel
            {
                BackColor = alt ? CardWhite : RowAlt,
                Size = new Size(rowW, ROW_H),
                Location = new Point(0, yOffset),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            row.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(50, 54, 66), 1f);
                e.Graphics.DrawLine(pen, 0, ROW_H - 1, ((Panel)s).Width, ROW_H - 1);
            };

            row.Controls.Add(new Label
            {
                Text = line.ItemName,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                Size = new Size(200, ROW_H),
                Location = new Point(0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            row.Controls.Add(new Label
            {
                Text = $"{line.MaxReturnable} / {line.PurchasedQty}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(64, ROW_H),
                Location = new Point(210, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            row.Controls.Add(new Label
            {
                Text = line.UOM,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccCyan,
                BackColor = Color.Transparent,
                Size = new Size(36, ROW_H),
                Location = new Point(280, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            row.Controls.Add(new Label
            {
                Text = Fmt(line.UnitPrice),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(76, ROW_H),
                Location = new Point(320, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // Return Qty input
            var tbQty = new TextBox
            {
                Text = line.ReturnQty.ToString(),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(42, 46, 58),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                Size = new Size(60, 26),
                Location = new Point(400, (ROW_H - 26) / 2),
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
                Size = new Size(110, 30),
                Location = new Point(470, (ROW_H - 30) / 2),
                Cursor = Cursors.Hand
            };
            btnAction.FlatAppearance.BorderSize = 0;
            btnAction.Region = MakeRoundedRegion(btnAction.Size, 6);

            void RefreshActionButton()
            {
                if (line.Added)
                {
                    btnAction.Text = "✓ Added";
                    btnAction.BackColor = BadgeAdded;
                    btnAction.ForeColor = BadgeAddedT;
                }
                else
                {
                    btnAction.Text = "Add to Return";
                    btnAction.BackColor = AccBlue;
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
                    { ShowStatus("Enter a Return Qty greater than zero before adding.", false); return; }

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
                    ShowStatus($"{line.ItemName} added to the return.", true);
                }
                else
                {
                    _cartLines.RemoveAll(cl =>
                        string.Equals(cl.SourceInvoiceNo, _currentInvoiceNo, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(cl.Name, line.ItemName, StringComparison.OrdinalIgnoreCase));
                    line.Added = false;
                    tbQty.Enabled = true;
                    ShowStatus($"{line.ItemName} removed from the return.", true);
                }

                RefreshActionButton();
                RebuildCartRows();
                RecalcTotal();
            };

            row.Controls.AddRange(new Control[] { tbQty, btnAction });
            return row;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 4 — RETURN CART (can hold lines from several invoices)
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardCart;
        private void BuildCartCard()
        {
            _cardCart = MakeCard();
            _cardCart.Visible = false;

            AddCardLabel(_cardCart, "Items in this Return", 0, new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            var hdr = new Panel
            {
                BackColor = SummaryBg,
                Size = new Size(1, 28),
                Location = new Point(0, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            hdr.Paint += (s, e) => { hdr.Width = _cardCart.Width - CARD_PAD * 2; e.Graphics.Clear(SummaryBg); };

            string[] hdrs = { "Invoice", "Item Name", "Qty", "UOM", "Refund", "" };
            int[] hdrX = { 0, 120, 340, 380, 430, 540 };
            foreach (var (t, x) in hdrs.Zip(hdrX, (a, b) => (a, b)))
                hdr.Controls.Add(new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = TextLight,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(x, 6)
                });
            _cardCart.Controls.Add(hdr);

            lblCartEmpty = new Label
            {
                Text = "No items added yet. Open an invoice above and add items to return.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 66)
            };
            _cardCart.Controls.Add(lblCartEmpty);

            panelCartRows = new Panel
            {
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 62),
                Width = 1
            };
            _cardCart.Controls.Add(panelCartRows);
            panelContent.Controls.Add(_cardCart);
        }

        private void RebuildCartRows()
        {
            foreach (Control c in panelCartRows.Controls) c.Dispose();
            panelCartRows.Controls.Clear();

            lblCartEmpty.Visible = _cartLines.Count == 0;
            _cardCart.Visible = true; // card itself always visible once a customer is picked, shows empty-state text

            int y = 0;
            for (int i = 0; i < _cartLines.Count; i++)
            {
                var row = BuildCartRow(_cartLines[i], y, i % 2 == 0);
                panelCartRows.Controls.Add(row);
                y += 44;
            }
            panelCartRows.Height = Math.Max(1, y);
            RelayoutCards();
        }

        private Panel BuildCartRow(ReturnCartLine line, int yOffset, bool alt)
        {
            const int ROW_H = 40;
            int rowW = Math.Max(1, panelCartRows.Width);

            var row = new Panel
            {
                BackColor = alt ? CardWhite : RowAlt,
                Size = new Size(rowW, ROW_H),
                Location = new Point(0, yOffset),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            row.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(50, 54, 66), 1f);
                e.Graphics.DrawLine(pen, 0, ROW_H - 1, ((Panel)s).Width, ROW_H - 1);
            };

            row.Controls.Add(new Label
            {
                Text = line.SourceInvoiceNo,
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                Size = new Size(114, ROW_H),
                Location = new Point(0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            row.Controls.Add(new Label
            {
                Text = line.Name,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                Size = new Size(210, ROW_H),
                Location = new Point(120, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            row.Controls.Add(new Label
            {
                Text = line.ReturnQty.ToString(),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(36, ROW_H),
                Location = new Point(340, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });
            row.Controls.Add(new Label
            {
                Text = line.UOM,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccCyan,
                BackColor = Color.Transparent,
                Size = new Size(40, ROW_H),
                Location = new Point(380, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });
            row.Controls.Add(new Label
            {
                Text = Fmt(line.LineRefund),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                Size = new Size(100, ROW_H),
                Location = new Point(428, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var btnRemove = new Button
            {
                Text = "Remove",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = AccRed,
                BackColor = Color.FromArgb(60, 30, 30),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(80, 28),
                Location = new Point(rowW - 90, (ROW_H - 28) / 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            btnRemove.FlatAppearance.BorderSize = 0;
            btnRemove.Region = MakeRoundedRegion(btnRemove.Size, 6);
            btnRemove.Click += (s, e) =>
            {
                _cartLines.Remove(line);

                // if the removed line's invoice is currently open, flip its row back to "Add"
                if (string.Equals(line.SourceInvoiceNo, _currentInvoiceNo, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = _currentInvoiceLines.FirstOrDefault(c =>
                        string.Equals(c.ItemName, line.Name, StringComparison.OrdinalIgnoreCase));
                    if (candidate != null) candidate.Added = false;
                    RebuildInvoiceLineRows();
                }

                RebuildCartRows();
                RecalcTotal();
                ShowStatus($"{line.Name} removed from the return.", true);
            };

            row.Controls.Add(btnRemove);
            return row;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 5 — RETURN DETAILS: Reason / RMA / Disposition / Refund method / Totals
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardDetails;
        private void BuildDetailsCard()
        {
            _cardDetails = MakeCard();
            _cardDetails.Visible = false;

            AddCardLabel(_cardDetails, "Return Details", 0, new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            var lblReason = MkFieldLabel("Return Reason", 32);
            cmbReturnReason = MkCombo(ReturnReasonOptions, 52);
            cmbReturnReason.SelectedIndexChanged += (s, e) =>
                _returnReason = cmbReturnReason.SelectedItem?.ToString() ?? "";

            var lblRma = MkFieldLabel("RMA Number", 96);
            txtRma = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(28, 32, 42),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(220, 30),
                Location = new Point(0, 116)
            };
            txtRma.TextChanged += (s, e) => _rmaNumber = txtRma.Text.Trim();

            var lblDisp = MkFieldLabel("Disposition", 160);
            cmbDisposition = MkCombo(DispositionOptions, 180);
            cmbDisposition.SelectedIndexChanged += (s, e) =>
                _dispositionCode = cmbDisposition.SelectedItem?.ToString() ?? "";

            // Totals
            var lblSubCap = MkSumLbl("Subtotal (after discount)", 232);
            _lblSummarySubtotal = MkSumVal("", 232);
            var lblTaxCap = MkSumLbl("Tax Refund", 256);
            _lblSummaryTax = MkSumVal("", 256);
            var div = new Panel { BackColor = CardBorder, Size = new Size(300, 1), Location = new Point(0, 282) };
            var lblTotalCap = new Label
            {
                Text = "Total Return Amount",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 290)
            };
            _lblSummaryTotal = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 312)
            };

            var lblMethod = MkFieldLabel("Refund Method", 348);
            btnMethodCash = MakeSummaryMethodBtn("Cash", AccGreen, new Point(0, 368));
            btnMethodCash.Click += (s, e) => SetRefundMethod("cash");

            _cardDetails.Controls.AddRange(new Control[]
            {
                lblReason, cmbReturnReason,
                lblRma, txtRma,
                lblDisp, cmbDisposition,
                lblSubCap, _lblSummarySubtotal,
                lblTaxCap, _lblSummaryTax,
                div, lblTotalCap, _lblSummaryTotal,
                lblMethod, btnMethodCash
            });

            panelContent.Controls.Add(_cardDetails);
            SetRefundMethod("cash");
        }

        private Label MkFieldLabel(string text, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = TextLight,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(0, y)
        };

        private ComboBox MkCombo(string[] items, int y)
        {
            var cmb = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(28, 32, 42),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 30),
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
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(0, y)
        };
        private Label MkSumVal(string text, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextDark,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(260, y)
        };

        private Button MakeSummaryMethodBtn(string text, Color fg, Point loc)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = fg == AccGreen ? Color.White : fg,
                BackColor = fg == AccGreen ? AccGreen : Color.FromArgb(243, 244, 246),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(80, 36),
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = MakeRoundedRegion(b.Size, 8);
            return b;
        }

        private new void SetRefundMethod(string method)
        {
            _refundMethod = method;
            Color offBg = Color.FromArgb(42, 46, 58);
            Color offFg = TextMid;
            btnMethodCash.BackColor = method == "cash" ? AccGreen : offBg;
            btnMethodCash.ForeColor = method == "cash" ? Color.White : offFg;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  RELAYOUT
        // ══════════════════════════════════════════════════════════════════════
        private void RelayoutCards()
        {
            if (panelContent == null) return;
            panelContent.Width = Math.Max(1, _scrollOuter.ClientSize.Width);
            int cardW = panelContent.Width - CARD_MARGIN * 2;
            int y = CARD_MARGIN;

            foreach (Panel card in new[] { _cardCustomer, _cardInvoices, _cardInvoiceLines, _cardCart, _cardDetails })
            {
                if (card == null) continue;
                card.Width = cardW;
                card.Location = new Point(CARD_MARGIN, y);

                if (card == _cardInvoiceLines && panelInvoiceLineRows != null)
                    panelInvoiceLineRows.Width = cardW - CARD_PAD * 2;
                if (card == _cardCart && panelCartRows != null)
                    panelCartRows.Width = cardW - CARD_PAD * 2;
               

                if (card.Visible) y += card.Height + CARD_MARGIN;
            }
            panelContent.Height = y;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  RECALC TOTAL
        // ══════════════════════════════════════════════════════════════════════
        private void RecalcTotal()
        {
            decimal subtotal = _cartLines.Sum(l => l.LineSubtotal);
            decimal tax = _cartLines.Sum(l => l.LineTax);
            decimal total = subtotal + tax;

            if (_lblSummarySubtotal != null)
            {
                _lblSummarySubtotal.Text = subtotal > 0 ? Fmt(subtotal) : "—";
                _lblSummarySubtotal.ForeColor = subtotal > 0 ? TextDark : TextLight;
            }
            if (_lblSummaryTax != null)
            {
                _lblSummaryTax.Text = tax > 0 ? Fmt(tax) : "—";
                _lblSummaryTax.ForeColor = tax > 0 ? AccBlue : TextLight;
            }
            if (_lblSummaryTotal != null)
            {
                _lblSummaryTotal.Text = total > 0 ? Fmt(total) : "—";
                _lblSummaryTotal.ForeColor = total > 0 ? TextDark : TextLight;
            }

            lblRefundTotal.Text = total > 0 ? $"Refund:  {Fmt(total)}" : "";
            lblRefundTotal.ForeColor = total > 0 ? TextGreen : TextMid;

            _cardDetails.Visible = !string.IsNullOrEmpty(_selectedCustomerName);   // was: _selectedCustomerId.HasValue
            btnProcessReturn.Enabled = _cartLines.Count > 0;
            RelayoutCards();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PROCESS RETURN
        // ══════════════════════════════════════════════════════════════════════
        private void BtnProcessReturn_Click(object sender, EventArgs e)
        {
            if (!_cartLines.Any())
            { ShowStatus("Add at least one item to the return before processing.", false); return; }

            if (string.IsNullOrWhiteSpace(_returnReason))
            { ShowStatus("Please select a Return Reason.", false); return; }

            if (string.IsNullOrWhiteSpace(_dispositionCode))
            { ShowStatus("Please select a Disposition for the returned items.", false); return; }

            decimal subtotal = _cartLines.Sum(l => l.LineSubtotal);
            decimal taxRefund = _cartLines.Sum(l => l.LineTax);
            decimal totalRefund = subtotal + taxRefund;

            if (totalRefund <= 0)
            { ShowStatus("Return amount is zero.", false); return; }

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
                    OriginalInvoiceNo = sourceInvoices,       // comma list when the return spans invoices
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
                        OriginalInvoiceNo = l.SourceInvoiceNo   // per-line source invoice
                    }).ToList()
                };

                SalesReturnRepository.EnsureSchema();
                SalesReturnRepository.SaveReturn(returnRecord);

                DashboardEventBus.Notify();
                PrintReturnReceipt(returnRecord);

                ShowStatus(
                    $"✓ Return {returnInvNo} processed — refund {Fmt(totalRefund)} via {RefundMethodLabel()}.", true);

                MessageBox.Show(
                    $"Return processed successfully!\n\nReturn Invoice:  {returnInvNo}\n" +
                    $"Source Invoice(s): {sourceInvoices}\n" +
                    $"Subtotal :       {Fmt(subtotal)}\n" +
                    $"Tax Refund :     {Fmt(taxRefund)}\n" +
                    $"Total Refund :   {Fmt(totalRefund)}\n" +
                    $"Method :         {RefundMethodLabel()}\n\nReturn receipt printed.",
                    "Return Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);

                ResetForm();
            }
            catch (Exception ex)
            {
                ShowStatus("Return error: " + ex.Message, false);
                MessageBox.Show("Failed to save return:\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PRINT RETURN RECEIPT — A4 layout
        // ══════════════════════════════════════════════════════════════════════
        private void PrintReturnReceipt(SalesReturnRecord r)
        {
            var data = BuildReturnReceiptData(r);

            var pd = new PrintDocument();
            pd.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
            pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            pd.PrintPage += (ps, pe) =>
                DrawA4ReturnReceipt(pe.Graphics, data, pe.PageBounds, pe.Graphics.DpiX);

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

        // ══════════════════════════════════════════════════════════════════════
        //  DrawA4ReturnReceipt  — GDI+ A4 layout
        //  Columns: # | Invoice | Description | Ret Qty | Unit | Unit Price | Disc% | Tax% | Tax Amt | Refund Amt
        // ══════════════════════════════════════════════════════════════════════
        private static void DrawA4ReturnReceipt(
        Graphics g, ReturnReceiptData d, Rectangle bounds, float dpi)
        {
            float sc = bounds.Width / 794f;
            float bx = bounds.X;
            float by = bounds.Y;
            float bw = bounds.Width;
            float bh = bounds.Height;
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

            void Txt(string s, Font f, Brush br, float x, float yt, float w, float h,
                     StringFormat sf = null)
                => g.DrawString(s, f, br, new RectangleF(x, yt, w, h), sf ?? lFmt);

            // ── [1] HEADER (company/logo block) ──────────────────────────────
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
            // NOTE: companyAddress intentionally removed from the receipt.

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

            // ── [2] "SALES RETURN" BANNER + meta box ──────────────────────────
            float bannerW = fullW * 0.48f;
            float metaX = left + bannerW + 6f * sc;
            float metaW = left + fullW - metaX;

            float metaLineH = fSmall.GetHeight(g) + 4f * sc;
            float metaBoxH = 7f * metaLineH + 14f * sc;
            float returnHdrH = Math.Max(96f * sc, metaBoxH);

            g.FillRectangle(bkReturnBanner, left, y, bannerW, returnHdrH);
            g.DrawRectangle(penBlk, left, y, bannerW, returnHdrH);
            g.DrawString("SALES RETURN", fBigBold, bkBlack,
                new RectangleF(left + 12f * sc, y + (returnHdrH - fBigBold.GetHeight(g)) / 2f,
                               bannerW - 16f * sc, fBigBold.GetHeight(g) + 4f), lFmt);

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

            // ── [3] CUSTOMER ROW ──────────────────────────────────────────────
            float custW = fullW * 0.50f;
            float custInnerW = custW - 14f * sc;
            float custBoxH = Math.Max(60f * sc,
                8f * sc + fBold.GetHeight(g) + 5f * sc + fNorm.GetHeight(g) + 4f * sc + 6f * sc);

            g.DrawRectangle(penThk, left, y, custW, custBoxH);
            {
                float cx = left + 7f * sc, cy = y + 8f * sc;
                g.DrawString("Customer", fBold, bkBlack, new RectangleF(cx, cy, custInnerW, fBold.GetHeight(g) + 2f), lFmt);
                cy += fBold.GetHeight(g) + 5f * sc;
                g.DrawString(string.IsNullOrWhiteSpace(d.CustomerName) ? "Walk-in" : d.CustomerName,
                    fNorm, bkBlack, new RectangleF(cx, cy, custInnerW, fNorm.GetHeight(g) + 2f), lFmt);
            }
            y += custBoxH + 10f * sc;

            // ── [4] ITEMS TABLE ─────────────────────────────────────────────
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
                var sf = new StringFormat
                {
                    Alignment = iRight[i] ? StringAlignment.Far : StringAlignment.Near,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(iHdrs[i], fBold, bkBlack,
                    new RectangleF(ix + 3f * sc, y, iWidths[i] - 6f * sc, iHdrH), sf);
                ix += iWidths[i];
            }
            y += iHdrH;

            int rowNum = 1;
            foreach (var li in d.Lines)
            {
                float descH = g.MeasureString(li.ItemName ?? "", fSmall,
                    new SizeF(iWidths[2] - 8f * sc, 999f), lTopFmt).Height;
                float rowH = Math.Max(minRowH, descH + 10f * sc);

                g.DrawRectangle(penBlk, left, y, fullW, rowH);

                string uom = string.IsNullOrWhiteSpace(li.UOM) ? "EA" : li.UOM;
                string disc = li.DiscountPct > 0 ? li.DiscountPct.ToString("F2") : "0.00";
                string taxP = li.TaxPct > 0 ? li.TaxPct.ToString("F2") : "0.00";

                string[] iVals =
                {
            rowNum++.ToString(),
            li.OriginalInvoiceNo ?? "",
            li.ItemName ?? "",
            li.ReturnQty.ToString(),
            uom,
            $"{sym} {li.UnitPrice:F2}",
            disc,
            taxP,
            $"{sym} {li.TaxAmt:F2}",
            $"{sym} {li.RefundAmt:F2}"
        };

                ix = left;
                for (int i = 0; i < iHdrs.Length; i++)
                {
                    if (i > 0) g.DrawLine(penBlk, ix, y, ix, y + rowH);
                    var sf = i == 2 ? lTopFmt
                           : iRight[i]
                             ? new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center }
                             : new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    g.DrawString(iVals[i], fSmall, bkBlack,
                        new RectangleF(ix + 3f * sc, y + 2f * sc, iWidths[i] - 6f * sc, rowH - 4f * sc), sf);
                    ix += iWidths[i];
                }
                y += rowH;
            }
            g.DrawLine(penThk, left, y, left + fullW, y);

            // ── [5] FOOTER — totals block (right) + sig lines + T&C (left) ────
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
                g.DrawString(label, tf, bkBlack,
                    new RectangleF(totX + 5f * sc, tv + 3f * sc, tLblW - 5f * sc, rh), lFmt);
                g.DrawString(val, tf, bkBlack,
                    new RectangleF(totX + tLblW, tv + 3f * sc, tValW - 4f * sc, rh), rFmt);
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
                g.DrawString(label, fBold, bkBlack,
                    new RectangleF(left, sy + sigLineH * 0.12f, sigW * 0.34f, sigLineH), lFmt);
                g.DrawLine(penBlk,
                    left + sigW * 0.36f, sy + sigLineH - 3f * sc,
                    left + sigW * 0.92f, sy + sigLineH - 3f * sc);
                sy += sigLineH;
            }
            SigRow("Received By :");
            SigRow("Signature   :");
            SigRow("Date        :");
            sy += 6f * sc;

            g.DrawRectangle(penBlk, left, sy, sigW, tcH);
            g.FillRectangle(bkTcBg, left + 1, sy + 1, sigW - 2, tcH - 2);
            g.DrawString(tc, fTiny, bkBlack,
                new RectangleF(left + 7f * sc, sy + 7f * sc, tcInnerW, tcH - 10f * sc), wrapFmt);

            fBigBold.Dispose(); fBold.Dispose(); fNorm.Dispose();
            fSmall.Dispose(); fTiny.Dispose(); fUnderBold.Dispose();
            bkLight.Dispose(); bkTcBg.Dispose(); bkReturnBanner.Dispose();
            penThk.Dispose(); penBlk.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  RESET
        // ══════════════════════════════════════════════════════════════════════
        private void ResetForm()
        {
            _selectedCustomerId = 0;
            _selectedCustomerName = "";
            _customerInvoiceData.Clear();   
            _currentInvoiceNo = "";
            _currentInvoiceLines.Clear();
            _cartLines.Clear();
            _returnReason = "";
            _rmaNumber = "";
            _dispositionCode = "";

            cmbCustomerSearch.SelectedIndex = -1;

            lblSelectedCustomer.Visible = false;
            if (cmbReturnReason != null) cmbReturnReason.SelectedIndex = -1;
            if (txtRma != null) txtRma.Text = "";
            if (cmbDisposition != null) cmbDisposition.SelectedIndex = -1;

            _cardInvoices.Visible = false;
            _cardInvoiceLines.Visible = false;
            RebuildInvoiceLineRows();
            RebuildCartRows();
            RecalcTotal();

            btnProcessReturn.Enabled = false;
            ShowStatus("Search for a customer to begin a return.", false);
            RelayoutCards();
            cmbCustomerSearch.Focus();   // was: txtCustomerSearch.Focus();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════
        private string Fmt(decimal v) => $"{_currencySymbol} {v:N2}";

        private string RefundMethodLabel(string m = null)
        {
            switch (m ?? _refundMethod)
            {
                case "cash": return "Cash";
                case "upi": return "UPI / Digital";
                case "bank": return "Bank Transfer";
                default: return "Cash";
            }
        }

        private void ShowStatus(string msg, bool ok)
        {
            lblStatus.Text = msg;
            lblStatus.ForeColor = ok ? TextGreen : AccRed;
        }

        private void LayoutFooterButtons()
        {
            if (panelFooter == null) return;
            int bh = 40;
            int btnY = Math.Max(4, (panelFooter.Height - bh) / 2);
            int right = panelFooter.Width - 16;

            if (btnProcessReturn != null)
            { btnProcessReturn.Location = new Point(right - btnProcessReturn.Width, btnY); right -= btnProcessReturn.Width + 10; }
            if (btnCancel != null)
            { btnCancel.Location = new Point(right - btnCancel.Width, btnY); }
            if (lblRefundTotal != null)
                lblRefundTotal.Location = new Point(
                    panelFooter.Width - lblRefundTotal.Width - btnProcessReturn.Width - btnCancel.Width - 36, 0);
        }

        // ── Card factory ───────────────────────────────────────────────────────
        private Panel MakeCard()
        {
            var p = new Panel { BackColor = CardWhite, Padding = new Padding(CARD_PAD), Height = 80 };
            p.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(CardBorder, 1f);
                using var path = RoundedPath(new Rectangle(0, 0, p.Width - 1, p.Height - 1), CARD_RADIUS);
                g.DrawPath(pen, path);
            };
            p.ControlAdded += (s, e) => FitCard(p);
            p.Resize += (s, e) => FitCard(p);
            return p;
        }

        private void FitCard(Panel card)
        {
            if (card.Controls.Count == 0) return;
            int maxBottom = card.Controls.OfType<Control>().Max(c => c.Bottom + CARD_PAD);
            if (card.Height != maxBottom + CARD_PAD) card.Height = maxBottom + CARD_PAD;
        }

        private static void AddCardLabel(Panel card, string text, int y, Font f, Color fc)
        {
            card.Controls.Add(new Label
            {
                Text = text,
                Font = f,
                ForeColor = fc,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, y)
            });
        }

        private Button MakeFooterBtn(string text, Color bg, Color fg, Size sz)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = fg,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Size = sz,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = MakeRoundedRegion(sz, 8);
            if (bg == AccGreen)
            {
                b.MouseEnter += (s, e) => b.BackColor = Color.FromArgb(21, 128, 61);
                b.MouseLeave += (s, e) => b.BackColor = AccGreen;
            }
            return b;
        }

        private Button MakeTitleBtn(string text, Point loc)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(46, HEADER_H),
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static void DrawRoundedBorder(Control ctrl, Color color, int radius)
        {
            ctrl.Invalidate();
            ctrl.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(color, 1.5f);
                using var path = new GraphicsPath();
                var rect = new Rectangle(1, 1, ctrl.Width - 3, ctrl.Height - 3);
                int d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                e.Graphics.DrawPath(pen, path);
            };
        }

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
                Location = Point.Add(_dragForm,
                    new Size(Point.Subtract(Cursor.Position, new Size(_dragCursor))));
        }
    }
}