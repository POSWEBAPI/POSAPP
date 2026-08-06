using Microsoft.Win32;
using POSAPP.SqlLite;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace POSAPP.Entity
{
    internal static class Program
    {
        // ── CHANGED: replaced ApiBase constant with these two lines ──
        internal static AppSettings Settings { get; private set; } = new();
        //internal static IApiService Api { get; private set; } = null!;

        // ── Unchanged ─────────────────────────────────────────────────
        private const string RegPath = @"SOFTWARE\YourCompany\POS";
        private const string LastValidKey = "LastValidatedUtc";
        private const string LicenseKeyReg = "LicenseKey";
        static Icon _appIcon;

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            POSAPP.UiLayoutService.Install();

            try
            {
                string icoPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "flo.ico");
                if (File.Exists(icoPath))
                    _appIcon = new Icon(icoPath);
            }
            catch { }

            // Apply to every form that opens
            Application.OpenForms.OfType<Form>().ToList()
                .ForEach(f => { if (_appIcon != null) f.Icon = _appIcon; });

            //using (var splash = new SplashScreen())
            //{
            //    splash.ShowDialog();   // blocking — returns only after splash closes
            //}

            // ── ADDED STEP 1: Load appsettings.json ───────────────────


            // ── Unchanged: license validation ─────────────────────────
            //var (valid, reason) = await ValidateLicense();
            //if (!valid)
            //{
            //    MessageBox.Show(reason,
            //        "POS — License Error",
            //        MessageBoxButtons.OK,
            //        MessageBoxIcon.Error);
            //    return;
            //}

            // ── Unchanged ─────────────────────────────────────────────
            DatabaseInitializer.Initialize();
            LocalAuthService.InitialiseDatabase();
            Application.Run(new login());

        }

        // ── ADDED: reads appsettings.json from the app folder ─────────


        // ── Everything below is your original code — UNCHANGED ────────

        //static async Task<(bool ok, string reason)> ValidateLicense()
        //{
        //    // Skip licence check if D365 mode has it disabled in appsettings
        //    if (Settings.ApiMode.Equals("d365", StringComparison.OrdinalIgnoreCase)
        //        && Settings.SkipLicenseInD365Mode)
        //        return (true, "");

        //    string apiBase = Settings.NormalApi?.BaseUrl ?? "https://localhost:7022";

        //    string licenseKey = ReadRegistry(LicenseKeyReg);
        //    if (string.IsNullOrWhiteSpace(licenseKey))
        //        return (false,
        //            "No license key found on this machine.\n" +
        //            "Please reinstall or contact your administrator.");

        //    string? mac = GetPhysicalMac();
        //    if (string.IsNullOrWhiteSpace(mac))
        //        return (false,
        //            "Unable to detect a physical network adapter.\n" +
        //            "Please ensure Wi-Fi or Ethernet is connected.");

        //    try
        //    {
        //        using var client = new HttpClient();
        //        client.Timeout = TimeSpan.FromSeconds(10);

        //        var url = $"{apiBase}/api/License/validate" +
        //                  $"?licenseKey={Uri.EscapeDataString(licenseKey)}" +
        //                  $"&macAddress={Uri.EscapeDataString(mac)}";

        //        var json = await client.GetStringAsync(url);
        //        var result = JsonSerializer.Deserialize<ValidationResponse>(json,
        //            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        //        if (result is null)
        //            return (false, "Invalid response from license server.");

        //        if (result.ServerTime.HasValue)
        //        {
        //            var age = DateTime.UtcNow - result.ServerTime.Value;
        //            if (Math.Abs(age.TotalMinutes) > 5)
        //                return (false,
        //                    "License server response is stale or clock is out of sync.");
        //        }

        //        if (result.Valid)
        //        {
        //            SaveRegistry(LastValidKey, DateTime.UtcNow.ToString("O"));
        //            return (true, "");
        //        }

        //        return (false,
        //            $"License validation failed:\n{result.Message ?? "Unknown reason."}" +
        //            "\n\nContact your administrator.");
        //    }
        //    catch (HttpRequestException) { return CheckOfflineGrace(); }
        //    catch (TaskCanceledException) { return CheckOfflineGrace(); }
        //}

        static string? GetPhysicalMac()
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                    !n.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) &&
                    !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                    n.GetPhysicalAddress().GetAddressBytes().Length == 6)
                .OrderBy(n => n.Name)
                .Select(n => string.Join("-",
                    n.GetPhysicalAddress().GetAddressBytes()
                     .Select(b => b.ToString("X2"))))
                .FirstOrDefault();
        }

        static string ReadRegistry(string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegPath);
                return key?.GetValue(valueName)?.ToString() ?? "";
            }
            catch { return ""; }
        }

        static void SaveRegistry(string valueName, string value)
        {
            try
            {
                string? mac = GetPhysicalMac();
                string bound = $"{value}|{mac}";
                using var key = Registry.LocalMachine
                    .OpenSubKey(RegPath, writable: true)
                    ?? Registry.LocalMachine.CreateSubKey(RegPath);
                key?.SetValue(valueName, bound);
            }
            catch { }
        }

        //static (bool ok, string reason) CheckOfflineGrace()
        //{
        //    string? mac = GetPhysicalMac();
        //    string? saved = ReadRegistry(LastValidKey);

        //    if (!string.IsNullOrWhiteSpace(saved))
        //    {
        //        var parts = saved.Split('|');
        //        if (parts.Length == 2)
        //        {
        //            string savedMac = parts[1];
        //            if (!string.Equals(savedMac, mac, StringComparison.OrdinalIgnoreCase))
        //                return (false, "License is not valid for this machine.");

        //            if (DateTime.TryParse(parts[0], null,
        //                System.Globalization.DateTimeStyles.RoundtripKind,
        //                out DateTime _)) ;
        //        }
        //    }

        //return (false,
        //    "Could not reach the license server.\n\n" +
        //    "Please connect to the internet and restart the application.");
        //}
    }

    record ValidationResponse(
        bool Valid,
        string? Message,
        string? ExpiresAt,
        int? DaysRemaining,
        DateTime? ServerTime
    );
}

