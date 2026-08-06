using POSAPP.Reports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace POSAPP.Payment
{
    // ── Raw API shapes for GET /api/SOInvoice ───────────────────────────────
    public class SOInvoiceHeaderApiRow
    {
        [JsonPropertyName("invoiceID")] public int InvoiceID { get; set; }
        [JsonPropertyName("companyID")] public int CompanyID { get; set; }
        [JsonPropertyName("storeID")] public int StoreID { get; set; }
        [JsonPropertyName("customerID")] public int CustomerID { get; set; }
        [JsonPropertyName("invoiceNo")] public string InvoiceNo { get; set; } = "";
        [JsonPropertyName("invoiceDate")] public DateTime InvoiceDate { get; set; }
        [JsonPropertyName("soNumber")] public string? SONumber { get; set; }
        [JsonPropertyName("totalInvoiceAmount")] public decimal TotalInvoiceAmount { get; set; }
        [JsonPropertyName("salesStatus")] public int SalesStatus { get; set; }
    }

    public class SOInvoiceLineApiRow2
    {
        [JsonPropertyName("invoiceID")] public int InvoiceID { get; set; }
        [JsonPropertyName("itemNo")] public int ItemNo { get; set; }
        [JsonPropertyName("itemName")] public string? ItemName { get; set; }
        [JsonPropertyName("qty")] public decimal Qty { get; set; }
        [JsonPropertyName("uom")] public int UOM { get; set; }
        [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
        [JsonPropertyName("chargesAmount")] public decimal ChargesAmount { get; set; }
        [JsonPropertyName("discountAmount")] public decimal DiscountAmount { get; set; }
        [JsonPropertyName("totalAmount")] public decimal TotalAmount { get; set; }
    }

    public class SOInvoicePaymentApiRow
    {
        [JsonPropertyName("invoiceID")] public int InvoiceID { get; set; }
        [JsonPropertyName("paymentMethod")] public int PaymentMethod { get; set; }
        [JsonPropertyName("paymentAmount")] public decimal PaymentAmount { get; set; }
        [JsonPropertyName("paymentType")] public string? PaymentType { get; set; }
    }

    public class SOInvoiceDetailApiRow
    {
        [JsonPropertyName("header")] public SOInvoiceHeaderApiRow Header { get; set; } = new();
        [JsonPropertyName("lines")] public List<SOInvoiceLineApiRow2> Lines { get; set; } = new();
        [JsonPropertyName("payments")] public List<SOInvoicePaymentApiRow> Payments { get; set; } = new();
    }

    public static class SalesInvoiceApi
    {
        private static readonly HttpClient _http = new HttpClient();
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        // NOTE: SalesStatus enum values (Open/Completed/Cancelled) aren't
        // confirmed from your entity — adjust the exclusion below once you
        // know which int means "Cancelled" if you want cancelled invoices hidden.
        public static async Task<List<SalesRepository.ReprintInvoiceRow>> GetReprintInvoicesAsync(
     int companyId, int days)
        {
            var result = new List<SalesRepository.ReprintInvoiceRow>();
            try
            {
                var url = $"api/SOInvoice/filtered?companyId={companyId}&days={days}";
                var resp = await _http.GetAsync(new Uri(new Uri(SalesOrderApi.BaseUrl), url));
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync();
                var all = JsonSerializer.Deserialize<List<SOInvoiceDetailApiRow>>(json, _jsonOpts)
                           ?? new List<SOInvoiceDetailApiRow>();

                // No more client-side companyId/date filtering needed — the API already
                // returned only the matching rows.
                var custNames = await SalesOrderApi.GetCustomerNameMapAsync();
                var itemNames = await SalesOrderApi.GetItemNameMapAsync();

                foreach (var x in all)
                {
                    var h = x.Header;
                    decimal paidCash = 0, paidCard = 0, paidDigital = 0;
                    foreach (var p in x.Payments)
                    {
                        var t = (p.PaymentType ?? "").ToLowerInvariant();
                        if (t.Contains("card"))
                            paidCard += p.PaymentAmount;
                        else if (t.Contains("upi") || t.Contains("digital") || t.Contains("online") || t.Contains("wallet"))
                            paidDigital += p.PaymentAmount;
                        else
                            paidCash += p.PaymentAmount;
                    }

                    var lineDtos = x.Lines.Select(l =>
                    {
                        decimal baseAmt = l.UnitPrice * l.Qty;
                        decimal discPct = baseAmt > 0 ? Math.Round(l.DiscountAmount / baseAmt * 100m, 2) : 0m;
                        string itemName = !string.IsNullOrWhiteSpace(l.ItemName)
                            ? l.ItemName
                            : (itemNames.TryGetValue(l.ItemNo, out var nm) && !string.IsNullOrWhiteSpace(nm)
                                ? nm
                                : $"Item #{l.ItemNo}");
                        return new
                        {
                            StockCode = l.ItemNo.ToString(),
                            Name = itemName,
                            UOM = l.UOM.ToString(),
                            Qty = l.Qty,
                            QtyRequested = l.Qty,
                            QtyDispatched = l.Qty,
                            UnitPrice = l.UnitPrice,
                            ListPrice = l.UnitPrice,
                            DiscountPct = discPct,
                            LineTotal = l.TotalAmount
                        };
                    }).ToList();

                    result.Add(new SalesRepository.ReprintInvoiceRow
                    {
                        InvoiceNo = h.InvoiceNo,
                        CustomerName = custNames.TryGetValue(h.CustomerID, out var cn) && !string.IsNullOrWhiteSpace(cn)
                            ? cn : "Walk-in",
                        SaleDate = h.InvoiceDate,
                        GrandTotal = h.TotalInvoiceAmount,
                        CurrencySymbol = "",
                        PaidCash = paidCash,
                        PaidDigital = paidDigital,
                        PaidCard = paidCard,
                        CartJson = JsonSerializer.Serialize(lineDtos)
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetReprintInvoicesAsync failed: " + ex.Message);
            }
            return result;
        }
    }
}