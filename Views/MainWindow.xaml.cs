using System;
using System.Diagnostics;
using System.IO;
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

namespace WinDock.Views
{
    public partial class MainWindow : Window
    {
        private IntPtr _hwnd = IntPtr.Zero;
        private DockManager? _dockManager;
        private ShellHookListener? _shellHook;
        private AppBarHelper? _appBarHelper;
        private HotKeyHelper? _hotKeyHelper;
        private NotchWindow? _notchWindow;
        private SpotlightWindow? _spotlightWindow;
        private AppListWindow? _appListWindow;

        private DispatcherTimer? _clockTimer;
        private DispatcherTimer? _autoHideTimer;
        private bool _isDockHidden = false;

        private readonly SettingsService _settingsService = SettingsService.Instance;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // 1. Initialize Dock Manager & Collections
            _dockManager = new DockManager(_hwnd);
            DockItemsControl.ItemsSource = _dockManager.DockItems;

            // 2. Initialize Shell Hook for real-time window tracking
            _shellHook = new ShellHookListener(_hwnd);
            _shellHook.WindowCreated += hWnd => _dockManager?.HandleWindowCreated(hWnd);
            _shellHook.WindowDestroyed += hWnd => _dockManager?.HandleWindowDestroyed(hWnd);
            _shellHook.WindowActivated += hWnd => _dockManager?.HandleWindowActivated(hWnd);
            _shellHook.WindowRedrawn += hWnd => _dockManager?.RefreshWindows();

            // 3. Initialize Work Area Helper (prevents maximized windows from being cut off)
            var settings = _settingsService.CurrentSettings;
            WorkAreaHelper.Initialize();

            // 4. Position Dock at Bottom-Center
            PositionAtBottomCenter();

            // 5. Hide Windows Taskbar if configured & update desktop work area
            if (settings.HideWindowsTaskbar)
            {
                TaskbarController.HideTaskbar();
                UpdateTaskbarIndicator(true);
                if (settings.ReserveScreenSpace)
                {
                    WorkAreaHelper.SetReservedWorkArea((int)settings.IconSize + 20);
                }
                else
                {
                    WorkAreaHelper.SetFullScreenWorkArea();
                }
            }
            else
            {
                UpdateTaskbarIndicator(false);
            }

            // 7. Spawn Dynamic Drop Notch Window if enabled
            if (settings.EnableDropNotch)
            {
                _notchWindow = new NotchWindow();
                _notchWindow.RequestPinToDock += path => _dockManager?.PinApplication(path);
                _notchWindow.Show();
            }

            // 8. Spawn MacBook-style Spotlight Window
            _spotlightWindow = new SpotlightWindow();
            _spotlightWindow.AttachDockManager(_dockManager!);

            // 9. Spawn Custom AppList & Power Menu Window
            _appListWindow = new AppListWindow();
            _appListWindow.AttachDockManager(_dockManager!);
            _appListWindow.RequestPinToDock += path => _dockManager?.PinApplication(path);

            // 9. Setup Global Hotkeys:
            //    - Win + Alt + D: Toggle Taskbar
            //    - Alt + Space: Toggle MacBook-style Spotlight Launcher
            try
            {
                _hotKeyHelper = new HotKeyHelper(_hwnd);
                int taskbarHotKeyId = _hotKeyHelper.Register(NativeMethods.MOD_WIN | NativeMethods.MOD_ALT, (uint)Keys.D);
                int spotlightHotKeyId = _hotKeyHelper.Register(NativeMethods.MOD_ALT, (uint)Keys.Space);

                _hotKeyHelper.HotKeyPressed += id =>
                {
                    if (id == taskbarHotKeyId)
                    {
                        OnToggleWindowsTaskbarClicked(this, new RoutedEventArgs());
                    }
                    else if (id == spotlightHotKeyId)
                    {
                        _spotlightWindow?.Toggle();
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Hotkey registration failed: {ex.Message}");
            }

            // 8. Start Digital Clock Timer
            UpdateClock();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, ev) => UpdateClock();
            _clockTimer.Start();

            // 9. Initial Window Enumeration
            _dockManager.RefreshWindows();

            // 10. Auto-Hide monitoring timer if enabled
            if (settings.AutoHideDock)
            {
                SetupAutoHideTimer();
            }

            // 11. Apply Solid-to-Glass & Border Radius Styling
            ApplyAppearance(settings);
            _settingsService.SettingsChanged += () => Dispatcher.Invoke(() => ApplyAppearance(_settingsService.CurrentSettings));
        }

        public void ApplyAppearance(DockSettings settings)
        {
            DockBar.CornerRadius = new CornerRadius(settings.BorderRadius);
            DockBar.Height = settings.IconSize + 18;

            if (_dockManager != null)
            {
                foreach (var item in _dockManager.DockItems)
                {
                    item.IconSize = settings.IconSize;
                }
            }
            PositionAtBottomCenter();

            if (TaskbarController.IsTaskbarHidden)
            {
                if (settings.ReserveScreenSpace)
                {
                    WorkAreaHelper.SetReservedWorkArea((int)settings.IconSize + 20);
                }
                else
                {
                    WorkAreaHelper.SetFullScreenWorkArea();
                }
            }

            byte alpha = (byte)Math.Clamp((int)(settings.GlassOpacity * 255), 25, 255);

            System.Windows.Media.Color baseColor = System.Windows.Media.Color.FromRgb(75, 85, 99); // #4B5563
            try
            {
                if (!string.IsNullOrEmpty(settings.DockTint))
                {
                    var converted = System.Windows.Media.ColorConverter.ConvertFromString(settings.DockTint);
                    if (converted is System.Windows.Media.Color c) baseColor = c;
                }
            }
            catch { }

            if (settings.GlassOpacity >= 0.95)
            {
                // Pure Solid matte background (as requested by user)
                DockBar.Background = new SolidColorBrush(baseColor);
                DockBar.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255));
            }
            else
            {
                // Frosted Glass gradient
                var col1 = System.Windows.Media.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
                byte darkerAlpha = (byte)Math.Min(255, alpha + 20);
                var col2 = System.Windows.Media.Color.FromArgb(darkerAlpha, (byte)(baseColor.R * 0.7), (byte)(baseColor.G * 0.7), (byte)(baseColor.B * 0.7));

                var gradient = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(col1, 0.0),
                        new GradientStop(col2, 1.0)
                    }
                };
                DockBar.Background = gradient;
                DockBar.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(alpha * 0.35 + 25), 255, 255, 255));
            }
        }

        public void PositionAtBottomCenter()
        {
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var bounds = primaryScreen.Bounds;

            Left = bounds.Left + (bounds.Width - Width) / 2;
            Top = bounds.Bottom - Height - 8;
        }

        private void UpdateClock()
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
            DateText.Text = DateTime.Now.ToString("ddd, MMM d");
        }

        private void UpdateTaskbarIndicator(bool isHidden)
        {
            if (isHidden)
            {
                TaskbarStatusIndicator.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                TaskbarStatusIndicator.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 239, 68, 68));
                BtnTaskbarToggle.ToolTip = "Windows Taskbar: Hidden (Click to Show)";
            }
            else
            {
                TaskbarStatusIndicator.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // Green
                TaskbarStatusIndicator.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 16, 185, 129));
                BtnTaskbarToggle.ToolTip = "Windows Taskbar: Visible (Click to Hide)";
            }
        }

        #region Auto-Hide Logic

        private void SetupAutoHideTimer()
        {
            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _autoHideTimer.Tick += (s, e) =>
            {
                if (!_settingsService.CurrentSettings.AutoHideDock) return;

                NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
                Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

                bool isNearBottom = pt.Y >= primaryScreen.Bounds.Bottom - 4;
                bool isMouseOverDock = IsMouseOver;

                if (isNearBottom || isMouseOverDock)
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
            };
            _autoHideTimer.Start();
        }

        private void AnimateDockSlide(bool show)
        {
            _isDockHidden = !show;
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

            double targetTop = show ? (primaryScreen.Bounds.Bottom - Height - 8) : (primaryScreen.Bounds.Bottom - 6);

            var anim = new DoubleAnimation
            {
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(200),
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

                    menu.Items.Add(new Separator());
                }

                if (item.IsPinned)
                {
                    var unpinItem = new MenuItem { Header = "Unpin from Dock" };
                    unpinItem.Click += (s, ev) => _dockManager?.UnpinItem(item);
                    menu.Items.Add(unpinItem);
                }
                else
                {
                    var pinItem = new MenuItem { Header = "Pin to Dock" };
                    pinItem.Click += (s, ev) =>
                    {
                        if (!string.IsNullOrEmpty(item.LaunchPath))
                        {
                            _dockManager?.PinApplication(item.LaunchPath);
                        }
                    };
                    menu.Items.Add(pinItem);
                }

                if (!string.IsNullOrEmpty(item.LaunchPath))
                {
                    var adminItem = new MenuItem { Header = "Run as Administrator" };
                    adminItem.Click += (s, ev) => DockManager.LaunchProcess(item.LaunchPath, true);
                    menu.Items.Add(adminItem);
                }

                if (item.IsRunning && item.WindowHandle != IntPtr.Zero)
                {
                    menu.Items.Add(new Separator());
                    var closeItem = new MenuItem { Header = "Close Window" };
                    closeItem.Click += (s, ev) => WindowHelper.CloseWindow(item.WindowHandle);
                    menu.Items.Add(closeItem);
                }

                menu.IsOpen = true;
            }
        }

        private void OnItemMouseEnter(object sender, MouseEventArgs e)
        {
        }

        private void OnItemMouseLeave(object sender, MouseEventArgs e)
        {
        }

        private void OnDockItemsMouseMove(object sender, MouseEventArgs e)
        {
            if (_dockManager == null) return;
            var settings = _settingsService.CurrentSettings;
            double maxScale = settings.MagnificationScale;

            Point mousePos = e.GetPosition(DockItemsControl);
            double spread = settings.IconSize * 2.2;

            DockItem? closestItem = null;
            double minDistance = double.MaxValue;
            Point closestItemPosInRoot = new Point(0, 0);

            for (int i = 0; i < DockItemsControl.Items.Count; i++)
            {
                if (DockItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
                {
                    Point itemCenterInDock = container.TranslatePoint(
                        new Point(container.ActualWidth / 2, container.ActualHeight / 2),
                        DockItemsControl);

                    double dist = Math.Abs(mousePos.X - itemCenterInDock.X);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestItem = DockItemsControl.Items[i] as DockItem;
                        closestItemPosInRoot = container.TranslatePoint(new Point(container.ActualWidth / 2, 0), RootGrid);
                    }

                    if (maxScale > 1.01 && dist < spread)
                    {
                        double factor = Math.Cos((dist / spread) * (Math.PI / 2));
                        double targetScale = 1.0 + (maxScale - 1.0) * factor;
                        if (DockItemsControl.Items[i] is DockItem item)
                        {
                            item.Scale = targetScale;
                        }
                    }
                    else
                    {
                        if (DockItemsControl.Items[i] is DockItem item)
                        {
                            item.Scale = 1.0;
                        }
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

        private void OnToggleWindowsTaskbarClicked(object sender, RoutedEventArgs e)
        {
            TaskbarController.ToggleTaskbar();
            bool isHidden = TaskbarController.IsTaskbarHidden;
            UpdateTaskbarIndicator(isHidden);

            if (isHidden)
            {
                if (_settingsService.CurrentSettings.ReserveScreenSpace)
                {
                    WorkAreaHelper.SetReservedWorkArea((int)_settingsService.CurrentSettings.IconSize + 20);
                }
                else
                {
                    WorkAreaHelper.SetFullScreenWorkArea();
                }
            }
            else
            {
                WorkAreaHelper.RestoreOriginalWorkArea();
            }
        }

        private void OnSpotlightClicked(object sender, RoutedEventArgs e)
        {
            _spotlightWindow?.Toggle();
        }

        private void OnToggleNotchClicked(object sender, RoutedEventArgs e)
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

        private void OnClockClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open Windows Date & Time Settings
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:dateandtime", UseShellExecute = true });
            }
            catch
            {
                // Ignore
            }
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            if (settingsWindow.ShowDialog() == true)
            {
                // Reload settings
                var current = _settingsService.CurrentSettings;
                if (current.HideWindowsTaskbar && !TaskbarController.IsTaskbarHidden)
                {
                    TaskbarController.HideTaskbar();
                    UpdateTaskbarIndicator(true);
                }
                else if (!current.HideWindowsTaskbar && TaskbarController.IsTaskbarHidden)
                {
                    TaskbarController.ShowTaskbar();
                    UpdateTaskbarIndicator(false);
                }

                if (current.ReserveScreenSpace && _appBarHelper == null)
                {
                    _appBarHelper = new AppBarHelper(_hwnd);
                    _appBarHelper.Register(NativeMethods.ABE_BOTTOM, 75);
                }
                else if (!current.ReserveScreenSpace && _appBarHelper != null)
                {
                    _appBarHelper.Unregister();
                    _appBarHelper = null;
                }

                _dockManager?.RefreshWindows();
            }
        }

        private void OnDockMouseEnter(object sender, MouseEventArgs e)
        {
        }

        private void OnDockMouseLeave(object sender, MouseEventArgs e)
        {
            if (_dockManager != null)
            {
                foreach (var item in _dockManager.DockItems)
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

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            // CRITICAL SAFETY: Always restore Windows Taskbar & desktop WorkArea when WinDock exits
            WorkAreaHelper.RestoreOriginalWorkArea();
            TaskbarController.ShowTaskbar();

            _appBarHelper?.Unregister();
            _shellHook?.Dispose();
            _hotKeyHelper?.Dispose();
            _notchWindow?.Close();
            _spotlightWindow?.Close();
            _appListWindow?.Close();
        }
    }
}
