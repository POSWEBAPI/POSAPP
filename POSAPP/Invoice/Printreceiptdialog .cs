// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  PrintReceiptDialog.cs — Thermal / A4 / PDF receipt for ShriPOS         ║
// ║  UPDATED: A4 layout matches FLO-TEK invoice PDF exactly                 ║
// ║           - Logo | Company info | Multiple Sales Offices (right col)    ║
// ║           - Customer box | Tax Invoice box (NO driver box)              ║
// ║           - Sales Order info row                                        ║
// ║           - Items table                                                 ║
// ║           - Received By / Signature / Date footer (always at bottom)   ║
// ║           - Totals block alongside footer                               ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using POSAPP.Printer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace POSAPP.Invoice
{
    // ══════════════════════════════════════════════════════════════════════════
    //  ReceiptData
    // ══════════════════════════════════════════════════════════════════════════
    public class ReceiptData
    {
        public string CompanyName { get; set; } = "ABC";
        public string CompanyAddress { get; set; } = "";
        public string CompanyPhone { get; set; } = "";
        public string InvoiceNo { get; set; } = "";
        public DateTime SaleDate { get; set; } = DateTime.Now;
        public string CustomerName { get; set; } = "";
        public string CustomerAddress { get; set; } = "";
        public string CashierName { get; set; } = "ADMIN";
        public string CurrencySymbol { get; set; } = "P";

        public List<ReceiptLine> Lines { get; set; } = new();
        public decimal Subtotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal GrandTotal { get; set; }

        public decimal PaidCash { get; set; }
        public decimal PaidDigital { get; set; }
        public string DigitalMethodName { get; set; } = "";
        public decimal PaidCard { get; set; }
        public decimal Change { get; set; }

        public string FooterLine1 { get; set; } = "Thank you for your purchase!";
        public string FooterLine2 { get; set; } = "";

        public string CompanyVat { get; set; } = "";
        public string CompanyWebsite { get; set; } = "";


        /// <summary>     public bool IsQuotation { get; set; } = false;
        /// Pipe-delimited list of sales-office blocks.
        /// Each block: "Office Name|Phone: …|Fax: …"
        /// Separate multiple offices with double-pipe "||"
        /// e.g. "Gaborone Sales office|Phone: +267 3972001/3/4|Fax: +267 3872014||Phakalane Sales Office|Phone: +267 3972001|Fax: +267 3872014"
        /// </summary>
        public string SalesOfficeInfo { get; set; } = "";

        public string SalesOrderNo { get; set; } = "";
        public string SalespersonName { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string DriverPhone { get; set; } = "";
        public string VehicleNo { get; set; } = "";
        public string ClearanceDoc { get; set; } = "";
        public string DeliveryRef { get; set; } = "";
        public string CustomerPONo { get; set; } = "";
        public decimal LineDiscount { get; set; }
        public decimal HeaderDiscount { get; set; }
        public decimal FreightCharges { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerVat { get; set; } = "";

        public   bool IsQuotation { get; set; } = false;
        public bool IsReprint { get; set; } = false;
        public string CardRefNumber { get; set; }
    }

    public class ReceiptLine
    {
        public string StockCode { get; set; } = "";
        public string Name { get; set; } = "";
        public string UOM { get; set; } = "EA";
        public decimal Qty { get; set; }
        public decimal QtyRequested { get; set; }
        public decimal QtyDispatched { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal ListPrice { get; set; }
        public decimal DiscountPct { get; set; }
        public decimal LineTotal { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PrintReceiptDialog
    // ══════════════════════════════════════════════════════════════════════════
    public static class PrintReceiptDialog
    {
        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color Panel2 = Color.FromArgb(42, 46, 56);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);



        private const int RECEIPT_CHARS = 42;
        private const float FULLSCR_FONT = 14f;
        // Add this to PrintReceiptDialog class (near the top, after palette fields):
        public static bool LastPrintWasSuccessful { get; private set; } = false;
        public static bool IsQuotation { get; set; } = false;
        // ──────────────────────────────────────────────────────────────────────
        public static void Show(Form owner, ReceiptData data)
        {
            LastPrintWasSuccessful = false;
            PrinterPreference.Load();

            var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BgDark,
                ClientSize = new Size(820, 660),
                ShowInTaskbar = false,
                KeyPreview = true,
                Text = "Print Receipt"
            };
            dlg.Region = MakeRound(dlg.Size, 14);

            bool dragging = false;
            Point dragStart = Point.Empty;

            // ── Header bar ────────────────────────────────────────────────────
            var pHead = new Panel { BackColor = Panel2, Size = new Size(820, 52), Location = Point.Empty };
            pHead.MouseDown += (s, e) => { dragging = true; dragStart = e.Location; };
            pHead.MouseMove += (s, e) => { if (dragging) { var p = dlg.Location; p.Offset(e.X - dragStart.X, e.Y - dragStart.Y); dlg.Location = p; } };
            pHead.MouseUp += (s, e) => dragging = false;
            pHead.Controls.Add(new Label
            {
                Text = "🖨️  Print Receipt — " + data.InvoiceNo,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(700, 52),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var btnX = MakeBtn("✕", TextMuted, Color.Transparent, new Point(776, 0), new Size(44, 52));
            btnX.Click += (s, e) => dlg.Close();
            pHead.Controls.Add(btnX);

            // ── A4 Preview panel ──────────────────────────────────────────────
            var pPreviewOuter = new Panel
            {
                BackColor = Color.FromArgb(30, 34, 43),
                Size = new Size(340, 556),
                Location = new Point(16, 58),
                Padding = new Padding(1)
            };
            pPreviewOuter.Region = MakeRound(pPreviewOuter.Size, 6);

            var pA4Preview = new Panel
            {
                BackColor = Color.FromArgb(80, 84, 96),
                Size = new Size(338, 524),
                Location = new Point(1, 31),
                AutoScroll = true,
                Visible = true,
                Cursor = Cursors.Hand
            };
            var pbA4 = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(80, 84, 96),
                Cursor = Cursors.Hand
            };
            pA4Preview.Controls.Add(pbA4);

            // Render immediately
            try
            {
                int bw = 794, bh = 1123;
                var bmp = new Bitmap(bw, bh);
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.White);
                DrawA4Receipt(g, data, new Rectangle(0, 0, bw, bh), 96f);
                pbA4.Image = bmp;
            }
            catch { }

            // Click preview → fullscreen
            EventHandler openA4Fullscreen = (s, e) =>
            {
                var full = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    WindowState = FormWindowState.Maximized,
                    BackColor = Color.FromArgb(60, 63, 75),
                    ShowInTaskbar = false,
                    KeyPreview = true
                };
                var pBar = new Panel { BackColor = Color.FromArgb(32, 35, 44), Dock = DockStyle.Top, Height = 44 };
                pBar.Controls.Add(new Label
                {
                    Text = "📃  A4 Receipt Preview   —   ESC to close",
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(12, 0, 0, 0)
                });
                var btnCF2 = new Button
                {
                    Text = "✕",
                    Dock = DockStyle.Right,
                    Size = new Size(44, 44),
                    BackColor = Color.FromArgb(32, 35, 44),
                    ForeColor = Color.FromArgb(130, 140, 158),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    FlatAppearance = { BorderSize = 0 }
                };
                btnCF2.Click += (s2, e2) => full.Close();
                pBar.Controls.Add(btnCF2);

                var bmpFull = new Bitmap(794, 1123);
                using (var gBmp = Graphics.FromImage(bmpFull))
                {
                    gBmp.SmoothingMode = SmoothingMode.AntiAlias;
                    gBmp.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    gBmp.Clear(Color.White);
                    DrawA4Receipt(gBmp, data, new Rectangle(0, 0, 794, 1123), 96f);
                }

                var pScroll = new Panel { BackColor = Color.FromArgb(60, 63, 75), Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 8, 0, 8) };
                var scr = Screen.PrimaryScreen.Bounds;
                int availH = scr.Height - 44 - 16;
                int sheetH = availH;
                int sheetW = (int)(sheetH * 794.0 / 1123.0);
                if (sheetW > (int)(scr.Width * 0.80)) { sheetW = (int)(scr.Width * 0.80); sheetH = (int)(sheetW * 1123.0 / 794.0); }

                var pbFull = new PictureBox
                {
                    Image = bmpFull,
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Width = sheetW,
                    Height = sheetH,
                    BackColor = Color.White,
                    Cursor = Cursors.Default
                };
                pbFull.Location = new Point((scr.Width - sheetW) / 2, 8);
                pScroll.Controls.Add(pbFull);
                pScroll.Resize += (s2, e2) => { int cx = Math.Max(0, (pScroll.ClientSize.Width - sheetW) / 2); pbFull.Left = cx; };

                full.Controls.Add(pScroll);
                full.Controls.Add(pBar);
                pBar.BringToFront();
                full.KeyDown += (s2, e2) => { if (e2.KeyCode == Keys.Escape) full.Close(); };
                full.ShowDialog(dlg);
            };
            pbA4.Click += openA4Fullscreen;
            pA4Preview.Click += openA4Fullscreen;

            pPreviewOuter.Controls.Add(pA4Preview);

            // ── Right info panel ──────────────────────────────────────────────
            var pInfo = new Panel { BackColor = Color.Transparent, Size = new Size(436, 556), Location = new Point(372, 58) };
            int iy = 0;
            Label InfoLabel(string t, bool big = false)
            {
                var l = new Label
                {
                    Text = t,
                    Font = new Font("Segoe UI", big ? 10F : 8F, big ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = big ? TextWhite : TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(436, big ? 28 : 22),
                    Location = new Point(0, iy),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                iy += l.Height + 4;
                return l;
            }
            Panel HSep() { var p = new Panel { BackColor = Border, Size = new Size(436, 1), Location = new Point(0, iy) }; iy += 14; return p; }

            string printerName = string.IsNullOrWhiteSpace(PrinterPreference.A4PrinterName)
                ? "(No printer configured)"
                : PrinterPreference.A4PrinterName;

            iy = 10;
            pInfo.Controls.Add(InfoLabel("CONFIGURED PRINTER", false));
            pInfo.Controls.Add(InfoLabel("📄  STANDARD / A4 PRINTER", true));
            pInfo.Controls.Add(InfoLabel(printerName, true));
            pInfo.Controls.Add(InfoLabel("GDI print document", false));
            pInfo.Controls.Add(HSep());

            pInfo.Controls.Add(new Label
            {
                Text = "Click  ✔ OK — Print  to send this bill\nto the printer shown above.\n\n" +
                       "To change the printer, open\nPrinter Settings from the dashboard.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(436, 110),
                Location = new Point(0, iy),
                TextAlign = ContentAlignment.TopLeft
            });
            iy += 118;
            pInfo.Controls.Add(HSep());

            // ── Bottom action bar ─────────────────────────────────────────────
            var pBottom = new Panel { BackColor = Panel2, Size = new Size(820, 52), Location = new Point(0, 608) };
            var lblBadge = new Label
            {
                Text = $"Printer: {printerName}",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(460, 52),
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pBottom.Controls.Add(lblBadge);

            var btnCancel = MakeBtn("✕  Cancel", Color.FromArgb(55, 60, 78), TextMuted, new Point(598, 8), new Size(100, 36));
            btnCancel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            btnCancel.Click += (s, e) => dlg.Close();
            pBottom.Controls.Add(btnCancel);

            var btnOk = MakeBtn("✔  OK — Print", AccGreen, Color.White, new Point(708, 8), new Size(100, 36));
            btnOk.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            pBottom.Controls.Add(btnOk);

            btnOk.Click += async (s, e) =>
            {
                btnOk.Enabled = false;
                btnCancel.Enabled = false;

                string pn = PrinterPreference.A4PrinterName?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(pn))
                {
                    lblBadge.Text = "✗ No A4 printer configured.";
                    lblBadge.ForeColor = AccRed;
                    btnOk.Enabled = true;
                    btnCancel.Enabled = true;
                    return;
                }

                lblBadge.Text = $"Printing on {pn}…";
                var (ok, msg) = await Task.Run(() => PrintA4(data, pn, dlg));
                if (ok)
                {
                    LastPrintWasSuccessful = true;   // ← mark printed
                    lblBadge.Text = $"✓ {msg}";
                    lblBadge.ForeColor = TextGreen;
                    await Task.Delay(1200);
                    dlg.Invoke(new Action(() => dlg.Close()));
                }
                else
                {
                    LastPrintWasSuccessful = false;
                    lblBadge.Text = $"✗ {msg}";
                    lblBadge.ForeColor = AccRed;
                    btnOk.Enabled = true;
                    btnCancel.Enabled = true;
                }
            };

            dlg.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) dlg.Close();
                if (e.KeyCode == Keys.Enter && btnOk.Enabled) btnOk.PerformClick();
            };

            dlg.Controls.AddRange(new Control[] { pHead, pPreviewOuter, pInfo, pBottom });
            pHead.BringToFront();
            pBottom.BringToFront();
            dlg.ShowDialog(owner);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helper — largest font that fits N chars inside maxPx
        // ══════════════════════════════════════════════════════════════════════
        private static float CalculateFittingFontSize(string fontFamily, int charCount, int maxPx)
        {
            float lo = 4f, hi = 30f, best = 7f;
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) / 2f;
                using var f = new Font(fontFamily, mid);
                int w = TextRenderer.MeasureText(new string('W', charCount), f).Width;
                if (w <= maxPx) { best = mid; lo = mid; } else hi = mid;
            }
            return best;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PREVIEW TEXT — plain-text 42-char receipt
        // ══════════════════════════════════════════════════════════════════════
        private static string BuildPreviewText(ReceiptData d)
        {
            const int W = RECEIPT_CHARS;
            var sb = new StringBuilder();
            void Line(string s = "") => sb.AppendLine(s);
            void Dashes() => Line(new string('-', W));
            void Equals() => Line(new string('=', W));
            string Ctr(string s) { if (s.Length >= W) return s; int pad = (W - s.Length) / 2; return s.PadLeft(s.Length + pad).PadRight(W); }
            string LR(string l, string r) { int sp = W - l.Length - r.Length; return l + new string(' ', Math.Max(1, sp)) + r; }
            string Sym(decimal v) => $"{d.CurrencySymbol} {v:F2}";

            string TableRow(string item, string qty, string price, string total)
            {
                string c1 = item.Length > 19 ? item[..19] : item.PadRight(19);
                return $"{c1}|{qty.PadLeft(4)}|{price.PadLeft(8)}|{total.PadLeft(8)}";
            }
            void TableDash() => Line(new string('-', 19) + "+" + new string('-', 4) + "+" + new string('-', 8) + "+" + new string('-', 8));

            Equals();
            Line(Ctr(d.CompanyName));
            if (!string.IsNullOrWhiteSpace(d.CompanyAddress)) Line(Ctr(d.CompanyAddress));
            if (!string.IsNullOrWhiteSpace(d.CompanyPhone)) Line(Ctr("Tel: " + d.CompanyPhone));
            Equals();
            Line();
            Line(LR("Invoice : " + d.InvoiceNo, d.SaleDate.ToString("dd/MM/yy HH:mm")));
            Line("Cashier : " + d.CashierName);
            Line("Customer: " + (string.IsNullOrWhiteSpace(d.CustomerName) ? "Walk-in" : d.CustomerName));
            Line();
            TableDash();
            Line(TableRow("Item", "Qty", "Price", "Total"));
            TableDash();
            foreach (var li in d.Lines)
            {
                string name = li.Name.Length > 19 ? li.Name[..18] + "." : li.Name;
                Line(TableRow(name, li.Qty.ToString(), li.UnitPrice.ToString("F2"), li.LineTotal.ToString("F2")));
                if (li.DiscountPct > 0) Line(TableRow($"  Disc {li.DiscountPct:F0}%", "", "", ""));
            }
            TableDash();
            Line(LR("Subtotal :", Sym(d.Subtotal)));
            if (d.DiscountTotal > 0) Line(LR("Discount :", "- " + Sym(d.DiscountTotal)));
            if (d.TaxTotal > 0) Line(LR("Tax :", Sym(d.TaxTotal)));
            Dashes();
            Line(LR("*** TOTAL ***", Sym(d.GrandTotal)));
            Dashes();
            if (d.PaidCash > 0) Line(LR("Cash :", Sym(d.PaidCash)));
            if (d.PaidDigital > 0) Line(LR((string.IsNullOrEmpty(d.DigitalMethodName) ? "Digital" : d.DigitalMethodName) + " :", Sym(d.PaidDigital)));
            if (d.PaidCard > 0) Line(LR("Card :", Sym(d.PaidCard)));
            if (d.Change > 0) Line(LR("Change :", Sym(d.Change)));
            Dashes();
            Line();
            if (!string.IsNullOrWhiteSpace(d.FooterLine1)) Line(Ctr(d.FooterLine1));
            if (!string.IsNullOrWhiteSpace(d.FooterLine2)) Line(Ctr(d.FooterLine2));
            Line();
            Equals();
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  THERMAL RAW ESC/POS
        // ══════════════════════════════════════════════════════════════════════
        private static (bool success, string message) PrintThermalRaw(ReceiptData data, string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return (false, "Please select a printer");
            try
            {
                byte[] bytes = Encoding.GetEncoding(1252).GetBytes(BuildThermalReceiptText(data));
                bool result = RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
                return result ? (true, "✅ Thermal Receipt Printed Successfully!") : (false, "❌ Failed to print.");
            }
            catch (Exception ex) { return (false, "Print Error: " + ex.Message); }
        }

        private static string BuildThermalReceiptText(ReceiptData d)
        {
            const int W = RECEIPT_CHARS;
            var sb = new StringBuilder();
            string Ctr(string s) { if (s.Length >= W) return s; int pad = (W - s.Length) / 2; return s.PadLeft(s.Length + pad).PadRight(W); }
            void Center(string s) => sb.AppendLine(Ctr(s));
            void Line(string l = "", string r = "") { if (string.IsNullOrEmpty(r)) { sb.AppendLine(l.PadRight(W)); return; } int sp = W - l.Length - r.Length; sb.AppendLine(l + new string(' ', Math.Max(0, sp)) + r); }
            void Dash() => sb.AppendLine(new string('-', W));

            Center("================================");
            Center(d.CompanyName.ToUpper());
            if (!string.IsNullOrWhiteSpace(d.CompanyAddress)) Center(d.CompanyAddress);
            if (!string.IsNullOrWhiteSpace(d.CompanyPhone)) Center("Ph: " + d.CompanyPhone);
            Center("================================");
            sb.AppendLine();
            Line("Invoice :", d.InvoiceNo);
            Line("Date    :", d.SaleDate.ToString("dd/MM/yyyy HH:mm"));
            Line("Customer:", string.IsNullOrWhiteSpace(d.CustomerName) ? "Walk-in" : d.CustomerName);
            Line("Cashier :", d.CashierName);
            Dash();
            Line("Item", "Qty   Price   Total");
            Dash();
            foreach (var item in d.Lines)
            {
                string name = item.Name.Length > 22 ? item.Name[..19] + ".." : item.Name;
                Line(name, $"{item.Qty}  {d.CurrencySymbol}{item.UnitPrice:F0}  {d.CurrencySymbol}{item.LineTotal:F0}");
            }
            Dash();
            sb.AppendLine();
            Line("Subtotal :", $"{d.CurrencySymbol}{d.Subtotal:F2}");
            if (d.DiscountTotal > 0) Line("Discount :", $"-{d.CurrencySymbol}{d.DiscountTotal:F2}");
            Line("Tax      :", $"{d.CurrencySymbol}{d.TaxTotal:F2}");
            Dash();
            Line("GRAND TOTAL", $"{d.CurrencySymbol}{d.GrandTotal:F2}");
            Dash();
            sb.AppendLine();
            Center("Thank You!");
            Center("Visit Again");
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  THERMAL TCP/IP
        // ══════════════════════════════════════════════════════════════════════
        private static (bool ok, string msg) PrintThermalNetwork(ReceiptData data, string ip, int port)
        {
            try
            {
                byte[] bytes = BuildEscPos(data);
                using var client = new TcpClient();
                var ar = client.BeginConnect(ip, port, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(4));
                if (!ok) return (false, $"Could not connect to {ip}:{port} (timeout).");
                client.EndConnect(ar);
                using var stream = client.GetStream();
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ESC/POS byte builder
        // ══════════════════════════════════════════════════════════════════════
        private static byte[] BuildEscPos(ReceiptData d)
        {
            const int W = RECEIPT_CHARS;
            var ms = new MemoryStream();
            void Raw(params byte[] b) { ms.Write(b, 0, b.Length); }
            void Txt(string s) { ms.Write(Encoding.ASCII.GetBytes(s), 0, Encoding.ASCII.GetByteCount(s)); }
            void Line(string s = "") { Txt(s + "\n"); }
            void Dashes() { Line(new string('-', W)); }
            string Ctr(string s) { if (s.Length >= W) return s; int pad = (W - s.Length) / 2; return s.PadLeft(s.Length + pad).PadRight(W); }
            string LR(string l, string r) { int sp = W - l.Length - r.Length; return l + new string(' ', Math.Max(1, sp)) + r; }
            string Sym(decimal v) => $"{d.CurrencySymbol} {v:F2}";

            Raw(0x1B, 0x40);
            Raw(0x1B, 0x21, 0x38); Line(Ctr(d.CompanyName)); Raw(0x1B, 0x21, 0x00);
            if (!string.IsNullOrWhiteSpace(d.CompanyAddress)) Line(Ctr(d.CompanyAddress));
            if (!string.IsNullOrWhiteSpace(d.CompanyPhone)) Line(Ctr("Tel: " + d.CompanyPhone));
            Dashes();
            Line(LR("Invoice : " + d.InvoiceNo, d.SaleDate.ToString("dd/MM/yy HH:mm")));
            Line("Cashier : " + d.CashierName);
            Line("Customer: " + (string.IsNullOrWhiteSpace(d.CustomerName) ? "Walk-in" : d.CustomerName));
            Dashes();
            Raw(0x1B, 0x45, 0x01);
            Line(string.Format("{0,-20}{1,4}{2,9}{3,9}", "Item", "Qty", "Price", "Total"));
            Raw(0x1B, 0x45, 0x00);
            Dashes();
            foreach (var li in d.Lines)
            {
                string name = (li.Name.Length > 20 ? li.Name[..19] + "." : li.Name).PadRight(20);
                Line(name + li.Qty.ToString().PadLeft(4) + li.UnitPrice.ToString("F2").PadLeft(9) + li.LineTotal.ToString("F2").PadLeft(9));
                if (li.DiscountPct > 0) Line("  " + $"({li.DiscountPct:F0}% discount)");
            }
            Dashes();
            Line(LR("Subtotal :", Sym(d.Subtotal)));
            if (d.DiscountTotal > 0) Line(LR("Discount :", "- " + Sym(d.DiscountTotal)));
            if (d.TaxTotal > 0) Line(LR("Tax :", Sym(d.TaxTotal)));
            Dashes();
            Raw(0x1B, 0x21, 0x18); Line(LR("TOTAL :", Sym(d.GrandTotal))); Raw(0x1B, 0x21, 0x00);
            Dashes();
            if (d.PaidCash > 0) Line(LR("Cash :", Sym(d.PaidCash)));
            if (d.PaidDigital > 0) Line(LR((string.IsNullOrEmpty(d.DigitalMethodName) ? "Digital" : d.DigitalMethodName) + " :", Sym(d.PaidDigital)));
            if (d.PaidCard > 0) Line(LR("Card :", Sym(d.PaidCard)));
            if (d.Change > 0) { Raw(0x1B, 0x45, 0x01); Line(LR("Change :", Sym(d.Change))); Raw(0x1B, 0x45, 0x00); }
            Dashes();
            Line();
            if (!string.IsNullOrWhiteSpace(d.FooterLine1)) Line(Ctr(d.FooterLine1));
            if (!string.IsNullOrWhiteSpace(d.FooterLine2)) Line(Ctr(d.FooterLine2));
            Line();
            Raw(0x1B, 0x64, 0x05);
            Raw(0x1D, 0x56, 0x41, 0x03);
            return ms.ToArray();
        }
        private static GraphicsPath GetRoundedRect(RectangleF rect, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);

            path.CloseFigure();
            return path;
        }

        private static void DrawA4Receipt(Graphics g, ReceiptData d, Rectangle bounds, float dpi)
        {
            float sc = bounds.Width / 794f;
            float bx = bounds.X, by = bounds.Y, bw = bounds.Width;
            string sym = string.IsNullOrWhiteSpace(d.CurrencySymbol) ? "Rs" : d.CurrencySymbol;

            var fTitle = new Font("Arial", 12f * sc, FontStyle.Bold);
            var fBold = new Font("Arial", 8.8f * sc, FontStyle.Bold);
            var fNorm = new Font("Arial", 8f * sc);
            var fSmall = new Font("Arial", 7.6f * sc);
            var fTiny = new Font("Arial", 6.8f * sc);
            var fInvNo = new Font("Arial", 11.5f * sc, FontStyle.Bold);

            float lhB = fBold.GetHeight(g);
            float lhS = fSmall.GetHeight(g);
            float lhT = fTiny.GetHeight(g);

            Brush bkBlack = Brushes.Black;
            Brush bkGray = Brushes.Gray;
            var penBlk = new Pen(Color.Black, 0.8f * sc);
            var penThk = new Pen(Color.Black, 1.4f * sc);

            var cFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var lFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            var rFmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var lTopFmt = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.LineLimit
            };

            float margin = 22f * sc;
            float y = by + 22f * sc;
            float fullW = bw - margin * 2f;
            float left = bx + margin;

            // SECTION 1 — Header
            float logoW = fullW * 0.23f;
            float compW = fullW * 0.42f;
            float invW = fullW * 0.35f;

            float colComp = left + logoW;
            float colInv = left + logoW + compW;

            int compLines = 1 + (string.IsNullOrWhiteSpace(d.CompanyAddress) ? 0 : 1)
                              + (string.IsNullOrWhiteSpace(d.CompanyPhone) ? 0 : 1)
                              + (string.IsNullOrWhiteSpace(d.CompanyVat) ? 0 : 1)
                              + (string.IsNullOrWhiteSpace(d.CompanyWebsite) ? 0 : 1);

            float headerH = Math.Max(98f * sc, compLines * (lhS + 4f * sc) + 22f * sc);

            g.DrawRectangle(penThk, left, y, fullW, headerH);
            g.DrawLine(penBlk, colComp, y, colComp, y + headerH);
            g.DrawLine(penBlk, colInv, y, colInv, y + headerH);

            // Logo — checks every filename/location this app has historically used
    //        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    //        string[] logoCandidates =
    //        {
    //    //Path.Combine(baseDir, "logo.jpg"),
    //    //Path.Combine(baseDir, "logo.jpeg"),
    //    //Path.Combine(baseDir, "logo.png"),
    //    //Path.Combine(baseDir, "flo.jpg"),
    //    //Path.Combine(baseDir, "flo.png"),
    //    //Path.Combine(baseDir, "Resources", "logo.jpg"),
    //    //Path.Combine(baseDir, "Resources", "logo.jpeg"),
    //    //Path.Combine(baseDir, "Resources", "logo.png"),
    //    //Path.Combine(baseDir, "Resources", "flo.jpg"),
    //    //Path.Combine(baseDir, "Resources", "flo.png"),
    //};
    //        string logoPath = logoCandidates.FirstOrDefault(File.Exists);

    //        if (string.IsNullOrEmpty(logoPath))
    //        {
    //            try
    //            {
    //                logoPath = Directory.GetFiles(baseDir, "logo.*").FirstOrDefault();
    //            }
    //            catch { /* ignore — directory scan failure just falls through to text fallback */ }
    //        }

            //if (!string.IsNullOrEmpty(logoPath))
            //{
            //    try
            //    {
            //        using var logo = Image.FromFile(logoPath);
            //        float pad = 6f * sc;
            //        float maxW = logoW - pad * 2, maxH = headerH - pad * 2;
            //        float ratio = Math.Min(maxW / logo.Width, maxH / logo.Height);
            //        float lw = logo.Width * ratio, lh = logo.Height * ratio;
            //        g.DrawImage(logo, left + (logoW - lw) / 2f, y + (headerH - lh) / 2f, lw, lh);
            //    }
            //    catch { DrawFallbackLogoText(g, d.CompanyName, fTitle, left, y, logoW, headerH, cFmt); }
            //}
            //else DrawFallbackLogoText(g, d.CompanyName, fTitle, left, y, logoW, headerH, cFmt);

            // Company info
            float cy = y + 8f * sc;
            void CompLine(string txt, Font fnt, bool bold = false)
            {
                if (string.IsNullOrWhiteSpace(txt)) return;
                g.DrawString(txt, bold ? fBold : fnt, bkBlack,
                    new RectangleF(colComp + 6 * sc, cy, compW - 12 * sc, fnt.GetHeight(g) + 2), lFmt);
                cy += fnt.GetHeight(g) + 4f * sc;
            }

            CompLine(d.CompanyName, fBold, true);
            CompLine(d.CompanyAddress, fSmall);
            if (!string.IsNullOrWhiteSpace(d.CompanyPhone)) CompLine("Tel: " + d.CompanyPhone, fSmall);
            if (!string.IsNullOrWhiteSpace(d.CompanyVat)) CompLine("VAT: " + d.CompanyVat, fSmall);
            if (!string.IsNullOrWhiteSpace(d.CompanyWebsite)) CompLine("Website: " + d.CompanyWebsite, fSmall);

            // Invoice details
            float titleH = lhB + 10f * sc;
            g.FillRectangle(new SolidBrush(Color.FromArgb(50, 50, 50)), colInv, y, invW, titleH);
            g.DrawString("TAX INVOICE", fBold, Brushes.White,
                new RectangleF(colInv, y, invW, titleH), cFmt);

            float iy2 = y + titleH + 6f * sc;
            void InvRow(string lbl, string val)
            {
                if (string.IsNullOrWhiteSpace(val)) return;
                float rh = lhS + 2f * sc;
                g.DrawString(lbl, fTiny, bkGray, new RectangleF(colInv + 6 * sc, iy2, invW * 0.42f, rh), lFmt);
                g.DrawString(val, fSmall, bkBlack, new RectangleF(colInv + invW * 0.44f, iy2, invW * 0.54f, rh), lFmt);
                iy2 += rh;
            }

            InvRow("Invoice No :", d.InvoiceNo ?? "");
            InvRow("Date :", d.SaleDate.ToString("dd-MM-yyyy"));
            InvRow("Time :", d.SaleDate.ToString("HH:mm:ss"));

            y += headerH + 12f * sc;

            // SECTION 2 — BILL TO
            float billH = lhB + 8f * sc;
            g.DrawRectangle(penBlk, left, y, fullW, billH);
            g.FillRectangle(new SolidBrush(Color.White), left, y, fullW, billH);
            g.DrawString("BILL TO", fBold, Brushes.Black,
                new RectangleF(left + 8 * sc, y + 2 * sc, fullW - 16 * sc, billH - 4 * sc), lFmt);

            float by2 = y + billH + 6f * sc;
            string customer = !string.IsNullOrWhiteSpace(d.CustomerName) ? d.CustomerName : "Walk-in Customer";
            g.DrawString(customer, fSmall, bkBlack,
                new RectangleF(left + 8 * sc, by2, fullW - 16 * sc, lhS + 4), lFmt);

            y += billH + 38f * sc;

            // SECTION 3 — Items Table
            float[] iPcts = { 0.05f, 0.12f, 0.30f, 0.07f, 0.09f, 0.12f, 0.08f, 0.17f };
            string[] iHdrs = { "#", "Stock\nCode", "Description", "UOM", "Qty", "List\nPrice", "Disc\n%", "Net Price" };
            float[] iWidths = iPcts.Select(p => fullW * p).ToArray();

            float iHdrH = lhB * 2.1f + 8f * sc;

            // Shaded header background instead of white — no border box, no column dividers
            g.FillRectangle(new SolidBrush(Color.FromArgb(217, 217, 217)), left, y, fullW, iHdrH);

            float tableTop = y; // top of header block

            float ox = left;
            for (int i = 0; i < iHdrs.Length; i++)
            {
                float cw = iWidths[i];

                bool isNum = i >= 4;
                var af = new StringFormat
                {
                    Alignment = isNum ? StringAlignment.Far : (i <= 1 ? StringAlignment.Near : StringAlignment.Center),
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(iHdrs[i], fBold, Brushes.Black,
                    new RectangleF(ox + 4 * sc, y, cw - 8 * sc, iHdrH), af);
                ox += cw;
            }
            y += iHdrH;

            // Thin rule under the header row only
            g.DrawLine(penBlk, left, y, left + fullW, y);

            float minRowH = lhS + 9f * sc;
            int lineNo = 0;
            foreach (var li in d.Lines)
            {
                lineNo++;
                var descSize = g.MeasureString(li.Name ?? "", fSmall, new SizeF(iWidths[2] - 10 * sc, 999f), lTopFmt);
                float rowH = Math.Max(minRowH, descSize.Height + 8f * sc);

                ox = left;

                string discStr = li.DiscountPct > 0 ? li.DiscountPct.ToString("F1") + "%" : "—";
                string[] vals =
                {
            lineNo.ToString(),
            li.StockCode ?? "",
            li.Name ?? "",
            li.UOM ?? "Ea",
            (li.QtyDispatched > 0 ? li.QtyDispatched : li.Qty).ToString("F2"),
            (li.ListPrice > 0 ? li.ListPrice : li.UnitPrice).ToString("F2"),
            discStr,
            li.LineTotal.ToString("N2")
        };

                for (int i = 0; i < iHdrs.Length; i++)
                {
                    float cw = iWidths[i];

                    var sf = i == 2 ? lTopFmt :
                             i <= 1 ? lFmt :
                             new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

                    g.DrawString(vals[i], fSmall, bkBlack,
                        new RectangleF(ox + 4 * sc, y + 2 * sc, cw - 8 * sc, rowH - 4 * sc), sf);
                    ox += cw;
                }
                y += rowH;
            }

            // no outer border, no closing line — header keeps its border/shading, items flow open below it
            // No outer border and no per-row borders — table is open, matching the reference layout
            y += 28f * sc;

            // SECTION 4 — Footer (Signature + Totals), pinned to the bottom of the page
            float totalsW = fullW * 0.37f;
            float totalsX = left + fullW - totalsW;
            float sigW = totalsX - left - 10 * sc;

            // --- Measure footer content height first, without drawing ---
            float sigLinesH = 3f * (lhB + 11f * sc); // Received By / Signature / Date

            string tc = "Not withstanding anything to the contrary, ownership in and to all such goods shall only pass to the buyer upon payment of full purchase price. Sale subject to our standard terms & conditions of sale, available on request.";
            var tcSize = g.MeasureString(tc, fTiny, new SizeF(sigW - 12 * sc, 999f), lTopFmt);
            float tcH = tcSize.Height + 12f * sc;

            float sigBlockH = sigLinesH + tcH;

            float rhSub = fBold.GetHeight(g) + 7f * sc;
            float rhVat = fNorm.GetHeight(g) + 7f * sc;
            float rhTot = fBold.GetHeight(g) + 7f * sc;
            float totalsBlockH = rhSub + rhVat + rhTot;

            float thankH = lhT + 11f * sc;
            float gapBeforeThank = 18f * sc;

            float footerBlockH = Math.Max(sigBlockH, totalsBlockH) + gapBeforeThank + thankH;

            // Bottom-usable edge of the page (mirrors top margin)
            float bottomLimit = by + bounds.Height - margin;
            float desiredFooterY = bottomLimit - footerBlockH;

            // Pin to bottom, but never overlap the table — fall back to right-after-table position
            float footerY = Math.Max(y, desiredFooterY);

            // --- Draw signature block ---
            float sy = footerY;
            void SigLine(string lbl)
            {
                g.DrawString(lbl, fBold, bkBlack, new RectangleF(left, sy, sigW * 0.36f, lhB + 4), lFmt);
                g.DrawLine(penBlk, left + sigW * 0.38f, sy + lhB + 2, left + sigW * 0.92f, sy + lhB + 2);
                sy += lhB + 11f * sc;
            }

            SigLine("Received By :");
            SigLine("Signature :");
            SigLine("Date :");

            g.DrawRectangle(penBlk, left, sy, sigW, tcH);
            g.FillRectangle(new SolidBrush(Color.FromArgb(255, 255, 245)), left, sy, sigW, tcH);
            g.DrawString(tc, fTiny, bkBlack, new RectangleF(left + 6 * sc, sy + 6 * sc, sigW - 12 * sc, tcH - 12 * sc), lTopFmt);

            // --- Draw totals block ---
            float tv = footerY;
            float tLblW = totalsW * 0.64f;
            float tValW = totalsW * 0.34f;

            void TotalRow(string lbl, string val, bool bold = false, bool topLine = false)
            {
                Font tf = bold ? fBold : fNorm;
                float rh = tf.GetHeight(g) + 7f * sc;
                if (topLine) g.DrawLine(penThk, totalsX, tv, totalsX + totalsW, tv);
                g.DrawString(lbl, tf, bkBlack, new RectangleF(totalsX, tv + 3 * sc, tLblW, rh), rFmt);
                g.DrawString(val, tf, bkBlack, new RectangleF(totalsX + tLblW, tv + 3 * sc, tValW, rh), rFmt);
                tv += rh;
            }

            TotalRow("Sub Total :", d.Subtotal.ToString("N2"), bold: true);
            TotalRow("VAT :", d.TaxTotal.ToString("N2"));
            TotalRow($"TOTAL ({sym}) :", d.GrandTotal.ToString("N2"), bold: true, topLine: true);

            g.DrawRectangle(penThk, totalsX, footerY, totalsW, tv - footerY);

            // --- Thank-you bar ---
            float thankY = Math.Max(sy + tcH, tv) + gapBeforeThank;
            g.FillRectangle(new SolidBrush(Color.FromArgb(50, 50, 50)), left, thankY, fullW, thankH);
            g.DrawString("Thank you for your purchase! • Goods once sold are not returnable.",
                fTiny, Brushes.White, new RectangleF(left, thankY, fullW, thankH), cFmt);

            fTitle.Dispose(); fBold.Dispose(); fNorm.Dispose();
            fSmall.Dispose(); fTiny.Dispose(); fInvNo.Dispose();
            penBlk.Dispose(); penThk.Dispose();
        }

        private static void DrawFallbackLogoText(Graphics g, string name, Font f,
            float x, float y, float w, float h, StringFormat sf)
            => g.DrawString(name, f, Brushes.Black, new RectangleF(x, y, w, h), sf);
        //        public static void DrawA4Receipt(Graphics g, ReceiptData d, Rectangle bounds, float dpi)
        //        {
        //            float sc = bounds.Width / 794f;
        //            float bx = bounds.X;
        //            float by = bounds.Y;
        //            float bw = bounds.Width;
        //            float bh = bounds.Height;

        //            string sym = string.IsNullOrWhiteSpace(d.CurrencySymbol) ? "BWP" : d.CurrencySymbol;

        //            // ── Fonts ──────────────────────────────────────────────────────────────
        //            var fBigBold = new Font("Arial", 13f * sc, FontStyle.Bold);
        //            var fBold = new Font("Arial", 9f * sc, FontStyle.Bold);
        //            var fNorm = new Font("Arial", 8.5f * sc);
        //            var fSmall = new Font("Arial", 7.8f * sc);
        //            var fTiny = new Font("Arial", 6.8f * sc);
        //            var fUnderBold = new Font("Arial", 8f * sc, FontStyle.Bold | FontStyle.Underline);

        //            // ── Pens & brushes ─────────────────────────────────────────────────────
        //            var penThk = new Pen(Color.Black, 1.4f * sc);
        //            var penBlk = new Pen(Color.Black, 0.7f * sc);
        //            var bkBlack = Brushes.Black;
        //            var bkLight = new SolidBrush(Color.FromArgb(220, 220, 220));

        //            // ── String formats ─────────────────────────────────────────────────────
        //            var cFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        //            var lFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        //            var rFmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        //            var lTopFmt = new StringFormat
        //            {
        //                Alignment = StringAlignment.Near,
        //                LineAlignment = StringAlignment.Near,
        //                FormatFlags = StringFormatFlags.LineLimit
        //            };
        //            var wrapFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

        //            // ── Page margins ───────────────────────────────────────────────────────
        //            float margin = 28f * sc;
        //            float left = bx + margin;
        //            float fullW = bw - margin * 2f;
        //            float y = by + 18f * sc;

        //            void Txt(string s, Font f, Brush br, float x, float yt, float w, float h, StringFormat sf = null)
        //                => g.DrawString(s, f, br, new RectangleF(x, yt, w, h), sf ?? lFmt);

        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [1] HEADER ROW  –  Logo | Company box | Offices box
        //            // ══════════════════════════════════════════════════════════════════════
        //            float logoW = fullW * 0.34f;
        //            float compX = left + logoW + 5f * sc;
        //            float compW = fullW * 0.33f;
        //            float officeX = compX + compW + 3f * sc;
        //            float officeW = left + fullW - officeX;

        //            var officeBlocks = string.IsNullOrWhiteSpace(d.SalesOfficeInfo)
        //                ? Array.Empty<string>()
        //                : d.SalesOfficeInfo.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);

        //            // Measure office box height
        //            float offLinH = fSmall.GetHeight(g) + 3f * sc;
        //            float offH = 10f * sc;
        //            foreach (var blk in officeBlocks)
        //            {
        //                var parts = blk.Split('|');
        //                offH += fUnderBold.GetHeight(g) + 3f * sc;
        //                offH += (parts.Length - 1) * offLinH;
        //                offH += 5f * sc;
        //            }
        //            offH += 6f * sc;

        //            // Measure company box height
        //            float cInnerW = compW - 14f * sc;
        //            float compH_content = 8f * sc;
        //            compH_content += fBold.GetHeight(g) + 5f * sc;
        //            if (!string.IsNullOrWhiteSpace(d.CompanyAddress))
        //            {
        //                var sz = g.MeasureString(d.CompanyAddress, fSmall, new SizeF(cInnerW, 999f), wrapFmt);
        //                compH_content += sz.Height + 4f * sc;
        //            }
        //            if (!string.IsNullOrWhiteSpace(d.CompanyPhone)) compH_content += fSmall.GetHeight(g) + 4f * sc;
        //            if (!string.IsNullOrWhiteSpace(d.CompanyVat)) compH_content += fSmall.GetHeight(g) + 4f * sc;
        //            if (!string.IsNullOrWhiteSpace(d.CompanyWebsite)) compH_content += fSmall.GetHeight(g) + 4f * sc;
        //            compH_content += 6f * sc;

        //            float headerH = Math.Max(120f * sc, Math.Max(compH_content, offH));

        //            // Logo
        //            string[] logoPaths =
        //            {
        //        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "flo.jpg"),
        //        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "flo.png"),
        //        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "flo.jpg"),
        //        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "flo.png"),
        //    };
        //            string logoFile = logoPaths.FirstOrDefault(File.Exists);
        //            if (logoFile != null)
        //            {
        //                try
        //                {
        //                    using var logo = Image.FromFile(logoFile);
        //                    float pad = 8f * sc;
        //                    float maxW = logoW - pad * 2f;
        //                    float maxH = headerH - pad * 2f;
        //                    float ratio = Math.Min(maxW / logo.Width, maxH / logo.Height);
        //                    float lw = logo.Width * ratio;
        //                    float lh = logo.Height * ratio;
        //                    g.DrawImage(logo, left + pad, y + (headerH - lh) / 2f, lw, lh);
        //                }
        //                catch { Txt(d.CompanyName, fBigBold, bkBlack, left, y, logoW, headerH, cFmt); }
        //            }
        //            else
        //            {
        //                Txt(d.CompanyName, fBigBold, bkBlack, left, y, logoW, headerH, cFmt);
        //            }

        //            // Company info box
        //            g.DrawRectangle(penThk, compX, y, compW, headerH);
        //            {
        //                float cx = compX + 7f * sc;
        //                float cw = cInnerW;
        //                float cy = y + 8f * sc;

        //                g.DrawString(d.CompanyName, fBold, bkBlack,
        //                    new RectangleF(cx, cy, cw, fBold.GetHeight(g) + 2f), lFmt);
        //                cy += fBold.GetHeight(g) + 5f * sc;

        //                if (!string.IsNullOrWhiteSpace(d.CompanyAddress))
        //                {
        //                    var sz = g.MeasureString(d.CompanyAddress, fSmall, new SizeF(cw, 999f), wrapFmt);
        //                    g.DrawString(d.CompanyAddress, fSmall, bkBlack,
        //                        new RectangleF(cx, cy, cw, sz.Height + 2f), wrapFmt);
        //                    cy += sz.Height + 4f * sc;
        //                }
        //                if (!string.IsNullOrWhiteSpace(d.CompanyPhone))
        //                {
        //                    g.DrawString("Phone : " + d.CompanyPhone, fSmall, bkBlack,
        //                        new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt);
        //                    cy += fSmall.GetHeight(g) + 4f * sc;
        //                }
        //                if (!string.IsNullOrWhiteSpace(d.CompanyVat))
        //                {
        //                    g.DrawString("Vat : " + d.CompanyVat, fSmall, bkBlack,
        //                        new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt);
        //                    cy += fSmall.GetHeight(g) + 4f * sc;
        //                }
        //                if (!string.IsNullOrWhiteSpace(d.CompanyWebsite))
        //                {
        //                    g.DrawString("Website : " + d.CompanyWebsite, fSmall, bkBlack,
        //                        new RectangleF(cx, cy, cw, fSmall.GetHeight(g) + 2f), lFmt);
        //                }
        //            }

        //            // Sales offices box
        //            g.DrawRectangle(penThk, officeX, y, officeW, headerH);
        //            {
        //                float ox = officeX + 7f * sc;
        //                float ow = officeW - 14f * sc;
        //                float oy = y + 8f * sc;
        //                foreach (var blk in officeBlocks)
        //                {
        //                    var parts = blk.Split('|');
        //                    g.DrawString(parts[0].Trim(), fUnderBold, bkBlack,
        //                        new RectangleF(ox, oy, ow, fUnderBold.GetHeight(g) + 2f), lFmt);
        //                    oy += fUnderBold.GetHeight(g) + 3f * sc;
        //                    for (int pi = 1; pi < parts.Length; pi++)
        //                    {
        //                        string part = parts[pi].Trim();
        //                        if (string.IsNullOrWhiteSpace(part)) continue;
        //                        g.DrawString(part, fSmall, bkBlack,
        //                            new RectangleF(ox, oy, ow, fSmall.GetHeight(g) + 2f), lFmt);
        //                        oy += fSmall.GetHeight(g) + 3f * sc;
        //                    }
        //                    oy += 5f * sc;
        //                }
        //            }

        //            y += headerH + 12f * sc;

        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [2] CUSTOMER ROW  –  Customer box (left 44%) | "Tax Invoice" + InvoiceNo
        //            // ══════════════════════════════════════════════════════════════════════
        //            float custW = fullW * 0.44f;
        //            float taxAreaX = left + custW + 8f * sc;
        //            float taxAreaW = left + fullW - taxAreaX;

        //            string custName = string.IsNullOrWhiteSpace(d.CustomerName) ? "Walk-in" : d.CustomerName;
        //            string custCode = d.CustomerCode ?? "";
        //            string custAddr = d.CustomerAddress ?? "";

        //            float custBoxH_content = 8f * sc;
        //            custBoxH_content += fBold.GetHeight(g) + 5f * sc;  // "Customer" heading
        //            if (!string.IsNullOrWhiteSpace(custCode))
        //                custBoxH_content += fBold.GetHeight(g) + 4f * sc;
        //            custBoxH_content += fBold.GetHeight(g) + 4f * sc;  // name
        //            if (!string.IsNullOrWhiteSpace(custAddr))
        //            {
        //                var sz2 = g.MeasureString(custAddr, fNorm, new SizeF(custW - 14f * sc, 999f), wrapFmt);
        //                custBoxH_content += sz2.Height + 4f * sc;
        //            }
        //            custBoxH_content += fNorm.GetHeight(g) + 8f * sc;  // VAT line
        //            custBoxH_content += 6f * sc;

        //            float custBoxH = Math.Max(100f * sc, custBoxH_content);

        //            g.DrawRectangle(penThk, left, y, custW, custBoxH);
        //            {
        //                float cx = left + 7f * sc;
        //                float cw = custW - 14f * sc;
        //                float cy = y + 8f * sc;

        //                g.DrawString("Customer", fBold, bkBlack,
        //                    new RectangleF(cx, cy, cw, fBold.GetHeight(g) + 2f), lFmt);
        //                cy += fBold.GetHeight(g) + 5f * sc;

        //                if (!string.IsNullOrWhiteSpace(custCode))
        //                {
        //                    g.DrawString(custCode, fBold, bkBlack,
        //                        new RectangleF(cx, cy, cw, fBold.GetHeight(g) + 2f), lFmt);
        //                    cy += fBold.GetHeight(g) + 4f * sc;
        //                }

        //                g.DrawString(custName, fBold, bkBlack,
        //                    new RectangleF(cx, cy, cw, fBold.GetHeight(g) + 2f), lFmt);
        //                cy += fBold.GetHeight(g) + 4f * sc;

        //                if (!string.IsNullOrWhiteSpace(custAddr))
        //                {
        //                    var sz2 = g.MeasureString(custAddr, fNorm, new SizeF(cw, 999f), wrapFmt);
        //                    g.DrawString(custAddr, fNorm, bkBlack,
        //                        new RectangleF(cx, cy, cw, sz2.Height + 2f), wrapFmt);
        //                    cy += sz2.Height + 4f * sc;
        //                }

        //                g.DrawString("Customer Vat No.: " + (d.CustomerVat ?? "N/A"), fNorm, bkBlack,
        //                    new RectangleF(cx, cy, cw, fNorm.GetHeight(g) + 2f), lFmt);
        //            }

        //            // Tax Invoice text (no box)
        //            {
        //                float totalTxtH = fBigBold.GetHeight(g) + 7f * sc + fBold.GetHeight(g)
        //                                + (d.IsReprint ? fSmall.GetHeight(g) + 4f * sc : 0f);
        //                float midY = y + (custBoxH - totalTxtH) / 2f;

        //                string invoiceLabel = d.IsQuotation ? "Quotation" : "Tax Invoice";
        //                g.DrawString(invoiceLabel, fBigBold, bkBlack,
        //                    new RectangleF(taxAreaX, midY, taxAreaW, fBigBold.GetHeight(g) + 4f), cFmt);
        //                midY += fBigBold.GetHeight(g) + 7f * sc;

        //                g.DrawString(d.InvoiceNo ?? "", fBold, bkBlack,
        //                    new RectangleF(taxAreaX, midY, taxAreaW, fBold.GetHeight(g) + 4f), cFmt);
        //                midY += fBold.GetHeight(g) + 4f * sc;

        //                // ── REPRINT stamp ──────────────────────────────────────────────
        //                if (d.IsReprint)
        //                {
        //                    string reprintLine = $"** REPRINT — {DateTime.Now:dd MMM yyyy  HH:mm:ss} **";

        //                    // Red background pill
        //                    float rpW = taxAreaW - 8f * sc;
        //                    float rpH = fSmall.GetHeight(g) + 6f * sc;
        //                    float rpX = taxAreaX + (taxAreaW - rpW) / 2f;

        //                    using var rpBrush = new SolidBrush(Color.White);   // red-600
        //                    using var rpPath = GetRoundedRect(new RectangleF(rpX, midY, rpW, rpH), 4f * sc);
        //                    g.FillPath(rpBrush, rpPath);

        //                    Font fSmallBold = new Font(fSmall.FontFamily, fSmall.Size, FontStyle.Bold);

        //                    g.DrawString(reprintLine, fSmallBold, Brushes.Black,
        //                        new RectangleF(rpX, midY, rpW, rpH), cFmt);
        //                }
        //            }

        //            y += custBoxH + 12f * sc;

        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [3] ORDER INFO ROW
        //            //  Sales Order | Order Date | Salesperson | Invoice Date | Invoice No | Customer PO No
        //            // ══════════════════════════════════════════════════════════════════════
        //            float[] oPcts = { 0.16f, 0.14f, 0.18f, 0.19f, 0.17f, 0.16f };
        //            string[] oHdrs = { "Sales Order", "Order Date", "Salesperson", "Invoice Date", "Invoice No", "Customer Purchase\nOrder No" };
        //            float[] oWidths = oPcts.Select(p => fullW * p).ToArray();

        //            float oHdrH = fBold.GetHeight(g) * 2.2f + 10f * sc;
        //            float oRowH = fSmall.GetHeight(g) + 12f * sc;

        //            g.FillRectangle(bkLight, left, y, fullW, oHdrH);
        //            g.DrawRectangle(penThk, left, y, fullW, oHdrH);
        //            float ox3 = left;
        //            for (int i = 0; i < oHdrs.Length; i++)
        //            {
        //                if (i > 0) g.DrawLine(penBlk, ox3, y, ox3, y + oHdrH);
        //                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        //                g.DrawString(oHdrs[i], fBold, bkBlack,
        //                    new RectangleF(ox3 + 2f * sc, y, oWidths[i] - 4f * sc, oHdrH), sf);
        //                ox3 += oWidths[i];
        //            }
        //            y += oHdrH;

        //            string[] oVals =
        //            {
        //        d.InvoiceNo    ?? "476408",
        //        d.SaleDate.ToString("dd/MM/yy"),
        //        d.CashierName ?? "Admin",
        //        d.SaleDate.ToString("dd/MM/yy"),
        //        d.InvoiceNo       ?? "",
        //        d.InvoiceNo    ?? ""
        //    };
        //            g.DrawRectangle(penThk, left, y, fullW, oRowH);
        //            ox3 = left;
        //            for (int i = 0; i < oVals.Length; i++)
        //            {
        //                if (i > 0) g.DrawLine(penBlk, ox3, y, ox3, y + oRowH);
        //                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        //                g.DrawString(oVals[i], fSmall, bkBlack,
        //                    new RectangleF(ox3 + 2f * sc, y, oWidths[i] - 4f * sc, oRowH), sf);
        //                ox3 += oWidths[i];
        //            }
        //            y += oRowH + 8f * sc;

        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [4] ITEMS TABLE
        //            //  Stock Code | Description | UOM | Qty Req | Qty Despatch | List Price | Disc% | Net Price | Incl Value
        //            // ══════════════════════════════════════════════════════════════════════
        //            float[] iPcts = { 0.12f, 0.25f, 0.07f, 0.08f, 0.09f, 0.10f, 0.08f, 0.10f, 0.11f };
        //            string[] iHdrs = { "Stock Code", "Description", "UOM", "Qty Req", "Qty\nDespatch", "List Price", "Disc %", "Net Price", "Incl Value" };
        //            bool[] iRight = { false, false, false, true, true, true, true, true, true };
        //            float[] iWidths = iPcts.Select(p => fullW * p).ToArray();

        //            float iHdrH = fBold.GetHeight(g) * 2.2f + 10f * sc;
        //            g.FillRectangle(bkLight, left, y, fullW, iHdrH);
        //            g.DrawRectangle(penThk, left, y, fullW, iHdrH);
        //            float ix2 = left;
        //            for (int i = 0; i < iHdrs.Length; i++)
        //            {
        //                if (i > 0) g.DrawLine(penBlk, ix2, y, ix2, y + iHdrH);
        //                var sf = new StringFormat
        //                {
        //                    Alignment = iRight[i] ? StringAlignment.Far : StringAlignment.Near,
        //                    LineAlignment = StringAlignment.Center
        //                };
        //                g.DrawString(iHdrs[i], fBold, bkBlack,
        //                    new RectangleF(ix2 + 3f * sc, y, iWidths[i] - 6f * sc, iHdrH), sf);
        //                ix2 += iWidths[i];
        //            }
        //            y += iHdrH;

        //            decimal taxRate = (d.Subtotal > 0) ? (d.TaxTotal / d.Subtotal) : 0.14m;// derive rate or default 14 %
        //            float minRowH = fSmall.GetHeight(g) + 12f * sc;

        //            foreach (var li in d.Lines)
        //            {
        //                float descH = g.MeasureString(li.Name ?? "", fSmall,
        //                    new SizeF(iWidths[1] - 8f * sc, 999f), lTopFmt).Height;
        //                float rowH = Math.Max(minRowH, descH + 10f * sc);

        //                decimal netPrice = li.LineTotal;                           // excl VAT
        //                decimal inclValue = Math.Round(netPrice * (1m + (decimal)taxRate), 2);

        //                string[] iVals =
        //     {
        //    li.StockCode ?? "",
        //    li.Name      ?? "",
        //    string.IsNullOrWhiteSpace(li.UOM) ? "EA" : li.UOM,
        //    (li.QtyRequested > 0 ? li.QtyRequested : li.Qty).ToString("0"),
        //    (li.QtyDispatched > 0 ? li.QtyDispatched : li.Qty).ToString("0"),
        //    (li.ListPrice > 0 ? li.ListPrice : li.UnitPrice).ToString("N2", System.Globalization.CultureInfo.InvariantCulture),  // ✅
        //    (li.DiscountPct > 0 ? li.DiscountPct.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) : ""),  // ✅
        //    netPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture),  // ✅
        //    inclValue.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)   // ✅
        //};

        //                ix2 = left;
        //                for (int i = 0; i < iHdrs.Length; i++)
        //                {
        //                    var sf = iRight[i]
        //                        ? new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center }
        //                        : new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        //                    g.DrawString(iVals[i], fSmall, bkBlack,
        //                        new RectangleF(ix2 + 3f * sc, y, iWidths[i] - 6f * sc, rowH), sf);
        //                    ix2 += iWidths[i];
        //                }
        //                y += rowH;
        //            }

        //            y += 10f * sc;

        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [5] PAYMENT METHOD BLOCK  (left-aligned, below items)
        //            // ══════════════════════════════════════════════════════════════════════
        //            float pmLineH = fSmall.GetHeight(g) + 5f * sc;
        //            float pmW = fullW * 0.45f;                      // Panel width
        //            float pmX = left + fullW - pmW;                 // Right-aligned start X
        //            float pmLabelW = 120f * sc;
        //            float pmValueX = pmX + pmLabelW + 10f * sc;    // Value column X
        //            float pmValueW = 80f * sc;

        //            if (!d.IsQuotation)
        //            {
        //                g.DrawString("-------- Payment Method --------", fSmall, bkBlack,
        //                    new RectangleF(pmX, y, pmW, pmLineH), lFmt);
        //                y += pmLineH;
        //                void PayRow(string method, decimal amount)
        //                {
        //                    if (amount <= 0) return;
        //                    g.DrawString(method, fSmall, bkBlack,
        //                        new RectangleF(pmX, y, pmLabelW, pmLineH), lFmt);
        //                    g.DrawString(amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture), fSmall, bkBlack,  // ✅
        //                        new RectangleF(pmValueX, y, pmValueW, pmLineH), lFmt);
        //                    y += pmLineH;
        //                }

        //                PayRow("CASH", d.PaidCash);
        //                PayRow("BANK TRANSFER", d.PaidDigital);
        //                PayRow("CARD", d.PaidCard);

        //                if (d.PaidCard > 0 && !string.IsNullOrWhiteSpace(d.CardRefNumber))
        //                {
        //                    g.DrawString("Card Ref:", fSmall, bkBlack,
        //                        new RectangleF(pmX, y, pmLabelW, pmLineH), lFmt);
        //                    g.DrawString(d.CardRefNumber, fSmall, bkBlack,
        //                        new RectangleF(pmValueX, y, pmW - pmLabelW - 10f * sc, pmLineH), lFmt);
        //                    y += pmLineH;
        //                }

        //                g.DrawString("Total payment  :", fSmall, bkBlack,
        //                    new RectangleF(pmX, y, pmLabelW, pmLineH), lFmt);
        //                g.DrawString((d.PaidCash + d.PaidDigital + d.PaidCard).ToString("N2", System.Globalization.CultureInfo.InvariantCulture), fSmall, bkBlack,  // ✅
        //                    new RectangleF(pmValueX, y, pmValueW, pmLineH), lFmt);
        //                y += pmLineH;

        //                g.DrawString("Invoice amount :", fSmall, bkBlack,
        //                    new RectangleF(pmX, y, pmLabelW, pmLineH), lFmt);
        //                g.DrawString(d.GrandTotal.ToString("N2", System.Globalization.CultureInfo.InvariantCulture), fSmall, bkBlack,  // ✅
        //                    new RectangleF(pmValueX, y, pmValueW, pmLineH), lFmt);
        //                y += pmLineH;

        //                decimal change = (d.PaidCash + d.PaidDigital + d.PaidCard) - d.GrandTotal;
        //                g.DrawString("Change given   :", fSmall, bkBlack,
        //                    new RectangleF(pmX, y, pmLabelW, pmLineH), lFmt);
        //                g.DrawString((change > 0 ? change : 0m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture), fSmall, bkBlack,  // ✅
        //                    new RectangleF(pmValueX, y, pmValueW, pmLineH), lFmt);
        //                y += pmLineH + 16f * sc;
        //            }
        //            else
        //            {
        //                g.DrawString("This is a quotation — no payment has been received.",
        //                    fSmall, bkBlack,
        //                    new RectangleF(pmX, y, pmW, pmLineH), lFmt);
        //                y += pmLineH + 16f * sc;
        //            }
        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [6] BOTTOM TOTALS TABLE  (right-aligned)
        //            //  Currency | Total Net Excl | Total Vat | Total Invoice Value
        //            // ══════════════════════════════════════════════════════════════════════
        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [6] BOTTOM TOTALS TABLE + SAFE FOOTER LAYOUT (FIXED)
        //            // ══════════════════════════════════════════════════════════════════════
        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [6] BOTTOM TOTALS TABLE + SAFE FOOTER LAYOUT (FIXED)
        //            // ══════════════════════════════════════════════════════════════════════

        //            float tblW = fullW * 0.55f;
        //            float tblX = left + fullW - tblW;
        //            float colW = tblW / 4f;

        //            float tHdrH = fBold.GetHeight(g) + 14f * sc;
        //            float tRowH = fSmall.GetHeight(g) + 12f * sc;
        //            float totalTableHeight = tHdrH + tRowH;

        //            // ─────────────────────────────────────────────────────────────────────
        //            // STEP 1: CALCULATE DISCLAIMER HEIGHT
        //            // ─────────────────────────────────────────────────────────────────────
        //            float discLineH = fTiny.GetHeight(g) + 2f * sc;
        //            float discHeight = discLineH * 2f + 6f * sc;
        //            float bottomPad = 14f * sc;

        //            float disclaimerY = bounds.Bottom - discHeight - bottomPad;

        //            // ─────────────────────────────────────────────────────────────────────
        //            // STEP 2: LINE ABOVE THE TABLE
        //            // ─────────────────────────────────────────────────────────────────────
        //            float lineAboveTableY = disclaimerY - totalTableHeight - 20f * sc;
        //            g.DrawLine(penThk, left, lineAboveTableY, left + fullW, lineAboveTableY);

        //            float footerY = lineAboveTableY + 8f * sc;

        //            // DATA
        //            string[] tHdrs = { "Currency", "Total Net Excl", "Total Vat", "Total Invoice\nValue" };
        //            string FormatMillions(decimal amount)
        //            {
        //                return amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        //            }
        //            string[] tVals =
        //            {
        //    sym,
        //   FormatMillions(d.Subtotal),         // Million format!
        //    FormatMillions(d.TaxTotal),         // Million format!
        //    FormatMillions(d.GrandTotal)
        //};

        //            // ─────────────────────────────────────────────────────────────────────
        //            // SHADOW
        //            // ─────────────────────────────────────────────────────────────────────
        //            float radius = 10f * sc;
        //            RectangleF shadowRect = new RectangleF(tblX + 3f, footerY + 3f, tblW, totalTableHeight);
        //            using (GraphicsPath shadowPath = GetRoundedRect(shadowRect, radius))
        //            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
        //                g.FillPath(shadowBrush, shadowPath);

        //            // ─────────────────────────────────────────────────────────────────────
        //            // ROUNDED TABLE BACKGROUND
        //            // ─────────────────────────────────────────────────────────────────────
        //            RectangleF outerRect = new RectangleF(tblX, footerY, tblW, totalTableHeight);
        //            using (GraphicsPath path = GetRoundedRect(outerRect, radius))
        //            {
        //                g.FillPath(Brushes.White, path);
        //                g.DrawPath(penThk, path);
        //            }

        //            // HEADER FILL (clip to rounded top only)
        //            RectangleF headerRect = new RectangleF(tblX, footerY, tblW, tHdrH);
        //            using (GraphicsPath headerClip = GetRoundedRect(outerRect, radius))
        //            {
        //                g.SetClip(headerClip);
        //                g.FillRectangle(bkLight, headerRect);
        //                g.ResetClip();
        //            }

        //            // Divider between header and values row
        //            g.DrawLine(penBlk, tblX, footerY + tHdrH, tblX + tblW, footerY + tHdrH);

        //            // ─────────────────────────────────────────────────────────────────────
        //            // COLUMN HEADERS + VERTICAL DIVIDERS
        //            // ─────────────────────────────────────────────────────────────────────
        //            var ctrFmt = new StringFormat
        //            {
        //                Alignment = StringAlignment.Center,
        //                LineAlignment = StringAlignment.Center
        //            };

        //            float tx = tblX;
        //            for (int i = 0; i < tHdrs.Length; i++)
        //            {
        //                if (i > 0)
        //                    g.DrawLine(penBlk, tx, footerY, tx, footerY + totalTableHeight);

        //                g.DrawString(tHdrs[i], fBold, bkBlack,
        //                    new RectangleF(tx + 3f * sc, footerY, colW - 6f * sc, tHdrH), ctrFmt);

        //                tx += colW;
        //            }

        //            // ─────────────────────────────────────────────────────────────────────
        //            // VALUE ROW
        //            // ─────────────────────────────────────────────────────────────────────
        //            float valY = footerY + tHdrH;
        //            tx = tblX;
        //            for (int i = 0; i < tVals.Length; i++)
        //            {
        //                g.DrawString(tVals[i], fSmall, bkBlack,
        //                    new RectangleF(tx + 3f * sc, valY, colW - 6f * sc, tRowH), ctrFmt);
        //                tx += colW;
        //            }

        //            // ══════════════════════════════════════════════════════════════════════
        //            //  [7] FOOTER DISCLAIMER  (centered, fixed at bottom)
        //            // ══════════════════════════════════════════════════════════════════════

        //            // Thin rule just above disclaimer text
        //            g.DrawLine(penBlk,
        //                left, disclaimerY - 5f * sc,
        //                left + fullW, disclaimerY - 5f * sc);

        //            string disc1 = "No claims for damages or shortages will be accepted unless notified in writing within 48 hours of delivery.";
        //            string disc2 = $"Goods return only with original invoice and prior approval by the Management - {d.CompanyName}";

        //            var centreNoClip = new StringFormat
        //            {
        //                Alignment = StringAlignment.Center,
        //                LineAlignment = StringAlignment.Near,
        //                Trimming = StringTrimming.EllipsisCharacter
        //            };

        //            g.DrawString(disc1, fTiny, bkBlack,
        //                new RectangleF(left, disclaimerY, fullW, discLineH), centreNoClip);

        //            g.DrawString(disc2, fTiny, bkBlack,
        //                new RectangleF(left, disclaimerY + discLineH + 2f * sc, fullW, discLineH), centreNoClip);

        //            // ── Dispose ───────────────────────────────────────────────────────────
        //            fBigBold.Dispose(); fBold.Dispose(); fNorm.Dispose();
        //            fSmall.Dispose(); fTiny.Dispose(); fUnderBold.Dispose();
        //            bkLight.Dispose();
        //            penThk.Dispose(); penBlk.Dispose();
        //            ctrFmt.Dispose(); centreNoClip.Dispose();
        //        }
        private static (bool success, string message) PrintA4(ReceiptData data, string printerName, Form ownerForm)
        {
            try
            {
                if (printerName.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase))
                {
                    string filePath = null;
                    ownerForm.Invoke(new Action(() =>
                    {
                        var sd = new SaveFileDialog
                        {
                            Filter = "PDF Files (*.pdf)|*.pdf",
                            FileName = $"Invoice_{(data.InvoiceNo ?? "").Replace("INV-", "")}_{DateTime.Now:yyyyMMddHHmm}.pdf",
                            Title = "Save Receipt as PDF",
                            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                        };
                        if (sd.ShowDialog(ownerForm) == DialogResult.OK) filePath = sd.FileName;
                    }));
                    if (string.IsNullOrEmpty(filePath)) return (false, "Save cancelled.");

                    var doc2 = new PrintDocument();
                    doc2.PrinterSettings.PrinterName = printerName;
                    doc2.PrinterSettings.PrintToFile = true;
                    doc2.PrinterSettings.PrintFileName = filePath;
                    doc2.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
                    doc2.PrintPage += (s, e) => DrawA4Receipt(e.Graphics, data, e.MarginBounds, e.Graphics.DpiY);
                    doc2.Print();
                    return (true, "PDF saved successfully!");
                }

                var doc = new PrintDocument();
                if (!string.IsNullOrEmpty(printerName)) doc.PrinterSettings.PrinterName = printerName;
                doc.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
                doc.PrintPage += (s, e) => DrawA4Receipt(e.Graphics, data, e.MarginBounds, e.Graphics.DpiY);
                doc.Print();
                return (true, "Printed successfully!");
            }
            catch (Exception ex) { return (false, "Print Error: " + ex.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  UI helpers
        // ══════════════════════════════════════════════════════════════════════
        private static Button MakeBtn(string text, Color bg, Color fg, Point loc, Size sz) =>
            new Button
            {
                Text = text,
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Location = loc,
                Size = sz,
                Cursor = Cursors.Hand,
                FlatAppearance = { BorderSize = 0 }
            };

        private static Button MakeTabBtn(string text, bool active, Point loc, Size sz)
        {
            var c = Color.FromArgb(28, 31, 40);
            return new Button
            {
                Text = text,
                BackColor = active ? Color.FromArgb(59, 130, 246) : c,
                ForeColor = active ? Color.White : Color.FromArgb(130, 140, 158),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = loc,
                Size = sz,
                Cursor = Cursors.Hand,
                FlatAppearance = { BorderSize = 0 }
            };
        }

        private static Region MakeRound(Size size, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(size.Width - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(size.Width - r * 2, size.Height - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, size.Height - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }
    } 
        public static class CashDrawer
        {
            private static readonly byte[] KickPin2 = { 0x1B, 0x70, 0x00, 0x19, 0xFA };
            private static readonly byte[] KickPin5 = { 0x1B, 0x70, 0x01, 0x19, 0xFA };
         
            private static readonly string _cacheFile =
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drawer_port.cache");

            private static string _cachedPort = null;

            private static (bool success, string message) SendKick(string comPort, int baudRate, bool usePin5)
            {
                try
                {
                    using var sp = new System.IO.Ports.SerialPort(comPort, baudRate)
                    {
                        Parity = System.IO.Ports.Parity.None,
                        DataBits = 8,
                        StopBits = System.IO.Ports.StopBits.One,
                        WriteTimeout = 1500,
                        ReadTimeout = 1500,
                        Handshake = System.IO.Ports.Handshake.None
                    };
                    sp.Open();
                    var cmd = usePin5 ? KickPin5 : KickPin2;
                    sp.Write(cmd, 0, cmd.Length);
                    System.Threading.Thread.Sleep(80);
                    sp.Close();
                    return (true, $"Cash drawer opened via {comPort}.");
                }
                catch (Exception ex)
                {
                    return (false, comPort + ": " + ex.Message);
                }
            }

           
            public static (bool success, string message) OpenAuto(int baudRate = 9600, bool usePin5 = false)
            {
              
                string cached = LoadCachedPort();
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    var r = SendKick(cached, baudRate, usePin5);
                    if (r.success) return r;
                    
                }
                 
                string[] ports;
                try { ports = System.IO.Ports.SerialPort.GetPortNames(); }
                catch { return (false, "No COM ports detected."); }

                if (ports.Length == 0)
                    return (false, "No COM ports detected — check the drawer adapter is plugged in.");

                foreach (var port in ports)
                {
                    if (string.Equals(port, cached, StringComparison.OrdinalIgnoreCase)) continue;  
                    var r = SendKick(port, baudRate, usePin5);
                    if (r.success)
                    {
                        SaveCachedPort(port);
                        return r;
                    }
                }

                return (false, $"Could not open drawer on any port ({string.Join(", ", ports)}).");
            }
 
        public static bool IsAvailable()
        {
            string cached = LoadCachedPort();
            if (!string.IsNullOrWhiteSpace(cached)) return true;

            try
            {
                return System.IO.Ports.SerialPort.GetPortNames().Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string LoadCachedPort()
            {
                if (_cachedPort != null) return _cachedPort;
                try
                {
                    if (System.IO.File.Exists(_cacheFile))
                        _cachedPort = System.IO.File.ReadAllText(_cacheFile).Trim();
                }
                catch { }
                return _cachedPort ?? "";
            }

            private static void SaveCachedPort(string port)
            {
                _cachedPort = port;
                try { System.IO.File.WriteAllText(_cacheFile, port); }
                catch { /* non-critical */ }
            }
        }
    

    // ══════════════════════════════════════════════════════════════════════════
    //  RawPrinterHelper
    // ══════════════════════════════════════════════════════════════════════════
    internal static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out nint hPrinter, nint pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ClosePrinter(nint hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartDocPrinter(nint hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndDocPrinter(nint hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartPagePrinter(nint hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndPagePrinter(nint hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool WritePrinter(nint hPrinter, nint pBytes, int dwCount, out int dwWritten);

        public static bool SendBytesToPrinter(string printerName, byte[] bytes)
        {
            if (!OpenPrinter(printerName, out nint hPrinter, nint.Zero)) return false;
            try
            {
                var di = new DOCINFOA { pDocName = "RAW", pDataType = "RAW" };
                if (!StartDocPrinter(hPrinter, 1, di)) return false;
                try
                {
                    if (!StartPagePrinter(hPrinter)) return false;
                    try
                    {
                        nint pBuf = Marshal.AllocCoTaskMem(bytes.Length);
                        try { Marshal.Copy(bytes, 0, pBuf, bytes.Length); return WritePrinter(hPrinter, pBuf, bytes.Length, out _); }
                        finally { Marshal.FreeCoTaskMem(pBuf); }
                    }
                    finally { EndPagePrinter(hPrinter); }
                }
                finally { EndDocPrinter(hPrinter); }
            }
            finally { ClosePrinter(hPrinter); }
        }
    }
}