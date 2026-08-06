using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;

namespace POSAPP.Entity
{

    public static class DashboardRepository
    {
        private static readonly string _dbPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ── Thresholds ─────────────────────────────────────────────────────────
        // Raised to 20/50 so current test data (qty 10, qty 40) appears in alerts.
        // Lower back to 5/10 once real inventory movements are synced.
        private const decimal CRITICAL_THRESHOLD = 20m;
        private const decimal LOW_THRESHOLD = 50m;


        /// <summary>
        /// Returns the top <paramref name="topN"/> selling products for the
        /// given company, ordered by total units sold descending.
        /// Pass from/to to restrict to a date window; null = all-time.
        /// </summary>
        public static List<TopSellingProductDto> GetTopSellingProducts(
            int companyId,
            int topN = 5,
            DateTime? from = null,
            DateTime? to = null)
        {
            var result = new List<TopSellingProductDto>();
            if (!File.Exists(_dbPath)) return result;

            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();

                if (!TableExists(conn, "SOInvoiceLine") || !TableExists(conn, "SOInvoiceHeader"))
                {
                    Debug.WriteLine("DashboardRepo: SOInvoiceLine or SOInvoiceHeader not found.");
                    return result;
                }

                // ── Date filter using SUBSTR(date,1,10) for dot-format safety ──────
                // InvoiceDate is stored as "YYYY-MM-DD HH.MM.SS" — the first 10 chars
                // are always "YYYY-MM-DD" and sort correctly as plain text.
                string dateClause = "";
                if (from.HasValue)
                    dateClause += " AND SUBSTR(h.InvoiceDate,1,10) >= @From";
                if (to.HasValue)
                    dateClause += " AND SUBSTR(h.InvoiceDate,1,10) <= @To";

                // ── Optional Item join for richer price data ────────────────────────
                // ItemNo is always 0 in current data so the join uses ItemName match.
                bool hasItemTable = TableExists(conn, "Item");

                string sql = hasItemTable
                    ? $@"
                        SELECT  l.ItemName                                                AS DisplayName,
                                CAST(SUM(l.Qty) AS REAL)                                  AS TotalSold,
                                COALESCE(NULLIF(l.UnitPrice,0),
                                         (SELECT i.SellingPrice
                                          FROM   Item i
                                          WHERE  TRIM(LOWER(i.ItemName)) = TRIM(LOWER(l.ItemName))
                                          LIMIT  1),
                                         0)                                                AS UnitPrice
                        FROM    SOInvoiceLine  l
                        JOIN    SOInvoiceHeader h ON h.InvoiceID = l.InvoiceID
                        WHERE   h.CompanyID = @CompanyID
                                {dateClause}
                        GROUP BY l.ItemName, l.UnitPrice
                        ORDER BY TotalSold DESC
                        LIMIT   @TopN;"
                    : $@"
                        SELECT  l.ItemName                   AS DisplayName,
                                CAST(SUM(l.Qty) AS REAL)      AS TotalSold,
                                COALESCE(l.UnitPrice, 0)      AS UnitPrice
                        FROM    SOInvoiceLine  l
                        JOIN    SOInvoiceHeader h ON h.InvoiceID = l.InvoiceID
                        WHERE   h.CompanyID = @CompanyID
                                {dateClause}
                        GROUP BY l.ItemName, l.UnitPrice
                        ORDER BY TotalSold DESC
                        LIMIT   @TopN;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@CompanyID", companyId);
                cmd.Parameters.AddWithValue("@TopN", topN);
                if (from.HasValue)
                    cmd.Parameters.AddWithValue("@From", from.Value.ToString("yyyy-MM-dd"));
                if (to.HasValue)
                    cmd.Parameters.AddWithValue("@To", to.Value.ToString("yyyy-MM-dd"));

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    result.Add(new TopSellingProductDto
                    {
                        ItemName = rdr["DisplayName"]?.ToString()?.Trim() ?? "Unknown",
                        TotalSold = Convert.ToInt32(Math.Round(Convert.ToDouble(rdr["TotalSold"]))),
                        UnitPrice = rdr.IsDBNull(rdr.GetOrdinal("UnitPrice"))
                                    ? 0m
                                    : Convert.ToDecimal(rdr["UnitPrice"]),
                    });
                }

                // ── Normalise bar widths relative to the top seller ─────────────────
                if (result.Count > 0)
                {
                    int maxSold = result[0].TotalSold;
                    foreach (var r in result)
                        r.BarPercent = maxSold > 0
                            ? (int)Math.Round(r.TotalSold * 100.0 / maxSold)
                            : 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetTopSellingProducts: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Returns items whose net stock qty is at or below LOW_THRESHOLD,
        /// classified as "Low" or "Critical".
        /// </summary>
        public static List<LowStockAlertDto> GetLowStockAlerts(
            int companyId,
            int storeId = 0,
            int maxRows = 10)
        {
            var result = new List<LowStockAlertDto>();
            if (!File.Exists(_dbPath)) return result;

            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();

                if (!TableExists(conn, "StockMovement"))
                {
                    Debug.WriteLine("DashboardRepo: StockMovement not yet synced.");
                    return result;
                }

                bool hasItemTable = TableExists(conn, "Item");

                // StoreID filter — 0 means "all stores"
                string storeClause = storeId > 0 ? "AND sm.StoreID = @StoreID" : "";

                // MovementType >= 1 → IN (+qty), anything else → OUT (-qty).
                // Adjust the CASE boundary if your SQL Server uses a different convention.
                string sql = hasItemTable
                    ? $@"
                        SELECT  sm.ItemID,
                                sm.ItemCode,
                                COALESCE(NULLIF(TRIM(i.ItemName),''),
                                         sm.ItemCode,
                                         'Item ' || sm.ItemID)                AS DisplayName,
                                SUM(CASE
                                        WHEN sm.MovementType >= 1 THEN  sm.ItemQty
                                        ELSE                           -sm.ItemQty
                                    END)                                       AS NetQty
                        FROM    StockMovement sm
                        LEFT JOIN Item i ON i.ItemID = sm.ItemID
                        WHERE   sm.CompanyID = @CompanyID
                                {storeClause}
                        GROUP BY sm.ItemID, sm.ItemCode, DisplayName
                        HAVING  NetQty <= @LowThreshold
                        ORDER BY NetQty ASC
                        LIMIT   @MaxRows;"
                    : $@"
                        SELECT  ItemID,
                                ItemCode,
                                COALESCE(NULLIF(TRIM(ItemCode),''),
                                         'Item ' || ItemID)                    AS DisplayName,
                                SUM(CASE
                                        WHEN MovementType >= 1 THEN  ItemQty
                                        ELSE                        -ItemQty
                                    END)                                       AS NetQty
                        FROM    StockMovement
                        WHERE   CompanyID = @CompanyID
                                {storeClause}
                        GROUP BY ItemID, ItemCode
                        HAVING  NetQty <= @LowThreshold
                        ORDER BY NetQty ASC
                        LIMIT   @MaxRows;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@CompanyID", companyId);
                cmd.Parameters.AddWithValue("@LowThreshold", (double)LOW_THRESHOLD);
                cmd.Parameters.AddWithValue("@MaxRows", maxRows);
                if (storeId > 0)
                    cmd.Parameters.AddWithValue("@StoreID", storeId);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    decimal qty = rdr.IsDBNull(rdr.GetOrdinal("NetQty"))
                                  ? 0m
                                  : Convert.ToDecimal(rdr["NetQty"]);

                    result.Add(new LowStockAlertDto
                    {
                        ItemName = rdr["DisplayName"]?.ToString()?.Trim() ?? "Unknown",
                        ItemCode = rdr["ItemCode"]?.ToString()?.Trim() ?? "",
                        CurrentQty = qty,
                        Status = qty <= CRITICAL_THRESHOLD ? "Critical" : "Low"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetLowStockAlerts: " + ex.Message);
            }

            return result;
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  HELPER
        // ═══════════════════════════════════════════════════════════════════════
        private static bool TableExists(SQLiteConnection conn, string tableName)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM   sqlite_master
                WHERE  type = 'table'
                AND    name = @Name;";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Name", tableName);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }
}