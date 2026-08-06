using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace POSAPP
{
    public class UpdateResponse
    {
        public bool IsSuccess { get; set; }
        public UpdateInfo Data { get; set; }
    }

    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public bool Mandatory { get; set; }
        public string ReleaseNotes { get; set; }
    }

    public class UpdateService
    {
        private readonly HttpClient _client;

        public UpdateService()
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:7022"),
                Timeout = TimeSpan.FromSeconds(20)
            };
        }

        public Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version;

        // ── Ask the server if a newer version exists ────────────────────
        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            try
            {
                var response = await _client.GetAsync("api/AppUpdate/latest");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var wrapper = JsonSerializer.Deserialize<UpdateResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (wrapper == null || !wrapper.IsSuccess || wrapper.Data == null)
                    return null;

                var info = wrapper.Data;
                if (string.IsNullOrWhiteSpace(info.Version))
                    return null;

                var latest = Version.Parse(info.Version);
                return latest > CurrentVersion ? info : null;
            }
            catch
            {
                // Never let a failed update check block the app from starting.
                return null;
            }
        }

        // ── Download the new build zip ───────────────────────────────────
        public async Task<string> DownloadUpdateAsync(UpdateInfo info)
        {
            string tempZip = Path.Combine(Path.GetTempPath(), $"POSAPP_{info.Version}.zip");

            using var response = await _client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(tempZip);
            await stream.CopyToAsync(fileStream);

            return tempZip;
        }

        // ── Extract the zip into a staging folder next to the app ────────
        public string ExtractUpdate(string zipPath, string version)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string stagingDir = Path.Combine(appDir, "update_staging_" + version);

            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            Directory.CreateDirectory(stagingDir);

            ZipFile.ExtractToDirectory(zipPath, stagingDir);
            return stagingDir;
        }

        // ── Hand off to Updater.exe and close this app ────────────────────
        public void LaunchUpdaterAndExit(string stagingDir)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string updaterExe = Path.Combine(appDir, "Updater.exe");

            if (!File.Exists(updaterExe))
            {
                MessageBox.Show("Updater.exe not found — cannot complete update.",
                    "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = updaterExe,
                Arguments = $"\"{stagingDir}\" \"{appDir}\" \"{exePath}\" {Process.GetCurrentProcess().Id}",
                UseShellExecute = true
            };
            Process.Start(psi);

            Application.Exit();
        }
    }
}