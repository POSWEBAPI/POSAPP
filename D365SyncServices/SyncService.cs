using System;
using System.IO;
using System.ServiceProcess;
using System.Timers;

namespace D365SyncService
{
    /// <summary>
    /// Windows Service that syncs D365 API → ShriPOS.db every 5 minutes.
    /// POSAPP (SalesForm) reads products ONLY from the local SQLite — never the API.
    /// </summary>
    public partial class SyncService : ServiceBase
    {
        // ── Config ─────────────────────────────────────────────────────────────
        // The DB and config.txt are expected alongside POSAPP.exe.
        // Point these at the correct folder if the service exe lives elsewhere.
        private static readonly string AppFolder =
            Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)
            ?? AppDomain.CurrentDomain.BaseDirectory;

        internal static readonly string DbPath =
            Path.Combine(AppFolder, "ShriPOS.db");

        internal static readonly string ConfigFile =
            Path.Combine(AppFolder, "config.txt");

        private const int SyncIntervalMinutes = 5;
        private const int StoreId = 1;

        // ── Internals ──────────────────────────────────────────────────────────
        private Timer _timer;
        private readonly SyncEngine _engine;

        // ── Constructor ────────────────────────────────────────────────────────
        public SyncService()
        {
            ServiceName = "D365POSSyncService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;

            _engine = new SyncEngine(DbPath, ConfigFile, StoreId, Log);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Windows Service lifecycle
        // ══════════════════════════════════════════════════════════════════════
        protected override void OnStart(string[] args)
        {
            Log($"D365 POS Sync Service starting. DB={DbPath}");

            // Run one immediate sync, then start the interval timer
            RunSync();

            _timer = new Timer(SyncIntervalMinutes * 60 * 1000) { AutoReset = true };
            _timer.Elapsed += (s, e) => RunSync();
            _timer.Start();

            Log($"Sync timer started — interval {SyncIntervalMinutes} min.");
        }

        protected override void OnStop()
        {
            Log("D365 POS Sync Service stopping.");
            _timer?.Stop();
            _timer?.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Console mode (--console flag) — for debugging without installing
        // ══════════════════════════════════════════════════════════════════════
        public void StartConsole() => OnStart(Array.Empty<string>());
        public void StopConsole()  => OnStop();

        // ══════════════════════════════════════════════════════════════════════
        //  Trigger a sync cycle (fire-and-forget; errors are caught inside)
        // ══════════════════════════════════════════════════════════════════════
        private void RunSync()
        {
            try
            {
                _engine.SyncNow();
            }
            catch (Exception ex)
            {
                Log($"ERROR in RunSync: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Logging — goes to Windows Event Log when running as a service,
        //  and to console + file when running with --console
        // ══════════════════════════════════════════════════════════════════════
        internal void Log(string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            // Always try the event log
            try { EventLog.WriteEntry(line); } catch { }

            // Also write to a plain log file next to the exe
            try
            {
                string logPath = Path.Combine(AppFolder, "D365SyncService.log");
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch { }

            // Console output for --console mode
            Console.WriteLine(line);
        }
    }
}
