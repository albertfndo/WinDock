using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WinDock.Services
{
    /// <summary>
    /// Checks GitHub Releases for a newer WinDock, downloads WinDock-Setup.exe and installs it silently.
    /// The installer relaunches WinDock afterwards. Release reads are unauthenticated, so the repo must be public.
    /// </summary>
    public static class UpdateService
    {
        private const string Repo = "albertfndo/WinDock";
        private const string SetupAssetName = "WinDock-Setup.exe";

        private static DispatcherTimer? _timer;
        private static bool _isUpdating;

        public static void Start()
        {
            // Local/dev builds keep the csproj default 1.0.0; never replace those with a release.
            if (CurrentVersion <= new Version(1, 0, 0)) return;

            _ = CheckAndInstallAsync();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            _timer.Tick += (s, e) => _ = CheckAndInstallAsync();
            _timer.Start();
        }

        // Trimmed to 3 parts: assembly versions carry a revision (1.0.5.0), which would compare greater than tag v1.0.5.
        private static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        private static async Task CheckAndInstallAsync()
        {
            if (_isUpdating) return;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WinDock-Updater");

                string json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= CurrentVersion) return;

                string? url = root.GetProperty("assets").EnumerateArray()
                    .Where(a => a.GetProperty("name").GetString() == SetupAssetName)
                    .Select(a => a.GetProperty("browser_download_url").GetString())
                    .FirstOrDefault();
                if (url == null) return;

                _isUpdating = true;
                string setupPath = Path.Combine(Path.GetTempPath(), $"WinDock-Setup-{latest}.exe");
                await File.WriteAllBytesAsync(setupPath, await http.GetByteArrayAsync(url));

                Process.Start(new ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                    UseShellExecute = true
                });

                // Exit through the normal path so the native taskbar and work area are restored before the installer runs.
                if (Application.Current.MainWindow is Views.MainWindow main) main.ExitApplication();
                else Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                // Offline, rate-limited or private repo: try again on the next tick.
                _isUpdating = false;
                Debug.WriteLine($"[UpdateService] {ex.Message}");
            }
        }
    }
}
