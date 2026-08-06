//// ╔══════════════════════════════════════════════════════════════════════════╗
//// ║  SalesForm_Products.cs  — PARTIAL CLASS                                 ║
//// ║                                                                          ║
//// ║  All product loading reads ONLY from the local ShriPOS.db.              ║
//// ║  Network sync has been moved to D365SyncService (Windows Service).      ║
//// ║                                                                          ║
//// ║  Removed from SalesForm:                                                 ║
//// ║    • SyncProductsFromApiInBackgroundAsync  (both overloads)              ║
//// ║    • LoadProductsFromD365Async             (API version)                 ║
//// ║    • SyncD365ToSQLite                      (write path)                  ║
//// ║    • GetAccessTokenAsync / TokenResponse                                 ║
//// ║                                                                          ║
//// ║  Kept / added:                                                            ║
//// ║    • LoadProductsFromD365SQLiteAsync       (read-only, unchanged)        ║
//// ║    • GetLastSyncDateTimeUtc / UpsertSyncControl (unchanged)              ║
//// ║    • GetStoreStock                         (unchanged)                   ║
//// ║    • UpdateStoreStockAfterSale             (unchanged)                   ║
//// ╚══════════════════════════════════════════════════════════════════════════╝

//using System;
//using System.Collections.Generic;
//using System.Data.SQLite;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Windows.Forms;

//namespace POSAPP
//{
//    public partial class SalesForm
//    {
//        // ══════════════════════════════════════════════════════════════════════
//        //  FAST LOCAL LOAD — called once from SalesForm_Load.
//        //  Reads D365Products + D365ProductDetails from ShriPOS.db.
//        //  No network traffic.  Typically completes in < 300 ms.
//        // ══════════════════════════════════════════════════════════════════════
//        private async Task LoadProductsFromD365SQLiteAsync()
//        {
//            try
//            {
//                ShowStatus("Loading products…", true);

//                if (!System.IO.File.Exists(_dbPath))
//                {
//                    ShowStatus($"Database not found: {_dbPath}", false);
//                    return;
//                }

//                var localMap       = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
//                var localCatalog   = new List<Product>();
//                var localDetailMap = new Dictionary<string, List<D365ProductDetail>>(StringComparer.OrdinalIgnoreCase);

//                using var conn = new SQLiteConnection(
//                    $"Data Source={_dbPath};Version=3;Foreign Keys=True;");
//                await conn.OpenAsync().ConfigureAwait(false);

//                // Enable WAL for concurrent reads with the sync service
//                using (var pragma = conn.CreateCommand())
//                {
//                    pragma.CommandText = "PRAGMA journal_mode=WAL;";
//                    await pragma.ExecuteNonQueryAsync().ConfigureAwait(false);
//                }

//                // Guard: tables may not exist on very first run (before any sync)
//                using (var chk = conn.CreateCommand())
//                {
//                    chk.CommandText =
//                        "SELECT COUNT(*) FROM sqlite_master " +
//                        "WHERE type='table' AND name='D365Products';";
//                    long exists = (long)(await chk.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
//                    if (exists == 0)
//                    {
//                        ShowStatus("No local product cache — D365SyncService has not run yet.", true);
//                        _isD365Mode = true;
//                      //  this.BeginInvoke(new Action(() => SetD365Mode(true)));
//                        return;
//                    }
//                }

//                // ── Master products ───────────────────────────────────────────
//                using (var cmd = conn.CreateCommand())
//                {
//                    cmd.CommandText =
//                        "SELECT ItemId, NameAlias, InventSiteId " +
//                        "FROM D365Products ORDER BY NameAlias;";

//                    using var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
//                    while (await rdr.ReadAsync().ConfigureAwait(false))
//                    {
//                        string itemId = rdr.IsDBNull(0) ? "" : Convert.ToString(rdr.GetValue(0)) ?? "";
//                        string name   = rdr.IsDBNull(1) ? "" : Convert.ToString(rdr.GetValue(1)) ?? "";
//                        string site   = rdr.IsDBNull(2) ? "" : Convert.ToString(rdr.GetValue(2)) ?? "";

//                        if (string.IsNullOrWhiteSpace(itemId)) continue;

//                        var prod   = new Product { Name = name, Barcode = itemId, Category = site, Price = 0m };
//                        string pad = itemId.PadLeft(13, '0');

//                        localCatalog.Add(prod);
//                        localMap[itemId] = prod;
//                        localMap[pad]    = prod;
//                        localDetailMap[itemId] = new List<D365ProductDetail>();
//                    }
//                }

//                if (localCatalog.Count == 0)
//                {
//                    ShowStatus("Local cache is empty — waiting for D365SyncService to run…", true);
//                    _isD365Mode = true;
//                   // this.BeginInvoke(new Action(() => SetD365Mode(true)));
//                    return;
//                }

//                // ── Detail rows (ORDER BY Amount → lowest price first) ─────────
//                using (var cmd = conn.CreateCommand())
//                {
//                    cmd.CommandText = @"
//                        SELECT DataAreaId, ItemId, NameAlias, OnHandModifiedDateTime,
//                               AvailPhysical, InventLocationId, Amount,
//                               InventSiteId, WMSLocationId, AccountRelation, ODataEtag
//                        FROM   D365ProductDetails
//                        ORDER  BY ItemId, Amount;";

//                    using var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
//                    while (await rdr.ReadAsync().ConfigureAwait(false))
//                    {
//                        string itemId = rdr.IsDBNull(1) ? "" : Convert.ToString(rdr.GetValue(1)) ?? "";
//                        if (!localDetailMap.ContainsKey(itemId)) continue;

//                        decimal availPhysical = 0m, amount = 0m;
//                        try { if (!rdr.IsDBNull(4)) availPhysical = Convert.ToDecimal(rdr.GetValue(4)); } catch { }
//                        try { if (!rdr.IsDBNull(6)) amount        = Convert.ToDecimal(rdr.GetValue(6)); } catch { }

//                        var detail = new D365ProductDetail
//                        {
//                            DataAreaId             = rdr.IsDBNull(0)  ? "" : Convert.ToString(rdr.GetValue(0))  ?? "",
//                            ItemId                 = itemId,
//                            NameAlias              = rdr.IsDBNull(2)  ? "" : Convert.ToString(rdr.GetValue(2))  ?? "",
//                            OnHandModifiedDateTime = rdr.IsDBNull(3)  ? "" : Convert.ToString(rdr.GetValue(3))  ?? "",
//                            AvailPhysical          = availPhysical,
//                            InventLocationId       = rdr.IsDBNull(5)  ? "" : Convert.ToString(rdr.GetValue(5))  ?? "",
//                            Amount                 = amount,
//                            InventSiteId           = rdr.IsDBNull(7)  ? "" : Convert.ToString(rdr.GetValue(7))  ?? "",
//                            WMSLocationId          = rdr.IsDBNull(8)  ? "" : Convert.ToString(rdr.GetValue(8))  ?? "",
//                            AccountRelation        = rdr.IsDBNull(9)  ? "" : Convert.ToString(rdr.GetValue(9))  ?? "",
//                            ODataEtag              = rdr.IsDBNull(10) ? "" : Convert.ToString(rdr.GetValue(10)) ?? "",
//                        };

//                        localDetailMap[itemId].Add(detail);

//                        // First row (lowest price) sets the Product.Price
//                        if (localMap.TryGetValue(itemId, out var prod) && prod.Price == 0m)
//                            prod.Price = detail.Amount;
//                    }
//                }

//                // ── Commit to in-memory state on background thread ────────────
//                _barcodeMap  = localMap;
//                _catalog     = localCatalog;
//                _d365Details = localDetailMap;
//                _isD365Mode  = true;
//                _useD365     = true;

//                // Show last-sync timestamp in status bar
//                string lastSync = GetLastSyncDisplay();

//                this.BeginInvoke(new Action(() =>
//                {
//                    BuildAutocomplete();
//                    BuildHotItems();
//                    //SetD365Mode(true);
//                    ShowStatus(
//                        $"✓ {localCatalog.Count} products loaded from local cache. Last sync: {lastSync}",
//                        true);
//                }));
//            }
//            catch (Exception ex)
//            {
//                ShowStatus("Cache load failed: " + ex.Message, false);
//                Debug.WriteLine("LoadProductsFromD365SQLiteAsync: " + ex);
//            }
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  SHOW LAST SYNC TIME — reads POS_SyncControl and formats nicely
//        // ══════════════════════════════════════════════════════════════════════
//        private string GetLastSyncDisplay()
//        {
//            try
//            {
//                if (!System.IO.File.Exists(_dbPath)) return "never";
//                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
//                conn.Open();
//                using var cmd = conn.CreateCommand();
//                cmd.CommandText =
//                    "SELECT LastSyncDateTime FROM POS_SyncControl " +
//                    "WHERE SyncType IN ('D365SyncService','SalesForm') " +
//                    "ORDER BY LastSyncDateTime DESC LIMIT 1;";
//                object val = cmd.ExecuteScalar();
//                if (val == null || val == DBNull.Value) return "never";
//                return val.ToString() ?? "never";
//            }
//            catch { return "unknown"; }
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  GetLastSyncDateTimeUtc — for any code still referencing it
//        // ══════════════════════════════════════════════════════════════════════
//        private DateTime GetLastSyncDateTimeUtc(string syncType)
//        {
//            try
//            {
//                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
//                conn.Open();
//                using var cmd = new SQLiteCommand(
//                    "SELECT LastSyncDateTime FROM POS_SyncControl WHERE SyncType = @t LIMIT 1;", conn);
//                cmd.Parameters.AddWithValue("@t", syncType);
//                object val = cmd.ExecuteScalar();

//                if (val != null && val != DBNull.Value)
//                {
//                    string stored = val.ToString();
//                    if (DateTime.TryParseExact(stored, "dd-MM-yyyy HH.mm",
//                            System.Globalization.CultureInfo.InvariantCulture,
//                            System.Globalization.DateTimeStyles.None, out DateTime local))
//                        return local.ToUniversalTime();

//                    if (DateTime.TryParse(stored, null,
//                            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime iso))
//                        return iso.ToUniversalTime();
//                }
//            }
//            catch (Exception ex) { Debug.WriteLine("GetLastSyncDateTimeUtc: " + ex.Message); }
//            return DateTime.MinValue;
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  UpsertSyncControl — kept for any code paths that still call it
//        // ══════════════════════════════════════════════════════════════════════
//        private static void UpsertSyncControl(
//            SQLiteConnection conn, SQLiteTransaction tx, string syncType)
//        {
//            string formatted = DateTime.Now.ToString("dd-MM-yyyy HH.mm");
//            using var cmd = new SQLiteCommand(@"
//                INSERT INTO POS_SyncControl (SyncType, LastSyncDateTime)
//                VALUES (@type, @dt)
//                ON CONFLICT(SyncType) DO UPDATE SET LastSyncDateTime = excluded.LastSyncDateTime;",
//                conn, tx);
//            cmd.Parameters.AddWithValue("@type", syncType);
//            cmd.Parameters.AddWithValue("@dt",   formatted);
//            cmd.ExecuteNonQuery();
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  GetStoreStock — unchanged
//        // ══════════════════════════════════════════════════════════════════════
//        private (decimal onHand, decimal reserved) GetStoreStock(string itemId)
//        {
//            if (string.IsNullOrWhiteSpace(itemId) || !System.IO.File.Exists(_dbPath))
//                return (0m, 0m);
//            try
//            {
//                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
//                conn.Open();
//                using var cmd = conn.CreateCommand();
//                cmd.CommandText =
//                    "SELECT OnHandQty, IFNULL(ReservedQty, 0) " +
//                    "FROM   StoreStock " +
//                    "WHERE  ItemID = @id AND StoreID = @store LIMIT 1;";
//                cmd.Parameters.AddWithValue("@id",    itemId);
//                cmd.Parameters.AddWithValue("@store", _storeId);
//                using var r = cmd.ExecuteReader();
//                if (r.Read())
//                {
//                    decimal onH = r.IsDBNull(0) ? 0m : Convert.ToDecimal(r.GetValue(0));
//                    decimal res = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
//                    return (onH, res);
//                }
//            }
//            catch (Exception ex) { Debug.WriteLine("GetStoreStock: " + ex.Message); }
//            return (0m, 0m);
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  UpdateStoreStockAfterSale — unchanged (local deduction only)
//        // ══════════════════════════════════════════════════════════════════════
//        private void UpdateStoreStockAfterSale()
//        {
//            if (_cart.Count == 0 || !System.IO.File.Exists(_dbPath)) return;
//            try
//            {
//                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
//                conn.Open();

//                using (var ddl = conn.CreateCommand())
//                {
//                    ddl.CommandText = @"
//                        CREATE TABLE IF NOT EXISTS StoreStock (
//                            PKStoreStockID INTEGER PRIMARY KEY AUTOINCREMENT,
//                            ItemID         TEXT    NOT NULL,
//                            StoreID        INTEGER NOT NULL DEFAULT 1,
//                            OnHandQty      REAL    NOT NULL DEFAULT 0,
//                            ReservedQty    REAL             DEFAULT 0,
//                            LastSyncQty    REAL             DEFAULT 0,
//                            UNIQUE(ItemID, StoreID)
//                        );
//                        CREATE INDEX IF NOT EXISTS IX_StoreStock_ItemStore
//                            ON StoreStock(ItemID, StoreID);";
//                    ddl.ExecuteNonQuery();
//                }

//                using var tx = conn.BeginTransaction();
//                foreach (var item in _cart)
//                {
//                    if (string.IsNullOrWhiteSpace(item.Barcode)) continue;
//                    using var cmd = conn.CreateCommand();
//                    cmd.Transaction = tx;
//                    cmd.CommandText = @"
//                        INSERT INTO StoreStock (ItemID, StoreID, OnHandQty, ReservedQty, LastSyncQty)
//                        VALUES (@item, @store,
//                                MAX(0.0, 0.0 - CAST(@qty AS REAL)),
//                                CAST(@qty AS REAL),
//                                0.0)
//                        ON CONFLICT(ItemID, StoreID) DO UPDATE SET
//                            OnHandQty   = MAX(0.0, OnHandQty   - CAST(@qty AS REAL)),
//                            ReservedQty = IFNULL(ReservedQty, 0.0) + CAST(@qty AS REAL);";
//                    cmd.Parameters.AddWithValue("@item",  item.Barcode);
//                    cmd.Parameters.AddWithValue("@store", _storeId);
//                    cmd.Parameters.AddWithValue("@qty",   (double)item.Qty);
//                    cmd.ExecuteNonQuery();
//                }
//                tx.Commit();
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine("UpdateStoreStockAfterSale: " + ex.Message);
//                ShowStatus("Stock update warning: " + ex.Message, false);
//            }
//        }
//    }
//}
