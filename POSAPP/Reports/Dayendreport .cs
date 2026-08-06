
using Newtonsoft.Json;
using POSAPP.Invoice;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using static POSAPP.Reports.DayEndScheduler;

namespace POSAPP.Reports
{
    public class InvoiceData
    {
        // Your model class for cart items
        public class CartItem
        {
            public int ItemNo { get; set; }
            public string ItemName { get; set; }
            public string StockCode { get; set; }
            public string BatchNo { get; set; }
            public string SerialNo { get; set; }
            public decimal Qty { get; set; }
            public int UOM { get; set; }
            public string UOM_Text { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal ListPrice { get; set; }
            public decimal LineNetAmount { get; set; }
            public decimal ChargesAmount { get; set; }
            public decimal Tax { get; set; }
            public decimal DiscountAmount { get; set; }
            public decimal TotalAmount { get; set; }
        }
    }
    // ══════════════════════════════════════════════════════════════════════════
    //  SaleRecord  — one row per payment LEG
    // ══════════════════════════════════════════════════════════════════════════
    public class SaleRecord
    {
        public int SaleID { get; set; }
        public string InvoiceNo { get; set; }
        public string CustomerCode { get; set; } = "CSG001";
        public string CustomerName { get; set; }
        public string CashierName { get; set; }
        public decimal InvoiceAmt { get; set; }
        public string PaymentType { get; set; }
        public string PaymentDesc { get; set; }
        public decimal PaymentAmt { get; set; }
        public DateTime SaleDate { get; set; }
        public bool IsReturn { get; set; }
        public int CompanyID { get; set; }
    }

    public class DayEndSummaryLine
    {
        public string TypeCode { get; set; }
        public string TypeDesc { get; set; }
        public decimal Gross { get; set; }
        public int Count { get; set; }
        public decimal Net { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SyncQueueRow DTO
    // ══════════════════════════════════════════════════════════════════════════
    public class SyncQueueRow
    {
        public long QueueId { get; set; }
        public long TransactionId { get; set; }   // = SOInvoiceHeader.InvoiceID
        public string InvoiceNo { get; set; } = "";
        public string SyncType { get; set; } = "";
        public string SyncStatus { get; set; } = "";  // Pending|Processing|Synced|Failed
        public int RetryCount { get; set; }
        public string? LastRetryDateTime { get; set; }
        public string? LastSyncMessage { get; set; }
        public string? D365SalesOrderId { get; set; }   // SOSalesId returned by D365
        public string? D365InvoiceId { get; set; }   // InvoiceId confirmed by D365
        public string CreatedDateTime { get; set; } = "";
        public string? ProcessedDateTime { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SalesRepository
    // ══════════════════════════════════════════════════════════════════════════
    public static class SalesRepository
    {
        private static readonly string DbPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ABC.db");

        private static int _invoiceSeq = 0;
        // Add this inside SalesRepository class
        public static bool IsQuotation(string invoiceNo) =>
        invoiceNo.StartsWith("QUO-", StringComparison.OrdinalIgnoreCase)
     || invoiceNo.StartsWith("QT-", StringComparison.OrdinalIgnoreCase)
     || invoiceNo.StartsWith("Q-", StringComparison.OrdinalIgnoreCase);
        private static readonly object _seqLock = new object();

        // ── Invoice number generator ──────────────────────────────────────────
        private static string _lastGeneratedInvoiceNo = "";
        private static string _lastGeneratedDate = "";

        public static string NextInvoiceNo()
        {
            string datePrefix = DateTime.Now.ToString("yyMMdd");
            string today = DateTime.Today.ToString("yyyy-MM-dd");

            if (_lastGeneratedDate == today && !string.IsNullOrEmpty(_lastGeneratedInvoiceNo))
                return _lastGeneratedInvoiceNo;

            try
            {
                string dbPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "ABC.db");

                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();

                using (var ensure = conn.CreateCommand())
                {
                    ensure.CommandText = @"
                        CREATE TABLE IF NOT EXISTS InvoiceCounter (
                            CounterDate TEXT PRIMARY KEY,
                            LastSeq     INTEGER NOT NULL DEFAULT 0
                        );";
                    ensure.ExecuteNonQuery();
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO InvoiceCounter (CounterDate, LastSeq) VALUES (@d, 1)
                    ON CONFLICT(CounterDate) DO UPDATE SET LastSeq = LastSeq + 1;
                    SELECT LastSeq FROM InvoiceCounter WHERE CounterDate = @d;";
                cmd.Parameters.AddWithValue("@d", today);

                long seq = Convert.ToInt64(cmd.ExecuteScalar() ?? 1L);
                string invoiceNo = $"INV-{datePrefix}{seq:D5}";

                _lastGeneratedInvoiceNo = invoiceNo;
                _lastGeneratedDate = today;

                return invoiceNo;
            }
            catch
            {
                return $"INV-{datePrefix}{DateTime.Now:HHmmss}";
            }
        }
        public static string NextQuotationNo()
        {
            string prefix = "QUO-" + DateTime.Now.ToString("yyyyMMdd");
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT MAX(InvoiceNo) FROM PendingInvoice
        WHERE InvoiceNo LIKE @p;";
            cmd.Parameters.AddWithValue("@p", prefix + "%");
            var result = cmd.ExecuteScalar();

            int next = 1;
            if (result != null && result != DBNull.Value)
            {
                string last = result.ToString();
                string seq = last.Substring(prefix.Length);
                if (int.TryParse(seq, out int n)) next = n + 1;
            }
            return prefix + next.ToString("D3");
        }

        // No separate "Consume" needed for quotations — number is generated
        // and immediately saved into PendingInvoice, so it's effectively reserved.

        public static void ConsumeInvoiceNo()
        {
            _lastGeneratedInvoiceNo = "";
            _lastGeneratedDate = "";
        }
        // ══════════════════════════════════════════════════════════════════════════════
        //  REPLACE these methods inside your existing SalesRepository static class
        // ══════════════════════════════════════════════════════════════════════════════

        // ── PosApiResponse ────────────────────────────────────────────────────────────

        public static void EnsurePosApiResponseTable()
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            const string sql = @"
        CREATE TABLE IF NOT EXISTS PosApiResponse (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            CompanyId     TEXT,
            StoreId       TEXT,
            InvoiceType   TEXT,
            InvoiceId     TEXT,
            ResponseBody  TEXT,
            RequestBody   TEXT,
            StatusCode    TEXT,
            SyncStatus    TEXT,
            CreatedOn     TEXT
        );";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        public static void InsertPosApiResponse(
            string companyId, string storeId, string invoiceType,
            string invoiceId, string requestBody, string responseBody,
            string statusCode, string syncStatus)
        {
            try
            {
                EnsurePosApiResponseTable();   // safe to call every time

                using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                conn.Open();

                const string sql = @"
            INSERT INTO PosApiResponse
                (CompanyId, StoreId, InvoiceType, InvoiceId,
                 RequestBody, ResponseBody, StatusCode, SyncStatus, CreatedOn)
            VALUES
                (@CompanyId, @StoreId, @InvoiceType, @InvoiceId,
                 @RequestBody, @ResponseBody, @StatusCode, @SyncStatus, @CreatedOn);";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@CompanyId", companyId ?? "");
                cmd.Parameters.AddWithValue("@StoreId", storeId ?? "");
                cmd.Parameters.AddWithValue("@InvoiceType", invoiceType ?? "");
                cmd.Parameters.AddWithValue("@InvoiceId", invoiceId ?? "");
                cmd.Parameters.AddWithValue("@RequestBody", requestBody ?? "");
                cmd.Parameters.AddWithValue("@ResponseBody", responseBody ?? "");
                cmd.Parameters.AddWithValue("@StatusCode", statusCode ?? "");
                cmd.Parameters.AddWithValue("@SyncStatus", syncStatus ?? "");
                cmd.Parameters.AddWithValue("@CreatedOn",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rows = cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine(
                    $"[PosApiResponse] Inserted {rows} row — InvoiceId={invoiceId} Type={invoiceType} Status={statusCode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PosApiResponse] INSERT failed: {ex.Message}");
            }
        }

        // ── Stuck processing row reset ────────────────────────────────────────────────

        public static void ResetStuckProcessingRows(int stuckMinutes = 10)
        {
            try
            {
                using var conn = Open();
                using var cmd = new SQLiteCommand(@"
            UPDATE POS_SyncQueue
               SET SyncStatus      = 'Failed',
                   LastSyncMessage = 'Reset from stuck Processing state'
             WHERE SyncStatus = 'Processing'
               AND (
                   LastRetryDateTime IS NULL
                   OR LastRetryDateTime <= datetime('now', '-' || @minutes || ' minutes')
               );", conn);
                cmd.Parameters.AddWithValue("@minutes", stuckMinutes);
                int affected = cmd.ExecuteNonQuery();
                if (affected > 0)
                    System.Diagnostics.Debug.WriteLine(
                        $"[Sync] Reset {affected} stuck Processing rows.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Sync] ResetStuckProcessingRows failed: {ex.Message}");
            }
        }

        // ── GetPendingSyncQueue with retry delay ──────────────────────────────────────

        public static List<SyncQueueRow> GetPendingSyncQueue(int maxRetry = 5)
        {
            using var conn = Open();
            var rows = new List<SyncQueueRow>();

            using var cmd = new SQLiteCommand(@"
        SELECT QueueId, TransactionId, InvoiceNo, SyncType,
               SyncStatus, RetryCount, LastRetryDateTime,
               LastSyncMessage, D365SalesOrderId, D365InvoiceId,
               CreatedDateTime, ProcessedDateTime
          FROM POS_SyncQueue
         WHERE SyncStatus IN ('Pending','Failed')
           AND RetryCount < @MaxRetry
           AND (
               LastRetryDateTime IS NULL
               OR LastRetryDateTime <= datetime('now', '-3 minutes')
           )
         ORDER BY CreatedDateTime;", conn);

            cmd.Parameters.AddWithValue("@MaxRetry", maxRetry);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(new SyncQueueRow
                {
                    QueueId = r.GetInt64(0),
                    TransactionId = r.GetInt64(1),
                    InvoiceNo = r.GetString(2),
                    SyncType = r.GetString(3),
                    SyncStatus = r.GetString(4),
                    RetryCount = r.GetInt32(5),
                    LastRetryDateTime = r.IsDBNull(6) ? null : r.GetString(6),
                    LastSyncMessage = r.IsDBNull(7) ? null : r.GetString(7),
                    D365SalesOrderId = r.IsDBNull(8) ? null : r.GetString(8),
                    D365InvoiceId = r.IsDBNull(9) ? null : r.GetString(9),
                    CreatedDateTime = r.GetString(10),
                    ProcessedDateTime = r.IsDBNull(11) ? null : r.GetString(11)
                });

            return rows;
        }

        // ── CompleteSyncAttempt (unchanged but included for completeness) ──────────────

        public static void CompleteSyncAttempt(
            long queueId,
            long logId,
            bool success,
            string? responsePayload,
            string? message,
            string? d365SalesOrderId = null,
            string? d365InvoiceId = null)
        {
            using var conn = Open();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string status = success ? "Synced" : "Failed";

            Exec(conn, @"
        UPDATE POS_SyncQueue
           SET SyncStatus        = @Status,
               LastRetryDateTime = @Now,
               LastSyncMessage   = @Message,
               RetryCount        = RetryCount + 1,
               D365SalesOrderId  = COALESCE(@D365SalesOrderId, D365SalesOrderId),
               D365InvoiceId     = COALESCE(@D365InvoiceId,    D365InvoiceId),
               ProcessedDateTime = CASE WHEN @Success = 1 THEN @Now
                                        ELSE ProcessedDateTime END
         WHERE QueueId = @QueueId;",
                ("@Status", status),
                ("@Now", now),
                ("@Message", (object?)message ?? DBNull.Value),
                ("@D365SalesOrderId", (object?)d365SalesOrderId ?? DBNull.Value),
                ("@D365InvoiceId", (object?)d365InvoiceId ?? DBNull.Value),
                ("@Success", success ? 1 : 0),
                ("@QueueId", queueId));

            if (logId > 0)
            {
                Exec(conn, @"
            UPDATE POS_SyncLog
               SET ResponsePayload = @ResponsePayload,
                   Status          = @Status,
                   LastMessage     = @Message
             WHERE LogId = @LogId;",
                    ("@ResponsePayload", (object?)responsePayload ?? DBNull.Value),
                    ("@Status", success ? "Succeeded" : "Failed"),
                    ("@Message", (object?)message ?? DBNull.Value),
                    ("@LogId", logId));
            }

            if (success)
                TouchSyncControl("Sales", now);
        }


        // ══════════════════════════════════════════════════════════════════════
        //  RecentSales helpers
        // ══════════════════════════════════════════════════════════════════════
        public static void EnsureRecentSalesSchema()
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS RecentSales (
                    RecentSaleID  INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceNo     TEXT    NOT NULL,
                    GrandTotal    REAL    NOT NULL DEFAULT 0,
                    SaleDate      TEXT    NOT NULL,
                    CompanyID     INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_RecentSales_Date
                    ON RecentSales(SaleDate, CompanyID);";
            cmd.ExecuteNonQuery();
        }

        public static void SaveRecentSale(string invoiceNo, decimal grandTotal, int companyId)
        {
            EnsureRecentSalesSchema();
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO RecentSales (InvoiceNo, GrandTotal, SaleDate, CompanyID)
                VALUES (@inv, @total, @date, @cid);";
            cmd.Parameters.AddWithValue("@inv", invoiceNo);
            cmd.Parameters.AddWithValue("@total", (double)grandTotal);
            cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@cid", companyId);
            cmd.ExecuteNonQuery();
        }

        public static List<(string InvoiceNo, decimal GrandTotal, DateTime SaleDate)>
            GetTodayRecentSales(int companyId)
        {
            EnsureRecentSalesSchema();
            var list = new List<(string, decimal, DateTime)>();
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT InvoiceNo, GrandTotal, SaleDate
                FROM   RecentSales
                WHERE  CompanyID = @cid
                  AND  DATE(SaleDate) = DATE('now', 'localtime')
                ORDER  BY RecentSaleID DESC
                LIMIT  100;";
            cmd.Parameters.AddWithValue("@cid", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string inv = r.IsDBNull(0) ? "" : r.GetString(0);
                decimal tot = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
                DateTime dt = r.IsDBNull(2) ? DateTime.Now
                               : DateTime.TryParse(r.GetString(2), out var d) ? d : DateTime.Now;
                list.Add((inv, tot, dt));
            }
            return list;
        }

        private static int LoadLastSeq()
        {
            if (!File.Exists(DbPath)) return 0;
            try
            {
                using var conn = Open();
                string today = DateTime.Now.ToString("yyyyMMdd");
                using var cmd = new SQLiteCommand(
                    $"SELECT MAX(CAST(SUBSTR(InvoiceNo,-4) AS INTEGER)) " +
                    $"FROM SOInvoiceHeader WHERE InvoiceNo LIKE 'INV-{today}-%';", conn);
                var result = cmd.ExecuteScalar();
                if (result != DBNull.Value && result != null) return Convert.ToInt32(result);
            }
            catch { }
            return 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  EnsureAllTables — call this anywhere; safe on existing databases
        // ══════════════════════════════════════════════════════════════════════
        public static void EnsureAllTables()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ABC.db");
            using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
            using var pragma = new SQLiteCommand("PRAGMA journal_mode=WAL;", conn);
            pragma.ExecuteNonQuery();

            var tables = new[]
            {
                @"CREATE TABLE IF NOT EXISTS SOInvoiceHeader (
                    InvoiceID           INTEGER PRIMARY KEY AUTOINCREMENT,
                    CompanyID           INTEGER NOT NULL DEFAULT 0,
                    StoreID             INTEGER NOT NULL DEFAULT 0,
                    TerminalID          INTEGER NOT NULL DEFAULT 0,
                    CustomerID          INTEGER NOT NULL DEFAULT 0,
                    VATRegistrationID   INTEGER NULL,
                    InvoiceAccount      INTEGER NULL,
                    InvoiceAccountName  TEXT    NULL,
                    InvoiceNo           TEXT    NOT NULL UNIQUE,
                    InvoiceDescription  TEXT    NULL,
                    InvoiceDate         TEXT    NOT NULL,
                    SONumber            TEXT    NOT NULL DEFAULT '',
                    PostingDate         TEXT    NOT NULL,
                    DueDate             TEXT    NULL,
                    TotalInvoiceAmount  REAL    NOT NULL DEFAULT 0,
                    Comments            TEXT    NULL,
                    SalesStatus         INTEGER NOT NULL DEFAULT 1,
                    CreatedBy           INTEGER NOT NULL DEFAULT 0,
                    CreatedDate         TEXT    NOT NULL,
                    ModifiedBy          INTEGER NULL,
                    ModifiedDate        TEXT    NULL
                );",

                @"CREATE TABLE IF NOT EXISTS SOInvoiceLine (
                    InvoiceLineID   INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceID       INTEGER NOT NULL,
                    ItemNo          INTEGER NOT NULL DEFAULT 0,
                    ItemName        TEXT    NOT NULL DEFAULT '',
                    BatchNo         TEXT    NULL,
                    SerialNo        TEXT    NULL,
                    Qty             REAL    NOT NULL DEFAULT 1,
                    UOM             INTEGER NOT NULL DEFAULT 0,
                    UnitPrice       REAL    NOT NULL DEFAULT 0,
                    LineNetAmount   REAL    NOT NULL DEFAULT 0,
                    ChargesAmount   REAL    NOT NULL DEFAULT 0,
                    Tax             REAL    NOT NULL DEFAULT 0,
                    DiscountAmount  REAL    NOT NULL DEFAULT 0,
                    TotalAmount     REAL    NOT NULL DEFAULT 0,
                    CreatedBy       INTEGER NOT NULL DEFAULT 0,
                    CreatedDate     TEXT    NOT NULL,
                    ModifiedBy      INTEGER NULL,
                    ModifiedDate    TEXT    NULL,
                    FOREIGN KEY(InvoiceID) REFERENCES SOInvoiceHeader(InvoiceID)
                );",

                @"CREATE TABLE IF NOT EXISTS SOInvoicePayment (
                    PaymentID       INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceID       INTEGER NOT NULL,
                    PaymentDate     TEXT    NOT NULL,
                    PaymentMethod   INTEGER NOT NULL DEFAULT 0,
                    PaymentAmount   REAL    NOT NULL DEFAULT 0,
                    PaymentType     TEXT    NOT NULL DEFAULT 'CASH',
                    PaymentDesc     TEXT    NOT NULL DEFAULT 'CASH - CASH',
                    CreatedBy       INTEGER NOT NULL DEFAULT 0,
                    CreatedDate     TEXT    NOT NULL,
                    ModifiedBy      INTEGER NULL,
                    ModifiedDate    TEXT    NULL,
                    FOREIGN KEY(InvoiceID) REFERENCES SOInvoiceHeader(InvoiceID)
                );",

                @"CREATE TABLE IF NOT EXISTS DayEndLog (
                    LogID        INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReportDate   TEXT    NOT NULL UNIQUE,
                    GeneratedAt  TEXT    NOT NULL,
                    TotalSales   REAL    NOT NULL,
                    InvoiceCount INTEGER NOT NULL
                );",

                @"CREATE TABLE IF NOT EXISTS PendingInvoice (
                    InvoiceNo     TEXT PRIMARY KEY,
                    CustomerName  TEXT,
                    SaleDate      TEXT,
                    GrandTotal    REAL,
                    Status        TEXT DEFAULT 'Unpaid',
                    CartJson      TEXT,
                    CompanyID     INTEGER
                );",

                @"CREATE TABLE IF NOT EXISTS DeletedInvoice (
                    ID            INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceNo     TEXT,
                    CustomerName  TEXT,
                    SaleDate      TEXT,
                    GrandTotal    REAL,
                    DeletedAt     TEXT,
                    DeletedBy     TEXT,
                    CompanyID     INTEGER
                );",

                // ── D365 Sync tables ─────────────────────────────────────────
                @"CREATE TABLE IF NOT EXISTS POS_SyncQueue (
                    QueueId           INTEGER PRIMARY KEY AUTOINCREMENT,
                    TransactionId     INTEGER NOT NULL,
                    InvoiceNo         TEXT    NOT NULL,
                    SyncType          TEXT    NOT NULL DEFAULT 'Sales',
                    SyncStatus        TEXT    NOT NULL DEFAULT 'Pending',
                    RetryCount        INTEGER NOT NULL DEFAULT 0,
                    LastRetryDateTime TEXT,
                    LastSyncMessage   TEXT,
                    D365SalesOrderId  TEXT,
                    D365InvoiceId     TEXT,
                    CreatedDateTime   TEXT    NOT NULL,
                    ProcessedDateTime TEXT
                );",

                @"CREATE TABLE IF NOT EXISTS POS_SyncLog (
                    LogId             INTEGER PRIMARY KEY AUTOINCREMENT,
                    TransactionId     INTEGER NOT NULL,
                    RequestPayload    TEXT,
                    ResponsePayload   TEXT,
                    Status            TEXT    NOT NULL DEFAULT 'Pending',
                    LastMessage       TEXT,
                    CreatedDateTime   TEXT    NOT NULL
                );",

                @"CREATE TABLE IF NOT EXISTS POS_SyncControl (
                    SyncType         TEXT PRIMARY KEY,
                    LastSyncDateTime TEXT,
                    Inventory        TEXT
                );"
            };

            foreach (var sql in tables)
            {
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
            }

            // Seed POS_SyncControl rows (safe — INSERT OR IGNORE)
            foreach (var t in new[] { "Sales", "Inventory", "Product" })
            {
                using var seed = new SQLiteCommand(
                    "INSERT OR IGNORE INTO POS_SyncControl (SyncType, LastSyncDateTime) VALUES (@t, '2024-05-25 06:00:00');",
                    conn);
                seed.Parameters.AddWithValue("@t", t);
                seed.ExecuteNonQuery();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MigrateSyncQueueColumns
        //  Safe to call every startup — adds missing columns to existing DB
        // ══════════════════════════════════════════════════════════════════════
        public static void MigrateSyncQueueColumns()
        {
            using var conn = Open();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new SQLiteCommand("PRAGMA table_info(POS_SyncQueue);", conn))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    existing.Add(r["name"].ToString()!);

            // If the table doesn't exist yet EnsureAllTables will create it — skip
            if (existing.Count == 0) return;

            void AddCol(string col, string def)
            {
                if (!existing.Contains(col))
                    Exec(conn, $"ALTER TABLE POS_SyncQueue ADD COLUMN {col} {def};");
            }

            AddCol("SyncStatus", "TEXT NOT NULL DEFAULT 'Pending'");
            AddCol("LastRetryDateTime", "TEXT");
            AddCol("D365SalesOrderId", "TEXT");
            AddCol("D365InvoiceId", "TEXT");
        }

        public static void MigrateInvoiceLineColumns()
        {
            using var conn = Open();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new SQLiteCommand("PRAGMA table_info(SOInvoiceLine);", conn))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    existing.Add(r["name"].ToString()!);

            if (existing.Count == 0) return;

            void AddCol(string col, string def)
            {
                if (!existing.Contains(col))
                    Exec(conn, $"ALTER TABLE SOInvoiceLine ADD COLUMN {col} {def};");
            }

            AddCol("StockCode", "TEXT NOT NULL DEFAULT ''");
            AddCol("UOM_Text", "TEXT NOT NULL DEFAULT 'EA'");
            AddCol("ListPrice", "REAL NOT NULL DEFAULT 0");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  EnsureSchema  (keeps both legacy and new tables in sync)
        // ══════════════════════════════════════════════════════════════════════
        public static void EnsureSchema()
        {
            // Delegates to EnsureAllTables which covers everything
            EnsureAllTables();
            MigrateSyncQueueColumns();
            MigrateInvoiceLineColumns();
        }

        public static void EnsurePendingInvoiceSchema() => EnsureSchema();

        // ══════════════════════════════════════════════════════════════════════
        //  Quotation helpers
        // ══════════════════════════════════════════════════════════════════════
        public static void EnsureQuotationSchema()
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Quotations (
                    QuotationID     INTEGER PRIMARY KEY AUTOINCREMENT,
                    QuotationNo     TEXT    NOT NULL UNIQUE,
                    CustomerName    TEXT,
                    CustomerAddress TEXT,
                    CustomerVat     TEXT,
                    PriceGroup      TEXT,
                    GrandTotal      REAL,
                    CartJson        TEXT,
                    CompanyID       INTEGER,
                    CreatedAt       TEXT    DEFAULT (datetime('now','localtime')),
                    Status          TEXT    DEFAULT 'Open'
                );
                CREATE INDEX IF NOT EXISTS IX_Quot_Co
                    ON Quotations(CompanyID, CreatedAt DESC);";
            cmd.ExecuteNonQuery();
        }



        public static void SaveQuotation(
            string quotNo, string customer, string address,
            string vat, string priceGroup, decimal grand,
            string cartJson, int companyId)
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Quotations
                    (QuotationNo,CustomerName,CustomerAddress,CustomerVat,
                     PriceGroup,GrandTotal,CartJson,CompanyID)
                VALUES (@qno,@cust,@addr,@vat,@pg,@grand,@cart,@co);";
            cmd.Parameters.AddWithValue("@qno", quotNo);
            cmd.Parameters.AddWithValue("@cust", customer);
            cmd.Parameters.AddWithValue("@addr", address);
            cmd.Parameters.AddWithValue("@vat", vat);
            cmd.Parameters.AddWithValue("@pg", priceGroup);
            cmd.Parameters.AddWithValue("@grand", (double)grand);
            cmd.Parameters.AddWithValue("@cart", cartJson);
            cmd.Parameters.AddWithValue("@co", companyId);
            cmd.ExecuteNonQuery();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PendingInvoice helpers
        // ══════════════════════════════════════════════════════════════════════
        public static void SavePendingInvoice(string invoiceNo, string customerName,
               decimal grandTotal, string cartJson, int companyId)
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = new SQLiteCommand(@"
                INSERT OR REPLACE INTO PendingInvoice
                    (InvoiceNo, CustomerName, SaleDate, GrandTotal, Status, CartJson, CompanyID)
                VALUES
                    (@inv, @cust, @date, @total, 'Unpaid', @cart, @co);", conn);
            cmd.Parameters.AddWithValue("@inv", invoiceNo);
            cmd.Parameters.AddWithValue("@cust", customerName);
            cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@total", (double)grandTotal);
            cmd.Parameters.AddWithValue("@cart", cartJson);
            cmd.Parameters.AddWithValue("@co", companyId);
            cmd.ExecuteNonQuery();
        }

        public static void UpsertPendingInvoice(
            string invoiceNo, string customerName,
            decimal grandTotal, string cartJson, int companyId)
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();

            using var chk = new SQLiteCommand(
                "SELECT COUNT(*) FROM PendingInvoice WHERE InvoiceNo = @inv AND Status = 'Unpaid';", conn);
            chk.Parameters.AddWithValue("@inv", invoiceNo);
            long exists = (long)(chk.ExecuteScalar() ?? 0L);

            if (exists > 0)
            {
                using var upd = new SQLiteCommand(@"
                    UPDATE PendingInvoice
                    SET CustomerName = @cust,
                        GrandTotal   = @total,
                        CartJson     = @cart,
                        SaleDate     = @date
                    WHERE InvoiceNo = @inv AND Status = 'Unpaid';", conn);
                upd.Parameters.AddWithValue("@cust", customerName);
                upd.Parameters.AddWithValue("@total", grandTotal);
                upd.Parameters.AddWithValue("@cart", cartJson);
                upd.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
                upd.Parameters.AddWithValue("@inv", invoiceNo);
                upd.ExecuteNonQuery();
                DashboardEventBus.Notify();
            }
            else
            {
                using var ins = new SQLiteCommand(@"
                    INSERT INTO PendingInvoice
                        (InvoiceNo, CustomerName, GrandTotal, CartJson, Status, CompanyID, SaleDate)
                    VALUES
                        (@inv, @cust, @total, @cart, 'Unpaid', @company, @date);", conn);
                ins.Parameters.AddWithValue("@inv", invoiceNo);
                ins.Parameters.AddWithValue("@cust", customerName);
                ins.Parameters.AddWithValue("@total", grandTotal);
                ins.Parameters.AddWithValue("@cart", cartJson);
                ins.Parameters.AddWithValue("@company", companyId);
                ins.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
                ins.ExecuteNonQuery();
            }
        }

        public static void DeletePendingInvoice(string invoiceNo, int companyId)
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();

            using var sel = new SQLiteCommand(
                "SELECT CustomerName, SaleDate, GrandTotal FROM PendingInvoice WHERE InvoiceNo=@inv AND Status='Unpaid';", conn);
            sel.Parameters.AddWithValue("@inv", invoiceNo);
            using var r = sel.ExecuteReader();
            if (r.Read())
            {
                string custName = r["CustomerName"]?.ToString() ?? "";
                string saleDate = r["SaleDate"]?.ToString() ?? "";
                decimal total = Convert.ToDecimal(r["GrandTotal"]);
                r.Close();

                using var ins = new SQLiteCommand(@"
                    INSERT INTO DeletedInvoice (InvoiceNo, CustomerName, SaleDate, GrandTotal, DeletedAt, DeletedBy, CompanyID)
                    VALUES (@inv, @cust, @date, @total, @delAt, @delBy, @co);", conn);
                ins.Parameters.AddWithValue("@inv", invoiceNo);
                ins.Parameters.AddWithValue("@cust", custName);
                ins.Parameters.AddWithValue("@date", saleDate);
                ins.Parameters.AddWithValue("@total", (double)total);
                ins.Parameters.AddWithValue("@delAt", DateTime.Now.ToString("o"));
                ins.Parameters.AddWithValue("@delBy", Environment.UserName.ToUpper());
                ins.Parameters.AddWithValue("@co", companyId);
                ins.ExecuteNonQuery();
            }
            else { r.Close(); }

            using var del = new SQLiteCommand(
                "DELETE FROM PendingInvoice WHERE InvoiceNo = @inv AND Status = 'Unpaid';", conn);
            del.Parameters.AddWithValue("@inv", invoiceNo);
            del.ExecuteNonQuery();
        }

        public static void DeletePendingInvoice(string invoiceNo)
            => DeletePendingInvoice(invoiceNo, 0);

        public static void MarkInvoicePaid(string invoiceNo)
        {
            if (string.IsNullOrWhiteSpace(invoiceNo)) return;
            try
            {
                using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "UPDATE PendingInvoice SET Status='Paid' WHERE InvoiceNo=@inv AND Status='Unpaid';", conn);
                cmd.Parameters.AddWithValue("@inv", invoiceNo.Trim());
                int rows = cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine(
                    $"[MarkInvoicePaid] InvoiceNo={invoiceNo} rows updated={rows}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MarkInvoicePaid] Error: {ex.Message}");
            }
        }

        public static List<PendingInvoiceRow> GetPendingInvoices(int companyId)
        {
            var list = new List<PendingInvoiceRow>();
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = new SQLiteCommand(@"
                SELECT InvoiceNo, CustomerName, SaleDate, GrandTotal, Status, CartJson
                FROM PendingInvoice
                WHERE CompanyID=@co
                ORDER BY SaleDate DESC;", conn);
            cmd.Parameters.AddWithValue("@co", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new PendingInvoiceRow
                {
                    InvoiceNo = r["InvoiceNo"].ToString(),
                    CustomerName = r["CustomerName"].ToString(),
                    SaleDate = DateTime.Parse(r["SaleDate"].ToString()),
                    GrandTotal = Convert.ToDecimal(r["GrandTotal"]),
                    Status = r["Status"].ToString(),
                    CartJson = r["CartJson"].ToString()
                });
            return list;
        }

        public static List<DeletedInvoiceRow> GetDeletedInvoices(int companyId, DateTime from, DateTime to)
        {
            var list = new List<DeletedInvoiceRow>();
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            using var cmd = new SQLiteCommand(@"
                SELECT InvoiceNo, CustomerName, SaleDate, GrandTotal, DeletedAt, DeletedBy
                FROM DeletedInvoice
                WHERE CompanyID=@co
                  AND DeletedAt >= @from
                  AND DeletedAt <  @to
                ORDER BY DeletedAt DESC;", conn);
            cmd.Parameters.AddWithValue("@co", companyId);
            cmd.Parameters.AddWithValue("@from", from.ToString("o"));
            cmd.Parameters.AddWithValue("@to", to.ToString("o"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new DeletedInvoiceRow
                {
                    InvoiceNo = r["InvoiceNo"].ToString(),
                    CustomerName = r["CustomerName"].ToString(),
                    SaleDate = DateTime.TryParse(r["SaleDate"].ToString(), out var sd) ? sd : DateTime.MinValue,
                    GrandTotal = Convert.ToDecimal(r["GrandTotal"]),
                    DeletedAt = DateTime.TryParse(r["DeletedAt"].ToString(), out var da) ? da : DateTime.MinValue,
                    DeletedBy = r["DeletedBy"].ToString()
                });
            return list;
        }

        public class PendingInvoiceRow
        {
            public string InvoiceNo { get; set; }
            public string CustomerName { get; set; }
            public DateTime SaleDate { get; set; }
            public decimal GrandTotal { get; set; }
            public string Status { get; set; }
            public string CartJson { get; set; }

            public string Source { get; set; } = "POS";   // "POS" or "SOInvoice"
            public int SourceSOId { get; set; }
        }

        public class DeletedInvoiceRow
        {
            public string InvoiceNo { get; set; }
            public string CustomerName { get; set; }
            public DateTime SaleDate { get; set; }
            public decimal GrandTotal { get; set; }
            public DateTime DeletedAt { get; set; }
            public string DeletedBy { get; set; }
        }

        // ── Add this class inside SalesRepository, after DeletedInvoiceRow ──────────
        public class ReprintLineDto
        {
            public string StockCode { get; set; }
            public string Name { get; set; }
            public string UOM { get; set; }
            public decimal Qty { get; set; }
            public decimal QtyRequested { get; set; }
            public decimal QtyDispatched { get; set; }
            public decimal UnitPrice { get; set; }   // matches BuildCartJsonFromLines
            public decimal ListPrice { get; set; }   // matches BuildCartJsonFromLines
            public decimal DiscountPct { get; set; }
            public decimal LineTotal { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SaveSale — writes Header / Lines / Payments then queues D365 sync
        // ══════════════════════════════════════════════════════════════════════
        public static int SaveSale(ReceiptData receipt, int companyId)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            string invoiceNo = receipt.InvoiceNo ?? NextInvoiceNo();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            // ── 1. Duplicate guard ─────────────────────────────────────────────────
            long existingId = 0;
            using (var chk = new SQLiteCommand(conn))
            {
                chk.Transaction = tx;
                chk.CommandText = "SELECT InvoiceID FROM SOInvoiceHeader WHERE InvoiceNo = @inv;";
                chk.Parameters.AddWithValue("@inv", invoiceNo);
                var result = chk.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    existingId = Convert.ToInt64(result);
            }

            if (existingId > 0)
            {
                using var lineChk = new SQLiteCommand(conn);
                lineChk.Transaction = tx;
                lineChk.CommandText = "SELECT COUNT(*) FROM SOInvoiceLine WHERE InvoiceID = @id;";
                lineChk.Parameters.AddWithValue("@id", existingId);
                long lineCount = Convert.ToInt64(lineChk.ExecuteScalar());

                if (lineCount > 0)
                {
                    tx.Rollback();
                    System.Diagnostics.Debug.WriteLine(
                        $"[SaveSale] Invoice {invoiceNo} already fully saved — skipping.");
                    return (int)existingId;
                }
            }

            // ── 2. Header ──────────────────────────────────────────────────────────
            long invoiceId;

            if (existingId > 0)
            {
                invoiceId = existingId;
                System.Diagnostics.Debug.WriteLine(
                    $"[SaveSale] Header exists for {invoiceNo} (ID={invoiceId}), inserting missing lines.");
            }
            else
            {
                using var hCmd = new SQLiteCommand(conn);
                hCmd.Transaction = tx;
                hCmd.CommandText = @"
            INSERT INTO SOInvoiceHeader (
                CompanyID, StoreID, TerminalID, CustomerID,
                VATRegistrationID, InvoiceAccount, InvoiceAccountName,
                InvoiceNo, InvoiceDescription, InvoiceDate,
                SONumber, PostingDate, DueDate,
                TotalInvoiceAmount, Comments, SalesStatus,
                CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            ) VALUES (
                @CompanyID, 0, 0, 0,
                NULL, NULL, @CustomerName,
                @InvoiceNo, NULL, @InvoiceDate,
                @InvoiceNo, @PostingDate, NULL,
                @TotalInvoiceAmount, NULL, 1,
                0, @CreatedDate, NULL, NULL
            );";
                hCmd.Parameters.AddWithValue("@CompanyID", companyId);
                hCmd.Parameters.AddWithValue("@CustomerName", receipt.CustomerName ?? "Walk-in");
                hCmd.Parameters.AddWithValue("@InvoiceNo", invoiceNo);
                hCmd.Parameters.AddWithValue("@InvoiceDate", now);
                hCmd.Parameters.AddWithValue("@PostingDate", today);
                hCmd.Parameters.AddWithValue("@TotalInvoiceAmount", (double)receipt.GrandTotal);
                hCmd.Parameters.AddWithValue("@CreatedDate", now);
                hCmd.ExecuteNonQuery();

                invoiceId = conn.LastInsertRowId;

                if (invoiceId <= 0)
                {
                    using var q = new SQLiteCommand(conn);
                    q.Transaction = tx;
                    q.CommandText = "SELECT InvoiceID FROM SOInvoiceHeader WHERE InvoiceNo = @inv;";
                    q.Parameters.AddWithValue("@inv", invoiceNo);
                    invoiceId = Convert.ToInt64(q.ExecuteScalar());
                }

                if (invoiceId <= 0)
                    throw new Exception($"[SaveSale] Failed to get InvoiceID for {invoiceNo}");
            }

            System.Diagnostics.Debug.WriteLine(
                $"[SaveSale] InvoiceNo={invoiceNo} InvoiceID={invoiceId} Lines={receipt.Lines?.Count ?? 0}");

            // ── 3. Lines ───────────────────────────────────────────────────────────
            if (receipt.Lines == null || receipt.Lines.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[SaveSale] WARNING: receipt.Lines is empty!");
            }
            else
            {
                decimal taxRate = receipt.TaxTotal > 0
                    ? receipt.TaxTotal / Math.Max(1m, receipt.Subtotal - receipt.DiscountTotal)
                    : 0m;

                int lineNum = 0;
                foreach (var line in receipt.Lines)
                {
                    lineNum++;
                    decimal lineNet = Math.Round(line.UnitPrice * line.Qty, 3);
                    decimal discAmt = Math.Round(lineNet * (line.DiscountPct / 100m), 3);
                    decimal afterDisc = lineNet - discAmt;
                    decimal tax = Math.Round(afterDisc * taxRate, 3);

                    using var lc = new SQLiteCommand(conn);
                    lc.Transaction = tx;
                    lc.CommandText = @"
            INSERT INTO SOInvoiceLine (
                InvoiceID, ItemNo, ItemName,
                BatchNo, SerialNo,
                Qty, UOM, UnitPrice,
                LineNetAmount, ChargesAmount, Tax,
                DiscountAmount, TotalAmount,
                StockCode, UOM_Text, ListPrice,
                CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            ) VALUES (
                @InvoiceID, 0, @ItemName,
                NULL, NULL,
                @Qty, 0, @UnitPrice,
                @LineNetAmount, 0, @Tax,
                @DiscountAmount, @TotalAmount,
                @StockCode, @UOM_Text, @ListPrice,
                0, @CreatedDate, NULL, NULL
            );";
                    lc.Parameters.AddWithValue("@InvoiceID", invoiceId);
                    lc.Parameters.AddWithValue("@ItemName", line.Name ?? "");
                    lc.Parameters.AddWithValue("@Qty", (double)line.Qty);
                    lc.Parameters.AddWithValue("@UnitPrice", (double)line.UnitPrice);
                    lc.Parameters.AddWithValue("@LineNetAmount", (double)lineNet);
                    lc.Parameters.AddWithValue("@Tax", (double)tax);
                    lc.Parameters.AddWithValue("@DiscountAmount", (double)discAmt);
                    lc.Parameters.AddWithValue("@TotalAmount", (double)line.LineTotal);
                    lc.Parameters.AddWithValue("@StockCode", line.StockCode ?? "");
                    lc.Parameters.AddWithValue("@UOM_Text", string.IsNullOrWhiteSpace(line.UOM) ? "EA" : line.UOM);
                    lc.Parameters.AddWithValue("@ListPrice", (double)(line.ListPrice > 0 ? line.ListPrice : line.UnitPrice));
                    lc.Parameters.AddWithValue("@CreatedDate", now);

                    int affected = lc.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine(
                        $"[SaveSale] Line {lineNum}: Item={line.Name} Qty={line.Qty} " +
                        $"Total={line.LineTotal} rows={affected}");
                }
            }

            // ── 4. Payments ────────────────────────────────────────────────────────
            void AddPayment(decimal amt, int methodId, string type, string desc)
            {
                if (amt <= 0) return;
                using var pc = new SQLiteCommand(conn);
                pc.Transaction = tx;
                pc.CommandText = @"
            INSERT INTO SOInvoicePayment (
                InvoiceID, PaymentDate, PaymentMethod,
                PaymentAmount, PaymentType, PaymentDesc,
                CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            ) VALUES (
                @InvoiceID, @PaymentDate, @PaymentMethod,
                @PaymentAmount, @PaymentType, @PaymentDesc,
                0, @CreatedDate, NULL, NULL
            );";
                pc.Parameters.AddWithValue("@InvoiceID", invoiceId);
                pc.Parameters.AddWithValue("@PaymentDate", today);
                pc.Parameters.AddWithValue("@PaymentMethod", methodId);
                pc.Parameters.AddWithValue("@PaymentAmount", (double)amt);
                pc.Parameters.AddWithValue("@PaymentType", type);
                pc.Parameters.AddWithValue("@PaymentDesc", desc);
                pc.Parameters.AddWithValue("@CreatedDate", now);
                pc.ExecuteNonQuery();
            }

            AddPayment(receipt.PaidCash, 1, "CASH", "CASH - CASH");
            AddPayment(receipt.PaidCard, 2, "CC", "CC - CREDIT CARD");

            string digitalType = string.IsNullOrWhiteSpace(receipt.DigitalMethodName)
                ? "DIGITAL" : receipt.DigitalMethodName.ToUpper();
            AddPayment(receipt.PaidDigital, 3, digitalType,
                $"{digitalType} - {(string.IsNullOrWhiteSpace(receipt.DigitalMethodName) ? "DIGITAL" : receipt.DigitalMethodName)}");

            // ── 5. Enqueue D365 sync ───────────────────────────────────────────────
            using (var qc = new SQLiteCommand(conn))
            {
                qc.Transaction = tx;
                qc.CommandText = @"
            INSERT INTO POS_SyncQueue (
                TransactionId, InvoiceNo, SyncType,
                SyncStatus, RetryCount,
                LastRetryDateTime, LastSyncMessage,
                D365SalesOrderId, D365InvoiceId,
                CreatedDateTime
            ) VALUES (
                @TransactionId, @InvoiceNo, 'Sales',
                'Pending', 0,
                NULL, NULL, NULL, NULL,
                @CreatedDateTime
            );";
                qc.Parameters.AddWithValue("@TransactionId", invoiceId);
                qc.Parameters.AddWithValue("@InvoiceNo", invoiceNo);
                qc.Parameters.AddWithValue("@CreatedDateTime", now);
                qc.ExecuteNonQuery();
            }

            tx.Commit();

            System.Diagnostics.Debug.WriteLine(
                $"[SaveSale] Committed. InvoiceID={invoiceId} Lines={receipt.Lines?.Count ?? 0}");

            try { TouchSyncControl("Sales", now); } catch { }

            return (int)invoiceId;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Build D365 payloads  (matches DynamicsApiClient entity models exactly)
        // ══════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Reads SOInvoiceHeader and maps every field to RetailSalesHeader.
        /// </summary>
        //public static RetailSalesHeader BuildD365Header(long invoiceId, string dataAreaId)
        //{
        //    try
        //    {
        //        using var conn = Open();
        //        using var cmd = new SQLiteCommand(
        //            "SELECT * FROM SOInvoiceHeader WHERE InvoiceID = @id;", conn);

        //        cmd.Parameters.AddWithValue("@id", invoiceId);

        //        using var r = cmd.ExecuteReader();
        //        if (!r.Read())
        //            throw new Exception($"Invoice header not found for InvoiceID: {invoiceId}");

        //        return new RetailSalesHeader
        //        {
        //            dataAreaId = dataAreaId,
        //            BISInvoiceId = GetString(r, "InvoiceNo"),
        //            CustAccount = "CSG001",                                   // Change if needed
        //            InvoiceDate = ParseDateTimeOrNull(r["InvoiceDate"])?.ToUniversalTime() ?? DateTime.UtcNow,
        //            PostingDate = ParseDateTimeOrNull(r["PostingDate"])?.ToUniversalTime() ?? DateTime.UtcNow,
        //            DueDate = ParseDateTimeOrNull(r["DueDate"])?.ToUniversalTime() ?? new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        //            RetryDateTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        //            InvoiceAmount = GetDecimal(r, "TotalInvoiceAmount"),
        //            InvoiceDescription = GetString(r, "InvoiceDescription") ?? "POS Sale",
        //            SalesStatus = "POSSales",
        //            InventLocationId = GetString(r, "InventLocationId") ?? "F1",
        //            InventSiteId = GetString(r, "InventSiteId") ?? "PVC",
        //            wMSLocationId = "Default",

        //            // Optional but good to have
        //            InvoiceAccountName = GetString(r, "InvoiceAccountName") ?? "Walk-in",
        //            SyncStatus = "Pending",
        //            StoreId = GetString(r, "StoreID") ?? "0",
        //            TerminalId = GetInt(r, "TerminalID"),
        //            CompanyId = GetString(r, "CompanyID") ?? ""
        //        };
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new Exception($"Failed to build D365 Header for InvoiceID {invoiceId}: {ex.Message}", ex);
        //    }
        //}
        private static string? GetString(SQLiteDataReader r, string column)
        {
            try
            {
                int ordinal = r.GetOrdinal(column);
                return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
            }
            catch
            {
                return null;   // Column doesn't exist or null
            }
        }

        private static decimal GetDecimal(SQLiteDataReader r, string column)
        {
            try
            {
                int ordinal = r.GetOrdinal(column);
                return r.IsDBNull(ordinal) ? 0m : r.GetDecimal(ordinal);
            }
            catch
            {
                return 0m;
            }
        }

        private static int GetInt(SQLiteDataReader r, string column)
        {
            try
            {
                int ordinal = r.GetOrdinal(column);
                return r.IsDBNull(ordinal) ? 0 : r.GetInt32(ordinal);
            }
            catch
            {
                return 0;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Sync lifecycle methods
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns all Pending / Failed queue rows eligible for (re)try.
        /// </summary> 

        /// <summary>
        /// Marks a queue row as Processing and writes a POS_SyncLog row.
        /// Call BEFORE sending to D365. Returns the new LogId.
        /// </summary>
        public static long BeginSyncAttempt(long queueId, long transactionId,
                                            string requestPayload)
        {
            using var conn = Open();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Exec(conn, @"
                UPDATE POS_SyncQueue
                   SET SyncStatus        = 'Processing',
                       LastRetryDateTime = @Now,
                       LastSyncMessage   = NULL
                 WHERE QueueId = @QueueId;",
                ("@Now", now),
                ("@QueueId", queueId));

            using var lc = new SQLiteCommand(@"
                INSERT INTO POS_SyncLog (
                    TransactionId, RequestPayload, ResponsePayload,
                    Status, LastMessage, CreatedDateTime
                ) VALUES (
                    @TransactionId, @RequestPayload, NULL,
                    'Pending', NULL, @CreatedDateTime
                );", conn);
            lc.Parameters.AddWithValue("@TransactionId", transactionId);
            lc.Parameters.AddWithValue("@RequestPayload", requestPayload);
            lc.Parameters.AddWithValue("@CreatedDateTime", now);
            lc.ExecuteNonQuery();
            return conn.LastInsertRowId;
        }


        // ══════════════════════════════════════════════════════════════════════
        //  LoadSales
        // ══════════════════════════════════════════════════════════════════════
        public static List<SaleRecord> LoadSales(DateTime from, DateTime to, int companyId)
        {
            var list = new List<SaleRecord>();
            if (!File.Exists(DbPath)) return list;

            using var conn = Open();
            const string sql = @"
                SELECT h.InvoiceID, h.InvoiceNo, h.InvoiceAccountName,
                       h.InvoiceDate, h.TotalInvoiceAmount,
                       p.PaymentType, p.PaymentDesc, p.PaymentAmount
                FROM SOInvoiceHeader h
                LEFT JOIN SOInvoicePayment p ON p.InvoiceID = h.InvoiceID
                WHERE h.CompanyID = @co
                  AND h.InvoiceDate >= @from
                  AND h.InvoiceDate <  @to
                ORDER BY h.InvoiceDate ASC, h.InvoiceID ASC;";

            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@co", companyId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal payAmt = r["PaymentAmount"] == DBNull.Value ? 0m : Convert.ToDecimal(r["PaymentAmount"]);
                string payType = r["PaymentType"]?.ToString() ?? "CASH";
                string payDesc = r["PaymentDesc"]?.ToString() ?? "CASH - CASH";

                if (r["PaymentAmount"] == DBNull.Value)
                {
                    payAmt = Convert.ToDecimal(r["TotalInvoiceAmount"]);
                    payType = "CASH";
                    payDesc = "CASH - CASH";
                }

                list.Add(new SaleRecord
                {
                    SaleID = Convert.ToInt32(r["InvoiceID"]),
                    InvoiceNo = r["InvoiceNo"]?.ToString() ?? "",
                    CustomerCode = "CSG001",
                    CustomerName = r["InvoiceAccountName"]?.ToString() ?? "Walk-in",
                    CashierName = "ADMIN",
                    InvoiceAmt = Convert.ToDecimal(r["TotalInvoiceAmount"]),
                    PaymentType = payType,
                    PaymentDesc = payDesc,
                    PaymentAmt = payAmt,
                    SaleDate = DateTime.TryParse(r["InvoiceDate"]?.ToString(), out var dt) ? dt : DateTime.Now,
                    IsReturn = false,
                    CompanyID = companyId
                });
            }
            return list;
        }

        // ── DayEnd log helpers ────────────────────────────────────────────────
        public static void LogDayEnd(DateTime reportDate, decimal totalSales, int invoiceCount)
        {
            using var conn = Open();
            Exec(conn, @"
                INSERT OR REPLACE INTO DayEndLog(ReportDate,GeneratedAt,TotalSales,InvoiceCount)
                VALUES(@rd,@ga,@ts,@ic);",
                ("@rd", reportDate.ToString("yyyy-MM-dd")),
                ("@ga", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                ("@ts", (double)totalSales),
                ("@ic", invoiceCount));
        }

        public static bool DayEndExists(DateTime date)
        {
            if (!File.Exists(DbPath)) return false;
            using var conn = Open();
            using var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM DayEndLog WHERE ReportDate=@d;", conn);
            cmd.Parameters.AddWithValue("@d", date.ToString("yyyy-MM-dd"));
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        public class ReprintInvoiceRow
        {
            public string InvoiceNo { get; set; }
            public string CustomerName { get; set; }
            public DateTime SaleDate { get; set; }
            public decimal GrandTotal { get; set; }
            public decimal PaidCash { get; set; }
            public decimal PaidDigital { get; set; }
            public decimal PaidCard { get; set; }
            public string CartJson { get; set; }
            public string CurrencySymbol { get; set; }
        }

        public static List<ReprintInvoiceRow> GetReprintInvoices(int companyId, int days = 30)
        {
            var list = new List<ReprintInvoiceRow>();
            try
            {
                string dbPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "ABC.db");
                if (!System.IO.File.Exists(dbPath)) return list;

                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();

                bool hasSOHeader = TableExists(conn, "SOInvoiceHeader");
                bool hasPayment = TableExists(conn, "SOInvoicePayment");
                bool hasPending = TableExists(conn, "PendingInvoice");

                System.Diagnostics.Debug.WriteLine(
                    $"[DEBUG] hasSOHeader={hasSOHeader} hasPayment={hasPayment} hasPending={hasPending}");

                // ══════════════════════════════════════════════════════════════
                // 1. Confirmed invoices from SOInvoiceHeader
                // ══════════════════════════════════════════════════════════════
                if (hasSOHeader)
                {
                    string paymentCols = hasPayment
                        ? @"IFNULL((SELECT SUM(PaymentAmount) FROM SOInvoicePayment p
                             WHERE p.InvoiceID = h.InvoiceID
                               AND p.PaymentType = 'CASH'), 0),
                    IFNULL((SELECT SUM(PaymentAmount) FROM SOInvoicePayment p
                             WHERE p.InvoiceID = h.InvoiceID
                               AND p.PaymentType NOT IN ('CASH','CC')), 0),
                    IFNULL((SELECT SUM(PaymentAmount) FROM SOInvoicePayment p
                             WHERE p.InvoiceID = h.InvoiceID
                               AND p.PaymentType = 'CC'), 0)"
                        : "0, 0, 0";

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $@"
SELECT
    h.InvoiceID,
    h.InvoiceNo,
    IFNULL(h.InvoiceAccountName, 'Walk-in') AS CustomerName,
    h.InvoiceDate,
    h.TotalInvoiceAmount,
    {paymentCols}
FROM SOInvoiceHeader h
WHERE h.CompanyID = @cid
  AND substr(h.InvoiceDate, 1, 10) >= substr(date('now', @days), 1, 10)
ORDER BY h.InvoiceDate DESC
LIMIT 500;";
                    cmd.Parameters.AddWithValue("@cid", companyId);
                    cmd.Parameters.AddWithValue("@days", $"-{days} days");

                    System.Diagnostics.Debug.WriteLine(
                        $"[DEBUG] SOHeader query: CompanyID={companyId} days={days}");

                    using var rdr = cmd.ExecuteReader();
                    int soCount = 0;
                    while (rdr.Read())
                    {
                        soCount++;
                        long invoiceId = rdr.GetInt64(0);
                        string invoiceNo = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                        bool isQuo = IsQuotation(invoiceNo);

                        string cartJson;
                        if (isQuo && hasPending)
                        {
                            cartJson = GetCartJsonFromPending(conn, invoiceNo);
                            if (string.IsNullOrEmpty(cartJson))
                                cartJson = BuildCartJsonFromLines(conn, invoiceId);
                        }
                        else
                        {
                            cartJson = BuildCartJsonFromLines(conn, invoiceId);
                        }

                        System.Diagnostics.Debug.WriteLine(
                            $"[DEBUG] SOHeader row: {invoiceNo} | isQuo={isQuo} | cartLen={cartJson.Length}");

                        list.Add(new ReprintInvoiceRow
                        {
                            InvoiceNo = invoiceNo,
                            CustomerName = rdr.IsDBNull(2) ? "Walk-in" : rdr.GetString(2),
                            SaleDate = rdr.IsDBNull(3) ? DateTime.Now
                                            : DateTime.TryParse(
                                                rdr.GetString(3).Replace('.', ':'),
                                                out var dt) ? dt : DateTime.Now,
                            GrandTotal = rdr.IsDBNull(4) ? 0m : Convert.ToDecimal(rdr.GetValue(4)),
                            PaidCash = rdr.IsDBNull(5) ? 0m : Convert.ToDecimal(rdr.GetValue(5)),
                            PaidDigital = rdr.IsDBNull(6) ? 0m : Convert.ToDecimal(rdr.GetValue(6)),
                            PaidCard = rdr.IsDBNull(7) ? 0m : Convert.ToDecimal(rdr.GetValue(7)),
                            CartJson = cartJson,
                            CurrencySymbol = "P"
                        });
                    }
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] SOHeader rows loaded: {soCount}");
                }

                // ══════════════════════════════════════════════════════════════
                // 2. Quotations / unpaid from PendingInvoice
                // ══════════════════════════════════════════════════════════════
                if (hasPending)
                {
                    using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = @"
SELECT
    InvoiceNo,
    IFNULL(CustomerName, 'Walk-in'),
    SaleDate,
    GrandTotal,
    CartJson
FROM PendingInvoice
WHERE (CompanyID = @cid OR CompanyID IS NULL)
  AND substr(SaleDate, 1, 10) >= substr(date('now', @days), 1, 10)
ORDER BY SaleDate DESC
LIMIT 500;";
                    cmd2.Parameters.AddWithValue("@cid", companyId);
                    cmd2.Parameters.AddWithValue("@days", $"-{days} days");

                    using var rdr2 = cmd2.ExecuteReader();
                    int pendCount = 0;
                    while (rdr2.Read())
                    {
                        string invoiceNo = rdr2.IsDBNull(0) ? "" : rdr2.GetString(0);

                        // ── Skip if already loaded from SOInvoiceHeader ───────
                        if (list.Any(r => r.InvoiceNo == invoiceNo))
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[DEBUG] Skipping duplicate from Pending: {invoiceNo}");
                            continue;
                        }

                        pendCount++;

                        string rawCart = rdr2.IsDBNull(4) ? "" : rdr2.GetString(4);
                        System.Diagnostics.Debug.WriteLine($"[RAW CartJson] {invoiceNo}: {rawCart}");

                        // Enrich CartJson — maps Price→UnitPrice, OriginalPrice→ListPrice,
                        // and fills StockCode from SOInvoiceLine
                        string cartJson = GetCartJsonFromPending(conn, invoiceNo);
                        if (string.IsNullOrEmpty(cartJson)) cartJson = rawCart;

                        System.Diagnostics.Debug.WriteLine(
                            $"[DEBUG] Pending row: {invoiceNo} | cartLen={cartJson.Length}");

                        list.Add(new ReprintInvoiceRow
                        {
                            InvoiceNo = invoiceNo,
                            CustomerName = rdr2.IsDBNull(1) ? "Walk-in" : rdr2.GetString(1),
                            SaleDate = rdr2.IsDBNull(2) ? DateTime.Now
                                            : DateTime.TryParse(
                                                rdr2.GetString(2).Replace('.', ':'),
                                                out var dt2) ? dt2 : DateTime.Now,
                            GrandTotal = rdr2.IsDBNull(3) ? 0m : Convert.ToDecimal(rdr2.GetValue(3)),
                            PaidCash = 0m,
                            PaidDigital = 0m,
                            PaidCard = 0m,
                            CartJson = cartJson,
                            CurrencySymbol = "P"
                        });
                    }
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Pending rows loaded: {pendCount}");
                }

                list = list.OrderByDescending(r => r.SaleDate).ToList();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Total rows returned: {list.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetReprintInvoices ERROR: " + ex.Message);
            }
            return list;
        }

        // ── Build CartJson from SOInvoiceLines table ───────────────────────────────
        private static string BuildCartJsonFromSOLines(SQLiteConnection conn, long invoiceId)
        {
            try
            {
                // First check what columns SOInvoiceLines has
                var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var pc = conn.CreateCommand())
                {
                    pc.CommandText = "PRAGMA table_info(SOInvoiceLines);";
                    using var pr = pc.ExecuteReader();
                    while (pr.Read()) cols.Add(pr.GetString(1));
                }

                if (cols.Count == 0) return "";

                // Build SELECT based on available columns
                string stockCode = cols.Contains("StockCode") ? "StockCode" : "''";
                string name = cols.Contains("ItemName") ? "ItemName"
                                  : cols.Contains("ProductName") ? "ProductName"
                                  : cols.Contains("Description") ? "Description" : "''";
                string uom = cols.Contains("UOM") ? "UOM" : "'EA'";
                string qty = cols.Contains("Qty") ? "Qty"
                                  : cols.Contains("Quantity") ? "Quantity" : "1";
                string unitPrice = cols.Contains("UnitPrice") ? "UnitPrice"
                                  : cols.Contains("SalePrice") ? "SalePrice" : "0";
                string listPrice = cols.Contains("ListPrice") ? "ListPrice"
                                  : cols.Contains("UnitPrice") ? "UnitPrice" : "0";
                string discPct = cols.Contains("DiscountPct") ? "DiscountPct"
                                  : cols.Contains("Discount") ? "Discount" : "0";
                string lineTotal = cols.Contains("LineTotal") ? "LineTotal"
                                  : cols.Contains("TotalAmount") ? "TotalAmount" : "0";
                string qtyReq = cols.Contains("QtyRequested") ? "QtyRequested" : qty;
                string qtyDis = cols.Contains("QtyDispatched") ? "QtyDispatched" : qty;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
            SELECT
                {stockCode}  AS StockCode,
                {name}       AS Name,
                {uom}        AS UOM,
                {qty}        AS Qty,
                {unitPrice}  AS UnitPrice,
                {listPrice}  AS ListPrice,
                {discPct}    AS DiscountPct,
                {lineTotal}  AS LineTotal,
                {qtyReq}     AS QtyRequested,
                {qtyDis}     AS QtyDispatched
            FROM SOInvoiceLines
            WHERE InvoiceID = @id;";
                cmd.Parameters.AddWithValue("@id", invoiceId);

                var lines = new System.Text.StringBuilder("[");
                bool first = true;
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    if (!first) lines.Append(",");
                    first = false;

                    string sc = rdr.IsDBNull(0) ? "" : Convert.ToString(rdr.GetValue(0)) ?? "";
                    string nm = rdr.IsDBNull(1) ? "" : Convert.ToString(rdr.GetValue(1)) ?? "";
                    string u = rdr.IsDBNull(2) ? "EA" : Convert.ToString(rdr.GetValue(2)) ?? "EA";
                    decimal q = rdr.IsDBNull(3) ? 1m : Convert.ToDecimal(rdr.GetValue(3));
                    decimal up = rdr.IsDBNull(4) ? 0m : Convert.ToDecimal(rdr.GetValue(4));
                    decimal lp = rdr.IsDBNull(5) ? 0m : Convert.ToDecimal(rdr.GetValue(5));
                    decimal dp = rdr.IsDBNull(6) ? 0m : Convert.ToDecimal(rdr.GetValue(6));
                    decimal lt = rdr.IsDBNull(7) ? 0m : Convert.ToDecimal(rdr.GetValue(7));
                    decimal qr = rdr.IsDBNull(8) ? q : Convert.ToDecimal(rdr.GetValue(8));
                    decimal qd = rdr.IsDBNull(9) ? q : Convert.ToDecimal(rdr.GetValue(9));

                    // Escape name/stockcode for JSON safety
                    nm = nm.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sc = sc.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    u = u.Replace("\\", "\\\\").Replace("\"", "\\\"");

                    lines.Append($@"{{
                ""StockCode"":""{sc}"",
                ""Name"":""{nm}"",
                ""UOM"":""{u}"",
                ""Qty"":{q},
                ""UnitPrice"":{up},
                ""ListPrice"":{lp},
                ""DiscountPct"":{dp},
                ""LineTotal"":{lt},
                ""QtyRequested"":{qr},
                ""QtyDispatched"":{qd}
            }}");
                }
                lines.Append("]");
                return first ? "" : lines.ToString(); // return "" if no lines found
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BuildCartJsonFromSOLines: " + ex.Message);
                return "";
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private static bool TableExists(SQLiteConnection conn, string tableName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@t;";
            cmd.Parameters.AddWithValue("@t", tableName);
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }

        private static string GetCartJsonFromPending(SQLiteConnection conn, string invoiceNo)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT CartJson FROM PendingInvoice WHERE InvoiceNo = @inv LIMIT 1;";
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                var result = cmd.ExecuteScalar();
                string cartJson = result == null || result == DBNull.Value
                    ? "" : result.ToString() ?? "";

                if (string.IsNullOrEmpty(cartJson)) return "";

                System.Diagnostics.Debug.WriteLine($"[GetCartJson] Raw for {invoiceNo}: {cartJson}");

                // ── Load StockCode lookup from SOInvoiceLine ──────────────────
                var stockLookup = new Dictionary<string, (string StockCode, string UOM, decimal ListPrice)>(
                    StringComparer.OrdinalIgnoreCase);

                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = @"
SELECT l.ItemName, l.StockCode, l.UOM_Text, l.ListPrice
FROM SOInvoiceLine l
INNER JOIN SOInvoiceHeader h ON h.InvoiceID = l.InvoiceID
WHERE h.InvoiceNo = @inv;";
                    cmd2.Parameters.AddWithValue("@inv", invoiceNo);
                    using var rdr = cmd2.ExecuteReader();
                    while (rdr.Read())
                    {
                        string itemName = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                        string stockCode = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                        string uom = rdr.IsDBNull(2) ? "EA" : rdr.GetString(2);
                        decimal listPrice = rdr.IsDBNull(3) ? 0m : Convert.ToDecimal(rdr.GetValue(3));
                        if (!string.IsNullOrEmpty(itemName) && !stockLookup.ContainsKey(itemName))
                            stockLookup[itemName] = (stockCode, uom, listPrice);
                    }
                }

                // ── Parse CartJson tolerantly using JsonDocument ──────────────
                // Works regardless of Newtonsoft vs STJ, camelCase vs PascalCase
                var enriched = new List<object>();
                using var doc = JsonDocument.Parse(cartJson);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string GetStr(string[] keys)
                    {
                        foreach (var k in keys)
                            if (item.TryGetProperty(k, out var v)) return v.GetString() ?? "";
                        return "";
                    }
                    decimal GetDec(string[] keys)
                    {
                        foreach (var k in keys)
                            if (item.TryGetProperty(k, out var v))
                                return v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
                        return 0m;
                    }

                    // Try both PascalCase and camelCase for every field
                    // Replace these lines in GetCartJsonFromPending:

                    string name = GetStr(new[] { "Name", "name" });
                    string stockCode = GetStr(new[] { "StockCode",    "stockCode",  "stock_code",
                                  "Barcode",       "barcode"                           });
                    string uom = GetStr(new[] { "UOM", "uom", "Uom" });
                    decimal qty = GetDec(new[] { "Qty", "qty", "quantity" });
                    decimal unitPrice = GetDec(new[] { "UnitPrice", "unitPrice", "unit_price",
                                   "Price",     "price"                    });
                    decimal listPrice = GetDec(new[] { "ListPrice", "listPrice", "list_price",
                                   "OriginalPrice", "originalPrice"        });
                    // ← OriginalPrice
                    decimal discPct = GetDec(new[] { "DiscountPct", "discountPct", "discount_pct" });
                    decimal lineTotal = GetDec(new[] { "LineTotal", "lineTotal", "line_total" });
                    decimal qtyReq = GetDec(new[] { "QtyRequested", "qtyRequested" });
                    decimal qtyDsp = GetDec(new[] { "QtyDispatched", "qtyDispatched" });

                    // After all GetDec calls, add:
                    if (lineTotal <= 0 && unitPrice > 0 && qty > 0)
                        lineTotal = Math.Round(unitPrice * qty * (1m - discPct / 100m), 2);

                    if (qtyReq <= 0) qtyReq = qty;
                    if (qtyDsp <= 0) qtyDsp = qty;

                    // Enrich from SOInvoiceLine if available
                    if (stockLookup.TryGetValue(name, out var found))
                    {
                        if (string.IsNullOrEmpty(stockCode)) stockCode = found.StockCode;
                        if (string.IsNullOrEmpty(uom) || uom == "EA") uom = found.UOM;
                        if (listPrice <= 0) listPrice = found.ListPrice;
                    }

                    if (listPrice <= 0) listPrice = unitPrice;

                    System.Diagnostics.Debug.WriteLine(
                        $"[GetCartJson] Line: {name} | Stock={stockCode} | " +
                        $"Unit={unitPrice} | List={listPrice} | Total={lineTotal} | Disc={discPct}%");

                    enriched.Add(new
                    {
                        StockCode = stockCode,
                        Name = name,
                        UOM = string.IsNullOrEmpty(uom) ? "EA" : uom,
                        Qty = qty,
                        QtyRequested = qtyReq,
                        QtyDispatched = qtyDsp,
                        UnitPrice = unitPrice,
                        ListPrice = listPrice,
                        DiscountPct = discPct,
                        LineTotal = lineTotal
                    });
                }

                return System.Text.Json.JsonSerializer.Serialize(enriched);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCartJsonFromPending ERROR ({invoiceNo}): {ex.Message}");
                return "";
            }
        }

        private static string EnhanceCartJsonWithStockCodes(string cartJson, string stockCodes)
        {
            try
            {
                if (string.IsNullOrEmpty(cartJson)) return cartJson;

                var cart = JsonConvert.DeserializeObject<dynamic>(cartJson);
                var codes = stockCodes?.Split('|') ?? new string[] { };

                if (cart?["items"] != null)
                {
                    for (int i = 0; i < cart["items"].Count; i++)
                    {
                        if (i < codes.Length)
                        {
                            cart["items"][i]["StockCode"] = codes[i];
                        }
                    }
                }

                return JsonConvert.SerializeObject(cart);
            }
            catch { return cartJson; }
        }
        public static string BuildCartJsonFromLines(SQLiteConnection conn, long invoiceId)
        {
            var items = new List<object>();
            try
            {
                // ── DEBUG: confirm we enter and what invoiceId is ─────────────
                System.Diagnostics.Debug.WriteLine($"[CartJson] Building for InvoiceID={invoiceId}");

                // ── DEBUG: check if SOInvoiceLine has rows for this invoice ───
                using (var chk = conn.CreateCommand())
                {
                    chk.CommandText = "SELECT COUNT(*) FROM SOInvoiceLine WHERE InvoiceID = @id;";
                    chk.Parameters.AddWithValue("@id", invoiceId);
                    long cnt = (long)(chk.ExecuteScalar() ?? 0L);
                    System.Diagnostics.Debug.WriteLine($"[CartJson] SOInvoiceLine rows for InvoiceID={invoiceId}: {cnt}");
                }

                // ── DEBUG: dump raw column values ─────────────────────────────
                using (var dbg = conn.CreateCommand())
                {
                    dbg.CommandText = @"
SELECT InvoiceLineID, ItemName, StockCode, Qty, UnitPrice, 
       ListPrice, LineNetAmount, DiscountAmount, TotalAmount, UOM_Text
FROM SOInvoiceLine WHERE InvoiceID = @id;";
                    dbg.Parameters.AddWithValue("@id", invoiceId);
                    using var dr = dbg.ExecuteReader();
                    while (dr.Read())
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[CartJson] Line: ID={dr[0]} Name={dr[1]} Code={dr[2]} " +
                            $"Qty={dr[3]} Unit={dr[4]} List={dr[5]} " +
                            $"Net={dr[6]} Disc={dr[7]} Total={dr[8]} UOM={dr[9]}");
                    }
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT ItemName, Qty, UnitPrice, LineNetAmount, DiscountAmount, TotalAmount,
       StockCode, UOM_Text, ListPrice
FROM SOInvoiceLine
WHERE InvoiceID = @id
ORDER BY InvoiceLineID;";
                cmd.Parameters.AddWithValue("@id", invoiceId);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string itemName = r.IsDBNull(0) ? "" : r.GetString(0);
                    decimal qty = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
                    decimal unitPrice = r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2));
                    decimal lineNet = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3));
                    decimal discAmt = r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetValue(4));
                    decimal totalAmt = r.IsDBNull(5) ? 0m : Convert.ToDecimal(r.GetValue(5));
                    string stockCode = r.IsDBNull(6) ? "" : r.GetString(6);
                    string uom = r.IsDBNull(7) ? "EA" : r.GetString(7);
                    decimal listPrice = r.IsDBNull(8) ? 0m : Convert.ToDecimal(r.GetValue(8));

                    decimal preDiscount = lineNet + discAmt;
                    decimal discPct = preDiscount > 0
                        ? Math.Round((discAmt / preDiscount) * 100m, 2)
                        : 0m;

                    if (listPrice <= 0) listPrice = unitPrice;
                    if (totalAmt <= 0) totalAmt = lineNet;

                    System.Diagnostics.Debug.WriteLine(
                        $"[CartJson] Mapped: {itemName} | Stock={stockCode} | " +
                        $"Unit={unitPrice} | List={listPrice} | Total={totalAmt} | Disc={discPct}%");

                    items.Add(new
                    {
                        StockCode = stockCode,
                        Name = itemName,
                        UOM = uom,
                        Qty = qty,
                        QtyRequested = qty,
                        QtyDispatched = qty,
                        UnitPrice = unitPrice,
                        ListPrice = listPrice,
                        DiscountPct = discPct,
                        LineTotal = totalAmt
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BuildCartJsonFromLines ERROR: " + ex.Message);
            }

            string json = System.Text.Json.JsonSerializer.Serialize(items);
            System.Diagnostics.Debug.WriteLine($"[CartJson] Final JSON: {json}");
            return json;
        }
        // ══════════════════════════════════════════════════════════════════════
        //  Private helpers
        // ══════════════════════════════════════════════════════════════════════
        private static void TouchSyncControl(string syncType, string dateTime)
        {
            using var conn = Open();
            Exec(conn, @"
                INSERT INTO POS_SyncControl (SyncType, LastSyncDateTime)
                VALUES (@SyncType, @LastSyncDateTime)
                ON CONFLICT(SyncType) DO UPDATE
                   SET LastSyncDateTime = excluded.LastSyncDateTime;",
                ("@SyncType", syncType),
                ("@LastSyncDateTime", dateTime));
        }

        private static DateTime? ParseDateTimeOrNull(object? value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (DateTime.TryParse(value.ToString(), out var dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

            return null;
        }

        private static SQLiteConnection Open()
        {
            var c = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            c.Open();
            using var p = new SQLiteCommand("PRAGMA journal_mode=WAL;", c);
            p.ExecuteNonQuery();
            return c;
        }

        private static void Exec(SQLiteConnection c, string sql,
            params (string key, object? val)[] parms)
        {
            using var cmd = new SQLiteCommand(sql, c);
            foreach (var (key, val) in parms)
                cmd.Parameters.AddWithValue(key, val ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        //public static List<RetailSalesLine> BuildD365Lines(long invoiceId)
        //{
        //    var lines = new List<RetailSalesLine>();

        //    using var conn = Open();

        //    // Separate command for COUNT — never reuse a command after ExecuteScalar()
        //    using (var countCmd = new SQLiteCommand(
        //        "SELECT COUNT(*) FROM SOInvoiceLine WHERE InvoiceID=@id;", conn))
        //    {
        //        countCmd.Parameters.AddWithValue("@id", invoiceId);
        //        int totalLines = Convert.ToInt32(countCmd.ExecuteScalar());
        //        Console.WriteLine($"[DEBUG] Total lines in DB for InvoiceID {invoiceId}: {totalLines}");
        //    }

        //    // Separate command for the actual SELECT
        //    using var cmd = new SQLiteCommand(
        //        "SELECT * FROM SOInvoiceLine WHERE InvoiceID=@id ORDER BY InvoiceLineID;", conn);
        //    cmd.Parameters.AddWithValue("@id", invoiceId);

        //    using var r = cmd.ExecuteReader();
        //    int rowCount = 0;
        //    while (r.Read())
        //    {
        //        rowCount++;
        //        var stockCode = r["StockCode"]?.ToString();
        //        if (string.IsNullOrWhiteSpace(stockCode))
        //            stockCode = r["ItemName"]?.ToString() ?? ""; // fallback for old rows

        //        var line = new RetailSalesLine
        //        {
        //            ItemId = stockCode,
        //            SalesQty = Convert.ToDecimal(r["Qty"]),
        //            PriceUnit = Convert.ToDecimal(r["UnitPrice"]),
        //            LineAmount = Convert.ToDecimal(r["LineNetAmount"]),
        //            TotalAmount = Convert.ToDecimal(r["TotalAmount"]),
        //            DiscountAmount = Convert.ToDecimal(r["DiscountAmount"]),
        //            ChargesAmount = Convert.ToDecimal(r["ChargesAmount"]),
        //            TaxAmount = Convert.ToDecimal(r["Tax"]),
        //            inventBatchId = r["BatchNo"]?.ToString() ?? "",
        //            inventSerialId = r["SerialNo"]?.ToString() ?? ""
        //        };
        //        lines.Add(line);
        //        Console.WriteLine($"[DEBUG] Line {rowCount}: Item={line.ItemId}, Qty={line.SalesQty}");
        //    }

        //    Console.WriteLine($"[DEBUG] Total lines built: {lines.Count}");
        //    return lines;
        //}
        /// <summary>
        /// Returns true if PosApiResponse already has a successful row for this
        /// invoice and type (e.g. "Header", "Line_1"). Prevents duplicate POSTs on retry.
        /// </summary>
        public static bool HasSuccessfulApiResponse(string invoiceId, string invoiceType)
        {
            try
            {
                using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                conn.Open();
                using var cmd = new SQLiteCommand(@"
            SELECT COUNT(*) FROM PosApiResponse
            WHERE InvoiceId   = @InvoiceId
              AND InvoiceType = @InvoiceType
              AND SyncStatus  = 'Success'
            LIMIT 1;", conn);
                cmd.Parameters.AddWithValue("@InvoiceId", invoiceId ?? "");
                cmd.Parameters.AddWithValue("@InvoiceType", invoiceType ?? "");
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
            catch
            {
                return false; // safe default: try the POST rather than silently skip
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DayEndReportForm  —  A4-sheet styled, multi-page
    // ══════════════════════════════════════════════════════════════════════════
    public class DayEndReportForm : Form
    {
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);

        private readonly List<SaleRecord> _sales;
        private readonly List<SalesRepository.PendingInvoiceRow> _pending;
        private readonly List<SalesRepository.DeletedInvoiceRow> _deleted;
        private readonly DateTime _reportDate;
        private readonly string _company;
        private readonly string _currency;
        private readonly int _companyId;

        private WebBrowser _browser;

        public DayEndReportForm(
            DateTime reportDate, int companyId,
            string company = "ABC",
            string currency = "BWP")
        {
            _reportDate = reportDate;
            _companyId = companyId;
            _company = company;
            _currency = currency;

            SalesRepository.EnsureAllTables();
            SalesRepository.MigrateSyncQueueColumns();
            SalesRepository.MigrateInvoiceLineColumns();

            _sales = SalesRepository.LoadSales(reportDate.Date, reportDate.Date.AddDays(1), companyId);
            _pending = SalesRepository.GetPendingInvoices(companyId);
            _deleted = SalesRepository.GetDeletedInvoices(companyId, reportDate.Date, reportDate.Date.AddDays(1));

            InitUI();
        }

        private void InitUI()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ClientSize = new Size(1150, 820);
            KeyPreview = true;
            Text = "Day End Report";

            var pHead = new Panel { BackColor = PanelDark, Dock = DockStyle.Top, Height = 52 };

            pHead.Controls.Add(new Label
            {
                Text = $"📊  Day End Report  —  {_reportDate:dd/MM/yyyy}",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.Orange,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(640, 52),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var btnPrint = MakeBtn("🖨  Print / PDF", AccGreen, new Point(660, 8), new Size(140, 36));
            var btnCsv = MakeBtn("📤  Export CSV", AccBlue, new Point(810, 8), new Size(140, 36));
            var btnX = MakeBtn("✕", Color.FromArgb(55, 60, 78), new Point(1102, 0), new Size(48, 52));

            btnPrint.Click += BtnPrint_Click;
            btnCsv.Click += BtnCsv_Click;
            btnX.Click += (s, e) => Close();

            pHead.Controls.AddRange(new Control[] { btnPrint, btnCsv, btnX });
            Controls.Add(pHead);

            var pDate = new Panel { BackColor = Color.FromArgb(28, 31, 40), Dock = DockStyle.Top, Height = 40 };

            pDate.Controls.Add(new Label
            {
                Text = "Report Date:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(16, 11)
            });

            var dtPicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = _reportDate,
                Size = new Size(130, 24),
                Location = new Point(140, 8),
                Font = new Font("Segoe UI", 9F)
            };
            pDate.Controls.Add(dtPicker);

            var btnRefresh = MakeBtn("↻  Refresh", AccBlue, new Point(282, 7), new Size(90, 26));
            btnRefresh.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            btnRefresh.Click += (s, e) =>
            {
                new DayEndReportForm(dtPicker.Value, _companyId, _company, _currency).Show(Owner);
                Close();
            };
            pDate.Controls.Add(btnRefresh);

            pDate.Controls.Add(new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(386, 11),
                Text = BuildSummaryStrip()
            });

            Controls.Add(pDate);
            pDate.BringToFront();

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
            _browser.DocumentText = BuildA4Html();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private string LogoSvg()
        {
            string[] candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "flo.png"),
                Path.Combine(Application.StartupPath,               "flo.png"),
                Path.Combine(Directory.GetCurrentDirectory(),        "flo.png")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    string b64 = Convert.ToBase64String(File.ReadAllBytes(path));
                    return $"<img src='data:image/png;base64,{b64}' " +
                           "width='72' height='72' " +
                           "style='border-radius:12px;object-fit:contain;display:block;' " +
                           "alt='Logo' />";
                }
                catch { }
            }

            return @"<svg xmlns='http://www.w3.org/2000/svg' width='72' height='72' viewBox='0 0 72 72'>
              <rect width='72' height='72' rx='12' fill='#1a56db'/>
              <text x='36' y='28' font-family=""'Segoe UI',Arial,sans-serif"" font-size='11'
                    font-weight='800' fill='#ffffff' text-anchor='middle' dominant-baseline='middle'>SHRI</text>
              <text x='36' y='44' font-family=""'Segoe UI',Arial,sans-serif"" font-size='9'
                    font-weight='600' fill='#93c5fd' text-anchor='middle' dominant-baseline='middle'>POS</text>
              <text x='36' y='58' font-family=""'Segoe UI',Arial,sans-serif"" font-size='7'
                    font-weight='400' fill='#bfdbfe' text-anchor='middle' dominant-baseline='middle'>RETAIL</text>
            </svg>";
        }

        private string SharedCss() => @"
  @import url('https://fonts.googleapis.com/css2?family=DM+Sans:wght@400;500;600;700&family=DM+Mono:wght@400;500&display=swap');
  * { box-sizing:border-box;margin:0;padding:0;
      -webkit-print-color-adjust:exact !important;print-color-adjust:exact !important; }
  html,body { background:#0f1117;font-family:'DM Sans',sans-serif;font-size:11px;color:#1a1a2e; }
  .a4 { width:794px;min-height:1123px;background:#ffffff;margin:28px auto 0;
    padding:40px 48px 48px;box-shadow:0 0 0 1px rgba(0,0,0,.08),0 20px 60px rgba(0,0,0,.5);
    border-radius:4px; }
  .a4+.a4 { margin-top:32px; }
  @media print {
    html,body { background:white !important; }
    * { -webkit-print-color-adjust:exact !important;print-color-adjust:exact !important; }
    .a4 { margin:0 !important;box-shadow:none !important;border-radius:0 !important;page-break-after:always; }
    .a4:last-child { page-break-after:avoid; }
  }
  .doc-header { display:flex;justify-content:space-between;align-items:flex-start;
    margin-bottom:24px;padding-bottom:16px;border-bottom:1.5px solid #e5e7eb; }
  .logo-wrap { display:flex;align-items:center;gap:12px; }
  .brand-name { font-size:20px;font-weight:700;color:#111827;letter-spacing:-.4px; }
  .brand-sub { font-size:10px;color:#6b7280;margin-top:2px;letter-spacing:.2px; }
  .doc-meta { text-align:right;font-size:10px;color:#6b7280;line-height:2; }
  .doc-meta .big { font-size:14px;font-weight:700;color:#111827;letter-spacing:-.2px; }
  .report-title { margin-bottom:20px; }
  .report-title h2 { font-size:12px;font-weight:700;color:#374151;
    text-transform:uppercase;letter-spacing:.8px;margin-bottom:2px; }
  .report-title p { font-size:10px;color:#9ca3af; }
  .summary-row { display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:20px; }
  .summary-panel { border:1px solid #e5e7eb;border-radius:8px;overflow:hidden; }
  .summary-panel table { width:100%;border-collapse:collapse;margin-bottom:0; }
  .summary-panel tbody td { padding:5px 8px;font-size:10px;border-bottom:1px solid #f3f4f6; }
  .summary-panel tbody td.num { text-align:right;font-family:'DM Mono',monospace; }
  .summary-panel tfoot td { padding:6px 8px;font-size:10px;font-weight:700;
    border-top:1.5px solid #d1d5db;font-family:'DM Mono',monospace; }
  .summary-panel tfoot td.num { text-align:right; }
  .section-head { font-size:9px;font-weight:700;text-transform:uppercase;letter-spacing:.8px;
    color:#9ca3af;margin:0 0 8px;padding-bottom:6px;border-bottom:1px solid #f3f4f6; }
  table { width:100%;border-collapse:collapse;font-size:10.5px;margin-bottom:22px; }
  td { padding:5px 8px;vertical-align:middle; }
  td.num { text-align:right;font-family:'DM Mono',monospace;font-size:10px; }
  tbody tr { border-bottom:1px solid #f9fafb; }
  tr.row-first td { padding:6px 8px 1px;border-bottom:none; }
  tr.row-extra td { padding:0 8px 1px;border-bottom:none;color:#6b7280;font-size:10px; }
  tr.row-cc td { padding:0 8px 8px;border-bottom:1px solid #f3f4f6;
    color:#9ca3af;font-size:9px;font-style:italic; }
  .alert-grid { display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:20px; }
  td.empty { text-align:center;color:#9ca3af;padding:24px;font-style:italic;font-size:11px; }
  .doc-footer { margin-top:28px;padding-top:12px;border-top:1px solid #e5e7eb;
    display:flex;justify-content:space-between;font-size:9px;color:#9ca3af; }
  .doc-footer strong { color:#6b7280; }
";

        private string PageHeader(string reportTitle, string reportSubtitle) => $@"
  <div class='doc-header'>
    <div class='logo-wrap'>
      {LogoSvg()}
      <div>
        <div class='brand-name'>{He(_company)}</div>
        <div class='brand-sub'>Cash Drawer 01 &mdash; Counter Sales &mdash; {He(_currency)} Operations</div>
      </div>
    </div>
    <div class='doc-meta'>
      <div class='big'>{He(reportTitle)}</div>
      <div>Date : {_reportDate:dd/MM/yyyy}</div>
      <div>Generated : {DateTime.Now:dd/MM/yyyy HH:mm}</div>
      <div>Printed by : {He(Environment.UserName.ToUpper())}</div>
    </div>
  </div>
  <div class='report-title'>
    <h2>{He(reportTitle)} &mdash; {_reportDate:dd MMMM yyyy}</h2>
    <p>{He(reportSubtitle)}</p>
  </div>";

        private string PageFooter() => $@"
  <div class='doc-footer'>
    <span>Printed by : <strong>{He(Environment.UserName.ToUpper())}</strong></span>
    <span>{He(_company)} &mdash; Day End Report &nbsp;|&nbsp; Version 1.0</span>
    <span>{DateTime.Now:dd/MM/yyyy HH:mm:ss}</span>
  </div>";

        private string BuildA4Html() => $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>{SharedCss()}</style>
</head>
<body>
  {BuildPage1Body()}
  {BuildPage2Body()}
  {BuildPage3Body()}
</body>
</html>";

        private string BuildPage1Body()
        {
            var normalSales = _sales.Where(x => !x.IsReturn).ToList();

            decimal totalGross = normalSales.Sum(x => x.PaymentAmt);
            decimal cashTot = normalSales.Where(x => x.PaymentType == "CASH").Sum(x => x.PaymentAmt);
            decimal cardTot = normalSales.Where(x => x.PaymentType == "CC").Sum(x => x.PaymentAmt);
            decimal digitalTot = normalSales.Where(x => x.PaymentType != "CASH" && x.PaymentType != "CC").Sum(x => x.PaymentAmt);

            int cashCnt = normalSales.Where(x => x.PaymentType == "CASH").Select(x => x.SaleID).Distinct().Count();
            int cardCnt = normalSales.Where(x => x.PaymentType == "CC").Select(x => x.SaleID).Distinct().Count();
            int digCnt = normalSales.Where(x => x.PaymentType != "CASH" && x.PaymentType != "CC").Select(x => x.SaleID).Distinct().Count();
            int totalTxns = cashCnt + cardCnt + digCnt;

            var grouped = normalSales.GroupBy(x => x.SaleID).ToList();
            var salesRows = new StringBuilder();
            foreach (var grp in grouped)
            {
                decimal invoiceTotal = grp.First().InvoiceAmt;
                bool isFirst = true;
                foreach (var s in grp)
                {
                    salesRows.Append($@"
      <tr class='{(isFirst ? "row-first" : "row-extra")}'>
        <td>CSG001</td>
        <td>{He(s.InvoiceNo)}</td>
        <td class='num'>{_currency}&nbsp;{s.InvoiceAmt:N2}</td>
        <td>{He(s.PaymentDesc)}</td>
        <td class='num'>{_currency}&nbsp;{s.PaymentAmt:N2}</td>
        <td>{He(s.CashierName)}</td>
        <td class='num'>{s.SaleDate:HH:mm}</td>
        <td class='num'>{(isFirst ? $"{_currency}&nbsp;{invoiceTotal:N2}" : "")}</td>
      </tr>");
                    isFirst = false;
                }
                salesRows.Append(@"
      <tr class='row-cc'>
        <td>Credit card:</td><td>Start date :</td>
        <td colspan='2'>Expiry date :</td>
        <td colspan='4'>Authorization :</td>
      </tr>");
            }

            if (!normalSales.Any())
                salesRows.Append("<tr><td colspan='8' class='empty'>No transactions recorded for this date.</td></tr>");

            salesRows.Append($@"
      <tr>
        <td colspan='2' style='background:#f0f9ff;font-weight:700;padding:7px 8px;border-top:2px solid #111827;font-size:11px;font-family:monospace;'><strong>Grand Total</strong></td>
        <td class='num' style='background:#f0f9ff;font-weight:700;padding:7px 8px;border-top:2px solid #111827;font-size:11px;font-family:monospace;'><strong>{_currency}&nbsp;{totalGross:N2}</strong></td>
        <td style='background:#f0f9ff;border-top:2px solid #111827;'></td>
        <td class='num' style='background:#f0f9ff;font-weight:700;padding:7px 8px;border-top:2px solid #111827;font-size:11px;font-family:monospace;'><strong>{_currency}&nbsp;{totalGross:N2}</strong></td>
        <td style='background:#f0f9ff;border-top:2px solid #111827;'></td>
        <td style='background:#f0f9ff;border-top:2px solid #111827;'></td>
        <td class='num' style='background:#f0f9ff;font-weight:700;padding:7px 8px;border-top:2px solid #111827;font-size:11px;font-family:monospace;'><strong>{_currency}&nbsp;{totalGross:N2}</strong></td>
      </tr>");

            return $@"
<div class='a4'>
  {PageHeader("Day End Report", $"{He(_company)} — Version 1.0")}
  <div class='summary-row'>
    <div class='summary-panel'>
      <div style='background:#111827;color:#e5e7eb;font-size:9px;font-weight:700;text-transform:uppercase;letter-spacing:.7px;padding:7px 10px;'>&#9632;&nbsp; Sales Amounts</div>
      <table>
        <thead><tr>
          <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Method</th>
          <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Amount ({_currency})</th>
        </tr></thead>
        <tbody>
          <tr><td><span style='background:#dcfce7;color:#166534;font-size:8px;font-weight:700;padding:1px 6px;border-radius:20px;'>CASH</span>&nbsp; Cash</td><td class='num'>{cashTot:N2}</td></tr>
          <tr><td><span style='background:#dbeafe;color:#1e40af;font-size:8px;font-weight:700;padding:1px 6px;border-radius:20px;'>CC</span>&nbsp; Card</td><td class='num'>{cardTot:N2}</td></tr>
          <tr><td><span style='background:#fef3c7;color:#92400e;font-size:8px;font-weight:700;padding:1px 6px;border-radius:20px;'>BANK</span>&nbsp; Bank / Digital</td><td class='num'>{digitalTot:N2}</td></tr>
        </tbody>
        <tfoot><tr>
          <td style='background:#f9fafb;padding:6px 8px;font-weight:700;border-top:1.5px solid #d1d5db;'><strong>Total</strong></td>
          <td class='num' style='background:#f9fafb;padding:6px 8px;font-weight:700;border-top:1.5px solid #d1d5db;'><strong>{totalGross:N2}</strong></td>
        </tr></tfoot>
      </table>
    </div>
    <div class='summary-panel'>
      <div style='background:#111827;color:#e5e7eb;font-size:9px;font-weight:700;text-transform:uppercase;letter-spacing:.7px;padding:7px 10px;'>&#9632;&nbsp; Transactions</div>
      <table>
        <thead><tr>
          <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Method</th>
          <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Count</th>
        </tr></thead>
        <tbody>
          <tr><td><span style='background:#dcfce7;color:#166534;font-size:8px;font-weight:700;padding:1px 6px;border-radius:20px;'>CASH</span>&nbsp; Cash</td><td class='num'>{cashCnt}</td></tr>
          <tr><td><span style='background:#dbeafe;color:#1e40af;font-size:8px;font-weight:700;padding:1px 6px;border-radius:20px;'>CC</span>&nbsp; Card</td><td class='num'>{cardCnt}</td></tr>
          <tr><td><span style='background:#fef3c7;color:#92400e;font-size:8px;font-weight:700;padding:1px 6px;border-radius:20px;'>BANK</span>&nbsp; Bank / Digital</td><td class='num'>{digCnt}</td></tr>
        </tbody>
        <tfoot><tr>
          <td style='background:#f9fafb;padding:6px 8px;font-weight:700;border-top:1.5px solid #d1d5db;'><strong>Total</strong></td>
          <td class='num' style='background:#f9fafb;padding:6px 8px;font-weight:700;border-top:1.5px solid #d1d5db;'><strong>{totalTxns}</strong></td>
        </tr></tfoot>
      </table>
    </div>
  </div>
  <div class='section-head'>Sales Detail</div>
  <table>
    <thead><tr>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Customer</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Invoice</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Inv Amount</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Payment Type</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Pay Amount</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Cashier</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Time</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Total ({_currency})</th>
    </tr></thead>
    <tbody>{salesRows}</tbody>
  </table>
  {PageFooter()}
</div>";
        }

        private string BuildPage2Body()
        {
            var unpaid = _pending.Where(x => x.Status == "Unpaid").ToList();
            decimal unpaidTotal = unpaid.Sum(x => x.GrandTotal);

            var rows = new StringBuilder();
            int seq = 1;
            foreach (var p in unpaid)
                rows.Append($@"
  <tr>
    <td>{seq++}</td><td>{He(p.InvoiceNo)}</td><td>{He(p.CustomerName)}</td>
    <td>{p.SaleDate:dd/MM/yyyy HH:mm}</td>
    <td class='num'>{_currency}&nbsp;{p.GrandTotal:N2}</td>
    <td><span style='background:#fef3c7;color:#92400e;font-size:8.5px;font-weight:700;
        padding:2px 7px;border-radius:20px;display:inline-block;text-transform:uppercase;'>UNPAID</span></td>
  </tr>");

            if (!unpaid.Any())
                rows.Append("<tr><td colspan='6' class='empty'>No unpaid invoices found.</td></tr>");

            return $@"
<div class='a4'>
  {PageHeader("Unpaid Invoices Report", $"All outstanding invoices as at {DateTime.Now:dd/MM/yyyy HH:mm}")}
  <div class='alert-grid'>
    <div style='background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:12px 14px;'>
      <div style='font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:.6px;color:#f87171;margin-bottom:4px;'>Unpaid Invoices Count</div>
      <div style='font-size:15px;font-weight:700;color:#dc2626;'>{unpaid.Count}</div>
    </div>
    <div style='background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:12px 14px;'>
      <div style='font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:.6px;color:#f87171;margin-bottom:4px;'>Total Unpaid Value</div>
      <div style='font-size:15px;font-weight:700;color:#dc2626;'>{_currency} {unpaidTotal:N2}</div>
    </div>
    <div style='background:#f9fafb;border:1px solid #f3f4f6;border-radius:8px;padding:12px 14px;'>
      <div style='font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:.6px;color:#9ca3af;margin-bottom:4px;'>Report Date</div>
      <div style='font-size:15px;font-weight:700;color:#111827;'>{_reportDate:dd/MM/yyyy}</div>
    </div>
  </div>
  <div class='section-head'>Unpaid Invoice Detail</div>
  <table>
    <thead><tr>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;width:36px;'>#</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Invoice No</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Customer</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Date / Time</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Amount ({_currency})</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Status</th>
    </tr></thead>
    <tbody>{rows}</tbody>
    <tfoot><tr>
      <td colspan='4' style='background:#111827;color:#e5e7eb;padding:8px;font-weight:700;'><strong>Total Outstanding</strong></td>
      <td class='num' style='background:#111827;color:#e5e7eb;padding:8px;font-weight:700;'><strong>{_currency} {unpaidTotal:N2}</strong></td>
      <td style='background:#111827;color:#e5e7eb;padding:8px;'></td>
    </tr></tfoot>
  </table>
  {PageFooter()}
</div>";
        }

        private string BuildPage3Body()
        {
            decimal deletedTotal = _deleted.Sum(x => x.GrandTotal);

            var rows = new StringBuilder();
            int seq = 1;
            foreach (var d in _deleted)
                rows.Append($@"
  <tr>
    <td>{seq++}</td><td>{He(d.InvoiceNo)}</td><td>{He(d.CustomerName)}</td>
    <td>{d.SaleDate:dd/MM/yyyy HH:mm}</td>
    <td class='num'>{_currency}&nbsp;{d.GrandTotal:N2}</td>
    <td>{d.DeletedAt:dd/MM/yyyy HH:mm}</td>
    <td>{He(d.DeletedBy)}</td>
    <td><span style='background:#fee2e2;color:#991b1b;font-size:8.5px;font-weight:700;
        padding:2px 7px;border-radius:20px;display:inline-block;text-transform:uppercase;'>DELETED</span></td>
  </tr>");

            if (!_deleted.Any())
                rows.Append("<tr><td colspan='8' class='empty'>No deleted invoices for this date.</td></tr>");

            return $@"
<div class='a4'>
  {PageHeader("Deleted Invoices Report", $"Invoices voided on {_reportDate:dd/MM/yyyy}")}
  <div class='alert-grid'>
    <div style='background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:12px 14px;'>
      <div style='font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:.6px;color:#f87171;margin-bottom:4px;'>Deleted Count</div>
      <div style='font-size:15px;font-weight:700;color:#dc2626;'>{_deleted.Count}</div>
    </div>
    <div style='background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:12px 14px;'>
      <div style='font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:.6px;color:#f87171;margin-bottom:4px;'>Total Value Deleted</div>
      <div style='font-size:15px;font-weight:700;color:#dc2626;'>{_currency} {deletedTotal:N2}</div>
    </div>
    <div style='background:#f9fafb;border:1px solid #f3f4f6;border-radius:8px;padding:12px 14px;'>
      <div style='font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:.6px;color:#9ca3af;margin-bottom:4px;'>Report Date</div>
      <div style='font-size:15px;font-weight:700;color:#111827;'>{_reportDate:dd/MM/yyyy}</div>
    </div>
  </div>
  <div class='section-head'>Deleted Invoice Detail</div>
  <table>
    <thead><tr>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;width:36px;'>#</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Invoice No</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Customer</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Original Date</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:right;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Amount ({_currency})</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Deleted At</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Deleted By</th>
      <th style='background:#111827;color:#e5e7eb;font-weight:600;padding:7px 8px;text-align:left;font-size:9px;text-transform:uppercase;letter-spacing:.5px;'>Status</th>
    </tr></thead>
    <tbody>{rows}</tbody>
    <tfoot><tr>
      <td colspan='4' style='background:#111827;color:#e5e7eb;padding:8px;font-weight:700;'><strong>Total Deleted Value</strong></td>
      <td class='num' style='background:#111827;color:#e5e7eb;padding:8px;font-weight:700;'><strong>{_currency} {deletedTotal:N2}</strong></td>
      <td colspan='3' style='background:#111827;color:#e5e7eb;padding:8px;'></td>
    </tr></tfoot>
  </table>
  {PageFooter()}
</div>";
        }

        private string BuildSummaryStrip()
        {
            if (!_sales.Any()) return "No sales recorded for this date.";

            decimal totalSales = _sales.Where(x => !x.IsReturn).GroupBy(x => x.SaleID).Sum(g => g.First().InvoiceAmt);
            int invoices = _sales.Where(x => !x.IsReturn).Select(x => x.SaleID).Distinct().Count();
            decimal cashTotal = _sales.Where(x => !x.IsReturn && x.PaymentType == "CASH").Sum(x => x.PaymentAmt);
            decimal cardTotal = _sales.Where(x => !x.IsReturn && x.PaymentType == "CC").Sum(x => x.PaymentAmt);
            decimal digitalTotal = _sales.Where(x => !x.IsReturn && x.PaymentType != "CASH" && x.PaymentType != "CC").Sum(x => x.PaymentAmt);

            return $"Total: {_currency} {totalSales:N2}  |  Invoices: {invoices}  |  " +
                   $"Cash: {_currency} {cashTotal:N2}  |  Card: {_currency} {cardTotal:N2}  |  " +
                   $"Bank/Digital: {_currency} {digitalTotal:N2}";
        }

        private void BtnPrint_Click(object sender, EventArgs e) => PrintViaChrome();

        private void PrintViaChrome()
        {
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(),
                    $"DayEndReport_{_reportDate:yyyyMMdd}_{DateTime.Now:HHmmss}.html");

                string html = $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
{SharedCss()}
* {{ -webkit-print-color-adjust:exact !important;print-color-adjust:exact !important; }}
</style>
</head>
<body onload='window.print()'>
  {BuildPage1Body()}
  {BuildPage2Body()}
  {BuildPage3Body()}
</body>
</html>";

                File.WriteAllText(tempPath, html, System.Text.Encoding.UTF8);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });

                var t = new System.Threading.Timer(_ =>
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }, null, 60000, System.Threading.Timeout.Infinite);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Print error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCsv_Click(object sender, EventArgs e)
        {
            var sd = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"DayEnd_{_reportDate:yyyyMMdd}.csv",
                Title = "Save Day End Report as CSV",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (sd.ShowDialog(this) != DialogResult.OK) return;

            var sb = new StringBuilder();
            sb.AppendLine("Customer,Invoice,InvoiceAmount,PaymentType,PaymentAmount,Cashier,Time,Total");
            foreach (var s in _sales)
                sb.AppendLine(
                    $"{Csv("CSG001")},{Csv(s.InvoiceNo)}," +
                    $"{s.InvoiceAmt:F2},{Csv(s.PaymentDesc)}," +
                    $"{s.PaymentAmt:F2},{Csv(s.CashierName)}," +
                    $"{s.SaleDate:yyyy-MM-dd HH:mm:ss},{s.InvoiceAmt:F2}");

            sb.AppendLine();
            sb.AppendLine("--- UNPAID INVOICES ---");
            sb.AppendLine("InvoiceNo,Customer,Date,Total,Status");
            foreach (var p in _pending.Where(x => x.Status == "Unpaid"))
                sb.AppendLine($"{Csv(p.InvoiceNo)},{Csv(p.CustomerName)},{p.SaleDate:yyyy-MM-dd HH:mm:ss},{p.GrandTotal:F2},Unpaid");

            sb.AppendLine();
            sb.AppendLine("--- DELETED INVOICES ---");
            sb.AppendLine("InvoiceNo,Customer,OriginalDate,Total,DeletedAt,DeletedBy");
            foreach (var d in _deleted)
                sb.AppendLine($"{Csv(d.InvoiceNo)},{Csv(d.CustomerName)},{d.SaleDate:yyyy-MM-dd HH:mm:ss},{d.GrandTotal:F2},{d.DeletedAt:yyyy-MM-dd HH:mm:ss},{Csv(d.DeletedBy)}");

            File.WriteAllText(sd.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Saved:\n{sd.FileName}", "Exported",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string He(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;")
                    .Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
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
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DayEndScheduler
    // ══════════════════════════════════════════════════════════════════════════
    public class DayEndScheduler : IDisposable
    {
        private System.Threading.Timer _timer;
        private readonly int _companyId;
        private readonly string _company;
        private readonly string _currency;
        private readonly Form _owner;

        public DayEndScheduler(Form owner, int companyId,
            string company = "ABC",
            string currency = "BWP")
        {
            _owner = owner;
            _companyId = companyId;
            _company = company;
            _currency = currency;
        }

        public void Start()
        {
            DateTime yesterday = DateTime.Now.Date.AddDays(-1);
            if (!SalesRepository.DayEndExists(yesterday))
                TryGenerateReport(yesterday);
            ScheduleNext();
        }

        private void ScheduleNext()
        {
            DateTime next = DateTime.Now.Date.AddDays(1);
            long msUntil = (long)(next - DateTime.Now).TotalMilliseconds;
            _timer = new System.Threading.Timer(_ => OnMidnight(), null, msUntil, Timeout.Infinite);
        }

        private void OnMidnight()
        {
            TryGenerateReport(DateTime.Now.Date.AddDays(-1));
            ScheduleNext();
        }

        private void TryGenerateReport(DateTime date)
        {
            if (SalesRepository.DayEndExists(date)) return;

            var sales = SalesRepository.LoadSales(date.Date, date.Date.AddDays(1), _companyId);
            int invoiceCount = sales.Where(x => !x.IsReturn).Select(x => x.SaleID).Distinct().Count();
            decimal totalSales = sales.Where(x => !x.IsReturn).GroupBy(x => x.SaleID).Sum(g => g.First().InvoiceAmt);

            SalesRepository.LogDayEnd(date, totalSales, invoiceCount);

            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ABC", "Reports");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"DayEnd_{date:yyyyMMdd}.txt");

            try
            {
                File.WriteAllText(file,
                    $"Day End Report — {date:dd/MM/yyyy}\n" +
                    $"Total: {totalSales:N2}  Invoices: {invoiceCount}\n" +
                    $"Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n",
                    Encoding.UTF8);
            }
            catch { }

            _owner?.BeginInvoke(new Action(() =>
            {
                new DayEndReportForm(date, _companyId, _company, _currency).Show(_owner);
            }));
        }

        public void Dispose() => _timer?.Dispose();


        public class RetailSalesHeader

        {

            public string? OdataEtag { get; set; } = "";

            public string? DataAreaId { get; set; } = "";

            public string? BISInvoiceId { get; set; } = "";

            public string? SyncStatus { get; set; } = "";

            public string? InvoiceDescription { get; set; } = "PVC";

            public string? VATRegistrationId { get; set; } = "";

            public string? Comments { get; set; } = "";

            public DateTime? InvoiceDate { get; set; }

            public string? InvoiceAccount { get; set; } = "";

            public DateTime? PostingDate { get; set; }

            public string? CustAccount { get; set; } = "CG001";

            public int RetryCount { get; set; } = 0;

            public string? InvoiceId { get; set; } = "";

            public DateTime? RetryDateTime { get; set; }

            public string? StoreId { get; set; } = "";

            public int TerminalId { get; set; } = 0;

            public string? InvoiceAccountName { get; set; } = "";

            public decimal InvoiceAmount { get; set; }

            public string? SOSalesId { get; set; } = "";

            public string? WMSLocationId { get; set; } = "Default";

            public string? InventLocationId { get; set; } = "F1";

            public string? CompanyId { get; set; }

            public DateTime? DueDate { get; set; }

            public string? SalesId { get; set; }

            public string? InventSiteId { get; set; } = "PVC";

            public string? SalesStatus { get; set; } = "POSSales";

        }

        public class RetailSalesLine

        {

            public string? OdataEtag { get; set; }

            public string? DataAreaId { get; set; }

            public string? BISInvoiceId { get; set; }

            public int InvoiceLineId { get; set; }

            public decimal DiscountAmount { get; set; }

            public string? ItemId { get; set; }

            public decimal ChargesAmount { get; set; }

            public decimal TotalAmount { get; set; }

            public string? TaxGroup { get; set; }

            public decimal TaxAmount { get; set; }

            public string? InvoiceId { get; set; }

            public decimal PriceUnit { get; set; }

            public decimal LineAmount { get; set; }

            public string? TaxItemGroup { get; set; }

            public decimal SalesQty { get; set; }

            public string? InventSerialId { get; set; }

            public string? InventBatchId { get; set; }

        }

        public class RetailSalesInvoicePaymentResponse

        {

            public string? OdataEtag { get; set; }

            public string? DataAreaId { get; set; }

            public string? BISInvoiceId { get; set; }

            public string? InvoiceId { get; set; }

            public decimal InvoiceAmount { get; set; }

            public string? SalesId { get; set; }

            public string? PaymentId { get; set; }

            public string? BISSalesStatus { get; set; }

            public DateTime? PaymentDate { get; set; }

            public string? BISSyncStatus { get; set; }

            public string? PaymentType { get; set; }

        }
    }
}