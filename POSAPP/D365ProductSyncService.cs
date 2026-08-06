// D365ProductSyncService.cs
using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace POSAPP.Sync
{
    public class D365ProductSyncService
    {
        private readonly System.Timers.Timer _timer;
        private readonly string _dbPath;
        private readonly string _apiBaseUrl;
        private readonly int _storeId = 1;
        private bool? _onlineCache = null;
        private Dictionary<string, List<D365ProductDetail>> _d365Details =
new Dictionary<string, List<D365ProductDetail>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Product> _barcodeMap =
            new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        private List<Product> _catalog = new List<Product>();
        internal readonly int _companyId;
        private DateTime _onlineChecked = DateTime.MinValue;
        private const int ONLINE_CACHE_SECONDS = 30;

        public D365ProductSyncService(string dbPath, string apiBaseUrl)
        {
            _dbPath = dbPath;
            _apiBaseUrl = apiBaseUrl;
            _timer = new System.Timers.Timer(10 * 60 * 1000); // 10 minutes
            _timer.Elapsed += async (s, e) => await SyncAsync();
            _timer.AutoReset = true;
        }

        public void Start()
        {
            _timer.Start();
            _ = SyncAsync(); // initial sync
            Console.WriteLine("D365 Sync Service started.");
        }

        public void Stop() => _timer.Stop(); 

        private async Task<string> GetAccessTokenAsync()
        {
            // Your existing token logic
            using var http = new HttpClient();
            string tenantId = "91ce49f5-eaf7-4049-8242-435e862944ed";
            string clientId = "fa1f34ba-85db-4efc-b111-e5c1f82b81af";
            string clientSecret = "FP~8Q~xRu.cKKfsWBAe06OV.AvFDKn9Kv0TVzaoL";
            string scope = "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com/.default";

            var url = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            var body = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "scope", scope },
                { "grant_type", "client_credentials" }
            };

            var content = new FormUrlEncodedContent(body);
            var resp = await http.PostAsync(url, content);
            if (!resp.IsSuccessStatusCode) return null;

            var tokenResp = JsonSerializer.Deserialize<TokenResponse>(await resp.Content.ReadAsStringAsync());
            return tokenResp?.access_token;
        }
        private async Task SyncAsync()
        {
            try
            {
                Console.WriteLine($"[{DateTime.Now}] Starting D365 sync...");

                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                string token = await GetAccessTokenAsync();
                if (string.IsNullOrWhiteSpace(token))
                {
                    Console.WriteLine("Failed to get access token.");
                    return;
                }

                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                const string d365Url = "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com/data/ProductPriceOnhandViews?cross-company=true";

                var response = await http.GetAsync(d365Url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"HTTP Error: {(int)response.StatusCode}");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);

                // Handle wrapped string case if needed
                if (root.ValueKind == JsonValueKind.String)
                {
                    string inner = root.GetString() ?? "{}";
                    root = JsonSerializer.Deserialize<JsonElement>(inner);
                }

                if (!root.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("No 'value' array in response.");
                    return;
                }

                var details = ParseD365Products(values);

                if (details.Count == 0)
                {
                    Console.WriteLine("No products parsed.");
                    return;
                }

                SyncD365ToSQLite(details);
                Console.WriteLine($"[{DateTime.Now}] Sync completed successfully. {details.Count} products processed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sync error: {ex.Message}");
            }
        }

        private void ShowStatus(string msg, bool ok)
        {
            //if (lblStatus.IsDisposed) return;
            //if (lblStatus.InvokeRequired)
            //    lblStatus.BeginInvoke(new Action(() =>
            //    {
            //        if (!lblStatus.IsDisposed)
            //        { lblStatus.Text = msg; lblStatus.ForeColor = ok ? TextGreen : AccRed; }
            //    }));
            //else
            //{ lblStatus.Text = msg; lblStatus.ForeColor = ok ? TextGreen : AccRed; }
        }
        private Dictionary<string, List<D365ProductDetail>> ParseD365Products(JsonElement values)
        {
            var dict = new Dictionary<string, List<D365ProductDetail>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in values.EnumerateArray())
            {
                string itemId = item.TryGetProperty("ItemId", out var id)
                    ? id.GetString() ?? "" : "";

                string name = item.TryGetProperty("NameAlias", out var na)
                    ? na.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(itemId))
                    continue;

                string dataArea = item.TryGetProperty("dataAreaId", out var da)
                    ? da.GetString() ?? "" : "";

                string site = item.TryGetProperty("InventSiteId", out var si)
                    ? si.GetString() ?? "" : "";

                string location = item.TryGetProperty("InventLocationId", out var lo)
                    ? lo.GetString() ?? "" : "";

                string wms = item.TryGetProperty("wMSLocationId", out var wm)
                    ? wm.GetString() ?? "" : "";

                string acct = item.TryGetProperty("AccountRelation", out var ar)
                    ? ar.GetString() ?? "" : "";

                string etag = item.TryGetProperty("@odata.etag", out var et)
                    ? et.GetString() ?? "" : "";

                string onHand = item.TryGetProperty("OnHandModifiedDateTime", out var oh)
                    ? oh.GetString() ?? "" : "";

                decimal price = 0m;
                if (item.TryGetProperty("Amount", out var amt) && amt.ValueKind == JsonValueKind.Number)
                    price = amt.GetDecimal();

                decimal avail = 0m;
                if (item.TryGetProperty("AvailPhysical", out var av) && av.ValueKind == JsonValueKind.Number)
                    avail = av.GetDecimal();

                // Initialize list if not exists
                if (!dict.ContainsKey(itemId))
                    dict[itemId] = new List<D365ProductDetail>();

                dict[itemId].Add(new D365ProductDetail
                {
                    DataAreaId = dataArea,
                    ItemId = itemId,
                    NameAlias = name,
                    OnHandModifiedDateTime = onHand,
                    AvailPhysical = avail,
                    InventLocationId = location,
                    Amount = price,
                    InventSiteId = site,
                    WMSLocationId = wms,
                    AccountRelation = acct,
                    ODataEtag = etag
                });
            }

            return dict;
        }

        private void SyncD365ToSQLite(Dictionary<string, List<D365ProductDetail>> details)
        {

            if (!System.IO.File.Exists(_dbPath)) return;
            try
            {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();

                // ── DDL ───────────────────────────────────────────────────────────────
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

                // ── Read existing etags to skip unchanged rows ────────────────────────
                var existingEtags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT ItemId || '|' || IFNULL(InventLocationId,'') || '|' || IFNULL(AccountRelation,''), " +
                        "       ODataEtag " +
                        "FROM D365ProductDetails;";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        existingEtags[r.IsDBNull(0) ? "" : r.GetString(0)] =
                                        r.IsDBNull(1) ? "" : r.GetString(1);
                }

                using var tx = conn.BeginTransaction();
                int upsertedDetails = 0, upsertedStock = 0;

                foreach (var kvp in details)
                {
                    string itemId = kvp.Key;
                    var rows = kvp.Value;
                    if (rows.Count == 0) continue;

                    // ── Master product ─────────────────────────────────────────────────
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                    INSERT INTO D365Products (ItemId, NameAlias, InventSiteId)
                    VALUES (@id, @name, @site)
                    ON CONFLICT(ItemId) DO UPDATE SET
                        NameAlias    = excluded.NameAlias,
                        InventSiteId = excluded.InventSiteId;";
                        cmd.Parameters.AddWithValue("@id", itemId);
                        cmd.Parameters.AddWithValue("@name", rows[0].NameAlias);
                        cmd.Parameters.AddWithValue("@site", rows[0].InventSiteId);
                        cmd.ExecuteNonQuery();
                    }

                    // ── Detail rows ────────────────────────────────────────────────────
                    foreach (var d in rows)
                    {
                        string key = $"{d.ItemId}|{d.InventLocationId}|{d.AccountRelation}";
                        if (!string.IsNullOrWhiteSpace(d.ODataEtag) &&
                            existingEtags.TryGetValue(key, out string ex) &&
                            ex == d.ODataEtag)
                            continue;

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
                        cmd.Parameters.AddWithValue("@da", d.DataAreaId);
                        cmd.Parameters.AddWithValue("@id", d.ItemId);
                        cmd.Parameters.AddWithValue("@name", d.NameAlias);
                        cmd.Parameters.AddWithValue("@ohd", d.OnHandModifiedDateTime);
                        cmd.Parameters.AddWithValue("@avail", d.AvailPhysical);
                        cmd.Parameters.AddWithValue("@loc", d.InventLocationId);
                        cmd.Parameters.AddWithValue("@amt", d.Amount);
                        cmd.Parameters.AddWithValue("@site", d.InventSiteId);
                        cmd.Parameters.AddWithValue("@wms", d.WMSLocationId);
                        cmd.Parameters.AddWithValue("@acct", d.AccountRelation);
                        cmd.Parameters.AddWithValue("@etag", d.ODataEtag);
                        cmd.ExecuteNonQuery();
                        upsertedDetails++;
                    }

                    // ── StoreStock — sum AvailPhysical across all locations ───────────
                    //   OnHandQty  = total available from D365 (refreshed on every sync)
                    //   LastSyncQty = same snapshot — baseline for drift comparison
                    //   ReservedQty is intentionally NOT reset; it accumulates from sales
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
                        //  NOTE: ReservedQty is excluded from the UPDATE intentionally —
                        //  we never want a D365 sync to wipe out the local sales tally.
                        cmd.Parameters.AddWithValue("@item", itemId);
                        cmd.Parameters.AddWithValue("@store", _storeId);
                        cmd.Parameters.AddWithValue("@qty", (double)totalAvail);
                        cmd.ExecuteNonQuery();
                        upsertedStock++;
                    }
                }

                UpsertSyncControl(conn, tx as SQLiteTransaction, "SalesForm");
                tx.Commit();
                Debug.WriteLine(
                    $"SyncD365ToSQLite: {upsertedDetails} detail rows, {upsertedStock} stock rows upserted.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SyncD365ToSQLite: " + ex.Message);
            }
            // ... full implementation from the original file
        }
        private static void UpsertSyncControl(
       SQLiteConnection conn, SQLiteTransaction tx,
       string syncType)
        {
            string formatted = DateTime.Now.ToString("dd-MM-yyyy HH.mm");

            using var cmd = new SQLiteCommand(@"
        INSERT INTO POS_SyncControl (SyncType, LastSyncDateTime)
        VALUES (@type, @dt)
        ON CONFLICT(SyncType) DO UPDATE SET LastSyncDateTime = excluded.LastSyncDateTime",
                conn, tx);
            cmd.Parameters.AddWithValue("@type", syncType);
            cmd.Parameters.AddWithValue("@dt", formatted);
            cmd.ExecuteNonQuery();
        }


        public class TokenResponse { public string access_token { get; set; } }
    }

    // Keep D365ProductDetail class here too
          public class D365ProductDetail
        {
            public string DataAreaId { get; set; } = "";
            public string ItemId { get; set; } = "";
            public string NameAlias { get; set; } = "";
            public string OnHandModifiedDateTime { get; set; } = "";
            public decimal AvailPhysical { get; set; }
            public string InventLocationId { get; set; } = "";
            public decimal Amount { get; set; }
            public string InventSiteId { get; set; } = "";
            public string WMSLocationId { get; set; } = "";
            public string AccountRelation { get; set; } = "";
            public string ODataEtag { get; set; } = "";
        }
    public class Product
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Barcode { get; set; }
        public string Category { get; set; }
    }
}