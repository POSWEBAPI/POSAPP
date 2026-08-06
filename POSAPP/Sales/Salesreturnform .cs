using POSAPP.Invoice;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;

namespace POSAPP.Sales
{
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
        private static readonly Color BadgeGreen = Color.FromArgb(20, 60, 30);
        private static readonly Color BadgeGreenT = Color.FromArgb(52, 211, 153);
        private static readonly Color InputBorder = Color.FromArgb(60, 65, 80);
        private static readonly Color InputFocus = Color.FromArgb(59, 130, 246);
        private static readonly Color RowAlt = Color.FromArgb(38, 42, 52);
        private static readonly Color SummaryBg = Color.FromArgb(28, 32, 42);

        // ── Return policy ──────────────────────────────────────────────────────
        /// <summary>Maximum days after sale date that a return is allowed.</summary>
        private const int MAX_RETURN_DAYS = 30;

        // ── State ──────────────────────────────────────────────────────────────
        private readonly int _companyId;
        private string _currencySymbol = "P";
        private string _originalInvoiceNo = "";
        private string _customerName = "Walk-in";
        private List<ReturnLineItem> _returnLines = new List<ReturnLineItem>();
        private string _refundMethod = "cash";
        private bool _drag;
        private Point _dragCursor, _dragForm;
        public static bool IsQuotation { get; set; } = false;
        private bool _isMaximized = false;
        private Rectangle _normalBounds;
        private string _companyName = "";
        private string _companyAddress = "";
        private string _companyPhone = "";
        private string _companyVat = "";
        private string _companyWebsite = "";
        private string _salesOfficeInfo = "";

        // ── Controls ───────────────────────────────────────────────────────────
        private Panel panelHeader, panelFooter, panelContent;
        private Label lblTitle, lblStatus, lblInvoiceInfo, lblRefundTotal;
        private TextBox txtInvoiceNo;
        private Button btnSearch, btnProcessReturn, btnCancel;
        private Panel panelLines;
        private Panel panelSearchBar;
        private Panel panelRefundMethod;
        private Button btnMethodCash, btnMethodUpi, btnMethodBank;
        private Button btnMaximize;
        private Panel _scrollOuter;

        // ── Layout constants ───────────────────────────────────────────────────
        private const int HEADER_H = 60;
        private const int FOOTER_H = 68;
        private const int CARD_RADIUS = 12;
        private const int CARD_MARGIN = 16;
        private const int CARD_PAD = 20;

        // ══════════════════════════════════════════════════════════════════════
        //  INNER MODEL
        // ══════════════════════════════════════════════════════════════════════
        private class ReturnLineItem
        {
            public string Name { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountPct { get; set; }
            /// <summary>Tax percentage on this line (e.g. 9 for 9 %).</summary>
            public decimal TaxPct { get; set; }
            public int OriginalQty { get; set; }
            public int ReturnQty { get; set; }
            public string Barcode { get; set; }
            public string UOM { get; set; } = "EA";
            public bool Selected { get; set; } = true;
            public string Reason { get; set; } = "Defective";

            // ── Derived values ─────────────────────────────────────────────
            /// <summary>Pre-tax refund (unit price after discount × qty).</summary>
            public decimal LineSubtotal =>
                Math.Round(UnitPrice * ReturnQty * (1m - DiscountPct / 100m), 2);

            /// <summary>Tax portion of the refund.</summary>
            public decimal LineTax =>
                Math.Round(LineSubtotal * TaxPct / 100m, 2);

            /// <summary>Total refund including tax.</summary>
            public decimal LineRefund => LineSubtotal + LineTax;
        }

        // ── A4 receipt DTOs ────────────────────────────────────────────────────
        private class ReturnReceiptData
        {
            public string ReturnInvoiceNo { get; set; }
            public string OriginalInvoiceNo { get; set; }
            public string CustomerName { get; set; }
            public DateTime ReturnDate { get; set; }
            public string CashierName { get; set; }
            public string RefundMethod { get; set; }
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
            ClientSize = new Size(820, 860);
            KeyPreview = true;
            Text = "Sales Return";
            MinimumSize = new Size(700, 600);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer, true);

            BuildHeader();
            BuildFooter();
            BuildScrollArea();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += (s, e) => { LayoutFooterButtons(); txtInvoiceNo?.Focus(); };
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
                Text = "Enter an invoice number to begin a return.",
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

            BuildSearchCard();
            BuildInvoiceDetailsCard();
            BuildItemsCard();
            BuildSummaryCard();
            RelayoutCards();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 1 — INVOICE NUMBER SEARCH
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardSearch;
        private void BuildSearchCard()
        {
            _cardSearch = MakeCard();
            AddCardLabel(_cardSearch, "Invoice Number", 0,
                new Font("Segoe UI", 9F, FontStyle.Bold), TextDark);

            var txtRow = new Panel { BackColor = Color.Transparent, Size = new Size(500, 42), Location = new Point(0, 45) };
            var txtWrapper = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Size = new Size(340, 42),
                Location = Point.Empty,
                Cursor = Cursors.IBeam
            };
            DrawRoundedBorder(txtWrapper, InputBorder, CARD_RADIUS - 4);

            txtInvoiceNo = new TextBox
            {
                Font = new Font("Segoe UI", 11F),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(28, 32, 42),
                BorderStyle = BorderStyle.None,
                CharacterCasing = CharacterCasing.Upper,
                Size = new Size(295, 24),
                Location = new Point(12, 9)
            };
            txtInvoiceNo.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { BtnSearch_Click(null, null); e.Handled = true; }
            };
            txtInvoiceNo.Enter += (s, e) => DrawRoundedBorder(txtWrapper, InputFocus, CARD_RADIUS - 4);
            txtInvoiceNo.Leave += (s, e) => DrawRoundedBorder(txtWrapper, InputBorder, CARD_RADIUS - 4);
            txtWrapper.Controls.Add(txtInvoiceNo);

            btnSearch = new Button
            {
                Text = "Search",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(112, 42),
                Location = new Point(348, 0),
                Cursor = Cursors.Hand
            };
            btnSearch.FlatAppearance.BorderSize = 0;
            btnSearch.Region = MakeRoundedRegion(btnSearch.Size, CARD_RADIUS - 4);
            btnSearch.Click += BtnSearch_Click;
            btnSearch.MouseEnter += (s, e) => btnSearch.BackColor = Color.FromArgb(37, 99, 210);
            btnSearch.MouseLeave += (s, e) => btnSearch.BackColor = AccBlue;

            txtRow.Controls.Add(txtWrapper);
            txtRow.Controls.Add(btnSearch);
            _cardSearch.Controls.Add(txtRow);
            panelSearchBar = _cardSearch;
            panelContent.Controls.Add(_cardSearch);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 2 — INVOICE DETAILS
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardDetails;
        private Label _lblDetCustomer, _lblDetDate, _lblDetTotal, _lblDetStatus;
        // ← NEW: shows how many days have elapsed since the sale
        private Label _lblDetDaysAgo;

        private void BuildInvoiceDetailsCard()
        {
            _cardDetails = MakeCard();
            _cardDetails.Visible = false;

            var lblHead = new Label
            {
                Text = "Invoice Details",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 0)
            };

            _lblDetStatus = new Label
            {
                Text = "✓ Completed",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = BadgeGreenT,
                BackColor = BadgeGreen,
                AutoSize = false,
                Size = new Size(100, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _lblDetStatus.Region = MakeRoundedRegion(_lblDetStatus.Size, 12);

            _lblDetCustomer = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 32)
            };

            var lblDateCaption = new Label { Text = "Invoice Date", Font = new Font("Segoe UI", 8.5F), ForeColor = TextLight, BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, 64) };
            var lblTotalCaption = new Label { Text = "Original Total", Font = new Font("Segoe UI", 8.5F), ForeColor = TextLight, BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, 88) };

            _lblDetDate = new Label { Text = "", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = TextDark, BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, 64) };
            _lblDetTotal = new Label { Text = "", Font = new Font("Segoe UI", 13F, FontStyle.Bold), ForeColor = TextDark, BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, 84) };

            // ← Days-elapsed badge
            _lblDetDaysAgo = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccBlue,
                AutoSize = false,
                Size = new Size(160, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 112)
            };
            _lblDetDaysAgo.Region = MakeRoundedRegion(_lblDetDaysAgo.Size, 10);

            lblInvoiceInfo = _lblDetCustomer;

            _cardDetails.Controls.AddRange(new Control[]
            {
                lblHead, _lblDetStatus,
                _lblDetCustomer,
                lblDateCaption, lblTotalCaption,
                _lblDetDate, _lblDetTotal,
                _lblDetDaysAgo
            });

            _cardDetails.Paint += (s, e) =>
            {
                int w = _cardDetails.Width - CARD_PAD * 2;
                if (_lblDetStatus.Width > 0) _lblDetStatus.Location = new Point(w - _lblDetStatus.Width, 2);
                if (_lblDetDate.Width > 0) _lblDetDate.Location = new Point(w - _lblDetDate.Width, 64);
                if (_lblDetTotal.Width > 0) _lblDetTotal.Location = new Point(w - _lblDetTotal.Width, 84);
                if (_lblDetDaysAgo.Width > 0) _lblDetDaysAgo.Location = new Point(w - _lblDetDaysAgo.Width, 112);
            };

            panelContent.Controls.Add(_cardDetails);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 3 — ITEMS TO RETURN
        //  Columns: ✓ | Item Name | Orig Qty | Unit | Price | Disc% | Tax% | Ret Qty | Reason | Refund
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardItems;
        private void BuildItemsCard()
        {
            _cardItems = MakeCard();
            _cardItems.Visible = false;

            AddCardLabel(_cardItems, "Items to Return", 0,
                new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            var hdr = new Panel
            {
                BackColor = SummaryBg,
                Size = new Size(1, 32),
                Location = new Point(0, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            hdr.Paint += (s, e) => { hdr.Width = _cardItems.Width - CARD_PAD * 2; e.Graphics.Clear(SummaryBg); };

            // Extended column headers now include Unit, Disc %, Tax %
            string[] hdrs = { "", "Sel", "Item Name", "Qty", "Unit", "Price", "Disc%", "Tax%", "Ret Qty", "Reason" };
            int[] hdrX = { 0, 8, 60, 240, 280, 318, 376, 416, 456, 528 };

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

            panelLines = new Panel
            {
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(0, 62),
                Width = 1
            };
            _cardItems.Controls.Add(panelLines);
            _cardItems.AutoSize = true;
            panelContent.Controls.Add(_cardItems);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CARD 4 — RETURN SUMMARY  (subtotal + tax + total)
        // ══════════════════════════════════════════════════════════════════════
        private Panel _cardSummary;
        private Label _lblSummarySubtotal, _lblSummaryTax, _lblSummaryTotal;

        private void BuildSummaryCard()
        {
            _cardSummary = MakeCard();
            _cardSummary.Visible = false;

            AddCardLabel(_cardSummary, "Return Summary", 0,
                new Font("Segoe UI", 10F, FontStyle.Bold), TextDark);

            // Subtotal row
            var lblSubCap = MkSumLbl("Subtotal (after discount)", 32);
            _lblSummarySubtotal = MkSumVal("", 32);

            // Tax row
            var lblTaxCap = MkSumLbl("Tax Refund", 56);
            _lblSummaryTax = MkSumVal("", 56);

            // Divider
            var div = new Panel { BackColor = CardBorder, Size = new Size(300, 1), Location = new Point(0, 82) };

            // Total row
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

            // Refund method button
            int btnY = 148;
            btnMethodCash = MakeSummaryMethodBtn("Cash", AccGreen, new Point(0, btnY));
            btnMethodCash.Click += (s, e) => SetRefundMethod("cash");

            _cardSummary.Controls.AddRange(new Control[]
            {
                lblSubCap, _lblSummarySubtotal,
                lblTaxCap, _lblSummaryTax,
                div,
                lblTotalCap, _lblSummaryTotal,
                btnMethodCash
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

            foreach (Panel card in new[] { _cardSearch, _cardDetails, _cardItems, _cardSummary })
            {
                if (card == null) continue;
                card.Width = cardW;
                card.Location = new Point(CARD_MARGIN, y);
                if (card == _cardItems && panelLines != null)
                    panelLines.Width = cardW - CARD_PAD * 2;
                if (card.Visible) y += card.Height + CARD_MARGIN;
            }
            panelContent.Height = y;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SEARCH  — with return-period validation
        // ══════════════════════════════════════════════════════════════════════
        private void BtnSearch_Click(object sender, EventArgs e)
        {
            string inv = txtInvoiceNo.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(inv))
            { ShowStatus("Please enter an invoice number.", false); return; }

            var rows = SalesReturnRepository.LoadOriginalInvoiceLines(inv, _companyId);
            if (rows == null || rows.Count == 0)
            { ShowStatus($"Invoice '{inv}' not found or has no line items.", false); return; }

            _originalInvoiceNo = inv;
            _customerName = SalesReturnRepository.GetCustomerForInvoice(inv) ?? "Walk-in";

            DateTime? invoiceDate = SalesReturnRepository.GetInvoiceSaleDate(inv);

            // ── Return-period check ───────────────────────────────────────────
            if (invoiceDate.HasValue)
            {
                int daysSinceSale = (DateTime.Today - invoiceDate.Value.Date).Days;

                // Colour-code the days badge
                string daysText;
                Color badgeBg;
                if (daysSinceSale == 0)
                {
                    daysText = "Today's invoice";
                    badgeBg = AccGreen;
                }
                else if (daysSinceSale == 1)
                {
                    daysText = "1 day ago";
                    badgeBg = AccGreen;
                }
                else
                {
                    daysText = $"{daysSinceSale} days ago";
                    badgeBg = daysSinceSale <= MAX_RETURN_DAYS ? AccBlue : AccRed;
                }

                _lblDetDaysAgo.Text = $"🕐 {daysText}";
                _lblDetDaysAgo.BackColor = badgeBg;
                _lblDetDaysAgo.Region = MakeRoundedRegion(_lblDetDaysAgo.Size, 10);

                // Block if outside the return window
                if (daysSinceSale > MAX_RETURN_DAYS)
                {
                    _lblDetCustomer.Text = _customerName;
                    _lblDetDate.Text = invoiceDate.Value.ToString("dd MMM yyyy");
                    _lblDetTotal.Text = "";
                    _cardDetails.Visible = true;
                    RelayoutCards();

                    ShowStatus(
                        $"Return not allowed. Invoice {inv} is {daysSinceSale} days old " +
                        $"(max {MAX_RETURN_DAYS} days).", false);

                    MessageBox.Show(
                        $"This invoice cannot be returned.\n\n" +
                        $"Invoice Date : {invoiceDate.Value:dd MMM yyyy}\n" +
                        $"Days Elapsed : {daysSinceSale} day(s)\n" +
                        $"Return Window: {MAX_RETURN_DAYS} days\n\n" +
                        "The return period has expired.",
                        "Return Period Expired",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    _cardItems.Visible = false;
                    _cardSummary.Visible = false;
                    btnProcessReturn.Enabled = false;
                    RelayoutCards();
                    return;
                }
            }
            else
            {
                // No date found — show neutral badge
                _lblDetDaysAgo.Text = "Invoice date unknown";
                _lblDetDaysAgo.BackColor = TextLight;
                _lblDetDaysAgo.Region = MakeRoundedRegion(_lblDetDaysAgo.Size, 10);
            }

            _lblDetCustomer.Text = _customerName;
            _lblDetDate.Text = invoiceDate.HasValue ? invoiceDate.Value.ToString("dd MMM yyyy") : "N/A";

            decimal origTotal = rows.Sum(r =>
                Math.Round(r.UnitPrice * r.Qty * (1m - r.DiscountPct / 100m), 2));
            _lblDetTotal.Text = Fmt(origTotal);
            _cardDetails.Visible = true;

            // Clear previous line rows
            foreach (Control c in panelLines.Controls) c.Dispose();
            panelLines.Controls.Clear();
            panelLines.Height = 0;

            // Subtract already-returned quantities
            var alreadyReturned = SalesReturnRepository.GetReturnedQtys(_originalInvoiceNo);

            _returnLines = new List<ReturnLineItem>();
            foreach (var r in rows)
            {
                int previouslyReturned = 0;
                foreach (var kv in alreadyReturned)
                {
                    if (string.Equals(kv.Key.Trim(), r.ItemName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(r.Barcode) &&
                         string.Equals(kv.Key.Trim(), r.Barcode.Trim(), StringComparison.OrdinalIgnoreCase)))
                    { previouslyReturned = kv.Value; break; }
                }

                int remainingQty = r.Qty - previouslyReturned;
                if (remainingQty <= 0) continue;

                _returnLines.Add(new ReturnLineItem
                {
                    Name = r.ItemName,
                    UnitPrice = r.UnitPrice,
                    DiscountPct = r.DiscountPct,
                    // ← TaxPct and UOM come from the original invoice row
                    TaxPct = r.TaxPct,   // add TaxPct to your repository DTO
                    UOM = string.IsNullOrWhiteSpace(r.UOM) ? "EA" : r.UOM,
                    OriginalQty = remainingQty,
                    ReturnQty = remainingQty,
                    Barcode = r.Barcode,
                    Selected = true,
                    Reason = "Defective"
                });
            }

            if (_returnLines.Count == 0)
            { ShowStatus($"Invoice {inv} has already been fully returned.", false); return; }

            RebuildLineRows();
            RecalcTotal();
            _cardItems.Visible = true;
            _cardSummary.Visible = true;
            btnProcessReturn.Enabled = true;

            RelayoutCards();
            ShowStatus($"Loaded {_returnLines.Count} returnable line(s) for {inv}. Adjust quantities then process.", true);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  REBUILD LINE ROWS
        // ══════════════════════════════════════════════════════════════════════
        private void RebuildLineRows()
        {
            foreach (Control c in panelLines.Controls) c.Dispose();
            panelLines.Controls.Clear();
            panelLines.Height = 0;

            int y = 0;
            for (int i = 0; i < _returnLines.Count; i++)
            {
                var row = BuildLineRow(_returnLines[i], y, i % 2 == 0);
                panelLines.Controls.Add(row);
                y += 52;
            }
            panelLines.Height = Math.Max(1, y);
            panelLines.Invalidate(true);
            _cardItems.Refresh();
            RelayoutCards();
        }

        private static readonly string[] ReasonOptions =
            { "Defective", "Wrong Size", "Wrong Item", "Not as Described", "Changed Mind", "Other" };

        // ── Build one line row  ────────────────────────────────────────────────
        // Columns: [chk] [name] [origQty] [UOM] [price] [disc%] [tax%] [retQty▲▼] [reason] [refund]
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

            // ── Checkbox ──────────────────────────────────────────────────────
            var chk = new CheckBox
            {
                Checked = line.Selected,
                Size = new Size(18, 18),
                Location = new Point(8, (ROW_H - 18) / 2),
                BackColor = Color.Transparent
            };
            chk.CheckedChanged += (s, e) => { line.Selected = chk.Checked; RecalcTotal(); };

            // ── Item name ─────────────────────────────────────────────────────
            row.Controls.Add(new Label
            {
                Text = line.Name,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.Transparent,
                Size = new Size(180, ROW_H),
                Location = new Point(30, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // ── Original Qty ──────────────────────────────────────────────────
            row.Controls.Add(new Label
            {
                Text = line.OriginalQty.ToString(),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(36, ROW_H),
                Location = new Point(216, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── UOM ───────────────────────────────────────────────────────────
            row.Controls.Add(new Label
            {
                Text = line.UOM,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccCyan,
                BackColor = Color.Transparent,
                Size = new Size(36, ROW_H),
                Location = new Point(256, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Unit Price ────────────────────────────────────────────────────
            row.Controls.Add(new Label
            {
                Text = Fmt(line.UnitPrice),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.Transparent,
                Size = new Size(80, ROW_H),
                Location = new Point(296, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // ── Discount % ────────────────────────────────────────────────────
            row.Controls.Add(new Label
            {
                Text = line.DiscountPct > 0 ? $"{line.DiscountPct:F1}%" : "—",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                Size = new Size(42, ROW_H),
                Location = new Point(378, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Tax % ─────────────────────────────────────────────────────────
            row.Controls.Add(new Label
            {
                Text = line.TaxPct > 0 ? $"{line.TaxPct:F1}%" : "—",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = AccBlue,
                BackColor = Color.Transparent,
                Size = new Size(42, ROW_H),
                Location = new Point(422, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Return Qty spinner ────────────────────────────────────────────
            var spWrapper = new Panel
            {
                BackColor = Color.FromArgb(42, 46, 58),
                Size = new Size(72, 30),
                Location = new Point(466, (ROW_H - 30) / 2)
            };
            spWrapper.Region = MakeRoundedRegion(spWrapper.Size, 6);

            var tbQty = new TextBox
            {
                Text = line.ReturnQty.ToString(),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextDark,
                BackColor = Color.FromArgb(42, 46, 58),
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center,
                Size = new Size(46, 24),
                Location = new Point(2, 4),
                MaxLength = 4
            };

            void ApplyQtyInput()
            {
                if (int.TryParse(tbQty.Text.Trim(), out int entered))
                {
                    int clamped = Math.Max(0, Math.Min(entered, line.OriginalQty));
                    if (entered > line.OriginalQty)
                    {
                        tbQty.BackColor = Color.FromArgb(80, 40, 40);
                        var t = new System.Windows.Forms.Timer { Interval = 600 };
                        t.Tick += (s, ev) => { tbQty.BackColor = Color.FromArgb(42, 46, 58); t.Stop(); };
                        t.Start();
                    }
                    line.ReturnQty = clamped;
                    tbQty.Text = clamped.ToString();
                    line.Selected = clamped > 0;
                    chk.Checked = line.Selected;
                }
                else tbQty.Text = line.ReturnQty.ToString();

                RecalcTotal();
                UpdateRowRefund(row, line);
            }

            tbQty.Leave += (s, e) => ApplyQtyInput();
            tbQty.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { ApplyQtyInput(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            tbQty.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };

            var btnUp = new Button { Text = "▲", Font = new Font("Segoe UI", 6F), BackColor = Color.Transparent, ForeColor = TextMid, FlatStyle = FlatStyle.Flat, Size = new Size(22, 14), Location = new Point(48, 1), Cursor = Cursors.Hand };
            btnUp.FlatAppearance.BorderSize = 0;
            var btnDn = new Button { Text = "▼", Font = new Font("Segoe UI", 6F), BackColor = Color.Transparent, ForeColor = TextMid, FlatStyle = FlatStyle.Flat, Size = new Size(22, 14), Location = new Point(48, 15), Cursor = Cursors.Hand };
            btnDn.FlatAppearance.BorderSize = 0;

            btnUp.Click += (s, e) =>
            {
                if (line.ReturnQty < line.OriginalQty)
                { line.ReturnQty++; tbQty.Text = line.ReturnQty.ToString(); line.Selected = true; chk.Checked = true; RecalcTotal(); UpdateRowRefund(row, line); }
            };
            btnDn.Click += (s, e) =>
            {
                if (line.ReturnQty > 0)
                { line.ReturnQty--; tbQty.Text = line.ReturnQty.ToString(); line.Selected = line.ReturnQty > 0; chk.Checked = line.Selected; RecalcTotal(); UpdateRowRefund(row, line); }
            };

            spWrapper.Controls.AddRange(new Control[] { tbQty, btnUp, btnDn });

            // ── Reason dropdown ───────────────────────────────────────────────
            var cmbReason = new ComboBox
            {
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMid,
                BackColor = Color.FromArgb(42, 46, 58),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(100, 28),
                Location = new Point(542, (ROW_H - 28) / 2)
            };
            cmbReason.Items.AddRange(ReasonOptions);
            cmbReason.SelectedItem = line.Reason ?? "Defective";
            cmbReason.SelectedIndexChanged += (s, e) =>
            {
                if (cmbReason.SelectedItem != null) line.Reason = cmbReason.SelectedItem.ToString();
            };

            // ── Refund amount label (includes tax) ────────────────────────────
            var lblRef = new Label
            {
                Name = "lblRefund",
                Text = Fmt(line.LineRefund),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                Size = new Size(100, ROW_H),
                Location = new Point(rowW - 108, 0),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            row.Controls.AddRange(new Control[] { chk, spWrapper, cmbReason, lblRef });
            return row;
        }

        private void UpdateRowRefund(Panel row, ReturnLineItem line)
        {
            var lbl = row.Controls.OfType<Label>().FirstOrDefault(l => l.Name == "lblRefund");
            if (lbl != null) lbl.Text = Fmt(line.LineRefund);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  RECALC TOTAL  — subtotal + tax + total
        // ══════════════════════════════════════════════════════════════════════
        private void RecalcTotal()
        {
            var active = _returnLines.Where(l => l.Selected && l.ReturnQty > 0).ToList();

            decimal subtotal = active.Sum(l => l.LineSubtotal);
            decimal tax = active.Sum(l => l.LineTax);
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
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PROCESS RETURN
        // ══════════════════════════════════════════════════════════════════════
        private void BtnProcessReturn_Click(object sender, EventArgs e)
        {
            var activeLines = _returnLines.Where(l => l.Selected && l.ReturnQty > 0).ToList();
            if (!activeLines.Any())
            { ShowStatus("Select at least one line item to return.", false); return; }

            decimal subtotal = activeLines.Sum(l => l.LineSubtotal);
            decimal taxRefund = activeLines.Sum(l => l.LineTax);
            decimal totalRefund = subtotal + taxRefund;

            if (totalRefund <= 0)
            { ShowStatus("Return amount is zero.", false); return; }

            var confirm = MessageBox.Show(
                $"Process return for {activeLines.Count} item(s)?\n\n" +
                $"Subtotal :  {Fmt(subtotal)}\n" +
                $"Tax Refund: {Fmt(taxRefund)}\n" +
                $"Total   :   {Fmt(totalRefund)}\n" +
                $"Method  :   {RefundMethodLabel()}\n\n" +
                "This action cannot be undone.",
                "Confirm Return", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            try
            {
                string returnInvNo = SalesReturnRepository.NextReturnInvoiceNo();
                var returnRecord = new SalesReturnRecord
                {
                    ReturnInvoiceNo = returnInvNo,
                    OriginalInvoiceNo = _originalInvoiceNo,
                    CustomerName = _customerName,
                    RefundMethod = _refundMethod,
                    RefundTotal = totalRefund,
                    ReturnDate = DateTime.Now,
                    CompanyId = _companyId,
                    CashierName = "ADMIN",
                    Lines = activeLines.Select(l => new SalesReturnLine
                    {
                        ItemName = l.Name,
                        UnitPrice = l.UnitPrice,
                        DiscountPct = l.DiscountPct,
                        TaxPct = l.TaxPct,
                        UOM = l.UOM,
                        ReturnQty = l.ReturnQty,
                        RefundAmt = l.LineRefund,
                        Barcode = l.Barcode
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
                OriginalInvoiceNo = r.OriginalInvoiceNo,
                CustomerName = r.CustomerName,
                ReturnDate = r.ReturnDate,
                CashierName = r.CashierName,
                RefundMethod = RefundMethodLabel(r.RefundMethod),
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
        //  Columns: # | Description | Ret Qty | Unit | Unit Price | Disc% | Tax% | Tax Amt | Refund Amt
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

            // ── Fonts ──────────────────────────────────────────────────────────
            var fBigBold = new Font("Arial", 13f * sc, FontStyle.Bold);
            var fBold = new Font("Arial", 9f * sc, FontStyle.Bold);
            var fNorm = new Font("Arial", 8.5f * sc);
            var fSmall = new Font("Arial", 7.8f * sc);
            var fTiny = new Font("Arial", 6.8f * sc);
            var fUnderBold = new Font("Arial", 8f * sc, FontStyle.Bold | FontStyle.Underline);

            // ── Pens & brushes ─────────────────────────────────────────────────
            var penThk = new Pen(Color.Black, 1.4f * sc);
            var penBlk = new Pen(Color.Black, 0.7f * sc);
            var bkBlack = Brushes.Black;
            var bkLight = new SolidBrush(Color.FromArgb(220, 220, 220));
            var bkTcBg = new SolidBrush(Color.FromArgb(255, 245, 245));
            var bkReturnBanner = new SolidBrush(Color.FromArgb(254, 226, 226));

            // ── String formats ─────────────────────────────────────────────────
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

            // ══════════════════════════════════════════════════════════════════
            //  [1] HEADER
            // ══════════════════════════════════════════════════════════════════
            float logoW = fullW * 0.34f;
            float compX = left + logoW + 5f * sc;
            float compW = fullW * 0.33f;
            float officeX = compX + compW + 3f * sc;
            float officeW = left + fullW - officeX;

            string companyName = d.CompanyName ?? "ABC";
            string companyAddress = d.CompanyAddress ?? "";
            string companyPhone = d.CompanyPhone ?? "";
            string companyVat = d.CompanyVat ?? "";
            string companyWebsite = d.CompanyWebsite ?? "";
            string officeInfo = d.SalesOfficeInfo ?? "";

            var officeBlocks = string.IsNullOrWhiteSpace(officeInfo)
                ? Array.Empty<string>()
                : officeInfo.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);

            // Measure office box height
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

            // Measure company box height
            float cInnerW = compW - 14f * sc;
            float compH_content = 8f * sc;
            compH_content += fBold.GetHeight(g) + 5f * sc;
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

            // Logo
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

            // Company box
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
                if (!string.IsNullOrWhiteSpace(companyPhone))
                { g.DrawString("Phone: " + companyPhone, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt); cy += fSmall.GetHeight(g) + 4f * sc; }
                if (!string.IsNullOrWhiteSpace(companyVat))
                { g.DrawString("Vat : " + companyVat, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt); cy += fSmall.GetHeight(g) + 4f * sc; }
                if (!string.IsNullOrWhiteSpace(companyWebsite))
                    g.DrawString("Website : " + companyWebsite, fSmall, bkBlack, new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt);
            }

            // Offices box
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

            // ══════════════════════════════════════════════════════════════════
            //  [2] "SALES RETURN" BANNER + meta box
            // ══════════════════════════════════════════════════════════════════
            float bannerW = fullW * 0.52f;
            float metaX = left + bannerW + 6f * sc;
            float metaW = left + fullW - metaX;

            float metaLineH = fSmall.GetHeight(g) + 5f * sc;
            float metaBoxH = 5f * metaLineH + 14f * sc;
            float returnHdrH = Math.Max(80f * sc, metaBoxH);

            g.FillRectangle(bkReturnBanner, left, y, bannerW, returnHdrH);
            g.DrawRectangle(penBlk, left, y, bannerW, returnHdrH);
            g.DrawString("SALES RETURN", fBigBold, bkBlack,
                new RectangleF(left + 12f * sc, y + (returnHdrH - fBigBold.GetHeight(g)) / 2f,
                               bannerW - 16f * sc, fBigBold.GetHeight(g) + 4f), lFmt);

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
                MetaRow("Original Inv   :", d.OriginalInvoiceNo ?? "");
                MetaRow("Date / Time    :", d.ReturnDate.ToString("dd/MM/yyyy HH:mm"));
                MetaRow("Cashier        :", d.CashierName ?? "");
                MetaRow("Refund Method  :", d.RefundMethod ?? "Cash");
            }
            y += returnHdrH + 10f * sc;

            // ══════════════════════════════════════════════════════════════════
            //  [3] CUSTOMER ROW
            // ══════════════════════════════════════════════════════════════════
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

            // ══════════════════════════════════════════════════════════════════
            //  [4] ITEMS TABLE
            //  # | Description | Ret Qty | Unit | Unit Price | Disc% | Tax% | Tax Amt | Refund Amt
            // ══════════════════════════════════════════════════════════════════
            float[] iPcts = { 0.04f, 0.26f, 0.07f, 0.06f, 0.12f, 0.07f, 0.07f, 0.12f, 0.19f };
            string[] iHdrs = { "#", "Description", "Ret Qty", "Unit", "Unit Price", "Disc%", "Tax%", "Tax Amt", "Refund Amt" };
            bool[] iRight = { false, false, true, false, true, true, true, true, true };
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
                    new SizeF(iWidths[1] - 8f * sc, 999f), lTopFmt).Height;
                float rowH = Math.Max(minRowH, descH + 10f * sc);

                g.DrawRectangle(penBlk, left, y, fullW, rowH);

                string uom = string.IsNullOrWhiteSpace(li.UOM) ? "EA" : li.UOM;
                string disc = li.DiscountPct > 0 ? li.DiscountPct.ToString("F2") : "0.00";
                string taxP = li.TaxPct > 0 ? li.TaxPct.ToString("F2") : "0.00";

                string[] iVals =
                {
                    rowNum++.ToString(),
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
                    var sf = i == 1 ? lTopFmt
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

            // ══════════════════════════════════════════════════════════════════
            //  [5] FOOTER — totals block (right) + sig lines + T&C (left)
            // ══════════════════════════════════════════════════════════════════
            float sigW = fullW * 0.58f;
            float totW = fullW * 0.38f;
            float totX = left + fullW - totW;

            float tRowH = fNorm.GetHeight(g) + 9f * sc;
            float totalsH = tRowH * 4f + fBold.GetHeight(g) + 9f * sc + 4f * sc; // 4 rows now

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

            // ── Totals block ──────────────────────────────────────────────────
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

            // ── Signature lines ───────────────────────────────────────────────
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

            // ── T&C bordered box ──────────────────────────────────────────────
            g.DrawRectangle(penBlk, left, sy, sigW, tcH);
            g.FillRectangle(bkTcBg, left + 1, sy + 1, sigW - 2, tcH - 2);
            g.DrawString(tc, fTiny, bkBlack,
                new RectangleF(left + 7f * sc, sy + 7f * sc, tcInnerW, tcH - 10f * sc), wrapFmt);

            // ── Dispose ───────────────────────────────────────────────────────
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
            _returnLines.Clear();
            _originalInvoiceNo = "";
            _customerName = "Walk-in";
            txtInvoiceNo.Text = "";
            if (_lblDetCustomer != null) _lblDetCustomer.Text = "";
            if (_lblDetDate != null) _lblDetDate.Text = "";
            if (_lblDetTotal != null) _lblDetTotal.Text = "";
            if (_lblDetDaysAgo != null) _lblDetDaysAgo.Text = "";
            if (_lblSummarySubtotal != null) _lblSummarySubtotal.Text = "";
            if (_lblSummaryTax != null) _lblSummaryTax.Text = "";
            if (_lblSummaryTotal != null) _lblSummaryTotal.Text = "";
            _cardDetails.Visible = false;
            _cardItems.Visible = false;
            _cardSummary.Visible = false;
            RebuildLineRows();
            RecalcTotal();
            btnProcessReturn.Enabled = false;
            ShowStatus("Enter an invoice number to begin a return.", false);
            RelayoutCards();
            txtInvoiceNo.Focus();
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

        private static string Truncate(string s, int max) =>
            s?.Length > max ? s.Substring(0, max - 1) + "…" : s ?? "";

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