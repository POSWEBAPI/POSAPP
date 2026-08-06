using POSAPP.Invoice;
using POSAPP.Payment;
using POSAPP.Reports;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace POSAPP
{
    public class ReprintHistoryForm : Form
    {
        // ── Palette ────────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);
        private static readonly Color TabActive = Color.FromArgb(59, 130, 246);
        private static readonly Color TabInactive = Color.FromArgb(42, 46, 56);

        // ── Layout constants ───────────────────────────────────────────────
        private const int FORM_W = 980;
        private const int FORM_H = 680;
        private const int HDR_H = 56;
        private const int FILTER_H = 52;
        private const int TAB_H = 42;
        private const int COL_H = 32;
        private const int LIST_TOP = HDR_H + FILTER_H + TAB_H + COL_H;
        private const int CARD_H = 58;
        private const int ROW_GAP = 62;

        // ── Column X positions (no overlap) ───────────────────────────────
        private const int COL_INV = 14;
        private const int COL_CUST = 210;
        private const int COL_DATE = 390;
        private const int COL_TIME = 490;
        private const int COL_TOTAL = 560;
        private const int COL_BTN = 840;

        // ── Column widths ──────────────────────────────────────────────────
        private const int WID_INV = 188;
        private const int WID_CUST = 172;
        private const int WID_DATE = 92;
        private const int WID_TIME = 62;
        private const int WID_TOTAL = 120;
        private const int WID_BTN = 110;

        // ── State ──────────────────────────────────────────────────────────
        private readonly int _companyId;
        private readonly string _currencySymbol;
        private readonly string _companyName;
        private readonly string _companyAddress;
        private readonly string _companyPhone;
        private readonly string _companyVat;
        private readonly string _companyWebsite;
        private readonly string _salesOfficeInfo;
        private readonly SalesForm _salesForm;

        private List<SalesRepository.ReprintInvoiceRow> _allRows = new();
        private List<SalesRepository.ReprintInvoiceRow> _filtered = new();
        private string _searchText = "";
        private int _daysFilter = 30;
        private string _activeTab = "inv";   // "inv" | "quo"

        // ── Controls ───────────────────────────────────────────────────────
        private Panel panelList;
        private TextBox txtSearch;
        private Label lblCount;
        private Label lblStatus;
        private Button _btnTabInv;
       // private Button _btnTabQuo;
        private Label _lblInvBadge;
        private Label _lblQuoBadge;
        private Panel panelColHdr;

        // ══════════════════════════════════════════════════════════════════
        public ReprintHistoryForm(
            int companyId, string currencySymbol,
            string companyName, string companyAddress,
            string companyPhone, string companyVat,
            string companyWebsite, string salesOfficeInfo,
            SalesForm salesForm)
        {
            _companyId = companyId;
            _currencySymbol = currencySymbol;
            _companyName = companyName;
            _companyAddress = companyAddress;
            _companyPhone = companyPhone;
            _companyVat = companyVat;
            _companyWebsite = companyWebsite;
            _salesOfficeInfo = salesOfficeInfo;
            _salesForm = salesForm;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgDark;
            ClientSize = new Size(FORM_W, FORM_H);
            KeyPreview = true;
            ShowInTaskbar = false;

            BuildUI();
            LoadData();
        }
         

        // ══════════════════════════════════════════════════════════════════
        //  UI BUILD
        // ══════════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            // ── [1] Header ────────────────────────────────────────────────
            var pnlHead = new Panel
            {
                BackColor = PanelDark,
                Size = new Size(FORM_W, HDR_H),
                Location = Point.Empty
            };

            pnlHead.Controls.Add(new Label
            {
                Text = "🖨  Invoice Reprint History",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(500, HDR_H),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            lblCount = new Label
            {
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(524, 20)
            };
            pnlHead.Controls.Add(lblCount);

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(52, HDR_H),
                Location = new Point(FORM_W - 52, 0),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.MouseEnter += (s, e) => btnX.ForeColor = AccRed;
            btnX.MouseLeave += (s, e) => btnX.ForeColor = TextMuted;
            btnX.Click += (s, e) => Close();
            pnlHead.Controls.Add(btnX);

            // ── [2] Filter bar ────────────────────────────────────────────
            var pnlFilter = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Size = new Size(FORM_W, FILTER_H),
                Location = new Point(0, HDR_H)
            };

            txtSearch = new TextBox
            {
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextMuted,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(260, 28),
                Location = new Point(12, 12),
                Text = "Search invoice / customer…"
            };
            txtSearch.Enter += (s, e) =>
            {
                if (txtSearch.Text.Contains("…"))
                { txtSearch.Text = ""; txtSearch.ForeColor = TextWhite; }
            };
            txtSearch.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                { txtSearch.Text = "Search invoice / customer…"; txtSearch.ForeColor = TextMuted; }
            };
            txtSearch.TextChanged += (s, e) =>
            {
                _searchText = txtSearch.Text.Contains("…") ? "" : txtSearch.Text.Trim();
                ApplyFilter();
            };
            pnlFilter.Controls.Add(txtSearch);

            // Day filter buttons
            int bx = 286;
            foreach (var (label, days) in new[]
            {
                ("Today",   1),
                ("7 Days",  7),
                ("30 Days", 30),
                ("All",     365)
            })
            {
                var cap = days;
                var btn = new Button
                {
                    Text = label,
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                    ForeColor = days == _daysFilter ? Color.White : TextMuted,
                    BackColor = days == _daysFilter ? AccBlue : Color.FromArgb(38, 42, 54),
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(72, 28),
                    Location = new Point(bx, 12),
                    Cursor = Cursors.Hand,
                    Tag = days
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) =>
                {
                    _daysFilter = cap;
                    foreach (Control c in pnlFilter.Controls)
                        if (c is Button b && b.Tag is int)
                        {
                            b.BackColor = (int)b.Tag == _daysFilter
                                ? AccBlue : Color.FromArgb(38, 42, 54);
                            b.ForeColor = (int)b.Tag == _daysFilter
                                ? Color.White : TextMuted;
                        }
                    LoadData();
                };
                pnlFilter.Controls.Add(btn);
                bx += 78;
            }

            lblStatus = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(FORM_W - 180, 18)
            };
            pnlFilter.Controls.Add(lblStatus);

            // ── [3] Tab bar ───────────────────────────────────────────────
            var pnlTabs = new Panel
            {
                BackColor = Color.FromArgb(26, 29, 38),
                Size = new Size(FORM_W, TAB_H),
                Location = new Point(0, HDR_H + FILTER_H)
            };

            // Tab bottom border line
            pnlTabs.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(FORM_W, 1),
                Location = new Point(0, TAB_H - 1)
            });

            _btnTabInv = MakeTabButton("📄  Sales Invoices", true);
            _btnTabInv.Location = new Point(12, 6);
            _btnTabInv.Click += (s, e) => SwitchTab("inv");

            //_btnTabQuo = MakeTabButton("📋  Quotations", false);
            //_btnTabQuo.Location = new Point(168, 6);
            //_btnTabQuo.Click += (s, e) => SwitchTab("quo");

            _lblInvBadge = MakeBadge("0", true);
            _lblInvBadge.Location = new Point(120, 13);

            _lblQuoBadge = MakeBadge("0", false);
            _lblQuoBadge.Location = new Point(276, 13);

            pnlTabs.Controls.AddRange(new Control[]
                { _btnTabInv, _lblInvBadge, _lblQuoBadge });//_btnTabQuo

            // ── [4] Column header ─────────────────────────────────────────
            panelColHdr = new Panel
            {
                BackColor = Color.FromArgb(32, 36, 48),
                Size = new Size(FORM_W, COL_H),
                Location = new Point(0, HDR_H + FILTER_H + TAB_H)
            };
            panelColHdr.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(FORM_W, 1),
                Location = new Point(0, COL_H - 1)
            });
            BuildColHeader();

            // ── [5] Scrollable list ───────────────────────────────────────
            panelList = new Panel
            {
                BackColor = BgDark,
                AutoScroll = true,
                Size = new Size(FORM_W, FORM_H - LIST_TOP),
                Location = new Point(0, LIST_TOP)
            };

            Controls.AddRange(new Control[]
                { pnlHead, pnlFilter, pnlTabs, panelColHdr, panelList });

            // Rounded border
            Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Border, 1.5f);
                using var path = RoundedPath(new Rectangle(1, 1, Width - 2, Height - 2), 12);
                pe.Graphics.DrawPath(pen, path);
            };
            Region = MakeRoundedRegion(Size, 12);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private Button MakeTabButton(string text, bool active) => new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = active ? Color.White : TextMuted,
            BackColor = active ? TabActive : Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(148, 30),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatAppearance = { BorderSize = 0 }
        };

        private Label MakeBadge(string text, bool active) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = active ? Color.White : TextMuted,
            BackColor = active ? Color.FromArgb(80, 140, 255) : Color.FromArgb(50, 54, 66),
            AutoSize = false,
            Size = new Size(28, 16),
            TextAlign = ContentAlignment.MiddleCenter
        };

        private void BuildColHeader()
        {
            panelColHdr.Controls.Clear();
            panelColHdr.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(FORM_W, 1),
                Location = new Point(0, COL_H - 1)
            });

            void CH(string t, int x, int w,
                    ContentAlignment a = ContentAlignment.MiddleLeft)
            {
                panelColHdr.Controls.Add(new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(w, COL_H - 1),
                    Location = new Point(x, 0),
                    TextAlign = a
                });
            }

            CH("INVOICE NO", COL_INV, WID_INV);
            CH("CUSTOMER", COL_CUST, WID_CUST);
            CH("DATE", COL_DATE, WID_DATE);
            CH("TIME", COL_TIME, WID_TIME);
            CH("TOTAL", COL_TOTAL, WID_TOTAL, ContentAlignment.MiddleRight);
            CH("", COL_BTN, WID_BTN);
        }

        private void SwitchTab(string tab)
        {
            _activeTab = tab;

            bool inv = tab == "inv";
            _btnTabInv.BackColor = inv ? TabActive : Color.Transparent;
            _btnTabInv.ForeColor = inv ? Color.White : TextMuted;
            //_btnTabQuo.BackColor = inv ? Color.Transparent : TabActive;
            //_btnTabQuo.ForeColor = inv ? TextMuted : Color.White;

            _lblInvBadge.BackColor = inv
                ? Color.FromArgb(80, 140, 255) : Color.FromArgb(50, 54, 66);
            _lblInvBadge.ForeColor = inv ? Color.White : TextMuted;
            _lblQuoBadge.BackColor = inv
                ? Color.FromArgb(50, 54, 66) : Color.FromArgb(80, 140, 255);
            _lblQuoBadge.ForeColor = inv ? TextMuted : Color.White;

            ApplyFilter();
        }

        // ══════════════════════════════════════════════════════════════════
        //  DATA
        // ══════════════════════════════════════════════════════════════════
        private async void LoadData()
        {
            lblStatus.Text = "Loading…";
            try
            {
                _allRows = await SalesInvoiceApi.GetReprintInvoicesAsync(_companyId, _daysFilter);
            }
            catch (Exception ex)
            {
                _allRows = new List<SalesRepository.ReprintInvoiceRow>();
                lblStatus.Text = "Load error: " + ex.Message;
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _filtered = _allRows
                .Where(r =>
                {
                    bool isQuo = IsQuotation(r.InvoiceNo);
                    bool matchTab = _activeTab == "quo" ? isQuo : !isQuo;
                    bool matchSearch = string.IsNullOrEmpty(_searchText)
                        || r.InvoiceNo.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                        || r.CustomerName.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
                    return matchTab && matchSearch;
                })
                .ToList();

            int invCount = _allRows.Count(r => !IsQuotation(r.InvoiceNo));
            //int quoCount = _allRows.Count(r => IsQuotation(r.InvoiceNo));

            _lblInvBadge.Text = invCount.ToString();
            //_lblQuoBadge.Text = quoCount.ToString();
            lblCount.Text = $"{invCount} invoice(s) ";// |  {quoCount} quotation(s)
            lblStatus.Text = $"Last {_daysFilter} day(s)";

            RenderList();
        }

        // ══════════════════════════════════════════════════════════════════
        //  RENDER
        // ══════════════════════════════════════════════════════════════════
        private void RenderList()
        {
            panelList.SuspendLayout();
            foreach (Control c in panelList.Controls) c.Dispose();
            panelList.Controls.Clear();

            if (_filtered.Count == 0)
            {
                panelList.Controls.Add(new Label
                {
                    Text = _activeTab == "quo"
                                ? "No quotations found."
                                : "No invoices found.",
                    Font = new Font("Segoe UI", 11F),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(FORM_W, 80),
                    Location = new Point(0, 40),
                    TextAlign = ContentAlignment.MiddleCenter
                });
                panelList.ResumeLayout();
                return;
            }

            int y = 4;
            foreach (var row in _filtered)
            {
                panelList.Controls.Add(BuildRow(row, y));
                y += ROW_GAP;
            }

            panelList.ResumeLayout();
        }

        private Panel BuildRow(SalesRepository.ReprintInvoiceRow row, int yOffset)
        {
            bool isQuo = IsQuotation(row.InvoiceNo);

            var card = new Panel
            {
                BackColor = Color.FromArgb(30, 33, 44),
                Size = new Size(FORM_W - 4, CARD_H),
                Location = new Point(2, yOffset),
                Cursor = Cursors.Hand
            };

            // Left accent bar
            card.Controls.Add(new Panel
            {
                BackColor = isQuo ? AccOrange : AccBlue,
                Size = new Size(3, CARD_H),
                Location = Point.Empty
            });

            void Lbl(string t, int x, int w, Font f, Color fc,
                     ContentAlignment a = ContentAlignment.MiddleLeft,
                     bool ellipsis = false)
            {
                card.Controls.Add(new Label
                {
                    Text = t,
                    Font = f,
                    ForeColor = fc,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(w, CARD_H),
                    Location = new Point(x, 0),
                    TextAlign = a,
                    AutoEllipsis = ellipsis
                });
            }

            // Invoice No
            Lbl(row.InvoiceNo,
                COL_INV, WID_INV,
                new Font("Segoe UI", 9F, FontStyle.Bold),
                TextWhite, ellipsis: true);

            // Badge
            var badge = new Label
            {
                Text = isQuo ? "Quotation" : "Tax Invoice",
                Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                ForeColor = isQuo ? Color.FromArgb(251, 191, 36) : Color.FromArgb(96, 165, 250),
                BackColor = isQuo ? Color.FromArgb(60, 45, 20) : Color.FromArgb(20, 40, 70),
                AutoSize = false,
                Size = new Size(74, 16),
                Location = new Point(COL_INV + 2, CARD_H - 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            badge.Region = MakeRoundedRegion(badge.Size, 4);
            card.Controls.Add(badge);

            // Customer
            Lbl(row.CustomerName,
                COL_CUST, WID_CUST,
                new Font("Segoe UI", 9F),
                TextMuted, ellipsis: true);

            // Date
            Lbl(row.SaleDate.ToString("dd MMM yyyy"),
                COL_DATE, WID_DATE,
                new Font("Segoe UI", 8.5F),
                TextMuted);

            // Time
            Lbl(row.SaleDate.ToString("HH:mm"),
                COL_TIME, WID_TIME,
                new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Color.FromArgb(99, 179, 255));

            // Total
            Lbl($"{_currencySymbol} {row.GrandTotal.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}",
    COL_TOTAL, WID_TOTAL,
        new Font("Segoe UI", 9.5F, FontStyle.Bold),
        TextGreen,
        ContentAlignment.MiddleRight);

            // Reprint button
            var btnReprint = new Button
            {
                Text = "🖨  Reprint",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = isQuo ? AccOrange : AccBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(100, 34),
                Location = new Point(COL_BTN, (CARD_H - 34) / 2),
                Cursor = Cursors.Hand
            };
            btnReprint.FlatAppearance.BorderSize = 0;
            btnReprint.Region = MakeRoundedRegion(btnReprint.Size, 6);
            btnReprint.MouseEnter += (s, e) =>
                btnReprint.BackColor = ControlPaint.Dark(btnReprint.BackColor, 0.1f);
            btnReprint.MouseLeave += (s, e) =>
                btnReprint.BackColor = isQuo ? AccOrange : AccBlue;

            var captured = row;
            btnReprint.Click += (s, e) => DoReprint(captured);
            card.Click += (s, ev) =>
            {
                var mp = card.PointToClient(Cursor.Position);
                bool overBtn = card.Controls.OfType<Button>()
                                   .Any(b => b.Bounds.Contains(mp));
                if (!overBtn) DoReprint(captured);
            };
            card.Controls.Add(btnReprint);

            // Bottom divider
            card.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(FORM_W - 4, 1),
                Location = new Point(0, CARD_H - 1)
            });

            // Hover
            Color normal = Color.FromArgb(30, 33, 44);
            Color hoverC = Color.FromArgb(40, 44, 58);
            card.MouseEnter += (s, e) => card.BackColor = hoverC;
            card.MouseLeave += (s, e) => card.BackColor = normal;

            return card;
        }

        // ══════════════════════════════════════════════════════════════════
        //  REPRINT
        // ══════════════════════════════════════════════════════════════════
        private void DoReprint(SalesRepository.ReprintInvoiceRow row)
        {
            try
            {
                bool isQuo = IsQuotation(row.InvoiceNo);   // ← fixed

                var data = new ReceiptData
                {
                    InvoiceNo = row.InvoiceNo,
                    CompanyName = _companyName,
                    CompanyAddress = _companyAddress,
                    CompanyPhone = _companyPhone,
                    CompanyVat = _companyVat,
                    CompanyWebsite = _companyWebsite,
                    SalesOfficeInfo = _salesOfficeInfo,
                    CustomerName = row.CustomerName,
                    CurrencySymbol = string.IsNullOrWhiteSpace(row.CurrencySymbol)
                                       ? _currencySymbol : row.CurrencySymbol,
                    SaleDate = row.SaleDate,
                    PaidCash = row.PaidCash,
                    PaidDigital = row.PaidDigital,
                    PaidCard = row.PaidCard,
                    GrandTotal = row.GrandTotal,
                    IsQuotation = isQuo,
                    IsReprint = true
                };

                if (!string.IsNullOrWhiteSpace(row.CartJson))
                {
                    try
                    {
                        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var dtos = JsonSerializer
                            .Deserialize<List<SalesRepository.ReprintLineDto>>(row.CartJson, opts);

                        if (dtos != null)
                        {
                            decimal subtotal = 0m, discTotal = 0m;
                            foreach (var d in dtos)
                            {
                                decimal unitPrice = d.UnitPrice > 0 ? d.UnitPrice : d.ListPrice;
                                decimal listPrice = d.ListPrice > 0 ? d.ListPrice : unitPrice;
                                decimal lineTotal = d.LineTotal > 0
                                    ? d.LineTotal
                                    : Math.Round(unitPrice * d.Qty * (1m - d.DiscountPct / 100m), 2);
                                decimal discAmt = Math.Round(
                                    unitPrice * d.Qty * (d.DiscountPct / 100m), 2);

                                data.Lines.Add(new ReceiptLine
                                {
                                    StockCode = d.StockCode ?? "",
                                    Name = d.Name ?? "",
                                    UOM = d.UOM ?? "EA",
                                    Qty = d.Qty,
                                    QtyRequested = d.QtyRequested > 0 ? d.QtyRequested : d.Qty,
                                    QtyDispatched = d.QtyDispatched > 0 ? d.QtyDispatched : d.Qty,
                                    UnitPrice = unitPrice,
                                    ListPrice = listPrice,
                                    DiscountPct = d.DiscountPct,
                                    LineTotal = lineTotal
                                });

                                subtotal += unitPrice * d.Qty;
                                discTotal += discAmt;
                            }

                            data.Subtotal = subtotal;
                            data.DiscountTotal = discTotal;
                            decimal afterDisc = subtotal - discTotal;
                            data.TaxTotal = Math.Round(afterDisc * 0.14m, 2);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "ReprintHistory CartJson parse: " + ex.Message);
                    }
                }

                lblStatus.ForeColor = AccOrange;

                Form owner = (_salesForm != null && !_salesForm.IsDisposed)
                    ? (Form)_salesForm
                    : (Form)this;

                PrintReceiptDialog.Show(owner, data);

                lblStatus.ForeColor = TextGreen;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Reprint error: " + ex.Message;
                lblStatus.ForeColor = AccRed;
            }
        }
        // In SalesRepository
        // Delegates to single source of truth in SalesRepository
        private static bool IsQuotation(string invoiceNo) =>
            SalesRepository.IsQuotation(invoiceNo);
        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
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
    }
}