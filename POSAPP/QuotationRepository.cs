// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  QuotationRepository.cs                                                  ║
// ╚══════════════════════════════════════════════════════════════════════════╝
using POSAPP.Reports;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace POSAPP
{
    public static class QuotationRepository
    {
        private static readonly string _dbPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ══════════════════════════════════════════════════════════════════
        //  DDL
        // ══════════════════════════════════════════════════════════════════
        public static void EnsureSchema()
        {
            if (!File.Exists(_dbPath)) return;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Quotations (
                    QuotationID     INTEGER PRIMARY KEY AUTOINCREMENT,
                    QuotationNo     TEXT    NOT NULL UNIQUE,
                    CompanyID       INTEGER NOT NULL DEFAULT 1,
                    CustomerName    TEXT    NOT NULL DEFAULT 'Walk-in',
                    CustomerAddress TEXT,
                    CustomerVat     TEXT,
                    QuoteDate       TEXT    NOT NULL,
                    ValidUntil      TEXT,
                    GrandTotal      REAL    NOT NULL DEFAULT 0,
                    Subtotal        REAL    NOT NULL DEFAULT 0,
                    TaxTotal        REAL    NOT NULL DEFAULT 0,
                    DiscountTotal   REAL    NOT NULL DEFAULT 0,
                    CurrencySymbol  TEXT    NOT NULL DEFAULT 'P',
                    Status          TEXT    NOT NULL DEFAULT 'Open',
                    Notes           TEXT,
                    ConvertedInvNo  TEXT,
                    ConvertedAt     TEXT,
                    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
                );
                CREATE TABLE IF NOT EXISTS QuotationLines (
                    LineID      INTEGER PRIMARY KEY AUTOINCREMENT,
                    QuotationNo TEXT    NOT NULL,
                    LineNo      INTEGER NOT NULL,
                    StockCode   TEXT,
                    Description TEXT    NOT NULL,
                    UOM         TEXT    NOT NULL DEFAULT 'Ea',
                    Qty         REAL    NOT NULL DEFAULT 1,
                    UnitPrice   REAL    NOT NULL DEFAULT 0,
                    DiscountPct REAL    NOT NULL DEFAULT 0,
                    LineTotal   REAL    NOT NULL DEFAULT 0,
                    PriceGroup  TEXT,
                    FOREIGN KEY(QuotationNo) REFERENCES Quotations(QuotationNo)
                );
                CREATE INDEX IF NOT EXISTS IX_QuotLines_QNo
                    ON QuotationLines(QuotationNo);
                CREATE TABLE IF NOT EXISTS QuotationCounter (
                    CounterDate TEXT PRIMARY KEY,
                    LastSeq     INTEGER NOT NULL DEFAULT 0
                );";
            cmd.ExecuteNonQuery();
        }

        // ══════════════════════════════════════════════════════════════════
        //  NEXT QUOTATION NUMBER  →  QO-20260611-001
        // ══════════════════════════════════════════════════════════════════
        public static string NextQuotationNo()
        {
            if (!File.Exists(_dbPath))
                return $"QO-{DateTime.Now:yyyyMMdd}-001";

            using var conn = Open();
            using var tx = conn.BeginTransaction();

            string today = DateTime.Now.ToString("yyyyMMdd");

            using (var up = conn.CreateCommand())
            {
                up.Transaction = tx;
                up.CommandText = @"
                    INSERT INTO QuotationCounter (CounterDate, LastSeq) VALUES (@d, 1)
                    ON CONFLICT(CounterDate) DO UPDATE SET LastSeq = LastSeq + 1;";
                up.Parameters.AddWithValue("@d", today);
                up.ExecuteNonQuery();
            }

            int seq;
            using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = "SELECT LastSeq FROM QuotationCounter WHERE CounterDate=@d;";
                sel.Parameters.AddWithValue("@d", today);
                seq = Convert.ToInt32(sel.ExecuteScalar());
            }

            tx.Commit();
            return $"QO-{today}-{seq:D3}";
        }

        // ══════════════════════════════════════════════════════════════════
        //  SAVE
        // ══════════════════════════════════════════════════════════════════
        public static void SaveQuotation(QuotationDto q, int companyId)
        {
            if (!File.Exists(_dbPath)) return;
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO Quotations
                        (QuotationNo,CompanyID,CustomerName,CustomerAddress,CustomerVat,
                         QuoteDate,ValidUntil,GrandTotal,Subtotal,TaxTotal,
                         DiscountTotal,CurrencySymbol,Status,Notes)
                    VALUES
                        (@qno,@cid,@cust,@addr,@vat,
                         @qdate,@valid,@grand,@sub,@tax,
                         @disc,@sym,'Open',@notes);";
                cmd.Parameters.AddWithValue("@qno", q.QuotationNo);
                cmd.Parameters.AddWithValue("@cid", companyId);
                cmd.Parameters.AddWithValue("@cust", q.CustomerName);
                cmd.Parameters.AddWithValue("@addr", q.CustomerAddress ?? "");
                cmd.Parameters.AddWithValue("@vat", q.CustomerVat ?? "");
                cmd.Parameters.AddWithValue("@qdate", q.QuoteDate.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@valid", q.ValidUntil?.ToString("yyyy-MM-dd") ?? "");
                cmd.Parameters.AddWithValue("@grand", (double)q.GrandTotal);
                cmd.Parameters.AddWithValue("@sub", (double)q.Subtotal);
                cmd.Parameters.AddWithValue("@tax", (double)q.TaxTotal);
                cmd.Parameters.AddWithValue("@disc", (double)q.DiscountTotal);
                cmd.Parameters.AddWithValue("@sym", q.CurrencySymbol);
                cmd.Parameters.AddWithValue("@notes", q.Notes ?? "");
                cmd.ExecuteNonQuery();
            }

            for (int i = 0; i < q.Lines.Count; i++)
            {
                var ln = q.Lines[i];
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO QuotationLines
                        (QuotationNo,LineNo,StockCode,Description,UOM,
                         Qty,UnitPrice,DiscountPct,LineTotal,PriceGroup)
                    VALUES
                        (@qno,@ln,@sc,@desc,@uom,
                         @qty,@price,@disc,@total,@pg);";
                cmd.Parameters.AddWithValue("@qno", q.QuotationNo);
                cmd.Parameters.AddWithValue("@ln", i + 1);
                cmd.Parameters.AddWithValue("@sc", ln.StockCode ?? "");
                cmd.Parameters.AddWithValue("@desc", ln.Description);
                cmd.Parameters.AddWithValue("@uom", ln.UOM ?? "Ea");
                cmd.Parameters.AddWithValue("@qty", (double)ln.Qty);
                cmd.Parameters.AddWithValue("@price", (double)ln.UnitPrice);
                cmd.Parameters.AddWithValue("@disc", (double)ln.DiscountPct);
                cmd.Parameters.AddWithValue("@total", (double)ln.LineTotal);
                cmd.Parameters.AddWithValue("@pg", ln.PriceGroup ?? "");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // ══════════════════════════════════════════════════════════════════
        //  LIST
        // ══════════════════════════════════════════════════════════════════
        public static List<QuotationRow> GetQuotations(int companyId,
            string statusFilter = "Open")
        {
            var list = new List<QuotationRow>();
            if (!File.Exists(_dbPath)) return list;

            using var conn = Open();
            using var cmd = conn.CreateCommand();

            bool all = string.IsNullOrEmpty(statusFilter) || statusFilter == "All";
            cmd.CommandText = all
                ? @"SELECT QuotationNo,CustomerName,QuoteDate,GrandTotal,
                          Status,ConvertedInvNo,ValidUntil,CurrencySymbol
                   FROM Quotations WHERE CompanyID=@cid ORDER BY QuoteDate DESC;"
                : @"SELECT QuotationNo,CustomerName,QuoteDate,GrandTotal,
                          Status,ConvertedInvNo,ValidUntil,CurrencySymbol
                   FROM Quotations WHERE CompanyID=@cid AND Status=@st
                   ORDER BY QuoteDate DESC;";
            cmd.Parameters.AddWithValue("@cid", companyId);
            if (!all) cmd.Parameters.AddWithValue("@st", statusFilter);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new QuotationRow
                {
                    QuotationNo = r[0]?.ToString() ?? "",
                    CustomerName = r[1]?.ToString() ?? "",
                    QuoteDate = DateTime.TryParse(r[2]?.ToString(), out var dt) ? dt : DateTime.Now,
                    GrandTotal = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
                    Status = r[4]?.ToString() ?? "Open",
                    ConvertedInvNo = r[5]?.ToString() ?? "",
                    ValidUntil = r[6]?.ToString() ?? "",
                    CurrencySymbol = r[7]?.ToString() ?? "P"
                });

            return list;
        }

        // ══════════════════════════════════════════════════════════════════
        //  LINES
        // ══════════════════════════════════════════════════════════════════
        public static List<QuotationLineRow> GetLines(string quotationNo)
        {
            var list = new List<QuotationLineRow>();
            if (!File.Exists(_dbPath)) return list;

            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT LineNo,StockCode,Description,UOM,Qty,
                       UnitPrice,DiscountPct,LineTotal,PriceGroup
                FROM QuotationLines WHERE QuotationNo=@qno ORDER BY LineNo;";
            cmd.Parameters.AddWithValue("@qno", quotationNo);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new QuotationLineRow
                {
                    LineNo = Convert.ToInt32(r.GetValue(0)),
                    StockCode = r[1]?.ToString() ?? "",
                    Description = r[2]?.ToString() ?? "",
                    UOM = r[3]?.ToString() ?? "Ea",
                    Qty = r.IsDBNull(4) ? 1m : Convert.ToDecimal(r.GetValue(4)),
                    UnitPrice = r.IsDBNull(5) ? 0m : Convert.ToDecimal(r.GetValue(5)),
                    DiscountPct = r.IsDBNull(6) ? 0m : Convert.ToDecimal(r.GetValue(6)),
                    LineTotal = r.IsDBNull(7) ? 0m : Convert.ToDecimal(r.GetValue(7)),
                    PriceGroup = r[8]?.ToString() ?? ""
                });

            return list;
        }

        // ══════════════════════════════════════════════════════════════════
        //  FULL DTO
        // ══════════════════════════════════════════════════════════════════
        public static QuotationDto? GetFull(string quotationNo)
        {
            if (!File.Exists(_dbPath)) return null;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT QuotationNo,CustomerName,CustomerAddress,CustomerVat,
                       QuoteDate,ValidUntil,GrandTotal,Subtotal,TaxTotal,
                       DiscountTotal,CurrencySymbol,Status,Notes,ConvertedInvNo
                FROM Quotations WHERE QuotationNo=@qno LIMIT 1;";
            cmd.Parameters.AddWithValue("@qno", quotationNo);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var dto = new QuotationDto
            {
                QuotationNo = r[0]?.ToString() ?? "",
                CustomerName = r[1]?.ToString() ?? "",
                CustomerAddress = r[2]?.ToString() ?? "",
                CustomerVat = r[3]?.ToString() ?? "",
                QuoteDate = DateTime.TryParse(r[4]?.ToString(), out var dt) ? dt : DateTime.Now,
                ValidUntil = DateTime.TryParse(r[5]?.ToString(), out var vt) ? vt : (DateTime?)null,
                GrandTotal = r.IsDBNull(6) ? 0m : Convert.ToDecimal(r.GetValue(6)),
                Subtotal = r.IsDBNull(7) ? 0m : Convert.ToDecimal(r.GetValue(7)),
                TaxTotal = r.IsDBNull(8) ? 0m : Convert.ToDecimal(r.GetValue(8)),
                DiscountTotal = r.IsDBNull(9) ? 0m : Convert.ToDecimal(r.GetValue(9)),
                CurrencySymbol = r[10]?.ToString() ?? "P",
                Status = r[11]?.ToString() ?? "Open",
                Notes = r[12]?.ToString() ?? "",
                ConvertedInvNo = r[13]?.ToString() ?? ""
            };
            r.Close();

            dto.Lines = GetLines(quotationNo)
                .Select(l => new QuotationLineDto
                {
                    StockCode = l.StockCode,
                    Description = l.Description,
                    UOM = l.UOM,
                    Qty = l.Qty,
                    UnitPrice = l.UnitPrice,
                    DiscountPct = l.DiscountPct,
                    PriceGroup = l.PriceGroup
                }).ToList();

            return dto;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONVERT TO SALE
        //  • Generates INV number atomically
        //  • Saves to SOInvoice / SOInvoiceLine via SalesRepository.SaveSale
        //  • Marks quotation Converted
        //  • Returns the new INV number
        // ══════════════════════════════════════════════════════════════════
        public static string ConvertToSale(
            string quotationNo, int companyId,
            decimal paidCash, decimal paidDigital, decimal paidCard,
            string cashierName, string currencySymbol,
            string companyName = "", string companyAddress = "",
            string companyPhone = "", string companyVat = "",
            string companyWebsite = "", string salesOfficeInfo = "")
        {
            if (!File.Exists(_dbPath))
                throw new Exception("Database not found.");

            var dto = GetFull(quotationNo)
                ?? throw new Exception($"Quotation {quotationNo} not found.");

            if (dto.Status != "Open")
                throw new Exception($"Quotation {quotationNo} is already {dto.Status}.");

            // ── Generate INV number FIRST (atomic, uses SalesRepository counter) ──
            string invNo = SalesRepository.NextInvoiceNo();
            SalesRepository.ConsumeInvoiceNo();   // advance the counter

            // ── Build ReceiptData ──────────────────────────────────────────────
            var receipt = new POSAPP.Invoice.ReceiptData
            {
                InvoiceNo = invNo,
                CompanyName = companyName,
                CompanyAddress = companyAddress,
                CompanyPhone = companyPhone,
                CompanyVat = companyVat,
                CompanyWebsite = companyWebsite,
                SalesOfficeInfo = salesOfficeInfo,
                CustomerName = dto.CustomerName,
                CustomerAddress = dto.CustomerAddress,
                CustomerVat = dto.CustomerVat,
                CashierName = cashierName,
                SaleDate = DateTime.Now,
                CurrencySymbol = currencySymbol,
                Subtotal = dto.Subtotal,
                DiscountTotal = dto.DiscountTotal,
                TaxTotal = dto.TaxTotal,
                GrandTotal = dto.GrandTotal,
                PaidCash = paidCash,
                PaidDigital = paidDigital,
                DigitalMethodName = "Bank Transfer",
                PaidCard = paidCard,
                Change = (paidCash + paidDigital + paidCard) - dto.GrandTotal
            };

            foreach (var l in dto.Lines)
                receipt.Lines.Add(new POSAPP.Invoice.ReceiptLine
                {
                    StockCode = l.StockCode,
                    Name = l.Description,
                    Qty = (int)l.Qty,
                    UnitPrice = l.UnitPrice,
                    DiscountPct = l.DiscountPct,
                    LineTotal = l.LineTotal,
                    UOM = l.UOM
                });

            // ── Save sale (uses its own connection — safe) ─────────────────────
            SalesRepository.SaveSale(receipt, companyId);

            // ── Mark quotation Converted (separate connection, no conflict) ───
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Quotations
                SET Status='Converted', ConvertedInvNo=@inv, ConvertedAt=@at
                WHERE QuotationNo=@qno;";
            cmd.Parameters.AddWithValue("@inv", invNo);
            cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@qno", quotationNo);
            cmd.ExecuteNonQuery();

            return invNo;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DELETE
        // ══════════════════════════════════════════════════════════════════
        public static void DeleteQuotation(string quotationNo)
        {
            if (!File.Exists(_dbPath)) return;
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            foreach (string sql in new[]
            {
                "DELETE FROM QuotationLines WHERE QuotationNo=@qno;",
                "DELETE FROM Quotations      WHERE QuotationNo=@qno;"
            })
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@qno", quotationNo);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private static SQLiteConnection Open()
        {
            var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
            conn.Open();
            using var p = conn.CreateCommand();
            p.CommandText = "PRAGMA journal_mode=WAL;";
            p.ExecuteNonQuery();
            return conn;
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────
    public class QuotationDto
    {
        public string QuotationNo { get; set; } = "";
        public string CustomerName { get; set; } = "Walk-in";
        public string CustomerAddress { get; set; } = "";
        public string CustomerVat { get; set; } = "";
        public DateTime QuoteDate { get; set; } = DateTime.Now;
        public DateTime? ValidUntil { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal Subtotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public string CurrencySymbol { get; set; } = "P";
        public string Status { get; set; } = "Open";
        public string Notes { get; set; } = "";
        public string ConvertedInvNo { get; set; } = "";
        public List<QuotationLineDto> Lines { get; set; } = new();
    }

    public class QuotationLineDto
    {
        public string StockCode { get; set; } = "";
        public string Description { get; set; } = "";
        public string UOM { get; set; } = "Ea";
        public decimal Qty { get; set; } = 1m;
        public decimal UnitPrice { get; set; }
        public decimal DiscountPct { get; set; }
        public decimal LineTotal => Math.Round(UnitPrice * Qty * (1m - DiscountPct / 100m), 2);
        public string PriceGroup { get; set; } = "";
    }

    public class QuotationRow
    {
        public string QuotationNo { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public DateTime QuoteDate { get; set; }
        public decimal GrandTotal { get; set; }
        public string Status { get; set; } = "Open";
        public string ConvertedInvNo { get; set; } = "";
        public string ValidUntil { get; set; } = "";
        public string CurrencySymbol { get; set; } = "P";
    }

    public class QuotationLineRow
    {
        public int LineNo { get; set; }
        public string StockCode { get; set; } = "";
        public string Description { get; set; } = "";
        public string UOM { get; set; } = "Ea";
        public decimal Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPct { get; set; }
        public decimal LineTotal { get; set; }
        public string PriceGroup { get; set; } = "";
    }
}