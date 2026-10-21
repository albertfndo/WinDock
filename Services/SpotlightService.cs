using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using WinDock.Models;
using WinDock.Native;

namespace WinDock.Services
{
    public class SpotlightService
    {
        private static SpotlightService? _instance;
        public static SpotlightService Instance => _instance ??= new SpotlightService();

        private readonly List<IndexedApp> _indexedApps = new();
        private bool _isIndexed = false;
        private readonly DataTable _mathEvaluator = new();

        public record IndexedApp(string Name, string ExecutablePath, string? IconPath);

        public SpotlightService()
        {
            IndexApplications();
        }

        /// <summary>
        /// Returns all indexed applications for the AppList / Launchpad drawer.
        /// </summary>
        public List<SpotlightItem> GetAllIndexedApps()
        {
            return _indexedApps
                .OrderBy(a => a.Name)
                .Select(CreateAppItem)
                .ToList();
        }

        public void IndexApplications()
        {
            if (_isIndexed) return;

            try
            {
                var discovered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // 1. Scan Common Start Menu (.lnk files)
                string commonStartMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs");

                // 2. Scan User Start Menu (.lnk files)
                string userStartMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs");

                ScanDirectoryForShortcuts(commonStartMenu, discovered);
                ScanDirectoryForShortcuts(userStartMenu, discovered);

                foreach (var (name, path) in discovered)
                {
                    _indexedApps.Add(new IndexedApp(name, path, path));
                }

                // 3. Add Essential Windows Built-in Utilities
                AddBuiltInTool("WhatsApp", "whatsapp:", "WhatsApp Messenger");
                AddBuiltInTool("Settings", "ms-settings:", "System Configuration");
                AddBuiltInTool("Task Manager", "taskmgr.exe", "System Activity Monitor");
                AddBuiltInTool("Calculator", "calc.exe", "Windows Calculator");
                AddBuiltInTool("Notepad", "notepad.exe", "Text Editor");
                AddBuiltInTool("Windows Terminal", "wt.exe", "Terminal / Shell");
                AddBuiltInTool("PowerShell", "powershell.exe", "PowerShell Automation Shell");
                AddBuiltInTool("Command Prompt", "cmd.exe", "Command Line Interface");
                AddBuiltInTool("File Explorer", "explorer.exe", "File Manager");
                AddBuiltInTool("Control Panel", "control.exe", "Classic Control Panel");
                AddBuiltInTool("Paint", "mspaint.exe", "Image Editor");
                AddBuiltInTool("Snipping Tool", "snippingtool.exe", "Screen Capture");

                _isIndexed = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpotlightService] Index error: {ex.Message}");
            }
        }

        private void AddBuiltInTool(string name, string path, string description)
        {
            if (!_indexedApps.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _indexedApps.Add(new IndexedApp(name, path, path));
            }
        }

        private void ScanDirectoryForShortcuts(string directory, Dictionary<string, string> discovered)
        {
            if (!Directory.Exists(directory)) return;

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.lnk", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(file);

                    // Skip uninstallers and helpers
                    if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Help", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!discovered.ContainsKey(name))
                    {
                        discovered[name] = file;
                    }
                }
            }
            catch
            {
                // Access permissions or missing subdirectories
            }
        }

        /// <summary>
        /// Queries applications, running windows, math calculations, system actions, and web fallbacks.
        /// </summary>
        public List<SpotlightItem> Search(string query, DockManager? dockManager = null)
        {
            var results = new List<SpotlightItem>();
            string clean = query.Trim();

            if (string.IsNullOrWhiteSpace(clean))
            {
                // Return top recommended utilities when search is empty
                foreach (var app in _indexedApps.Take(6))
                {
                    results.Add(CreateAppItem(app));
                }
                return results;
            }

            // 1. Math / Calculator Check (e.g. "12 * 8 + 4" or "25 / 5")
            if (TryEvaluateMath(clean, out string mathResult))
            {
                results.Add(new SpotlightItem
                {
                    Title = mathResult,
                    Subtitle = $"Calculation: {clean} (Press Enter to Copy to Clipboard)",
                    Category = SpotlightCategory.Calculation,
                    ExecuteAction = () =>
                    {
                        Clipboard.SetText(mathResult.Replace("= ", ""));
                    }
                });
            }

            // 2. System Commands Check (e.g. "lock", "sleep", "restart", "shutdown")
            AddSystemCommandMatches(clean, results);

            // 3. Search Running Windows
            if (dockManager != null)
            {
                foreach (var dockItem in dockManager.DockItems.Where(i => i.IsRunning))
                {
                    if (dockItem.Title.Contains(clean, StringComparison.OrdinalIgnoreCase) ||
                        dockItem.ProcessName.Contains(clean, StringComparison.OrdinalIgnoreCase))
                    {
                        var winItem = new SpotlightItem
                        {
                            Title = dockItem.Title,
                            Subtitle = $"Running Window • Switch to {dockItem.ProcessName}",
                            Category = SpotlightCategory.RunningWindow,
                            Icon = dockItem.Icon,
                            ExecuteAction = () =>
                            {
                                if (dockItem.WindowHandle != IntPtr.Zero)
                                {
                                    WindowHelper.ActivateWindow(dockItem.WindowHandle);
                                }
                            }
                        };
                        results.Add(winItem);
                    }
                }
            }

            // 4. Search Applications
            var matchedApps = _indexedApps
                .Where(a => a.Name.Contains(clean, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Name.StartsWith(clean, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(a => a.Name.Length)
                .Take(7);

            foreach (var app in matchedApps)
            {
                results.Add(CreateAppItem(app));
            }

            // 5. URL or Web Search Fallback
            if (Uri.TryCreate(clean, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                results.Add(new SpotlightItem
                {
                    Title = $"Open {clean}",
                    Subtitle = "Web Link • Open in default browser",
                    Category = SpotlightCategory.WebSearch,
                    ExecuteAction = () => DockManager.LaunchProcess(clean)
                });
            }
            else
            {
                results.Add(new SpotlightItem
                {
                    Title = $"Search Google for \"{clean}\"",
                    Subtitle = "Web Search • Open results in browser",
                    Category = SpotlightCategory.WebSearch,
                    ExecuteAction = () =>
                    {
                        string searchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(clean)}";
                        DockManager.LaunchProcess(searchUrl);
                    }
                });
            }

            return results;
        }

        private SpotlightItem CreateAppItem(IndexedApp app)
        {
            return new SpotlightItem
            {
                Title = app.Name,
                Subtitle = $"Application • {Path.GetFileName(app.ExecutablePath)}",
                Category = SpotlightCategory.Application,
                Icon = WindowHelper.GetWindowIcon(IntPtr.Zero, app.ExecutablePath),
                LaunchPath = app.ExecutablePath,
                ExecuteAction = () => DockManager.LaunchProcess(app.ExecutablePath)
            };
        }

        private bool TryEvaluateMath(string input, out string result)
        {
            result = string.Empty;

            // Only evaluate if input looks like an arithmetic expression
            if (!Regex.IsMatch(input, @"^[\d\s\+\-\*\/\^\%\(\)\.]+$"))
                return false;

            if (!input.Any(char.IsDigit) || !input.Any(c => "+-*/%^".Contains(c)))
                return false;

            try
            {
                string expr = input.Replace("^", ""); // DataTable doesn't support ^
                var val = _mathEvaluator.Compute(expr, null);
                if (val != null && double.TryParse(val.ToString(), out double d))
                {
                    result = $"= {d:G10}";
                    return true;
                }
            }
            catch
            {
                // Invalid or incomplete syntax
            }

            return false;
        }

        private void AddSystemCommandMatches(string clean, List<SpotlightItem> results)
        {
            string lower = clean.ToLowerInvariant();

            if ("lock screen".Contains(lower) || "lock".Equals(lower))
            {
                results.Add(new SpotlightItem
                {
                    Title = "Lock Workstation",
                    Subtitle = "System Command • Lock current Windows session",
                    Category = SpotlightCategory.SystemCommand,
                    ExecuteAction = () => DockManager.LaunchProcess("rundll32.exe user32.dll,LockWorkStation")
                });
            }

            if ("restart computer".Contains(lower) || "reboot".Contains(lower))
            {
                results.Add(new SpotlightItem
                {
                    Title = "Restart PC",
                    Subtitle = "System Command • Reboot Windows",
                    Category = SpotlightCategory.SystemCommand,
                    ExecuteAction = () => DockManager.LaunchProcess("shutdown /r /t 0")
                });
            }

            if ("shutdown computer".Contains(lower) || "turn off".Contains(lower))
            {
                results.Add(new SpotlightItem
                {
                    Title = "Shut Down PC",
                    Subtitle = "System Command • Power off Windows",
                    Category = SpotlightCategory.SystemCommand,
                    ExecuteAction = () => DockManager.LaunchProcess("shutdown /s /t 0")
                });
            }

            if ("recycle bin".Contains(lower) || "trash".Contains(lower))
            {
                results.Add(new SpotlightItem
                {
                    Title = "Open Recycle Bin",
                    Subtitle = "System Folder • View deleted files",
                    Category = SpotlightCategory.SystemCommand,
                    ExecuteAction = () => DockManager.LaunchProcess("explorer.exe shell:RecycleBinFolder")
                });
            }
        }
    }
}
