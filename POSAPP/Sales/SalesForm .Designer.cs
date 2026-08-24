using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace POSAPP
{
    partial class SalesForm
    {
        private System.ComponentModel.IContainer components = null;

        // Header / chrome
        private Panel panelHeader;
        private Label lblPOSTitle, lblOperator, lblInvoiceNo, lblTime, lblDate;
        private Button btnMin, btnMax, btnClose;

        // Header search controls (moved from left column)
        private Label lblSearchHeader, lblBarcodeHeader;
        private Label lblSearchSep;// lblBarcodeSep;

        // Root layout
        private TableLayoutPanel tblRoot;

        // LEFT COLUMN
        private Panel panelLeft;
        private Panel panelSearchCard;
        private Label lblBarcodeTitle, lblCustomerTitle;
        private TextBox txtSearch, txtCustomer;//txtBarcode
        private ListBox listSearchResults;
        private Label lblBarcodeHint, lblStatus;
        private Panel panelDiscountCard;
        private Label lblDiscountTitle;
        private NumericUpDown nudDiscount;
        private Panel panelHotCard;
        private Label lblHotTitle;
        private Panel panelHotItems;
        private Panel panelRecentCard;
        private Label lblRecentTitle;
        private Panel panelRecentSales;
        private Label lblGrandTotalBig;

        // CENTRE COLUMN
        private Panel panelCentre;
        private Panel panelCentreHeader;
        private Label lblCartTitle, lblItemCount;
      
        private Panel panelCartItems;

        // RIGHT COLUMN
        private Panel panelRight;
        private Panel panelTotalsCard;
        private Label lblSubtotalHint, lblSubtotalVal;
        private Label lblDiscountHint, lblDiscountVal;
        private Label lblTaxHint, lblTaxVal;
        private Panel panelTotalDivider;
        private Label lblGrandHint, lblGrandTotal;
        private Label lblStockReduction;
        private Panel panelPayCard;
        private Label lblPayTitle;
        private Panel panelSplitCash, panelSplitUpi, panelSplitCard;
        private Label lblCashTitle, lblUpiTitle, lblCardTitle;
        private TextBox txtSplitCash, txtSplitUpi, txtSplitCard;
        private Label lblSplitBalance;
        private Button btnSplitExact;
        private Panel panelNumpad;
        private Panel panelFooterBar;
        private Button btnTenderSale, btnCancelSale;
        private Label lblShortcuts;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            // ── Instantiate ────────────────────────────────────────────────────
            panelHeader = new Panel();
            lblPOSTitle = new Label(); lblOperator = new Label();
            lblInvoiceNo = new Label(); lblTime = new Label(); lblDate = new Label();
            btnMin = new Button(); btnMax = new Button(); btnClose = new Button();

            txtSearch = new TextBox();
            //txtBarcode = new TextBox();
            lblSearchHeader = new Label();
            lblBarcodeHeader = new Label();
            lblSearchSep = new Label();
            //lblBarcodeSep = new Label();

            tblRoot = new TableLayoutPanel();

            panelLeft = new Panel();
            panelSearchCard = new Panel();
            lblBarcodeTitle = new Label();
            lblCustomerTitle = new Label();
            lblBarcodeHint = new Label();

            txtCustomer = new TextBox();
            listSearchResults = new ListBox();
            lblStatus = new Label();

            panelDiscountCard = new Panel();
            lblDiscountTitle = new Label();
            nudDiscount = new NumericUpDown();

            panelHotCard = new Panel(); lblHotTitle = new Label(); panelHotItems = new Panel();
            panelRecentCard = new Panel(); lblRecentTitle = new Label(); panelRecentSales = new Panel();

            panelCentre = new Panel();
            panelCentreHeader = new Panel();
            lblCartTitle = new Label();
            lblGrandTotalBig = new Label(); lblItemCount = new Label();
            panelCartItems = new Panel();

            panelRight = new Panel();
            panelTotalsCard = new Panel();
            lblSubtotalHint = new Label(); lblSubtotalVal = new Label();
            lblDiscountHint = new Label(); lblDiscountVal = new Label();
            lblTaxHint = new Label(); lblTaxVal = new Label();
            panelTotalDivider = new Panel();
            lblGrandHint = new Label(); lblGrandTotal = new Label();
            lblStockReduction = new Label();
            panelPayCard = new Panel();
            lblPayTitle = new Label();
            panelSplitCash = new Panel(); panelSplitUpi = new Panel(); panelSplitCard = new Panel();
            lblCashTitle = new Label(); lblUpiTitle = new Label(); lblCardTitle = new Label();
            txtSplitCash = new TextBox(); txtSplitUpi = new TextBox(); txtSplitCard = new TextBox();
            lblSplitBalance = new Label();
            //btnQuick50 = new Button(); btnQuick100 = new Button();
            //btnQuick200 = new Button(); btnQuick500 = new Button();
            btnSplitExact = new Button();
            panelNumpad = new Panel();
            panelFooterBar = new Panel();
            btnTenderSale = new Button(); btnCancelSale = new Button();
            lblShortcuts = new Label();

            this.SuspendLayout();

            // ══════════════════════════════════════════════════════════════════
            //  FORM
            // ══════════════════════════════════════════════════════════════════
            this.Text = "POS — New Sale";
            this.ClientSize = new Size(1440, 820);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(22, 24, 30);
            this.Load += new EventHandler(SalesForm_Load);
            this.KeyPreview = true;

            // ══════════════════════════════════════════════════════════════════
            //  HEADER  44 px
            // ══════════════════════════════════════════════════════════════════
            panelHeader.Dock = DockStyle.Top;
            panelHeader.Height = 44;
            panelHeader.BackColor = Color.FromArgb(32, 35, 44);
            panelHeader.Paint += new PaintEventHandler(panelHeader_Paint);
            panelHeader.MouseDown += new MouseEventHandler(panelHeader_MouseDown);
            panelHeader.MouseMove += new MouseEventHandler(panelHeader_MouseMove);
            panelHeader.MouseUp += new MouseEventHandler(panelHeader_MouseUp);
            panelHeader.DoubleClick += new EventHandler(panelHeader_DoubleClick);
            panelHeader.Resize += new EventHandler(panelHeader_Resize);

            //lblPOSTitle.Text = "ShriPOS";
            lblPOSTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblPOSTitle.ForeColor = Color.FromArgb(240, 242, 246);
            lblPOSTitle.BackColor = Color.Transparent;
            lblPOSTitle.AutoSize = true;
            lblPOSTitle.Location = new Point(14, 12);

            lblOperator.Text = "";
            lblOperator.Font = new Font("Segoe UI", 8.5F);
            lblOperator.ForeColor = Color.FromArgb(130, 140, 158);
            lblOperator.BackColor = Color.Transparent;
            lblOperator.AutoSize = true;
            lblOperator.Location = new Point(110, 14);

            lblInvoiceNo.Text = "";
            lblInvoiceNo.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblInvoiceNo.ForeColor = Color.FromArgb(52, 211, 153);
            lblInvoiceNo.BackColor = Color.Transparent;
            lblInvoiceNo.AutoSize = true;
            lblInvoiceNo.Location = new Point(240, 14);

            lblSearchSep.Text = "|";
            lblSearchSep.Font = new Font("Segoe UI", 13F);
            lblSearchSep.ForeColor = Color.FromArgb(55, 60, 75);
            lblSearchSep.BackColor = Color.Transparent;
            lblSearchSep.AutoSize = true;
            lblSearchSep.Location = new Point(350, 10);

            lblSearchHeader.Text = "🔍";
            lblSearchHeader.Font = new Font("Segoe UI Emoji", 9F);
            lblSearchHeader.BackColor = Color.Transparent;
            lblSearchHeader.AutoSize = true;
            lblSearchHeader.Location = new Point(366, 12);
            lblSearchHeader.Cursor = Cursors.IBeam;
            lblSearchHeader.Click += (s, e) => txtSearch.Focus();

            txtSearch.Font = new Font("Segoe UI", 9F);
            txtSearch.ForeColor = Color.FromArgb(240, 242, 246);
            txtSearch.BackColor = Color.FromArgb(44, 48, 60);
            txtSearch.BorderStyle = BorderStyle.FixedSingle;
            txtSearch.Size = new Size(550, 24);
            txtSearch.Location = new Point(586, 10);

            //lblBarcodeSep.Text = "|";
            //lblBarcodeSep.Font = new Font("Segoe UI", 13F);
            //lblBarcodeSep.ForeColor = Color.FromArgb(55, 60, 75);
            //lblBarcodeSep.BackColor = Color.Transparent;
            //lblBarcodeSep.AutoSize = true;
            //lblBarcodeSep.Location = new Point(582, 10);

            //lblBarcodeHeader.Text = "▦";
            //lblBarcodeHeader.Font = new Font("Segoe UI", 10F);
            //lblBarcodeHeader.ForeColor = Color.FromArgb(52, 211, 153);
            //lblBarcodeHeader.BackColor = Color.Transparent;
            //lblBarcodeHeader.AutoSize = true;
            //lblBarcodeHeader.Location = new Point(598, 12);
            //lblBarcodeHeader.Cursor = Cursors.IBeam;
            //lblBarcodeHeader.Click += (s, e) => txtBarcode.Focus();

            //txtBarcode.Font = new Font("Consolas", 9F);
            //txtBarcode.ForeColor = Color.FromArgb(52, 211, 153);
            //txtBarcode.BackColor = Color.FromArgb(44, 48, 60);
            //txtBarcode.BorderStyle = BorderStyle.None;
            //txtBarcode.Size = new Size(160, 24);
            //txtBarcode.Location = new Point(1216, 10); 

            Button MakeHdrBtn(string t, bool bold = false)
            {
                var b = new Button
                {
                    Text = t,
                    Font = new Font("Segoe UI", 10F, bold ? FontStyle.Bold : FontStyle.Regular),
                    Size = new Size(46, 44),
                    Anchor = AnchorStyles.None,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(32, 35, 44),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                return b;
            }

            btnClose = MakeHdrBtn("✕", true);
           // btnClose.Location = new Point(1394, 0);
            btnClose.Click += new EventHandler(btnClose_Click);
            btnClose.MouseEnter += new EventHandler(btnClose_MouseEnter);
            btnClose.MouseLeave += new EventHandler(btnClose_MouseLeave);

            btnMax = MakeHdrBtn("□");
           // btnMax.Location = new Point(1348, 0);
            btnMax.Click += new EventHandler(btnMax_Click);
            btnMax.MouseEnter += new EventHandler(btnMax_MouseEnter);
            btnMax.MouseLeave += new EventHandler(btnMax_MouseLeave);

            btnMin = MakeHdrBtn("─");
           // btnMin.Location = new Point(1302, 0);
            btnMin.Click += new EventHandler(btnMin_Click);
            btnMin.MouseEnter += new EventHandler(btnMin_MouseEnter);
            btnMin.MouseLeave += new EventHandler(btnMin_MouseLeave);

            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Location = new Point(panelHeader.Width - 46, 0);  // will be corrected by RepositionTitleButtons

            btnMax.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMax.Location = new Point(panelHeader.Width - 92, 0);

            btnMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMin.Location = new Point(panelHeader.Width - 138, 0);

            panelHeader.Controls.AddRange(new Control[]
            {
                lblPOSTitle, lblOperator, lblInvoiceNo,
                lblSearchSep, lblSearchHeader, txtSearch
                , lblBarcodeHeader, 
                lblDate, lblTime,
                btnClose, btnMax, btnMin//txtBarcodelblBarcodeSep
            });

            // ══════════════════════════════════════════════════════════════════
            //  FOOTER  58 px
            // ══════════════════════════════════════════════════════════════════
            panelFooterBar.Dock = DockStyle.Bottom;
            panelFooterBar.Height = 58;
            panelFooterBar.BackColor = Color.FromArgb(32, 35, 44);
            panelFooterBar.Paint += new PaintEventHandler(panelFooterBar_Paint);

            btnTenderSale.Text = "💾  Save  (F1)";
            btnTenderSale.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnTenderSale.Size = new Size(260, 42);
            btnTenderSale.Location = new Point(14, 8);
            btnTenderSale.BackColor = Color.FromArgb(34, 197, 94);
            btnTenderSale.ForeColor = Color.White;
            btnTenderSale.FlatStyle = FlatStyle.Flat;
            btnTenderSale.FlatAppearance.BorderSize = 0;
            btnTenderSale.Cursor = Cursors.Hand;
            btnTenderSale.Click += new EventHandler(btnTenderSale_Click);

            btnCancelSale.Text = "CANCEL SALE";
            btnCancelSale.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnCancelSale.Size = new Size(140, 42);
            btnCancelSale.Location = new Point(284, 8);
            btnCancelSale.BackColor = Color.FromArgb(239, 68, 68);
            btnCancelSale.ForeColor = Color.White;
            btnCancelSale.FlatStyle = FlatStyle.Flat;
            btnCancelSale.FlatAppearance.BorderSize = 0;
            btnCancelSale.Cursor = Cursors.Hand;
            btnCancelSale.Click += new EventHandler(btnCancelSale_Click);

            lblShortcuts.Text = " ";
            lblShortcuts.Font = new Font("Segoe UI", 8F);
            lblShortcuts.ForeColor = Color.FromArgb(100, 110, 130);
            lblShortcuts.BackColor = Color.Transparent;
            lblShortcuts.AutoSize = false;
            lblShortcuts.Size = new Size(700, 20);
            lblShortcuts.Location = new Point(450, 20);

            panelFooterBar.Controls.AddRange(new Control[] { btnTenderSale, btnCancelSale, lblShortcuts });

            // ══════════════════════════════════════════════════════════════════
            //  ROOT TABLE  22% | 48% | 30%
            // ══════════════════════════════════════════════════════════════════
            tblRoot.Dock = DockStyle.Fill;
            tblRoot.BackColor = Color.Transparent;
            tblRoot.ColumnCount = 3;
            tblRoot.RowCount = 1;
            tblRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            tblRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            tblRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            tblRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tblRoot.Padding = new Padding(8, 8, 8, 8);

            // ══════════════════════════════════════════════════════════════════
            //  LEFT COLUMN
            // ══════════════════════════════════════════════════════════════════
            panelLeft.Dock = DockStyle.Fill;
            panelLeft.BackColor = Color.Transparent;
            panelLeft.Padding = new Padding(0, 0, 6, 0);

            panelSearchCard.Dock = DockStyle.Top;
            panelSearchCard.Height = 36;
            panelSearchCard.BackColor = Color.FromArgb(32, 35, 44);
            panelSearchCard.Paint += new PaintEventHandler(PaintDarkCard);
            panelSearchCard.Padding = new Padding(10, 6, 10, 4);

            lblStatus.Text = "";
            lblStatus.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
            lblStatus.BackColor = Color.Transparent;
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(290, 20);
            lblStatus.Location = new Point(10, 8);

            panelSearchCard.Controls.Add(lblStatus);

            listSearchResults.BackColor = Color.FromArgb(30, 34, 46);
            listSearchResults.ForeColor = Color.FromArgb(220, 228, 245);
            listSearchResults.BorderStyle = BorderStyle.None;
            listSearchResults.Font = new Font("Segoe UI", 10F);
            listSearchResults.ItemHeight = 32;
            listSearchResults.IntegralHeight = false;
            listSearchResults.Size = new Size(190, 0);
            listSearchResults.Visible = false;
            listSearchResults.DrawMode = DrawMode.OwnerDrawFixed;
            listSearchResults.DrawItem += listSearchResults_DrawItem;
            listSearchResults.KeyDown += new KeyEventHandler(listSearchResults_KeyDown);
            listSearchResults.DoubleClick += new EventHandler(listSearchResults_DoubleClick);
            listSearchResults.Click += new EventHandler(listSearchResults_Click);



            panelDiscountCard.Dock = DockStyle.Top;
            panelDiscountCard.Height = 52;   // shorter — just the one button
            panelDiscountCard.BackColor = Color.FromArgb(32, 35, 44);
            panelDiscountCard.Paint += new PaintEventHandler(PaintDarkCard);
            panelDiscountCard.Margin = new Padding(0, 6, 0, 0);

            // Keep txtCustomer in DOM (hidden) so existing code that reads it still compiles
            txtCustomer.Visible = false;
            txtCustomer.Size = new Size(1, 1);
            txtCustomer.Location = new Point(-100, -100);

            // Keep nudDiscount in DOM (hidden) so nudDiscount_ValueChanged still compiles
            ((System.ComponentModel.ISupportInitialize)nudDiscount).BeginInit();
            nudDiscount.Minimum = 0;
            nudDiscount.Maximum = 100;
            nudDiscount.DecimalPlaces = 1;
            nudDiscount.Increment = 0.5m;
            nudDiscount.Value = 0;
            nudDiscount.Visible = false;
            nudDiscount.Size = new Size(1, 1);
            nudDiscount.Location = new Point(-100, -100);
            nudDiscount.ValueChanged += new EventHandler(nudDiscount_ValueChanged);
            ((System.ComponentModel.ISupportInitialize)nudDiscount).EndInit();

            panelDiscountCard.Controls.AddRange(new Control[] { txtCustomer, nudDiscount });
            // The actual Customer Details button is added dynamically in BuildCustomerDetailsButton()

            panelHotCard.Dock = DockStyle.Top;
            panelHotCard.Height = 340;
            panelHotCard.BackColor = Color.FromArgb(32, 35, 44);
            panelHotCard.Paint += new PaintEventHandler(PaintDarkCard);
            panelHotCard.Margin = new Padding(0, 6, 0, 0);

            lblHotTitle.Text = "QUICK ADD — POPULAR ITEMS";
            lblHotTitle.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            lblHotTitle.ForeColor = Color.DarkSalmon;
            lblHotTitle.BackColor = Color.Transparent;
            lblHotTitle.AutoSize = true;
            lblHotTitle.Location = new Point(10, 8);

            panelHotItems.Location = new Point(6, 26);
            panelHotItems.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            panelHotItems.Size = new Size(288, 306);
            panelHotItems.BackColor = Color.Transparent;
            panelHotItems.AutoScroll = true;

            panelHotCard.Controls.AddRange(new Control[] { lblHotTitle, panelHotItems });

            panelRecentCard.Dock = DockStyle.Fill;
            panelRecentCard.BackColor = Color.FromArgb(32, 35, 44);
            panelRecentCard.Paint += new PaintEventHandler(PaintDarkCard);
            panelRecentCard.Margin = new Padding(0, 6, 0, 0);

            var pnlRH = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.Transparent };
            lblRecentTitle.Text = "RECENT SALES";
            lblRecentTitle.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            lblRecentTitle.ForeColor = Color.FromArgb(130, 140, 158);
            lblRecentTitle.BackColor = Color.Transparent;
            lblRecentTitle.AutoSize = true;
            lblRecentTitle.Location = new Point(10, 6);
            pnlRH.Controls.Add(lblRecentTitle);

            var recentDiv = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(50, 54, 66) };

            panelRecentSales.Dock = DockStyle.Fill;
            panelRecentSales.BackColor = Color.Transparent;
            panelRecentSales.Padding = new Padding(4);
            panelRecentSales.AutoScroll = true;

            panelRecentCard.Controls.Add(panelRecentSales);
            panelRecentCard.Controls.Add(recentDiv);
            panelRecentCard.Controls.Add(pnlRH);

            panelLeft.Controls.Add(panelRecentCard);
            panelLeft.Controls.Add(panelHotCard);
            panelLeft.Controls.Add(panelDiscountCard);
            panelLeft.Controls.Add(panelSearchCard);

            // ══════════════════════════════════════════════════════════════════
            //  CENTRE COLUMN
            // ══════════════════════════════════════════════════════════════════
            panelCentre.Dock = DockStyle.Fill;
            panelCentre.BackColor = Color.FromArgb(32, 35, 44);
            panelCentre.Padding = new Padding(6, 0, 6, 0);
            panelCentre.Paint += new PaintEventHandler(PaintDarkCard);

            panelCentreHeader.Dock = DockStyle.Top;
            panelCentreHeader.Height = 38;
            panelCentreHeader.BackColor = Color.Transparent;

            lblCartTitle.Text = "CART / ITEMS";
            lblCartTitle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            lblCartTitle.ForeColor = Color.FromArgb(240, 242, 246);
            lblCartTitle.BackColor = Color.Transparent;
            lblCartTitle.AutoSize = true;
            lblCartTitle.Location = new Point(10, 10);

            lblItemCount.Text = "0 item(s)";
            lblItemCount.Font = new Font("Segoe UI", 8.5F);
            lblItemCount.ForeColor = Color.FromArgb(130, 140, 158);
            lblItemCount.BackColor = Color.Transparent;
            lblItemCount.AutoSize = true;
            lblItemCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblItemCount.Location = new Point(420, 12);

            panelCentreHeader.Controls.AddRange(new Control[] { lblCartTitle, lblItemCount });

            // ── Big centered Grand Total banner — sits above the cart, always visible ──
            // ── Big centered Grand Total / Due banner — sits below the cart, always visible ──
            lblGrandTotalBig.Dock = DockStyle.Bottom;
            lblGrandTotalBig.Height = 60;
            lblGrandTotalBig.Text = "P 0.00";
            lblGrandTotalBig.Font = new Font("Segoe UI", 26F, FontStyle.Bold);
            lblGrandTotalBig.ForeColor = Color.FromArgb(52, 211, 153);
            lblGrandTotalBig.BackColor = Color.FromArgb(28, 32, 42);
            lblGrandTotalBig.TextAlign = ContentAlignment.MiddleCenter;

            // ── Cart items panel — no extra padding so row content controls alignment ──
            panelCartItems.Dock = DockStyle.Fill;
            panelCartItems.BackColor = Color.Transparent;
            panelCartItems.AutoScroll = true;
            panelCartItems.Padding = new Padding(2, 2, 2, 2);   // FIX: reduced padding so price col is not clipped

            panelCentre.Controls.Add(panelCartItems);
            panelCentre.Controls.Add(lblGrandTotalBig);
            panelCentre.Controls.Add(panelCentreHeader);

            // ══════════════════════════════════════════════════════════════════
            //  RIGHT COLUMN
            // ══════════════════════════════════════════════════════════════════
            panelRight.Dock = DockStyle.Fill;
            panelRight.BackColor = Color.Transparent;
            panelRight.Padding = new Padding(6, 0, 0, 0);

            // ── Totals card — uses TableLayoutPanel so values are ALWAYS right-aligned ──
            panelTotalsCard.Dock = DockStyle.Top;
            panelTotalsCard.Height = 184;
            panelTotalsCard.BackColor = Color.FromArgb(32, 35, 44);
            panelTotalsCard.Paint += new PaintEventHandler(PaintDarkCard);
            panelTotalsCard.Padding = new Padding(10, 8, 10, 8);

            // TableLayoutPanel: 2 cols (label | value), 4 rows (subtotal, discount, tax, grand)
            var tblTotals = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 7,   // subtotal, discount, tax, divider-row, grand
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            tblTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));  // label col
            tblTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));  // value col  // value col  // subtotal, discount, tax, divider, unit prices, grand
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));  // row 0: subtotal
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));  // row 1: discount
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));  // row 2: tax
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, 6F));   // row 3: divider gap
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));   // row 4: unit prices (starts hidden)
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // row 5: grand total
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));  // row 6: stock reduction ← ADD

            // Helper: left hint label
            Label MakeTL(string text, bool bold = false, float sz = 9.5f, Color? col = null) =>
                new Label
                {
                    Text = text,
                    Font = new Font("Segoe UI", sz, bold ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = col ?? Color.FromArgb(130, 140, 158),
                    BackColor = Color.Transparent,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(2, 0, 0, 0)
                };

            // Helper: right value label
            Label MakeTR(string text, bool bold = false, float sz = 9.5f, Color? col = null) =>
                new Label
                {
                    Text = text,
                    Font = new Font("Segoe UI", sz, bold ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = col ?? Color.FromArgb(240, 242, 246),
                    BackColor = Color.Transparent,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleRight,
                    Margin = new Padding(0, 0, 2, 0)
                };

            lblSubtotalHint = MakeTL("Subtotal");
            lblSubtotalVal = MakeTR("P 0.00");

            lblDiscountHint = MakeTL("Discount", col: Color.FromArgb(251, 146, 60));
            lblDiscountVal = MakeTR("- P 0.00", col: Color.FromArgb(251, 146, 60));

            lblTaxHint = MakeTL("Tax (14%)");
            lblTaxVal = MakeTR("P 0.00");

            // Divider row — spans both columns
            panelTotalDivider.Dock = DockStyle.Fill;
            panelTotalDivider.BackColor = Color.FromArgb(50, 54, 66);
            panelTotalDivider.Margin = new Padding(0, 2, 0, 2);

            // ADD this just before lblGrandTotal is added to its parent panel
            // FIND and REPLACE the lblUnitPricesSummary creation:
            //lblUnitPricesSummary = new Label
            //{
            //    Name = "lblUnitPricesSummary",
            //    Font = new Font("Segoe UI", 7.5F),
            //    ForeColor = Color.FromArgb(100, 110, 135),
            //    BackColor = Color.Transparent,
            //    AutoSize = false,
            //    TextAlign = ContentAlignment.TopLeft,
            //    Dock = DockStyle.Fill,        // ← Fill instead of None
            //    Margin = new Padding(2, 2, 2, 2),
            //    Visible = false               // ← hidden until cart has items
            //};
            // Add it to the same parent panel as lblGrandTotal
            // e.g. panelTotals.Controls.Add(lblUnitPricesSummary);

            // Grand Total row
            lblGrandHint = new Label
            {
                Text = "GRAND TOTAL",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(2, 0, 0, 0)
            };

            lblGrandTotal = new Label
            {
                Text = "P 0.00",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 0, 4, 0),
                AutoEllipsis = false,
                AutoSize = false
            };
            lblStockReduction = new Label
            {
                Text = "📉 Stock to reduce: 0 unit(s)",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 146, 60),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(2, 2, 2, 0),
                AutoEllipsis = true
            };

            // Add rows to table
            // REPLACE:
            tblTotals.Controls.Add(lblSubtotalHint, 0, 0);
            tblTotals.Controls.Add(lblSubtotalVal, 1, 0);
            tblTotals.Controls.Add(lblDiscountHint, 0, 1);
            tblTotals.Controls.Add(lblDiscountVal, 1, 1);
            tblTotals.Controls.Add(lblTaxHint, 0, 2);
            tblTotals.Controls.Add(lblTaxVal, 1, 2);
            tblTotals.Controls.Add(panelTotalDivider, 0, 3);
            tblTotals.SetColumnSpan(panelTotalDivider, 2);
            // Unit prices row — spans both columns, hidden until cart has items
            //tblTotals.Controls.Add(lblUnitPricesSummary, 0, 4);
            //tblTotals.SetColumnSpan(lblUnitPricesSummary, 2);
            // Grand total row
            tblTotals.Controls.Add(lblGrandHint, 0, 5);
            tblTotals.Controls.Add(lblGrandTotal, 1, 5);

            tblTotals.Controls.Add(lblStockReduction, 0, 6);
            tblTotals.SetColumnSpan(lblStockReduction, 2);

            panelTotalsCard.Controls.Add(tblTotals);

            // ── Payment card (fills remaining space) ──────────────────────────
            panelPayCard.Dock = DockStyle.Fill;
            panelPayCard.BackColor = Color.FromArgb(32, 35, 44);
            panelPayCard.Paint += new PaintEventHandler(PaintDarkCard);
            panelPayCard.Padding = new Padding(10, 8, 10, 8);
            panelPayCard.Margin = new Padding(0, 6, 0, 0);

            lblPayTitle.Text = "";
            lblPayTitle.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            lblPayTitle.ForeColor = Color.FromArgb(130, 140, 158);
            lblPayTitle.BackColor = Color.Transparent;
            lblPayTitle.AutoSize = false;
            lblPayTitle.Size = new Size(580, 20);
            lblPayTitle.Location = new Point(10, 6);

            Panel MakeSplitPanel(string icon, string title, out Label titleLbl, out TextBox valBox, int y)
            {
                var p = new Panel
                {
                    Size = new Size(580, 44),
                    Location = new Point(10, y),
                    BackColor = Color.FromArgb(42, 46, 56),
                    Cursor = Cursors.Hand,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                p.Region = MakeRoundedRegion(p.Size, 8);
                p.SizeChanged += (s, ev) => { var pp = (Panel)s; pp.Region = MakeRoundedRegion(pp.Size, 8); };

                var icoLbl = new Label
                {
                    Text = icon,
                    Font = new Font("Segoe UI Emoji", 14F),
                    ForeColor = Color.FromArgb(240, 242, 246),
                    BackColor = Color.Transparent,
                    Size = new Size(38, 44),
                    Location = new Point(6, 0),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                titleLbl = new Label
                {
                    Text = title,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(130, 140, 158),
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(48, 12)
                };
                var rsLbl = new Label
                {
                    Text = "P",
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = Color.FromArgb(130, 140, 158),
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(380, 13),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                valBox = new TextBox
                {
                    Font = new Font("Consolas", 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(52, 211, 153),
                    BackColor = Color.FromArgb(42, 46, 56),
                    BorderStyle = BorderStyle.None,
                    ReadOnly = true,
                    Size = new Size(160, 22),
                    Location = new Point(400, 11),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    TextAlign = HorizontalAlignment.Right,
                    Text = ""
                };
                p.Controls.AddRange(new Control[] { icoLbl, titleLbl, rsLbl, valBox });
                return p;
            }

            panelSplitCash = MakeSplitPanel("💵", "CASH", out lblCashTitle, out txtSplitCash, 30);
            panelSplitUpi = MakeSplitPanel("📱", "Bank Transfer", out lblUpiTitle, out txtSplitUpi, 82);
            panelSplitCard = MakeSplitPanel("💳", "CARD", out lblCardTitle, out txtSplitCard, 134);

            panelSplitCash.Click += new EventHandler(panelSplitCash_Click);
            panelSplitUpi.Click += new EventHandler(panelSplitUpi_Click);
            panelSplitCard.Click += new EventHandler(panelSplitCard_Click);

            foreach (Control c in panelSplitCash.Controls) c.Click += new EventHandler(panelSplitCash_Click);
            foreach (Control c in panelSplitUpi.Controls) c.Click += new EventHandler(panelSplitUpi_Click);
            foreach (Control c in panelSplitCard.Controls) c.Click += new EventHandler(panelSplitCard_Click);

            lblSplitBalance.Text = "";
            lblSplitBalance.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            lblSplitBalance.ForeColor = Color.FromArgb(130, 140, 158);
            lblSplitBalance.BackColor = Color.Transparent;
            lblSplitBalance.AutoSize = false;
            lblSplitBalance.Size = new Size(580, 42);
            lblSplitBalance.Location = new Point(10, 184);
            lblSplitBalance.TextAlign = ContentAlignment.TopLeft;
            lblSplitBalance.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            btnSplitExact.Text = "Fill (F7)";
            btnSplitExact.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            btnSplitExact.Size = new Size(120, 26);
            btnSplitExact.Location = new Point(460, 232);
            btnSplitExact.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSplitExact.BackColor = Color.FromArgb(50, 70, 55);
            btnSplitExact.ForeColor = Color.FromArgb(52, 211, 153);
            btnSplitExact.FlatStyle = FlatStyle.Flat;
            btnSplitExact.FlatAppearance.BorderSize = 0;
            btnSplitExact.Cursor = Cursors.Hand;
            btnSplitExact.Click += new EventHandler(btnSplitExact_Click);

            //Button MakeQuick(string text, int x, EventHandler handler)
            //{
            //    var b = new Button
            //    {
            //        Text = text,
            //        Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            //        Size = new Size(132, 30),
            //        Location = new Point(x, 218),
            //        BackColor = Color.FromArgb(50, 55, 68),
            //        ForeColor = Color.FromArgb(220, 225, 235),
            //        FlatStyle = FlatStyle.Flat,
            //        Cursor = Cursors.Hand
            //    };
            //    b.FlatAppearance.BorderSize = 0;
            //    b.Region = MakeRoundedRegion(b.Size, 6);
            //    b.Click += handler;
            //    return b;
            //}

            //btnQuick50 = MakeQuick("Rs 50", 10, btnQuick50_Click);
            //btnQuick100 = MakeQuick("Rs 100", 148, btnQuick100_Click);
            //btnQuick200 = MakeQuick("Rs 200", 286, btnQuick200_Click);
            //btnQuick500 = MakeQuick("Rs 500", 424, btnQuick500_Click);

            panelNumpad.Location = new Point(10, 256);
            panelNumpad.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panelNumpad.BackColor = Color.Transparent;
            panelNumpad.Size = new Size(580, 300);
            panelNumpad.Visible = false;  // HIDE NUMPAD

            string[] tags = { "7", "8", "9", "4", "5", "6", "1", "2", "3", ".", "0", "back" };
            string[] labels = { "7", "8", "9", "4", "5", "6", "1", "2", "3", ".", "0", "⌫" };

            for (int r = 0; r < 4; r++)
                for (int c2 = 0; c2 < 3; c2++)
                {
                    int idx = r * 3 + c2;
                    var nb = new Button
                    {
                        Text = labels[idx],
                        Tag = tags[idx],
                        Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                        BackColor = tags[idx] == "back" ? Color.FromArgb(180, 40, 40) : Color.FromArgb(44, 48, 60),
                        ForeColor = Color.FromArgb(240, 242, 246),
                        FlatStyle = FlatStyle.Flat,
                        Cursor = Cursors.Hand,
                        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
                    };
                    nb.FlatAppearance.BorderSize = 0;
                    nb.Click += new EventHandler(NumpadBtn_Click);

                    var origColor = nb.BackColor;
                    nb.MouseEnter += (s, ev) => ((Button)s).BackColor = ControlPaint.Light(origColor, 0.15f);
                    nb.MouseLeave += (s, ev) => ((Button)s).BackColor = origColor;

                    panelNumpad.Controls.Add(nb);
                }

            void LayoutNumpad(Panel np)
            {
                int cols = 3, rows = 4;
                int gx = 6, gy = 6;
                int bw = (np.Width - gx * (cols + 1)) / cols;
                int bh = (np.Height - gy * (rows + 1)) / rows;
                if (bw <= 0 || bh <= 0) return;

                int idx = 0;
                for (int row = 0; row < rows; row++)
                    for (int col = 0; col < cols; col++)
                    {
                        if (idx >= np.Controls.Count) break;
                        var btn = np.Controls[idx++];
                        btn.SetBounds(gx + col * (bw + gx), gy + row * (bh + gy), bw, bh);
                        btn.Region = MakeRoundedRegion(btn.Size, 8);
                    }
            }

            panelNumpad.SizeChanged += (s, e) => LayoutNumpad((Panel)s);
            panelNumpad.HandleCreated += (s, e) => LayoutNumpad((Panel)s);

            panelPayCard.SizeChanged += (s, e) =>
            {
                int newH = panelPayCard.Height - 256 - panelPayCard.Padding.Bottom - 8;
                if (newH > 80)
                {
                    panelNumpad.Size = new Size(
                        panelPayCard.Width - panelPayCard.Padding.Left - panelPayCard.Padding.Right - 4,
                        newH);
                    LayoutNumpad(panelNumpad);
                }
            };

            panelPayCard.Controls.AddRange(new Control[]
            {
                lblPayTitle,
                panelSplitCash, panelSplitUpi, panelSplitCard,
                lblSplitBalance, btnSplitExact,
                //btnQuick50, btnQuick100, btnQuick200, btnQuick500,
                panelNumpad
            });

            panelRight.Controls.Add(panelPayCard);
            panelRight.Controls.Add(panelTotalsCard);

            // ══════════════════════════════════════════════════════════════════
            //  ASSEMBLE
            // ══════════════════════════════════════════════════════════════════
            tblRoot.Controls.Add(panelLeft, 0, 0);
            tblRoot.Controls.Add(panelCentre, 1, 0);
            tblRoot.Controls.Add(panelRight, 2, 0);

            this.Controls.Add(tblRoot);
            this.Controls.Add(panelFooterBar);
            this.Controls.Add(panelHeader);

            this.ResumeLayout(false);
        }
    }
}   