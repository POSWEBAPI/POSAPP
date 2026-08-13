using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

// ============================================================================
// PurchaseOrderForm.cs
// Single-file port of the React "Purchase Order" screen (list + create/edit +
// Return Order flow) to WinForms, written to slot into the existing POSAPP
// Dashboard the same way SalesForm / SalesReturnForm do.
//
// Wire-up in Dashboard (see bottom of this file for the exact snippet):
//   ShowPage(new PurchaseOrderForm(_selectedCompanyId, _currencySymbol));
//
// NOTE: Update ApiBaseUrl below to point at your backend. Endpoint paths are
// guesses based on the original React api/* modules (purchaseOrderApi,
// vendorApi, itemApi, uomApi, taxCategoryApi, discountApi, chargesApi,
// paymentTermApi, currencyApi, storeStockApi) — adjust the route strings in
// PoApiClient to match your actual controllers.
// ============================================================================

namespace POSAPP.Sales
{
    #region Models

    public static class PoType
    {
        public const string PurchaseOrder = "Purchase Order";
        public const string ReturnOrder = "Return Order";
    }

    public static class PoStatus
    {
        public const string Open = "Open";
        public const string Approved = "Approved";
        public const string Confirmed = "Confirmed";
        public const string Closed = "Closed";
        public const string Cancelled = "Cancelled";
    }

    public static class ReturnReasonOptions
    {
        public static readonly string[] Values =
        {
            "Damaged Goods", "Wrong Item Shipped", "Excess Stock", "Quality Issue",
            "Expired Product", "Pricing Discrepancy", "Customer Cancellation", "Other"
        };
    }

    public static class DispositionOptions
    {
        public static readonly string[] Values =
        {
            "Credit", "Scrap", "Return to Vendor", "Replace", "Repair", "Restock"
        };
    }

    public class VendorModel
    {
        public int VendorID { get; set; }
        public string VendorName { get; set; } = "";
        public string Address { get; set; } = "";
        public int? PaymentTermID { get; set; }
        public int? CurrencyID { get; set; }
    }

    public class PackSizeModel
    {
        public int UomId { get; set; }
        public string PackDescription { get; set; } = "";
        public decimal RetailPrice { get; set; }
        public decimal UnitsPerPack { get; set; } = 1;
    }

    public class ItemModel
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public string Sku { get; set; } = "";
        public string Barcode { get; set; } = "";
        public decimal CostPrice { get; set; }
        public decimal SellingPrice { get; set; }
        public int BaseUOM { get; set; }
        public int PurchaseTaxID { get; set; }
        public List<PackSizeModel> PackSizes { get; set; } = new();
    }

    public class UomModel
    {
        public int UomID { get; set; }
        public string UomDescription { get; set; } = "";
    }

    public class TaxModel
    {
        public int TaxId { get; set; }
        public string TaxCode { get; set; } = "";
        public decimal TaxPercentage { get; set; }
    }

    public class DiscountModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Percentage { get; set; }
        public bool? PurchaseSales { get; set; }
    }

    public class PaymentTermModel
    {
        public int PaymentTermID { get; set; }
        public string Description { get; set; } = "";
    }

    public class CurrencyModel
    {
        public int CurrencyID { get; set; }
        public string CurrencyName { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public string CurrencySymbol { get; set; } = "";
    }

    public class POLineModel
    {
        public int? POLineID { get; set; }
        public int LineNo { get; set; }
        public int ItemId { get; set; }
        public string ItemSearch { get; set; } = "";
        public decimal Qty { get; set; } = 1;
        public int UOM { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercentage { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal AdditionalDiscountAmount { get; set; }
        public int? TaxID { get; set; }
        public decimal TaxPercentage { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal Total { get; set; }

        public bool IsReturnItem { get; set; }
        public string? SourceLineKey { get; set; } // "{InvoiceID}-{InvoiceLineID}"

        public POLineModel Clone() => (POLineModel)MemberwiseClone();
    }

    public class PurchaseOrderModel
    {
        public int? POId { get; set; }
        public int CompanyID { get; set; } = 1;
        public int StoreID { get; set; } = 1;
        public int VendorId { get; set; }
        public int CurrencyID { get; set; }
        public string PONumber { get; set; } = "";
        public DateTime PODate { get; set; } = DateTime.Today;
        public string POType { get; set; } = PoType.PurchaseOrder;
        public decimal PODiscountAmt { get; set; }
        public int SelectedPODiscountID { get; set; }
        public decimal POCharges { get; set; }
        public int? PaymentTermID { get; set; }
        public string DelieveryAddress { get; set; } = "";
        public DateTime? DelieveryDate { get; set; }
        public string Status { get; set; } = PoStatus.Open;
        public decimal POAmount { get; set; }

        public string ReturnReasonCode { get; set; } = "";
        public string RMANumber { get; set; } = "";
        public string DispositionCode { get; set; } = "";

        public List<POLineModel> Lines { get; set; } = new() { new POLineModel() };

        public PurchaseOrderModel Clone()
        {
            var copy = (PurchaseOrderModel)MemberwiseClone();
            copy.Lines = Lines.Select(l => l.Clone()).ToList();
            return copy;
        }
    }

    public class InvoiceLineModel
    {
        public int? InvoiceLineID { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public decimal Qty { get; set; }
        public int UOM { get; set; }
        public decimal UnitPrice { get; set; }
        public string BatchNo { get; set; } = "";
    }

    public class InvoiceModel
    {
        public int InvoiceID { get; set; }
        public string InvoiceNo { get; set; } = "";
        public string PONumber { get; set; } = "";
        public List<InvoiceLineModel> PoInvoiceLines { get; set; } = new();
    }

    public class ReturnLineRow
    {
        public string SourceLineKey { get; set; } = "";
        public int InvoiceID { get; set; }
        public string InvoiceNo { get; set; } = "";
        public string PONumber { get; set; } = "";
        public int ItemId { get; set; }
        public string ItemSearch { get; set; } = "";
        public decimal PurchasedQty { get; set; }
        public decimal ReturnQty { get; set; }
        public int UOM { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercentage { get; set; }
    }

    public class ProcessReturnLine
    {
        public int InvoiceID { get; set; }
        public int InvoiceLineID { get; set; }
        public decimal ReturnQty { get; set; }
    }

    public class StockMovementPayload
    {
        public int ItemID { get; set; }
        public string ItemCode { get; set; } = "";
        public int CompanyID { get; set; }
        public int? StoreID { get; set; }
        public string Uom { get; set; } = "";
        public decimal ItemQty { get; set; }
        public decimal ReturnQty { get; set; }
        public int? TransactionID { get; set; }
    }

    #endregion

    #region API Client

    /// <summary>
    /// Thin REST client mirroring the axios calls in the original React
    /// screen. Adjust ApiBaseUrl and the route strings to match your backend.
    /// </summary>
    public class PoApiClient
    {
        public static string ApiBaseUrl = "https://localhost:7022/"; // TODO: point at your API
        private readonly HttpClient _http;

        public PoApiClient(string? bearerToken = null)
        {
            _http = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(bearerToken))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private async Task<T?> GetAsync<T>(string url)
        {
            var res = await _http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            return JsonConvert.DeserializeObject<T>(await res.Content.ReadAsStringAsync(), _jsonSettings);
        }
        private async Task<HttpResponseMessage> PostAsync(string url, object payload)
        {
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            return await _http.PostAsync(url, content);
        }

        private async Task<HttpResponseMessage> PutAsync(string url, object payload)
        {
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            return await _http.PutAsync(url, content);
        }

        public async Task<List<VendorModel>> GetAllVendorsAsync(int companyId)
            => await GetAsync<List<VendorModel>>($"/api/master/vendor?companyId={companyId}") ?? new();

        public async Task<List<ItemModel>> GetItemsAsync(int companyId)
            => await GetAsync<List<ItemModel>>($"/api/master/item?companyId={companyId}") ?? new();

        public async Task<ItemModel?> GetItemByIdAsync(int itemId)
            => await GetAsync<ItemModel>($"/api/master/item/{itemId}");

        public async Task<List<UomModel>> GetUomsAsync()
            => await GetAsync<List<UomModel>>("/api/master/uom") ?? new();

        public async Task<List<PaymentTermModel>> GetPaymentTermsAsync()
            => await GetAsync<List<PaymentTermModel>>("/api/master/paymentterm") ?? new();

        public async Task<List<DiscountModel>> GetDiscountsAsync(int companyId)
            => await GetAsync<List<DiscountModel>>($"/api/master/discount?companyId={companyId}") ?? new();

        public async Task<List<TaxModel>> GetTaxesAsync(int companyId)
            => await GetAsync<List<TaxModel>>($"/api/master/taxcategory?companyId={companyId}") ?? new();

        public async Task<List<CurrencyModel>> GetCurrenciesAsync()
            => await GetAsync<List<CurrencyModel>>("/api/master/currency") ?? new();

       
        public async Task<List<PurchaseOrderModel>> GetPurchaseOrdersAsync()
    => await GetAsync<List<PurchaseOrderModel>>("/api/purchaseorders") ?? new();

        public async Task<PurchaseOrderModel?> GetPurchaseOrderByIdAsync(int id)
            => await GetAsync<PurchaseOrderModel>($"/api/purchaseorders/{id}");

        public async Task<List<POLineModel>> GetPurchaseOrderLinesAsync(int poId)
            => await GetAsync<List<POLineModel>>($"/api/purchaseorders/GetPolines?poid={poId}") ?? new();

        public async Task<(bool ok, int? newId, string? error)> CreatePurchaseOrderAsync(PurchaseOrderModel payload)
        {
            var res = await PostAsync("/api/purchaseorders", payload);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return (false, null, body);
            try
            {
                dynamic parsed = JsonConvert.DeserializeObject(body)!;
                int? id = (int?)(parsed.poId ?? parsed.POId ?? parsed.id);
                return (true, id, null);
            }
            catch { return (true, null, null); }
        }

        public async Task<(bool ok, string? error)> UpdatePurchaseOrderAsync(int id, PurchaseOrderModel payload)
        {
            var res = await PutAsync($"/api/purchaseorders/{id}", payload);
            var body = await res.Content.ReadAsStringAsync();
            return res.IsSuccessStatusCode ? (true, null) : (false, body);
        }

        public async Task<bool> DeletePurchaseOrderAsync(int id)
            => (await _http.DeleteAsync($"/api/purchaseorders/{id}")).IsSuccessStatusCode;

        public async Task<List<InvoiceModel>> GetInvoicesByVendorAsync(int vendorId)
            => await GetAsync<List<InvoiceModel>>($"/api/ap/purchaseorder/invoices-by-vendor/{vendorId}") ?? new();

        public async Task<(bool ok, string? error)> ProcessReturnAsync(List<ProcessReturnLine> lines)
        {
            var res = await PostAsync("/api/ap/purchaseorder/process-return", lines);
            var body = await res.Content.ReadAsStringAsync();
            return res.IsSuccessStatusCode ? (true, null) : (false, body);
        }

        public async Task<(bool ok, string? error)> UpsertOpeningStockAsync(StockMovementPayload payload)
        {
            var res = await PostAsync("/api/stock", payload);
            var body = await res.Content.ReadAsStringAsync();
            return res.IsSuccessStatusCode ? (true, null) : (false, body);
        }

        public async Task<(bool ok, string? error)> ProcessPurchaseReturnStockAsync(StockMovementPayload payload)
        {
            var res = await PostAsync("/api/stockmovement/purchase-return", payload);
            var body = await res.Content.ReadAsStringAsync();
            return res.IsSuccessStatusCode ? (true, null) : (false, body);
        }
    }

    #endregion

    #region Calculation helpers

    public enum DiscountMode { Auto, Percent, Amount }

    public static class LineCalculator
    {
        /// <summary>Mirrors calculateLine() from the React screen.</summary>
        public static POLineModel CalculateLine(POLineModel line, DiscountMode mode = DiscountMode.Auto)
        {
            var subtotal = line.Qty * line.UnitPrice;
            var discountPercentInput = line.DiscountPercentage;
            var discountAmountInput = line.DiscountAmount;

            decimal discountPercent = discountPercentInput;
            decimal discountAmount;

            if (mode == DiscountMode.Percent)
                discountAmount = subtotal * discountPercentInput / 100m;
            else if (mode == DiscountMode.Amount)
            {
                discountAmount = discountAmountInput;
                discountPercent = subtotal > 0 ? discountAmountInput / subtotal * 100m : 0;
            }
            else if (discountPercentInput > 0)
                discountAmount = subtotal * discountPercentInput / 100m;
            else if (discountAmountInput > 0 && subtotal > 0)
            {
                discountAmount = discountAmountInput;
                discountPercent = discountAmountInput / subtotal * 100m;
            }
            else discountAmount = 0;

            var additionalDiscountAmount = line.AdditionalDiscountAmount;
            var taxableAmount = subtotal - discountAmount - additionalDiscountAmount;
            var taxAmount = taxableAmount * line.TaxPercentage / 100m;

            line.DiscountPercentage = Math.Round(discountPercent, 2);
            line.DiscountAmount = Math.Round(discountAmount, 2);
            line.TaxAmount = taxAmount;
            line.Total = taxableAmount + taxAmount;
            return line;
        }

        public class TotalsResult
        {
            public decimal PreTaxSubtotal, PODiscountAmt, TaxableAmount, VatAmount, GrandTotal;
        }

        /// <summary>Mirrors the PO-level discount + proportional VAT scaling block.</summary>
        public static TotalsResult CalculateTotals(IEnumerable<POLineModel> lines, decimal poDiscountPercentage, decimal poCharges)
        {
            var list = lines.ToList();
            var preTaxSubtotal = list.Sum(x => x.Total - x.TaxAmount);
            var podiscountAmt = preTaxSubtotal * poDiscountPercentage / 100m;
            var taxableAmount = preTaxSubtotal - podiscountAmt;
            var perLineTaxTotal = list.Sum(x => x.TaxAmount);
            var vatAmount = preTaxSubtotal > 0 ? perLineTaxTotal * (taxableAmount / preTaxSubtotal) : 0;
            return new TotalsResult
            {
                PreTaxSubtotal = preTaxSubtotal,
                PODiscountAmt = podiscountAmt,
                TaxableAmount = taxableAmount,
                VatAmount = vatAmount,
                GrandTotal = taxableAmount + vatAmount + poCharges
            };
        }

        public static decimal GetUnitsPerPackForLine(POLineModel line, ItemModel? item)
        {
            if (item == null) return 1;
            if (item.BaseUOM == line.UOM) return 1;
            var pack = item.PackSizes.FirstOrDefault(p => p.UomId == line.UOM);
            return pack != null && pack.UnitsPerPack > 0 ? pack.UnitsPerPack : 1;
        }
    }

    #endregion

    /// <summary>
    /// Purchase Order / Return Order screen. Embed via Dashboard.ShowPage(new
    /// PurchaseOrderForm(companyId, currencySymbol)) the same way SalesForm /
    /// SalesReturnForm are used.
    /// </summary>
    public partial class PurchaseOrderForm : Form
    {
        // ---- palette (kept close to the Dashboard's own palette) ----
        static readonly Color C_Navy = Color.FromArgb(23, 32, 58);
        static readonly Color C_Blue = Color.FromArgb(59, 130, 246);
        static readonly Color C_Green = Color.FromArgb(34, 197, 130);
        static readonly Color C_Red = Color.FromArgb(239, 68, 68);
        static readonly Color C_Slate = Color.FromArgb(120, 132, 156);
        static readonly Color C_LightBg = Color.FromArgb(247, 249, 253);
        static readonly Color C_Border = Color.FromArgb(228, 233, 245);

        private readonly PoApiClient _api;
        private readonly int _companyId;
        private readonly int _storeId;

        // master data
        private List<VendorModel> _vendors = new();
        private List<ItemModel> _items = new();
        private List<UomModel> _uoms = new();
        private List<TaxModel> _taxes = new();
        private List<DiscountModel> _discounts = new();
        private List<PaymentTermModel> _paymentTerms = new();
        private List<CurrencyModel> _currencies = new();
        private List<PurchaseOrderModel> _purchaseOrders = new();

        // edit state
        private PurchaseOrderModel _form = new();
        private int? _editId;
        private bool _isSaving;
        private bool _isConfirming;
        private string _currencySymbol = "";
        private readonly HashSet<string> _processedReturnKeys = new();

        // return-order picker state
        private List<InvoiceModel> _returnInvoices = new();
        private List<ReturnLineRow> _returnLines = new();

        // ---- controls: list view ----
        private Panel _pnlList = new() { Dock = DockStyle.Fill };
        private TextBox _txtSearch = new() { Width = 240, PlaceholderText = "Quick search..." };
        private Button _btnAdd = new() { Text = "+ New Purchase Order", AutoSize = true };
        private DataGridView _grid = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        // ---- controls: edit view ----
        private Panel _pnlEdit = new() { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
        private Button _btnBack = new() { Text = "← Back", AutoSize = true };
        private Label _lblEditTitle = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, ForeColor = C_Navy };
        private Button _btnSave = new() { Text = "SAVE", AutoSize = true, BackColor = C_Navy, ForeColor = Color.White };
        private Button _btnConfirm = new() { Text = "CONFIRM", AutoSize = true, BackColor = C_Green, ForeColor = Color.White };

        private ComboBox _cmbPOType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private ComboBox _cmbVendor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private DateTimePicker _dtPODate = new() { Width = 200, Format = DateTimePickerFormat.Short };
        private DateTimePicker _dtDeliveryDate = new() { Width = 200, Format = DateTimePickerFormat.Short };
        private ComboBox _cmbPaymentTerm = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private TextBox _txtAddress = new() { Width = 260, Height = 60, Multiline = true };
        private ComboBox _cmbPODiscount = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private TextBox _txtCharges = new() { Width = 120, Text = "0" };
        private ComboBox _cmbReturnReason = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private TextBox _txtRMA = new() { Width = 200 };
        private ComboBox _cmbDisposition = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private Label _lblGrandTotal = new() { Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.FromArgb(234, 88, 12), AutoSize = true };
        private Label _lblStatus = new() { AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        private Control _pnlReturnPicker = new Panel { Visible = false };
        private DataGridView _gridReturnLines = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        private DataGridView _gridLines = new()
        {
            Height = 260,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        private Button _btnAddLine = new() { Text = "+ Add Line", AutoSize = true };

        private List<PurchaseOrderModel> _pagedSource = new();
        private int _currentPage = 1;
        private int _pageSize = 10;
        private Label _lblPage = new() { AutoSize = true };
        private Button _btnPrev = new() { Text = "Prev", AutoSize = true };
        private Button _btnNext = new() { Text = "Next", AutoSize = true };

        public PurchaseOrderForm(int companyId, string currencySymbol = "")
        {
            _companyId = companyId;
            _storeId = companyId; // adjust if you track storeId separately
            _currencySymbol = currencySymbol;
            _api = new PoApiClient();

            Text = "Purchase Orders";
            BackColor = C_LightBg;
            Dock = DockStyle.Fill;          // so it behaves when embedded by ShowPage()
            FormBorderStyle = FormBorderStyle.None;

            BuildListView();
            BuildEditView();
            Controls.Add(_pnlEdit);
            Controls.Add(_pnlList);

            Load += async (_, __) => await LoadMasterDataAsync();
        }

        // ============================================================
        // LIST VIEW
        // ============================================================

        private void BuildListView()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(10) };
            top.Controls.Add(_txtSearch);
            var right = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 0, 10, 0) };
            right.Controls.Add(_btnAdd);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PONumber", HeaderText = "PO Number", Width = 160 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Vendor", HeaderText = "Vendor", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Date", Width = 100 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", Width = 110 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount", Width = 120 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 100 });
            _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Edit", HeaderText = "", Text = "Edit", UseColumnTextForButtonValue = true, Width = 60 });
            _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Delete", HeaderText = "", Text = "Delete", UseColumnTextForButtonValue = true, Width = 65 });

            var pager = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            pager.Controls.Add(_btnNext);
            pager.Controls.Add(_btnPrev);
            pager.Controls.Add(_lblPage);

            _pnlList.Controls.Add(_grid);
            _pnlList.Controls.Add(pager);
            _pnlList.Controls.Add(right);
            _pnlList.Controls.Add(top);

            _btnAdd.Click += (_, __) => OpenEditor(null);
            _txtSearch.TextChanged += (_, __) => { _currentPage = 1; RefreshGrid(); };
            _btnPrev.Click += (_, __) => { if (_currentPage > 1) { _currentPage--; RefreshGrid(); } };
            _btnNext.Click += (_, __) => { _currentPage++; RefreshGrid(); };
            _grid.CellContentClick += async (_, e) =>
            {
                if (e.RowIndex < 0) return;
                var po = (PurchaseOrderModel)_grid.Rows[e.RowIndex].Tag!;
                var col = _grid.Columns[e.ColumnIndex].Name;
                if (col == "Edit") OpenEditor(po);
                else if (col == "Delete") await DeletePoAsync(po);
            };
        }

        private async Task LoadMasterDataAsync()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
               
                _purchaseOrders = await _api.GetPurchaseOrdersAsync();

                //_vendors = await _api.GetAllVendorsAsync(_companyId);
                //_items = await _api.GetItemsAsync(_companyId);
                //_uoms = await _api.GetUomsAsync();
                //_taxes = await _api.GetTaxesAsync(_companyId);
                //_discounts = await _api.GetDiscountsAsync(_companyId);
                //_paymentTerms = await _api.GetPaymentTermsAsync();
                //_currencies = await _api.GetCurrenciesAsync();

                //_cmbVendor.DataSource = _vendors.ToList();
                _cmbVendor.DisplayMember = "VendorName";
                _cmbVendor.ValueMember = "VendorID";
                _cmbVendor.SelectedIndex = -1;

               // _cmbPaymentTerm.DataSource = _paymentTerms.ToList();
                _cmbPaymentTerm.DisplayMember = "Description";
                _cmbPaymentTerm.ValueMember = "PaymentTermID";
                _cmbPaymentTerm.SelectedIndex = -1;

                var discountList = _discounts.Where(d => d.PurchaseSales == true || d.PurchaseSales == null)
                    .Select(d => new { d.Id, Label = $"{d.Name} ({d.Percentage}%)" }).ToList();
                _cmbPODiscount.DataSource = discountList;
                _cmbPODiscount.DisplayMember = "Label";
                _cmbPODiscount.ValueMember = "Id";
                _cmbPODiscount.SelectedIndex = -1;

                _cmbReturnReason.DataSource = ReturnReasonOptions.Values.ToList();
                _cmbDisposition.DataSource = DispositionOptions.Values.ToList();

                RefreshGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load Purchase Order data: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        private List<PurchaseOrderModel> FilteredPOs()
        {
            var vendorMap = _vendors.ToDictionary(v => v.VendorID, v => v.VendorName);
            var s = _txtSearch.Text.Trim().ToLower();
            return _purchaseOrders.Where(po =>
                    (po.PONumber ?? "").ToLower().Contains(s) ||
                    (vendorMap.TryGetValue(po.VendorId, out var vn) ? vn : "").ToLower().Contains(s) ||
                    (po.Status ?? "").ToLower().Contains(s))
                .OrderByDescending(po => po.POId ?? 0)
                .ToList();
        }

        private void RefreshGrid()
        {
            var vendorMap = _vendors.ToDictionary(v => v.VendorID, v => v.VendorName);
            var filtered = FilteredPOs();
            var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)_pageSize));
            if (_currentPage > totalPages) _currentPage = totalPages;
            var page = filtered.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();

            _grid.Rows.Clear();
            foreach (var po in page)
            {
                var vendorName = vendorMap.TryGetValue(po.VendorId, out var vn) ? vn : "-";
                var sign = po.POType == PoType.ReturnOrder ? "-" : "";
                var i = _grid.Rows.Add(po.PONumber, vendorName, po.PODate.ToString("yyyy-MM-dd"), po.POType, $"{sign}{po.POAmount:0.00}", po.Status);
                _grid.Rows[i].Tag = po;
            }
            _lblPage.Text = $"Page {_currentPage} of {totalPages} • {filtered.Count} total   ";
        }

        private async Task DeletePoAsync(PurchaseOrderModel po)
        {
            if (MessageBox.Show($"Delete {po.PONumber}? This cannot be undone.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                await _api.DeletePurchaseOrderAsync(po.POId ?? 0);
                _purchaseOrders = await _api.GetPurchaseOrdersAsync();
                RefreshGrid();
            }
            catch (Exception ex) { MessageBox.Show("Delete failed: " + ex.Message); }
        }

        // ============================================================
        // EDIT VIEW
        // ============================================================

        private void BuildEditView()
        {
            var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10) };
            header.Controls.Add(_btnBack);
            header.Controls.Add(new Label { Width = 20 });
            header.Controls.Add(_lblEditTitle);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            actions.Controls.Add(_btnConfirm);
            actions.Controls.Add(_btnSave);
            actions.Controls.Add(_lblStatus);

            var infoGroup = new GroupBox { Text = "PO Information", Location = new Point(10, 100), Size = new Size(320, 300) };
            LayoutField(infoGroup, "PO Type", _cmbPOType, 25);
            LayoutField(infoGroup, "Vendor", _cmbVendor, 70);
            LayoutField(infoGroup, "PO / Return Date", _dtPODate, 115);
            LayoutField(infoGroup, "Return Reason", _cmbReturnReason, 160);
            LayoutField(infoGroup, "RMA Number", _txtRMA, 205);
            LayoutField(infoGroup, "Disposition", _cmbDisposition, 250);

            var deliveryGroup = new GroupBox { Text = "Delivery Details", Location = new Point(340, 100), Size = new Size(320, 300) };
            LayoutField(deliveryGroup, "Payment Term", _cmbPaymentTerm, 25);
            LayoutField(deliveryGroup, "Delivery Date", _dtDeliveryDate, 70);
            LayoutField(deliveryGroup, "Address", _txtAddress, 115);

            var summaryGroup = new GroupBox { Text = "Order Summary", Location = new Point(670, 100), Size = new Size(300, 300) };
            LayoutField(summaryGroup, "Discount", _cmbPODiscount, 25);
            LayoutField(summaryGroup, "Charges", _txtCharges, 70);
            var totalPanel = new Panel { Location = new Point(15, 120), Size = new Size(270, 70), BackColor = Color.FromArgb(255, 247, 237) };
            totalPanel.Controls.Add(new Label { Text = "GRAND TOTAL", ForeColor = Color.FromArgb(194, 65, 12), Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8), AutoSize = true });
            _lblGrandTotal.Location = new Point(10, 26);
            totalPanel.Controls.Add(_lblGrandTotal);
            summaryGroup.Controls.Add(totalPanel);

            // Return-order invoice picker
            var returnPickerGroup = new GroupBox { Text = "Vendor Invoices — pick items to return", Location = new Point(10, 410), Size = new Size(960, 260) };
            _gridReturnLines.Dock = DockStyle.Fill;
            _gridReturnLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "InvoiceNo", HeaderText = "Invoice", Width = 100, ReadOnly = true });
            _gridReturnLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemSearch", HeaderText = "Item", Width = 220, ReadOnly = true });
            _gridReturnLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "PurchasedQty", HeaderText = "Purchased Qty", Width = 100, ReadOnly = true });
            _gridReturnLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "ReturnQty", HeaderText = "Return Qty", Width = 100 });
            _gridReturnLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "UOM", HeaderText = "UOM", Width = 80, ReadOnly = true });
            _gridReturnLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "Unit Price", Width = 90, ReadOnly = true });
            _gridReturnLines.Columns.Add(new DataGridViewButtonColumn { Name = "AddBtn", HeaderText = "", Text = "Add to Return", UseColumnTextForButtonValue = true, Width = 110 });
            returnPickerGroup.Controls.Add(_gridReturnLines);
            _pnlReturnPicker = returnPickerGroup;
            _pnlReturnPicker.Visible = false;

            // PO / Return lines grid
            var linesGroup = new GroupBox { Text = "Lines", Location = new Point(10, 680), Size = new Size(960, 320) };
            _btnAddLine.Location = new Point(15, 25);
            _gridLines.Location = new Point(15, 55);
            _gridLines.Size = new Size(930, 250);
            _gridLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "SNo", HeaderText = "S.No", Width = 40, ReadOnly = true });
            _gridLines.Columns.Add(new DataGridViewComboBoxColumn { Name = "Item", HeaderText = "Item (SKU - Name)", Width = 220 });
            _gridLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Qty", Width = 60 });
            _gridLines.Columns.Add(new DataGridViewComboBoxColumn { Name = "UOM", HeaderText = "UOM", Width = 110 });
            _gridLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "Price", Width = 80 });
            _gridLines.Columns.Add(new DataGridViewComboBoxColumn { Name = "Tax", HeaderText = "Tax", Width = 110 });
            _gridLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscPct", HeaderText = "Disc %", Width = 60 });
            _gridLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscAmt", HeaderText = "Disc Amt", Width = 70 });
            _gridLines.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Total", Width = 90, ReadOnly = true });
            _gridLines.Columns.Add(new DataGridViewButtonColumn { Name = "Remove", HeaderText = "", Text = "✕", UseColumnTextForButtonValue = true, Width = 40 });
            linesGroup.Controls.Add(_btnAddLine);
            linesGroup.Controls.Add(_gridLines);

            _pnlEdit.Controls.Add(linesGroup);
            _pnlEdit.Controls.Add(_pnlReturnPicker);
            _pnlEdit.Controls.Add(summaryGroup);
            _pnlEdit.Controls.Add(deliveryGroup);
            _pnlEdit.Controls.Add(infoGroup);
            _pnlEdit.Controls.Add(actions);
            _pnlEdit.Controls.Add(header);

            // populate static combos
            _cmbPOType.Items.AddRange(new object[] { PoType.PurchaseOrder, PoType.ReturnOrder });

            WireEditEvents();
        }

        private static void LayoutField(GroupBox group, string label, Control input, int y)
        {
            group.Controls.Add(new Label { Text = label, Location = new Point(15, y), AutoSize = true, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = C_Slate });
            input.Location = new Point(15, y + 16);
            group.Controls.Add(input);
        }

        private void WireEditEvents()
        {
            _btnBack.Click += (_, __) => { _pnlEdit.Visible = false; _pnlList.Visible = true; };

            _cmbPOType.SelectedIndexChanged += (_, __) =>
            {
                _form.POType = (string)_cmbPOType.SelectedItem!;
                bool isReturn = _form.POType == PoType.ReturnOrder;
                _cmbReturnReason.Enabled = isReturn;
                _txtRMA.Enabled = isReturn;
                _cmbDisposition.Enabled = isReturn;
                _pnlReturnPicker.Visible = isReturn && _form.VendorId > 0;
                RecalculateTotals();
            };

            _cmbVendor.SelectedIndexChanged += async (_, __) =>
            {
                if (_cmbVendor.SelectedItem is not VendorModel v) return;
                _form.VendorId = v.VendorID;
                _txtAddress.Text = v.Address;
                if (v.PaymentTermID.HasValue) _cmbPaymentTerm.SelectedValue = v.PaymentTermID.Value;
                _form.CurrencyID = v.CurrencyID ?? 0;
                _currencySymbol = _currencies.FirstOrDefault(c => c.CurrencyID == _form.CurrencyID)?.CurrencySymbol ?? "";

                if (_form.POType == PoType.ReturnOrder)
                {
                    _pnlReturnPicker.Visible = true;
                    await LoadVendorInvoicesAsync();
                }
                RecalculateTotals();
            };

            _cmbPODiscount.SelectedIndexChanged += (_, __) => RecalculateTotals();
            _txtCharges.TextChanged += (_, __) => RecalculateTotals();

            _btnAddLine.Click += (_, __) =>
            {
                _form.Lines.Add(new POLineModel { Qty = 1 });
                RenderLines();
            };

            _gridLines.CellClick += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                if (_gridLines.Columns[e.ColumnIndex].Name == "Remove")
                {
                    _form.Lines.RemoveAt(e.RowIndex);
                    RenderLines();
                }
            };

            _gridLines.CellValueChanged += (_, e) => ApplyLineEdit(e.RowIndex, e.ColumnIndex);
            _gridLines.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (_gridLines.IsCurrentCellDirty) _gridLines.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            _gridReturnLines.CellClick += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                if (_gridReturnLines.Columns[e.ColumnIndex].Name == "AddBtn")
                    AddReturnLineToForm(e.RowIndex);
            };
            _gridReturnLines.CellEndEdit += (_, e) =>
            {
                if (_gridReturnLines.Columns[e.ColumnIndex].Name != "ReturnQty") return;
                var row = _returnLines[e.RowIndex];
                var val = _gridReturnLines.Rows[e.RowIndex].Cells["ReturnQty"].Value?.ToString();
                if (decimal.TryParse(val, out var qty))
                {
                    if (qty > row.PurchasedQty)
                    {
                        MessageBox.Show($"Return Qty cannot exceed Purchased Qty ({row.PurchasedQty}) for {row.ItemSearch}");
                        _gridReturnLines.Rows[e.RowIndex].Cells["ReturnQty"].Value = row.ReturnQty;
                        return;
                    }
                    row.ReturnQty = qty;
                }
            };

            _btnSave.Click += async (_, __) => await SaveAsync();
            _btnConfirm.Click += async (_, __) => await ConfirmAsync();
        }

        private void OpenEditor(PurchaseOrderModel? po)
        {
            _editId = po?.POId;
            _form = po == null
                ? new PurchaseOrderModel { CompanyID = _companyId, StoreID = _storeId, Lines = new() { new POLineModel { Qty = 1 } } }
                : po.Clone();
            _processedReturnKeys.Clear();
            _returnInvoices.Clear();
            _returnLines.Clear();

            _lblEditTitle.Text = (_editId == null ? "New " : "Update ") + (_form.POType == PoType.ReturnOrder ? "Return Order" : "Purchase Order");
            _cmbPOType.SelectedItem = _form.POType;
            _cmbVendor.SelectedValue = _form.VendorId > 0 ? _form.VendorId : (object)DBNull.Value;
            _dtPODate.Value = _form.PODate == default ? DateTime.Today : _form.PODate;
            _dtDeliveryDate.Value = _form.DelieveryDate ?? DateTime.Today;
            _txtAddress.Text = _form.DelieveryAddress;
            _txtCharges.Text = _form.POCharges.ToString();
            _cmbReturnReason.SelectedItem = _form.ReturnReasonCode;
            _txtRMA.Text = _form.RMANumber;
            _cmbDisposition.SelectedItem = _form.DispositionCode;
            _lblStatus.Text = _form.Status;

            bool isReturn = _form.POType == PoType.ReturnOrder;
            _cmbReturnReason.Enabled = isReturn;
            _txtRMA.Enabled = isReturn;
            _cmbDisposition.Enabled = isReturn;
            _pnlReturnPicker.Visible = isReturn && _form.VendorId > 0;

            RenderLines();
            RecalculateTotals();

            _pnlList.Visible = false;
            _pnlEdit.Visible = true;

            if (isReturn && _form.VendorId > 0)
                _ = LoadVendorInvoicesAsync();
        }

       

        // ---------- lines grid rendering ----------

        private void RenderLines()
        {
            _gridLines.Rows.Clear();
            var itemOptions = _items.Select(i => $"{i.Sku} - {i.ItemName}").ToArray();
            var taxOptions = _taxes.Select(t => $"{t.TaxCode} ({t.TaxPercentage}%)").ToArray();

            for (int idx = 0; idx < _form.Lines.Count; idx++)
            {
                var line = _form.Lines[idx];
                var uomOptions = GetUomOptionsForItem(line.ItemId).Select(u => u.uomDescription).ToArray();

                var i = _gridLines.Rows.Add(
                    idx + 1,
                    line.ItemSearch,
                    line.Qty,
                    _uoms.FirstOrDefault(u => u.UomID == line.UOM)?.UomDescription ?? "",
                    line.UnitPrice,
                    _taxes.FirstOrDefault(t => t.TaxId == line.TaxID)?.TaxCode ?? "",
                    line.DiscountPercentage,
                    line.AdditionalDiscountAmount,
                    line.Total.ToString("0.00"));

                ((DataGridViewComboBoxCell)_gridLines.Rows[i].Cells["Item"]).Items.AddRange(itemOptions);
                ((DataGridViewComboBoxCell)_gridLines.Rows[i].Cells["UOM"]).Items.AddRange(uomOptions);
                ((DataGridViewComboBoxCell)_gridLines.Rows[i].Cells["Tax"]).Items.AddRange(taxOptions);
            }
        }

        private List<(int uomID, string uomDescription, decimal price)> GetUomOptionsForItem(int itemId)
        {
            var results = new List<(int, string, decimal)>();
            var item = _items.FirstOrDefault(x => x.ItemId == itemId);
            if (item == null) return results;

            var baseDesc = _uoms.FirstOrDefault(u => u.UomID == item.BaseUOM)?.UomDescription ?? "Single (Base)";
            results.Add((item.BaseUOM, baseDesc, item.CostPrice));

            foreach (var pack in item.PackSizes)
            {
                var desc = _uoms.FirstOrDefault(u => u.UomID == pack.UomId)?.UomDescription ?? pack.PackDescription;
                results.Add((pack.UomId, desc, pack.RetailPrice));
            }
            return results;
        }

        private void ApplyLineEdit(int rowIndex, int colIndex)
        {
            if (rowIndex < 0 || rowIndex >= _form.Lines.Count) return;
            var line = _form.Lines[rowIndex];
            var colName = _gridLines.Columns[colIndex].Name;
            var cellVal = _gridLines.Rows[rowIndex].Cells[colIndex].Value?.ToString() ?? "";

            switch (colName)
            {
                case "Item":
                    var sku = cellVal.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
                    var item = _items.FirstOrDefault(x => x.Sku == sku);
                    if (item != null)
                    {
                        line.ItemId = item.ItemId;
                        line.ItemSearch = cellVal;
                        var uomOptions = GetUomOptionsForItem(item.ItemId);
                        if (uomOptions.Count > 0)
                        {
                            line.UOM = uomOptions[0].uomID;
                            line.UnitPrice = uomOptions[0].price;
                        }
                        var tax = _taxes.FirstOrDefault(t => t.TaxId == item.PurchaseTaxID);
                        if (tax != null) { line.TaxID = tax.TaxId; line.TaxPercentage = tax.TaxPercentage; }
                    }
                    break;
                case "Qty":
                    decimal.TryParse(cellVal, out var qty);
                    line.Qty = qty <= 0 ? 1 : qty;
                    break;
                case "UOM":
                    var uomOpt = GetUomOptionsForItem(line.ItemId).FirstOrDefault(u => u.uomDescription == cellVal);
                    if (uomOpt.uomID != 0) { line.UOM = uomOpt.uomID; line.UnitPrice = uomOpt.price; }
                    break;
                case "UnitPrice":
                    decimal.TryParse(cellVal, out var price);
                    line.UnitPrice = price;
                    break;
                case "Tax":
                    var taxCode = cellVal.Split('(')[0].Trim();
                    var taxSel = _taxes.FirstOrDefault(t => t.TaxCode == taxCode);
                    if (taxSel != null) { line.TaxID = taxSel.TaxId; line.TaxPercentage = taxSel.TaxPercentage; }
                    break;
                case "DiscPct":
                    decimal.TryParse(cellVal, out var discPct);
                    line.DiscountPercentage = discPct;
                    LineCalculator.CalculateLine(line, DiscountMode.Percent);
                    RenderLines();
                    RecalculateTotals();
                    return;
                case "DiscAmt":
                    decimal.TryParse(cellVal, out var discAmt);
                    line.AdditionalDiscountAmount = discAmt;
                    break;
            }

            LineCalculator.CalculateLine(line);
            _gridLines.Rows[rowIndex].Cells["Total"].Value = line.Total.ToString("0.00");
            RecalculateTotals();
        }

        // ---------- return-order invoice picker ----------

        private async Task LoadVendorInvoicesAsync()
        {
            _gridReturnLines.Rows.Clear();
            _returnLines.Clear();
            if (_form.VendorId <= 0) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                _returnInvoices = await _api.GetInvoicesByVendorAsync(_form.VendorId);
                foreach (var inv in _returnInvoices)
                {
                    foreach (var line in inv.PoInvoiceLines)
                    {
                        var itemMeta = _items.FirstOrDefault(i => i.ItemId == line.ItemId);
                        var row = new ReturnLineRow
                        {
                            SourceLineKey = $"{inv.InvoiceID}-{line.InvoiceLineID}",
                            InvoiceID = inv.InvoiceID,
                            InvoiceNo = inv.InvoiceNo,
                            PONumber = inv.PONumber,
                            ItemId = line.ItemId,
                            ItemSearch = itemMeta != null ? $"{itemMeta.Sku} - {itemMeta.ItemName}" : line.ItemName,
                            PurchasedQty = line.Qty,
                            UOM = line.UOM,
                            UnitPrice = line.UnitPrice
                        };
                        _returnLines.Add(row);
                    }
                }
                RenderReturnLinesGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load invoices for this vendor: " + ex.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void RenderReturnLinesGrid()
        {
            _gridReturnLines.Rows.Clear();
            foreach (var row in _returnLines)
            {
                var i = _gridReturnLines.Rows.Add(
                    row.InvoiceNo, row.ItemSearch, row.PurchasedQty, row.ReturnQty,
                    _uoms.FirstOrDefault(u => u.UomID == row.UOM)?.UomDescription ?? row.UOM.ToString(),
                    row.UnitPrice, "Add to Return");

                bool added = _form.Lines.Any(l => l.IsReturnItem && l.SourceLineKey == row.SourceLineKey);
                _gridReturnLines.Rows[i].Cells["AddBtn"].Value = added ? "✓ Added" : "Add to Return";
            }
        }

        private void AddReturnLineToForm(int rowIndex)
        {
            var row = _returnLines[rowIndex];
            if (row.ReturnQty <= 0)
            {
                MessageBox.Show("Set a Return Qty greater than 0 first.");
                return;
            }
            if (_form.Lines.Any(l => l.IsReturnItem && l.SourceLineKey == row.SourceLineKey))
                return; // already added

            var newLine = new POLineModel
            {
                ItemId = row.ItemId,
                ItemSearch = row.ItemSearch,
                Qty = row.ReturnQty,
                UOM = row.UOM,
                UnitPrice = row.UnitPrice,
                IsReturnItem = true,
                SourceLineKey = row.SourceLineKey
            };
            LineCalculator.CalculateLine(newLine);

            bool hasBlankFirstLine = _form.Lines.Count == 1 && _form.Lines[0].ItemId == 0;
            if (hasBlankFirstLine) _form.Lines[0] = newLine;
            else _form.Lines.Add(newLine);

            RenderLines();
            RenderReturnLinesGrid();
            RecalculateTotals();
        }

        // ---------- totals ----------

        private void RecalculateTotals()
        {
            decimal.TryParse(_txtCharges.Text, out var charges);
            _form.POCharges = charges;

            var discountPct = 0m;
            if (_cmbPODiscount.SelectedValue is int discId)
                discountPct = _discounts.FirstOrDefault(d => d.Id == discId)?.Percentage ?? 0;

            var totals = LineCalculator.CalculateTotals(_form.Lines, discountPct, charges);
            var sign = _form.POType == PoType.ReturnOrder ? "-" : "";
            _lblGrandTotal.Text = $"{sign}{_currencySymbol} {totals.GrandTotal:0.00}";
            _form.PODiscountAmt = totals.PODiscountAmt;
        }

        // ---------- save / confirm ----------

        private async Task<int?> SaveAsync()
        {
            if (_isSaving) return null;
            if (_form.VendorId <= 0) { MessageBox.Show("Please select a Vendor"); return null; }
            if (_form.CurrencyID <= 0) { MessageBox.Show("Vendor has no currency configured"); return null; }
            if (_form.Lines.Any(l => l.Qty <= 0 || l.UnitPrice <= 0)) { MessageBox.Show("Every line needs a Qty and Unit Price greater than 0"); return null; }
            if (_form.POType == PoType.ReturnOrder && string.IsNullOrEmpty(_form.ReturnReasonCode))
            { MessageBox.Show("Please select a Return Reason"); return null; }

            _isSaving = true;
            _btnSave.Text = "SAVING...";
            try
            {
                _form.PODate = _dtPODate.Value;
                _form.DelieveryDate = _dtDeliveryDate.Value;
                _form.DelieveryAddress = _txtAddress.Text;
                _form.RMANumber = _txtRMA.Text;
                _form.ReturnReasonCode = _cmbReturnReason.SelectedItem as string ?? "";
                _form.DispositionCode = _cmbDisposition.SelectedItem as string ?? "";
                if (_cmbPaymentTerm.SelectedValue is int pt) _form.PaymentTermID = pt;
                if (_cmbPODiscount.SelectedValue is int discId) _form.SelectedPODiscountID = discId;

                var totals = LineCalculator.CalculateTotals(_form.Lines, 0, _form.POCharges);
                _form.POAmount = totals.GrandTotal;

                if (_editId == null)
                {
                    var (ok, newId, error) = await _api.CreatePurchaseOrderAsync(_form);
                    if (!ok) { MessageBox.Show("Save failed: " + error); return null; }
                    _editId = newId;
                    _form.POId = newId;
                }
                else
                {
                    var (ok, error) = await _api.UpdatePurchaseOrderAsync(_editId.Value, _form);
                    if (!ok) { MessageBox.Show("Save failed: " + error); return null; }
                }

                MessageBox.Show((_form.POType == PoType.ReturnOrder ? "Return Order" : "Purchase Order") +
                    (_editId == null ? " Created" : " Updated") + " Successfully");
                _purchaseOrders = await _api.GetPurchaseOrdersAsync();
                return _editId;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message);
                return null;
            }
            finally { _isSaving = false; _btnSave.Text = "SAVE"; }
        }

        private async Task ConfirmAsync()
        {
            if (_isConfirming) return;
            _isConfirming = true;
            _btnConfirm.Text = "CONFIRMING...";
            try
            {
                bool isReturn = _form.POType == PoType.ReturnOrder;
                _form.Status = PoStatus.Confirmed;

                var newReturnLines = _form.Lines
                    .Where(l => l.IsReturnItem && l.SourceLineKey != null && !_processedReturnKeys.Contains(l.SourceLineKey))
                    .ToList();

                var savedId = await SaveAsync();
                if (savedId == null)
                {
                    _form.Status = PoStatus.Open;
                    MessageBox.Show("Could not confirm — please fix the errors above and try again.");
                    return;
                }

                if (isReturn)
                {
                    if (newReturnLines.Count == 0)
                        MessageBox.Show("No return lines with a quantity to process. Set a Return Qty and try again.");
                    else
                    {
                        var ok = await ProcessReturnLinesAsync(newReturnLines);
                        if (ok) await PostStockMovementsAsync(savedId.Value, newReturnLines, isReturn: true);
                        else { MessageBox.Show("Return processing failed — stock was not updated"); return; }
                    }
                }
                else
                {
                    await PostStockMovementsAsync(savedId.Value, _form.Lines, isReturn: false);
                }

                MessageBox.Show((isReturn ? "Return Order" : "Purchase Order") + " Confirmed Successfully");
                _pnlEdit.Visible = false;
                _pnlList.Visible = true;
                RefreshGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Confirmation failed: " + ex.Message);
            }
            finally { _isConfirming = false; _btnConfirm.Text = "CONFIRM"; }
        }

        private async Task<bool> ProcessReturnLinesAsync(List<POLineModel> returnLines)
        {
            var payload = new List<ProcessReturnLine>();
            foreach (var l in returnLines)
            {
                var parts = (l.SourceLineKey ?? "").Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], out var invId) || !int.TryParse(parts[1], out var invLineId)) continue;
                if (invId <= 0 || invLineId <= 0 || l.Qty <= 0) continue;
                payload.Add(new ProcessReturnLine { InvoiceID = invId, InvoiceLineID = invLineId, ReturnQty = l.Qty });
            }
            if (payload.Count == 0) { MessageBox.Show("Return lines had no valid quantity/invoice reference — nothing sent"); return false; }

            var (ok, error) = await _api.ProcessReturnAsync(payload);
            if (!ok) { MessageBox.Show("Failed to update invoiced quantities for the return: " + error); return false; }

            foreach (var l in returnLines) if (l.SourceLineKey != null) _processedReturnKeys.Add(l.SourceLineKey);
            return true;
        }

        private async Task PostStockMovementsAsync(int poId, List<POLineModel> lines, bool isReturn)
        {
            try
            {
                var totals = new Dictionary<int, decimal>();
                foreach (var line in lines)
                {
                    if (line.ItemId <= 0) continue;
                    var item = _items.FirstOrDefault(i => i.ItemId == line.ItemId);
                    var factor = LineCalculator.GetUnitsPerPackForLine(line, item);
                    totals.TryGetValue(line.ItemId, out var existing);
                    totals[line.ItemId] = existing + line.Qty * factor;
                }

                foreach (var kv in totals)
                {
                    var item = _items.FirstOrDefault(i => i.ItemId == kv.Key);
                    var payload = new StockMovementPayload
                    {
                        ItemID = kv.Key,
                        ItemCode = item?.Sku ?? "",
                        CompanyID = _form.CompanyID,
                        StoreID = _form.StoreID,
                        Uom = _uoms.FirstOrDefault(u => u.UomID == item?.BaseUOM)?.UomDescription ?? "",
                        ItemQty = kv.Value,
                        ReturnQty = kv.Value,
                        TransactionID = poId
                    };

                    var (ok, error) = isReturn
                        ? await _api.ProcessPurchaseReturnStockAsync(payload)
                        : await _api.UpsertOpeningStockAsync(payload);

                    if (!ok) throw new Exception(error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Stock update failed: " + ex.Message);
            }
        }
    }
}

// ============================================================================
// DASHBOARD WIRE-UP
// Add a nav button the same way btnNavSales / btnNavSalesReturn are declared
// and add this handler, then call it from InitializeComponent alongside the
// other MakeNavBtn(...) calls:
//
//   private Button btnNavPurchaseOrder;
//   ...
//   btnNavPurchaseOrder = MakeNavBtn("🧾", "Purchase Orders", NAV_START + NAV_H * N, btnNavPurchaseOrder_Click);
//   // add it to the panelSidebar.Controls.AddRange(...) list and to
//   // RepositionNavButtons()'s `buttons` array alongside the others.
//
//   private void btnNavPurchaseOrder_Click(object sender, EventArgs e)
//   {
//       SetActiveNav(btnNavPurchaseOrder);
//       ShowPage(new POSAPP.Sales.PurchaseOrderForm(_selectedCompanyId, _currencySymbol));
//   }
// ============================================================================