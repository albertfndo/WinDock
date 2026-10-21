using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Animation;
using WinDock.Models;
using WinDock.Native;
using WinDock.Services;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace WinDock.Views
{
    public enum NotchTab
    {
        Media,
        Shelf,
        Clipboard,
        System,
        Timer
    }

    public partial class NotchWindow : Window
    {
        private readonly ObservableCollection<NotchItem> _items = new();
        private bool _isExpanded;
        private System.Windows.Point _dragStartPoint;
        private readonly Storyboard? _expandAnimation;
        private readonly Storyboard? _collapseAnimation;
        private readonly Storyboard? _equalizerStoryboard;
        private readonly SystemMonitorService _systemMonitor;
        private readonly PomodoroTimerService _timerService;
        private readonly MediaControllerService _mediaService;
        private NotchTab _currentTab = NotchTab.Shelf;
        private System.Windows.Threading.DispatcherTimer? _topmostTimer;
        private System.Windows.Threading.DispatcherTimer? _hoverExpandTimer;

        private static readonly Brush AmberBgBrush = (Brush)new BrushConverter().ConvertFromString("#25F59E0B")!;
        private static readonly Brush AmberFgBrush = (Brush)new BrushConverter().ConvertFromString("#FBBF24")!;
        private static readonly Brush AmberBorderBrush = (Brush)new BrushConverter().ConvertFromString("#50F59E0B")!;

        private static readonly Brush BlueBgBrush = (Brush)new BrushConverter().ConvertFromString("#250A84FF")!;
        private static readonly Brush BlueFgBrush = (Brush)new BrushConverter().ConvertFromString("#38BDF8")!;
        private static readonly Brush BlueBorderBrush = (Brush)new BrushConverter().ConvertFromString("#500A84FF")!;

        private static readonly Brush RoseBgBrush = (Brush)new BrushConverter().ConvertFromString("#25F43F5E")!;
        private static readonly Brush RoseFgBrush = (Brush)new BrushConverter().ConvertFromString("#FB7185")!;
        private static readonly Brush RoseBorderBrush = (Brush)new BrushConverter().ConvertFromString("#50F43F5E")!;

        private static readonly Brush EmeraldFgBrush = (Brush)new BrushConverter().ConvertFromString("#10B981")!;
        private static readonly Brush MutedFgBrush = (Brush)new BrushConverter().ConvertFromString("#A1A1AA")!;

        private static readonly Brush SpotifyBgBrush = (Brush)new BrushConverter().ConvertFromString("#2010B981")!;
        private static readonly Brush SpotifyBorderBrush = (Brush)new BrushConverter().ConvertFromString("#4010B981")!;
        private static readonly Brush YoutubeBgBrush = (Brush)new BrushConverter().ConvertFromString("#20F43F5E")!;
        private static readonly Brush YoutubeFgBrush = (Brush)new BrushConverter().ConvertFromString("#FB7185")!;
        private static readonly Brush YoutubeBorderBrush = (Brush)new BrushConverter().ConvertFromString("#40F43F5E")!;

        public event Action<string>? RequestPinToDock;

        public NotchWindow()
        {
            InitializeComponent();

            NotchItemsControl.ItemsSource = _items;
            _items.CollectionChanged += (s, e) => UpdateBadge();

            ClipboardItemsControl.ItemsSource = ClipboardHistoryService.Instance.History;
            ClipboardHistoryService.Instance.History.CollectionChanged += OnClipboardHistoryChanged;

            _expandAnimation = (Storyboard)FindResource("ExpandStoryboard");
            _collapseAnimation = (Storyboard)FindResource("CollapseStoryboard");
            _equalizerStoryboard = (Storyboard)FindResource("EqualizerStoryboard");

            _systemMonitor = SystemMonitorService.Instance;
            _systemMonitor.StatsUpdated += OnSystemStatsUpdated;

            _timerService = PomodoroTimerService.Instance;
            _timerService.TimerTicked += OnTimerTicked;
            _timerService.TimerCompleted += OnTimerCompleted;

            _mediaService = MediaControllerService.Instance;
            _mediaService.MediaStateChanged += OnMediaStateChanged;

            Loaded += OnLoaded;
            Deactivated += (s, e) => EnsureTopmost();
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            WindowHelper.HideFromAltTab(hwnd);
            PositionAtTopCenter();
            EnsureTopmost();

            _topmostTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _topmostTimer.Tick += (s, ev) => EnsureTopmost();
            _topmostTimer.Start();

            _systemMonitor.Start();
            _mediaService.Start();
            ClipboardHistoryService.Instance.Attach(hwnd);
            UpdateClipboardUi();
            UpdateTimerUi();
            UpdateMediaUi();
            UpdateCompactPill();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _topmostTimer?.Stop();
            _hoverExpandTimer?.Stop();
            ClipboardHistoryService.Instance.Detach();
            ClipboardHistoryService.Instance.History.CollectionChanged -= OnClipboardHistoryChanged;
            _systemMonitor.StatsUpdated -= OnSystemStatsUpdated;
            _timerService.TimerTicked -= OnTimerTicked;
            _timerService.TimerCompleted -= OnTimerCompleted;
            _mediaService.MediaStateChanged -= OnMediaStateChanged;
            _mediaService.Stop();
        }

        public void PositionAtTopCenter()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - Width) / 2;
            // Always flush against top screen bezel (Top = 0) like a genuine MacBook Notch
            Top = 0;
            EnsureTopmost();
        }

        public void EnsureTopmost()
        {
            Dispatcher.InvokeAsync(() =>
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero && IsVisible)
                {
                    NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                }
            });
        }

        private void OnHideNotchClicked(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void OnNotchRightClick(object sender, MouseButtonEventArgs e)
        {
            var menu = new System.Windows.Controls.ContextMenu();
            var hideItem = new System.Windows.Controls.MenuItem { Header = "✕ Hide Dynamic Drop Notch" };
            hideItem.Click += (s, ev) => Hide();
            menu.Items.Add(hideItem);

            if (_items.Count > 0)
            {
                var clearItem = new System.Windows.Controls.MenuItem { Header = "🗑 Clear All Items" };
                clearItem.Click += (s, ev) => OnClearAllClicked(sender, new RoutedEventArgs());
                menu.Items.Add(clearItem);
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        private void ExpandNotch()
        {
            if (_isExpanded) return;
            _isExpanded = true;

            CompactView.Visibility = Visibility.Collapsed;
            ExpandedView.Visibility = Visibility.Visible;
            _expandAnimation?.Begin();
        }

        private void CollapseNotch()
        {
            if (!_isExpanded) return;
            _isExpanded = false;

            _collapseAnimation?.Begin();

            // After animation ends, switch views
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (!_isExpanded)
                {
                    ExpandedView.Visibility = Visibility.Collapsed;
                    CompactView.Visibility = Visibility.Visible;
                }
            };
            timer.Start();
        }

        private void UpdateBadge()
        {
            int count = _items.Count;
            if (count > 0)
            {
                EmptyDropPlaceholder.Visibility = Visibility.Collapsed;
                ItemsScrollViewer.Visibility = Visibility.Visible;
                if (_currentTab == NotchTab.Shelf)
                {
                    HeaderClearAllButton.Visibility = Visibility.Visible;
                }
            }
            else
            {
                EmptyDropPlaceholder.Visibility = Visibility.Visible;
                ItemsScrollViewer.Visibility = Visibility.Collapsed;
                HeaderClearAllButton.Visibility = Visibility.Collapsed;
            }

            UpdateCompactPill();
        }

        private void UpdateCompactPill()
        {
            bool isTimerActive = _timerService.State == TimerState.Running || _timerService.State == TimerState.Paused;
            bool isTimerCompleted = _timerService.State == TimerState.Completed;
            bool isMediaPlaying = _mediaService.IsPlaying;

            if (isTimerActive)
            {
                // Priority 1: Timer active
                PillTimerBadge.Visibility = Visibility.Visible;
                PillMediaBadge.Visibility = Visibility.Collapsed;
                ItemCountBadge.Visibility = Visibility.Collapsed;
                CompactSystemStats.Visibility = Visibility.Collapsed;
                _equalizerStoryboard?.Stop();

                string icon = _timerService.CurrentMode switch
                {
                    "Short Break" => "☕",
                    "Long Break" => "🌿",
                    "Quick Timer" => "⚡",
                    _ => "🍅"
                };
                string pausePrefix = _timerService.State == TimerState.Paused ? "⏸ " : "";
                PillTimerText.Text = $"{icon} {pausePrefix}{_timerService.FormattedTime}";
                NotchStatusText.Text = _timerService.CurrentMode;
            }
            else if (isTimerCompleted)
            {
                PillTimerBadge.Visibility = Visibility.Visible;
                PillMediaBadge.Visibility = Visibility.Collapsed;
                ItemCountBadge.Visibility = Visibility.Collapsed;
                CompactSystemStats.Visibility = Visibility.Collapsed;
                _equalizerStoryboard?.Stop();
                PillTimerText.Text = "🎉 Done!";
                NotchStatusText.Text = "Break Time!";
            }
            else if (isMediaPlaying)
            {
                // Priority 2: Media playing
                PillTimerBadge.Visibility = Visibility.Collapsed;
                PillMediaBadge.Visibility = Visibility.Visible;
                ItemCountBadge.Visibility = Visibility.Collapsed;
                CompactSystemStats.Visibility = Visibility.Collapsed;
                _equalizerStoryboard?.Begin();

                NotchStatusText.Text = string.IsNullOrEmpty(_mediaService.AppSource) ? "Music" : _mediaService.AppSource;
                string trackSnippet = !string.IsNullOrEmpty(_mediaService.TrackTitle)
                    ? _mediaService.TrackTitle
                    : "Playing";
                PillMediaText.Text = trackSnippet;
            }
            else if (_items.Count > 0)
            {
                // Priority 3: Dropped items count
                PillTimerBadge.Visibility = Visibility.Collapsed;
                PillMediaBadge.Visibility = Visibility.Collapsed;
                ItemCountBadge.Visibility = Visibility.Visible;
                CompactSystemStats.Visibility = Visibility.Collapsed;
                _equalizerStoryboard?.Stop();
                BadgeCountText.Text = _items.Count.ToString();
                NotchStatusText.Text = $"{_items.Count} Item{(_items.Count > 1 ? "s" : "")}";
            }
            else
            {
                // Priority 4: System stats / Idle
                PillTimerBadge.Visibility = Visibility.Collapsed;
                PillMediaBadge.Visibility = Visibility.Collapsed;
                ItemCountBadge.Visibility = Visibility.Collapsed;
                CompactSystemStats.Visibility = Visibility.Visible;
                _equalizerStoryboard?.Stop();
                NotchStatusText.Text = "Drop Shelf";
            }
        }

        private void OnSystemStatsUpdated(SystemStats stats)
        {
            // Compact pill view
            PillCpuText.Text = stats.CpuFormatted;
            PillRamText.Text = stats.RamFormatted;

            // System HUD tab
            CpuText.Text = stats.CpuFormatted;
            CpuValueLargeText.Text = stats.CpuFormatted;
            CpuProgressBar.Value = stats.CpuUsagePercentage;
            CpuDetailText.Text = stats.CpuUsagePercentage > 80 ? "Heavy Load" : (stats.CpuUsagePercentage > 40 ? "Moderate Load" : "Optimal Load");

            RamText.Text = stats.RamFormatted;
            RamValueLargeText.Text = stats.RamFormatted;
            RamProgressBar.Value = stats.RamUsagePercentage;
            RamDetailText.Text = stats.RamDetailFormatted;

            BatteryText.Text = stats.HasBattery ? stats.BatteryFormatted : (stats.IsBatteryCharging ? "Online" : "AC");
            BatteryValueLargeText.Text = stats.HasBattery ? stats.BatteryFormatted : "100%";
            BatteryProgressBar.Value = stats.HasBattery ? stats.BatteryPercentage : 100;
            BatteryDetailText.Text = stats.IsBatteryCharging
                ? (stats.HasBattery ? "⚡ Charging (AC)" : "⚡ AC Power Line")
                : "🔋 On Battery";
        }

        #region Navigation / Tabs

        private void OnTabMediaClicked(object sender, RoutedEventArgs e)
        {
            SetTab(NotchTab.Media);
        }

        private void OnTabShelfClicked(object sender, RoutedEventArgs e)
        {
            SetTab(NotchTab.Shelf);
        }

        private void OnTabClipboardClicked(object sender, RoutedEventArgs e)
        {
            SetTab(NotchTab.Clipboard);
        }

        private void OnTabSystemClicked(object sender, RoutedEventArgs e)
        {
            SetTab(NotchTab.System);
        }

        private void OnTabTimerClicked(object sender, RoutedEventArgs e)
        {
            SetTab(NotchTab.Timer);
        }

        public void SetTab(NotchTab tab)
        {
            _currentTab = tab;

            MediaContentGrid.Visibility = (tab == NotchTab.Media) ? Visibility.Visible : Visibility.Collapsed;
            ShelfContentGrid.Visibility = (tab == NotchTab.Shelf) ? Visibility.Visible : Visibility.Collapsed;
            ClipboardContentGrid.Visibility = (tab == NotchTab.Clipboard) ? Visibility.Visible : Visibility.Collapsed;
            SystemHudContentGrid.Visibility = (tab == NotchTab.System) ? Visibility.Visible : Visibility.Collapsed;
            TimerContentGrid.Visibility = (tab == NotchTab.Timer) ? Visibility.Visible : Visibility.Collapsed;

            TabMediaButton.Tag = (tab == NotchTab.Media) ? "Active" : "Inactive";
            TabShelfButton.Tag = (tab == NotchTab.Shelf) ? "Active" : "Inactive";
            TabClipboardButton.Tag = (tab == NotchTab.Clipboard) ? "Active" : "Inactive";
            TabSystemButton.Tag = (tab == NotchTab.System) ? "Active" : "Inactive";
            TabTimerButton.Tag = (tab == NotchTab.Timer) ? "Active" : "Inactive";

            HeaderClearAllButton.Visibility = (tab == NotchTab.Shelf && _items.Count > 0) ? Visibility.Visible : Visibility.Collapsed;

            if (tab == NotchTab.Media)
            {
                UpdateMediaUi();
            }
            else if (tab == NotchTab.Clipboard)
            {
                UpdateClipboardUi();
                foreach (var item in ClipboardHistoryService.Instance.History)
                {
                    item.RefreshTimeFormatted();
                }
            }
        }

        #endregion

        #region Drag and Drop In

        private void OnWindowDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                SetTab(NotchTab.Shelf);
                e.Effects = DragDropEffects.Copy;
                ExpandNotch();
                e.Handled = true;
            }
        }

        private void OnWindowDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
                if (!_isExpanded) ExpandNotch();
                e.Handled = true;
            }
        }

        private void OnWindowDragLeave(object sender, DragEventArgs e)
        {
            // If dragging leaves and mouse is not hovering, collapse if empty
            if (_items.Count == 0 && !NotchContainer.IsMouseOver)
            {
                CollapseNotch();
            }
        }

        private void OnWindowDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (string path in files)
                    {
                        AddPathToShelf(path);
                    }
                    e.Handled = true;
                }
                else if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    string text = (string)e.Data.GetData(DataFormats.Text);
                    AddTextToShelf(text);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotchWindow] Drop error: {ex.Message}");
            }
        }

        private void AddPathToShelf(string path)
        {
            bool isDirectory = Directory.Exists(path);
            bool isFile = File.Exists(path);

            if (!isDirectory && !isFile) return;

            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;

            string sizeStr = isDirectory ? "Folder" : FormatFileSize(new FileInfo(path).Length);
            var icon = WindowHelper.GetWindowIcon(IntPtr.Zero, path);

            var item = new NotchItem
            {
                Name = name,
                FilePath = path,
                ItemType = isDirectory ? NotchItemType.Folder : (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? NotchItemType.Application : NotchItemType.File),
                SizeFormatted = sizeStr,
                Icon = icon,
                DroppedTime = DateTime.Now
            };

            _items.Add(item);
        }

        private void AddTextToShelf(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            bool isUrl = Uri.TryCreate(text, UriKind.Absolute, out _);

            var item = new NotchItem
            {
                Name = isUrl ? text : (text.Length > 20 ? text.Substring(0, 20) + "..." : text),
                TextContent = text,
                ItemType = isUrl ? NotchItemType.Url : NotchItemType.Text,
                SizeFormatted = isUrl ? "Web Link" : $"{text.Length} chars",
                DroppedTime = DateTime.Now
            };

            _items.Add(item);
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        #endregion

        #region Drag Out

        private void OnItemMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element && element.DataContext is NotchItem item)
            {
                System.Windows.Point currentPoint = e.GetPosition(null);
                if (Math.Abs(currentPoint.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPoint.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (!string.IsNullOrEmpty(item.FilePath))
                    {
                        var data = new System.Windows.DataObject(DataFormats.FileDrop, new string[] { item.FilePath });
                        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy | DragDropEffects.Move);
                    }
                    else if (!string.IsNullOrEmpty(item.TextContent))
                    {
                        var data = new System.Windows.DataObject(DataFormats.Text, item.TextContent);
                        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
                    }
                }
            }
            else
            {
                _dragStartPoint = e.GetPosition(null);
            }
        }

        #endregion

        #region User Interactions

        private void OnNotchMouseEnter(object sender, MouseEventArgs e)
        {
            if (_isExpanded) return;

            // 260ms hover delay: cursor passing across the top of the screen (e.g. switching browser tabs)
            // will not accidentally trigger the notch expansion
            _hoverExpandTimer?.Stop();
            _hoverExpandTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            _hoverExpandTimer.Tick += (s, ev) =>
            {
                _hoverExpandTimer.Stop();
                if (NotchContainer.IsMouseOver && !_isExpanded)
                {
                    ExpandNotch();
                }
            };
            _hoverExpandTimer.Start();
        }

        private void OnNotchMouseLeave(object sender, MouseEventArgs e)
        {
            _hoverExpandTimer?.Stop();

            // If no items dropped and not showing completion banner, automatically collapse when mouse leaves
            if (_items.Count == 0 && _timerService.State != TimerState.Completed)
            {
                CollapseNotch();
            }
        }

        private void OnNotchMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _hoverExpandTimer?.Stop();
            if (!_isExpanded)
            {
                if (_mediaService.IsPlaying && _items.Count == 0 && _timerService.State == TimerState.Idle)
                {
                    SetTab(NotchTab.Media);
                }
                ExpandNotch();
            }
        }

        private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                if (e.Delta < 0) sv.LineRight();
                else sv.LineLeft();
                e.Handled = true;
            }
        }

        private void OnClearAllClicked(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            CollapseNotch();
        }

        private void OnRemoveItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is NotchItem item)
            {
                _items.Remove(item);
                if (_items.Count == 0)
                {
                    CollapseNotch();
                }
            }
        }

        private int _shelfFeedbackCounter;

        private async void ShowShelfFeedback(string message, bool isSuccess = true)
        {
            if (ShelfToastBorder == null || ShelfToastText == null || ShelfToastIcon == null) return;

            int currentId = ++_shelfFeedbackCounter;

            ShelfToastIcon.Text = isSuccess ? "✓" : "⚠";
            ShelfToastIcon.Foreground = isSuccess ? EmeraldFgBrush : RoseFgBrush;
            ShelfToastBorder.BorderBrush = isSuccess ? BlueBorderBrush : RoseBorderBrush;
            ShelfToastText.Text = message;
            ShelfToastBorder.Visibility = Visibility.Visible;

            await Task.Delay(2500);

            if (_shelfFeedbackCounter == currentId)
            {
                ShelfToastBorder.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnZipAllClicked(object sender, RoutedEventArgs e)
        {
            var validPaths = _items
                .Select(i => i.FilePath)
                .Where(p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validPaths.Count == 0)
            {
                ShowShelfFeedback("No local files to archive", isSuccess: false);
                return;
            }

            var dialog = new ZipNameDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ZipFileName))
            {
                return; // User cancelled
            }

            string enteredName = dialog.ZipFileName.Trim();
            if (!enteredName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                enteredName += ".zip";
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string targetPath = Path.Combine(desktop, enteredName);

            try
            {
                string zipPath = FileToolsService.Instance.CreateZipArchive(validPaths, targetPath);
                string zipFileName = Path.GetFileName(zipPath);

                // Clear shelf items after successful zip as requested
                _items.Clear();
                UpdateBadge();

                ShowShelfFeedback($"✓ Created {zipFileName} & cleared shelf");
                ShelfStatusFeedbackText.Text = $"Created {zipFileName} on Desktop";

                if (sender is System.Windows.Controls.Button zipBtn)
                {
                    object oldContent = zipBtn.Content;
                    var oldFg = zipBtn.Foreground;

                    zipBtn.Content = "✓ Zipped!";
                    zipBtn.Foreground = EmeraldFgBrush;

                    await Task.Delay(1500);

                    zipBtn.Content = oldContent;
                    zipBtn.Foreground = oldFg;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotchWindow] ZipAll error: {ex.Message}");
                ShowShelfFeedback($"Zip failed: {ex.Message}", isSuccess: false);
            }
        }

        private async void OnCopyPathsClicked(object sender, RoutedEventArgs e)
        {
            var paths = _items
                .Select(i => i.FilePath ?? i.TextContent)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (paths.Count == 0)
            {
                ShowShelfFeedback("Shelf is empty", isSuccess: false);
                return;
            }

            string combined = string.Join(Environment.NewLine, paths);
            System.Windows.Clipboard.SetText(combined);

            ShowShelfFeedback($"✓ Copied {paths.Count} path{(paths.Count > 1 ? "s" : "")} to clipboard");
            ShelfStatusFeedbackText.Text = $"{paths.Count} path{(paths.Count > 1 ? "s" : "")} copied";

            if (sender is System.Windows.Controls.Button copyBtn)
            {
                object oldContent = copyBtn.Content;
                var oldFg = copyBtn.Foreground;

                copyBtn.Content = "✓ Copied!";
                copyBtn.Foreground = EmeraldFgBrush;

                await Task.Delay(1200);

                copyBtn.Content = oldContent;
                copyBtn.Foreground = oldFg;
            }
        }

        private void OnCopyPathClicked(object sender, RoutedEventArgs e)
        {
            OnCopyPathsClicked(sender, e);
        }

        private void OnPinSelectedToDockClicked(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(item.FilePath))
                {
                    RequestPinToDock?.Invoke(item.FilePath);
                }
            }
        }

        private void OnItemRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is NotchItem item)
            {
                var menu = new ContextMenu();

                if (!string.IsNullOrEmpty(item.FilePath))
                {
                    var openItem = new MenuItem { Header = "Open in Explorer" };
                    openItem.Click += (s, ev) => FileToolsService.Instance.OpenInExplorer(item.FilePath);
                    menu.Items.Add(openItem);

                    var copyItem = new MenuItem { Header = "Copy File Path" };
                    copyItem.Click += (s, ev) =>
                    {
                        System.Windows.Clipboard.SetText(item.FilePath);
                        ShowShelfFeedback("Copied file path to clipboard");
                    };
                    menu.Items.Add(copyItem);

                    var pinItem = new MenuItem { Header = "📌 Pin to Dock" };
                    pinItem.Click += (s, ev) => RequestPinToDock?.Invoke(item.FilePath);
                    menu.Items.Add(pinItem);

                    menu.Items.Add(new Separator());
                }
                else if (!string.IsNullOrEmpty(item.TextContent))
                {
                    var copyTextItem = new MenuItem { Header = "Copy Content" };
                    copyTextItem.Click += (s, ev) =>
                    {
                        System.Windows.Clipboard.SetText(item.TextContent);
                        ShowShelfFeedback("Copied text to clipboard");
                    };
                    menu.Items.Add(copyTextItem);

                    menu.Items.Add(new Separator());
                }

                var removeItem = new MenuItem { Header = "Remove from Shelf" };
                removeItem.Click += (s, ev) =>
                {
                    _items.Remove(item);
                    if (_items.Count == 0)
                    {
                        CollapseNotch();
                    }
                };
                menu.Items.Add(removeItem);

                menu.IsOpen = true;
                e.Handled = true;
            }
        }

        #endregion

        #region Pomodoro & Timer Handlers

        private void OnTimerTicked(PomodoroTimerService service)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnTimerTicked(service));
                return;
            }

            UpdateTimerUi();
            UpdateCompactPill();
        }

        private void OnTimerCompleted(string mode)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnTimerCompleted(mode));
                return;
            }

            SetTab(NotchTab.Timer);
            ExpandNotch();
            UpdateTimerUi();
            UpdateCompactPill();
        }

        private void UpdateTimerUi()
        {
            TimerBigTimeText.Text = _timerService.FormattedTime;

            string icon = _timerService.CurrentMode switch
            {
                "Short Break" => "☕",
                "Long Break" => "🌿",
                "Quick Timer" => "⚡",
                _ => "🍅"
            };
            TimerModeBadgeText.Text = $"{icon} {_timerService.CurrentMode}";

            TimerProgressBar.Value = _timerService.ProgressPercentage;
            TimerProgressRatioText.Text = $"{Math.Round(_timerService.ProgressPercentage, 0)}% completed";

            if (_timerService.State == TimerState.Running)
            {
                TimerPlayPauseButton.Content = "⏸ Pause";
                TimerPlayPauseButton.Background = AmberBgBrush;
                TimerPlayPauseButton.Foreground = AmberFgBrush;
                TimerPlayPauseButton.BorderBrush = AmberBorderBrush;
                TimerBannerText.Text = $"{_timerService.CurrentMode} in progress...";
                TimerBannerText.Foreground = MutedFgBrush;
            }
            else if (_timerService.State == TimerState.Paused)
            {
                TimerPlayPauseButton.Content = "▶ Resume";
                TimerPlayPauseButton.Background = BlueBgBrush;
                TimerPlayPauseButton.Foreground = BlueFgBrush;
                TimerPlayPauseButton.BorderBrush = BlueBorderBrush;
                TimerBannerText.Text = "Timer Paused";
                TimerBannerText.Foreground = AmberFgBrush;
            }
            else if (_timerService.State == TimerState.Completed)
            {
                TimerPlayPauseButton.Content = "▶ Start";
                TimerPlayPauseButton.Background = RoseBgBrush;
                TimerPlayPauseButton.Foreground = RoseFgBrush;
                TimerPlayPauseButton.BorderBrush = RoseBorderBrush;
                TimerBannerText.Text = "🎉 Pomodoro Complete! Take a break!";
                TimerBannerText.Foreground = EmeraldFgBrush;
            }
            else // Idle
            {
                TimerPlayPauseButton.Content = "▶ Start";
                TimerPlayPauseButton.Background = RoseBgBrush;
                TimerPlayPauseButton.Foreground = RoseFgBrush;
                TimerPlayPauseButton.BorderBrush = RoseBorderBrush;
                TimerBannerText.Text = "Ready for Focus Session";
                TimerBannerText.Foreground = MutedFgBrush;
            }
        }

        private void OnTimerPlayPauseClicked(object sender, RoutedEventArgs e)
        {
            if (_timerService.State == TimerState.Running)
            {
                _timerService.PauseTimer();
            }
            else if (_timerService.State == TimerState.Paused)
            {
                _timerService.ResumeTimer();
            }
            else
            {
                _timerService.StartTimer(_timerService.RemainingTime > TimeSpan.Zero ? _timerService.RemainingTime : TimeSpan.FromMinutes(25), _timerService.CurrentMode);
            }
        }

        private void OnTimerResetClicked(object sender, RoutedEventArgs e)
        {
            _timerService.ResetTimer();
        }

        private void OnPresetPomodoroClicked(object sender, RoutedEventArgs e)
        {
            _timerService.StartTimer(TimeSpan.FromMinutes(25), "Pomodoro");
        }

        private void OnPresetShortBreakClicked(object sender, RoutedEventArgs e)
        {
            _timerService.StartTimer(TimeSpan.FromMinutes(5), "Short Break");
        }

        private void OnPresetLongBreakClicked(object sender, RoutedEventArgs e)
        {
            _timerService.StartTimer(TimeSpan.FromMinutes(15), "Long Break");
        }

        private void OnPresetTestClicked(object sender, RoutedEventArgs e)
        {
            _timerService.StartTimer(TimeSpan.FromMinutes(1), "Quick Timer");
        }

        #endregion

        #region Clipboard Shelf Handlers

        private void OnClipboardHistoryChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnClipboardHistoryChanged(sender, e));
                return;
            }

            UpdateClipboardUi();
        }

        private void UpdateClipboardUi()
        {
            int count = ClipboardHistoryService.Instance.History.Count;
            if (count > 0)
            {
                EmptyClipboardPlaceholder.Visibility = Visibility.Collapsed;
                ClipboardScrollViewer.Visibility = Visibility.Visible;
                ClipboardCountText.Text = $"{count} item{(count > 1 ? "s" : "")}";
                ClipboardClearButton.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyClipboardPlaceholder.Visibility = Visibility.Visible;
                ClipboardScrollViewer.Visibility = Visibility.Collapsed;
                ClipboardCountText.Text = "0 items";
                ClipboardClearButton.Visibility = Visibility.Collapsed;
            }
        }

        private void OnClearClipboardClicked(object sender, RoutedEventArgs e)
        {
            ClipboardHistoryService.Instance.ClearHistory();
        }

        private void OnClipboardCardDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ClipboardItem item)
            {
                ClipboardHistoryService.Instance.RemoveItem(item);
            }
        }

        private async void OnClipboardCardCopyClicked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button copyBtn && copyBtn.Tag is ClipboardItem item)
            {
                ClipboardHistoryService.Instance.CopyToClipboard(item);

                object oldContent = copyBtn.Content;
                var oldFg = copyBtn.Foreground;
                var oldBg = copyBtn.Background;

                copyBtn.Content = "✓ Copied!";
                copyBtn.Foreground = EmeraldFgBrush;

                await Task.Delay(1200);

                copyBtn.Content = oldContent;
                copyBtn.Foreground = oldFg;
                copyBtn.Background = oldBg;
            }
        }

        #endregion

        #region Now Playing / Media Handlers

        private void OnMediaStateChanged(MediaControllerService service)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnMediaStateChanged(service));
                return;
            }

            UpdateMediaUi();
            UpdateCompactPill();
        }

        private void UpdateMediaUi()
        {
            bool hasMedia = _mediaService.IsPlaying || !string.IsNullOrWhiteSpace(_mediaService.TrackTitle);

            if (hasMedia)
            {
                MediaEmptyPlaceholder.Visibility = Visibility.Collapsed;
                MediaActivePanel.Visibility = Visibility.Visible;

                MediaTrackTitleText.Text = !string.IsNullOrWhiteSpace(_mediaService.TrackTitle)
                    ? _mediaService.TrackTitle
                    : "Unknown Track";

                MediaArtistText.Text = !string.IsNullOrWhiteSpace(_mediaService.ArtistName)
                    ? _mediaService.ArtistName
                    : "Unknown Artist";

                string source = !string.IsNullOrWhiteSpace(_mediaService.AppSource)
                    ? _mediaService.AppSource
                    : "Media";
                MediaSourceText.Text = source;

                if (source.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
                {
                    MediaSourceBadge.Background = SpotifyBgBrush;
                    MediaSourceBadge.BorderBrush = SpotifyBorderBrush;
                    MediaSourceText.Foreground = EmeraldFgBrush;
                    MediaVinylGlyph.Text = "🟢";
                }
                else if (source.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    MediaSourceBadge.Background = YoutubeBgBrush;
                    MediaSourceBadge.BorderBrush = YoutubeBorderBrush;
                    MediaSourceText.Foreground = YoutubeFgBrush;
                    MediaVinylGlyph.Text = "▶";
                }
                else
                {
                    MediaSourceBadge.Background = BlueBgBrush;
                    MediaSourceBadge.BorderBrush = BlueBorderBrush;
                    MediaSourceText.Foreground = BlueFgBrush;
                    MediaVinylGlyph.Text = "🎵";
                }

                if (_mediaService.IsPlaying)
                {
                    MediaPlayPauseButton.Content = "⏸";
                    MediaPlayPauseButton.ToolTip = "Pause";
                }
                else
                {
                    MediaPlayPauseButton.Content = "▶";
                    MediaPlayPauseButton.ToolTip = "Play";
                }
            }
            else
            {
                MediaEmptyPlaceholder.Visibility = Visibility.Visible;
                MediaActivePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void OnMediaPlayPauseClicked(object sender, RoutedEventArgs e)
        {
            _mediaService.PlayPause();
        }

        private void OnMediaPreviousClicked(object sender, RoutedEventArgs e)
        {
            _mediaService.PreviousTrack();
        }

        private void OnMediaNextClicked(object sender, RoutedEventArgs e)
        {
            _mediaService.NextTrack();
        }

        #endregion
    }
}
