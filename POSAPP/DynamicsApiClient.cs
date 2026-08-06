//using System;
//using System.Collections.Generic;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Text;
//using System.Text.Json;
//using System.Text.Json.Serialization;
//using System.Threading.Tasks;

//public class DynamicsApiClient
//{
//    private readonly string _baseUrl = "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com/data/";
//    private readonly string _tenantId = "91ce49f5-eaf7-4049-8242-435e862944ed";
//    private readonly string _clientId = "fa1f34ba-85db-4efc-b111-e5c1f82b81af";
//    private readonly string _clientSecret = "FP~8Q~xRu.cKKfsWBAe06OV.AvFDKn9Kv0TVzaoL";

//    public class TokenResponse
//    {
//        public string token_type { get; set; } = string.Empty;
//        public int expires_in { get; set; }
//        public string access_token { get; set; } = string.Empty;
//    }

//    private async Task<string> GetAccessTokenAsync()
//    {
//        using var http = new HttpClient();
//        string scope = "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com/.default";

//        var url = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";

//        var body = new Dictionary<string, string>
//        {
//            { "client_id", _clientId },
//            { "client_secret", _clientSecret },
//            { "scope", scope },
//            { "grant_type", "client_credentials" }
//        };

//        var content = new FormUrlEncodedContent(body);
//        var response = await http.PostAsync(url, content);
//        var result = await response.Content.ReadAsStringAsync();



//        if (!response.IsSuccessStatusCode)
//            throw new Exception($"Token Error: {result}");

//        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(result);
//        return tokenResponse?.access_token ?? throw new Exception("Failed to get access token");
//    }

//    // ====================== GENERIC METHODS ======================

//    public async Task<T> GetAsync<T>(string endpoint)
//    {
//        var token = await GetAccessTokenAsync();
//        using var http = new HttpClient();
//        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
//        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//        var response = await http.GetAsync(endpoint);
//        var result = await response.Content.ReadAsStringAsync();

//        if (!response.IsSuccessStatusCode)
//            throw new Exception($"GET Error: {result}");

//        return JsonSerializer.Deserialize<T>(result) ?? throw new Exception("Failed to deserialize response");
//    }
//    public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest payload)
//    {
//        var token = await GetAccessTokenAsync();
//        using var http = new HttpClient();
//        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
//        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//        var options = new JsonSerializerOptions
//        {
//            PropertyNamingPolicy = null,
//            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
//        };

//        var jsonContent = new StringContent(
//            JsonSerializer.Serialize(payload, options), Encoding.UTF8, "application/json");

//        var response = await http.PostAsync(endpoint, jsonContent);
//        var result = await response.Content.ReadAsStringAsync();

//        if (!response.IsSuccessStatusCode)
//            throw new Exception($"POST Error ({(int)response.StatusCode}): {result}");

//        return JsonSerializer.Deserialize<TResponse>(result)
//            ?? throw new Exception("Failed to deserialize POST response");
//    }

//    public async Task<(string responseBody, int statusCode)> PostRawAsync<TRequest>(
//    string endpoint, TRequest payload)
//{
//    var token = await GetAccessTokenAsync();
//    using var http = new HttpClient();
//    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
//    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//    var options = new JsonSerializerOptions
//    {
//        PropertyNamingPolicy = null,
//        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
//    };

//    var jsonContent = new StringContent(
//        JsonSerializer.Serialize(payload, options), Encoding.UTF8, "application/json");

//    var response = await http.PostAsync(endpoint, jsonContent);
//    string body = await response.Content.ReadAsStringAsync();
//    return (body, (int)response.StatusCode);
//}

//    // ====================== RETAIL SALES HEADER ======================

//    public async Task<object> CreateRetailSalesHeaderAsync(RetailSalesHeader payload)
//    {
//        string endpoint = $"{_baseUrl}RetailSalesHeaders?cross-company=true";
//        return await PostAsync<RetailSalesHeader, object>(endpoint, payload);
//    }

//    // ====================== RETAIL SALES LINES ======================

//    public async Task<object> CreateRetailSalesLineAsync(RetailSalesLine payload)
//    {
//        string endpoint = $"{_baseUrl}RetailSalesLines?cross-company=true";
//        return await PostAsync<RetailSalesLine, object>(endpoint, payload);
//    }

//    public async Task<List<object>> CreateRetailSalesLinesAsync(string bisInvoiceId, List<RetailSalesLine> lines)
//    {
//        var results = new List<object>();

//        for (int i = 0; i < lines.Count; i++)
//        {
//            var line = lines[i];

//            line.dataAreaId = "1110";
//            line.BISInvoiceId = bisInvoiceId;
//            line.InvoiceLineId = i + 1;

//            // Optional: Clear InvoiceId just in case
//            line.InvoiceId = null;

//            var result = await CreateRetailSalesLineAsync(line);
//            results.Add(result);
//        }

//        return results;
//    }

//    // ====================== RETAIL SALES INVOICE PAYMENT RESPONSES ======================

//    public async Task<List<RetailSalesInvoicePaymentResponse>> GetRetailSalesInvoicePaymentResponsesAsync(string? filter = null)
//    {
//        string endpoint = $"{_baseUrl}RetailSalesInvoicePaymentResponses?cross-company=true";

//        //if (!string.IsNullOrEmpty(filter))
//        //    endpoint += $"&$filter={Uri.EscapeDataString(filter)}";

//        var odataResponse = await GetAsync<ODataResponse<RetailSalesInvoicePaymentResponse>>(endpoint);
//        return odataResponse.Value ?? new List<RetailSalesInvoicePaymentResponse>();
//    }
//}

//// ====================== ODATA RESPONSE WRAPPER ======================
//public class ODataResponse<T>
//{
//    public List<T>? Value { get; set; }
//}

//// ====================== ENTITY MODELS ======================




//public class RetailSalesHeader
//{
//    public string? dataAreaId { get; set; }
//    public string? BISInvoiceId { get; set; }
//    public string? CustAccount { get; set; }
//    public DateTime? InvoiceDate { get; set; }
//    public DateTime? PostingDate { get; set; }
//    public decimal InvoiceAmount { get; set; }
//    public string? InvoiceDescription { get; set; }
//    public string? SalesStatus { get; set; }
//    public string? InventLocationId { get; set; }
//    public string? InventSiteId { get; set; }
//    public string? wMSLocationId { get; set; }
     
//    public string? SyncStatus { get; set; } = "Pending";
//    public string? InvoiceAccountName { get; set; } = "Walk-in";
//    public string? InvoiceAccount { get; set; } = "";
//    public int RetryCount { get; set; } = 0;
//    public DateTime? RetryDateTime { get; set; } = new DateTime(1900, 1, 1);
//    public string? StoreId { get; set; } = "0";
//    public int TerminalId { get; set; } = 0;
//    public string? CompanyId { get; set; } = "";
//    public DateTime? DueDate { get; set; } = new DateTime(1900, 1, 1);
//    public string? SalesId { get; set; } = "";
//    public string? SOSalesId { get; set; }
//    public string? VATRegistrationId { get; set; } = "";
//    public string? Comments { get; set; } = "";
//}





//public class RetailSalesLine
//{
//    public string? dataAreaId { get; set; }
//    public string? BISInvoiceId { get; set; }
//    public int InvoiceLineId { get; set; }

//    public string? ItemId { get; set; }
//    public decimal SalesQty { get; set; }
//    public decimal PriceUnit { get; set; }
//    public decimal LineAmount { get; set; }
//    public decimal TotalAmount { get; set; }

//    public decimal DiscountAmount { get; set; } = 0m;
//    public decimal ChargesAmount { get; set; } = 0m;
//    public decimal TaxAmount { get; set; } = 0m;

//    public string? TaxGroup { get; set; } = "";
//    public string? TaxItemGroup { get; set; } = ""; 
//    public string? InvoiceId { get; set; }

//    public string? inventSerialId { get; set; } = "";
//    public string? inventBatchId { get; set; } = "";
//}

//public class RetailSalesInvoicePaymentResponse
//{
//    public string? OdataEtag { get; set; }
//    public string? DataAreaId { get; set; }
//    public string? BISInvoiceId { get; set; }
//    public string? InvoiceId { get; set; }
//    public decimal InvoiceAmount { get; set; }
//    public string? SalesId { get; set; }
//    public string? PaymentId { get; set; }
//    public string? BISSalesStatus { get; set; }
//    public DateTime? PaymentDate { get; set; }
//    public string? BISSyncStatus { get; set; }
//    public string? PaymentType { get; set; }
//}