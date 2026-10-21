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
            SizeChanged += (s, e) => PositionAtBottomCenter();
            Closed += OnWindowClosed;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // Hide from Alt+Tab switcher so WinDock runs purely as a background desktop dock
            WindowHelper.HideFromAltTab(_hwnd);

            // Apply DWM Blur and Theme now that HWND exists
            ApplyAppearance(_settingsService.CurrentSettings);

            DockBar.SizeChanged += (s, ev) =>
            {
                PositionAtBottomCenter();
                ThemeHelper.UpdateDockWindowRegion(_hwnd, DockBar, _settingsService.CurrentSettings.BorderRadius);
            };

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
                _notchWindow?.EnsureTopmost();
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
            }
            UpdateWorkArea(settings);

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
            _menuBar.RequestReassertNotch += () => _notchWindow?.EnsureTopmost();
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
                        SyncMenuBar();
                        UpdateWorkArea(_settingsService.CurrentSettings);
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
            if (TaskbarController.IsTaskbarHidden)
            {
                _menuBar.Show();
                WorkAreaHelper.TopReservedDip = MenuBarWindow.BarHeight;
            }
            else
            {
                _menuBar.Hide();
                WorkAreaHelper.TopReservedDip = 0;
            }

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
            double barHeight = settings.IconSize + 28;
            DockBar.Height = barHeight;
            Height = barHeight;

            if (_dockManager != null)
            {
                foreach (var item in _dockManager.AllItems)
                {
                    item.IconSize = settings.IconSize;
                }
            }

            PositionAtBottomCenter();

            // Handle Reserve Screen Space accurately with DPI scaling
            UpdateWorkArea(settings);

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

            // Launchpad, Downloads & Trash tiles match app icon size, with the macOS squircle ratio
            LaunchpadTile.Width = LaunchpadTile.Height = settings.IconSize;
            LaunchpadTile.CornerRadius = new CornerRadius(settings.IconSize * 0.225);

            if (DownloadsTile != null)
            {
                DownloadsTile.Width = DownloadsTile.Height = settings.IconSize;
                DownloadsTile.CornerRadius = new CornerRadius(settings.IconSize * 0.225);
            }

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
            if (DockBar != null)
            {
                DockBar.CornerRadius = new CornerRadius(settings.BorderRadius);
                ThemeHelper.PaintDock(DockBar, baseColor, settings.GlassOpacity, settings.BlurIntensity);
            }

            if (_hwnd != IntPtr.Zero)
            {
                ThemeHelper.ApplyWindowBlur(_hwnd, baseColor, settings.BlurIntensity);
                if (DockBar != null)
                {
                    ThemeHelper.UpdateDockWindowRegion(_hwnd, DockBar, settings.BorderRadius);
                }
            }

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
            double barHeight = (_settingsService?.CurrentSettings != null)
                ? _settingsService.CurrentSettings.IconSize + 28
                : (DockBar != null && DockBar.Height > 0 ? DockBar.Height : 80);

            double bottomOffset = (!(_settingsService?.CurrentSettings?.HideWindowsTaskbar ?? true))
                ? WorkAreaHelper.GetTaskbarHeightDip()
                : 0;

            Left = (screenWidthDip - winWidth) / 2;
            Top = screenHeightDip - barHeight - 8 - bottomOffset;

            if (_hwnd != IntPtr.Zero && DockBar != null)
            {
                ThemeHelper.UpdateDockWindowRegion(_hwnd, DockBar, _settingsService?.CurrentSettings?.BorderRadius ?? 28);
            }
        }

        private void UpdateWorkArea(DockSettings settings)
        {
            double barHeight = settings.IconSize + 28;

            if (settings.HideWindowsTaskbar)
            {
                if (!TaskbarController.IsTaskbarHidden)
                {
                    TaskbarController.HideTaskbar();
                }
            }
            else
            {
                if (TaskbarController.IsTaskbarHidden)
                {
                    TaskbarController.ShowTaskbar();
                }
            }

            if (settings.ReserveScreenSpace)
            {
                // Dock is placed at Top = screenHeight - barHeight - 8.
                // Reserving (barHeight + 8) causes maximized windows to stop flush against the top edge of WinDock.
                double margin = 8;
                WorkAreaHelper.SetReservedWorkArea(barHeight, marginDip: margin);
                AppBarHelper.SetReservedSpace(barHeight, marginDip: margin);
            }
            else
            {
                AppBarHelper.ClearReservedSpace();

                if (settings.HideWindowsTaskbar)
                {
                    WorkAreaHelper.SetFullScreenWorkArea();
                }
                else
                {
                    WorkAreaHelper.RestoreOriginalWorkArea();
                }
            }
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

        private Point _dockDragStart;
        private bool _isDraggingDockItem = false;

        public void OnDockItemPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _dockDragStart = e.GetPosition(this);
                _isDraggingDockItem = false;
            }
        }

        public void OnDockItemPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingDockItem && sender is FrameworkElement elem && elem.DataContext is DockItem item)
            {
                Point current = e.GetPosition(this);
                Vector diff = _dockDragStart - current;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDraggingDockItem = true;
                    HideMacTooltip();
                    var data = new System.Windows.DataObject("WinDockItem", item);
                    DragDrop.DoDragDrop(elem, data, DragDropEffects.Move);
                    _isDraggingDockItem = false;
                }
            }
        }

        public void OnDockItemDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("WinDockItem"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        public void OnDockItemDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("WinDockItem") &&
                e.Data.GetData("WinDockItem") is DockItem source &&
                sender is FrameworkElement elem &&
                elem.DataContext is DockItem target &&
                source != target)
            {
                _dockManager?.ReorderItems(source, target);
                e.Handled = true;
            }
        }

        private void OnItemClick(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingDockItem) return;
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
            FrameworkElement? closestContainer = null;
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
                        closestContainer = container;
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

            // Smooth Mac Tooltip Bubble tracking relative to DockBar
            if (closestItem != null && minDistance < settings.IconSize * 0.7 && closestContainer != null)
            {
                Point posInDock = closestContainer.TranslatePoint(new Point(closestContainer.ActualWidth / 2, 0), DockBar);
                ShowMacTooltip(closestItem.Title, posInDock.X);
            }
            else if (minDistance >= settings.IconSize * 0.9)
            {
                HideMacTooltip();
            }
        }

        private void ShowMacTooltip(string text, double centerXInDock)
        {
            if (MacTooltipBubble == null || MacTooltipText == null || MacTooltipPopup == null) return;

            MacTooltipText.Text = text;
            MacTooltipBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bubbleWidth = MacTooltipBubble.DesiredSize.Width;

            // Center bubble strictly above the hovered icon relative to DockBar
            double dockWidth = DockBar.ActualWidth > 0 ? DockBar.ActualWidth : ActualWidth;
            double left = Math.Clamp(centerXInDock - (bubbleWidth / 2), 4, Math.Max(4, dockWidth - bubbleWidth - 4));

            MacTooltipPopup.HorizontalOffset = left;
            MacTooltipPopup.VerticalOffset = -6;

            if (!MacTooltipPopup.IsOpen)
            {
                MacTooltipPopup.IsOpen = true;
            }
            else
            {
                // Force WPF Popup position recalculation during horizontal gliding
                MacTooltipPopup.HorizontalOffset = left + 0.001;
                MacTooltipPopup.HorizontalOffset = left;
            }
        }

        private void HideMacTooltip()
        {
            if (MacTooltipPopup != null && MacTooltipPopup.IsOpen)
            {
                MacTooltipPopup.IsOpen = false;
            }
        }

        private void OnDockItemMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DockItem item)
            {
                Point centerInDock = elem.TranslatePoint(new Point(elem.ActualWidth / 2, 0), DockBar);
                ShowMacTooltip(item.Title, centerInDock.X);
            }
        }

        private void OnDockItemMouseLeave(object sender, MouseEventArgs e)
        {
            HideMacTooltip();
        }

        private void OnLaunchpadMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                Point centerInDock = elem.TranslatePoint(new Point(elem.ActualWidth / 2, 0), DockBar);
                ShowMacTooltip("Launchpad", centerInDock.X);
            }
        }

        private void OnDownloadsMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                Point centerInDock = elem.TranslatePoint(new Point(elem.ActualWidth / 2, 0), DockBar);
                ShowMacTooltip("Downloads", centerInDock.X);
            }
        }

        private void OnTrashMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                Point centerInDock = elem.TranslatePoint(new Point(elem.ActualWidth / 2, 0), DockBar);
                ShowMacTooltip("Recycle Bin", centerInDock.X);
            }
        }

        private void OnSpecialTileMouseLeave(object sender, MouseEventArgs e)
        {
            HideMacTooltip();
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

        private void OnDownloadsClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (System.IO.Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
            }
            catch { }
        }

        private void OnDownloadsDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                if (DownloadsTile != null) DownloadsTile.Background = new SolidColorBrush(Color.FromArgb(0x4D, 0x3B, 0x82, 0xF6));
                e.Handled = true;
            }
        }

        private void OnDownloadsDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnDownloadsDragLeave(object sender, DragEventArgs e)
        {
            if (DownloadsTile != null) DownloadsTile.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
        }

        private void OnDownloadsDrop(object sender, DragEventArgs e)
        {
            if (DownloadsTile != null) DownloadsTile.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                string targetDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (System.IO.Directory.Exists(targetDir))
                {
                    foreach (string file in files)
                    {
                        try
                        {
                            string dest = System.IO.Path.Combine(targetDir, System.IO.Path.GetFileName(file));
                            if (System.IO.File.Exists(file)) System.IO.File.Copy(file, dest, true);
                            else if (System.IO.Directory.Exists(file)) CopyDirectory(file, dest);
                        }
                        catch { }
                    }
                }
                e.Handled = true;
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            System.IO.Directory.CreateDirectory(destDir);
            foreach (var file in System.IO.Directory.GetFiles(sourceDir))
            {
                System.IO.File.Copy(file, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file)), true);
            }
            foreach (var subDir in System.IO.Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(subDir, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(subDir)));
            }
        }

        private void OnTrashDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Move;
                if (TrashTile != null) TrashTile.Background = new SolidColorBrush(Color.FromArgb(0x66, 0xEF, 0x44, 0x44));
                e.Handled = true;
            }
        }

        private void OnTrashDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void OnTrashDragLeave(object sender, DragEventArgs e)
        {
            if (TrashTile != null) TrashTile.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
        }

        private void OnTrashDrop(object sender, DragEventArgs e)
        {
            if (TrashTile != null) TrashTile.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (string file in files)
                {
                    MoveToRecycleBin(file);
                }
                e.Handled = true;
            }
        }

        public static void MoveToRecycleBin(string path)
        {
            try
            {
                var shf = new NativeMethods.SHFILEOPSTRUCT
                {
                    wFunc = NativeMethods.FO_DELETE,
                    pFrom = path + '\0' + '\0',
                    fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT
                };
                NativeMethods.SHFileOperation(ref shf);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecycleBin] Error moving {path}: {ex.Message}");
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
            AppBarHelper.ClearReservedSpace();
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
