//// ╔══════════════════════════════════════════════════════════════════════════╗
//// ║  SalesForm_TenderAndLoad.cs  — PARTIAL CLASS                            ║
//// ║                                                                          ║
//// ║  REPLACES the SalesForm_Load() and btnTenderSale_Click() bodies that    ║
//// ║  exist in the original SalesForm.cs.                                    ║
//// ║                                                                          ║
//// ║  Changes vs original:                                                    ║
//// ║   • SalesForm_Load: removed SyncProductsFromApiInBackgroundAsync call.  ║
//// ║     Products are now served by D365SyncService writing to ShriPOS.db.   ║
//// ║   • SalesForm_Load: calls BuildReprintButton().                         ║
//// ║   • btnTenderSale_Click (D365 pending path):                            ║
//// ║       – calls ShowReceiptAfterSave() so operator can print immediately. ║
//// ║   • btnTenderSale_Click (payment path):                                 ║
//// ║       – calls ShowReceiptAfterSave() instead of PrintReceiptDialog.Show ║
//// ╚══════════════════════════════════════════════════════════════════════════╝

//using POSAPP.Invoice;
//using POSAPP.Reports;
//using POSAPP.Shift;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text.Json;
//using System.Threading.Tasks;
//using System.Windows.Forms;

//namespace POSAPP
//{
//    public partial class SalesForm
//    {
//        // ══════════════════════════════════════════════════════════════════════
//        //  FORM LOAD
//        // ══════════════════════════════════════════════════════════════════════
//        //private async void SalesForm_Load(object sender, EventArgs e)
//        //{
//        //    lblOperator.Text = "ADMIN";

//        //    if (string.IsNullOrWhiteSpace(lblInvoiceNo.Text) || lblInvoiceNo.Text == "INV-")
//        //        lblInvoiceNo.Text = SalesRepository.NextInvoiceNo();

//        //    lblTime.Text = DateTime.Now.ToString("HH:mm");
//        //    lblDate.Text = DateTime.Now.ToString("ddd, dd MMM");

//        //    var clock = new System.Windows.Forms.Timer { Interval = 30000 };
//        //    clock.Tick += (s, ev) =>
//        //    {
//        //        lblTime.Text = DateTime.Now.ToString("HH:mm");
//        //        lblDate.Text = DateTime.Now.ToString("ddd, dd MMM");
//        //    };
//        //    clock.Start();

//        //    txtSearch.TextChanged   += TxtSearch_TextChanged;
//        //    txtSearch.KeyDown       += TxtSearch_KeyDown;
//        //    txtBarcode.KeyDown      += TxtBarcode_KeyDown;
//        //    txtBarcode.KeyPress     += TxtBarcode_KeyPress;
//        //    txtCustomer.TextChanged += TxtCustomer_TextChanged;
//        //    txtCustomer.KeyDown     += TxtCustomer_KeyDown;

//        //    ApplyModernStyle(txtSearch,   "Search products…",  AccBlue,   out _searchWrapper);
//        //    ApplyModernStyle(txtBarcode,  "Scan barcode…",     AccCyan,   out _barcodeWrapper);
//        //    ApplyModernStyle(txtCustomer, "Search customer…",  AccPurple, out _customerWrapper);

//        //    BuildCustomerDetailsButton();

//        //    this.Controls.Add(listSearchResults);
//        //    listSearchResults.BringToFront();

//        //    BuildCustomerDropdown();
//        //    SetActiveSplit("cash");
//        //    LoadCompanyInfo();

//        //    if (!System.IO.File.Exists(_dbPath))
//        //    {
//        //        ShowStatus($"Database not found: {_dbPath}", false);
//        //        MessageBox.Show(
//        //            $"Database file not found!\n\nExpected:\n{_dbPath}\n\n" +
//        //            "Copy ShriPOS.db next to POSAPP.exe and restart.",
//        //            "Database Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
//        //        return;
//        //    }

//        //    // ── Phase 1: fast local load ──────────────────────────────────────
//        //    ShowStatus("Loading products from local cache…", true);
//        //    await Task.Run(() => LoadSalesFrequency());

//        //    // Read-only load from ShriPOS.db  — no network calls.
//        //    // D365SyncService keeps the DB fresh every 10 minutes.
//        //    await LoadProductsFromD365SQLiteAsync();

//        //    nudDiscount.Value = _defaultDiscountPct;
//        //    RefreshCart();
//        //    UpdateTotals();
//        //    RepositionTitleButtons();

//        //    try { SalesRepository.EnsureSchema(); }
//        //    catch (Exception ex) { ShowStatus("DB schema error: " + ex.Message, false); }

//        //    try { SalesRepository.EnsurePendingInvoiceSchema(); }
//        //    catch (Exception ex) { ShowStatus("Pending schema error: " + ex.Message, false); }

//        //    try
//        //    {
//        //        SalesRepository.EnsureRecentSalesSchema();
//        //        LoadTodayRecentSales();
//        //    }
//        //    catch (Exception ex) { ShowStatus("Recent sales load error: " + ex.Message, false); }

//        //    try
//        //    {
//        //        _scheduler = new DayEndScheduler(this, _companyId, _companyName, _currencySymbol);
//        //        _scheduler.Start();
//        //    }
//        //    catch (Exception ex) { ShowStatus("Scheduler error: " + ex.Message, false); }

//        //    BuildFloatFooterLabel();

//        //    // ── Reprint button (new) ──────────────────────────────────────────
//        //    BuildReprintButton();

//        //    panelFooterBar.Controls.Add(btnPrintLast);
//        //    panelFooterBar.SizeChanged += (s, ev) => PositionFooterButtons();
//        //    PositionFooterButtons();
//        //    BuildNumpadDisplay();

//        //    // ── Phase 2: background (payment methods only — no product sync) ──
//        //    _ = LoadPaymentMethodsAsync();

//        //    // ─────────────────────────────────────────────────────────────────
//        //    // NOTE: SyncProductsFromApiInBackgroundAsync is intentionally NOT
//        //    // called here.  Network sync now runs in the D365SyncService Windows
//        //    // Service every 10 minutes, independent of the POS UI.
//        //    // ─────────────────────────────────────────────────────────────────
//        //}

//        // ══════════════════════════════════════════════════════════════════════
//        //  TENDER / SAVE BUTTON
//        // ══════════════════════════════════════════════════════════════════════
//        private void btnTenderSale_Click(object sender, EventArgs e)
//        {
//            if (_cart.Count == 0) { ShowStatus("Cart is empty.", false); return; }

//            // ── D365 normal mode: save as pending invoice ─────────────────────
//            if (_isD365Mode && !_isPendingInvoiceMode)
//            {
//                string customer  = GetRealText(txtCustomer);
//                if (string.IsNullOrWhiteSpace(customer)) customer = "Walk-in";
//                string invoiceNo = lblInvoiceNo.Text;
//                decimal grand    = GrandTotal();

//                var dtos = _cart.Select(i => new CartItemDto
//                {
//                    Name          = i.Name,
//                    OriginalPrice = i.OriginalPrice,
//                    Price         = i.Price,
//                    Qty           = i.Qty,
//                    DiscountPct   = i.DiscountPct,
//                    Barcode       = i.Barcode
//                }).ToList();

//                try
//                {
//                    SalesRepository.EnsurePendingInvoiceSchema();
//                    SalesRepository.ConsumeInvoiceNo();
//                    SalesRepository.UpsertPendingInvoice(
//                        invoiceNo, customer, grand,
//                        JsonSerializer.Serialize(dtos), _companyId);

//                    AddToRecentSales(invoiceNo, grand);
//                    DashboardEventBus.Notify();

//                    // ── Build receipt data and show print dialog ──────────────
//                    var receiptForPrint = PrepareReceiptData();
//                    receiptForPrint.InvoiceNo    = invoiceNo;
//                    receiptForPrint.CustomerName = customer;
//                    receiptForPrint.GrandTotal   = grand;

//                    // Show styled "saved" confirmation then offer print
//                    ShowInvoiceSavedDialog(invoiceNo, customer, grand);

//                    // Show A4 receipt/print dialog (NEW)
//                    ShowReceiptAfterSave(receiptForPrint, isPendingSave: true);

//                    ResetSale(generateNewInvoiceNo: true);
//                }
//                catch (Exception ex) { ShowStatus("Save error: " + ex.Message, false); }
//                return;
//            }

//            // ── Payment flow (normal sale or pending invoice payment) ──────────
//            decimal grandTotal = GrandTotal();
//            decimal splitSum   = _splitCash + _splitUpi + _splitCard;

//            if (splitSum < grandTotal)
//            {
//                ShowStatus($"Insufficient. Need {Fmt(grandTotal - splitSum)} more.", false);
//                return;
//            }

//            if (_splitCash > 0 && !ShiftState.IsOpen)
//            {
//                MessageBox.Show(
//                    "No shift is open.\n\nOpen a shift via Float Entry (F8) before processing cash.",
//                    "⚠ No Shift Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
//                return;
//            }

//            decimal change     = splitSum - grandTotal;
//            var receiptData    = PrepareReceiptData();
//            string invNo       = lblInvoiceNo.Text;

//            AddToRecentSales(invNo, grandTotal);

//            try
//            {
//                SalesRepository.ConsumeInvoiceNo();
//                SalesRepository.SaveSale(receiptData, _companyId);
//                UpdateStoreStockAfterSale();
//            }
//            catch (Exception ex) { ShowStatus("Sale save error: " + ex.Message, false); }

//            try { SalesRepository.MarkInvoicePaid(invNo); } catch { }

//            ShiftState.RecordSale(_splitCash, change > 0 ? change : 0m, _splitUpi, _splitCard);
//            RefreshFloatLabel();

//            _wasCompletedFromPendingInvoice = _isPendingInvoiceMode;

//            // ── Show A4 receipt print dialog (replaces old PrintReceiptDialog.Show) ──
//            ShowReceiptAfterSave(receiptData, isPendingSave: false);

//            ResetSale(generateNewInvoiceNo: true);
//            DashboardEventBus.Notify();
//        }
//    }
//}
