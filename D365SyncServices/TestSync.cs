using System;
using System.IO;

namespace D365SyncService
{
    class TestSync
    {
        static void Main(string[] args)
        {
            // Get the folder where D365SyncService.exe is installed
            string serviceDir = AppDomain.CurrentDomain.BaseDirectory;

            // POS App is installed one level up (service is in {app}\Service\)
            string appDir = Path.GetFullPath(Path.Combine(serviceDir, @"..\"));

            string dbPath = Path.Combine(appDir, "ShriPOS.db");
            string configFile = Path.Combine(appDir, "config.txt");
            int storeId = 1;

            // Logger
            var logger = new Action<string>(msg =>
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
                Console.WriteLine(line);
                try
                {
                    // Log file sits next to the service exe
                    File.AppendAllText(Path.Combine(serviceDir, "D365SyncService.log"), line + Environment.NewLine);
                }
                catch { }
            });

            logger("=== Starting Manual Sync Test ===");
            logger($"Service Dir : {serviceDir}");
            logger($"App Dir     : {appDir}");
            logger($"Database    : {dbPath}");
            logger($"Config File : {configFile}");

            var engine = new SyncEngine(dbPath, configFile, storeId, logger);
            engine.SyncNow();

            logger("=== Test Finished ===");
            Console.WriteLine("\nPress any key to close the window...");
            Console.ReadKey();
        }
    }
}