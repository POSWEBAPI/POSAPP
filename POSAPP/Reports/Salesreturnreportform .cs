using POSAPP.Invoice;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace POSAPP.Reports
{
    public class SalesReturnReportForm : Form
    {
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue2 = Color.FromArgb(37, 99, 235);   // replaces AccOrange
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);

        private readonly int _companyId;
        private readonly string _company;
        private readonly string _currency;
        private DateTime _from;
        private DateTime _to;

        private List<ReturnReportRow> _rows = new List<ReturnReportRow>();
        private WebBrowser _browser;

        // ══════════════════════════════════════════════════════════════════════
        public SalesReturnReportForm(
            int companyId,
            string company = "ABC",
            string currency = "P",
            DateTime? from = null,
            DateTime? to = null)
        {
            _companyId = companyId;
            _company = company;
            _currency = currency;
            _from = from ?? DateTime.Now.Date;
            _to = to ?? DateTime.Now.Date.AddDays(1);

            LoadData();
            InitUI();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  DATA
        // ══════════════════════════════════════════════════════════════════════
        private void LoadData()
        {
            try
            {
                SalesReturnRepository.EnsureSchema();
                _rows = SalesReturnRepository.LoadReturns(_from, _to, _companyId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SalesReturnReportForm.LoadData: " + ex.Message);
                _rows = new List<ReturnReportRow>();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  UI
        // ══════════════════════════════════════════════════════════════════════
        private void InitUI()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ClientSize = new Size(1140, 820);
            KeyPreview = true;
            Text = "Sales Return Report";

            // ── Header ────────────────────────────────────────────────────────
            var pHead = new Panel { BackColor = PanelDark, Dock = DockStyle.Top, Height = 52 };
            pHead.Controls.Add(new Label
            {
                Text = "↩  Sales Return Report",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = AccBlue2,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(500, 52),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var btnPrint = MakeBtn("🖨  Print / PDF", AccGreen, new Point(660, 8), new Size(140, 36));
            var btnCsv = MakeBtn("📤  Export CSV", AccBlue, new Point(810, 8), new Size(140, 36));
            var btnX = MakeBtn("✕", Color.FromArgb(55, 60, 78),
                                   new Point(ClientSize.Width - 46, 0), new Size(46, 52));

            btnPrint.Click += (s, e) => _browser?.ShowPrintDialog();
            btnCsv.Click += BtnCsv_Click;
            btnX.Click += (s, e) => Close();
            btnX.Font = new Font("Segoe UI", 11F);

            pHead.Controls.AddRange(new Control[] { btnPrint, btnCsv, btnX });
            Controls.Add(pHead);

            // ── Toolbar ───────────────────────────────────────────────────────
            var pDate = new Panel
            {
                BackColor = Color.FromArgb(28, 31, 40),
                Dock = DockStyle.Top,
                Height = 42
            };

            AddLabel(pDate, "From:", new Point(12, 12));
            var dtFrom = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = _from,
                Size = new Size(120, 24),
                Location = new Point(56, 9),
                Font = new Font("Segoe UI", 9F)
            };
            pDate.Controls.Add(dtFrom);

            AddLabel(pDate, "To:", new Point(186, 12));
            var dtTo = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = _to.AddDays(-1),
                Size = new Size(120, 24),
                Location = new Point(210, 9),
                Font = new Font("Segoe UI", 9F)
            };
            pDate.Controls.Add(dtTo);

            string[] ranges = { "Today", "This Week", "This Month" };
            int rx = 342;
            foreach (var range in ranges)
            {
                var rc = range;
                var b = MakeBtn(range, Color.FromArgb(44, 48, 60), new Point(rx, 8), new Size(90, 26));
                b.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
                b.Click += (s, e) =>
                {
                    DateTime today = DateTime.Now.Date;
                    switch (rc)
                    {
                        case "Today": dtFrom.Value = today; dtTo.Value = today; break;
                        case "This Week": dtFrom.Value = today.AddDays(-(int)today.DayOfWeek); dtTo.Value = today; break;
                        case "This Month": dtFrom.Value = new DateTime(today.Year, today.Month, 1); dtTo.Value = today; break;
                    }
                };
                pDate.Controls.Add(b);
                rx += 96;
            }

            var btnRefresh = MakeBtn("↻  Refresh", AccBlue, new Point(rx + 6, 8), new Size(90, 26));
            btnRefresh.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            btnRefresh.Click += (s, e) =>
            {
                _from = dtFrom.Value.Date;
                _to = dtTo.Value.Date.AddDays(1);
                LoadData();
                _browser.DocumentText = BuildHtml();
            };
            pDate.Controls.Add(btnRefresh);

            var lblStrip = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(rx + 108, 13),
                Text = BuildSummaryStrip()
            };
            pDate.Controls.Add(lblStrip);
            btnRefresh.Click += (s, e) => lblStrip.Text = BuildSummaryStrip();

            Controls.Add(pDate);
            pDate.BringToFront();

            // ── Browser ───────────────────────────────────────────────────────
            var pOuter = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(48, 52, 64) };
            Controls.Add(pOuter);
            pOuter.BringToFront();

            _browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                ScrollBarsEnabled = true,
                IsWebBrowserContextMenuEnabled = false,
                AllowWebBrowserDrop = false,
                ScriptErrorsSuppressed = true
            };
            pOuter.Controls.Add(_browser);
            _browser.DocumentText = BuildHtml();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HTML REPORT
        // ══════════════════════════════════════════════════════════════════════
        private string BuildHtml()
        {
            decimal totalRefund = _rows.Sum(r => r.RefundTotal);
            int totalReturns = _rows.Count;
            int totalItems = _rows.Sum(r => r.TotalItemsReturned);
            decimal cashRefund = _rows.Where(r => r.RefundMethod == "cash").Sum(r => r.RefundTotal);
            decimal upiRefund = _rows.Where(r => r.RefundMethod == "upi").Sum(r => r.RefundTotal);
            decimal cardRefund = _rows.Where(r => r.RefundMethod == "card").Sum(r => r.RefundTotal);
            decimal bankRefund = _rows.Where(r => r.RefundMethod == "bank").Sum(r => r.RefundTotal);

            // ── Aggregate item totals across all returns ───────────────────────
            // item name → (totalQty, totalRefundAmt)
            var itemTotals = new Dictionary<string, (int Qty, decimal Amt)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
            {
                try
                {
                    var lines = SalesReturnRepository.LoadReturnLines(row.ReturnInvoiceNo);
                    foreach (var l in lines)
                    {
                        if (itemTotals.ContainsKey(l.ItemName))
                            itemTotals[l.ItemName] = (itemTotals[l.ItemName].Qty + l.ReturnQty,
                                                      itemTotals[l.ItemName].Amt + l.RefundAmt);
                        else
                            itemTotals[l.ItemName] = (l.ReturnQty, l.RefundAmt);
                    }
                }
                catch { }
            }

            // ── Item summary rows ──────────────────────────────────────────────
            var itemSummaryRows = new StringBuilder();
            foreach (var kv in itemTotals.OrderByDescending(x => x.Value.Qty))
            {
                itemSummaryRows.Append($@"
                  <tr>
                    <td>{He(kv.Key)}</td>
                    <td class='num'><strong>{kv.Value.Qty}</strong></td>
                    <td class='num'>{_currency} {kv.Value.Amt:N2}</td>
                  </tr>");
            }
            if (!itemTotals.Any())
                itemSummaryRows.Append("<tr><td colspan='3' class='empty'>No item data.</td></tr>");

            // ── Detail rows ────────────────────────────────────────────────────
            var detailRows = new StringBuilder();
            foreach (var row in _rows)
            {
                string methodLabel = MethodLabel(row.RefundMethod);
                string methodColor = MethodColor(row.RefundMethod);

                detailRows.Append($@"
                  <tr class='data-row' onclick=""toggleDetail('{He(row.ReturnInvoiceNo)}'"")>
                    <td><span class='expand-icon' id='icon-{He(row.ReturnInvoiceNo)}'>▶</span></td>
                    <td><span class='inv-no'>{He(row.ReturnInvoiceNo)}</span></td>
                    <td>{He(row.OriginalInvoiceNo)}</td>
                    <td>{He(row.CustomerName)}</td>
                    <td>{He(row.CashierName)}</td>
                    <td>{row.ReturnDate:dd/MM/yyyy HH:mm}</td>
                    <td class='num'>{row.TotalItemsReturned}</td>
                    <td><span class='badge' style='background:{methodColor}'>{methodLabel}</span></td>
                    <td class='num refund-amt'>{_currency} {row.RefundTotal:N2}</td>
                  </tr>
                  <tr class='detail-row' id='detail-{He(row.ReturnInvoiceNo)}' style='display:none'>
                    <td colspan='9'>
                      <div class='detail-box'>
                        <strong>Line Items:</strong>
                        {BuildLineDetailHtml(row.ReturnInvoiceNo)}
                      </div>
                    </td>
                  </tr>");
            }

            if (!_rows.Any())
                detailRows.Append("<tr><td colspan='9' class='empty'>No returns found for this date range.</td></tr>");

            // ── Method summary rows ────────────────────────────────────────────
            var methodSummary = _rows
                .GroupBy(r => r.RefundMethod)
                .Select(g => (Method: g.Key, Total: g.Sum(r => r.RefundTotal), Count: g.Count()))
                .OrderByDescending(x => x.Total);

            var methodRows = new StringBuilder();
            foreach (var (method, total, count) in methodSummary)
                methodRows.Append($@"
                  <tr>
                    <td><span class='badge' style='background:{MethodColor(method)}'>{MethodLabel(method)}</span></td>
                    <td class='num'>{count}</td>
                    <td class='num'>{_currency} {total:N2}</td>
                  </tr>");

            if (!methodSummary.Any())
                methodRows.Append("<tr><td colspan='3' class='empty'>No data.</td></tr>");

            // ── HTML ──────────────────────────────────────────────────────────
            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
  html, body {{
    margin: 0; padding: 0;
    background: #1e2330;
    font-family: 'Segoe UI', 'Calibri', Arial, sans-serif;
    font-size: 11px;
    color: #1a1a2e;
  }}
  .a4 {{
    width: 1020px;
    background: #fff;
    margin: 24px auto 40px;
    padding: 44px 52px 52px;
    box-sizing: border-box;
    box-shadow: 0 8px 40px rgba(0,0,0,.55);
    border-top: 5px solid #1d4ed8;
  }}
  .doc-header {{
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    margin-bottom: 18px;
    padding-bottom: 14px;
    border-bottom: 2px solid #e2e8f0;
  }}
  .brand-name {{ font-size: 22px; font-weight: 800; color: #1d4ed8; letter-spacing: -0.5px; }}
  .brand-sub  {{ font-size: 10px; color: #64748b; margin-top: 3px; }}
  .doc-meta   {{ text-align: right; font-size: 10px; color: #475569; line-height: 1.9; }}
  .doc-meta .big {{ font-size: 14px; font-weight: 700; color: #1a1a2e; }}

  .report-title {{
    background: #eff6ff;
    border-left: 4px solid #1d4ed8;
    padding: 9px 14px;
    margin-bottom: 20px;
    border-radius: 0 6px 6px 0;
  }}
  .report-title h2 {{ margin: 0 0 2px; font-size: 13px; font-weight: 700;
                      color: #1d4ed8; text-transform: uppercase; letter-spacing: .4px; }}
  .report-title p  {{ margin: 0; font-size: 10px; color: #64748b; }}

  /* KPI cards */
  .kv-grid {{
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 10px;
    margin-bottom: 24px;
  }}
  .kv-card {{
    background: #f8fafc;
    border: 1px solid #e2e8f0;
    border-radius: 6px;
    padding: 10px 14px;
  }}
  .kv-label {{ font-size: 9px; color: #64748b; text-transform: uppercase;
               letter-spacing: .5px; font-weight: 600; margin-bottom: 4px; }}
  .kv-value {{ font-size: 16px; font-weight: 800; color: #1a1a2e; line-height: 1; }}
  .kv-value.blue   {{ color: #1d4ed8; }}
  .kv-value.green  {{ color: #16a34a; }}
  .kv-value.indigo {{ color: #4338ca; }}
  .kv-value.sky    {{ color: #0284c7; }}

  /* Tables */
  .section-head {{
    font-size: 9.5px; font-weight: 700; text-transform: uppercase;
    letter-spacing: .6px; color: #64748b;
    margin: 0 0 6px;
    padding-bottom: 4px;
    border-bottom: 1px solid #e2e8f0;
  }}
  table {{ width: 100%; border-collapse: collapse; font-size: 10.5px; margin-bottom: 22px; }}
  thead th {{
    background: #1d4ed8; color: #fff; font-weight: 600;
    padding: 7px 8px; text-align: left;
    font-size: 9.5px; text-transform: uppercase; letter-spacing: .3px;
  }}
  thead th.num {{ text-align: right; }}
  td.num {{ text-align: right; }}
  td.empty {{ text-align: center; color: #94a3b8; padding: 24px; font-style: italic; }}

  /* Detail rows */
  tr.data-row {{ cursor: pointer; transition: background .1s; }}
  tr.data-row:hover td {{ background: #eff6ff; }}
  tr.data-row td {{ padding: 7px 8px; border-bottom: 1px solid #f0f4fa; vertical-align: middle; }}
  .expand-icon {{ font-size: 9px; color: #94a3b8; user-select: none; }}

  /* Detail drill-down */
  .detail-row td {{ padding: 0 !important; background: #f8fafc !important; }}
  .detail-box {{
    padding: 12px 20px 12px 40px;
    font-size: 10.5px;
    color: #334155;
    border-bottom: 1px solid #e2e8f0;
  }}
  .detail-box table {{ margin: 8px 0 0 0; width: 640px; }}
  .detail-box thead th {{ background: #475569; font-size: 9px; padding: 5px 8px; }}
  .detail-box td {{ padding: 4px 8px; border-bottom: 1px solid #e8edf5; }}

  .inv-no     {{ font-weight: 700; color: #1d4ed8; }}
  .refund-amt {{ font-weight: 700; color: #1d4ed8; }}

  /* Badge */
  .badge {{
    display: inline-block;
    padding: 2px 8px;
    border-radius: 10px;
    font-size: 9px; font-weight: 700;
    color: #fff;
    text-transform: uppercase; letter-spacing: .3px;
  }}

  /* Method summary */
  .method-table {{ width: 340px; }}
  .method-table tbody td {{ padding: 6px 8px; border-bottom: 1px solid #f0f4fa; }}
  .method-table tfoot td {{
    padding: 8px; font-weight: 700; font-size: 11px;
    background: #eff6ff; border-top: 2px solid #1d4ed8;
  }}

  /* Item summary */
  .item-table {{ width: 100%; }}
  .item-table thead th {{ background: #1d4ed8; }}
  .item-table tbody tr:hover td {{ background: #eff6ff; }}
  .item-table tbody td {{ padding: 6px 8px; border-bottom: 1px solid #f0f4fa; }}
  .item-table tfoot td {{
    padding: 8px; font-weight: 700; font-size: 11px;
    background: #eff6ff; border-top: 2px solid #1d4ed8;
  }}

  /* Footer */
  .doc-footer {{
    margin-top: 28px; padding-top: 12px;
    border-top: 2px solid #e2e8f0;
    display: flex; justify-content: space-between;
    font-size: 9.5px; color: #94a3b8;
  }}
  .doc-footer strong {{ color: #475569; }}

  @media print {{
    html, body {{ background: white; }}
    .a4 {{ margin: 0; box-shadow: none; }}
    tr.data-row {{ cursor: default; }}
  }}
</style>
<script>
function toggleDetail(invNo) {{
  var row  = document.getElementById('detail-' + invNo);
  var icon = document.getElementById('icon-' + invNo);
  if (!row) return;
  if (row.style.display === 'none') {{
    row.style.display = 'table-row';
    if (icon) icon.textContent = '▼';
  }} else {{
    row.style.display = 'none';
    if (icon) icon.textContent = '▶';
  }}
}}
</script>
</head>
<body>
<div class='a4'>

  <div class='doc-header'>
    <div>
      <div class='brand-name'>{He(_company)}</div>
      <div class='brand-sub'>Sales Return Report &mdash; {_currency} Operations</div>
    </div>
    <div class='doc-meta'>
      <div class='big'>Sales Returns Report</div>
      <div>Period : {_from:dd/MM/yyyy} &ndash; {_to.AddDays(-1):dd/MM/yyyy}</div>
      <div>Generated : {DateTime.Now:dd/MM/yyyy HH:mm}</div>
      <div>Printed by : {He(Environment.UserName.ToUpper())}</div>
    </div>
  </div>

  <div class='report-title'>
    <h2>↩ Returns &amp; Refunds Report</h2>
    <p>ShriPOS &mdash; {He(_company)} &mdash; Version 1.0</p>
  </div>

  <!-- KPI Cards -->
  <div class='kv-grid'>
    <div class='kv-card'>
      <div class='kv-label'>Total Refunded</div>
      <div class='kv-value blue'>{_currency} {totalRefund:N2}</div>
    </div>
    <div class='kv-card'>
      <div class='kv-label'>Return Transactions</div>
      <div class='kv-value indigo'>{totalReturns}</div>
    </div>
    <div class='kv-card'>
      <div class='kv-label'>Items Returned</div>
      <div class='kv-value sky'>{totalItems}</div>
    </div>
    <div class='kv-card'>
      <div class='kv-label'>Cash Refunds</div>
      <div class='kv-value green'>{_currency} {cashRefund:N2}</div>
    </div>
  </div>

  <!-- Side-by-side: method summary + breakdown -->
  <table style='width:100%; margin-bottom:22px; border-collapse:collapse;'>
    <tr style='vertical-align:top;'>
      <td style='width:360px; padding-right:24px; border:none;'>
        <div class='section-head'>Refund Method Summary</div>
        <table class='method-table'>
          <thead>
            <tr>
              <th>Method</th>
              <th class='num'>Returns</th>
              <th class='num'>Amount</th>
            </tr>
          </thead>
          <tbody>{methodRows}</tbody>
          <tfoot>
            <tr>
              <td><strong>Total</strong></td>
              <td class='num'><strong>{totalReturns}</strong></td>
              <td class='num'><strong>{_currency} {totalRefund:N2}</strong></td>
            </tr>
          </tfoot>
        </table>
      </td>
      <td style='border:none;'>
        <div class='section-head'>Refund Breakdown by Method</div>
        <table>
          <thead>
            <tr>
              <th>Method</th>
              <th class='num'>UPI / Digital</th>
              <th class='num'>Card</th>
              <th class='num'>Bank Transfer</th>
              <th class='num'>Cash</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Amount</td>
              <td class='num'>{_currency} {upiRefund:N2}</td>
              <td class='num'>{_currency} {cardRefund:N2}</td>
              <td class='num'>{_currency} {bankRefund:N2}</td>
              <td class='num'>{_currency} {cashRefund:N2}</td>
            </tr>
          </tbody>
        </table>
      </td>
    </tr>
  </table>

  <!-- ═══ ITEM RETURN SUMMARY (NEW) ════════════════════════════════════════ -->
  <div class='section-head'>Returned Items Summary &nbsp;&mdash;&nbsp; what was returned &amp; how many</div>
  <table class='item-table'>
    <thead>
      <tr>
        <th>Item Name</th>
        <th class='num'>Total Qty Returned</th>
        <th class='num'>Total Refund Amount</th>
      </tr>
    </thead>
    <tbody>{itemSummaryRows}</tbody>
    <tfoot>
      <tr>
        <td><strong>Grand Total</strong></td>
        <td class='num'><strong>{totalItems}</strong></td>
        <td class='num'><strong>{_currency} {totalRefund:N2}</strong></td>
      </tr>
    </tfoot>
  </table>

  <!-- Detail table -->
  <div class='section-head'>Return Transactions Detail &nbsp;(click row to expand items)</div>
  <table>
    <thead>
      <tr>
        <th style='width:20px'></th>
        <th>Return Inv</th>
        <th>Original Inv</th>
        <th>Customer</th>
        <th>Cashier</th>
        <th>Date / Time</th>
        <th class='num'>Items</th>
        <th>Method</th>
        <th class='num'>Refund Amt</th>
      </tr>
    </thead>
    <tbody>{detailRows}</tbody>
  </table>

  <div class='doc-footer'>
    <span>Printed by : <strong>{He(Environment.UserName.ToUpper())}</strong></span>
    <span>ABC &mdash; Sales Return Report &nbsp;|&nbsp; Version 1.0</span>
    <span>{DateTime.Now:dd/MM/yyyy HH:mm:ss}</span>
  </div>
</div>
</body>
</html>";
        }

        // ── Per-return line detail HTML ────────────────────────────────────────
        private string BuildLineDetailHtml(string returnInvoiceNo)
        {
            try
            {
                var lines = SalesReturnRepository.LoadReturnLines(returnInvoiceNo);
                if (!lines.Any()) return "<em>No line data available.</em>";

                var sb = new StringBuilder();
                sb.Append(@"
                  <table>
                    <thead>
                      <tr>
                        <th>Item</th>
                        <th>Barcode</th>
                        <th class='num'>Unit Price</th>
                        <th class='num'>Disc %</th>
                        <th class='num'>Qty Returned</th>
                        <th class='num'>Refund Amt</th>
                      </tr>
                    </thead>
                    <tbody>");

                foreach (var l in lines)
                    sb.Append($@"
                      <tr>
                        <td>{He(l.ItemName)}</td>
                        <td>{He(l.Barcode)}</td>
                        <td class='num'>{_currency} {l.UnitPrice:N2}</td>
                        <td class='num'>{l.DiscountPct:F1}%</td>
                        <td class='num'><strong style='color:#1d4ed8'>{l.ReturnQty}</strong></td>
                        <td class='num' style='font-weight:700;color:#1d4ed8'>{_currency} {l.RefundAmt:N2}</td>
                      </tr>");

                sb.Append("</tbody></table>");
                return sb.ToString();
            }
            catch { return "<em>Could not load line data.</em>"; }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SUMMARY STRIP
        // ══════════════════════════════════════════════════════════════════════
        private string BuildSummaryStrip()
        {
            if (!_rows.Any()) return "No returns in selected period.";
            decimal total = _rows.Sum(r => r.RefundTotal);
            int cnt = _rows.Count;
            int items = _rows.Sum(r => r.TotalItemsReturned);
            return $"Returns: {cnt}  |  Items: {items}  |  Total Refunded: {_currency} {total:N2}";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CSV EXPORT
        // ══════════════════════════════════════════════════════════════════════
        private void BtnCsv_Click(object sender, EventArgs e)
        {
            var sd = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"SalesReturns_{_from:yyyyMMdd}_{_to.AddDays(-1):yyyyMMdd}.csv",
                Title = "Export Sales Return Report as CSV",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (sd.ShowDialog(this) != DialogResult.OK) return;

            var sb = new StringBuilder();
            sb.AppendLine("ReturnInvoice,OriginalInvoice,Customer,Cashier,Date," +
                          "RefundMethod,ItemsReturned,RefundTotal");

            foreach (var r in _rows)
                sb.AppendLine(
                    $"{Csv(r.ReturnInvoiceNo)},{Csv(r.OriginalInvoiceNo)}," +
                    $"{Csv(r.CustomerName)},{Csv(r.CashierName)}," +
                    $"{r.ReturnDate:yyyy-MM-dd HH:mm:ss}," +
                    $"{Csv(MethodLabel(r.RefundMethod))}," +
                    $"{r.TotalItemsReturned},{r.RefundTotal:F2}");

            File.WriteAllText(sd.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Exported to:\n{sd.FileName}", "Export Complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════
        private static string MethodLabel(string m)
        {
            switch (m?.ToLower())
            {
                case "cash": return "Cash";
                case "upi": return "UPI/Digital";
                case "card": return "Card";
                case "bank": return "Bank Transfer";
                default: return m ?? "Cash";
            }
        }

        private static string MethodColor(string m)
        {
            switch (m?.ToLower())
            {
                case "cash": return "#16a34a";
                case "upi": return "#1d4ed8";
                case "card": return "#7c3aed";
                case "bank": return "#0891b2";
                default: return "#64748b";
            }
        }

        private static string He(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;")
                    .Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            return s.Contains(',') || s.Contains('"')
                ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }

        private static Button MakeBtn(string text, Color bg, Point loc, Size sz)
        {
            var b = new Button
            {
                Text = text,
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = loc,
                Size = sz,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static void AddLabel(Control parent, string text, Point loc)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = loc
            });
        }
    }
}