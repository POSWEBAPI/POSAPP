//using System;
//using System.ServiceProcess;
//using System.Threading;

//namespace D365SyncService
//{
//    internal static class Program
//    {
//        static void Main(string[] args)
//        {
//            bool standalone = Array.Exists(args,
//                a => a.Equals("--standalone", StringComparison.OrdinalIgnoreCase));

//            bool console = Array.Exists(args,
//                a => a.Equals("--console", StringComparison.OrdinalIgnoreCase));

//            if (standalone)
//            {
//                // ── Launched by POSAPP.exe ─────────────────────────────────────
//                // Runs as a plain background process (visible in Task Manager).
//                // No service registration needed.
//                RunStandalone();
//                return;
//            }

//            if (console)
//            {
//                // ── Developer debug mode ───────────────────────────────────────
//                var svc = new SyncService();
//                svc.StartConsole();
//                Console.WriteLine("Running — press ENTER to stop.");
//                Console.ReadLine();
//                svc.StopConsole();
//                return;
//            }

//            // ── Registered Windows Service (installutil) ───────────────────────
//            ServiceBase.Run(new SyncService());
//        }

//        // ══════════════════════════════════════════════════════════════════════
//        //  Standalone loop — same behaviour as the service timer but without
//        //  the ServiceBase scaffolding.  Exits cleanly when the parent process
//        //  (POSAPP) terminates, via the AppDomain.ProcessExit event.
//        // ══════════════════════════════════════════════════════════════════════
//        private static void RunStandalone()
//        {
//            const int IntervalMinutes = 5;

//            var engine = new SyncEngine(
//                SyncService.DbPath,
//                SyncService.ConfigFile,
//                storeId: 1,
//                log: msg => Console.WriteLine(
//                    $"[{DateTime.Now:HH:mm:ss}] {msg}"));

//            using var cts = new CancellationTokenSource();

//            // Stop when the host process exits
//            AppDomain.CurrentDomain.ProcessExit += (s, e) => cts.Cancel();

//            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] D365SyncService standalone started " +
//                              $"(interval={IntervalMinutes} min).");

//            // Immediate first sync
//            TrySync(engine);

//            while (!cts.Token.IsCancellationRequested)
//            {
//                // Sleep in 1-second ticks so we can react to cancellation quickly
//                for (int i = 0; i < IntervalMinutes * 60; i++)
//                {
//                    if (cts.Token.IsCancellationRequested) break;
//                    Thread.Sleep(1000);
//                }

//                if (!cts.Token.IsCancellationRequested)
//                    TrySync(engine);
//            }

//            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] D365SyncService standalone stopped.");
//        }

//        private static void TrySync(SyncEngine engine)
//        {
//            try   { engine.SyncNow(); }
//            catch (Exception ex)
//            { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}"); }
//        }
//    }
//}
