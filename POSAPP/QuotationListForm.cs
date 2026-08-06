// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  QuotationListForm.cs  — lists, prints, converts, deletes quotations    ║
// ╚══════════════════════════════════════════════════════════════════════════╝
using POSAPP.Invoice;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace POSAPP
{
    public class QuotationListForm : Form
    {
        // ── Palette ────────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);

        // ── State ──────────────────────────────────────────────────────────
        private readonly int _companyId;
        private readonly string _currencySymbol;
        private readonly string _companyName, _companyAddress, _companyPhone;
        private readonly string _companyVat, _companyWebsite, _salesOfficeInfo;
        private readonly string _cashierName;

        private List<QuotationRow> _rows = new();
        private string _filterText = "";
        private string _statusFilter = "Open";

        private Panel panelHeader, panelFilter, panelList;
        private TextBox txtSearch;
        private Label lblCount;
        private ComboBox cmbStatus;

        private const int W = 1020;
        private const int H = 660;
        private const int HDR_H = 52;
        private const int FILTER_H = 48;
        private const int LIST_TOP = HDR_H + FILTER_H;
        private const int CARD_H = 56;
        private const int ROW_GAP = 62;

        // ── Constructor ────────────────────────────────────────────────────
        public QuotationListForm(
            int companyId, string currencySymbol,
            string companyName, string companyAddress, string companyPhone,
            string companyVat, string companyWebsite, string salesOfficeInfo,
            string cashierName = "ADMIN")
        {
            _companyId = companyId;
            _currencySymbol = currencySymbol;
            _companyName = companyName;
            _companyAddress = companyAddress;
            _companyPhone = companyPhone;
            _companyVat = companyVat;
            _companyWebsite = companyWebsite;
            _salesOfficeInfo = salesOfficeInfo;
            _cashierName = cashierName;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgDark;
            ClientSize = new Size(W, H);
            KeyPreview = true;
            ShowInTaskbar = false;

            BuildUI();
            LoadRows();
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI
        // ══════════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            panelHeader = new Panel
            {
                BackColor = PanelDark,
                Size = new Size(W, HDR_H),
                Location = Point.Empty
            };
            panelHeader.Controls.Add(new Label
            {
                Text = "📄  Quotations",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(400, HDR_H),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            lblCount = new Label
            {
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(430, 18)
            };
            panelHeader.Controls.Add(lblCount);
            var btnClose = MakeIconBtn("✕", new Point(W - 52, 0), new Size(52, HDR_H));
            btnClose.Click += (s, e) => Close();
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = AccRed;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = TextMuted;
            panelHeader.Controls.Add(btnClose);

            panelFilter = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Size = new Size(W, FILTER_H),
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
                Text = "Search…"
            };
            txtSearch.Enter += (s, e) =>
            {
                if (txtSearch.Text == "Search…")
                { txtSearch.Text = ""; txtSearch.ForeColor = TextWhite; }
            };
            txtSearch.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                { txtSearch.Text = "Search…"; txtSearch.ForeColor = TextMuted; }
            };
            txtSearch.TextChanged += (s, e) =>
            {
                _filterText = txtSearch.Text == "Search…" ? "" : txtSearch.Text.Trim();
                RenderList();
            };
            panelFilter.Controls.Add(txtSearch);

            panelFilter.Controls.Add(new Label
            {
                Text = "Status:",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(290, 14)
            });

            cmbStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(130, 28),
                Location = new Point(342, 10)
            };
            cmbStatus.Items.AddRange(new[] { "Open", "Converted", "All" });
            cmbStatus.SelectedIndex = 0;
            cmbStatus.SelectedIndexChanged += (s, e) =>
            {
                _statusFilter = cmbStatus.SelectedItem.ToString();
                LoadRows();
            };
            panelFilter.Controls.Add(cmbStatus);

            var btnNew = new Button
            {
                Text = "+ New Quotation",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(138, 28),
                Location = new Point(490, 10),
                Cursor = Cursors.Hand
            };
            btnNew.FlatAppearance.BorderSize = 0;
            btnNew.Click += (s, e) => OpenNewQuotationForm();
            panelFilter.Controls.Add(btnNew);

            panelList = new Panel
            {
                BackColor = BgDark,
                AutoScroll = true,
                Size = new Size(W, H - LIST_TOP),
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

        // ══════════════════════════════════════════════════════════════════
        //  DATA
        // ══════════════════════════════════════════════════════════════════
        private void LoadRows()
        {
            try
            {
                QuotationRepository.EnsureSchema();
                _rows = QuotationRepository.GetQuotations(
                    _companyId,
                    _statusFilter == "All" ? "" : _statusFilter);
            }
            catch (Exception ex)
            {
                _rows = new List<QuotationRow>();
                MessageBox.Show("Load error: " + ex.Message);
            }
            RenderList();
        }

        private void RenderList()
        {
            panelList.SuspendLayout();
            foreach (Control c in panelList.Controls) c.Dispose();
            panelList.Controls.Clear();

            var filtered = _rows.Where(r =>
                string.IsNullOrEmpty(_filterText)
                || r.QuotationNo.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
                || r.CustomerName.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            lblCount.Text = $"{filtered.Count} quotation(s)";

            if (filtered.Count == 0)
            {
                panelList.Controls.Add(new Label
                {
                    Text = "No quotations found.",
                    Font = new Font("Segoe UI", 11F),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(W, 80),
                    Location = new Point(0, 40),
                    TextAlign = ContentAlignment.MiddleCenter
                });
                panelList.ResumeLayout();
                return;
            }

            // Column header
            var hdr = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Size = new Size(W - 20, 30),
                Location = new Point(10, 4)
            };
            void Hdr(string t, int x, int w2,
                ContentAlignment a = ContentAlignment.MiddleLeft)
                => hdr.Controls.Add(new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(w2, 30),
                    Location = new Point(x, 0),
                    TextAlign = a
                });
            Hdr("QUO NO", 10, 140);
            Hdr("CUSTOMER", 158, 200);
            Hdr("DATE", 362, 120);
            Hdr("VALID UNTIL", 486, 110);
            Hdr("TOTAL", 600, 110, ContentAlignment.MiddleRight);
            Hdr("STATUS", 718, 90);
            panelList.Controls.Add(hdr);

            int y = 38;
            foreach (var row in filtered)
            {
                panelList.Controls.Add(BuildRow(row, y));
                y += ROW_GAP;
            }
            panelList.ResumeLayout();
        }

        private Panel BuildRow(QuotationRow row, int yOffset)
        {
            bool converted = row.Status == "Converted";
            var card = new Panel
            {
                BackColor = converted
                    ? Color.FromArgb(30, 38, 30)
                    : Color.FromArgb(38, 42, 52),
                Size = new Size(W - 20, CARD_H),
                Location = new Point(10, yOffset)
            };

            void L(string t, int x, int w, Font f, Color fc,
                ContentAlignment a = ContentAlignment.MiddleLeft)
                => card.Controls.Add(new Label
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

            var fN = new Font("Segoe UI", 9F);
            var fB = new Font("Segoe UI", 9F, FontStyle.Bold);

            L(row.QuotationNo, 10, 140, fB, AccOrange);
            L(row.CustomerName, 158, 200, fN, TextWhite);
            L(row.QuoteDate.ToString("dd MMM yyyy  HH:mm"), 362, 120, fN, TextMuted);
            L(row.ValidUntil, 486, 110, fN, TextMuted);
            L($"{row.CurrencySymbol} {row.GrandTotal:F2}", 600, 110,
                fB, TextGreen, ContentAlignment.MiddleRight);

            // Status badge
            Color sc = converted ? AccGreen : AccOrange;
            card.Controls.Add(new Label
            {
                Text = row.Status,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = sc,
                BackColor = Color.FromArgb(sc.R / 4, sc.G / 4, sc.B / 4),
                AutoSize = false,
                Size = new Size(80, 24),
                Location = new Point(718, (CARD_H - 24) / 2),
                TextAlign = ContentAlignment.MiddleCenter
            });

            if (converted)
            {
                L($"→ {row.ConvertedInvNo}", 810, 190,
                    new Font("Segoe UI", 8F), TextMuted);
            }
            else
            {
                var btnPrint = MakeSmlBtn("🖨 Print", AccBlue, new Point(810, (CARD_H - 28) / 2));
                var cap = row;
                btnPrint.Click += (s, e) => PrintRow(cap);
                card.Controls.Add(btnPrint);

                var btnConvert = MakeSmlBtn("💳 Convert", AccGreen, new Point(896, (CARD_H - 28) / 2));
                btnConvert.Size = new Size(88, 28);
                btnConvert.Click += (s, e) => ConvertToSale(cap);
                card.Controls.Add(btnConvert);

                var btnDel = MakeSmlBtn("🗑", AccRed, new Point(W - 68, (CARD_H - 28) / 2));
                btnDel.Size = new Size(32, 28);
                btnDel.Click += (s, e) =>
                {
                    if (MessageBox.Show($"Delete {cap.QuotationNo}?", "Confirm",
                        MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        QuotationRepository.DeleteQuotation(cap.QuotationNo);
                        LoadRows();
                    }
                };
                card.Controls.Add(btnDel);
            }

            card.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(W - 20, 1),
                Location = new Point(0, CARD_H - 1)
            });
            return card;
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRINT
        // ══════════════════════════════════════════════════════════════════
        private void PrintRow(QuotationRow row)
        {
            var dto = QuotationRepository.GetFull(row.QuotationNo);
            if (dto == null) return;
            var rd = QuotationPrintHelper.BuildReceiptData(dto,
                _companyName, _companyAddress, _companyPhone,
                _companyVat, _companyWebsite, _salesOfficeInfo);
            PrintReceiptDialog.Show(this, rd);
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONVERT TO SALE
        // ══════════════════════════════════════════════════════════════════
        private void ConvertToSale(QuotationRow row)
        {
            var dto = QuotationRepository.GetFull(row.QuotationNo);
            if (dto == null) return;

            string invNo = ShowPaymentDialog(dto);
            if (string.IsNullOrEmpty(invNo)) return;    // cancelled

            ShowConvertedInvoiceDialog(row.QuotationNo, invNo, dto);
            LoadRows();
        }

        private string ShowPaymentDialog(QuotationDto dto)
        {
            string sym = dto.CurrencySymbol;
            decimal grand = dto.GrandTotal;
            string result = "";

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(460, 400),
                ShowInTaskbar = false,
                KeyPreview = true
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            // Header
            var pHead = new Panel
            {
                BackColor = Color.FromArgb(42, 46, 58),
                Size = new Size(460, 50),
                Location = Point.Empty
            };
            pHead.Controls.Add(new Label
            {
                Text = $"💳  Convert to Sale — {dto.QuotationNo}",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(414, 50),
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var bx = MakeIconBtn("✕", new Point(414, 0), new Size(46, 50));
            bx.Click += (s, e) => dlg.Close();
            pHead.Controls.Add(bx);
            dlg.Controls.Add(pHead);

            dlg.Controls.Add(new Label
            {
                Text = $"{sym} {grand:F2}",
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 60),
                Location = new Point(0, 54),
                TextAlign = ContentAlignment.MiddleCenter
            });
            dlg.Controls.Add(new Label
            {
                Text = $"Customer: {dto.CustomerName}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 22),
                Location = new Point(0, 120),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // Payment method
            dlg.Controls.Add(new Label
            {
                Text = "Payment Method",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 152)
            });
            var cmbMethod = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(420, 30),
                Location = new Point(20, 172)
            };
            cmbMethod.Items.AddRange(new[] { "Cash", "Bank Transfer / EFT", "Card" });
            cmbMethod.SelectedIndex = 0;
            dlg.Controls.Add(cmbMethod);

            // Amount
            dlg.Controls.Add(new Label
            {
                Text = "Amount Received",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 215)
            });
            var tbAmount = new TextBox
            {
                Text = grand.ToString("F2"),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.FromArgb(38, 42, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(420, 36),
                Location = new Point(20, 235),
                TextAlign = HorizontalAlignment.Right
            };
            tbAmount.Enter += (s, e) => tbAmount.SelectAll();
            dlg.Controls.Add(tbAmount);

            var lblChange = new Label
            {
                Text = $"Change: {sym} 0.00",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = AccGreen,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 280)
            };
            tbAmount.TextChanged += (s, e) =>
            {
                if (decimal.TryParse(tbAmount.Text, out decimal paid))
                {
                    decimal chg = paid - grand;
                    lblChange.Text = chg >= 0
                        ? $"Change: {sym} {chg:F2}"
                        : $"Still need: {sym} {Math.Abs(chg):F2}";
                    lblChange.ForeColor = chg >= 0 ? AccGreen : AccRed;
                }
            };
            dlg.Controls.Add(lblChange);

            var lblSt = new Label
            {
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = AccRed,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(20, 305)
            };
            dlg.Controls.Add(lblSt);

            var btnConfirm = new Button
            {
                Text = "✓  Confirm & Generate Invoice",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(420, 46),
                Location = new Point(20, 334),
                Cursor = Cursors.Hand
            };
            btnConfirm.FlatAppearance.BorderSize = 0;
            btnConfirm.Region = MakeRoundedRegion(btnConfirm.Size, 8);

            btnConfirm.Click += (s, e) =>
            {
                if (!decimal.TryParse(tbAmount.Text.Trim(), out decimal paid)
                    || paid < grand)
                {
                    lblSt.Text = paid < grand
                        ? $"Amount too low. Need {sym} {grand - paid:F2} more."
                        : "Invalid amount.";
                    return;
                }

                decimal cash = 0, digital = 0, card = 0;
                switch (cmbMethod.SelectedIndex)
                {
                    case 0: cash = paid; break;
                    case 1: digital = paid; break;
                    case 2: card = paid; break;
                }

                try
                {
                    btnConfirm.Enabled = false;
                    lblSt.Text = "Saving…";
                    lblSt.ForeColor = TextMuted;

                    // ConvertToSale now handles ConsumeInvoiceNo internally — no double-call
                    result = QuotationRepository.ConvertToSale(
                        dto.QuotationNo, _companyId,
                        cash, digital, card,
                        _cashierName, sym,
                        _companyName, _companyAddress, _companyPhone,
                        _companyVat, _companyWebsite, _salesOfficeInfo);

                    dlg.Close();
                }
                catch (Exception ex)
                {
                    lblSt.Text = "Error: " + ex.Message;
                    lblSt.ForeColor = AccRed;
                    btnConfirm.Enabled = true;
                }
            };
            dlg.Controls.Add(btnConfirm);

            dlg.ClientSize = new Size(460, 396);
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) btnConfirm.PerformClick();
                if (e.KeyCode == Keys.Escape) dlg.Close();
            };
            dlg.Shown += (s, e) => { tbAmount.SelectAll(); tbAmount.Focus(); };
            dlg.ShowDialog(this);

            return result;
        }

        private void ShowConvertedInvoiceDialog(string quotationNo,
            string invNo, QuotationDto dto)
        {
            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(28, 32, 42),
                ClientSize = new Size(460, 290),
                ShowInTaskbar = false,
                KeyPreview = true
            };
            dlg.Region = MakeRoundedRegion(dlg.Size, 12);

            dlg.Controls.Add(new Label
            {
                Text = "✅  Sale Confirmed!",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = AccGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 40),
                Location = new Point(0, 20),
                TextAlign = ContentAlignment.MiddleCenter
            });
            dlg.Controls.Add(new Label
            {
                Text = $"Invoice: {invNo}",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 40),
                Location = new Point(0, 68),
                TextAlign = ContentAlignment.MiddleCenter
            });
            dlg.Controls.Add(new Label
            {
                Text = $"Converted from: {quotationNo}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 24),
                Location = new Point(0, 114),
                TextAlign = ContentAlignment.MiddleCenter
            });
            dlg.Controls.Add(new Label
            {
                Text = $"Customer: {dto.CustomerName}    Total: {dto.CurrencySymbol} {dto.GrandTotal:F2}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 24),
                Location = new Point(0, 140),
                TextAlign = ContentAlignment.MiddleCenter
            });

            var btnPrint = new Button
            {
                Text = "🖨  Print Invoice",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(200, 44),
                Location = new Point(20, 190),
                Cursor = Cursors.Hand
            };
            btnPrint.FlatAppearance.BorderSize = 0;
            btnPrint.Region = MakeRoundedRegion(btnPrint.Size, 8);
            btnPrint.Click += (s, e) =>
            {
                // Reload the now-converted DTO (has the real INV number stored)
                var freshDto = QuotationRepository.GetFull(quotationNo);
                if (freshDto == null) return;

                var rd = QuotationPrintHelper.BuildReceiptData(freshDto,
                    _companyName, _companyAddress, _companyPhone,
                    _companyVat, _companyWebsite, _salesOfficeInfo);
                rd.InvoiceNo = invNo;     // use the real INV number
                rd.FooterLine1 = "Thank you for your business!";
                rd.FooterLine2 = "";
                dlg.Close();
                PrintReceiptDialog.Show(this, rd);
            };

            var btnDone = new Button
            {
                Text = "✓  Done",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(200, 44),
                Location = new Point(238, 190),
                Cursor = Cursors.Hand
            };
            btnDone.FlatAppearance.BorderSize = 0;
            btnDone.Region = MakeRoundedRegion(btnDone.Size, 8);
            btnDone.Click += (s, e) => dlg.Close();

            dlg.Controls.AddRange(new Control[] { btnPrint, btnDone });
            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) dlg.Close();
            };
            dlg.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════
        //  NEW QUOTATION
        // ══════════════════════════════════════════════════════════════════
        private void OpenNewQuotationForm()
        {
            var f = new QuotationForm(
                _companyId, _currencySymbol,
                _companyName, _companyVat,
                _companyAddress, _companyPhone,
                _companyWebsite, _salesOfficeInfo);
            f.FormClosed += (s, e) => LoadRows();
            f.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
        private Button MakeIconBtn(string text, Point loc, Size sz)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = sz,
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private Button MakeSmlBtn(string text, Color bg, Point loc)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(78, 28),
                Location = loc,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = MakeRoundedRegion(b.Size, 5);
            return b;
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