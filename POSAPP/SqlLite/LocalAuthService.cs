using System;
using System.Data.SQLite;
using System.IO;

namespace POSAPP.SqlLite
{
    public static class LocalAuthService
    {
        // Store the local DB in a writable, per-user location instead of the
        // install directory (Program Files is read-only for standard users
        // once the app is installed via setup — this was causing SaveUser to
        // fail silently and offline login to always report "no local credentials").
        private static readonly string DbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShriPOS");

        private static readonly string DbPath = Path.Combine(DbFolder, "ShriPOS.db");

        private static string ConnectionString =>
            $"Data Source={DbPath};Version=3;";

        public static void InitialiseDatabase()
        {
            Directory.CreateDirectory(DbFolder); // ensure the folder exists before opening the DB

            using var conn = new SQLiteConnection(ConnectionString);
            conn.Open();

            const string sql = @"
                CREATE TABLE IF NOT EXISTS Users (
                    PKuserID    INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserID      INTEGER NOT NULL,
                    Password    TEXT    NOT NULL,
                    Email       TEXT    NOT NULL UNIQUE,
                    Mobile      TEXT,
                    RoleID      INTEGER NOT NULL DEFAULT 0,
                    StoreID     INTEGER NOT NULL DEFAULT 0,
                    CompanyID   INTEGER NOT NULL DEFAULT 0,
                    Status      INTEGER NOT NULL DEFAULT 1,
                    CreatedBy   INTEGER,
                    CreatedDate TEXT
                );";

            using var cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        // ── Save / update a user after a successful API login ────────────────
        public static void SaveUser(UserInfo user, string plainPassword)
        {
            if (user == null || string.IsNullOrWhiteSpace(plainPassword)) return;

            try
            {
                EnsureDbExists();

                using var conn = new SQLiteConnection(ConnectionString);
                conn.Open();

                // Check if user already exists
                const string checkSql = "SELECT COUNT(1) FROM Users WHERE Email = @email;";
                using var checkCmd = new SQLiteCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@email", user.Email);
                long count = (long)checkCmd.ExecuteScalar();

                if (count > 0)
                {
                    // Update existing
                    const string update = @"
                UPDATE Users SET
                    UserID    = @uid,
                    Password  = @pwd,
                    Mobile    = @mobile,
                    RoleID    = @role,
                    StoreID   = @store,
                    CompanyID = @company
                WHERE Email = @email;";

                    using var cmd = new SQLiteCommand(update, conn);
                    cmd.Parameters.AddWithValue("@uid", user.UserID);
                    cmd.Parameters.AddWithValue("@pwd", plainPassword);
                    cmd.Parameters.AddWithValue("@mobile", user.Mobile ?? "");
                    cmd.Parameters.AddWithValue("@role", user.RoleID);
                    cmd.Parameters.AddWithValue("@store", user.StoreID);
                    cmd.Parameters.AddWithValue("@company", user.CompanyID);
                    cmd.Parameters.AddWithValue("@email", user.Email);
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    // Insert new
                    const string insert = @"
                INSERT INTO Users (UserID, Password, Email, Mobile,
                                   RoleID, StoreID, CompanyID, Status, CreatedDate)
                VALUES (@uid, @pwd, @email, @mobile,
                        @role, @store, @company, 1, @ts);";

                    using var cmd = new SQLiteCommand(insert, conn);
                    cmd.Parameters.AddWithValue("@uid", user.UserID);
                    cmd.Parameters.AddWithValue("@pwd", plainPassword);
                    cmd.Parameters.AddWithValue("@email", user.Email);
                    cmd.Parameters.AddWithValue("@mobile", user.Mobile ?? "");
                    cmd.Parameters.AddWithValue("@role", user.RoleID);
                    cmd.Parameters.AddWithValue("@store", user.StoreID);
                    cmd.Parameters.AddWithValue("@company", user.CompanyID);
                    cmd.Parameters.AddWithValue("@ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogError("SaveUser", ex);
                throw; // preserve existing behaviour for the caller's try/catch in login.cs
            }
        }

        // ── Validate credentials against local DB ────────────────────────────
        public static UserInfo ValidateUserByPassword(string plainPassword)
        {
            try
            {
                // Always ensure the Users table exists before querying it —
                // same reasoning as SaveUser's EnsureDbExists() call.
                EnsureDbExists();

                using var conn = new SQLiteConnection(ConnectionString);
                conn.Open();

                const string query = @"
            SELECT PKuserID, UserID, Email, Mobile, RoleID, StoreID, CompanyID
            FROM   Users
            WHERE  Password = @pwd
              AND  Status   = 1
            LIMIT  1;";

                using var cmd = new SQLiteCommand(query, conn);
                cmd.Parameters.AddWithValue("@pwd", plainPassword); // plain match

                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;

                return new UserInfo
                {
                    PKUserID = reader.GetInt32(0),
                    UserID = reader.GetInt32(1),
                    Email = reader.GetString(2),
                    Mobile = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RoleID = reader.GetInt32(4),
                    StoreID = reader.GetInt32(5),
                    CompanyID = reader.GetInt32(6),
                };
            }
            catch (Exception ex)
            {
                LogError("ValidateUserByPassword", ex);
                return null; // fall through to "no local credentials" instead of crashing
            }
        }

        private static void EnsureDbExists()
        {
            //if (!File.Exists(DbPath))
            InitialiseDatabase();
        }

        // ── Simple file logger so failures are visible even in an installed
        //    Release build where Debug.WriteLine goes nowhere. ──────────────
        private static void LogError(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(DbFolder);
                string logPath = Path.Combine(DbFolder, "error.log");
                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {ex}\n\n");
            }
            catch
            {
                // logging must never throw
            }
        }
    }
}