using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using WinDock.Models;
using WinDock.Native;

namespace WinDock.Services
{
    public class DockManager : INotifyPropertyChanged
    {
        private readonly IntPtr _dockHwnd;
        private readonly SettingsService _settingsService;
        private readonly ObservableCollection<DockItem> _pinnedItems = new();
        private readonly ObservableCollection<DockItem> _runningItems = new();

        public ObservableCollection<DockItem> PinnedItems => _pinnedItems;
        public ObservableCollection<DockItem> RunningItems => _runningItems;

        public bool HasRunningItems => _runningItems.Count > 0;

        public IEnumerable<DockItem> AllItems => _pinnedItems.Concat(_runningItems);
        public IEnumerable<DockItem> DockItems => AllItems;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DockManager(IntPtr dockHwnd)
        {
            _dockHwnd = dockHwnd;
            _settingsService = SettingsService.Instance;

            _runningItems.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasRunningItems));
            };

            InitializePinnedItems();
        }

        public void InitializePinnedItems()
        {
            _pinnedItems.Clear();

            foreach (var pin in _settingsService.CurrentSettings.PinnedApps)
            {
                string resolved = WindowHelper.ResolveExecutablePath(pin.LaunchPath);
                var item = new DockItem
                {
                    Title = pin.Title,
                    ProcessName = pin.ProcessName,
                    LaunchPath = resolved,
                    IsPinned = true,
                    IsRunning = false,
                    IsActive = false,
                    WindowCount = 0,
                    IconSize = _settingsService.CurrentSettings.IconSize
                };

                // Extract High-Res Icon from resolved path
                item.Icon = WindowHelper.GetWindowIcon(IntPtr.Zero, resolved, pin.Title);
                _pinnedItems.Add(item);
            }
        }

        /// <summary>
        /// Scans all active top-level windows and populates running states for pinned and unpinned apps.
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
                // 1. Reset running states for pinned items
                foreach (var pin in _pinnedItems)
                {
                    pin.IsRunning = false;
                    pin.IsActive = false;
                    pin.WindowCount = 0;
                    pin.WindowHandle = IntPtr.Zero;
                }

                var currentUnpinned = _runningItems.ToList();
                var matchedUnpinned = new HashSet<DockItem>();

                foreach (var win in discoveredWindows)
                {
                    // Check if window matches any pinned item
                    var pinnedItem = _pinnedItems.FirstOrDefault(i =>
                        (string.Equals(i.ProcessName, win.ProcName, StringComparison.OrdinalIgnoreCase)) ||
                        (string.Equals(i.Title, win.Title, StringComparison.OrdinalIgnoreCase)) ||
                        (win.ProcName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) && i.Title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) ||
                        (win.ProcName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) && i.ProcessName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(i.LaunchPath) && !string.IsNullOrEmpty(win.ExePath) &&
                         string.Equals(Path.GetFileName(i.LaunchPath), Path.GetFileName(win.ExePath), StringComparison.OrdinalIgnoreCase)));

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
                            pinnedItem.Icon = WindowHelper.GetWindowIcon(win.Hwnd, win.ExePath, pinnedItem.Title);
                        }
                    }
                    else
                    {
                        // Check if unpinned running item already exists for this process or window handle
                        var existing = currentUnpinned.FirstOrDefault(i =>
                            i.WindowHandle == win.Hwnd ||
                            (string.Equals(i.ProcessName, win.ProcName, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(i.Title, win.Title, StringComparison.OrdinalIgnoreCase)) ||
                            (win.ProcName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) &&
                             i.ProcessName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)));

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
                            string displayTitle = !string.IsNullOrWhiteSpace(win.Title) ? win.Title : win.ProcName;
                            var newItem = new DockItem
                            {
                                Title = displayTitle,
                                ProcessName = win.ProcName,
                                LaunchPath = win.ExePath,
                                WindowHandle = win.Hwnd,
                                IsPinned = false,
                                IsRunning = true,
                                IsActive = (win.Hwnd == activeHwnd),
                                WindowCount = 1,
                                IconSize = _settingsService.CurrentSettings.IconSize,
                                Icon = WindowHelper.GetWindowIcon(win.Hwnd, win.ExePath, displayTitle)
                            };

                            _runningItems.Add(newItem);
                            matchedUnpinned.Add(newItem);
                        }
                    }
                }

                // Remove unpinned items that are no longer running
                foreach (var unpinned in currentUnpinned)
                {
                    if (!matchedUnpinned.Contains(unpinned))
                    {
                        _runningItems.Remove(unpinned);
                    }
                }

                OnPropertyChanged(nameof(HasRunningItems));
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
                foreach (var item in AllItems)
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
                if (path.Equals("whatsapp", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("whatsapp.exe", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("whatsapp:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "whatsapp:",
                            UseShellExecute = true
                        });
                        return;
                    }
                    catch { }
                }

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
                if (path.Contains("whatsapp", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = "whatsapp:", UseShellExecute = true });
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Pins a file or application to the dock.
        /// </summary>
        public void PinApplication(string filePath)
        {
            string resolvedPath = WindowHelper.ResolveExecutablePath(filePath);
            string title = Path.GetFileNameWithoutExtension(filePath);
            string procName = Path.GetFileNameWithoutExtension(resolvedPath);

            // Check if already in pinned
            var existing = _pinnedItems.FirstOrDefault(i =>
                string.Equals(i.LaunchPath, resolvedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.ProcessName, procName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.IsPinned = true;
            }
            else
            {
                // Check if currently running in unpinned list
                var running = _runningItems.FirstOrDefault(i =>
                    string.Equals(i.LaunchPath, resolvedPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.ProcessName, procName, StringComparison.OrdinalIgnoreCase));

                if (running != null)
                {
                    _runningItems.Remove(running);
                    running.IsPinned = true;
                    _pinnedItems.Add(running);
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
                        IconSize = _settingsService.CurrentSettings.IconSize,
                        Icon = WindowHelper.GetWindowIcon(IntPtr.Zero, resolvedPath, title)
                    };
                    _pinnedItems.Add(newItem);
                }
            }

            SavePinnedState();
            OnPropertyChanged(nameof(HasRunningItems));
        }

        /// <summary>
        /// Unpins an application from the dock.
        /// </summary>
        public void UnpinItem(DockItem item)
        {
            item.IsPinned = false;
            _pinnedItems.Remove(item);

            if (item.IsRunning)
            {
                _runningItems.Add(item);
            }

            SavePinnedState();
            OnPropertyChanged(nameof(HasRunningItems));
        }

        private void SavePinnedState()
        {
            var pinnedConfigs = _pinnedItems
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

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
