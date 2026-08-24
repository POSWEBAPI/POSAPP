// ════════════════════════════════════════════════════════════════════════
//  SalesReturnForm.cs
//
//  Brings the React "Return Order" concept (customer → browse their
//  invoices → pick lines → set Return Qty → Add) into this WinForms
//  screen, plus header-level RMA / Return Reason and a Disposition step
//  before saving — matching SalesOrderEntry.jsx.
//
//  Aligned to your real SalesReturnRepository:
//    - Customers come from  GetActiveCustomersAsync(companyId)  → List<POSAPP.CustomerFullDto>
//    - Invoices for a customer come from  GetInvoicesForCustomer(customerName, companyId)
//      — keyed by CUSTOMER NAME (string), not an ID → List<POSAPP.Invoice.InvoiceLite>
//    - Already-returned qty comes from  GetReturnedQtysAsync(invoiceNo)  (async)
//    - SalesReturnRecord's header fields are named RmaNumber / ReturnReason / DispositionCode
//    - SalesReturnLine already has OriginalInvoiceNo per line
// ════════════════════════════════════════════════════════════════════════

using POSAPP.Invoice;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace POSAPP.Sales
{
    public class SalesReturnForm : Form
    {
        // ── Palette (dark theme) ────────────────────────────────────────────
        private static readonly Color BgPage = Color.FromArgb(22, 24, 30);
        private static readonly Color CardWhite = Color.FromArgb(32, 35, 44);
        private static readonly Color CardBorder = Color.FromArgb(50, 54, 66);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccCyan = Color.FromArgb(20, 184, 166);
        private static readonly Color AccPurple = Color.FromArgb(168, 130, 246);
        private static readonly Color TextDark = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMid = Color.FromArgb(160, 170, 188);
        private static readonly Color TextLight = Color.FromArgb(100, 110, 130);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color HeaderBg = Color.FromArgb(32, 35, 44);
        private static readonly Color HeaderBorder = Color.FromArgb(50, 54, 66);
        private static readonly Color BadgeGreen = Color.FromArgb(20, 60, 30);
        private static readonly Color BadgeGreenT = Color.FromArgb(52, 211, 153);
        private static readonly Color InputBorder = Color.FromArgb(60, 65, 80);
        private static readonly Color InputFocus = Color.FromArgb(59, 130, 246);
        private static readonly Color RowAlt = Color.FromArgb(38, 42, 52);
        private static readonly Color SummaryBg = Color.FromArgb(28, 32, 42);

        // ── Return policy ────────────────────────────────────────────────────
        private const int MAX_RETURN_DAYS = 30;

        // ── Static option lists (mirrors RETURN_REASON_OPTIONS / DISPOSITION_OPTIONS in the React file) ──
        private static readonly string[] ReturnReasonOptions =
        {
            "Damaged Goods", "Wrong Item Shipped", "Excess Stock", "Quality Issue",
            "Expired Product", "Pricing Discrepancy", "Customer Cancellation", "Other"
        };
        private static readonly string[] LineReasonOptions =
            { "Defective", "Wrong Size", "Wrong Item", "Not as Described", "Changed Mind", "Other" };
        private static readonly string[] DispositionOptions =
            { "Credit", "Scrap", "Return to Vendor", "Replace", "Repair", "Restock" };

        // ── State ────────────────────────────────────────────────────────────
        private readonly int _companyId;
        private string _currencySymbol = "P";
        private string _companyName = "", _companyAddress = "", _companyPhone = "", _companyVat = "", _companyWebsite = "", _salesOfficeInfo = "";

        private int? _selectedCustomerId;
        private string _selectedCustomerName = "";
        private List<POSAPP.CustomerFullDto> _customers = new List<POSAPP.CustomerFullDto>();
        private List<InvoiceLite> _customerInvoices = new List<InvoiceLite>();

        private string _activeInvoiceNo;                                   // invoice currently being browsed
        private List<CandidateLine> _candidateLines = new List<CandidateLine>(); // lines of the browsed invoice, not yet added
        private List<ReturnLineItem> _returnLines = new List<ReturnLineItem>();  // lines actually in this return (can span invoices)

        private string _refundMethod = "cash";
        private string _rmaNumber = "";
        private string _returnReasonCode = "";
        private string _dispositionCode = "";

        private bool _drag;
        private Point _dragCursor, _dragForm;
        private bool _isMaximized = false;
        private Rectangle _normalBounds;

        // ── Controls ─────────────────────────────────────────────────────────
        private Panel panelHeader, panelFooter, panelContent;
        private Label lblTitle, lblStatus, lblRefundTotal;
        private Button btnProcessReturn, btnCancel, btnMaximize;
        private Panel _scrollOuter;

        private ComboBox cmbCustomer;
        private FlowLayoutPanel flowInvoices;
        private Panel panelCandidateLines;
        private Label lblCandidateHeader;
        private Button btnBackToInvoices;

        private TextBox txtRMA;
        private ComboBox cmbReturnReason;

        private Panel panelLines;
        private Panel panelRefundMethod;
        private Button btnMethodCash;

        // ── Layout constants ────────────────────────────────────────────────
        private const int HEADER_H = 60;
        private const int FOOTER_H = 68;
        private const int CARD_RADIUS = 12;
        private const int CARD_MARGIN = 16;
        private const int CARD_PAD = 20;

        // ══════════════════════════════════════════════════════════════════
        //  MODELS
        // ══════════════════════════════════════════════════════════════════
        public class CustomerSummary
        {
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public override string ToString() => CustomerName;
        }

        public class InvoiceSummary
        {
            public string InvoiceNo { get; set; }
            public DateTime? InvoiceDate { get; set; }
            public decimal Total { get; set; }
            public int LineCount { get; set; }
        }

        /// <summary>A line from a browsed invoice that has not yet been added to the return.</summary>
        private class CandidateLine
        {
            public string SourceKey { get; set; }          // "{InvoiceNo}|{ItemName}|{Barcode}"
            public string InvoiceNo { get; set; }
            public string Name { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountPct { get; set; }
            public decimal TaxPct { get; set; }
            public string UOM { get; set; } = "EA";
            public string Barcode { get; set; }
            public int PurchasedQty { get; set; }           // remaining returnable qty on that invoice
            public int ReturnQty { get; set; }
            public bool Added { get; set; }
        }

        private class ReturnLineItem
        {
            public string SourceKey { get; set; }
            public string SourceInvoiceNo { get; set; }
            public string Name { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountPct { get; set; }
            public decimal TaxPct { get; set; }
            public int ReturnQty { get; set; }
            public string Barcode { get; set; }
            public string UOM { get; set; } = "EA";
            public string Reason { get; set; } = "Defective";

            public decimal LineSubtotal => Math.Round(UnitPrice * ReturnQty * (1m - DiscountPct / 100m), 2);
            public decimal LineTax => Math.Round(LineSubtotal * TaxPct / 100m, 2);
            public decimal LineRefund => LineSubtotal + LineTax;
        }

        // ── A4 receipt DTOs ──────────────────────────────────────────────────
        private class ReturnReceiptData
        {
            public string ReturnInvoiceNo { get; set; }
            public string OriginalInvoiceNos { get; set; }     // comma-joined — may span several invoices now
            public string CustomerName { get; set; }
            public DateTime ReturnDate { get; set; }
            public string CashierName { get; set; }
            public string RefundMethod { get; set; }
            public string RMANumber { get; set; }
            public string ReturnReasonCode { get; set; }
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
            public string InvoiceNo { get; set; }
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
            ClientSize = new Size(860, 900);
            KeyPreview = true;
            Text = "Sales Return";
            MinimumSize = new Size(720, 620);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);

            BuildHeader();
            BuildFooter();
            BuildScrollArea();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += (s, e) => { LayoutFooterButtons(); LoadCustomers(); cmbCustomer?.Focus(); };
        }

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
                Text = "Select a customer to begin a return.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, FOOTER_H),
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

        private void BuildScrollArea()
        {
            _scrollOuter = new Panel { BackColor = BgPage, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0) };
            Controls.Add(_scrollOuter);

            panelContent = new Panel { BackColor = BgPage, AutoSize = true, Width = _scrollOuter.ClientSize.Width };
            _scrollOuter.Controls.Add(panelContent);
            _scrollOuter.SizeChanged += (s, e) => { panelContent.Width = _scrollOuter.ClientSize.Width; RelayoutCards(); };

            BuildCustomerCard();
            BuildInvoiceListCard();
            BuildCandidateLinesCard();
            BuildReturnDetailsCard();
            BuildItemsCard();
            BuildSummaryCard();
            RelayoutCards();
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARD 1 — CUSTOMER PICKER   (concept: React's Customer dropdown)
        // ══════════════════════════════════════════════════════════════════
        private Panel _cardCustomer;
        private void BuildCustomerCard()
        {
            _cardCustomer = MakeCard();
            AddCardLabel(_cardCustomer, "Customer", 0, new Font("Segoe UI", 9F, FontStyle.Bold), TextDark);

            cmbCustomer = new ComboBox
            {
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(28, 32, 42),
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DisplayMember = "CustomerName",
                Size = new Size(420, 30),
                Location = new Point(0, 34)
            };
            cmbCustomer.SelectedIndexChanged += (s, e) =>
            {
                if (cmbCustomer.SelectedItem is POSAPP.CustomerFullDto cs)
                {
                    _selectedCustomerName = cs.CustomerName;
                    LoadCustomerInvoices();
                }
            };

            _cardCustomer.Controls.Add(cmbCustomer);
            panelContent.Controls.Add(_cardCustomer);
        }

        private async void LoadCustomers()
        {
            try
            {
                _customers = await SalesReturnRepository.GetActiveCustomersAsync(_companyId)
                             ?? new List<POSAPP.CustomerFullDto>();
            }
            catch (Exception ex)
            {
                ShowStatus("Could not load customers: " + ex.Message, false);
                _customers = new List<POSAPP.CustomerFullDto>();
            }
            cmbCustomer.Items.Clear();
            cmbCustomer.Items.AddRange(_customers.Cast<object>().ToArray());
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARD 2 — INVOICES FOR THE SELECTED CUSTOMER  (React's invoice grid)
        // ══════════════════════════════════════════════════════════════════
        private Panel _cardInvoiceList;
        private Label _lblInvoiceListHead;

        private void BuildInvoiceListCard()
        {
            _cardInvoiceList = MakeCard();
            _cardInvoiceList.Visible = false;

            _lblInvoiceListHead = new Label
            {
                Text = "Invoices",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 0)
            };

            flowInvoices = new FlowLayoutPanel
            {
                Location = new Point(0, 30),
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent
            };

            _cardInvoiceList.Controls.Add(_lblInvoiceListHead);
            _cardInvoiceList.Controls.Add(flowInvoices);
            panelContent.Controls.Add(_cardInvoiceList);
        }

        private void LoadCustomerInvoices()
        {
            if (string.IsNullOrWhiteSpace(_selectedCustomerName)) return;

            try
            {
                _customerInvoices = SalesReturnRepository.GetInvoicesForCustomer(_selectedCustomerName, _companyId)
                                     ?? new List<InvoiceLite>();
            }
            catch (Exception ex)
            {
                ShowStatus("Could not load invoices: " + ex.Message, false);
                _customerInvoices = new List<InvoiceLite>();
            }

            _activeInvoiceNo = null;
            _cardCandidateLines.Visible = false;
            RebuildInvoiceCards();
            _cardInvoiceList.Visible = true;
            RelayoutCards();

            ShowStatus(_customerInvoices.Count == 0
                ? $"No invoices found for {_selectedCustomerName}."
                : $"{_customerInvoices.Count} invoice(s) found for {_selectedCustomerName}. Select one to view items.", true);
        }

        private void RebuildInvoiceCards()
        {
            foreach (Control c in flowInvoices.Controls) c.Dispose();
            flowInvoices.Controls.Clear();

            foreach (var inv in _customerInvoices)
            {
                var card = new Panel
                {
                    Size = new Size(220, 78),
                    Margin = new Padding(0, 0, 10, 10),
                    BackColor = Color.FromArgb(28, 32, 42),
                    Cursor = Cursors.Hand,
                    Tag = inv.InvoiceNo
                };
                DrawRoundedBorder(card, InputBorder, 8);

                var lblNo = new Label
                {
                    Text = inv.InvoiceNo,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = AccBlue,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(12, 10)
                };
                var lblDate = new Label
                {
                    Text = inv.InvoiceDate.ToString("dd MMM yyyy"),
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = TextMid,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(12, 32)
                };
                var lblTotal = new Label
                {
                    Text = Fmt(inv.Total),
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    ForeColor = TextDark,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(12, 52)
                };

                card.Controls.AddRange(new Control[] { lblNo, lblDate, lblTotal });
                EventHandler clickHandler = (s, e) => SelectInvoice(inv.InvoiceNo);
                card.Click += clickHandler;
                foreach (Control child in card.Controls) child.Click += clickHandler;

                flowInvoices.Controls.Add(card);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARD 3 — CANDIDATE LINES OF THE BROWSED INVOICE
        //  (React: "Return Lines" table with a Return Qty box + Add button)
        // ══════════════════════════════════════════════════════════════════
        private Panel _cardCandidateLines;
        private Panel panelCandidateHeader;

        private void BuildCandidateLinesCard()
        {
            _cardCandidateLines = MakeCard();
            _cardCandidateLines.Visible = false;

            panelCandidateHeader = new Panel { BackColor = Color.Transparent, Size = new Size(1, 28), Location = Point.Empty };

            lblCandidateHeader = new Label
            {
                Text = "Invoice Items",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 4)
            };

            btnBackToInvoices = new Button
            {
                Text = "← Back to Invoices",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = AccBlue,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Location = new Point(220, 4)
            };
            btnBackToInvoices.FlatAppearance.BorderSize = 0;
            btnBackToInvoices.Click += (s, e) =>
            {
                _activeInvoiceNo = null;
                _cardCandidateLines.Visible = false;
                RelayoutCards();
            };

            panelCandidateHeader.Controls.AddRange(new Control[] { lblCandidateHeader, btnBackToInvoices });
            _cardCandidateLines.Controls.Add(panelCandidateHeader);

            panelCandidateLines = new Panel { BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, 36), Width = 1 };
            _cardCandidateLines.Controls.Add(panelCandidateLines);
            _cardCandidateLines.AutoSize = true;
            panelContent.Controls.Add(_cardCandidateLines);
        }


        private async void SelectInvoice(string invoiceNo)
        {
            var rows = SalesReturnRepository.LoadOriginalInvoiceLines(invoiceNo, _companyId);
            if (rows == null || rows.Count == 0)
            { ShowStatus($"Invoice '{invoiceNo}' has no line items.", false); return; }

            DateTime? invoiceDate = SalesReturnRepository.GetInvoiceSaleDate(invoiceNo);
            if (invoiceDate.HasValue)
            {
                int daysSinceSale = (DateTime.Today - invoiceDate.Value.Date).Days;
                if (daysSinceSale > MAX_RETURN_DAYS)
                {
                    MessageBox.Show(
                        $"This invoice cannot be returned.\n\n" +
                        $"Invoice Date : {invoiceDate.Value:dd MMM yyyy}\n" +
                        $"Days Elapsed : {daysSinceSale} day(s)\n" +
                        $"Return Window: {MAX_RETURN_DAYS} days\n\n" +
                        "The return period has expired.",
                        "Return Period Expired", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ShowStatus($"Invoice {invoiceNo} is {daysSinceSale} days old (max {MAX_RETURN_DAYS}).", false);
                    return;
                }
            }

            var alreadyReturned = await SalesReturnRepository.GetReturnedQtysAsync(invoiceNo);

            _candidateLines = new List<CandidateLine>();
            foreach (var r in rows)
            {
                int previouslyReturned = 0;
                foreach (var kv in alreadyReturned)
                {
                    if (string.Equals(kv.Key.Trim(), r.ItemName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(r.Barcode) && string.Equals(kv.Key.Trim(), r.Barcode.Trim(), StringComparison.OrdinalIgnoreCase)))
                    { previouslyReturned = kv.Value; break; }
                }

                // Also subtract quantity already added to THIS in-progress return, from any invoice.
                string key = $"{invoiceNo}|{r.ItemName}|{r.Barcode}";
                int alreadyInThisReturn = _returnLines.Where(l => l.SourceKey == key).Sum(l => l.ReturnQty);

                int remainingQty = r.Qty - previouslyReturned - alreadyInThisReturn;
                if (remainingQty <= 0) continue;

                _candidateLines.Add(new CandidateLine
                {
                    SourceKey = key,
                    InvoiceNo = invoiceNo,
                    Name = r.ItemName,
                    UnitPrice = r.UnitPrice,
                    DiscountPct = r.DiscountPct,
                    TaxPct = r.TaxPct,
                    UOM = string.IsNullOrWhiteSpace(r.UOM) ? "EA" : r.UOM,
                    Barcode = r.Barcode,
                    PurchasedQty = remainingQty,
                    ReturnQty = 0,
                    Added = _returnLines.Any(l => l.SourceKey == key)
                });
            }

            _activeInvoiceNo = invoiceNo;
            lblCandidateHeader.Text = $"Invoice Items — {invoiceNo}";
            RebuildCandidateRows();
            _cardCandidateLines.Visible = true;
            RelayoutCards();

            ShowStatus(_candidateLines.Count == 0
                ? $"Invoice {invoiceNo} has already been fully returned."
                : $"{_candidateLines.Count} returnable line(s) on {invoiceNo}. Set a Return Qty and click Add.", true);
        }

        private void RebuildCandidateRows()
        {
            foreach (Control c in panelCandidateLines.Controls) c.Dispose();
            panelCandidateLines.Controls.Clear();

            int y = 0;
            for (int i = 0; i < _candidateLines.Count; i++)
            {
                panelCandidateLines.Controls.Add(BuildCandidateRow(_candidateLines[i], y, i % 2 == 0));
                y += 46;
            }
            panelCandidateLines.Height = Math.Max(1, y);
            RelayoutCards();
        }

        private Panel BuildCandidateRow(CandidateLine c, int yOffset, bool alt)
        {
            const int ROW_H = 44;
            int rowW = Math.Max(1, panelCandidateLines.Width);

            var row = new Panel
            {
                BackColor = alt ? CardWhite : RowAlt,
                Size = new Size(rowW, ROW_H),
                Location = new Point(0, yOffset),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            row.Controls.Add(new Label
            {
                Text = c.Name,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                Size = new Size(220, ROW_H),
                Location = new Point(8, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            row.Controls.Add(new Label
            {
                Text = $"Qty: {c.PurchasedQty}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(70, ROW_H),
                Location = new Point(236, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            row.Controls.Add(new Label
            {
                Text = c.UOM,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccCyan,
                BackColor = Color.Transparent,
                Size = new Size(44, ROW_H),
                Location = new Point(306, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            row.Controls.Add(new Label
            {
                Text = Fmt(c.UnitPrice),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(84, ROW_H),
                Location = new Point(354, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var tbQty = new TextBox
            {
                Text = c.ReturnQty > 0 ? c.ReturnQty.ToString() : "",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(42, 46, 58),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                Size = new Size(56, 26),
                Location = new Point(446, (ROW_H - 26) / 2),
                Enabled = !c.Added
            };
            tbQty.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            tbQty.TextChanged += (s, e) =>
            {
                int.TryParse(tbQty.Text.Trim(), out int q);
                c.ReturnQty = Math.Max(0, Math.Min(q, c.PurchasedQty));
            };

            var btnAdd = new Button
            {
                Text = c.Added ? "✓ Added" : "Add",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = c.Added ? BadgeGreenT : Color.White,
                BackColor = c.Added ? BadgeGreen : AccBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(84, 28),
                Location = new Point(rowW - 96, (ROW_H - 28) / 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Enabled = !c.Added
            };
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Click += (s, e) => AddCandidateLineToReturn(c);

            row.Controls.AddRange(new Control[] { tbQty, btnAdd });
            return row;
        }

        private void AddCandidateLineToReturn(CandidateLine c)
        {
            if (c.ReturnQty <= 0)
            { ShowStatus("Enter a Return Qty greater than zero before adding.", false); return; }

            _returnLines.Add(new ReturnLineItem
            {
                SourceKey = c.SourceKey,
                SourceInvoiceNo = c.InvoiceNo,
                Name = c.Name,
                UnitPrice = c.UnitPrice,
                DiscountPct = c.DiscountPct,
                TaxPct = c.TaxPct,
                UOM = c.UOM,
                Barcode = c.Barcode,
                ReturnQty = c.ReturnQty,
                Reason = "Defective"
            });
            c.Added = true;

            RebuildCandidateRows();
            RebuildLineRows();
            RecalcTotal();

            _cardReturnDetails.Visible = true;
            _cardItems.Visible = true;
            _cardSummary.Visible = true;
            btnProcessReturn.Enabled = _returnLines.Count > 0;
            RelayoutCards();

            ShowStatus($"{c.Name} added to return ({c.ReturnQty} {c.UOM}).", true);
        }

        private void RemoveReturnLine(ReturnLineItem line)
        {
            _returnLines.Remove(line);

            // If its source invoice is still being browsed, flip its candidate back to "not added".
            if (_activeInvoiceNo == line.SourceInvoiceNo)
            {
                var cand = _candidateLines.FirstOrDefault(c => c.SourceKey == line.SourceKey);
                if (cand != null) { cand.Added = false; cand.ReturnQty = 0; }
                RebuildCandidateRows();
            }

            RebuildLineRows();
            RecalcTotal();

            bool anyLeft = _returnLines.Count > 0;
            _cardReturnDetails.Visible = anyLeft;
            _cardItems.Visible = anyLeft;
            _cardSummary.Visible = anyLeft;
            btnProcessReturn.Enabled = anyLeft;
            RelayoutCards();
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARD 4 — RETURN DETAILS  (header-level RMA Number + Return Reason)
        // ══════════════════════════════════════════════════════════════════
        private Panel _cardReturnDetails;
        private void BuildReturnDetailsCard()
        {
            _cardReturnDetails = MakeCard();
            _cardReturnDetails.Visible = false;

            AddCardLabel(_cardReturnDetails, "Return Details", 0, new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            var lblReasonCap = new Label
            {
                Text = "Return Reason *",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 34)
            };
            cmbReturnReason = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(260, 28),
                Location = new Point(0, 54)
            };
            cmbReturnReason.Items.AddRange(ReturnReasonOptions);
            cmbReturnReason.SelectedIndexChanged += (s, e) =>
                _returnReasonCode = cmbReturnReason.SelectedItem?.ToString() ?? "";

            var lblRmaCap = new Label
            {
                Text = "RMA Number (optional)",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(284, 34)
            };
            txtRMA = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F),
                Size = new Size(200, 28),
                Location = new Point(284, 54)
            };
            txtRMA.TextChanged += (s, e) => _rmaNumber = txtRMA.Text.Trim();

            _cardReturnDetails.Controls.AddRange(new Control[] { lblReasonCap, cmbReturnReason, lblRmaCap, txtRMA });
            panelContent.Controls.Add(_cardReturnDetails);
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARD 5 — ITEMS IN THIS RETURN
        //  Columns: Invoice | Item Name | UOM | Price | Disc% | Tax% | Qty | Reason | Refund | ✕
        // ══════════════════════════════════════════════════════════════════
        private Panel _cardItems;
        private void BuildItemsCard()
        {
            _cardItems = MakeCard();
            _cardItems.Visible = false;

            AddCardLabel(_cardItems, "Items to Return", 0, new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            var hdr = new Panel
            {
                BackColor = SummaryBg,
                Size = new Size(1, 32),
                Location = new Point(0, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            hdr.Paint += (s, e) => { hdr.Width = _cardItems.Width - CARD_PAD * 2; e.Graphics.Clear(SummaryBg); };

            string[] hdrs = { "Invoice", "Item Name", "Unit", "Price", "Disc%", "Tax%", "Ret Qty", "Reason" };
            int[] hdrX = { 8, 96, 260, 298, 356, 396, 436, 508 };
            foreach (var (t, x) in hdrs.Zip(hdrX, (a, b) => (a, b)))
                hdr.Controls.Add(new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = TextLight,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(x, 8)
                });

            _cardItems.Controls.Add(hdr);

            panelLines = new Panel { BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, 62), Width = 1 };
            _cardItems.Controls.Add(panelLines);
            _cardItems.AutoSize = true;
            panelContent.Controls.Add(_cardItems);
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARD 6 — RETURN SUMMARY
        // ══════════════════════════════════════════════════════════════════
        private Panel _cardSummary;
        private Label _lblSummarySubtotal, _lblSummaryTax, _lblSummaryTotal;

        private void BuildSummaryCard()
        {
            _cardSummary = MakeCard();
            _cardSummary.Visible = false;

            AddCardLabel(_cardSummary, "Return Summary", 0, new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            var lblSubCap = MkSumLbl("Subtotal (after discount)", 32);
            _lblSummarySubtotal = MkSumVal("", 32);
            var lblTaxCap = MkSumLbl("Tax Refund", 56);
            _lblSummaryTax = MkSumVal("", 56);
            var div = new Panel { BackColor = CardBorder, Size = new Size(300, 1), Location = new Point(0, 82) };

            var lblTotalCap = new Label
            {
                Text = "Total Return Amount",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 90)
            };
            _lblSummaryTotal = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 112)
            };

            btnMethodCash = MakeSummaryMethodBtn("Cash", AccGreen, new Point(0, 148));
            btnMethodCash.Click += (s, e) => SetRefundMethod("cash");

            _cardSummary.Controls.AddRange(new Control[]
            {
                lblSubCap, _lblSummarySubtotal, lblTaxCap, _lblSummaryTax, div,
                lblTotalCap, _lblSummaryTotal, btnMethodCash
            });

            panelRefundMethod = _cardSummary;
            panelContent.Controls.Add(_cardSummary);
            SetRefundMethod("cash");
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
            btnMethodCash.BackColor = method == "cash" ? AccGreen : offBg;
            btnMethodCash.ForeColor = method == "cash" ? Color.White : TextMid;
        }

        // ══════════════════════════════════════════════════════════════════
        //  RELAYOUT
        // ══════════════════════════════════════════════════════════════════
        private void RelayoutCards()
        {
            if (panelContent == null) return;
            panelContent.Width = Math.Max(1, _scrollOuter.ClientSize.Width);
            int cardW = panelContent.Width - CARD_MARGIN * 2;
            int y = CARD_MARGIN;

            foreach (Panel card in new[] { _cardCustomer, _cardInvoiceList, _cardCandidateLines, _cardReturnDetails, _cardItems, _cardSummary })
            {
                if (card == null) continue;
                card.Width = cardW;
                card.Location = new Point(CARD_MARGIN, y);
                if (card == _cardItems && panelLines != null) panelLines.Width = cardW - CARD_PAD * 2;
                if (card == _cardCandidateLines && panelCandidateLines != null) panelCandidateLines.Width = cardW - CARD_PAD * 2;
                if (card.Visible) y += card.Height + CARD_MARGIN;
            }
            panelContent.Height = y;
        }

        // ══════════════════════════════════════════════════════════════════
        //  REBUILD LINE ROWS  (the return's line list — can span invoices)
        // ══════════════════════════════════════════════════════════════════
        private void RebuildLineRows()
        {
            foreach (Control c in panelLines.Controls) c.Dispose();
            panelLines.Controls.Clear();

            int y = 0;
            for (int i = 0; i < _returnLines.Count; i++)
            {
                panelLines.Controls.Add(BuildLineRow(_returnLines[i], y, i % 2 == 0));
                y += 52;
            }
            panelLines.Height = Math.Max(1, y);
            RelayoutCards();
        }

        private Panel BuildLineRow(ReturnLineItem line, int yOffset, bool alt)
        {
            const int ROW_H = 48;
            int rowW = Math.Max(1, panelLines.Width);

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
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = AccPurple,
                BackColor = Color.Transparent,
                Size = new Size(84, ROW_H),
                Location = new Point(8, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            row.Controls.Add(new Label
            {
                Text = line.Name,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                Size = new Size(160, ROW_H),
                Location = new Point(96, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            row.Controls.Add(new Label
            {
                Text = line.UOM,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccCyan,
                BackColor = Color.Transparent,
                Size = new Size(36, ROW_H),
                Location = new Point(260, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            row.Controls.Add(new Label
            {
                Text = Fmt(line.UnitPrice),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(56, ROW_H),
                Location = new Point(298, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            row.Controls.Add(new Label
            {
                Text = line.DiscountPct > 0 ? $"{line.DiscountPct:F1}%" : "—",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                Size = new Size(38, ROW_H),
                Location = new Point(356, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            row.Controls.Add(new Label
            {
                Text = line.TaxPct > 0 ? $"{line.TaxPct:F1}%" : "—",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccBlue,
                BackColor = Color.Transparent,
                Size = new Size(38, ROW_H),
                Location = new Point(396, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            var spWrapper = new Panel
            {
                BackColor = Color.FromArgb(42, 46, 58),
                Size = new Size(64, 30),
                Location = new Point(436, (ROW_H - 30) / 2)
            };
            spWrapper.Region = MakeRoundedRegion(spWrapper.Size, 6);
            var tbQty = new TextBox
            {
                Text = line.ReturnQty.ToString(),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(42, 46, 58),
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center,
                Size = new Size(56, 24),
                Location = new Point(4, 3),
                MaxLength = 4
            };
            tbQty.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            void ApplyQtyInput()
            {
                if (int.TryParse(tbQty.Text.Trim(), out int entered) && entered > 0)
                { line.ReturnQty = entered; tbQty.Text = entered.ToString(); }
                else { RemoveReturnLine(line); return; }
                RecalcTotal();
                UpdateRowRefund(row, line);
            }
            tbQty.Leave += (s, e) => ApplyQtyInput();
            tbQty.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ApplyQtyInput(); e.Handled = true; e.SuppressKeyPress = true; } };
            spWrapper.Controls.Add(tbQty);

            var cmbReason = new ComboBox
            {
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.FromArgb(42, 46, 58),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(92, 28),
                Location = new Point(508, (ROW_H - 28) / 2)
            };
            cmbReason.Items.AddRange(LineReasonOptions);
            cmbReason.SelectedItem = line.Reason ?? "Defective";
            cmbReason.SelectedIndexChanged += (s, e) => { if (cmbReason.SelectedItem != null) line.Reason = cmbReason.SelectedItem.ToString(); };

            var lblRef = new Label
            {
                Name = "lblRefund",
                Text = Fmt(line.LineRefund),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                Size = new Size(88, ROW_H),
                Location = new Point(rowW - 132, 0),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var btnRemove = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(28, 28),
                Location = new Point(rowW - 36, (ROW_H - 28) / 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            btnRemove.FlatAppearance.BorderSize = 0;
            btnRemove.Click += (s, e) => RemoveReturnLine(line);

            row.Controls.AddRange(new Control[] { spWrapper, cmbReason, lblRef, btnRemove });
            return row;
        }

        private void UpdateRowRefund(Panel row, ReturnLineItem line)
        {
            var lbl = row.Controls.OfType<Label>().FirstOrDefault(l => l.Name == "lblRefund");
            if (lbl != null) lbl.Text = Fmt(line.LineRefund);
        }

        // ══════════════════════════════════════════════════════════════════
        //  RECALC TOTAL
        // ══════════════════════════════════════════════════════════════════
        private void RecalcTotal()
        {
            decimal subtotal = _returnLines.Sum(l => l.LineSubtotal);
            decimal tax = _returnLines.Sum(l => l.LineTax);
            decimal total = subtotal + tax;

            if (_lblSummarySubtotal != null) { _lblSummarySubtotal.Text = subtotal > 0 ? Fmt(subtotal) : "—"; _lblSummarySubtotal.ForeColor = subtotal > 0 ? TextDark : TextLight; }
            if (_lblSummaryTax != null) { _lblSummaryTax.Text = tax > 0 ? Fmt(tax) : "—"; _lblSummaryTax.ForeColor = tax > 0 ? AccBlue : TextLight; }
            if (_lblSummaryTotal != null) { _lblSummaryTotal.Text = total > 0 ? Fmt(total) : "—"; _lblSummaryTotal.ForeColor = total > 0 ? TextDark : TextLight; }

            lblRefundTotal.Text = total > 0 ? $"Refund:  {Fmt(total)}" : "";
            lblRefundTotal.ForeColor = total > 0 ? TextGreen : TextMid;
        }

        // ══════════════════════════════════════════════════════════════════
        //  PROCESS RETURN — validates header fields, then shows the
        //  Disposition step (mirrors React's showDispositionModal flow)
        //  before saving.
        // ══════════════════════════════════════════════════════════════════
        private void BtnProcessReturn_Click(object sender, EventArgs e)
        {
            if (!_returnLines.Any())
            { ShowStatus("Add at least one item to the return.", false); return; }

            if (string.IsNullOrWhiteSpace(_returnReasonCode))
            { ShowStatus("Please select a Return Reason before processing.", false); return; }

            using (var dispForm = new DispositionPickerForm())
            {
                if (dispForm.ShowDialog(this) != DialogResult.OK) return;
                _dispositionCode = dispForm.SelectedDisposition;
            }

            decimal subtotal = _returnLines.Sum(l => l.LineSubtotal);
            decimal taxRefund = _returnLines.Sum(l => l.LineTax);
            decimal totalRefund = subtotal + taxRefund;

            if (totalRefund <= 0)
            { ShowStatus("Return amount is zero.", false); return; }

            string invoiceList = string.Join(", ", _returnLines.Select(l => l.SourceInvoiceNo).Distinct());

            var confirm = MessageBox.Show(
                $"Process return for {_returnLines.Count} item(s) across invoice(s) {invoiceList}?\n\n" +
                $"Customer   :  {_selectedCustomerName}\n" +
                $"RMA Number :  {(string.IsNullOrWhiteSpace(_rmaNumber) ? "—" : _rmaNumber)}\n" +
                $"Reason     :  {_returnReasonCode}\n" +
                $"Disposition:  {_dispositionCode}\n\n" +
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
                    OriginalInvoiceNo = invoiceList,      // now potentially multi-invoice
                    CustomerName = _selectedCustomerName,
                    RefundMethod = _refundMethod,
                    RefundTotal = totalRefund,
                    ReturnDate = DateTime.Now,
                    CompanyId = _companyId,
                    CashierName = "ADMIN",
                    // ── new header fields — add these properties to SalesReturnRecord ──
                    RmaNumber = _rmaNumber,
                    ReturnReason = _returnReasonCode,
                    DispositionCode = _dispositionCode,
                    Lines = _returnLines.Select(l => new SalesReturnLine
                    {
                        ItemName = l.Name,
                        UnitPrice = l.UnitPrice,
                        DiscountPct = l.DiscountPct,
                        TaxPct = l.TaxPct,
                        UOM = l.UOM,
                        ReturnQty = l.ReturnQty,
                        RefundAmt = l.LineRefund,
                        Barcode = l.Barcode,
                        // ── new per-line field — add this property to SalesReturnLine ──
                        OriginalInvoiceNo = l.SourceInvoiceNo
                    }).ToList()
                };

                SalesReturnRepository.EnsureSchema();
                SalesReturnRepository.SaveReturn(returnRecord);

                DashboardEventBus.Notify();

                // Disposition = "Scrap" means the goods are written off rather
                // than restocked — skip any stock-return hook here. If/when you
                // add an inventory-return call for other dispositions, gate it
                // with this same check.
                if (!string.Equals(_dispositionCode, "Scrap", StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: call your inventory "return to stock" routine here,
                    // e.g. InventoryRepository.ReturnStock(returnRecord.Lines, _companyId);
                }

                PrintReturnReceipt(returnRecord);

                ShowStatus($"✓ Return {returnInvNo} processed — refund {Fmt(totalRefund)} via {RefundMethodLabel()}.", true);

                MessageBox.Show(
                    $"Return processed successfully!\n\nReturn Invoice:  {returnInvNo}\n" +
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
                MessageBox.Show("Failed to save return:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  DISPOSITION PICKER — small modal, mirrors React's showDispositionModal
        // ══════════════════════════════════════════════════════════════════
        private class DispositionPickerForm : Form
        {
            public string SelectedDisposition { get; private set; } = "";
            private ComboBox _cmb;

            public DispositionPickerForm()
            {
                Text = "Select Disposition";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = false; MinimizeBox = false;
                ClientSize = new Size(360, 190);
                BackColor = CardWhite;

                var lblTitle = new Label
                {
                    Text = "Select Disposition",
                    Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                    ForeColor = TextDark,
                    AutoSize = true,
                    Location = new Point(24, 20)
                };
                var lblSub = new Label
                {
                    Text = "Choose how the returned items should be handled",
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = TextMid,
                    AutoSize = true,
                    Location = new Point(24, 48)
                };

                _cmb = new ComboBox
                {
                    Font = new Font("Segoe UI", 10F),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Size = new Size(312, 30),
                    Location = new Point(24, 76)
                };
                _cmb.Items.AddRange(DispositionOptions);

                var btnCancel = new Button
                {
                    Text = "Cancel",
                    Size = new Size(150, 38),
                    Location = new Point(24, 132),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(229, 231, 235),
                    ForeColor = TextMid
                };
                btnCancel.FlatAppearance.BorderSize = 0;
                btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

                var btnOk = new Button
                {
                    Text = "Confirm",
                    Size = new Size(150, 38),
                    Location = new Point(186, 132),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = AccBlue,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                btnOk.FlatAppearance.BorderSize = 0;
                btnOk.Click += (s, e) =>
                {
                    if (_cmb.SelectedItem == null)
                    { MessageBox.Show("Please select a Disposition Code.", "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    SelectedDisposition = _cmb.SelectedItem.ToString();
                    DialogResult = DialogResult.OK;
                    Close();
                };

                Controls.AddRange(new Control[] { lblTitle, lblSub, _cmb, btnCancel, btnOk });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRINT RETURN RECEIPT — A4 layout
        // ══════════════════════════════════════════════════════════════════
        private void PrintReturnReceipt(SalesReturnRecord r)
        {
            var data = BuildReturnReceiptData(r);

            var pd = new PrintDocument();
            pd.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
            pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            pd.PrintPage += (ps, pe) => DrawA4ReturnReceipt(pe.Graphics, data, pe.PageBounds, pe.Graphics.DpiX);

            var preview = new PrintPreviewDialog { Document = pd, WindowState = FormWindowState.Maximized, StartPosition = FormStartPosition.CenterParent };
            preview.ShowDialog(this);
        }

        private ReturnReceiptData BuildReturnReceiptData(SalesReturnRecord r)
        {
            decimal sub = r.Lines.Sum(l => Math.Round(l.UnitPrice * l.ReturnQty * (1m - l.DiscountPct / 100m), 2));
            decimal tax = r.Lines.Sum(l => Math.Round(l.UnitPrice * l.ReturnQty * (1m - l.DiscountPct / 100m) * l.TaxPct / 100m, 2));

            return new ReturnReceiptData
            {
                ReturnInvoiceNo = r.ReturnInvoiceNo,
                OriginalInvoiceNos = r.OriginalInvoiceNo,
                CustomerName = r.CustomerName,
                ReturnDate = r.ReturnDate,
                CashierName = r.CashierName,
                RefundMethod = RefundMethodLabel(r.RefundMethod),
                RMANumber = r.RmaNumber,
                ReturnReasonCode = r.ReturnReason,
                DispositionCode = r.DispositionCode,
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
                    InvoiceNo = l.OriginalInvoiceNo,
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

        // ══════════════════════════════════════════════════════════════════
        //  DrawA4ReturnReceipt — GDI+ A4 layout
        //  Columns: # | Invoice | Description | Ret Qty | Unit | Unit Price | Disc% | Tax% | Tax Amt | Refund Amt
        // ══════════════════════════════════════════════════════════════════
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

            // [1] HEADER — unchanged from original (logo / company / office boxes)
            float logoW = fullW * 0.34f;
            float compX = left + logoW + 5f * sc;
            float compW = fullW * 0.33f;
            float officeX = compX + compW + 3f * sc;
            float officeW = left + fullW - officeX;

            string companyName = d.CompanyName ?? "ShriPOS";
            string companyAddress = d.CompanyAddress ?? "";
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
            float compH_content = 8f * sc + fBold.GetHeight(g) + 5f * sc;
            if (!string.IsNullOrWhiteSpace(companyAddress))
            {
                var sz = g.MeasureString(companyAddress, fSmall, new SizeF(cInnerW, 999f), wrapFmt);
                compH_content += sz.Height + 4f * sc;
            }
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
                    float lw = logo.Width * ratio, lh = logo.Height * ratio;
                    g.DrawImage(logo, left + pad, y + (headerH - lh) / 2f, lw, lh);
                }
                catch { Txt(companyName, fBigBold, bkBlack, left, y, logoW, headerH, cFmt); }
            }
            else Txt(companyName, fBigBold, bkBlack, left, y, logoW, headerH, cFmt);

            g.DrawRectangle(penThk, compX, y, compW, headerH);
            {
                float cx = compX + 7f * sc, cw = cInnerW, cy = y + 8f * sc;
                g.DrawString(companyName, fBold, bkBlack, new RectangleF(cx, cy, cw, fBold.GetHeight(g) + 2f), lFmt);
                cy += fBold.GetHeight(g) + 5f * sc;
                if (!string.IsNullOrWhiteSpace(companyAddress))
                {
                    var sz = g.MeasureString(companyAddress, fSmall, new SizeF(cw, 999f), wrapFmt);
                    g.DrawString(companyAddress, fSmall, bkBlack, new RectangleF(cx, cy, cw, sz.Height + 2f), wrapFmt);
                    cy += sz.Height + 4f * sc;
                }
                if (!string.IsNullOrWhiteSpace(companyPhone)) { g.DrawString("Phone: " + companyPhone, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt); cy += fSmall.GetHeight(g) + 4f * sc; }
                if (!string.IsNullOrWhiteSpace(companyVat)) { g.DrawString("Vat : " + companyVat, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt); cy += fSmall.GetHeight(g) + 4f * sc; }
                if (!string.IsNullOrWhiteSpace(companyWebsite)) g.DrawString("Website : " + companyWebsite, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt);
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

            // [2] "SALES RETURN" banner + meta box — now includes RMA / Reason / Disposition
            float bannerW = fullW * 0.52f;
            float metaX = left + bannerW + 6f * sc;
            float metaW = left + fullW - metaX;

            float metaLineH = fSmall.GetHeight(g) + 5f * sc;
            float metaBoxH = 8f * metaLineH + 14f * sc;
            float returnHdrH = Math.Max(80f * sc, metaBoxH);

            g.FillRectangle(bkReturnBanner, left, y, bannerW, returnHdrH);
            g.DrawRectangle(penBlk, left, y, bannerW, returnHdrH);
            g.DrawString("SALES RETURN", fBigBold, bkBlack,
                new RectangleF(left + 12f * sc, y + (returnHdrH - fBigBold.GetHeight(g)) / 2f, bannerW - 16f * sc, fBigBold.GetHeight(g) + 4f), lFmt);

            g.DrawRectangle(penThk, metaX, y, metaW, returnHdrH);
            {
                float cx = metaX + 7f * sc, cw = metaW - 14f * sc, cy = y + 7f * sc, lw = cw * 0.46f;
                void MetaRow(string lbl, string val)
                {
                    g.DrawString(lbl, fBold, bkBlack, new RectangleF(cx, cy, lw, metaLineH), lFmt);
                    g.DrawString(val, fSmall, bkBlack, new RectangleF(cx + lw, cy, cw - lw, metaLineH), lFmt);
                    cy += metaLineH;
                }
                MetaRow("Return Invoice :", d.ReturnInvoiceNo ?? "");
                MetaRow("Original Inv(s):", d.OriginalInvoiceNos ?? "");
                MetaRow("Date / Time    :", d.ReturnDate.ToString("dd/MM/yyyy HH:mm"));
                MetaRow("Cashier        :", d.CashierName ?? "");
                MetaRow("Refund Method  :", d.RefundMethod ?? "Cash");
                MetaRow("RMA Number     :", string.IsNullOrWhiteSpace(d.RMANumber) ? "—" : d.RMANumber);
                MetaRow("Return Reason  :", d.ReturnReasonCode ?? "");
                MetaRow("Disposition    :", d.DispositionCode ?? "");
            }
            y += returnHdrH + 10f * sc;

            // [3] Customer row
            float custW = fullW * 0.50f;
            float custInnerW = custW - 14f * sc;
            float custBoxH = Math.Max(60f * sc, 8f * sc + fBold.GetHeight(g) + 5f * sc + fNorm.GetHeight(g) + 4f * sc + 6f * sc);

            g.DrawRectangle(penThk, left, y, custW, custBoxH);
            {
                float cx = left + 7f * sc, cy = y + 8f * sc;
                g.DrawString("Customer", fBold, bkBlack, new RectangleF(cx, cy, custInnerW, fBold.GetHeight(g) + 2f), lFmt);
                cy += fBold.GetHeight(g) + 5f * sc;
                g.DrawString(string.IsNullOrWhiteSpace(d.CustomerName) ? "Walk-in" : d.CustomerName, fNorm, bkBlack, new RectangleF(cx, cy, custInnerW, fNorm.GetHeight(g) + 2f), lFmt);
            }
            y += custBoxH + 10f * sc;

            // [4] Items table — includes an Invoice column since a return can span several
            float[] iPcts = { 0.04f, 0.13f, 0.19f, 0.06f, 0.06f, 0.11f, 0.06f, 0.06f, 0.11f, 0.18f };
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
                    rowNum++.ToString(), li.InvoiceNo ?? "", li.ItemName ?? "", li.ReturnQty.ToString(), uom,
                    $"{sym} {li.UnitPrice:F2}", disc, taxP, $"{sym} {li.TaxAmt:F2}", $"{sym} {li.RefundAmt:F2}"
                };

                ix = left;
                for (int i = 0; i < iHdrs.Length; i++)
                {
                    if (i > 0) g.DrawLine(penBlk, ix, y, ix, y + rowH);
                    var sf = i == 2 ? lTopFmt
                           : iRight[i] ? new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center }
                                       : new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    g.DrawString(iVals[i], fSmall, bkBlack, new RectangleF(ix + 3f * sc, y + 2f * sc, iWidths[i] - 6f * sc, rowH - 4f * sc), sf);
                    ix += iWidths[i];
                }
                y += rowH;
            }
            g.DrawLine(penThk, left, y, left + fullW, y);

            // [5] Footer — totals + signatures + T&C (unchanged layout)
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
        //  RESET
        // ══════════════════════════════════════════════════════════════════
        private void ResetForm()
        {
            _returnLines.Clear();
            _candidateLines.Clear();
            _activeInvoiceNo = null;
            _selectedCustomerId = null;
            _selectedCustomerName = "";
            _rmaNumber = ""; _returnReasonCode = ""; _dispositionCode = "";

            cmbCustomer.SelectedIndex = -1;
            cmbCustomer.Text = "";
            if (txtRMA != null) txtRMA.Text = "";
            if (cmbReturnReason != null) cmbReturnReason.SelectedIndex = -1;

            if (_lblSummarySubtotal != null) _lblSummarySubtotal.Text = "";
            if (_lblSummaryTax != null) _lblSummaryTax.Text = "";
            if (_lblSummaryTotal != null) _lblSummaryTotal.Text = "";

            _cardInvoiceList.Visible = false;
            _cardCandidateLines.Visible = false;
            _cardReturnDetails.Visible = false;
            _cardItems.Visible = false;
            _cardSummary.Visible = false;

            RebuildLineRows();
            RecalcTotal();
            btnProcessReturn.Enabled = false;
            ShowStatus("Select a customer to begin a return.", false);
            RelayoutCards();
            cmbCustomer.Focus();
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
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

            if (btnProcessReturn != null) { btnProcessReturn.Location = new Point(right - btnProcessReturn.Width, btnY); right -= btnProcessReturn.Width + 10; }
            if (btnCancel != null) btnCancel.Location = new Point(right - btnCancel.Width, btnY);
            if (lblRefundTotal != null)
                lblRefundTotal.Location = new Point(panelFooter.Width - lblRefundTotal.Width - btnProcessReturn.Width - btnCancel.Width - 36, 0);
        }

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
            card.Controls.Add(new Label { Text = text, Font = f, ForeColor = fc, BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, y) });
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