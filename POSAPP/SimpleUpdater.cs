using Velopack;
using Velopack.Sources;

namespace POSAPP
{
    public static class SimpleUpdater
    {
        // Option A: your own server (any folder that serves static files)
        private static readonly UpdateManager Mgr =
            new(new SimpleWebSource("https://shriposapi.mythitsolutions.co.in/updates/"));


        private static Velopack.UpdateInfo _info;
        public static Action<int> OnProgress;

        public static Version Current
        {
            get
            {
                var v = Mgr.CurrentVersion;
                return v != null ? new Version(v.Major, v.Minor, v.Patch)
                                 : new Version(1, 0, 0);
            }
        }

        public static async Task<Version> CheckAsync()
        {
            if (!Mgr.IsInstalled) return null;          // running from Visual Studio
            _info = await Mgr.CheckForUpdatesAsync();
            if (_info == null) return null;
            var v = _info.TargetFullRelease.Version;
            return new Version(v.Major, v.Minor, v.Patch);
        }

        public static async Task DownloadAndInstallAsync()
        {
            await Mgr.DownloadUpdatesAsync(_info, p => OnProgress?.Invoke(p));
            Mgr.ApplyUpdatesAndRestart(_info.TargetFullRelease);   // closes app, updates, relaunches
        }
    }
}