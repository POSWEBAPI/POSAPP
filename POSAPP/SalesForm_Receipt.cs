//// ╔══════════════════════════════════════════════════════════════════════════╗
//// ║  SalesForm_Receipt.cs  — PARTIAL CLASS                                  ║
//// ║                                                                          ║
//// ║  • ShowReceiptAfterSave()  — called by btnTenderSale after every        ║
//// ║    completed sale / saved pending invoice.                               ║
//// ║  • Reprint button in the footer bar.                                     ║
//// ║  • _lastReceiptData field so "Reprint Last" always works.               ║
//// ╚══════════════════════════════════════════════════════════════════════════╝

//using POSAPP.Invoice;
//using System;
//using System.Drawing;
//using System.Windows.Forms;

//namespace POSAPP
//{
//    public partial class SalesForm
//    {
//        // ── Stores the last completed receipt so Reprint always has data ──────
//        private ReceiptData _lastReceiptData = null;

//        // ── Footer reprint button (built once in BuildFloatFooterLabel) ───────
//        private Button _btnReprint;

//        // ══════════════════════════════════════════════════════════════════════
//        //  BUILD REPRINT BUTTON — call once from SalesForm_Load
//        // ══════════════════════════════════════════════════════════════════════
//        internal void BuildReprintButton()
//        {
//            _btnReprint = new Button
//            {
//                Name      = "btnReprint",
//                Text      = "🖨 Reprint Last",
//                Font      = new Font("Segoe UI", 8.5F, FontStyle.Bold),
//                ForeColor = Color.FromArgb(251, 146, 60),   // AccOrange
//                BackColor = Color.FromArgb(50, 38, 18),
//                FlatStyle = FlatStyle.Flat,
//                Size      = new Size(118, 26),
//                Cursor    = Cursors.Hand,
//                Enabled   = false,                           // enabled once a sale completes
//                Visible   = true
//            };
//            _btnReprint.FlatAppearance.BorderColor = Color.FromArgb(120, 70, 20);
//            _btnReprint.FlatAppearance.BorderSize  = 1;
//            _btnReprint.Click += (s, e) => ReprintLast();

//            panelFooterBar.Controls.Add(_btnReprint);

//            // Re-position whenever footer resizes
//            panelFooterBar.SizeChanged += (s, e) => PositionReprintButton();
//            PositionReprintButton();
//        }

//        private void PositionReprintButton()
//        {
//            if (_btnReprint == null || panelFooterBar == null) return;
//            int h   = panelFooterBar.Height;
//            int btnH = _btnReprint.Height;
//            int y    = Math.Max(2, (h - btnH) / 2);
//            // Place just to the left of the Day-End button (or right edge minus offset)
//            _btnReprint.Location = new Point(panelFooterBar.Width - 270, y);
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  ShowReceiptAfterSave
//        //  Call this after every completed sale AND after every pending-invoice
//        //  save so the operator can immediately print.
//        //
//        //  isPendingSave = true  → labelled "Invoice Saved" (pending hold)
//        //  isPendingSave = false → labelled "Sale Complete"
//        // ══════════════════════════════════════════════════════════════════════
//        internal void ShowReceiptAfterSave(ReceiptData receipt, bool isPendingSave = false)
//        {
//            if (receipt == null) return;

//            // Cache for Reprint
//            _lastReceiptData = receipt;
//            if (_btnReprint != null) _btnReprint.Enabled = true;

//            // Show the unified print dialog (A4 preview + print)
//            PrintReceiptDialog.Show(this, receipt);
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  ReprintLast — triggered by footer Reprint button or menu
//        // ══════════════════════════════════════════════════════════════════════
//        internal void ReprintLast()
//        {
//            if (_lastReceiptData == null)
//            {
//                ShowStatus("No receipt to reprint — complete a sale first.", false);
//                return;
//            }
//            ShowStatus($"Reprinting {_lastReceiptData.InvoiceNo}…", true);
//            PrintReceiptDialog.Show(this, _lastReceiptData);
//        }
//    }
//}
