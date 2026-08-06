using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace POSAPP.Shift
{
    public static class ShiftState
    {
        // ── Read-only properties ──────────────────────────────────────────
        public static bool IsOpen { get; private set; }
        public static decimal OpeningFloat { get; private set; } = 1200;
        public static decimal CashReceived { get; private set; }
        public static decimal BankReceived { get; private set; }
        public static decimal CardReceived { get; private set; }
        public static decimal ChangeGiven { get; private set; }
        public static int TxCount { get; private set; }
        public static DateTime OpenedAt { get; private set; }
        public static int OpenedByUserId { get; private set; }
        public static int ShiftId { get; private set; }
        public static int CompanyId { get; private set; }

        // ── Declared amounts (populated after SaveTenderDeclaration) ──────
        public static decimal DeclaredCash { get; private set; }
        public static decimal DeclaredUpi { get; private set; }
        public static decimal DeclaredCard { get; private set; }
        public static decimal DeclaredBank { get; private set; }
        public static bool HasDeclaration { get; private set; }

    

        // ── Pending float — set by FloatManagerForm, consumed by CloseShiftForm ──
        /// <summary>
        /// Float amount entered in FloatManagerForm but not yet used to open a shift.
        /// CloseShiftForm reads this and pre-fills the opening float field.
        /// Reset to 0 once a shift is successfully opened.
        /// </summary>
        public static decimal PendingFloat { get; private set; }

        /// <summary>Called from FloatManagerForm "Save" — stores the amount only, does NOT open the shift.</summary>
        public static void SetPendingFloat(decimal amount)
        {
            PendingFloat = amount > 0 ? amount : 0m;
        }

        /// <summary>
        /// Running float in the drawer:
        ///   Opening float + all money received − change paid out.
        /// </summary>
        public static decimal CurrentFloat =>
            OpeningFloat + CashReceived + BankReceived + CardReceived - ChangeGiven;

        private static string DbPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ══════════════════════════════════════════════════════════════════
        //  LOAD FROM DB  ─ call immediately after every login
        // ══════════════════════════════════════════════════════════════════
        public static void LoadFromDb(int companyId)
        {
            CompanyId = companyId;
            Reset();

            if (!File.Exists(DbPath)) return;

            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            EnsureTableExists(conn);

            using var cmd = new SQLiteCommand(@"
        SELECT ShiftID, OpenedByUserID, OpeningFloat, OpenedAt,
               CashReceived, BankReceived, CardReceived,
               ChangeGiven,  TxCount,      IsOpen
        FROM   Shifts
        WHERE  CompanyID = @cid
        ORDER  BY ShiftID DESC  -- Get most recent shift
        LIMIT  1;", conn);  // ← Removed IsOpen = 1 filter

            cmd.Parameters.AddWithValue("@cid", companyId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;

            ShiftId = Convert.ToInt32(r["ShiftID"]);
            OpenedByUserId = Convert.ToInt32(r["OpenedByUserID"]);
            OpeningFloat = Convert.ToDecimal(r["OpeningFloat"]);  // ← This will now load!
            OpenedAt = Convert.ToDateTime(r["OpenedAt"]);
            CashReceived = r["CashReceived"] == DBNull.Value ? 0m : Convert.ToDecimal(r["CashReceived"]);
            BankReceived = r["BankReceived"] == DBNull.Value ? 0m : Convert.ToDecimal(r["BankReceived"]);
            CardReceived = r["CardReceived"] == DBNull.Value ? 0m : Convert.ToDecimal(r["CardReceived"]);
            ChangeGiven = r["ChangeGiven"] == DBNull.Value ? 0m : Convert.ToDecimal(r["ChangeGiven"]);
            TxCount = r["TxCount"] == DBNull.Value ? 0 : Convert.ToInt32(r["TxCount"]);
            IsOpen = r["IsOpen"] != DBNull.Value && Convert.ToBoolean(r["IsOpen"]);
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPEN SHIFT  ─ called from CloseShiftForm
        // ══════════════════════════════════════════════════════════════════
        public static async Task<bool> OpenShift(int userId, int companyId, decimal openingAmount)
        {
            if (IsOpen) return false;

            // Always offline for now (online path commented out upstream).
            return await OpenShiftOffline(userId, companyId, openingAmount);
        }

        private static Task<bool> OpenShiftOffline(int userId, int companyId, decimal openingAmount)
        {
            try
            {
                using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                conn.Open();
                EnsureTableExists(conn);

                using var cmd = new SQLiteCommand(@"
                    INSERT INTO Shifts
                        (CompanyID, OpenedByUserID, OpeningFloat, OpenedAt,
                         CashReceived, BankReceived, CardReceived,
                         ChangeGiven,  TxCount, IsOpen, OfflineId)
                    VALUES
                        (@cid, @uid, @amt, @at, 0, 0, 0, 0, 0, 1, @offlineId);
                    SELECT last_insert_rowid();", conn);

                cmd.Parameters.AddWithValue("@cid", companyId);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@amt", openingAmount);
                cmd.Parameters.AddWithValue("@at", DateTime.Now);
                cmd.Parameters.AddWithValue("@offlineId", Guid.NewGuid().ToString());

                int localId = Convert.ToInt32(cmd.ExecuteScalar());

                // Update in-memory state
                ShiftId = localId;
                OpenedByUserId = userId;
                CompanyId = companyId;
                OpeningFloat = openingAmount;
                CashReceived = 0m;
                BankReceived = 0m;
                CardReceived = 0m;
                ChangeGiven = 0m;
                TxCount = 0;
                OpenedAt = DateTime.Now;
                IsOpen = true;
                PendingFloat = 0m;  // consumed — clear it

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OpenShiftOffline: " + ex.Message);
                return Task.FromResult(false);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  CLOSE SHIFT  ─ called from CloseShiftForm
        //
        //  Accepts the declared tender amounts directly.
        //  Validation (balanced / over / short) is the UI's responsibility;
        //  ShiftState just persists whatever the user confirmed.
        // ══════════════════════════════════════════════════════════════════
        public static bool CloseShift(int userId,
                                      decimal cash,
                                      decimal upi,
                                      decimal card,
                                      decimal bank)
        {
            if (!IsOpen) return false;

            try
            {
                using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                conn.Open();

                using var cmd = new SQLiteCommand(@"
                    UPDATE Shifts
                    SET    ClosedByUserID = @uid,
                           ClosedAt       = @at,
                           DeclaredCash   = @cash,
                           DeclaredUPI    = @upi,
                           DeclaredCard   = @card,
                           DeclaredBank   = @bank,
                           IsOpen         = 0
                    WHERE  ShiftID = @sid;", conn);

                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@at", DateTime.Now);
                cmd.Parameters.AddWithValue("@cash", cash);
                cmd.Parameters.AddWithValue("@upi", upi);
                cmd.Parameters.AddWithValue("@card", card);
                cmd.Parameters.AddWithValue("@bank", bank);
                cmd.Parameters.AddWithValue("@sid", ShiftId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ShiftState.CloseShift DB: " + ex.Message);
                return false;
            }

            Reset();
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        //  RECORD SALE  ─ called from SalesForm after every tender
        // ══════════════════════════════════════════════════════════════════
        public static void RecordSale(decimal cashTendered,
                                      decimal changeBack,
                                      decimal bankTendered = 0m,
                                      decimal cardTendered = 0m)
        {
            if (!IsOpen) return;

            CashReceived += cashTendered;
            BankReceived += bankTendered;
            CardReceived += cardTendered;
            ChangeGiven += changeBack;
            TxCount++;

            try
            {
                using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                conn.Open();
                using var cmd = new SQLiteCommand(@"
                    UPDATE Shifts
                    SET    CashReceived = @cr,
                           BankReceived = @br,
                           CardReceived = @ce,
                           ChangeGiven  = @cg,
                           TxCount      = @tx
                    WHERE  ShiftID = @sid;", conn);
                cmd.Parameters.AddWithValue("@cr", CashReceived);
                cmd.Parameters.AddWithValue("@br", BankReceived);
                cmd.Parameters.AddWithValue("@ce", CardReceived);
                cmd.Parameters.AddWithValue("@cg", ChangeGiven);
                cmd.Parameters.AddWithValue("@tx", TxCount);
                cmd.Parameters.AddWithValue("@sid", ShiftId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ShiftState.RecordSale DB: " + ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  REFRESH  ─ called by CloseShiftForm timer + on load
        // ══════════════════════════════════════════════════════════════════
        public static async Task RefreshShiftStatusAsync(int companyId, int userId)
        {
            // Always reload from local DB (online sync handled separately).
            await Task.Run(() => LoadFromDb(companyId));
        }

        // ══════════════════════════════════════════════════════════════════
        //  TENDER DECLARATION  ─ save denominations + payment totals
        // ══════════════════════════════════════════════════════════════════
        public static void SaveTenderDeclaration(int shiftId, int closingUserId,
                                                 int[] quantities,
                                                 decimal cash, decimal card, decimal bank)
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();

            using (var cmdCreate = new SQLiteCommand(@"
                CREATE TABLE IF NOT EXISTS tender_declaration (
                    tender_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                    shift_id        INTEGER NOT NULL,
                    confirm_user_id INTEGER,
                    notes_200       INTEGER DEFAULT 0,
                    notes_100       INTEGER DEFAULT 0,
                    notes_50        INTEGER DEFAULT 0,
                    notes_20        INTEGER DEFAULT 0,
                    notes_10        INTEGER DEFAULT 0,
                    coins_5         INTEGER DEFAULT 0,
                    coins_2         INTEGER DEFAULT 0,
                    coins_1         INTEGER DEFAULT 0,
                    coins_50c       INTEGER DEFAULT 0,
                    coins_20c       INTEGER DEFAULT 0,
                    coins_10c       INTEGER DEFAULT 0,
                    cash_counted    REAL DEFAULT 0.00,
                    upi_amount      REAL DEFAULT 0.00,
                    card_amount     REAL DEFAULT 0.00,
                    bank_transfer   REAL DEFAULT 0.00,
                    grand_total     REAL DEFAULT 0.00,
                    confirmed       TEXT DEFAULT 'Y' CHECK(confirmed IN ('Y','N')),
                    created_at      DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at      DATETIME DEFAULT CURRENT_TIMESTAMP
                );", conn))
                cmdCreate.ExecuteNonQuery();

            bool exists;
            using (var check = new SQLiteCommand(
                "SELECT 1 FROM tender_declaration WHERE shift_id = @sid LIMIT 1;", conn))
            {
                check.Parameters.AddWithValue("@sid", shiftId);
                exists = check.ExecuteScalar() != null;
            }

            string sql = exists
                ? @"UPDATE tender_declaration
                    SET notes_200=@n200, notes_100=@n100, notes_50=@n50,
                        notes_20=@n20,   notes_10=@n10,
                        coins_5=@c5,     coins_2=@c2,   coins_1=@c1,
                        coins_50c=@c50c, coins_20c=@c20c, coins_10c=@c10c,
                        cash_counted=@cash, card_amount=@card, bank_transfer=@bank,
                        grand_total=@grand, confirmed='Y', updated_at=CURRENT_TIMESTAMP
                    WHERE shift_id = @shift_id;"
                : @"INSERT INTO tender_declaration
                        (shift_id, confirm_user_id,
                         notes_200, notes_100, notes_50, notes_20, notes_10,
                         coins_5, coins_2, coins_1, coins_50c, coins_20c, coins_10c,
                         cash_counted, card_amount, bank_transfer, grand_total, confirmed)
                    VALUES
                        (@shift_id, @user_id,
                         @n200, @n100, @n50, @n20, @n10,
                         @c5, @c2, @c1, @c50c, @c20c, @c10c,
                         @cash, @card, @bank, @grand, 'Y');";

            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@shift_id", shiftId);
            if (!exists) cmd.Parameters.AddWithValue("@user_id", closingUserId);

            cmd.Parameters.AddWithValue("@n200", quantities[0]);
            cmd.Parameters.AddWithValue("@n100", quantities[1]);
            cmd.Parameters.AddWithValue("@n50", quantities[2]);
            cmd.Parameters.AddWithValue("@n20", quantities[3]);
            cmd.Parameters.AddWithValue("@n10", quantities[4]);
            cmd.Parameters.AddWithValue("@c5", quantities[5]);
            cmd.Parameters.AddWithValue("@c2", quantities[6]);
            cmd.Parameters.AddWithValue("@c1", quantities[7]);
            cmd.Parameters.AddWithValue("@c50c", quantities[8]);
            cmd.Parameters.AddWithValue("@c20c", quantities[9]);
            cmd.Parameters.AddWithValue("@c10c", quantities[10]);

            decimal grand = cash + card + bank;
            cmd.Parameters.AddWithValue("@cash", cash);
            cmd.Parameters.AddWithValue("@card", card);
            cmd.Parameters.AddWithValue("@bank", bank);
            cmd.Parameters.AddWithValue("@grand", grand);

            cmd.ExecuteNonQuery();

            // Update in-memory declared state
            DeclaredCash = cash;
            DeclaredCard = card;
            DeclaredBank = bank;
            HasDeclaration = true;
        }

        // ── Get existing tender declaration for the current open shift ────
        public static TenderDeclarationData GetExistingTenderDeclaration(int shiftId)
        {
            var data = new TenderDeclarationData { Exists = false };

            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();

            using var cmd = new SQLiteCommand(@"
                SELECT t.tender_id,
                       t.notes_200, t.notes_100, t.notes_50, t.notes_20, t.notes_10,
                       t.coins_5,   t.coins_2,   t.coins_1,
                       t.coins_50c, t.coins_20c, t.coins_10c,
                       t.cash_counted, t.upi_amount, t.card_amount,
                       t.bank_transfer, t.grand_total
                FROM   tender_declaration t
                INNER  JOIN Shifts s ON t.shift_id = s.ShiftID
                WHERE  t.shift_id = @shiftId AND s.IsOpen = 1
                LIMIT  1;", conn);
            cmd.Parameters.AddWithValue("@shiftId", shiftId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return data;

            data.Exists = true;
            data.TenderId = reader.GetInt32(0);
            for (int i = 0; i < 11; i++)
                data.Quantities[i] = reader.GetInt32(i + 1);
            data.CashCounted = reader.GetDecimal(12);
            data.UpiAmount = reader.GetDecimal(13);
            data.CardAmount = reader.GetDecimal(14);
            data.BankAmount = reader.GetDecimal(15);
            data.GrandTotal = reader.GetDecimal(16);

            return data;
        }

        public class TenderDeclarationData
        {
            public int TenderId { get; set; }
            public int[] Quantities { get; set; } = new int[11];
            public decimal CashCounted { get; set; }
            public decimal UpiAmount { get; set; }
            public decimal CardAmount { get; set; }
            public decimal BankAmount { get; set; }
            public decimal GrandTotal { get; set; }
            public bool Exists { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  OFFLINE SYNC  ─ push pending shifts to server when back online
        // ══════════════════════════════════════════════════════════════════
        public static async Task SyncPendingShifts()
        {
            if (!IsInternetAvailable()) return;

            var pending = GetPendingOfflineShifts();
            if (pending.Count == 0) return;

            try
            {
                using var client = new HttpClient();
                var json = JsonSerializer.Serialize(pending);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("https://shriposapi.mythitsolutions.co.in/api/shifts/sync", content);

                if (resp.IsSuccessStatusCode)
                    MarkShiftsAsSynced(pending.Select(p => p.OfflineId).ToList());
                else
                    System.Diagnostics.Debug.WriteLine("Sync failed: " + resp.StatusCode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SyncPendingShifts: " + ex.Message);
            }
        }

        private static List<ShiftSyncDto> GetPendingOfflineShifts()
        {
            var list = new List<ShiftSyncDto>();

            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();
            EnsureTableExists(conn);

            using var cmd = new SQLiteCommand(@"
                SELECT CompanyID, OpenedByUserID, OpeningFloat, OpenedAt, OfflineId
                FROM   Shifts
                WHERE  OfflineId IS NOT NULL
                  AND (IsSynced IS NULL OR IsSynced = 0);", conn);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ShiftSyncDto
                {
                    CompanyID = Convert.ToInt32(r["CompanyID"]),
                    OpenedByUserID = Convert.ToInt32(r["OpenedByUserID"]),
                    OpeningFloat = Convert.ToDecimal(r["OpeningFloat"]),
                    OpenedAt = Convert.ToDateTime(r["OpenedAt"]),
                    OfflineId = r["OfflineId"].ToString(),
                    IsOpen = 1
                });
            }

            return list;
        }

        private static void MarkShiftsAsSynced(List<string> offlineIds)
        {
            if (offlineIds == null || offlineIds.Count == 0) return;

            using var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;");
            conn.Open();

            string idList = string.Join(",", offlineIds.Select(id => $"'{id}'"));
            try
            {
                using var cmd = new SQLiteCommand(
                    $"UPDATE Shifts SET IsSynced = 1, OfflineId = NULL WHERE OfflineId IN ({idList});", conn);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                using var cmd = new SQLiteCommand(
                    $"UPDATE Shifts SET OfflineId = NULL WHERE OfflineId IN ({idList});", conn);
                cmd.ExecuteNonQuery();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  DTOs
        // ══════════════════════════════════════════════════════════════════
        public class ShiftSyncDto
        {
            public int CompanyID { get; set; }
            public int OpenedByUserID { get; set; }
            public decimal OpeningFloat { get; set; }
            public DateTime OpenedAt { get; set; }
            public string OfflineId { get; set; }
            public int IsOpen { get; set; } = 1;
        }

        public class OpenShiftResponse
        {
            public bool IsSuccess { get; set; }
            public string Message { get; set; }
            public ShiftData Data { get; set; }
        }

        public class ShiftStatusResponse
        {
            public bool IsSuccess { get; set; }
            public ShiftData Data { get; set; }
        }

        public class ShiftData
        {
            public int ShiftID { get; set; }
            public int CompanyID { get; set; }
            public bool IsOpen { get; set; }
            public DateTime OpenedAt { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
        private static void Reset()
        {
            IsOpen = false;
            ShiftId = 0;
            OpenedByUserId = 0;
            OpeningFloat = 0m;
            CashReceived = 0m;
            BankReceived = 0m;
            CardReceived = 0m;
            ChangeGiven = 0m;
            TxCount = 0;
            OpenedAt = default;
            DeclaredCash = 0m;
            DeclaredUpi = 0m;
            DeclaredCard = 0m;
            DeclaredBank = 0m;
            HasDeclaration = false;
        }

        private static bool IsInternetAvailable()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                return client.GetAsync("https://www.google.com").Result.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private static void EnsureTableExists(SQLiteConnection conn)
        {
            using (var cmd = new SQLiteCommand(@"
                CREATE TABLE IF NOT EXISTS Shifts (
                    ShiftID        INTEGER  PRIMARY KEY AUTOINCREMENT,
                    CompanyID      INTEGER  NOT NULL,
                    OpenedByUserID INTEGER  NOT NULL,
                    OpeningFloat   DECIMAL(18,2) NOT NULL,
                    OpenedAt       DATETIME NOT NULL,
                    CashReceived   DECIMAL(18,2) DEFAULT 0,
                    ChangeGiven    DECIMAL(18,2) DEFAULT 0,
                    TxCount        INTEGER  DEFAULT 0,
                    ClosedByUserID INTEGER,
                    ClosedAt       DATETIME,
                    DeclaredCash   DECIMAL(18,2),
                    DeclaredUPI    DECIMAL(18,2),
                    DeclaredCard   DECIMAL(18,2),
                    DeclaredBank   DECIMAL(18,2),
                    IsOpen         INTEGER  NOT NULL DEFAULT 1,
                    OfflineId      TEXT
                );", conn))
                cmd.ExecuteNonQuery();

            // Safely add columns that may not exist in older DB versions
            foreach (var alter in new[]
            {
                "ALTER TABLE Shifts ADD COLUMN OfflineId    TEXT;",
                "ALTER TABLE Shifts ADD COLUMN IsSynced     INTEGER DEFAULT 0;",
                "ALTER TABLE Shifts ADD COLUMN BankReceived DECIMAL(18,2);",
                "ALTER TABLE Shifts ADD COLUMN CardReceived DECIMAL(18,2);"
            })
            {
                try
                {
                    using var cmd = new SQLiteCommand(alter, conn);
                    cmd.ExecuteNonQuery();
                }
                catch { /* column already exists — safe to ignore */ }
            }
        }
    }
}