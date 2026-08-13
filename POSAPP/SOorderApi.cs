using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static POSAPP.login;

namespace POSAPP.Payment
{
    public class SalesOrderLineApiRow
    {
        [JsonPropertyName("itemId")] public int ItemId { get; set; }
        [JsonPropertyName("itemName")] public string ItemName { get; set; } = "";
        [JsonPropertyName("qty")] public decimal Qty { get; set; }
        [JsonPropertyName("uom")] public int UOM { get; set; }
        [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
        [JsonPropertyName("discountPercent")] public decimal? DiscountPercent { get; set; }
        [JsonPropertyName("discountAmount")] public decimal? DiscountAmount { get; set; }
        [JsonPropertyName("charges")] public decimal? Charges { get; set; }
        [JsonPropertyName("total")] public decimal Total { get; set; }
    }
    public class SalesOrderApiRow
    {
        [JsonPropertyName("soId")] public int SOId { get; set; }
        [JsonPropertyName("companyID")] public int CompanyID { get; set; }
        [JsonPropertyName("storeID")] public int StoreID { get; set; }
        [JsonPropertyName("soNumber")] public string SONumber { get; set; } = "";
        [JsonPropertyName("customerId")] public int CustomerId { get; set; }
        [JsonPropertyName("soDate")] public DateTime? SODate { get; set; }
        [JsonPropertyName("soAmount")] public decimal SOAmount { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "Open";
        [JsonPropertyName("lines")] public List<SalesOrderLineApiRow>? Lines { get; set; }
    }

    public class SalesOrderEnvelope
    {
        [JsonPropertyName("data")]
        public List<SalesOrderApiRow>? Data { get; set; }
    }
    

    public class CustomerApiRow
    {
        [JsonPropertyName("customerID")] public int CustomerID { get; set; }
        [JsonPropertyName("customerName")] public string CustomerName { get; set; } = "";
    }

    // Concrete class (not anonymous type) — needed so the ?? fallback
    // in CreateSOInvoiceFromSalesOrderAsync type-checks.
    public class SOInvoiceLinePayload
    {
        [JsonPropertyName("itemNo")] public int ItemNo { get; set; }
        [JsonPropertyName("itemName")] public string ItemName { get; set; } = "";
        [JsonPropertyName("batchNo")] public string? BatchNo { get; set; }
        [JsonPropertyName("serialNo")] public string? SerialNo { get; set; }
        [JsonPropertyName("qty")] public decimal Qty { get; set; }
        [JsonPropertyName("uom")] public int UOM { get; set; }
        [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
        [JsonPropertyName("chargesAmount")] public decimal ChargesAmount { get; set; }
        [JsonPropertyName("tax")] public decimal Tax { get; set; }
        [JsonPropertyName("discountAmount")] public decimal DiscountAmount { get; set; }
    }

    public class SOInvoicePayload
    {
        [JsonPropertyName("companyID")] public int CompanyID { get; set; }
        [JsonPropertyName("storeID")] public int StoreID { get; set; }
        [JsonPropertyName("terminalID")] public int TerminalID { get; set; }
        [JsonPropertyName("customerID")] public int CustomerID { get; set; }
        [JsonPropertyName("vatRegistrationID")] public int? VATRegistrationID { get; set; }
        [JsonPropertyName("invoiceAccount")] public int? InvoiceAccount { get; set; }
        [JsonPropertyName("invoiceAccountName")] public string? InvoiceAccountName { get; set; }
        [JsonPropertyName("invoiceDescription")] public string? InvoiceDescription { get; set; }
        [JsonPropertyName("invoiceDate")] public DateTime InvoiceDate { get; set; }
        [JsonPropertyName("soNumber")] public string SONumber { get; set; } = "";
        [JsonPropertyName("dueDate")] public DateTime? DueDate { get; set; }
        [JsonPropertyName("comments")] public string? Comments { get; set; }
        [JsonPropertyName("lines")] public List<SOInvoiceLinePayload> Lines { get; set; } = new();
        [JsonPropertyName("payments")] public List<object> Payments { get; set; } = new();
    }
    public class ItemApiRow
    {
        [JsonPropertyName("itemID")] public int ItemID { get; set; }
        [JsonPropertyName("itemName")] public string ItemName { get; set; } = "";
    }
   
 



    // ══════════════════════════════════════════════════════════════════════
    //  NEW — Save Sales Order request payload
    //  Matches: POST /api/SalesOrder
    // ══════════════════════════════════════════════════════════════════════
    public class CreateSOLinePayload
    {
        [JsonPropertyName("itemId")] public int ItemId { get; set; }
        [JsonPropertyName("qty")] public decimal Qty { get; set; }
        [JsonPropertyName("uom")] public int UOM { get; set; }
        [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
        [JsonPropertyName("discountPercent")] public decimal DiscountPercent { get; set; }
        [JsonPropertyName("discountAmount")] public decimal DiscountAmount { get; set; }
        [JsonPropertyName("charges")] public decimal Charges { get; set; }
        [JsonPropertyName("tax")] public decimal Tax { get; set; }
        [JsonPropertyName("total")] public decimal Total { get; set; }
    }

    public class CreateSOChargePayload
    {
        [JsonPropertyName("chargesID")] public int ChargesID { get; set; }
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("currencyID")] public int CurrencyID { get; set; }
        [JsonPropertyName("type")] public int Type { get; set; }
        [JsonPropertyName("applyTo")] public int ApplyTo { get; set; }
    }

    public class CreateSalesOrderPayload
    {
        [JsonPropertyName("companyID")] public int CompanyID { get; set; }
        [JsonPropertyName("storeID")] public int StoreID { get; set; }
        [JsonPropertyName("customerId")] public int CustomerId { get; set; }
        [JsonPropertyName("paymentTermID")] public int PaymentTermID { get; set; }
        [JsonPropertyName("soAmount")] public int SOAmount { get; set; }
        [JsonPropertyName("currency")] public string Currency { get; set; } = "";
        [JsonPropertyName("soType")] public string SOType { get; set; } = "";
        [JsonPropertyName("soDiscountAmt")] public decimal SODiscountAmt { get; set; }
        [JsonPropertyName("soTax")] public decimal SOTax { get; set; }
        [JsonPropertyName("soCharges")] public decimal SOCharges { get; set; }
        [JsonPropertyName("currencyID")] public int CurrencyID { get; set; }
        [JsonPropertyName("soDiscountID")] public int SODiscountID { get; set; }
        [JsonPropertyName("soTaxID")] public int SOTaxID { get; set; }
        [JsonPropertyName("deliveryAddress")] public string DeliveryAddress { get; set; } = "";
        [JsonPropertyName("deliveryDate")] public DateTime DeliveryDate { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("WinPos")] public string WinPos { get; set; } = "";
        [JsonPropertyName("lines")] public List<CreateSOLinePayload> Lines { get; set; } = new();
        [JsonPropertyName("charges")] public List<CreateSOChargePayload> Charges { get; set; } = new();
    }

    public static class SalesOrderApi
    {
     
        // TODO: set this to the same base URL your React app's api.js points to
        //public static string BaseUrl = "https://shriposapi.mythitsolutions.co.in";
       // public static string BaseUrl = "https://localhost:7022";
        public static string BaseUrl = AppConfig.BaseUrl.TrimEnd('/');

        private static readonly HttpClient _http = new HttpClient();

        // ONE declaration only — merged both option sets
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private static List<CustomerApiRow>? _customerCache;
        // SO numbers successfully converted to SOInvoice this session — filtered
        // out of the pending list so they don't reappear (server SO status is
        // not being changed by this client, so without this they'd resurface).
        public static readonly HashSet<string> InvoicedSoNumbers = new(StringComparer.OrdinalIgnoreCase);

        private static async Task<List<CustomerApiRow>> GetCustomersAsync()
        {
            if (_customerCache != null) return _customerCache;
            try
            {
                var resp = await _http.GetAsync(new Uri(new Uri(BaseUrl), "api/Customer"));
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();

                List<CustomerApiRow>? rows;
                try { rows = JsonSerializer.Deserialize<List<CustomerApiRow>>(json, _jsonOpts); }
                catch
                {
                    var doc = JsonDocument.Parse(json);
                    var dataEl = doc.RootElement.TryGetProperty("data", out var d) ? d : default;
                    rows = dataEl.ValueKind == JsonValueKind.Array
                        ? JsonSerializer.Deserialize<List<CustomerApiRow>>(dataEl.GetRawText(), _jsonOpts)
                        : new List<CustomerApiRow>();
                }
                _customerCache = rows ?? new List<CustomerApiRow>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetCustomersAsync failed: " + ex.Message);
                _customerCache = new List<CustomerApiRow>();
            }
            return _customerCache;
        }

        /// <summary>
        /// Public accessor so callers (e.g. SalesForm) can resolve a CustomerID
        /// from the free-typed customer name before creating a Sales Order.
        /// Returns 0 if no match is found (e.g. Walk-in).
        /// </summary>
        /// 
        public class BankAccountDto
        {
            public int BankAccountID { get; set; }
            public string BankName { get; set; } = "";
            public string AccountNumber { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string Branch { get; set; } = "";
            public string CurrencyCode { get; set; } = "";
            public bool Status { get; set; } = true;

            public string Display => string.IsNullOrWhiteSpace(AccountNumber)
                ? BankName
                : $"{BankName} — {AccountNumber}";
        }

        public static async Task<List<BankAccountDto>> GetAllBanksAsync(int companyId)
        {
            var result = new List<BankAccountDto>();
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                var resp = await http.GetAsync($"{BaseUrl}/api/Bank?companyId={companyId}").ConfigureAwait(false);

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Debug.WriteLine($"GetAllBanksAsync RESPONSE ({(int)resp.StatusCode}): {json}");

                if (!resp.IsSuccessStatusCode) return result;

                using var doc = JsonDocument.Parse(json);
                JsonElement arr = doc.RootElement;
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("data", out var d))
                    arr = d;

                if (arr.ValueKind != JsonValueKind.Array) return result;

                // Case-insensitive, multi-alias property lookup — API field names vary.
                static string GetStr(JsonElement el, params string[] names)
                {
                    foreach (var prop in el.EnumerateObject())
                        foreach (var n in names)
                            if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                                return prop.Value.ValueKind == JsonValueKind.String
                                    ? prop.Value.GetString() ?? ""
                                    : prop.Value.ValueKind == JsonValueKind.Number
                                        ? prop.Value.GetRawText()
                                        : "";
                    return "";
                }
                static int GetInt(JsonElement el, params string[] names)
                {
                    foreach (var prop in el.EnumerateObject())
                        foreach (var n in names)
                            if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                            {
                                if (prop.Value.ValueKind == JsonValueKind.Number) return prop.Value.GetInt32();
                                if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out int v)) return v;
                            }
                    return 0;
                }
                static bool GetBool(JsonElement el, params string[] names)
                {
                    foreach (var prop in el.EnumerateObject())
                        foreach (var n in names)
                            if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                            {
                                if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                                    return prop.Value.GetBoolean();
                                if (prop.Value.ValueKind == JsonValueKind.Number)
                                    return prop.Value.GetInt32() != 0;
                            }
                    return true; // default active if no status field present
                }

                foreach (var row in arr.EnumerateArray())
                {
                    var b = new BankAccountDto
                    {
                        BankAccountID = GetInt(row, "bankAccountID", "bankAccountId", "id", "bankID", "bankId"),
                        BankName = GetStr(row, "bankName", "bankAccountName", "name", "bank"),
                        AccountNumber = GetStr(row, "accountNumber", "acNumber", "accountNo", "acNo"),
                        AccountName = GetStr(row, "accountName", "acName"),
                        Branch = GetStr(row, "branch", "branchName"),
                        CurrencyCode = GetStr(row, "currencyCode", "currency"),
                        Status = GetBool(row, "status", "isActive", "active")
                    };

                    // Fallback so a row is never blank even if BankName mapping missed.
                    if (string.IsNullOrWhiteSpace(b.BankName))
                        b.BankName = !string.IsNullOrWhiteSpace(b.AccountName) ? b.AccountName
                                   : !string.IsNullOrWhiteSpace(b.AccountNumber) ? $"Account {b.AccountNumber}"
                                   : $"Bank #{b.BankAccountID}";

                    result.Add(b);
                }

                result = result.Where(b => b.Status).ToList();
                Debug.WriteLine($"GetAllBanksAsync: parsed {result.Count} bank account(s).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetAllBanksAsync: " + ex.Message);
            }
            return result;
        }
        public static async Task<int> GetCustomerIdByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            var customers = await GetCustomersAsync();
            var match = customers.FirstOrDefault(c =>
                c.CustomerName.Equals(name, StringComparison.OrdinalIgnoreCase));
            return match?.CustomerID ?? 0;
        }
        /// <summary>Public accessor so SalesInvoiceApi can resolve customer names for invoices.</summary>
        public static async Task<Dictionary<int, string>> GetCustomerNameMapAsync()
        {
            var customers = await GetCustomersAsync();
            return customers
                .GroupBy(c => c.CustomerID)
                .ToDictionary(g => g.Key, g => g.First().CustomerName ?? "");
        }

        private static List<ItemApiRow>? _itemCache;

        private static async Task<List<ItemApiRow>> GetItemsAsync()
        {
            if (_itemCache != null) return _itemCache;
            try
            {
                var resp = await _http.GetAsync(new Uri(new Uri(BaseUrl), "api/item"));
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();

                List<ItemApiRow>? rows;
                try { rows = JsonSerializer.Deserialize<List<ItemApiRow>>(json, _jsonOpts); }
                catch
                {
                    using var doc = JsonDocument.Parse(json);
                    var dataEl = doc.RootElement.TryGetProperty("data", out var d) ? d : default;
                    rows = dataEl.ValueKind == JsonValueKind.Array
                        ? JsonSerializer.Deserialize<List<ItemApiRow>>(dataEl.GetRawText(), _jsonOpts)
                        : new List<ItemApiRow>();
                }
                _itemCache = rows ?? new List<ItemApiRow>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetItemsAsync failed: " + ex.Message);
                _itemCache = new List<ItemApiRow>();
            }
            return _itemCache;
        }

        /// <summary>Public accessor so PendingInvoicesForm can resolve item names for SO lines.</summary>
        public static async Task<Dictionary<int, string>> GetItemNameMapAsync()
        {
            var items = await GetItemsAsync();
            return items
                .GroupBy(i => i.ItemID)
                .ToDictionary(g => g.Key, g => g.First().ItemName ?? "");
        }

        public static async Task<List<(SalesOrderApiRow Order, string CustomerName)>> GetPendingSalesOrdersAsync()
        {
            try
            {
                var resp = await _http.GetAsync(new Uri(new Uri(BaseUrl), "api/SalesOrder"));
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();

                List<SalesOrderApiRow> rows;

                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        rows = JsonSerializer.Deserialize<List<SalesOrderApiRow>>(json, _jsonOpts)
                               ?? new List<SalesOrderApiRow>();
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object
                             && doc.RootElement.TryGetProperty("data", out var dataEl)
                             && dataEl.ValueKind == JsonValueKind.Array)
                    {
                        rows = JsonSerializer.Deserialize<List<SalesOrderApiRow>>(dataEl.GetRawText(), _jsonOpts)
                               ?? new List<SalesOrderApiRow>();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("GetPendingSalesOrdersAsync: unexpected JSON shape.");
                        return new List<(SalesOrderApiRow, string)>();
                    }
                }

                var pending = rows
                    .Where(r => string.Equals(r.Status, "Confirm", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(r.SONumber))
                    .ToList();

                var customers = await GetCustomersAsync();

                return pending.Select(r =>
                {
                    var cust = customers.FirstOrDefault(c => c.CustomerID == r.CustomerId);
                    return (r, cust?.CustomerName ?? "Walk-in");
                }).ToList();
            }
            catch (JsonException jex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GetPendingSalesOrdersAsync JSON error: {jex.Message} | Path: {jex.Path}");
                return new List<(SalesOrderApiRow, string)>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetPendingSalesOrdersAsync failed: " + ex.Message);
                return new List<(SalesOrderApiRow, string)>();
            }
        }

        public static async Task<SalesOrderApiRow?> GetSalesOrderByIdAsync(int soId)
        {
            try
            {
                var resp = await _http.GetAsync(new Uri(new Uri(BaseUrl), $"api/SalesOrder/{soId}"));
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SalesOrderApiRow>(json, _jsonOpts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetSalesOrderByIdAsync failed: " + ex.Message);
                return null;
            }
        }

        public static async Task<bool> DeleteSalesOrderAsync(int soId)
        {
            try
            {
                var resp = await _http.DeleteAsync(new Uri(new Uri(BaseUrl), $"api/SalesOrder/{soId}"));
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DeleteSalesOrderAsync failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// NEW — creates a Sales Order via POST /api/SalesOrder using the exact
        /// payload shape the API expects. Returns (success, soId, soNumber) —
        /// soId/soNumber are populated only if the API returns them.
        /// </summary>
        public static async Task<(bool Success, int? SoId, string? SoNumber)> CreateSalesOrderAsync(
      CreateSalesOrderPayload payload)
        {
            try
            {
                string requestJson = JsonSerializer.Serialize(payload);
                System.Diagnostics.Debug.WriteLine("CreateSalesOrderAsync REQUEST BODY: " + requestJson);

                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                var resp = await _http.PostAsync(new Uri(new Uri(BaseUrl), "api/SalesOrder"), content);

                var json = await resp.Content.ReadAsStringAsync();

                // ── Always log the raw response — this is what we need to see if
                //    SoNumber keeps coming back null. Leave this in until confirmed fixed. ──
                System.Diagnostics.Debug.WriteLine(
                    $"CreateSalesOrderAsync RESPONSE ({(int)resp.StatusCode}): {json}");

                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"CreateSalesOrderAsync: {(int)resp.StatusCode} — {json}");
                    return (false, null, null);
                }

                int? soId = null;
                string? soNumber = null;

                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        // ── Case-insensitive property lookup — JsonElement.TryGetProperty is
                        //    case-sensitive, which was the real reason soNumber kept coming
                        //    back null (actual API casing didn't match our hardcoded guesses). ──
                        static bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
                        {
                            foreach (var prop in obj.EnumerateObject())
                            {
                                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                                {
                                    value = prop.Value;
                                    return true;
                                }
                            }
                            value = default;
                            return false;
                        }

                        // ── Last-resort: find any property whose NAME contains a keyword,
                        //    for API shapes we haven't seen/guessed yet. ──
                        static bool TryFindPropertyContaining(JsonElement obj, string keyword, out JsonElement value, out string? foundName)
                        {
                            foreach (var prop in obj.EnumerateObject())
                            {
                                if (prop.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    value = prop.Value;
                                    foundName = prop.Name;
                                    return true;
                                }
                            }
                            value = default;
                            foundName = null;
                            return false;
                        }
                         
                        JsonElement target = default;
                        bool foundTarget = false;

                        if (root.ValueKind == JsonValueKind.Object)
                        {
                            // Try common wrapper property names, in order (case-insensitive)
                            string[] wrapperNames = { "data", "result", "salesOrder", "so", "order" };
                            JsonElement wrapper = default;
                            bool hasWrapper = false;

                            foreach (var w in wrapperNames)
                            {
                                if (TryGetPropertyCI(root, w, out wrapper))
                                {
                                    hasWrapper = true;
                                    break;
                                }
                            }

                            if (hasWrapper)
                            {
                                if (wrapper.ValueKind == JsonValueKind.Object)
                                {
                                    target = wrapper;
                                    foundTarget = true;
                                }
                                else if (wrapper.ValueKind == JsonValueKind.Array && wrapper.GetArrayLength() > 0)
                                {
                                    target = wrapper[0];
                                    foundTarget = true;
                                }
                            }

                            // No known wrapper — root itself might be the SO object
                            if (!foundTarget)
                            {
                                target = root;
                                foundTarget = true;
                            }
                        }
                        else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                        {
                            target = root[0];
                            foundTarget = true;
                        }

                        if (foundTarget && target.ValueKind == JsonValueKind.Object)
                        {
                            // ── SoId — case-insensitive match against known variants ──
                            string[] idNames = { "soId", "soID", "id" };
                            foreach (var n in idNames)
                            {
                                if (TryGetPropertyCI(target, n, out var idEl))
                                {
                                    if (idEl.ValueKind == JsonValueKind.Number)
                                    {
                                        soId = idEl.GetInt32();
                                        break;
                                    }
                                    if (idEl.ValueKind == JsonValueKind.String
                                        && int.TryParse(idEl.GetString(), out int parsedId))
                                    {
                                        soId = parsedId;
                                        break;
                                    }
                                }
                            }

                            // ── SoNumber — case-insensitive match against known variants ──
                            string[] numNames =
                            {
                        "soNumber", "soNo", "orderNumber", "invoiceNo", "number"
                    };
                            foreach (var n in numNames)
                            {
                                if (TryGetPropertyCI(target, n, out var numEl) && numEl.ValueKind == JsonValueKind.String)
                                {
                                    var val = numEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        soNumber = val;
                                        break;
                                    }
                                }
                            }

                            // ── Last-resort fallback: none of the known names matched —
                            //    scan for ANY property whose name contains "number". ──
                            if (soNumber == null)
                            {
                                if (TryFindPropertyContaining(target, "number", out var numEl, out var foundName)
                                    && numEl.ValueKind == JsonValueKind.String
                                    && !string.IsNullOrWhiteSpace(numEl.GetString()))
                                {
                                    soNumber = numEl.GetString();
                                    System.Diagnostics.Debug.WriteLine(
                                        $"CreateSalesOrderAsync: recovered SoNumber via fallback property '{foundName}'.");
                                }
                            }

                            // ── Same last-resort fallback for SoId ──
                            if (soId == null)
                            {
                                if (TryFindPropertyContaining(target, "Id", out var idEl, out var foundName)
                                    && idEl.ValueKind == JsonValueKind.Number)
                                {
                                    soId = idEl.GetInt32();
                                    System.Diagnostics.Debug.WriteLine(
                                        $"CreateSalesOrderAsync: recovered SoId via fallback property '{foundName}'.");
                                }
                            }
                        }

                        if (soId == null && soNumber == null)
                        {
                            // Dump every top-level property name we actually saw, so the real
                            // casing/shape is visible in the log instead of guessing again.
                            var seenNames = foundTarget && target.ValueKind == JsonValueKind.Object
                                ? string.Join(", ", target.EnumerateObject().Select(p => p.Name))
                                : "(no object target found)";
                            System.Diagnostics.Debug.WriteLine(
                                "CreateSalesOrderAsync: could not locate soId/soNumber — check the RESPONSE log above. " +
                                "Properties seen at target: " + seenNames);
                        }
                    }
                    catch (JsonException jex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "CreateSalesOrderAsync: response was not valid JSON — " + jex.Message);
                    }
                }

                // ── Fallback: if we got a SoId but no SoNumber, look the order up by ID
                //    via the existing GET endpoint, which we know parses SONumber correctly. ──
                if (soNumber == null && soId.HasValue && soId.Value > 0)
                {
                    var fetched = await GetSalesOrderByIdAsync(soId.Value).ConfigureAwait(false);
                    if (fetched != null && !string.IsNullOrWhiteSpace(fetched.SONumber))
                    {
                        soNumber = fetched.SONumber;
                        System.Diagnostics.Debug.WriteLine(
                            $"CreateSalesOrderAsync: recovered SoNumber '{soNumber}' via GetSalesOrderByIdAsync fallback.");
                    }
                }

                return (true, soId, soNumber);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CreateSalesOrderAsync failed: " + ex.Message);
                return (false, null, null);
            }
        }
        public static async Task<(int? InvoiceId, string? Error)> CreateSOInvoiceFromSalesOrderAsync(SalesOrderApiRow so)
        {
            try
            {
                List<SOInvoiceLinePayload> lines;
                if (so.Lines != null)
                {
                    // Item name often isn't returned on SO lines — resolve missing ones from the item cache.
                    Dictionary<int, string>? itemNameMap = null;
                    if (so.Lines.Any(l => string.IsNullOrWhiteSpace(l.ItemName)))
                    {
                        itemNameMap = await GetItemNameMapAsync().ConfigureAwait(false);
                    }

                    lines = so.Lines.Select(l =>
                    {
                        string resolvedName = l.ItemName;
                        if (string.IsNullOrWhiteSpace(resolvedName) &&
                            itemNameMap != null &&
                            itemNameMap.TryGetValue(l.ItemId, out var nameFromCache))
                        {
                            resolvedName = nameFromCache;
                        }

                        return new SOInvoiceLinePayload
                        {
                            ItemNo = l.ItemId,
                            ItemName = resolvedName ?? "",
                            BatchNo = null,
                            SerialNo = null,
                            Qty = l.Qty,
                            UOM = l.UOM,
                            UnitPrice = l.UnitPrice,
                            ChargesAmount = l.Charges ?? 0m,
                            Tax = 0,
                            DiscountAmount = l.DiscountAmount ?? 0m
                        };
                    }).ToList();
                }
                else
                {
                    lines = new List<SOInvoiceLinePayload>();
                }
                // ... rest unchanged

                var payload = new SOInvoicePayload
                {
                    CompanyID = so.CompanyID,
                    StoreID = so.StoreID,
                    TerminalID = 1,
                    CustomerID = so.CustomerId,
                    VATRegistrationID = null,
                    InvoiceAccount = null,
                    InvoiceAccountName = null,
                    InvoiceDescription = null,
                    InvoiceDate = DateTime.UtcNow,
                    SONumber = so.SONumber,
                    DueDate = null,
                    Comments = null,
                    Lines = lines,
                    Payments = new List<object>()
                };

                string requestJson = JsonSerializer.Serialize(payload);
                System.Diagnostics.Debug.WriteLine("CreateSOInvoiceFromSalesOrderAsync REQUEST BODY: " + requestJson);

                var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

                var resp = await _http.PostAsync(new Uri(new Uri(BaseUrl), "api/SOInvoice"), content);

                if (!resp.IsSuccessStatusCode)
                {
                    var errBody = await resp.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine(
                        $"CreateSOInvoiceFromSalesOrderAsync: {(int)resp.StatusCode} — {errBody}");
                    return (null, $"{(int)resp.StatusCode}: {errBody}");
                }

                var json = await resp.Content.ReadAsStringAsync();

                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var target = root;
                    if (root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("data", out var dataEl)
                        && dataEl.ValueKind == JsonValueKind.Object)
                        target = dataEl;

                    if (target.ValueKind == JsonValueKind.Object)
                    {
                        if (target.TryGetProperty("invoiceID", out var idEl)
                            || target.TryGetProperty("InvoiceID", out idEl)
                            || target.TryGetProperty("invoiceId", out idEl))
                        {
                            if (idEl.ValueKind == JsonValueKind.Number)
                                return (idEl.GetInt32(), null);
                        }
                    }
                }

                // Success status but no invoiceID in response — still treat as success
                return (0, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CreateSOInvoiceFromSalesOrderAsync failed: " + ex.Message);
                return (null, ex.Message);
            }
        }
        public static async Task<List<SalesOrderApiRow>> GetAllSalesOrdersAsync()
        {
            try
            {
                var resp = await _http.GetAsync(new Uri(new Uri(BaseUrl), "api/SalesOrder"));
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<SalesOrderApiRow>>(json, _jsonOpts) ?? new();

                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("data", out var dataEl)
                    && dataEl.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<SalesOrderApiRow>>(dataEl.GetRawText(), _jsonOpts) ?? new();

                return new List<SalesOrderApiRow>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetAllSalesOrdersAsync failed: " + ex.Message);
                return new List<SalesOrderApiRow>();
            }
        }

        public static async Task<SalesOrderApiRow?> GetSalesOrderBySoNumberAsync(string soNumber)
        {
            if (string.IsNullOrWhiteSpace(soNumber)) return null;
            var all = await GetAllSalesOrdersAsync();
            return all.FirstOrDefault(r =>
                string.Equals(r.SONumber, soNumber, StringComparison.OrdinalIgnoreCase));
        }
        // In SalesOrderApi.cs
        public static async Task<bool> ConfirmSOInvoiceAsync(int invoiceId)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);

                int userId = CurrentUser.UserInfo?.UserID ?? 0;

                var resp = await http.PutAsync(
             $"{BaseUrl}/api/SOInvoice/{invoiceId}/confirm?userId={userId}",
             new StringContent("")).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.WriteLine($"ConfirmSOInvoiceAsync: {(int)resp.StatusCode} {err}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ConfirmSOInvoiceAsync: " + ex.Message);
                return false;
            }
        }
        public class CustomerPaymentSettlementDto
        {
            [JsonPropertyName("invoiceID")] public int InvoiceID { get; set; }
            [JsonPropertyName("invoiceNo")] public string InvoiceNo { get; set; } = "";
            [JsonPropertyName("invoiceAmount")] public decimal InvoiceAmount { get; set; }
            [JsonPropertyName("amountToSettle")] public decimal AmountToSettle { get; set; }
            [JsonPropertyName("retentionAmount")] public decimal RetentionAmount { get; set; } = 0m;
            [JsonPropertyName("discountAmount")] public decimal DiscountAmount { get; set; } = 0m;
            [JsonPropertyName("whtAmount")] public decimal WhtAmount { get; set; } = 0m;
        }

        public class SaveCustomerPaymentPayload
        {
            [JsonPropertyName("companyID")] public int CompanyId { get; set; }
            [JsonPropertyName("customerID")] public int CustomerId { get; set; }
            [JsonPropertyName("paymentDate")] public DateTime PaymentDate { get; set; } 
            [JsonPropertyName("paymentMethod")] public string PaymentMethod { get; set; } = "Cash";
            [JsonPropertyName("bankAccountID")] public int BankAccountId { get; set; } = 0;
            [JsonPropertyName("referenceNo")] public string ReferenceNo { get; set; } = "";
            [JsonPropertyName("currencyCode")] public string CurrencyCode { get; set; } = "";
            [JsonPropertyName("exchangeRate")] public decimal ExchangeRate { get; set; } = 1m;
            [JsonPropertyName("description")] public string Description { get; set; } = "";
            [JsonPropertyName("comments")] public string Comments { get; set; } = "";
            [JsonPropertyName("paymentStatus")] public string PaymentStatus { get; set; } = "Draft";
            [JsonPropertyName("createdBy")] public int CreatedBy { get; set; }
            [JsonPropertyName("settlements")] public List<CustomerPaymentSettlementDto> Settlements { get; set; } = new();
        }
         
        public static async Task<(bool Success, int? PaymentId, string PaymentNo)> SaveCustomerPaymentAsync(SaveCustomerPaymentPayload payload)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);
                string json = System.Text.Json.JsonSerializer.Serialize(payload);
                using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // was: $"{BaseUrl}/api/ar/customerpayment/save"
                var resp = await http.PostAsync($"{BaseUrl}/api/CustomerPayment/SaveCustomerPayment", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return (false, null, null);

                string respJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var doc = System.Text.Json.JsonDocument.Parse(respJson);
                bool isSuccess = doc.RootElement.TryGetProperty("isSuccess", out var s) && s.GetBoolean();
                if (!isSuccess) return (false, null, null);

                var data = doc.RootElement.GetProperty("data");
                int? paymentId = data.TryGetProperty("paymentID", out var pid) ? pid.GetInt32()
                                : data.TryGetProperty("PaymentID", out var pid2) ? pid2.GetInt32() : (int?)null;
                string paymentNo = data.TryGetProperty("paymentNo", out var pn) ? pn.GetString()
                                  : data.TryGetProperty("PaymentNo", out var pn2) ? pn2.GetString() : null;

                return (true, paymentId, paymentNo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveCustomerPaymentAsync: " + ex.Message);
                return (false, null, null);
            }
        }

        public static async Task<bool> PostCustomerPaymentAsync(int paymentId, int modifiedBy, int? bankAccountId)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);
                var body = new { ModifiedBy = modifiedBy, BankAccountID = bankAccountId };
                string json = System.Text.Json.JsonSerializer.Serialize(body);
                using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // was: POST $"{BaseUrl}/api/ar/customerpayment/{paymentId}/post"
                var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/api/CustomerPayment/{paymentId}/post")
                {
                    Content = content
                };
                var resp = await http.SendAsync(request).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return false;

                string respJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = System.Text.Json.JsonDocument.Parse(respJson);
                return doc.RootElement.TryGetProperty("isSuccess", out var s) && s.GetBoolean();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PostCustomerPaymentAsync: " + ex.Message);
                return false;
            }
        }
    }
}