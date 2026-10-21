using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinDock.Models;
using WinDock.Native;
using WinDock.Services;
using Application = System.Windows.Application;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace WinDock.Views
{
    public partial class MainWindow : Window
    {
        private IntPtr _hwnd = IntPtr.Zero;
        private DockManager? _dockManager;
        private ShellHookListener? _shellHook;
        private HotKeyHelper? _hotKeyHelper;
        private NotchWindow? _notchWindow;
        private SpotlightWindow? _spotlightWindow;
        private AppListWindow? _appListWindow;
        private MenuBarWindow? _menuBar;
        private WindowsKeyHook? _windowsKeyHook;
        private bool _isExplicitExit = false;
        private int _taskbarHotKeyId = -1;
        private int _spotlightHotKeyId = -1;

        private DispatcherTimer? _statusTimer;
        private DispatcherTimer? _autoHideTimer;
        private bool _isDockHidden = false;

        private readonly SettingsService _settingsService = SettingsService.Instance;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            SizeChanged += (s, e) => PositionAtBottomCenter(); // SizeToContent=Width: re-center as apps come and go
            Closed += OnWindowClosed;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // Hide from Alt+Tab switcher so WinDock runs purely as a background desktop dock
            WindowHelper.HideFromAltTab(_hwnd);

            // 1. Initialize Dock Manager & Collections (Pinned & Running separated)
            _dockManager = new DockManager(_hwnd);
            PinnedItemsControl.ItemsSource = _dockManager.PinnedItems;
            RunningItemsControl.ItemsSource = _dockManager.RunningItems;
            UpdateDividerVisibility();

            _dockManager.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(DockManager.HasRunningItems))
                {
                    Dispatcher.Invoke(UpdateDividerVisibility);
                }
            };

            // 2. Initialize Shell Hook for real-time window tracking
            _shellHook = new ShellHookListener(_hwnd);
            _shellHook.WindowCreated += hWnd => _dockManager?.HandleWindowCreated(hWnd);
            _shellHook.WindowDestroyed += hWnd => _dockManager?.HandleWindowDestroyed(hWnd);
            _shellHook.WindowActivated += hWnd =>
            {
                _dockManager?.HandleWindowActivated(hWnd);
                _dockManager?.RefreshWindows();
            };
            _shellHook.WindowRedrawn += hWnd => _dockManager?.RefreshWindows();

            // 3. Initialize Work Area Helper (prevents maximized windows from being cut off or overlapped)
            var settings = _settingsService.CurrentSettings;
            WorkAreaHelper.Initialize();
            WorkAreaHelper.TopReservedDip = MenuBarWindow.BarHeight;

            // 4. Position Dock at Bottom-Center
            PositionAtBottomCenter();

            // 5. Hide Windows Taskbar if configured & update desktop work area
            if (settings.HideWindowsTaskbar)
            {
                TaskbarController.HideTaskbar();
                if (settings.ReserveScreenSpace)
                {
                    WorkAreaHelper.SetReservedWorkArea(settings.IconSize + 26, marginDip: 8);
                }
                else
                {
                    WorkAreaHelper.SetFullScreenWorkArea();
                }
            }

            // 6. Spawn Dynamic Drop Notch Window if enabled
            if (settings.EnableDropNotch)
            {
                _notchWindow = new NotchWindow();
                _notchWindow.RequestPinToDock += path => _dockManager?.PinApplication(path);
                _notchWindow.Show();
            }

            // 7. Spawn MacBook-style Spotlight Window (Alt + Space)
            _spotlightWindow = new SpotlightWindow();
            _spotlightWindow.AttachDockManager(_dockManager!);

            // 8. Spawn Custom AppList & Power Menu Window (Launchpad)
            _appListWindow = new AppListWindow();
            _appListWindow.AttachDockManager(_dockManager!);
            _appListWindow.RequestPinToDock += path => _dockManager?.PinApplication(path);
            _appListWindow.RequestToggleNotch += ToggleNotch;

            // 8b. macOS-style top menu bar (clock, network, volume, battery), shown while the taskbar is hidden
            _menuBar = new MenuBarWindow();
            _menuBar.RequestLaunchpad += () => _appListWindow?.Toggle();
            _menuBar.RequestToggleNotch += ToggleNotch;
            SyncMenuBar();

            // 9. Setup Global Hotkeys (Customizable via Preferences)
            try
            {
                _hotKeyHelper = new HotKeyHelper(_hwnd);
                RegisterHotKeys(settings);

                _hotKeyHelper.HotKeyPressed += id =>
                {
                    if (id == _taskbarHotKeyId)
                    {
                        TaskbarController.ToggleTaskbar();
                        if (TaskbarController.IsTaskbarHidden)
                        {
                            if (_settingsService.CurrentSettings.ReserveScreenSpace)
                                WorkAreaHelper.SetReservedWorkArea(_settingsService.CurrentSettings.IconSize + 26, 8);
                            else
                                WorkAreaHelper.SetFullScreenWorkArea();
                        }
                        else
                        {
                            WorkAreaHelper.RestoreOriginalWorkArea();
                        }
                        SyncMenuBar();
                    }
                    else if (id == _spotlightHotKeyId)
                    {
                        _spotlightWindow?.Toggle();
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Hotkey registration failed: {ex.Message}");
            }

            // 9b. Intercept single Windows Key press to trigger WinDock Start Menu / Launchpad
            try
            {
                _windowsKeyHook = new WindowsKeyHook();
                _windowsKeyHook.WindowsKeyPressed += () =>
                {
                    // InvokeAsync: never block inside a low-level hook, Windows silently drops slow hooks
                    Dispatcher.InvokeAsync(() => _appListWindow?.Toggle());
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] WindowsKeyHook init failed: {ex.Message}");
            }

            // 11. Running windows sync (shell hook covers most changes; this catches the rest)
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, ev) => _dockManager?.RefreshWindows();
            _statusTimer.Start();

            // 12. Initial Window Enumeration
            _dockManager.RefreshWindows();

            // 13. Auto-Hide monitoring timer if enabled
            if (settings.AutoHideDock)
            {
                SetupAutoHideTimer();
            }

            // 14. Apply Solid-to-Glass & Border Radius Styling + System Theme adaptation
            ApplyAppearance(settings);
            _settingsService.SettingsChanged += () => Dispatcher.Invoke(() =>
            {
                var current = _settingsService.CurrentSettings;
                ApplyIntegration(current);
                ApplyAppearance(current);
                RegisterHotKeys(current);
            });

            ThemeHelper.SystemThemeChanged += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_settingsService.CurrentSettings.AutoSystemTheme)
                    {
                        ApplyAppearance(_settingsService.CurrentSettings);
                    }
                });
            };

            // 15. Background auto-update from GitHub Releases
            UpdateService.Start();
        }

        private void UpdateDividerVisibility()
        {
            if (RunningDivider != null && _dockManager != null)
            {
                RunningDivider.Visibility = _dockManager.HasRunningItems ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Applies taskbar / notch toggles from Settings live, so "Save" takes effect without restarting WinDock.
        /// </summary>
        private void ApplyIntegration(DockSettings settings)
        {
            if (settings.HideWindowsTaskbar && !TaskbarController.IsTaskbarHidden)
            {
                TaskbarController.HideTaskbar();
            }
            else if (!settings.HideWindowsTaskbar && TaskbarController.IsTaskbarHidden)
            {
                TaskbarController.ShowTaskbar();
                WorkAreaHelper.RestoreOriginalWorkArea();
            }

            SyncMenuBar();

            if (settings.EnableDropNotch != (_notchWindow?.IsVisible == true))
            {
                ToggleNotch();
            }
        }

        private void SyncMenuBar()
        {
            if (_menuBar == null) return;
            if (TaskbarController.IsTaskbarHidden) _menuBar.Show();
            else _menuBar.Hide();

            // Re-assert notch topmost so it sits on top of the bar instead of under it
            if (_notchWindow?.IsVisible == true)
            {
                _notchWindow.Topmost = false;
                _notchWindow.Topmost = true;
            }
        }

        private void RegisterHotKeys(DockSettings settings)
        {
            if (_hotKeyHelper == null) return;
            try
            {
                _hotKeyHelper.UnregisterAll();
                var (taskbarMods, taskbarKey) = HotKeyHelper.ParseHotKey(settings.TaskbarHotKey);
                var (spotlightMods, spotlightKey) = HotKeyHelper.ParseHotKey(settings.SpotlightHotKey);

                _taskbarHotKeyId = _hotKeyHelper.Register(taskbarMods, taskbarKey);
                _spotlightHotKeyId = _hotKeyHelper.Register(spotlightMods, spotlightKey);
            }
            catch { }
        }

        public void ApplyAppearance(DockSettings settings)
        {
            DockBar.CornerRadius = new CornerRadius(settings.BorderRadius);
            double barHeight = settings.IconSize + 26;
            DockBar.Height = barHeight;

            if (_dockManager != null)
            {
                foreach (var item in _dockManager.AllItems)
                {
                    item.IconSize = settings.IconSize;
                }
            }

            PositionAtBottomCenter();

            // Handle Reserve Screen Space accurately with DPI scaling
            if (TaskbarController.IsTaskbarHidden)
            {
                if (settings.ReserveScreenSpace)
                {
                    WorkAreaHelper.SetReservedWorkArea(barHeight, marginDip: 8);
                }
                else
                {
                    WorkAreaHelper.SetFullScreenWorkArea();
                }
            }

            // Handle Auto-Hide dynamically
            if (settings.AutoHideDock)
            {
                SetupAutoHideTimer();
            }
            else
            {
                _autoHideTimer?.Stop();
                if (_isDockHidden)
                {
                    AnimateDockSlide(show: true);
                }
            }

            // Launchpad & Trash tiles match app icon size, with the macOS squircle ratio
            LaunchpadTile.Width = LaunchpadTile.Height = settings.IconSize;
            LaunchpadTile.CornerRadius = new CornerRadius(settings.IconSize * 0.225);

            if (TrashTile != null)
            {
                TrashTile.Width = TrashTile.Height = settings.IconSize;
                TrashTile.CornerRadius = new CornerRadius(settings.IconSize * 0.225);
            }

            // Solid to Glass styling + Windows Dark/Light theme adaptation
            string tint = settings.DockTint ?? "#4B5563";
            if (settings.AutoSystemTheme && !ThemeHelper.IsWindowsDarkTheme())
            {
                tint = "#E2E8F0"; // Frosty light glass for Windows Light mode
            }

            Color baseColor = ThemeHelper.ParseColor(tint, Color.FromRgb(75, 85, 99));
            ThemeHelper.PaintDock(DockBar, baseColor, settings.GlassOpacity);

            // Light glass needs dark glyphs and text, otherwise white-on-light is unreadable
            bool light = ThemeHelper.IsLight(baseColor) && settings.GlassOpacity >= 0.4;
            Resources["DockGlyph"] = new SolidColorBrush(light ? Color.FromArgb(0xE6, 0x1F, 0x29, 0x37) : Color.FromArgb(0xE6, 255, 255, 255));
            Resources["DockText"] = new SolidColorBrush(light ? Color.FromRgb(0x11, 0x18, 0x27) : Color.FromArgb(0xF5, 255, 255, 255));
            Resources["DockTextMuted"] = new SolidColorBrush(light ? Color.FromArgb(0xB3, 0x11, 0x18, 0x27) : Color.FromArgb(0xB3, 255, 255, 255));
        }

        public void PositionAtBottomCenter()
        {
            double screenWidthDip = SystemParameters.PrimaryScreenWidth;
            double screenHeightDip = SystemParameters.PrimaryScreenHeight;

            double winWidth = ActualWidth > 0 ? ActualWidth : Width;
            double winHeight = ActualHeight > 0 ? ActualHeight : 140;

            Left = (screenWidthDip - winWidth) / 2;
            Top = screenHeightDip - winHeight - 8;
        }

        #region Auto-Hide Logic

        private void SetupAutoHideTimer()
        {
            if (_autoHideTimer == null)
            {
                _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _autoHideTimer.Tick += (s, e) => CheckAutoHide();
            }
            _autoHideTimer.Start();
        }

        private void CheckAutoHide()
        {
            if (!_settingsService.CurrentSettings.AutoHideDock)
            {
                if (_isDockHidden) AnimateDockSlide(show: true);
                return;
            }

            NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

            bool isNearBottom = (pt.Y >= primaryScreen.Bounds.Bottom - 4);

            NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT dockRect);
            bool isCursorInDock = (pt.X >= dockRect.Left && pt.X <= dockRect.Right &&
                                  pt.Y >= dockRect.Top && pt.Y <= dockRect.Bottom);

            if (isNearBottom || isCursorInDock)
            {
                if (_isDockHidden)
                {
                    AnimateDockSlide(show: true);
                }
            }
            else
            {
                if (!_isDockHidden)
                {
                    AnimateDockSlide(show: false);
                }
            }
        }

        private void AnimateDockSlide(bool show)
        {
            _isDockHidden = !show;

            double screenHeightDip = SystemParameters.PrimaryScreenHeight;
            double winHeight = ActualHeight > 0 ? ActualHeight : 140;

            double visibleTop = screenHeightDip - winHeight - 8;
            double hiddenTop = screenHeightDip - 4; // subtle 4px touch trigger at bottom

            double targetTop = show ? visibleTop : hiddenTop;

            var anim = new DoubleAnimation
            {
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(TopProperty, anim);
        }

        #endregion

        #region Dock Item Click & Magnification

        private void OnItemClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DockItem item)
            {
                _dockManager?.ExecuteItemClick(item);
            }
        }

        private void OnItemRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DockItem item)
            {
                var menu = new ContextMenu();

                if (item.IsRunning && item.WindowHandle != IntPtr.Zero)
                {
                    var activateItem = new MenuItem { Header = "Bring to Front" };
                    activateItem.Click += (s, ev) => WindowHelper.ActivateWindow(item.WindowHandle);
                    menu.Items.Add(activateItem);

                    var minimizeItem = new MenuItem { Header = "Minimize" };
                    minimizeItem.Click += (s, ev) => WindowHelper.MinimizeWindow(item.WindowHandle);
                    menu.Items.Add(minimizeItem);

                    var closeItem = new MenuItem { Header = "Close Window" };
                    closeItem.Click += (s, ev) => WindowHelper.CloseWindow(item.WindowHandle);
                    menu.Items.Add(closeItem);

                    menu.Items.Add(new Separator());
                }

                if (item.IsPinned)
                {
                    var unpinItem = new MenuItem { Header = "📌 Unpin from Dock" };
                    unpinItem.Click += (s, ev) => _dockManager?.UnpinItem(item);
                    menu.Items.Add(unpinItem);
                }
                else
                {
                    var pinItem = new MenuItem { Header = "📌 Keep in Dock (Pin)" };
                    pinItem.Click += (s, ev) =>
                    {
                        if (!string.IsNullOrEmpty(item.LaunchPath))
                        {
                            _dockManager?.PinApplication(item.LaunchPath);
                        }
                    };
                    menu.Items.Add(pinItem);
                }

                menu.IsOpen = true;
            }
        }

        private void OnDockItemsMouseMove(object sender, MouseEventArgs e)
        {
            if (_dockManager == null) return;

            var settings = _settingsService.CurrentSettings;
            double maxScale = settings.MagnificationScale;
            Point mousePosInRoot = e.GetPosition(RootGrid);

            DockItem? closestItem = null;
            double minDistance = double.MaxValue;
            Point closestItemPosInRoot = new Point(0, 0);

            // Magnify across both Pinned and Running items
            foreach (var item in _dockManager.AllItems)
            {
                FrameworkElement? container = null;

                if (PinnedItemsControl.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement pElem)
                    container = pElem;
                else if (RunningItemsControl.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement rElem)
                    container = rElem;

                if (container != null)
                {
                    Point itemPosInRoot = container.TranslatePoint(new Point(container.ActualWidth / 2, container.ActualHeight / 2), RootGrid);
                    double dist = Math.Abs(mousePosInRoot.X - itemPosInRoot.X);
                    double spread = settings.IconSize * 2.2;

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestItem = item;
                        closestItemPosInRoot = itemPosInRoot;
                    }

                    if (maxScale > 1.01 && dist < spread)
                    {
                        double factor = Math.Cos((dist / spread) * (Math.PI / 2));
                        item.Scale = 1.0 + (maxScale - 1.0) * factor;
                    }
                    else
                    {
                        item.Scale = 1.0;
                    }
                }
            }

            // Smooth Mac Tooltip Bubble tracking
            if (closestItem != null && minDistance < settings.IconSize * 1.5)
            {
                ShowMacTooltip(closestItem.Title, closestItemPosInRoot.X);
            }
            else
            {
                HideMacTooltip();
            }
        }

        private void ShowMacTooltip(string text, double centerXInRoot)
        {
            if (MacTooltipBubble == null || MacTooltipText == null) return;

            MacTooltipText.Text = text;
            MacTooltipBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bubbleWidth = MacTooltipBubble.DesiredSize.Width;

            double left = Math.Max(0, centerXInRoot - (bubbleWidth / 2));
            MacTooltipBubble.Margin = new Thickness(left, 0, 0, 6);

            if (MacTooltipBubble.Opacity < 0.95)
            {
                var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                MacTooltipBubble.BeginAnimation(OpacityProperty, anim);
            }
        }

        private void HideMacTooltip()
        {
            if (MacTooltipBubble == null) return;
            if (MacTooltipBubble.Opacity > 0.05)
            {
                var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                MacTooltipBubble.BeginAnimation(OpacityProperty, anim);
            }
        }

        #endregion

        #region System Tray Widgets & Controller

        private void OnStartButtonClicked(object sender, RoutedEventArgs e)
        {
            _appListWindow?.Toggle();
        }

        public void ToggleNotch()
        {
            if (_notchWindow == null)
            {
                _notchWindow = new NotchWindow();
                _notchWindow.RequestPinToDock += path => _dockManager?.PinApplication(path);
                _notchWindow.Show();
            }
            else
            {
                if (_notchWindow.IsVisible)
                {
                    _notchWindow.Hide();
                }
                else
                {
                    _notchWindow.Show();
                    _notchWindow.PositionAtTopCenter();
                }
            }
        }

        private void OnRecycleBinClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", "shell:RecycleBinFolder");
            }
            catch { }
        }

        private void OnDockMouseEnter(object sender, MouseEventArgs e)
        {
        }

        private void OnDockMouseLeave(object sender, MouseEventArgs e)
        {
            if (_dockManager != null)
            {
                foreach (var item in _dockManager.AllItems)
                {
                    item.Scale = 1.0;
                }
            }
            HideMacTooltip();
        }

        #endregion

        #region Drag and Drop onto Dock to Pin

        private void OnDockDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
        }

        private void OnDockDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
        }

        private void OnDockDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string file in files)
                {
                    _dockManager?.PinApplication(file);
                }
                e.Handled = true;
            }
        }

        #endregion

        public void ExitApplication()
        {
            _isExplicitExit = true;
            Application.Current.Shutdown();
        }

        public void OpenHelpWindow()
        {
            var help = new HelpWindow();
            help.Owner = this;
            help.Show();
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            // F1: Open WinDock Shortcuts & Help Guide
            if (e.Key == Key.F1)
            {
                OpenHelpWindow();
                e.Handled = true;
                return;
            }

            // Block Alt+F4 from closing the background dock
            if (e.Key == Key.System && e.SystemKey == Key.F4)
            {
                e.Handled = true;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true; // Prevent closing from Alt+Tab or system close commands
                return;
            }
            base.OnClosing(e);
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            // CRITICAL SAFETY: Always restore Windows Taskbar & desktop WorkArea when WinDock exits
            WorkAreaHelper.RestoreOriginalWorkArea();
            TaskbarController.ShowTaskbar();

            _statusTimer?.Stop();
            _autoHideTimer?.Stop();

            _windowsKeyHook?.Dispose();
            _shellHook?.Dispose();
            _hotKeyHelper?.Dispose();
            _notchWindow?.Close();
            _spotlightWindow?.Close();
            _appListWindow?.Close();
            _menuBar?.Close();
        }
    }
}
