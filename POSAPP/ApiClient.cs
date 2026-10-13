using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace POSAPP.Invoice
{
    public static class ApiClient
    {
        public static string AuthToken { get; set; } = "";

        private static readonly HttpClient _http = new HttpClient();

        private static void ApplyAuth(HttpRequestMessage req)
        {
            if (!string.IsNullOrWhiteSpace(AuthToken))
                req.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthToken);
        }

        public static async Task<JsonElement> GetJsonAsync(string path)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppConfig.BaseUrl + path);
            ApplyAuth(req);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        public static async Task<JsonElement> PostJsonAsync(string path, object payload)
        {
            string body = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, AppConfig.BaseUrl + path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            ApplyAuth(req);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        public static async Task<T> GetAsync<T>(string path)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppConfig.BaseUrl + path);
            ApplyAuth(req);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<T>(json, opts);
        }

        public static JsonElement UnwrapArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array) return root;
            if (root.TryGetProperty("data", out var d))
            {
                if (d.ValueKind == JsonValueKind.Array) return d;
                if (d.TryGetProperty("data", out var dd) && dd.ValueKind == JsonValueKind.Array) return dd;
            }
            return root;
        }

        // ── THE ACTUAL FIX LIVES HERE — these are the ONLY Str/Int/Dec/DateVal
        // in the project. Every call site (including GetInvoicesForCustomerAsync)
        // calls "ApiClient.Str(...)" etc., so this is the copy that matters.
        // Do NOT duplicate these into SalesReturnRepository — a duplicate there
        // is dead code, since nothing calls it unqualified. ────────────────────
        public static string Str(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return "";
            foreach (var n in names)
                if (el.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null)
                    return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            return "";
        }

        public static int Int(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return 0;
            foreach (var n in names)
                if (el.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null)
                {
                    if (v.ValueKind == JsonValueKind.Number)
                    {
                        if (v.TryGetInt32(out int i)) return i;
                        if (v.TryGetDecimal(out decimal d)) return (int)Math.Round(d); // handles "3.00", "100.00"
                    }
                    if (int.TryParse(v.ToString(), out int p)) return p;
                    if (decimal.TryParse(v.ToString(), out decimal p2)) return (int)Math.Round(p2);
                }
            return 0;
        }

        public static decimal Dec(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return 0m;
            foreach (var n in names)
                if (el.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null)
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out decimal d)) return d;
                    if (decimal.TryParse(v.ToString(), out decimal p)) return p;
                }
            return 0m;
        }

        public static DateTime DateVal(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return DateTime.Now;
            foreach (var n in names)
                if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                    if (DateTime.TryParse(v.GetString(), out var dt)) return dt;
            return DateTime.Now;
        }
    }

    public class InvoiceWithLines
    {
        public InvoiceLite Header { get; set; }
        public List<OriginalInvoiceLine> Lines { get; set; } = new List<OriginalInvoiceLine>();
    }

    public static partial class SalesReturnRepository
    {
        public static async Task<List<CustomerLite>> SearchCustomersAsync(int companyId)
        {
            var list = new List<CustomerLite>();
            try
            {
                var root = ApiClient.UnwrapArray(await ApiClient.GetJsonAsync("/api/customers"));
                foreach (var c in root.EnumerateArray())
                {
                    int id = ApiClient.Int(c, "customerID", "CustomerID", "customerId", "id");
                    string name = ApiClient.Str(c, "customerName", "CustomerName", "name");
                    int compId = ApiClient.Int(c, "companyID", "CompanyID", "companyId");

                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (companyId != 0 && compId != 0 && compId != companyId) continue;

                    list.Add(new CustomerLite { CustomerId = id, CustomerName = name });
                }

                list.Sort((a, b) => string.Compare(a.CustomerName, b.CustomerName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SearchCustomersAsync ERROR: " + ex.Message);
            }
            return list;
        }
         
    }
}