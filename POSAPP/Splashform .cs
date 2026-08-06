using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace POSAPP
{
    // ══════════════════════════════════════════════════════════════════════
    //  POS_SyncService
    //  Runs as a background timer — picks up Pending rows from POS_SyncQueue,
    //  pushes them to D365, writes POS_SyncLog, updates SOInvoiceHeader.
    //
    //  Usage (add to SalesForm_Load after existing setup):
    //      _syncService = new POS_SyncService(_dbPath, GetAccessToken);
    //      _syncService.Start();
    //
    //  Dispose in OnFormClosed:
    //      _syncService?.Dispose();
    // ══════════════════════════════════════════════════════════════════════
    public class POS_SyncService : IDisposable
    {
        // ── Config ─────────────────────────────────────────────────────────
        private const int TIMER_INTERVAL_MS = 30_000;   // poll every 30s
        private const int MAX_RETRY = 3;
        private const int RETRY_DELAY_MINUTES = 5;        // base; doubles per attempt

        private readonly string _dbPath;
        private readonly Func<Task<string>> _getToken;    // async token factory
        private readonly string _d365BaseUrl;

        private System.Threading.Timer _timer;
        private bool _running;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // ── Constructor ────────────────────────────────────────────────────
        public POS_SyncService(
            string dbPath,
            Func<Task<string>> getAccessToken,
            string d365BaseUrl = "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com")
        {
            _dbPath = dbPath;
            _getToken = getAccessToken;
            _d365BaseUrl = d365BaseUrl.TrimEnd('/');
        }

        // ── Lifecycle ──────────────────────────────────────────────────────
        public void Start()
        {
            EnsureSchema();
            _timer = new System.Threading.Timer(
                async _ => await ProcessQueueAsync(),
                null,
                TimeSpan.FromSeconds(5),          // first run 5s after start
                TimeSpan.FromMilliseconds(TIMER_INTERVAL_MS));
            Debug.WriteLine("POS_SyncService: started.");
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _lock?.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════
        //  ENQUEUE  — call this right after SaveSale() in SalesRepository
        // ══════════════════════════════════════════════════════════════════
        public void Enqueue(int transactionId, string invoiceNo)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO POS_SyncQueue
                        (TransactionID, InvoiceNo, SyncType, Status, RetryCount,
                         NextRetryDateTime, CreatedDatetime)
                    VALUES
                        (@tid, @inv, 'Sales', 'Pending', 0,
                         @now, @now);";
                cmd.Parameters.AddWithValue("@tid", transactionId);
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                cmd.Parameters.AddWithValue("@now", Iso(DateTime.UtcNow));
                cmd.ExecuteNonQuery();
                Debug.WriteLine($"POS_SyncService: enqueued {invoiceNo}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("POS_SyncService.Enqueue: " + ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PROCESS QUEUE
        // ══════════════════════════════════════════════════════════════════
        private async Task ProcessQueueAsync()
        {
            if (_running) return;
            if (!await _lock.WaitAsync(0)) return;   // skip if already running

            _running = true;
            try
            {
                var rows = GetPendingRows();
                if (rows.Count == 0) return;

                Debug.WriteLine($"POS_SyncService: processing {rows.Count} row(s).");

                string token = null;
                try { token = await _getToken(); }
                catch (Exception ex)
                {
                    Debug.WriteLine("POS_SyncService: token error — " + ex.Message);
                    return;
                }

                foreach (var row in rows)
                    await ProcessRowAsync(row, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("POS_SyncService.ProcessQueueAsync: " + ex.Message);
            }
            finally
            {
                _running = false;
                _lock.Release();
            }
        }

        // ── Single queue row ───────────────────────────────────────────────
        private async Task ProcessRowAsync(SyncQueueRow row, string token)
        {
            SetStatus(row.QueueId, "Processing");
            string requestPayload = "";
            string responsePayload = "";
            bool success = false;
            string errorMsg = "";

            try
            {
                // ── Build D365 Sales Order payload ─────────────────────────
                var invoice = GetInvoiceHeader(row.InvoiceNo);
                if (invoice == null)
                {
                    errorMsg = $"Invoice {row.InvoiceNo} not found in SOInvoiceHeader.";
                    Debug.WriteLine("POS_SyncService: " + errorMsg);
                    MarkFailed(row, errorMsg);
                    return;
                }

                var lines = GetInvoiceLines(invoice.InvoiceId);
                var payments = GetInvoicePayments(invoice.InvoiceId);

                requestPayload = BuildD365Payload(invoice, lines, payments);

                // ── POST to D365 ───────────────────────────────────────────
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
                http.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                string url = $"{_d365BaseUrl}/data/SalesOrderHeaders";   // adjust entity name as needed
                var content = new StringContent(requestPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage resp;
                try { resp = await http.PostAsync(url, content).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    errorMsg = "HTTP error: " + ex.Message;
                    MarkFailed(row, errorMsg);
                    WriteLog(row, requestPayload, "", false, errorMsg);
                    return;
                }

                responsePayload = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                {
                    // Try to extract D365 Sales Order ID from response
                    string d365Id = ExtractD365Id(responsePayload);
                    MarkSynced(row, d365Id);
                    success = true;
                    Debug.WriteLine($"POS_SyncService: {row.InvoiceNo} synced OK → {d365Id}");
                }
                else
                {
                    errorMsg = $"HTTP {(int)resp.StatusCode}: {responsePayload}";
                    MarkFailed(row, errorMsg);
                    Debug.WriteLine($"POS_SyncService: {row.InvoiceNo} FAILED — {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                MarkFailed(row, errorMsg);
                Debug.WriteLine("POS_SyncService.ProcessRowAsync: " + ex);
            }
            finally
            {
                WriteLog(row, requestPayload, responsePayload, success, errorMsg);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  D365 PAYLOAD BUILDER
        //  Adjust field names to match your D365 Sales Order entity exactly.
        // ══════════════════════════════════════════════════════════════════
        private static string BuildD365Payload(
            InvoiceHeaderDto hdr,
            List<InvoiceLineDto> lines,
            List<InvoicePaymentDto> payments)
        {
            // Build a JSON object matching D365 SalesOrderHeader OData entity
            var obj = new
            {
                dataAreaId = "RIDI",               // ← your legal entity
                SalesOrderNumber = hdr.InvoiceNo,
                InvoiceAccount = string.IsNullOrWhiteSpace(hdr.InvoiceAccountName)
                                        ? "CASH001" : hdr.InvoiceAccountName,
                CurrencyCode = "BWP",
                RequestedShippingDate = hdr.PostingDate,
                SalesOrderOrigin = "POS",
                POSInvoiceNo = hdr.InvoiceNo,
                POSPostingDate = hdr.PostingDate,
                TotalInvoiceAmount = hdr.TotalInvoiceAmount,
                // Add line items as a child collection if your D365 entity supports deep insert
                SalesOrderLines = lines.ConvertAll(l => new
                {
                    ItemNumber = l.ItemName,    // use real ItemId if available
                    Quantity = l.Qty,
                    SalesPrice = l.UnitPrice,
                    DiscountAmount = l.DiscountAmount,
                    SalesOrderLineDiscountAmount = l.DiscountAmount
                })
            };

            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = null    // preserve PascalCase for D365
            });
        }

        private static string ExtractD365Id(string json)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                if (doc.TryGetProperty("SalesOrderNumber", out var v)) return v.GetString() ?? "";
                if (doc.TryGetProperty("salesOrderNumber", out var v2)) return v2.GetString() ?? "";
            }
            catch { }
            return "";
        }

        // ══════════════════════════════════════════════════════════════════
        //  DATABASE HELPERS
        // ══════════════════════════════════════════════════════════════════

        // ── Schema ────────────────────────────────────────────────────────
        public void EnsureSchema()
        {
            try
            {
                using var conn = Open();

                // POS_SyncQueue
                Exec(conn, @"
                    CREATE TABLE IF NOT EXISTS POS_SyncQueue (
                        QueueId           INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionID     INTEGER,
                        InvoiceNo         TEXT,
                        SyncType          TEXT NOT NULL DEFAULT 'Sales',
                        Status            TEXT NOT NULL DEFAULT 'Pending',
                        RetryCount        INTEGER NOT NULL DEFAULT 0,
                        NextRetryDateTime TEXT,
                        LastErrorMessage  TEXT,
                        CreatedDatetime   TEXT,
                        ProcessedDateTime TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IX_SyncQueue_Status
                        ON POS_SyncQueue(Status, NextRetryDateTime);");

                // POS_SyncLog
                Exec(conn, @"
                    CREATE TABLE IF NOT EXISTS POS_SyncLog (
                        LogID             INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionID     INTEGER,
                        RequestPayLoadID  TEXT,
                        ResponsePayLoadID TEXT,
                        Status            TEXT,
                        ErrorMessage      TEXT,
                        CreatedDatetime   TEXT
                    );");

                // Add SyncStatus + D365SalesOrderId columns to SOInvoiceHeader
                // (safe — ALTER TABLE IF NOT EXISTS column doesn't exist yet)
                TryAddColumn(conn, "SOInvoiceHeader", "SyncStatus",
                    "TEXT NOT NULL DEFAULT 'Pending'");
                TryAddColumn(conn, "SOInvoiceHeader", "D365SalesOrderId",
                    "TEXT NULL");
                TryAddColumn(conn, "SOInvoiceHeader", "RetryCount",
                    "INTEGER NOT NULL DEFAULT 0");
                TryAddColumn(conn, "SOInvoiceHeader", "LastRetryDateTime",
                    "TEXT NULL");

                Debug.WriteLine("POS_SyncService.EnsureSchema: OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("POS_SyncService.EnsureSchema: " + ex.Message);
            }
        }

        private static void TryAddColumn(SQLiteConnection conn,
            string table, string column, string definition)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                cmd.ExecuteNonQuery();
            }
            catch { /* column already exists — ignore */ }
        }

        // ── Read pending rows ──────────────────────────────────────────────
        private List<SyncQueueRow> GetPendingRows()
        {
            var list = new List<SyncQueueRow>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT QueueId, TransactionID, InvoiceNo, RetryCount
                    FROM   POS_SyncQueue
                    WHERE  Status IN ('Pending', 'Failed')
                      AND  RetryCount < @max
                      AND  (NextRetryDateTime IS NULL
                            OR NextRetryDateTime <= @now)
                    ORDER  BY QueueId
                    LIMIT  20;";
                cmd.Parameters.AddWithValue("@max", MAX_RETRY);
                cmd.Parameters.AddWithValue("@now", Iso(DateTime.UtcNow));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new SyncQueueRow
                    {
                        QueueId = r.GetInt32(0),
                        TransactionId = r.GetInt32(1),
                        InvoiceNo = r.IsDBNull(2) ? "" : r.GetString(2),
                        RetryCount = r.GetInt32(3)
                    });
            }
            catch (Exception ex) { Debug.WriteLine("GetPendingRows: " + ex.Message); }
            return list;
        }

        // ── Status updates ─────────────────────────────────────────────────
        private void SetStatus(int queueId, string status)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "UPDATE POS_SyncQueue SET Status=@s WHERE QueueId=@id;";
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@id", queueId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Debug.WriteLine("SetStatus: " + ex.Message); }
        }

        private void MarkSynced(SyncQueueRow row, string d365Id)
        {
            try
            {
                using var conn = Open();
                using var tx = conn.BeginTransaction();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        UPDATE POS_SyncQueue
                        SET  Status            = 'Completed',
                             ProcessedDateTime = @now
                        WHERE QueueId = @id;";
                    cmd.Parameters.AddWithValue("@now", Iso(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", row.QueueId);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        UPDATE SOInvoiceHeader
                        SET  SyncStatus      = 'Synced',
                             D365SalesOrderId = @d365id,
                             ModifiedDate    = @now
                        WHERE InvoiceNo = @inv;";
                    cmd.Parameters.AddWithValue("@d365id", d365Id ?? "");
                    cmd.Parameters.AddWithValue("@now", Iso(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@inv", row.InvoiceNo);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex) { Debug.WriteLine("MarkSynced: " + ex.Message); }
        }

        private void MarkFailed(SyncQueueRow row, string error)
        {
            try
            {
                int newRetry = row.RetryCount + 1;
                // Exponential back-off: 5, 10, 20 minutes
                int delayMin = RETRY_DELAY_MINUTES * (int)Math.Pow(2, row.RetryCount);
                string nextRetry = Iso(DateTime.UtcNow.AddMinutes(delayMin));
                string finalStatus = newRetry >= MAX_RETRY ? "Failed" : "Pending";

                using var conn = Open();
                using var tx = conn.BeginTransaction();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        UPDATE POS_SyncQueue
                        SET  Status            = @status,
                             RetryCount        = @retry,
                             NextRetryDateTime = @next,
                             LastErrorMessage  = @err
                        WHERE QueueId = @id;";
                    cmd.Parameters.AddWithValue("@status", finalStatus);
                    cmd.Parameters.AddWithValue("@retry", newRetry);
                    cmd.Parameters.AddWithValue("@next", nextRetry);
                    cmd.Parameters.AddWithValue("@err", error ?? "");
                    cmd.Parameters.AddWithValue("@id", row.QueueId);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        UPDATE SOInvoiceHeader
                        SET  SyncStatus       = @status,
                             RetryCount       = @retry,
                             LastRetryDateTime = @now
                        WHERE InvoiceNo = @inv;";
                    cmd.Parameters.AddWithValue("@status", finalStatus);
                    cmd.Parameters.AddWithValue("@retry", newRetry);
                    cmd.Parameters.AddWithValue("@now", Iso(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@inv", row.InvoiceNo);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex) { Debug.WriteLine("MarkFailed: " + ex.Message); }
        }

        private void WriteLog(SyncQueueRow row, string req, string resp,
            bool success, string error)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO POS_SyncLog
                        (TransactionID, RequestPayLoadID, ResponsePayLoadID,
                         Status, ErrorMessage, CreatedDatetime)
                    VALUES
                        (@tid, @req, @resp, @status, @err, @now);";
                cmd.Parameters.AddWithValue("@tid", row.TransactionId);
                cmd.Parameters.AddWithValue("@req", req ?? "");
                cmd.Parameters.AddWithValue("@resp", resp ?? "");
                cmd.Parameters.AddWithValue("@status", success ? "Success" : "Failed");
                cmd.Parameters.AddWithValue("@err", error ?? "");
                cmd.Parameters.AddWithValue("@now", Iso(DateTime.UtcNow));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Debug.WriteLine("WriteLog: " + ex.Message); }
        }

        // ── Data readers ───────────────────────────────────────────────────
        private InvoiceHeaderDto GetInvoiceHeader(string invoiceNo)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT InvoiceID, InvoiceNo, InvoiceAccountName,
                           PostingDate, TotalInvoiceAmount, CompanyID
                    FROM   SOInvoiceHeader
                    WHERE  InvoiceNo = @inv LIMIT 1;";
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    return new InvoiceHeaderDto
                    {
                        InvoiceId = r.GetInt32(0),
                        InvoiceNo = r.IsDBNull(1) ? "" : r.GetString(1),
                        InvoiceAccountName = r.IsDBNull(2) ? "" : r.GetString(2),
                        PostingDate = r.IsDBNull(3) ? "" : r.GetString(3),
                        TotalInvoiceAmount = r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetValue(4)),
                        CompanyId = r.IsDBNull(5) ? 0 : r.GetInt32(5)
                    };
            }
            catch (Exception ex) { Debug.WriteLine("GetInvoiceHeader: " + ex.Message); }
            return null;
        }

        private List<InvoiceLineDto> GetInvoiceLines(int invoiceId)
        {
            var list = new List<InvoiceLineDto>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT ItemName, Qty, UnitPrice, DiscountAmount, TotalAmount
                    FROM   SOInvoiceLine
                    WHERE  InvoiceID = @id;";
                cmd.Parameters.AddWithValue("@id", invoiceId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new InvoiceLineDto
                    {
                        ItemName = r.IsDBNull(0) ? "" : r.GetString(0),
                        Qty = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1)),
                        UnitPrice = r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2)),
                        DiscountAmount = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
                        TotalAmount = r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetValue(4))
                    });
            }
            catch (Exception ex) { Debug.WriteLine("GetInvoiceLines: " + ex.Message); }
            return list;
        }

        private List<InvoicePaymentDto> GetInvoicePayments(int invoiceId)
        {
            var list = new List<InvoicePaymentDto>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT PaymentType, PaymentAmount
                    FROM   SOInvoicePayment
                    WHERE  InvoiceID = @id;";
                cmd.Parameters.AddWithValue("@id", invoiceId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new InvoicePaymentDto
                    {
                        PaymentType = r.IsDBNull(0) ? "CASH" : r.GetString(0),
                        PaymentAmount = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1))
                    });
            }
            catch (Exception ex) { Debug.WriteLine("GetInvoicePayments: " + ex.Message); }
            return list;
        }

        // ── Utility ────────────────────────────────────────────────────────
        private SQLiteConnection Open()
        {
            var c = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
            c.Open();
            using var p = new SQLiteCommand("PRAGMA journal_mode=WAL;", c);
            p.ExecuteNonQuery();
            return c;
        }

        private static void Exec(SQLiteConnection c, string sql)
        {
            // SQLite ignores everything after the first semicolon in a single
            // command — split on semicolons to run each statement individually.
            foreach (var stmt in sql.Split(';'))
            {
                string s = stmt.Trim();
                if (string.IsNullOrEmpty(s)) continue;
                using var cmd = new SQLiteCommand(s, c);
                cmd.ExecuteNonQuery();
            }
        }

        private static string Iso(DateTime dt) =>
            dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        // ── Inner DTOs ─────────────────────────────────────────────────────
        private class SyncQueueRow
        {
            public int QueueId { get; set; }
            public int TransactionId { get; set; }
            public string InvoiceNo { get; set; }
            public int RetryCount { get; set; }
        }

        private class InvoiceHeaderDto
        {
            public int InvoiceId { get; set; }
            public string InvoiceNo { get; set; }
            public string InvoiceAccountName { get; set; }
            public string PostingDate { get; set; }
            public decimal TotalInvoiceAmount { get; set; }
            public int CompanyId { get; set; }
        }

        private class InvoiceLineDto
        {
            public string ItemName { get; set; }
            public decimal Qty { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountAmount { get; set; }
            public decimal TotalAmount { get; set; }
        }

        private class InvoicePaymentDto
        {
            public string PaymentType { get; set; }
            public decimal PaymentAmount { get; set; }
        }
    }


    // ══════════════════════════════════════════════════════════════════════
    //  AzureAdTokenService
    //  Separate from the sync service so you can swap auth strategies.
    //  Pass GetAccessTokenAsync as the token factory above.
    // ══════════════════════════════════════════════════════════════════════
    public class AzureAdTokenService
    {
        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _scope;

        private string _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public AzureAdTokenService(
            string tenantId = "91ce49f5-eaf7-4049-8242-435e862944ed",
            string clientId = "fa1f34ba-85db-4efc-b111-e5c1f82b81af",
            string clientSecret = "FP~8Q~xRu.cKKfsWBAe06OV.AvFDKn9Kv0TVzaoL",
            string scope = "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com/.default")
        {
            _tenantId = tenantId;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _scope = scope;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            // Return cached token if still valid (5-minute buffer)
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
                return _cachedToken;

            using var http = new HttpClient();
            var body = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["scope"] = _scope
            };

            var resp = await http.PostAsync(
                $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token",
                new FormUrlEncodedContent(body)).ConfigureAwait(false);

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Token error: {json}");

            _cachedToken = doc.GetProperty("access_token").GetString() ?? "";
            int expiresIn = doc.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

            return _cachedToken;
        }
    }
}