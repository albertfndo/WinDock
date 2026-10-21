using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using WinDock.Models;
using WinDock.Native;

namespace WinDock.Services
{
    public class DockManager
    {
        private readonly IntPtr _dockHwnd;
        private readonly SettingsService _settingsService;
        private readonly ObservableCollection<DockItem> _dockItems = new();
        private readonly Dictionary<IntPtr, DockItem> _windowMap = new();

        public ObservableCollection<DockItem> DockItems => _dockItems;

        public DockManager(IntPtr dockHwnd)
        {
            _dockHwnd = dockHwnd;
            _settingsService = SettingsService.Instance;

            InitializePinnedItems();
        }

        private void InitializePinnedItems()
        {
            _dockItems.Clear();

            foreach (var pin in _settingsService.CurrentSettings.PinnedApps)
            {
                var item = new DockItem
                {
                    Title = pin.Title,
                    ProcessName = pin.ProcessName,
                    LaunchPath = pin.LaunchPath,
                    IsPinned = true,
                    IsRunning = false,
                    IsActive = false,
                    WindowCount = 0
                };

                // Extract Icon from path or executable
                item.Icon = WindowHelper.GetWindowIcon(IntPtr.Zero, pin.LaunchPath);
                _dockItems.Add(item);
            }
        }

        /// <summary>
        /// Scans all active top-level windows and populates running state.
        /// </summary>
        public void RefreshWindows()
        {
            var activeHwnd = NativeMethods.GetForegroundWindow();
            var discoveredWindows = new List<(IntPtr Hwnd, string Title, string ProcName, string? ExePath)>();

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (WindowHelper.IsAltTabWindow(hWnd, _dockHwnd))
                {
                    string title = WindowHelper.GetWindowTitle(hWnd);
                    var (procName, exePath) = WindowHelper.GetProcessInfo(hWnd);
                    discoveredWindows.Add((hWnd, title, procName, exePath));
                }
                return true;
            }, IntPtr.Zero);

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Reset running states for pinned items
                foreach (var pin in _dockItems.Where(i => i.IsPinned))
                {
                    pin.IsRunning = false;
                    pin.IsActive = false;
                    pin.WindowCount = 0;
                    pin.WindowHandle = IntPtr.Zero;
                }

                var currentUnpinned = _dockItems.Where(i => !i.IsPinned).ToList();
                var matchedUnpinned = new HashSet<DockItem>();

                foreach (var win in discoveredWindows)
                {
                    // Check if window matches any pinned item
                    var pinnedItem = _dockItems.FirstOrDefault(i =>
                        i.IsPinned &&
                        (string.Equals(i.ProcessName, win.ProcName, StringComparison.OrdinalIgnoreCase) ||
                         (!string.IsNullOrEmpty(i.LaunchPath) && !string.IsNullOrEmpty(win.ExePath) &&
                          string.Equals(Path.GetFileName(i.LaunchPath), Path.GetFileName(win.ExePath), StringComparison.OrdinalIgnoreCase))));

                    if (pinnedItem != null)
                    {
                        pinnedItem.IsRunning = true;
                        pinnedItem.WindowCount++;
                        pinnedItem.WindowHandle = win.Hwnd;
                        if (win.Hwnd == activeHwnd)
                        {
                            pinnedItem.IsActive = true;
                        }

                        if (pinnedItem.Icon == null)
                        {
                            pinnedItem.Icon = WindowHelper.GetWindowIcon(win.Hwnd, win.ExePath);
                        }
                    }
                    else
                    {
                        // Check if unpinned item already exists for this process
                        var existing = currentUnpinned.FirstOrDefault(i =>
                            string.Equals(i.ProcessName, win.ProcName, StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            existing.IsRunning = true;
                            existing.WindowCount++;
                            existing.WindowHandle = win.Hwnd;
                            if (win.Hwnd == activeHwnd)
                            {
                                existing.IsActive = true;
                            }
                            matchedUnpinned.Add(existing);
                        }
                        else
                        {
                            // New running application!
                            var newItem = new DockItem
                            {
                                Title = string.IsNullOrWhiteSpace(win.Title) ? win.ProcName : win.Title,
                                ProcessName = win.ProcName,
                                LaunchPath = win.ExePath,
                                WindowHandle = win.Hwnd,
                                IsPinned = false,
                                IsRunning = true,
                                IsActive = (win.Hwnd == activeHwnd),
                                WindowCount = 1,
                                Icon = WindowHelper.GetWindowIcon(win.Hwnd, win.ExePath)
                            };

                            _dockItems.Add(newItem);
                            matchedUnpinned.Add(newItem);
                        }
                    }
                }

                // Remove unpinned items that are no longer running
                foreach (var unpinned in currentUnpinned)
                {
                    if (!matchedUnpinned.Contains(unpinned))
                    {
                        _dockItems.Remove(unpinned);
                    }
                }
            });
        }

        public void HandleWindowCreated(IntPtr hWnd)
        {
            if (!WindowHelper.IsAltTabWindow(hWnd, _dockHwnd)) return;
            RefreshWindows();
        }

        public void HandleWindowDestroyed(IntPtr hWnd)
        {
            RefreshWindows();
        }

        public void HandleWindowActivated(IntPtr hWnd)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in _dockItems)
                {
                    item.IsActive = (item.WindowHandle == hWnd);
                }
            });
        }

        /// <summary>
        /// Handles user clicking a dock item: toggles window minimize/restore or launches process.
        /// </summary>
        public void ExecuteItemClick(DockItem item)
        {
            if (item.IsRunning && item.WindowHandle != IntPtr.Zero)
            {
                if (item.IsActive)
                {
                    // Active -> Minimize
                    WindowHelper.MinimizeWindow(item.WindowHandle);
                    item.IsActive = false;
                }
                else
                {
                    // Inactive -> Restore and Bring to Top
                    WindowHelper.ActivateWindow(item.WindowHandle);
                    item.IsActive = true;
                }
            }
            else if (!string.IsNullOrEmpty(item.LaunchPath))
            {
                LaunchProcess(item.LaunchPath);
            }
            else if (!string.IsNullOrEmpty(item.ProcessName))
            {
                LaunchProcess(item.ProcessName);
            }
        }

        /// <summary>
        /// Launches an application or URL.
        /// </summary>
        public static void LaunchProcess(string path, bool asAdmin = false)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };

                if (asAdmin)
                {
                    psi.Verb = "runas";
                }

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DockManager] Launch error for {path}: {ex.Message}");
            }
        }

        /// <summary>
        /// Pins a file or application to the dock.
        /// </summary>
        public void PinApplication(string filePath)
        {
            string resolvedPath = ShortcutHelper.ResolveShortcut(filePath);
            string title = Path.GetFileNameWithoutExtension(filePath);
            string procName = Path.GetFileNameWithoutExtension(resolvedPath);

            var existing = _dockItems.FirstOrDefault(i =>
                string.Equals(i.LaunchPath, resolvedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.ProcessName, procName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.IsPinned = true;
            }
            else
            {
                var newItem = new DockItem
                {
                    Title = title,
                    ProcessName = procName,
                    LaunchPath = resolvedPath,
                    IsPinned = true,
                    IsRunning = false,
                    Icon = WindowHelper.GetWindowIcon(IntPtr.Zero, resolvedPath)
                };
                _dockItems.Add(newItem);
            }

            SavePinnedState();
        }

        /// <summary>
        /// Unpins an application from the dock.
        /// </summary>
        public void UnpinItem(DockItem item)
        {
            item.IsPinned = false;
            if (!item.IsRunning)
            {
                _dockItems.Remove(item);
            }

            SavePinnedState();
        }

        private void SavePinnedState()
        {
            var pinnedConfigs = _dockItems
                .Where(i => i.IsPinned)
                .Select(i => new PinnedAppConfig
                {
                    Title = i.Title,
                    ProcessName = i.ProcessName,
                    LaunchPath = i.LaunchPath ?? string.Empty
                })
                .ToList();

            var current = _settingsService.CurrentSettings;
            current.PinnedApps = pinnedConfigs;
            _settingsService.SaveSettings(current);
        }
    }
}
