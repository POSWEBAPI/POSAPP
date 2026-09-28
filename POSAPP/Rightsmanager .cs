using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace POSAPP.Security
{
    public class MenuRightDto
    {
        [JsonPropertyName("menuId")] public int MenuId { get; set; }
        [JsonPropertyName("menuKey")] public string MenuKey { get; set; }
        [JsonPropertyName("canView")] public bool CanView { get; set; }
        [JsonPropertyName("canCreate")] public bool CanCreate { get; set; }
        [JsonPropertyName("canUpdate")] public bool CanUpdate { get; set; }
        [JsonPropertyName("canDelete")] public bool CanDelete { get; set; }
    }

    /// <summary>
    /// Loads the logged-in role's menu rights once at login, caches them in
    /// memory for the session, and mirrors them to SQLite so rights still
    /// apply when the POS runs offline.
    ///
    /// Call ONCE after login:
    ///     await RightsManager.LoadAsync(CurrentUser.RoleID, isOnline);
    ///
    /// Then anywhere:
    ///     if (RightsManager.CanView("/sales")) { ... }
    /// </summary>
    public static class RightsManager
    {
        // Keyed case-insensitively so casing drift between the DB seed and
        // the C# call sites can never silently deny access.
        private static Dictionary<string, MenuRightDto> _rights =
            new Dictionary<string, MenuRightDto>(StringComparer.OrdinalIgnoreCase);

        public static bool IsLoaded { get; private set; }

        private static string DbPath() =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ==============================================================
        // LOAD
        // ==============================================================
        public static async Task LoadAsync(int roleId, bool isOnline)
        {
            _rights.Clear();

            if (roleId <= 0)
            {
                IsLoaded = true;
                return;
            }

            List<MenuRightDto> rows = null;

            if (isOnline)
            {
                rows = await TryFetchFromApiAsync(roleId);
                if (rows != null)
                    CacheToSQLite(roleId, rows);   // mirror for offline runs
            }

            if (rows == null)
                rows = LoadFromSQLite(roleId);     // offline, or API failed

            _rights = (rows ?? new List<MenuRightDto>())
                .Where(r => !string.IsNullOrWhiteSpace(r.MenuKey))
                .GroupBy(r => r.MenuKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            IsLoaded = true;
            Debug.WriteLine($"RightsManager: loaded {_rights.Count} menu rights for role {roleId}");
        }

        public static void Clear()
        {
            _rights.Clear();
            IsLoaded = false;
        }

        // ==============================================================
        // CHECKS
        // ==============================================================
        public static bool CanView(string menuKey) =>
            _rights.TryGetValue(menuKey ?? "", out var r) && r.CanView;

        public static bool CanCreate(string menuKey) =>
            _rights.TryGetValue(menuKey ?? "", out var r) && r.CanCreate;

        public static bool CanUpdate(string menuKey) =>
            _rights.TryGetValue(menuKey ?? "", out var r) && r.CanUpdate;

        public static bool CanDelete(string menuKey) =>
            _rights.TryGetValue(menuKey ?? "", out var r) && r.CanDelete;

        // ==============================================================
        // API
        // ==============================================================
        private static async Task<List<MenuRightDto>> TryFetchFromApiAsync(int roleId)
        {
            try
            {
                var api = new ApiService();
                string json = await api.GetAsync($"api/rights/role/{roleId}");
                if (string.IsNullOrWhiteSpace(json)) return null;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var response = JsonSerializer.Deserialize<ApiResponse<List<MenuRightDto>>>(json, options);

                return response?.IsSuccess == true ? response.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RightsManager API fetch failed: " + ex.Message);
                return null;
            }
        }

        // ==============================================================
        // LOCAL SQLITE MIRROR
        // ==============================================================
        private static void EnsureSchema(SQLiteConnection conn)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS RoleMenuRightsCache (
                    RoleId    INTEGER NOT NULL,
                    MenuId    INTEGER NOT NULL,
                    MenuKey   TEXT    NOT NULL,
                    CanView   INTEGER NOT NULL DEFAULT 0,
                    CanCreate INTEGER NOT NULL DEFAULT 0,
                    CanUpdate INTEGER NOT NULL DEFAULT 0,
                    CanDelete INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (RoleId, MenuId)
                );";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        private static void CacheToSQLite(int roleId, List<MenuRightDto> rows)
        {
            try
            {
                using var conn = new SQLiteConnection($"Data Source={DbPath()};Version=3;");
                conn.Open();
                EnsureSchema(conn);

                using var tx = conn.BeginTransaction();

                using (var del = new SQLiteCommand(
                    "DELETE FROM RoleMenuRightsCache WHERE RoleId = @r;", conn, tx))
                {
                    del.Parameters.AddWithValue("@r", roleId);
                    del.ExecuteNonQuery();
                }

                const string ins = @"
                    INSERT INTO RoleMenuRightsCache
                        (RoleId, MenuId, MenuKey, CanView, CanCreate, CanUpdate, CanDelete)
                    VALUES (@r, @m, @k, @v, @c, @u, @d);";

                foreach (var row in rows)
                {
                    using var cmd = new SQLiteCommand(ins, conn, tx);
                    cmd.Parameters.AddWithValue("@r", roleId);
                    cmd.Parameters.AddWithValue("@m", row.MenuId);
                    cmd.Parameters.AddWithValue("@k", row.MenuKey ?? "");
                    cmd.Parameters.AddWithValue("@v", row.CanView ? 1 : 0);
                    cmd.Parameters.AddWithValue("@c", row.CanCreate ? 1 : 0);
                    cmd.Parameters.AddWithValue("@u", row.CanUpdate ? 1 : 0);
                    cmd.Parameters.AddWithValue("@d", row.CanDelete ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RightsManager CacheToSQLite failed: " + ex.Message);
            }
        }

        private static List<MenuRightDto> LoadFromSQLite(int roleId)
        {
            var list = new List<MenuRightDto>();
            try
            {
                if (!System.IO.File.Exists(DbPath())) return list;

                using var conn = new SQLiteConnection($"Data Source={DbPath()};Version=3;");
                conn.Open();
                EnsureSchema(conn);

                const string sql = @"
                    SELECT MenuId, MenuKey, CanView, CanCreate, CanUpdate, CanDelete
                    FROM RoleMenuRightsCache
                    WHERE RoleId = @r;";

                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@r", roleId);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    list.Add(new MenuRightDto
                    {
                        MenuId = Convert.ToInt32(rdr["MenuId"]),
                        MenuKey = rdr["MenuKey"]?.ToString() ?? "",
                        CanView = Convert.ToInt32(rdr["CanView"]) == 1,
                        CanCreate = Convert.ToInt32(rdr["CanCreate"]) == 1,
                        CanUpdate = Convert.ToInt32(rdr["CanUpdate"]) == 1,
                        CanDelete = Convert.ToInt32(rdr["CanDelete"]) == 1
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RightsManager LoadFromSQLite failed: " + ex.Message);
            }
            return list;
        }
    }
}