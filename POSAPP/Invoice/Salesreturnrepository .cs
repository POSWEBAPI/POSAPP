using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using POSAPP;

namespace POSAPP.Invoice
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Domain models
    // ══════════════════════════════════════════════════════════════════════════
    public class SalesReturnRecord
    {
        public int Id { get; set; }
        public string ReturnInvoiceNo { get; set; }

        /// <summary>Comma-joined list of every distinct source invoice referenced by Lines.</summary>
        public string OriginalInvoiceNo { get; set; }

        public string CustomerName { get; set; }
        public string CashierName { get; set; }
        public string RefundMethod { get; set; }
        public decimal RefundTotal { get; set; }
        public DateTime ReturnDate { get; set; }
        public int CompanyId { get; set; }

        // ── ADDED — header-level fields from the "concept" flow ────────────────
        public string ReturnReason { get; set; }
        public string RmaNumber { get; set; }
        public string DispositionCode { get; set; }
        // ─────────────────────────────────────────────────────────────────────

        public List<SalesReturnLine> Lines { get; set; } = new List<SalesReturnLine>();
    }

    public class SalesReturnLine
    {
        public int Id { get; set; }
        public string ItemName { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPct { get; set; }
        public int ReturnQty { get; set; }
        public decimal RefundAmt { get; set; }
        public string Barcode { get; set; }

        /// <summary>Tax percentage (e.g. 9 for 9%). 0 if none.</summary>
        public decimal TaxPct { get; set; } = 0m;

        /// <summary>Unit of measure (e.g. "EA", "KG", "PCS"). Defaults to EA.</summary>
        public string UOM { get; set; } = "EA";

        // ── ADDED — a single return can now span several source invoices, so
        // each line remembers exactly which invoice it came from. ─────────────
        public string OriginalInvoiceNo { get; set; }
    }

    public class OriginalInvoiceLine
    {
        public string ItemName { get; set; }
        public int Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPct { get; set; }
        public string Barcode { get; set; }
        public decimal TaxPct { get; set; } = 0m;
        public string UOM { get; set; } = "EA";
    }
    public class CustomerFullDto
    {
        [JsonPropertyName("customerID")] public int CustomerID { get; set; }
        [JsonPropertyName("customerCode")] public string CustomerCode { get; set; } = "";
        [JsonPropertyName("customerName")] public string CustomerName { get; set; } = "";
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("city")] public string City { get; set; } = "";
        [JsonPropertyName("country")] public string Country { get; set; } = "";
        [JsonPropertyName("mobile")] public string Mobile { get; set; } = "";
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("status")] public bool Status { get; set; }
    }
    public class ReturnedQtyDto
    {
        [JsonPropertyName("barcode")] public string Barcode { get; set; }
        [JsonPropertyName("itemName")] public string ItemName { get; set; }
        [JsonPropertyName("totalReturned")] public int TotalReturned { get; set; }
    }

    // Aggregated row for the Returns Report
    public class ReturnReportRow
    {
        public string ReturnInvoiceNo { get; set; }
        public string OriginalInvoiceNo { get; set; }
        public string CustomerName { get; set; }
        public string CashierName { get; set; }
        public string RefundMethod { get; set; }
        public decimal RefundTotal { get; set; }
        public DateTime ReturnDate { get; set; }
        public int LineCount { get; set; }
        public int TotalItemsReturned { get; set; }
        public string ReturnReason { get; set; }
        public string RmaNumber { get; set; }
        public string DispositionCode { get; set; }
    }

    // ── ADDED — support models for the customer-first return flow ─────────────
    public class CustomerLite
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
    }

    public class InvoiceLite
    {
        public string InvoiceNo { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal Total { get; set; }
        public int LineCount { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SalesReturnRepository
    // ══════════════════════════════════════════════════════════════════════════
    public static partial class SalesReturnRepository
    {
        private static readonly string DbPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        private static int _returnSeq = 0;
        private static readonly object _seqLock = new object();

        // ══════════════════════════════════════════════════════════════════════
        //  SCHEMA
        // ══════════════════════════════════════════════════════════════════════
        public static void EnsureSchema()
        {
            using var conn = Open();

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS SalesReturnHeader (
                    ReturnId          INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnInvoiceNo   TEXT    NOT NULL UNIQUE,
                    OriginalInvoiceNo TEXT    NOT NULL,
                    CustomerName      TEXT    NOT NULL DEFAULT 'Walk-in',
                    CashierName       TEXT    NOT NULL DEFAULT 'ADMIN',
                    RefundMethod      TEXT    NOT NULL DEFAULT 'cash',
                    RefundTotal       REAL    NOT NULL DEFAULT 0,
                    ReturnDate        TEXT    NOT NULL,
                    CompanyId         INTEGER NOT NULL DEFAULT 0,
                    Notes             TEXT    NULL,
                    ReturnReason      TEXT    NULL,
                    RmaNumber         TEXT    NULL,
                    DispositionCode   TEXT    NULL
                );");

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS SalesReturnLine (
                    LineId           INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnId         INTEGER NOT NULL,
                    ItemName         TEXT    NOT NULL,
                    UnitPrice        REAL    NOT NULL DEFAULT 0,
                    DiscountPct      REAL    NOT NULL DEFAULT 0,
                    ReturnQty        INTEGER NOT NULL DEFAULT 1,
                    RefundAmt        REAL    NOT NULL DEFAULT 0,
                    Barcode          TEXT    NULL,
                    TaxPct           REAL    NOT NULL DEFAULT 0,
                    UOM              TEXT    NOT NULL DEFAULT 'EA',
                    OriginalInvoiceNo TEXT   NULL,
                    FOREIGN KEY(ReturnId) REFERENCES SalesReturnHeader(ReturnId)
                );");

            // Index for fast lookup by original invoice
            Execute(conn, @"
                CREATE INDEX IF NOT EXISTS idx_return_orig_inv
                ON SalesReturnHeader(OriginalInvoiceNo);");

            // ── Migration guard: tables created by an older build won't have
            // the newer columns. ALTER TABLE ... ADD COLUMN is safe to attempt
            // repeatedly — SQLite throws "duplicate column name" if it already
            // exists, which we just swallow. ──────────────────────────────────
            TryAddColumn(conn, "SalesReturnHeader", "ReturnReason", "TEXT");
            TryAddColumn(conn, "SalesReturnHeader", "RmaNumber", "TEXT");
            TryAddColumn(conn, "SalesReturnHeader", "DispositionCode", "TEXT");
            TryAddColumn(conn, "SalesReturnLine", "TaxPct", "REAL NOT NULL DEFAULT 0");
            TryAddColumn(conn, "SalesReturnLine", "UOM", "TEXT NOT NULL DEFAULT 'EA'");
            TryAddColumn(conn, "SalesReturnLine", "OriginalInvoiceNo", "TEXT");
        }

        private static void TryAddColumn(SQLiteConnection conn, string table, string column, string type)
        {
            try { Execute(conn, $"ALTER TABLE {table} ADD COLUMN {column} {type};"); }
            catch { /* column already exists — nothing to do */ }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  INVOICE NUMBER
        // ══════════════════════════════════════════════════════════════════════
        public static string NextReturnInvoiceNo()
        {
            lock (_seqLock)
            {
                if (_returnSeq == 0) _returnSeq = LoadLastReturnSeq();
                _returnSeq++;
                return $"RTN-{DateTime.Now:yyyyMMdd}-{_returnSeq:D4}";
            }
        }

        private static int LoadLastReturnSeq()
        {
            if (!File.Exists(DbPath)) return 0;
            try
            {
                using var conn = Open();
                string today = DateTime.Now.ToString("yyyyMMdd");
                using var cmd = new SQLiteCommand(
                    $"SELECT MAX(CAST(SUBSTR(ReturnInvoiceNo,-4) AS INTEGER)) " +
                    $"FROM SalesReturnHeader WHERE ReturnInvoiceNo LIKE 'RTN-{today}-%';", conn);
                var result = cmd.ExecuteScalar();
                if (result != DBNull.Value && result != null) return Convert.ToInt32(result);
            }
            catch { }
            return 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CUSTOMER / INVOICE LOOKUPS  (drives the customer-first return flow)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Distinct customer names from SOInvoiceHeader matching the search text.</summary>
        public static List<CustomerLite> SearchCustomers(string query, int companyId)
        {
            var list = new List<CustomerLite>();
            if (!File.Exists(DbPath) || string.IsNullOrWhiteSpace(query)) return list;

            try
            {
                using var conn = Open();
                const string sql = @"
                    SELECT DISTINCT TRIM(InvoiceAccountName) AS CustomerName
                    FROM   SOInvoiceHeader
                    WHERE  CompanyID = @co
                      AND  InvoiceAccountName IS NOT NULL
                      AND  TRIM(InvoiceAccountName) <> ''
                      AND  LOWER(InvoiceAccountName) LIKE LOWER(@q)
                    ORDER  BY CustomerName
                    LIMIT  50;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@co", companyId);
                cmd.Parameters.AddWithValue("@q", $"%{query.Trim()}%");

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string name = r["CustomerName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        list.Add(new CustomerLite { CustomerName = name });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SearchCustomers ERROR: " + ex.Message);
            }
            return list;
        }
        public static async Task<List<POSAPP.CustomerFullDto>> GetActiveCustomersAsync(int companyId)
        {
            var list = new List<POSAPP.CustomerFullDto>();
            try
            {
                var result = await ApiClient.GetAsync<POSAPP.CustomerListDto>("/api/customers");
                list = (result?.Data ?? new List<POSAPP.CustomerFullDto>())
                    .Where(c => c.Status)
                    .OrderBy(c => c.CustomerName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                // NOTE: companyId is currently unused — CustomerFullDto has no CompanyID
                // property, so there's nothing to filter by per-company here. If your
                // customers ARE scoped per company, see the two options below.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetActiveCustomersAsync ERROR: " + ex.Message);
            }
            return list;
        }

        /// <summary>All invoices billed to this customer name, newest first, with a
        /// rough Total (sum of line net amounts, pre-tax) and item-line count.</summary>
        public static List<InvoiceLite> GetInvoicesForCustomer(string customerName, int companyId)
        {
            var list = new List<InvoiceLite>();
            if (!File.Exists(DbPath) || string.IsNullOrWhiteSpace(customerName)) return list;

            try
            {
                using var conn = Open();
                const string sql = @"
                    SELECT  h.InvoiceNo,
                            h.InvoiceDate,
                            COALESCE(SUM(l.LineNetAmount), 0) AS Total,
                            COUNT(l.InvoiceLineID)            AS LineCount
                    FROM    SOInvoiceHeader h
                    JOIN    SOInvoiceLine   l ON l.InvoiceID = h.InvoiceID
                    WHERE   h.CompanyID = @co
                      AND   TRIM(h.InvoiceAccountName) = TRIM(@cust)
                    GROUP BY h.InvoiceID, h.InvoiceNo, h.InvoiceDate
                    ORDER BY h.InvoiceDate DESC;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@co", companyId);
                cmd.Parameters.AddWithValue("@cust", customerName.Trim());

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    DateTime invDate = DateTime.Now;
                    string raw = r["InvoiceDate"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        if (!DateTime.TryParseExact(raw, "yyyy-MM-dd HH.mm.ss",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out invDate))
                        {
                            DateTime.TryParse(raw.Replace('.', ':'), out invDate);
                        }
                    }

                    list.Add(new InvoiceLite
                    {
                        InvoiceNo = r["InvoiceNo"]?.ToString() ?? "",
                        InvoiceDate = invDate,
                        Total = Convert.ToDecimal(r["Total"]),
                        LineCount = Convert.ToInt32(r["LineCount"])
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetInvoicesForCustomer ERROR: " + ex.Message);
            }
            return list;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SAVE RETURN
        // ══════════════════════════════════════════════════════════════════════
        public static int SaveReturn(SalesReturnRecord record)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            try
            {
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                long returnId;
                using (var cmd = new SQLiteCommand(@"
            INSERT INTO SalesReturnHeader
                (ReturnInvoiceNo, OriginalInvoiceNo, CustomerName, CashierName,
                 RefundMethod, RefundTotal, ReturnDate, CompanyId,
                 ReturnReason, RmaNumber, DispositionCode)
            VALUES
                (@rtn, @orig, @cust, @cashier,
                 @method, @total, @date, @company,
                 @reason, @rma, @disp);", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@rtn", record.ReturnInvoiceNo);
                    cmd.Parameters.AddWithValue("@orig", record.OriginalInvoiceNo);
                    cmd.Parameters.AddWithValue("@cust", record.CustomerName ?? "Walk-in");
                    cmd.Parameters.AddWithValue("@cashier", record.CashierName ?? "ADMIN");
                    cmd.Parameters.AddWithValue("@method", record.RefundMethod ?? "cash");
                    cmd.Parameters.AddWithValue("@total", (double)record.RefundTotal);
                    cmd.Parameters.AddWithValue("@date", now);
                    cmd.Parameters.AddWithValue("@company", record.CompanyId);
                    cmd.Parameters.AddWithValue("@reason", (object)record.ReturnReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@rma", (object)record.RmaNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@disp", (object)record.DispositionCode ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                    returnId = conn.LastInsertRowId;
                }

                System.Diagnostics.Debug.WriteLine($"SaveReturn: Header inserted, ReturnId={returnId}");

                foreach (var line in record.Lines)
                {
                    using var lc = new SQLiteCommand(@"
                INSERT INTO SalesReturnLine
                    (ReturnId, ItemName, UnitPrice, DiscountPct,
                     ReturnQty, RefundAmt, Barcode, TaxPct, UOM, OriginalInvoiceNo)
                VALUES
                    (@rid, @name, @price, @disc,
                     @qty, @refund, @barcode, @taxpct, @uom, @origline);", conn, tx);
                    lc.Parameters.AddWithValue("@rid", returnId);
                    lc.Parameters.AddWithValue("@name", line.ItemName);
                    lc.Parameters.AddWithValue("@price", (double)line.UnitPrice);
                    lc.Parameters.AddWithValue("@disc", (double)line.DiscountPct);
                    lc.Parameters.AddWithValue("@qty", line.ReturnQty);
                    lc.Parameters.AddWithValue("@refund", (double)line.RefundAmt);
                    lc.Parameters.AddWithValue("@barcode", line.Barcode ?? "");
                    lc.Parameters.AddWithValue("@taxpct", (double)line.TaxPct);
                    lc.Parameters.AddWithValue("@uom", string.IsNullOrWhiteSpace(line.UOM) ? "EA" : line.UOM);
                    lc.Parameters.AddWithValue("@origline", (object)line.OriginalInvoiceNo ?? (object)record.OriginalInvoiceNo ?? DBNull.Value);
                    lc.ExecuteNonQuery();

                    System.Diagnostics.Debug.WriteLine($"SaveReturn: Line inserted — {line.ItemName} qty={line.ReturnQty} inv={line.OriginalInvoiceNo}");
                }

                tx.Commit();
                System.Diagnostics.Debug.WriteLine("SaveReturn: Transaction committed.");
                return (int)returnId;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                System.Diagnostics.Debug.WriteLine("SaveReturn FAILED: " + ex.Message);
                throw;  // re-throw so BtnProcessReturn_Click catches and shows the error
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  LOAD ORIGINAL INVOICE LINES  (from SOInvoiceHeader/Line)
        // ══════════════════════════════════════════════════════════════════════
        public static List<OriginalInvoiceLine> LoadOriginalInvoiceLines(
      string invoiceNo, int companyId)
        {
            var list = new List<OriginalInvoiceLine>();

            if (!File.Exists(DbPath))
            {
                System.Diagnostics.Debug.WriteLine($"LoadOriginalInvoiceLines: DB not found at '{DbPath}'");
                return list;
            }

            try
            {
                using var conn = Open();

                const string sql = @"
            SELECT  l.ItemName,
                    l.UnitPrice,
                    CASE WHEN (l.LineNetAmount + l.DiscountAmount) > 0
                         THEN ROUND((l.DiscountAmount /
                              (l.LineNetAmount + l.DiscountAmount)) * 100, 2)
                         ELSE 0
                    END                            AS DiscountPct,
                    CAST(l.Qty AS INTEGER)         AS Qty,
                    COALESCE(l.StockCode,  '')     AS Barcode,
                    COALESCE(l.Tax,         0)     AS TaxPct,
                    COALESCE(l.UOM_Text,  'EA')   AS UOM
            FROM    SOInvoiceHeader h
            JOIN    SOInvoiceLine   l ON l.InvoiceID = h.InvoiceID
            WHERE   TRIM(h.InvoiceNo) = TRIM(@inv)
            AND     h.CompanyID       = @co
            ORDER   BY l.InvoiceLineID;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@inv", invoiceNo.Trim());
                cmd.Parameters.AddWithValue("@co", companyId);

                using var r = cmd.ExecuteReader();

                if (!r.HasRows)
                    System.Diagnostics.Debug.WriteLine(
                        $"No lines found for InvoiceNo='{invoiceNo}' CompanyID={companyId}");

                while (r.Read())
                {
                    list.Add(new OriginalInvoiceLine
                    {
                        ItemName = r["ItemName"]?.ToString() ?? "Unknown",
                        UnitPrice = Convert.ToDecimal(r["UnitPrice"]),
                        DiscountPct = Convert.ToDecimal(r["DiscountPct"]),
                        Qty = Math.Max(1, Convert.ToInt32(r["Qty"])),
                        Barcode = r["Barcode"]?.ToString() ?? "",
                        TaxPct = Convert.ToDecimal(r["TaxPct"]),
                        UOM = r["UOM"]?.ToString() ?? "EA"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadOriginalInvoiceLines ERROR: " + ex.Message);
            }

            return list;
        }
        // ══════════════════════════════════════════════════════════════════════
        //  GET CUSTOMER FOR INVOICE  (kept for backward compatibility)
        // ══════════════════════════════════════════════════════════════════════
        public static string GetCustomerForInvoice(string invoiceNo)
        {
            if (!File.Exists(DbPath)) return "Walk-in";
            try
            {
                using var conn = Open();
                using var cmd = new SQLiteCommand(
                    "SELECT InvoiceAccountName FROM SOInvoiceHeader WHERE InvoiceNo=@inv LIMIT 1;",
                    conn);
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                var result = cmd.ExecuteScalar();
                return result?.ToString()?.Trim() is string s && !string.IsNullOrEmpty(s) ? s : "Walk-in";
            }
            catch { return "Walk-in"; }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  IS FULLY RETURNED
        // ══════════════════════════════════════════════════════════════════════
        public static bool IsFullyReturned(string originalInvoiceNo)
        {
            if (!File.Exists(DbPath)) return false;
            try
            {
                using var conn = Open();

                // Sum of returned qty per item vs original qty
                const string sql = @"
                    SELECT
                        COALESCE(SUM(rtn.ReturnQty), 0)  AS ReturnedQty,
                        COALESCE(MAX(orig.TotalQty), 0)  AS OrigQty
                    FROM (
                        SELECT SUM(CAST(l.Qty AS INTEGER)) AS TotalQty
                        FROM SOInvoiceHeader h
                        JOIN SOInvoiceLine   l ON l.InvoiceID = h.InvoiceID
                        WHERE h.InvoiceNo = @inv
                    ) orig,
                    (
                        SELECT COALESCE(SUM(rl.ReturnQty), 0) AS ReturnQty
                        FROM SalesReturnHeader rh
                        JOIN SalesReturnLine   rl ON rl.ReturnId = rh.ReturnId
                        WHERE rl.OriginalInvoiceNo = @inv
                           OR rh.OriginalInvoiceNo = @inv
                    ) rtn;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@inv", originalInvoiceNo);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    int returned = Convert.ToInt32(r["ReturnedQty"]);
                    int original = Convert.ToInt32(r["OrigQty"]);
                    return original > 0 && returned >= original;
                }
            }
            catch { }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  LOAD RETURNS FOR REPORT
        // ══════════════════════════════════════════════════════════════════════
        public static List<ReturnReportRow> LoadReturns(
            DateTime from, DateTime to, int companyId)
        {
            var list = new List<ReturnReportRow>();
            if (!File.Exists(DbPath)) return list;

            try
            {
                using var conn = Open();
                const string sql = @"
                    SELECT
                        h.ReturnInvoiceNo,
                        h.OriginalInvoiceNo,
                        h.CustomerName,
                        h.CashierName,
                        h.RefundMethod,
                        h.RefundTotal,
                        h.ReturnDate,
                        h.ReturnReason,
                        h.RmaNumber,
                        h.DispositionCode,
                        COUNT(l.LineId)    AS LineCount,
                        SUM(l.ReturnQty)   AS TotalItemsReturned
                    FROM SalesReturnHeader h
                    LEFT JOIN SalesReturnLine l ON l.ReturnId = h.ReturnId
                    WHERE h.CompanyId   = @co
                      AND h.ReturnDate >= @from
                      AND h.ReturnDate <  @to
                    GROUP BY h.ReturnId
                    ORDER BY h.ReturnDate DESC;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@co", companyId);
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new ReturnReportRow
                    {
                        ReturnInvoiceNo = r["ReturnInvoiceNo"]?.ToString() ?? "",
                        OriginalInvoiceNo = r["OriginalInvoiceNo"]?.ToString() ?? "",
                        CustomerName = r["CustomerName"]?.ToString() ?? "Walk-in",
                        CashierName = r["CashierName"]?.ToString() ?? "ADMIN",
                        RefundMethod = r["RefundMethod"]?.ToString() ?? "cash",
                        RefundTotal = Convert.ToDecimal(r["RefundTotal"]),
                        ReturnDate = DateTime.TryParse(r["ReturnDate"]?.ToString(), out var dt) ? dt : DateTime.Now,
                        ReturnReason = r["ReturnReason"] == DBNull.Value ? "" : r["ReturnReason"]?.ToString(),
                        RmaNumber = r["RmaNumber"] == DBNull.Value ? "" : r["RmaNumber"]?.ToString(),
                        DispositionCode = r["DispositionCode"] == DBNull.Value ? "" : r["DispositionCode"]?.ToString(),
                        LineCount = Convert.ToInt32(r["LineCount"]),
                        TotalItemsReturned = r["TotalItemsReturned"] == DBNull.Value
                                            ? 0 : Convert.ToInt32(r["TotalItemsReturned"])
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadReturns: " + ex.Message);
            }

            return list;
        }

        // Load full lines for a single return (used in drill-down report)
        public static List<SalesReturnLine> LoadReturnLines(string returnInvoiceNo)
        {
            var list = new List<SalesReturnLine>();
            if (!File.Exists(DbPath)) return list;

            try
            {
                using var conn = Open();
                const string sql = @"
                    SELECT  l.*
                    FROM    SalesReturnLine   l
                    JOIN    SalesReturnHeader h ON h.ReturnId = l.ReturnId
                    WHERE   h.ReturnInvoiceNo = @rtn
                    ORDER   BY l.LineId;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@rtn", returnInvoiceNo);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new SalesReturnLine
                    {
                        Id = Convert.ToInt32(r["LineId"]),
                        ItemName = r["ItemName"]?.ToString() ?? "",
                        UnitPrice = Convert.ToDecimal(r["UnitPrice"]),
                        DiscountPct = Convert.ToDecimal(r["DiscountPct"]),
                        ReturnQty = Convert.ToInt32(r["ReturnQty"]),
                        RefundAmt = Convert.ToDecimal(r["RefundAmt"]),
                        Barcode = r["Barcode"]?.ToString() ?? "",
                        TaxPct = HasColumn(r, "TaxPct") && r["TaxPct"] != DBNull.Value ? Convert.ToDecimal(r["TaxPct"]) : 0m,
                        UOM = HasColumn(r, "UOM") && r["UOM"] != DBNull.Value ? r["UOM"].ToString() : "EA",
                        OriginalInvoiceNo = HasColumn(r, "OriginalInvoiceNo") && r["OriginalInvoiceNo"] != DBNull.Value
                                             ? r["OriginalInvoiceNo"].ToString() : ""
                    });
            }
            catch { }
            return list;
        }

        private static bool HasColumn(SQLiteDataReader r, string name)
        {
            for (int i = 0; i < r.FieldCount; i++)
                if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Summary totals for the toolbar strip
        public static (decimal TotalRefund, int TotalReturns, int TotalItems)
            GetReturnSummary(DateTime from, DateTime to, int companyId)
        {
            if (!File.Exists(DbPath)) return (0m, 0, 0);
            try
            {
                using var conn = Open();
                const string sql = @"
                    SELECT  COALESCE(SUM(h.RefundTotal), 0)  AS TotalRefund,
                            COUNT(DISTINCT h.ReturnId)        AS TotalReturns,
                            COALESCE(SUM(l.ReturnQty), 0)    AS TotalItems
                    FROM SalesReturnHeader h
                    LEFT JOIN SalesReturnLine l ON l.ReturnId = h.ReturnId
                    WHERE h.CompanyId   = @co
                      AND h.ReturnDate >= @from
                      AND h.ReturnDate <  @to;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@co", companyId);
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    return (Convert.ToDecimal(r["TotalRefund"]),
                            Convert.ToInt32(r["TotalReturns"]),
                            Convert.ToInt32(r["TotalItems"]));
            }
            catch { }
            return (0m, 0, 0);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static SQLiteConnection Open()
        {
            var c = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            c.Open();
            using (var p = new SQLiteCommand("PRAGMA journal_mode=WAL;", c))
                p.ExecuteNonQuery();
            using (var p = new SQLiteCommand("PRAGMA foreign_keys=ON;", c))
                p.ExecuteNonQuery();
            return c;
        }

        private static void Execute(SQLiteConnection conn, string sql)
        {
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }
        public static DateTime? GetInvoiceSaleDate(string invoiceNo)
        {

            if (!File.Exists(DbPath)) return null;

            try
            {
                using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                conn.Open();

                const string sql = @"
    SELECT InvoiceDate
    FROM   SOInvoiceHeader
    WHERE  InvoiceNo = @InvoiceNo
    LIMIT  1;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@InvoiceNo", invoiceNo);

                var result = cmd.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                    return null;

                string raw = result.ToString();

                if (DateTime.TryParseExact( 
                        raw,
                        "yyyy-MM-dd HH.mm.ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime dt))
                {
                    return dt;
                }

                if (DateTime.TryParse(raw.Replace('.', ':'), out dt))
                {
                    return dt;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetInvoiceSaleDate: " + ex.Message);
                return null;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  GET RETURNED QTYS — now sourced from the API instead of local SQLite
        // ══════════════════════════════════════════════════════════════════════
        public static async Task<Dictionary<string, int>> GetReturnedQtysAsync(string originalInvoiceNo)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(originalInvoiceNo)) return dict;

            try
            {
                var result = await ApiClient.GetAsync<List<ReturnedQtyDto>>(
                    $"/api/salesreturns/returned-qtys?invoiceNo={Uri.EscapeDataString(originalInvoiceNo)}");

                if (result != null)
                {
                    foreach (var r in result)
                    {
                        string key = !string.IsNullOrWhiteSpace(r.Barcode) ? r.Barcode.Trim() : r.ItemName?.Trim();
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        dict[key] = dict.ContainsKey(key) ? dict[key] + r.TotalReturned : r.TotalReturned;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetReturnedQtysAsync ERROR: " + ex.Message);
            }
            return dict;
        }
    }
}