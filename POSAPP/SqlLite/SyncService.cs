using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

public class SyncService : IDisposable
{
    private readonly string sqlConnStr = "Server=102.134.166.35;Database=POSWeb;User ID=sa;Password=Mythit@1234$;Connect Timeout=5;TrustServerCertificate=True;";
    private readonly string sqliteConnStr = "Data Source=ShriPOS.db";

    private Timer _syncTimer;
    private volatile bool _isSyncing = false;
    private readonly object _syncLock = new object();

    // ── Raised on the thread-pool; marshal to UI thread if needed ──────────
    public event Action<string> OnSyncStatusChanged;
    public event Action<Exception> OnSyncError;

    // ══════════════════════════════════════════════════════════════════════
    //  START / STOP
    //  FIX: dueTime changed from TimeSpan.Zero → TimeSpan.FromMinutes(1)
    //       so the first sync fires AFTER the login page has fully loaded,
    //       preventing a SQL connection attempt that blocked/crashed startup.
    // ══════════════════════════════════════════════════════════════════════
    public void StartAutoSync()
    {
        _syncTimer = new Timer(
            callback: _ => TriggerSync(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),   // ← was TimeSpan.Zero (crash cause)
            period: TimeSpan.FromMinutes(10)
        );
        OnSyncStatusChanged?.Invoke("Auto-sync started. First sync in 1 minute, then every 10 minutes.");
    }

    public void StopAutoSync()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
        OnSyncStatusChanged?.Invoke("Auto-sync stopped.");
    }

    

    // ══════════════════════════════════════════════════════════════════════
    //  SYNC ALL TABLES
    // ══════════════════════════════════════════════════════════════════════
    public void SyncAll(string callerScreen = "Background Sync")
    {
        try
        {
            OnSyncStatusChanged?.Invoke($"[{DateTime.Now:dd-MM-yyyy HH:mm:ss}] Sync started by: {callerScreen}");

            if (!IsServerReachable())
            {
                OnSyncStatusChanged?.Invoke($"[{DateTime.Now:HH:mm:ss}] Server unreachable. Sync skipped.");
                return;
            }

            SyncTable("CompanyMaster", "CompanyID");
            SyncTable("Store", "StoreID");
            SyncTable("CustomerMaster", "CustomerID");
            //SyncTable("Item", "ItemID");
            SyncTable("CurrencyMaster", "CurrencyID");
            SyncTable("PaymentMethod", "PaymentMethodID");
            SyncTable("Users", "PKuserID");
            SyncTable("POSPermission", "RoleID");
            SyncTable("SOInvoiceLine", "SOInvoiceLineID");
            SyncTable("StockMovement", "StockMovementId");

            // ── Write ONE row: only the screen that triggered this sync ───────
            //    e.g. "SyncService" for the background timer,
            //         "SalesForm"   if  SalesForm called TriggerSyncForScreen(),
            //         "ReportsForm" if ReportsForm triggered it, etc.
            using (var conn = new SQLiteConnection(sqliteConnStr))
            {
                conn.Open();
                using var tx = conn.BeginTransaction();
                UpsertSyncControl(conn, tx, callerScreen);
                tx.Commit();
            }

            OnSyncStatusChanged?.Invoke(
                $"[{DateTime.Now:HH:mm:ss}] Sync complete [{callerScreen}]. Next in 10 min.");
        }
        catch (Exception ex)
        {
            OnSyncError?.Invoke(ex);
            OnSyncStatusChanged?.Invoke($"[{DateTime.Now:HH:mm:ss}] Sync failed: {ex.Message}");
        }
    }
    public void TriggerSyncForScreen(string screenName)
    {
        lock (_syncLock)
        {
            if (_isSyncing)
            {
                OnSyncStatusChanged?.Invoke($"Sync skipped [{screenName}] — previous sync still running.");
                return;
            }
            _isSyncing = true;
        }

        Task.Run(() =>
        {
            try { SyncAll(screenName); }
            catch { /* SyncAll handles and reports */ }
            finally { lock (_syncLock) { _isSyncing = false; } }
        });
    }
    public void TriggerSyncNow() => TriggerSync();   // unchanged — uses "SyncService"

    // Internal trigger still writes "SyncService"
    private void TriggerSync()
    {
        lock (_syncLock)
        {
            if (_isSyncing)
            {
                OnSyncStatusChanged?.Invoke("Sync skipped — previous sync still running.");
                return;
            }
            _isSyncing = true;
        }

        Task.Run(() =>
        {
            try { SyncAll("SyncService"); }
            catch { }
            finally { lock (_syncLock) { _isSyncing = false; } }
        });
    }

    private static void UpsertSyncControl(
     SQLiteConnection conn, SQLiteTransaction tx,
     string syncType)
    {
        string formatted = DateTime.Now.ToString("dd-MM-yyyy HH.mm");

        using var cmd = new SQLiteCommand(@"
        INSERT INTO POS_SyncControl (SyncType, LastSyncDateTime)
        VALUES (@type, @dt)
        ON CONFLICT(SyncType) DO UPDATE SET
            LastSyncDateTime = excluded.LastSyncDateTime",
            conn, tx);
        cmd.Parameters.AddWithValue("@type", syncType);
        cmd.Parameters.AddWithValue("@dt", formatted);
        cmd.ExecuteNonQuery();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SYNC SINGLE TABLE
    //  OPT: Open both connections once per table; reuse across all rows.
    // ══════════════════════════════════════════════════════════════════════
    private void SyncTable(string tableName, string primaryKey)
    {
        try
        {
            OnSyncStatusChanged?.Invoke($"  → Syncing {tableName}...");

            using (var sqlConn = new SqlConnection(sqlConnStr))
            using (var sqliteConn = new SQLiteConnection(sqliteConnStr))
            {
                sqlConn.Open();
                sqliteConn.Open();

                // Step 1: Create table in SQLite if it doesn't exist
                EnsureTableExists(sqlConn, sqliteConn, tableName, primaryKey);

                // Step 2: Fetch all rows from SQL Server
                int inserted = 0, updated = 0;

                using (var cmd = new SqlCommand($"SELECT * FROM [{tableName}]", sqlConn))
                {
                    cmd.CommandTimeout = 60;

                    using (var reader = cmd.ExecuteReader())
                    using (var transaction = sqliteConn.BeginTransaction())
                    {
                        // OPT: Pre-build reusable SQLite commands outside the row loop
                        //      so command objects are not re-allocated on every row.
                        SQLiteCommand insertCmd = null;
                        SQLiteCommand updateCmd = null;
                        SQLiteCommand checkCmd = null;

                        try
                        {
                            while (reader.Read())
                            {
                                // Build commands lazily on the first row (schema is stable)
                                if (checkCmd == null)
                                {
                                    checkCmd = BuildCheckCommand(sqliteConn, transaction, tableName, primaryKey);
                                    insertCmd = BuildInsertCommand(sqliteConn, transaction, reader, tableName);
                                    updateCmd = BuildUpdateCommand(sqliteConn, transaction, reader, tableName, primaryKey);
                                }

                                bool wasUpdated = UpsertRecord(
                                    checkCmd, insertCmd, updateCmd,
                                    reader, primaryKey);

                                if (wasUpdated) updated++;
                                else inserted++;
                            }

                            transaction.Commit();
                        }
                        finally
                        {
                            checkCmd?.Dispose();
                            insertCmd?.Dispose();
                            updateCmd?.Dispose();
                        }
                    }
                }

                OnSyncStatusChanged?.Invoke(
                    $"  ✓ {tableName}: {inserted} inserted, {updated} updated.");
            }
        }
        catch (Exception ex)
        {
            OnSyncStatusChanged?.Invoke($"  ✗ {tableName} failed: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  COMMAND BUILDERS  (called once per table, not once per row)
    // ══════════════════════════════════════════════════════════════════════
    private static SQLiteCommand BuildCheckCommand(
        SQLiteConnection conn, SQLiteTransaction tx,
        string tableName, string pkName)
    {
        var cmd = new SQLiteCommand(
            $"SELECT COUNT(1) FROM [{tableName}] WHERE [{pkName}] = @pkVal",
            conn, tx);
        cmd.Parameters.Add("@pkVal", DbType.Object);
        return cmd;
    }

    private static SQLiteCommand BuildInsertCommand(  SQLiteConnection conn, SQLiteTransaction tx,  SqlDataReader reader, string tableName)
    {
        var columns = new List<string>();
        var parameters = new List<string>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add($"[{reader.GetName(i)}]");
            parameters.Add("@p" + i);
        }

        var cmd = new SQLiteCommand(
            $"INSERT INTO [{tableName}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})",
            conn, tx);

        for (int i = 0; i < reader.FieldCount; i++)
            cmd.Parameters.Add("@p" + i, DbType.Object);

        return cmd;
    }

    private static SQLiteCommand BuildUpdateCommand( SQLiteConnection conn, SQLiteTransaction tx,   SqlDataReader reader, string tableName, string pkName)
    {
        var setClauses = new List<string>();

        for (int i = 0; i < reader.FieldCount; i++)
            if (!reader.GetName(i).Equals(pkName, StringComparison.OrdinalIgnoreCase))
                setClauses.Add($"[{reader.GetName(i)}] = @p{i}");

        var cmd = new SQLiteCommand(
            $"UPDATE [{tableName}] SET {string.Join(", ", setClauses)} WHERE [{pkName}] = @pkVal",
            conn, tx);

        for (int i = 0; i < reader.FieldCount; i++)
            cmd.Parameters.Add("@p" + i, DbType.Object);

        cmd.Parameters.Add("@pkVal", DbType.Object);
        return cmd;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UPSERT — reuses pre-built commands; returns true if row was updated
    // ══════════════════════════════════════════════════════════════════════
    private static bool UpsertRecord(   SQLiteCommand checkCmd,
        SQLiteCommand insertCmd,
        SQLiteCommand updateCmd,
        SqlDataReader reader,
        string pkName)
    {
        object pkVal = reader[pkName];
        if (pkVal == DBNull.Value) pkVal = DBNull.Value; // explicit for clarity

        // ── Check existence ───────────────────────────────────────────────
        checkCmd.Parameters["@pkVal"].Value = pkVal;
        bool exists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;

        // ── Populate shared column parameters (@p0 … @pN) ─────────────────
        SQLiteCommand activeCmd = exists ? updateCmd : insertCmd;

        for (int i = 0; i < reader.FieldCount; i++)
        {
            object val = reader.GetValue(i);
            activeCmd.Parameters["@p" + i].Value = (val == DBNull.Value) ? DBNull.Value : val;
        }

        if (exists)
            activeCmd.Parameters["@pkVal"].Value = pkVal;

        activeCmd.ExecuteNonQuery();
        return exists;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  AUTO-CREATE TABLE IN SQLITE IF NOT EXISTS
    // ══════════════════════════════════════════════════════════════════════
    private void EnsureTableExists(SqlConnection sqlConn, SQLiteConnection sqliteConn,
                                    string tableName, string primaryKey)
    {
        using (var cmd = new SqlCommand($"SELECT TOP 0 * FROM [{tableName}]", sqlConn))
        using (var reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly))
        {
            DataTable schema = reader.GetSchemaTable();
            if (schema == null) return;

            var colDefs = new List<string>();

            foreach (DataRow row in schema.Rows)
            {
                string colName = row["ColumnName"]?.ToString() ?? "";
                string colType = SqlTypeToSQLite(row["DataTypeName"]?.ToString() ?? "");
                bool isPk = colName.Equals(primaryKey, StringComparison.OrdinalIgnoreCase);

                colDefs.Add(isPk
                    ? $"[{colName}] {colType} PRIMARY KEY"
                    : $"[{colName}] {colType}");
            }

            string createSql = $"CREATE TABLE IF NOT EXISTS [{tableName}] ({string.Join(", ", colDefs)})";

            using (var createCmd = new SQLiteCommand(createSql, sqliteConn))
                createCmd.ExecuteNonQuery();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SQL SERVER → SQLITE TYPE MAPPING
    // ══════════════════════════════════════════════════════════════════════
    private static string SqlTypeToSQLite(string sqlType)
    {
        switch (sqlType.ToLower())
        {
            case "int":
            case "bigint":
            case "smallint":
            case "tinyint":
            case "bit":
                return "INTEGER";

            case "decimal":
            case "numeric":
            case "money":
            case "smallmoney":
            case "float":
            case "real":
                return "REAL";

            case "datetime":
            case "datetime2":
            case "date":
            case "time":
            case "smalldatetime":
                return "TEXT";

            case "uniqueidentifier":
                return "TEXT";

            default:
                return "TEXT";
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CONNECTIVITY CHECK
    // ══════════════════════════════════════════════════════════════════════
    private bool IsServerReachable()
    {
        try
        {
            using (var conn = new SqlConnection(sqlConnStr))
            {
                conn.Open();
                return true;
            }
        }
        catch (Exception ex)
        {
            OnSyncStatusChanged?.Invoke($"  Server check failed: {ex.Message}");
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DISPOSE  (implement IDisposable properly)
    // ══════════════════════════════════════════════════════════════════════
    public void Dispose()
    {
        StopAutoSync();
    }
}