using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace POSAPP.Invoice
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Domain models
    // ══════════════════════════════════════════════════════════════════════════
    public class SalesReturnRecord
    {
        public int Id { get; set; }
        public string ReturnInvoiceNo { get; set; }
        public string OriginalInvoiceNo { get; set; }
        public string CustomerName { get; set; }
        public string CashierName { get; set; }
        public string RefundMethod { get; set; }
        public decimal RefundTotal { get; set; }
        public DateTime ReturnDate { get; set; }
        public int CompanyId { get; set; }
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

        // ── ADD THESE TWO ──────────────────────────────
        /// <summary>Tax percentage (e.g. 9 for 9%). 0 if none.</summary>
        public decimal TaxPct { get; set; } = 0m;

        /// <summary>Unit of measure (e.g. "EA", "KG", "PCS"). Defaults to EA.</summary>
        public string UOM { get; set; } = "EA";
        // ───────────────────────────────────────────────
    }
    public class OriginalInvoiceLine
    {
      
        public string ItemName { get; set; }
        public int Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPct { get; set; }
        public string Barcode { get; set; }

        // ── ADD THESE TWO ──────────────────────────────
        /// <summary>Tax percentage (e.g. 9 for 9%). 0 if none.</summary>
        public decimal TaxPct { get; set; } = 0m;

        /// <summary>Unit of measure (e.g. "EA", "KG", "PCS"). Defaults to EA.</summary>
        public string UOM { get; set; } = "EA";
        // ───────────────────────────────────────────────
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
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SalesReturnRepository
    // ══════════════════════════════════════════════════════════════════════════
    public static class SalesReturnRepository
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
                    Notes             TEXT    NULL
                );");

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS SalesReturnLine (
                    LineId      INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnId    INTEGER NOT NULL,
                    ItemName    TEXT    NOT NULL,
                    UnitPrice   REAL    NOT NULL DEFAULT 0,
                    DiscountPct REAL    NOT NULL DEFAULT 0,
                    ReturnQty   INTEGER NOT NULL DEFAULT 1,
                    RefundAmt   REAL    NOT NULL DEFAULT 0,
                    Barcode     TEXT    NULL,
                    FOREIGN KEY(ReturnId) REFERENCES SalesReturnHeader(ReturnId)
                );");

            // Index for fast lookup by original invoice
            Execute(conn, @"
                CREATE INDEX IF NOT EXISTS idx_return_orig_inv
                ON SalesReturnHeader(OriginalInvoiceNo);");
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
                 RefundMethod, RefundTotal, ReturnDate, CompanyId)
            VALUES
                (@rtn, @orig, @cust, @cashier,
                 @method, @total, @date, @company);", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@rtn", record.ReturnInvoiceNo);
                    cmd.Parameters.AddWithValue("@orig", record.OriginalInvoiceNo);
                    cmd.Parameters.AddWithValue("@cust", record.CustomerName ?? "Walk-in");
                    cmd.Parameters.AddWithValue("@cashier", record.CashierName ?? "ADMIN");
                    cmd.Parameters.AddWithValue("@method", record.RefundMethod ?? "cash");
                    cmd.Parameters.AddWithValue("@total", (double)record.RefundTotal);
                    cmd.Parameters.AddWithValue("@date", now);
                    cmd.Parameters.AddWithValue("@company", record.CompanyId);
                    cmd.ExecuteNonQuery();
                    returnId = conn.LastInsertRowId;
                }

                System.Diagnostics.Debug.WriteLine($"SaveReturn: Header inserted, ReturnId={returnId}");

                foreach (var line in record.Lines)
                {
                    using var lc = new SQLiteCommand(@"
                INSERT INTO SalesReturnLine
                    (ReturnId, ItemName, UnitPrice, DiscountPct,
                     ReturnQty, RefundAmt, Barcode)
                VALUES
                    (@rid, @name, @price, @disc,
                     @qty, @refund, @barcode);", conn, tx);
                    lc.Parameters.AddWithValue("@rid", returnId);
                    lc.Parameters.AddWithValue("@name", line.ItemName);
                    lc.Parameters.AddWithValue("@price", (double)line.UnitPrice);
                    lc.Parameters.AddWithValue("@disc", (double)line.DiscountPct);
                    lc.Parameters.AddWithValue("@qty", line.ReturnQty);
                    lc.Parameters.AddWithValue("@refund", (double)line.RefundAmt);
                    lc.Parameters.AddWithValue("@barcode", line.Barcode ?? "");
                    lc.ExecuteNonQuery();

                    System.Diagnostics.Debug.WriteLine($"SaveReturn: Line inserted — {line.ItemName} qty={line.ReturnQty}");
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
        //  GET CUSTOMER FOR INVOICE
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
                        WHERE rh.OriginalInvoiceNo = @inv
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
                        Barcode = r["Barcode"]?.ToString() ?? ""
                    });
            }
            catch { }
            return list;
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
                using var conn = new SQLiteConnection( $"Data Source={DbPath};Version=3;");
                conn.Open();

                // Adjust the table/column names below to match your actual schema.
                // Common variants: Sales.SaleDate | Sales.InvoiceDate | Sales.CreatedAt
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
        // Returns a dictionary of  Barcode-or-ItemName → total qty already returned
        // for a given original invoice number.
        public static Dictionary<string, int> GetReturnedQtys(string originalInvoiceNo)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(DbPath)) return dict;

            try
            {
                using var conn = Open();
                const string sql = @"
            SELECT  rl.Barcode, rl.ItemName, SUM(rl.ReturnQty) AS TotalReturned
            FROM    SalesReturnLine   rl
            JOIN    SalesReturnHeader rh ON rh.ReturnId = rl.ReturnId
            WHERE   rh.OriginalInvoiceNo = @inv
            GROUP   BY rl.Barcode, rl.ItemName;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@inv", originalInvoiceNo);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    string key = rdr["Barcode"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        key = rdr["ItemName"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    int qty = Convert.ToInt32(rdr["TotalReturned"]);
                    dict[key] = dict.ContainsKey(key) ? dict[key] + qty : qty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetReturnedQtys: " + ex.Message);
            }

            return dict;
        }
    }
}