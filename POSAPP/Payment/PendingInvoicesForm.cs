using Newtonsoft.Json;
using POSAPP.Reports;
using POSAPP.Shift;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace POSAPP.Payment
{
    // ── Cache bridging SO Order → SO Invoice conversion ────────────────────
    // OpenInvoice() stashes the full SalesOrderApiRow here (keyed by SONumber)
    // when a "SalesOrder" row is opened. SalesForm's payment-success handler
    // should check this cache and, if a match is found, call
    // SalesOrderApi.CreateSOInvoiceFromSalesOrderAsync(so) then remove the key.
    public static class PendingSalesOrderCache
    {
        public static readonly Dictionary<string, SalesOrderApiRow> Cache = new();
    }

    public class PendingInvoicesForm : Form
    {
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color PanelDark2 = Color.FromArgb(42, 46, 56);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);

        private const int FORM_W = 900;
        private const int FORM_H = 640;
        private const int HDR_H = 52;
        private const int FILTER_H = 48;
        private const int LIST_TOP = HDR_H + FILTER_H;
        private const int CARD_W = FORM_W - 20;
        private const int CARD_H = 52;
        private const int ROW_GAP = 58;

        private const int COL_INV = 10;
        private const int COL_CUST = 155;
        private const int COL_DATE = 340;
        private const int COL_TOTAL = 490;
        private const int COL_OPEN = 620;
        private const int COL_DEL = 740;

        private readonly int _companyId;
        private readonly string _currencySymbol;
        private readonly SalesForm _salesForm;

        private const string FILTER_STATUS = "Unpaid";
        private string _filterText = "";

        private List<SalesRepository.PendingInvoiceRow> _rows = new();

        private Panel panelHeader = null!;
        private Panel panelFilter = null!;
        private Panel panelList = null!;
        private TextBox txtSearch = null!;
        private Label lblCount = null!;

        public PendingInvoicesForm(int companyId, string currencySymbol, SalesForm salesForm)
        {
            _companyId = companyId;
            _currencySymbol = currencySymbol;
            _salesForm = salesForm;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgDark;
            ClientSize = new Size(FORM_W, FORM_H);
            KeyPreview = true;
            ShowInTaskbar = false;

            BuildUI();
            LoadRows();
        }

        private void BuildUI()
        {
            panelHeader = new Panel
            {
                BackColor = PanelDark,
                Size = new Size(FORM_W, HDR_H),
                Location = Point.Empty
            };

            panelHeader.Controls.Add(new Label
            {
                Text = "📋  Unpaid Invoices",
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
                Location = new Point(520, 18)
            };
            panelHeader.Controls.Add(lblCount);

            var btnClose = new Button
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
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = AccRed;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = TextMuted;
            btnClose.Click += (s, e) => Close();
            panelHeader.Controls.Add(btnClose);

            panelFilter = new Panel
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
                Location = new Point(12, 10),
                Text = "Search invoice / customer\u2026"
            };
            txtSearch.Enter += (s, e) =>
            {
                if (txtSearch.Text.Contains("\u2026"))
                {
                    txtSearch.Text = "";
                    txtSearch.ForeColor = TextWhite;
                }
            };
            txtSearch.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    txtSearch.Text = "Search invoice / customer\u2026";
                    txtSearch.ForeColor = TextMuted;
                }
            };
            txtSearch.TextChanged += (s, e) =>
            {
                _filterText = txtSearch.Text.Contains("\u2026") ? "" : txtSearch.Text.Trim();
                RenderList();
            };
            panelFilter.Controls.Add(txtSearch);

            panelList = new Panel
            {
                BackColor = BgDark,
                AutoScroll = true,
                Size = new Size(FORM_W, FORM_H - LIST_TOP),
                Location = new Point(0, LIST_TOP)
            };

            Controls.AddRange(new Control[] { panelHeader, panelFilter, panelList });

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

        public async void LoadRows()
        {
            try
            {
                var soRows = await SalesOrderApi.GetPendingSalesOrdersAsync();

                var mappedSoRows = soRows
                    .Where(x => !SalesOrderApi.InvoicedSoNumbers.Contains(x.Order.SONumber))
                    .Select(x => new SalesRepository.PendingInvoiceRow
                    {
                        InvoiceNo = x.Order.SONumber,
                        CustomerName = x.CustomerName,
                        SaleDate = x.Order.SODate ?? DateTime.Now,
                        GrandTotal = x.Order.SOAmount,
                        Status = "Unpaid",
                        CartJson = "",
                        Source = "SalesOrder",
                        SourceSOId = x.Order.SOId
                    })
                    .ToList();

                _rows = mappedSoRows.OrderByDescending(r => r.SaleDate).ToList();
            }
            catch (Exception ex)
            {
                _rows = new List<SalesRepository.PendingInvoiceRow>();
                MessageBox.Show("Could not load pending sales orders:\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RenderList();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadRows();
        }

        private void RenderList()
        {
            panelList.SuspendLayout();
            foreach (Control c in panelList.Controls) c.Dispose();
            panelList.Controls.Clear();

            var filtered = _rows
                .Where(r => r.Status == FILTER_STATUS)
                .Where(r => string.IsNullOrEmpty(_filterText)
                         || r.InvoiceNo.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
                         || r.CustomerName.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            lblCount.Text = $"{filtered.Count} unpaid invoice(s)";

            if (filtered.Count == 0)
            {
                panelList.Controls.Add(new Label
                {
                    Text = "No unpaid invoices found.",
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

            var hdr = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Size = new Size(CARD_W, 30),
                Location = new Point(10, 4)
            };

            void Hdr(string t, int x, int w, ContentAlignment a = ContentAlignment.MiddleLeft) =>
                hdr.Controls.Add(new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(w, 30),
                    Location = new Point(x, 0),
                    TextAlign = a
                });

            Hdr("INVOICE", COL_INV, 130);
            Hdr("CUSTOMER", COL_CUST, 175);
            Hdr("DATE", COL_DATE, 140);
            Hdr("TOTAL", COL_TOTAL, 110, ContentAlignment.MiddleRight);
            Hdr("", COL_OPEN, 160);

            panelList.Controls.Add(hdr);

            int y = 38;
            foreach (var row in filtered)
            {
                panelList.Controls.Add(BuildInvoiceRow(row, y));
                y += ROW_GAP;
            }

            panelList.ResumeLayout();
        }

        // ══════════════════════════════════════════════════════════════════
        //  ROW BUILD — single `captured` declaration, no duplicate
        // ══════════════════════════════════════════════════════════════════
        private Panel BuildInvoiceRow(SalesRepository.PendingInvoiceRow row, int yOffset)
        {
            var card = new Panel
            {
                BackColor = Color.FromArgb(38, 42, 52),
                Size = new Size(CARD_W, CARD_H),
                Location = new Point(10, yOffset),
                Cursor = Cursors.Hand
            };

            void Lbl(string t, int x, int w, Font f, Color fc,
                     ContentAlignment a = ContentAlignment.MiddleLeft)
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
                    TextAlign = a
                });
            }

            Lbl(row.InvoiceNo, COL_INV, 130, new Font("Segoe UI", 9F, FontStyle.Bold), TextWhite);
            Lbl(row.CustomerName, COL_CUST, 175, new Font("Segoe UI", 9F), TextMuted);
            Lbl(row.SaleDate.ToString("dd MMM yyyy  HH:mm"), COL_DATE, 140, new Font("Segoe UI", 8.5F), TextMuted);
            Lbl($"{_currencySymbol} {row.GrandTotal:F2}", COL_TOTAL, 110,
                new Font("Segoe UI", 9.5F, FontStyle.Bold), TextGreen, ContentAlignment.MiddleRight);

            if (row.Source == "SalesOrder")
            {
                card.Controls.Add(new Label
                {
                    Text = "SO",
                    Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                    ForeColor = AccOrange,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(COL_INV, 32)
                });
            }

            var btnOpen = new Button
            {
                Text = "Pay",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(100, 34),
                Location = new Point(COL_OPEN, (CARD_H - 34) / 2),
                Cursor = Cursors.Hand
            };
            btnOpen.FlatAppearance.BorderSize = 0;
            btnOpen.Region = MakeRoundedRegion(btnOpen.Size, 6);

            // Single capture — used by both the button and the row-click handler
            var captured = row;

            btnOpen.Click += (s, e) => OpenInvoice(captured);

            card.Click += (s, ev) =>
            {
                var mp = card.PointToClient(Cursor.Position);
                bool overBtn = false;
                foreach (Control ch in card.Controls)
                    if (ch is Button && ch.Bounds.Contains(mp)) { overBtn = true; break; }
                if (!overBtn) OpenInvoice(captured);
            };

            card.Controls.Add(btnOpen);

            var btnDel = new Button
            {
                Text = "🗑",
                Font = new Font("Segoe UI", 10F),
                ForeColor = AccRed,
                BackColor = Color.FromArgb(55, 28, 28),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 34),
                Location = new Point(COL_DEL, (CARD_H - 34) / 2),
                Cursor = Cursors.Hand
            };
            btnDel.FlatAppearance.BorderSize = 0;
            btnDel.Region = MakeRoundedRegion(btnDel.Size, 6);

            var capturedDel = row;
            btnDel.Click += async (s, e) =>
            {
                if (MessageBox.Show(
                        $"Cancel {capturedDel.InvoiceNo}?\nThis cannot be undone.",
                        "Confirm Cancel",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                try
                {
                    bool ok = await SalesOrderApi.DeleteSalesOrderAsync(capturedDel.SourceSOId);

                    if (!ok)
                    {
                        MessageBox.Show("Cancel failed.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    LoadRows();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Cancel failed:\n" + ex.Message,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            card.Controls.Add(btnDel);

            card.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(CARD_W, 1),
                Location = new Point(0, CARD_H - 1)
            });

            Color normal = Color.FromArgb(38, 42, 52);
            Color hover = Color.FromArgb(44, 50, 62);
            card.MouseEnter += (s, e) => card.BackColor = hover;
            card.MouseLeave += (s, e) => card.BackColor = normal;

            return card;
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPEN INVOICE — single-arg LoadPendingInvoice call preserved.
        //  For SalesOrder rows, the full SalesOrderApiRow is stashed in
        //  PendingSalesOrderCache so SalesForm can convert it after payment.
        // ══════════════════════════════════════════════════════════════════
        private async void OpenInvoice(SalesRepository.PendingInvoiceRow row)
        {
            if (_salesForm == null || _salesForm.IsDisposed)
            {
                MessageBox.Show("Sales window is not available.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                bool isLinkedToSalesOrder = row.Source == "SalesOrder"
              || row.SourceSOId > 0
              || row.InvoiceNo.StartsWith("SO-", StringComparison.OrdinalIgnoreCase);

                if (isLinkedToSalesOrder)
                {
                    var soDetail = row.SourceSOId > 0
                        ? await SalesOrderApi.GetSalesOrderByIdAsync(row.SourceSOId)
                        : await SalesOrderApi.GetSalesOrderBySoNumberAsync(row.InvoiceNo);

                    if (soDetail == null)
                    {
                        MessageBox.Show("Failed to load Sales Order details.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var itemNames = await SalesOrderApi.GetItemNameMapAsync();
                    row.CartJson = BuildCartJsonFromSOLines(soDetail, itemNames);

                    // Stash for SalesForm to pick up after successful payment
                    PendingSalesOrderCache.Cache[row.InvoiceNo] = soDetail;
                }
                _rows.RemoveAll(r => r.InvoiceNo == row.InvoiceNo);
                this.Close();

                if (_salesForm.WindowState == FormWindowState.Minimized)
                    _salesForm.WindowState = FormWindowState.Normal;
                if (!_salesForm.Visible) _salesForm.Show();

                _salesForm.BringToFront();
                _salesForm.Activate();
                _salesForm.Focus();

                _salesForm.BeginInvoke(new Action(() =>
                {
                    ShiftState.LoadFromDb(_salesForm._companyId);
                    try
                    {
                        //_salesForm.LoadPendingInvoice(row);
                        _salesForm.BringToFront();
                        _salesForm.Activate();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to load invoice:\n" + ex.Message,
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open invoice:\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }



        private static string BuildCartJsonFromSOLines(SalesOrderApiRow so, Dictionary<int, string> itemNames)
        {
            var items = (so.Lines ?? new List<SalesOrderLineApiRow>())
                .Select(l => new
                {
                    Name = itemNames.TryGetValue(l.ItemId, out var n) && !string.IsNullOrWhiteSpace(n)
                        ? n
                        : $"Item #{l.ItemId}",
                    OriginalPrice = l.UnitPrice,
                    Price = l.UnitPrice,
                    Qty = l.Qty,
                    DiscountPct = l.DiscountPercent ?? 0m,
                    Barcode = l.ItemId.ToString()
                });

            return System.Text.Json.JsonSerializer.Serialize(items);
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
    }
}