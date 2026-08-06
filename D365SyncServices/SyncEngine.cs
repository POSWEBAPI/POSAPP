
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;

namespace D365SyncService
{
    /// <summary>
    /// Stateless sync engine: fetches from D365 API and upserts into ShriPOS.db.
    /// Used by SyncService (timer) and can also be called directly for testing.
    /// </summary>
    internal sealed class SyncEngine
    {
        private readonly string _dbPath;
        private readonly string _configFile;
        private readonly int _storeId;
        private readonly Action<string> _log;

        public SyncEngine(string dbPath, string configFile,
                          int storeId, Action<string> log)
        {
            _dbPath     = dbPath;
            _configFile = configFile;
            _storeId    = storeId;
            _log        = log;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Public entry point
        // ══════════════════════════════════════════════════════════════════════
        public void SyncNow()
        {
            _log("Sync cycle starting…");

            string apiBase = ReadApiBaseUrl();
            _log($"API base: {apiBase}");

            var details = FetchFromApi(apiBase);

            if (details == null)
            {
                _log("API unreachable or returned an error — skipping this cycle.");
                return;
            }

            if (details.Count == 0)
            {
                _log("API returned 0 products — nothing to write.");
                return;
            }

            int rows = UpsertToSQLite(details);
            _log($"Sync complete — {details.Count} products, " +
                 $"{rows} detail row(s) written at {DateTime.Now:HH:mm:ss}.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Read API URL from config.txt  (same file POSAPP uses)
        // ══════════════════════════════════════════════════════════════════════
        private string ReadApiBaseUrl()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    string url = File.ReadAllText(_configFile).Trim();
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        url = "https://" + url;

                    if (Uri.TryCreate(url, UriKind.Absolute, out _))
                        return url.TrimEnd('/');
                }
            }
            catch (Exception ex) { _log($"config.txt read error: {ex.Message}"); }

            return "https://shriposapi.mythitsolutions.co.in";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HTTP fetch from /api/Product
        // ══════════════════════════════════════════════════════════════════════
        private Dictionary<string, List<SyncDetail>> FetchFromApi(string apiBase)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
                string url = $"{apiBase}/api/Product";
                _log($"GET {url}");

                var response = http.GetAsync(url).GetAwaiter().GetResult();

                _log($"HTTP Status: {(int)response.StatusCode} {response.ReasonPhrase}");

                if (!response.IsSuccessStatusCode)
                {
                    string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    _log($"Error Body: {error.Substring(0, Math.Min(200, error.Length))}");
                    return null;
                }

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                _log($"Received JSON length: {json.Length} characters");
                var result = ParseJson(json);
                return result;
            }
            catch (Exception ex)
            {
                _log($"FetchFromApi error: {ex.GetType().Name} - {ex.Message}");
                return null;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  JSON parser — same field names as POSAPP
        // ══════════════════════════════════════════════════════════════════════
        // ══════════════════════════════════════════════════════════════════════
        // JSON parser — same safe style as your working POSAPP code
        // ══════════════════════════════════════════════════════════════════════
        private Dictionary<string, List<SyncDetail>> ParseJson(string json)
        {
            var result = new Dictionary<string, List<SyncDetail>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                _log("Starting JSON parsing...");

                var root = JObject.Parse(json);

                var values = root["value"] as JArray;

                _log("Starting JSON parsing...");

                if (values == null)
                {
                    _log("JSON response has no 'value' array.");
                    return result;
                }

                int parsedCount = 0;

                _log("Starting JSON parsing...");

                foreach (var item in values)
                {

                    _log("Starting JSON parsing..."+item);
                    string itemId = item["ItemId"]?.ToString();
                    string name = item["NameAlias"]?.ToString();

                    if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!result.ContainsKey(itemId))
                        result[itemId] = new List<SyncDetail>();

                    result[itemId].Add(new SyncDetail
                    {
                        DataAreaId = item["dataAreaId"]?.ToString(),
                        ItemId = itemId,
                        NameAlias = name,
                        OnHandModifiedDateTime = item["OnHandModifiedDateTime"]?.ToString(),
                        AvailPhysical = item["AvailPhysical"]?.ToObject<decimal>() ?? 0,
                        InventLocationId = item["InventLocationId"]?.ToString(),
                        Amount = item["Amount"]?.ToObject<decimal>() ?? 0,
                        InventSiteId = item["InventSiteId"]?.ToString(),
                        WMSLocationId = item["wMSLocationId"]?.ToString(),
                        AccountRelation = item["AccountRelation"]?.ToString(),
                        ODataEtag = item["@odata.etag"]?.ToString(),
                    });

                    parsedCount++;
                }

                _log($"Parsed {parsedCount} rows, {result.Count} products.");
            }
            catch (Exception ex)
            {
                _log($"ParseJson error: {ex}");
            }

            return result;
        }

        // Helper methods (safe like your POS code)
        private static string GetPropertyString(JObject element, string propertyName)
        {
            if (element.TryGetValue(propertyName, out var value) &&
                value.Type == JTokenType.String)
                return value.ToString() ?? "";
            return "";
        }

        private static decimal GetPropertyDecimal(JObject element, string propertyName)
        {
            if (element.TryGetValue(propertyName, out var value) &&
                value.Type == JTokenType.Float)
                return value.ToObject<decimal>();

            return 0m;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SQLite upsert
        //  • WAL journal mode → SalesForm can READ while we WRITE
        //  • Etag check → only rows that actually changed are written
        //  • Returns count of detail rows written (0 = everything up to date)
        // ══════════════════════════════════════════════════════════════════════
        private int UpsertToSQLite(Dictionary<string, List<SyncDetail>> details)
        {
            if (!File.Exists(_dbPath))
            {
                _log($"Database not found at {_dbPath} — skipping write.");
                return -1;
            }

            int upserted = 0;

            using var conn = new SQLiteConnection(
                $"Data Source={_dbPath};Version=3;");
            conn.Open();

            // WAL mode allows concurrent reads from POSAPP
            using (var p = conn.CreateCommand())
            { p.CommandText = "PRAGMA journal_mode=WAL;"; p.ExecuteNonQuery(); }

            // ── Ensure all tables exist ────────────────────────────────────────
            using (var ddl = conn.CreateCommand())
            {
                ddl.CommandText = @"
                    CREATE TABLE IF NOT EXISTS D365Products (
                        ItemId       TEXT PRIMARY KEY,
                        NameAlias    TEXT,
                        InventSiteId TEXT
                    );

                    CREATE TABLE IF NOT EXISTS D365ProductDetails (
                        RowId                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        DataAreaId             TEXT,
                        ItemId                 TEXT,
                        NameAlias              TEXT,
                        OnHandModifiedDateTime TEXT,
                        AvailPhysical          REAL,
                        InventLocationId       TEXT,
                        Amount                 REAL,
                        InventSiteId           TEXT,
                        WMSLocationId          TEXT,
                        AccountRelation        TEXT,
                        ODataEtag              TEXT,
                        UNIQUE(ItemId, InventLocationId, AccountRelation)
                    );

                    CREATE TABLE IF NOT EXISTS StoreStock (
                        PKStoreStockID INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemID         TEXT    NOT NULL,
                        StoreID        INTEGER NOT NULL DEFAULT 1,
                        OnHandQty      REAL    NOT NULL DEFAULT 0,
                        ReservedQty    REAL             DEFAULT 0,
                        LastSyncQty    REAL             DEFAULT 0,
                        UNIQUE(ItemID, StoreID)
                    );
                    CREATE INDEX IF NOT EXISTS IX_StoreStock_ItemStore
                        ON StoreStock(ItemID, StoreID);

                    CREATE TABLE IF NOT EXISTS POS_SyncControl (
                        SyncType         TEXT PRIMARY KEY,
                        LastSyncDateTime TEXT
                    );";
                ddl.ExecuteNonQuery();
            }

            // ── Load existing etags — skip rows that haven't changed ───────────
            var existingEtags = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT ItemId || '|' || IFNULL(InventLocationId,'') " +
                    "              || '|' || IFNULL(AccountRelation,''), " +
                    "       ODataEtag " +
                    "FROM   D365ProductDetails;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    existingEtags[r.IsDBNull(0) ? "" : r.GetString(0)] =
                                  r.IsDBNull(1) ? "" : r.GetString(1);
            }

            // ── Single transaction for all upserts ────────────────────────────
            using var tx = conn.BeginTransaction();

            foreach (var kvp in details)
            {
                string itemId = kvp.Key;
                var rows = kvp.Value;
                if (rows.Count == 0) continue;

                // Master product
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO D365Products (ItemId, NameAlias, InventSiteId)
                        VALUES (@id, @name, @site)
                        ON CONFLICT(ItemId) DO UPDATE SET
                            NameAlias    = excluded.NameAlias,
                            InventSiteId = excluded.InventSiteId;";
                    cmd.Parameters.AddWithValue("@id",   itemId);
                    cmd.Parameters.AddWithValue("@name", rows[0].NameAlias);
                    cmd.Parameters.AddWithValue("@site", rows[0].InventSiteId);
                    cmd.ExecuteNonQuery();
                }

                // Detail rows
                foreach (var d in rows)
                {
                    string key = $"{d.ItemId}|{d.InventLocationId}|{d.AccountRelation}";

                    if (!string.IsNullOrWhiteSpace(d.ODataEtag) &&
                        existingEtags.TryGetValue(key, out string existing) &&
                        existing == d.ODataEtag)
                        continue;   // unchanged — skip write

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO D365ProductDetails
                            (DataAreaId, ItemId, NameAlias, OnHandModifiedDateTime,
                             AvailPhysical, InventLocationId, Amount,
                             InventSiteId, WMSLocationId, AccountRelation, ODataEtag)
                        VALUES
                            (@da,@id,@name,@ohd,@avail,@loc,@amt,@site,@wms,@acct,@etag)
                        ON CONFLICT(ItemId, InventLocationId, AccountRelation) DO UPDATE SET
                            NameAlias              = excluded.NameAlias,
                            OnHandModifiedDateTime = excluded.OnHandModifiedDateTime,
                            AvailPhysical          = excluded.AvailPhysical,
                            Amount                 = excluded.Amount,
                            InventSiteId           = excluded.InventSiteId,
                            WMSLocationId          = excluded.WMSLocationId,
                            ODataEtag              = excluded.ODataEtag;";
                    cmd.Parameters.AddWithValue("@da",    d.DataAreaId);
                    cmd.Parameters.AddWithValue("@id",    d.ItemId);
                    cmd.Parameters.AddWithValue("@name",  d.NameAlias);
                    cmd.Parameters.AddWithValue("@ohd",   d.OnHandModifiedDateTime);
                    cmd.Parameters.AddWithValue("@avail", (double)d.AvailPhysical);
                    cmd.Parameters.AddWithValue("@loc",   d.InventLocationId);
                    cmd.Parameters.AddWithValue("@amt",   (double)d.Amount);
                    cmd.Parameters.AddWithValue("@site",  d.InventSiteId);
                    cmd.Parameters.AddWithValue("@wms",   d.WMSLocationId);
                    cmd.Parameters.AddWithValue("@acct",  d.AccountRelation);
                    cmd.Parameters.AddWithValue("@etag",  d.ODataEtag);
                    cmd.ExecuteNonQuery();
                    upserted++;
                }

                // StoreStock — refresh on-hand for this item
                decimal totalAvail = rows.Sum(d => d.AvailPhysical);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO StoreStock (ItemID, StoreID, OnHandQty, LastSyncQty)
                        VALUES (@item, @store, @qty, @qty)
                        ON CONFLICT(ItemID, StoreID) DO UPDATE SET
                            OnHandQty   = excluded.OnHandQty,
                            LastSyncQty = excluded.LastSyncQty;";
                    // ReservedQty is intentionally NOT reset — local sales accumulate it
                    cmd.Parameters.AddWithValue("@item",  itemId);
                    cmd.Parameters.AddWithValue("@store", _storeId);
                    cmd.Parameters.AddWithValue("@qty",   (double)totalAvail);
                    cmd.ExecuteNonQuery();
                }
            }

            // Sync-control timestamp
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO POS_SyncControl (SyncType, LastSyncDateTime)
                    VALUES ('D365SyncService', @dt)
                    ON CONFLICT(SyncType) DO UPDATE SET
                        LastSyncDateTime = excluded.LastSyncDateTime;";
                cmd.Parameters.AddWithValue("@dt",
                    DateTime.Now.ToString("dd-MM-yyyy HH:mm"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return upserted;
        }

        // ── JSON helpers ──────────────────────────────────────────────────────
        private static string Str(JObject e, string key) =>
            e.TryGetValue(key, out var v) &&
            v.Type == JTokenType.String ? v.ToString() ?? "" : "";

        private static decimal Dec(JObject e, string key) =>
            e.TryGetValue(key, out var v) &&
            v.Type == JTokenType.Float ? v.ToObject<decimal>() : 0m;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Internal DTO
    // ══════════════════════════════════════════════════════════════════════════
    internal sealed class SyncDetail
    {
        public string  DataAreaId             { get; set; } = "";
        public string  ItemId                 { get; set; } = "";
        public string  NameAlias              { get; set; } = "";
        public string  OnHandModifiedDateTime { get; set; } = "";
        public decimal AvailPhysical          { get; set; }
        public string  InventLocationId       { get; set; } = "";
        public decimal Amount                 { get; set; }
        public string  InventSiteId           { get; set; } = "";
        public string  WMSLocationId          { get; set; } = "";
        public string  AccountRelation        { get; set; } = "";
        public string  ODataEtag              { get; set; } = "";
    }
}
