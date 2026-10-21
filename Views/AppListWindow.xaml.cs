using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using WinDock.Models;
using WinDock.Native;
using WinDock.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace WinDock.Views
{
    /// <summary>
    /// Full-screen macOS-style Launchpad: blurred wallpaper, paged app grid, search, power actions.
    /// </summary>
    public partial class AppListWindow : Window
    {
        private const double TileSize = 128;

        private readonly SpotlightService _spotlightService = SpotlightService.Instance;
        private DockManager? _dockManager;
        private List<SpotlightItem> _allApps = new();
        private List<SpotlightItem> _filtered = new();
        private int _page;
        private int _pageSize = 35;
        private int _cols = 6;
        private int _selectedIndex = -1;
        private int _wheelAccum;
        private DateTime _lastPageFlip = DateTime.MinValue;

        public event Action<string>? RequestPinToDock;
        public event Action? RequestToggleNotch;

        public AppListWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => WindowHelper.HideFromAltTab(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }

        public void AttachDockManager(DockManager manager)
        {
            _dockManager = manager;
        }

        public void LoadApplications(bool forceReload = false)
        {
            if (forceReload || _allApps.Count == 0)
            {
                _allApps = _spotlightService.GetAllIndexedApps();
            }
            ApplyFilter();
        }

        public void ShowAndFocus()
        {
            try
            {
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
                LayoutGrid();
                LoadWallpaper();

                if (_allApps.Count == 0) LoadApplications();
                SearchBox.Text = string.Empty; // also resets to page 1 via OnSearchTextChanged
                _page = 0;
                ShowPage();

                Show();
                Activate();
                SearchBox.Focus();
                AnimateIn();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppListWindow] ShowAndFocus error: {ex.Message}");
                try
                {
                    Show();
                    Activate();
                }
                catch { }
            }
        }

        public void Toggle()
        {
            try
            {
                if (IsVisible) Hide();
                else ShowAndFocus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppListWindow] Toggle error: {ex.Message}");
            }
        }

        #region Layout, paging & search

        private void LayoutGrid()
        {
            // Leave room for the search row on top and the page dots + dock at the bottom.
            int cols = Math.Clamp((int)((Width - 160) / TileSize), 3, 8);
            int rows = Math.Clamp((int)((Height - 300) / TileSize), 2, 5);
            _cols = cols;
            _pageSize = cols * rows;
            AppsItemsControl.Width = cols * TileSize;
        }

        private int PageCount => Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)_pageSize));

        private void ApplyFilter()
        {
            string query = SearchBox.Text.Trim();
            _filtered = string.IsNullOrEmpty(query)
                ? _allApps
                : _allApps.Where(a => a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            _page = 0;
            _selectedIndex = -1;
            ShowPage();
        }

        private void ShowPage()
        {
            _page = Math.Clamp(_page, 0, PageCount - 1);
            AppsItemsControl.ItemsSource = _filtered.Skip(_page * _pageSize).Take(_pageSize).ToList();

            bool empty = _filtered.Count == 0 && SearchBox.Text.Trim().Length > 0;
            NoResultsText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            NoResultsText.Text = $"No apps match \"{SearchBox.Text.Trim()}\"";

            PageDots.Children.Clear();
            if (PageCount < 2) return;
            for (int i = 0; i < PageCount; i++)
            {
                int target = i;
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(5, 0, 5, 0),
                    Cursor = Cursors.Hand,
                    Fill = new SolidColorBrush(i == _page ? Colors.White : Color.FromArgb(0x66, 255, 255, 255))
                };
                dot.MouseLeftButtonUp += (s, e) => { _page = target; ShowPage(); e.Handled = true; };
                PageDots.Children.Add(dot);
            }
        }

        private void ChangePage(int delta)
        {
            int next = Math.Clamp(_page + delta, 0, PageCount - 1);
            if (next == _page) return;
            _page = next;
            ShowPage();

            // Short slide in the paging direction
            var slide = new TranslateTransform(40 * delta, 0);
            AppsItemsControl.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 }
            });
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void OnWindowMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Touchpads fire many small deltas: flip once per notch-worth of scroll, at most every 350ms
            _wheelAccum += e.Delta;
            if (Math.Abs(_wheelAccum) < 120 || DateTime.Now - _lastPageFlip < TimeSpan.FromMilliseconds(350)) return;

            ChangePage(_wheelAccum < 0 ? 1 : -1);
            _wheelAccum = 0;
            _lastPageFlip = DateTime.Now;
        }

        #endregion

        #region Look & motion

        private void AnimateIn()
        {
            var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
            var duration = TimeSpan.FromMilliseconds(240);

            Backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            ContentRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, 1, duration) { EasingFunction = ease });
            ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, 1, duration) { EasingFunction = ease });
        }

        /// <summary>
        /// Decodes the current wallpaper at low resolution; it is blurred anyway, so 480px is plenty and cheap.
        /// </summary>
        private void LoadWallpaper()
        {
            try
            {
                string? path = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // Slideshow / Spotlight wallpapers only exist as the transcoded copy
                    path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Microsoft\Windows\Themes\TranscodedWallpaper");
                }
                if (!File.Exists(path))
                {
                    WallpaperImage.Source = null;
                    return;
                }

                var bmp = new BitmapImage();
                using (var stream = File.OpenRead(path))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 480;
                    bmp.StreamSource = stream;
                    bmp.EndInit();
                }
                bmp.Freeze();
                WallpaperImage.Source = bmp;
            }
            catch
            {
                WallpaperImage.Source = null; // plain dark backdrop
            }
        }

        #endregion

        #region App tiles

        private void Launch(SpotlightItem item)
        {
            Hide();
            item.ExecuteAction?.Invoke();
        }

        private void OnAppClicked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is SpotlightItem item) Launch(item);
        }

        private void OnAppRightClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement elem || elem.DataContext is not SpotlightItem item) return;

            var menu = new ContextMenu();
            var openItem = new MenuItem { Header = "Open" };
            openItem.Click += (s, ev) => Launch(item);
            menu.Items.Add(openItem);

            if (!string.IsNullOrEmpty(item.LaunchPath))
            {
                var pinItem = new MenuItem { Header = "Keep in Dock" };
                pinItem.Click += (s, ev) => RequestPinToDock?.Invoke(item.LaunchPath);
                menu.Items.Add(pinItem);
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        #endregion

        #region Corner actions & power

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            new SettingsWindow().ShowDialog();
        }

        private void OnNotchClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            RequestToggleNotch?.Invoke();
        }

        private void OnHelpClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            new HelpWindow().Show();
        }

        private void OnPowerDropdownClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void OnLockClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            TaskbarController.Lock();
        }

        private void OnSleepClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            TaskbarController.Sleep();
        }

        private void OnRestartClicked(object sender, RoutedEventArgs e)
        {
            if (Confirm("Restart your computer now? Unsaved work in open apps may be lost.", "Restart"))
                TaskbarController.Restart();
        }

        private void OnShutdownClicked(object sender, RoutedEventArgs e)
        {
            if (Confirm("Shut down your computer now? Unsaved work in open apps may be lost.", "Shut Down"))
                TaskbarController.Shutdown();
        }

        private void OnExitWinDockClicked(object sender, RoutedEventArgs e)
        {
            if (!Confirm("Quit WinDock? The Windows taskbar will be restored.", "Quit WinDock")) return;

            if (Application.Current.MainWindow is MainWindow mainWin) mainWin.ExitApplication();
            else Application.Current.Shutdown();
        }

        private bool Confirm(string message, string title)
        {
            Hide();
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        #endregion

        #region Keyboard & dismissal

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool inSearch = SearchBox.IsKeyboardFocusWithin;
            bool searchEmpty = SearchBox.Text.Length == 0;
            var currentItems = (AppsItemsControl.ItemsSource as List<SpotlightItem>) ?? new List<SpotlightItem>();
            int itemCount = currentItems.Count;

            switch (e.Key)
            {
                case Key.Escape:
                    if (!searchEmpty) SearchBox.Text = string.Empty;
                    else Hide();
                    e.Handled = true;
                    break;

                // Move focus down into apps grid from search box
                case Key.Down when inSearch:
                    if (itemCount > 0)
                    {
                        _selectedIndex = 0;
                        FocusTileAt(_selectedIndex);
                        e.Handled = true;
                    }
                    break;

                // Navigate within grid using arrow keys
                case Key.Down when !inSearch && _selectedIndex >= 0:
                    if (_selectedIndex + _cols < itemCount)
                    {
                        _selectedIndex += _cols;
                        FocusTileAt(_selectedIndex);
                        e.Handled = true;
                    }
                    break;

                case Key.Up when !inSearch && _selectedIndex >= 0:
                    if (_selectedIndex - _cols >= 0)
                    {
                        _selectedIndex -= _cols;
                        FocusTileAt(_selectedIndex);
                        e.Handled = true;
                    }
                    else
                    {
                        _selectedIndex = -1;
                        SearchBox.Focus();
                        SearchBox.SelectAll();
                        e.Handled = true;
                    }
                    break;

                case Key.Right when !inSearch && _selectedIndex >= 0:
                    if (_selectedIndex + 1 < itemCount)
                    {
                        _selectedIndex++;
                        FocusTileAt(_selectedIndex);
                        e.Handled = true;
                    }
                    else if (_page < PageCount - 1)
                    {
                        ChangePage(1);
                        _selectedIndex = 0;
                        Dispatcher.BeginInvoke(new Action(() => FocusTileAt(_selectedIndex)), System.Windows.Threading.DispatcherPriority.Loaded);
                        e.Handled = true;
                    }
                    break;

                case Key.Left when !inSearch && _selectedIndex >= 0:
                    if (_selectedIndex - 1 >= 0)
                    {
                        _selectedIndex--;
                        FocusTileAt(_selectedIndex);
                        e.Handled = true;
                    }
                    else if (_page > 0)
                    {
                        ChangePage(-1);
                        _selectedIndex = Math.Max(0, Math.Min(_pageSize - 1, _filtered.Count - _page * _pageSize - 1));
                        Dispatcher.BeginInvoke(new Action(() => FocusTileAt(_selectedIndex)), System.Windows.Threading.DispatcherPriority.Loaded);
                        e.Handled = true;
                    }
                    break;

                case Key.Enter when inSearch:
                    if (currentItems.Count > 0) Launch(currentItems[0]);
                    e.Handled = true;
                    break;

                case Key.Enter when !inSearch && _selectedIndex >= 0 && _selectedIndex < currentItems.Count:
                    Launch(currentItems[_selectedIndex]);
                    e.Handled = true;
                    break;

                case Key.PageDown:
                case Key.Right when inSearch && searchEmpty:
                    ChangePage(1);
                    e.Handled = true;
                    break;

                case Key.PageUp:
                case Key.Left when inSearch && searchEmpty:
                    ChangePage(-1);
                    e.Handled = true;
                    break;

                case Key.F1:
                    OnHelpClicked(sender, e);
                    e.Handled = true;
                    break;
            }
        }

        private void FocusTileAt(int index)
        {
            if (index < 0) return;
            AppsItemsControl.UpdateLayout();
            if (AppsItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            {
                var btn = container as Button ?? FindVisualChild<Button>(container);
                btn?.Focus();
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void OnBackdropClicked(object sender, MouseButtonEventArgs e)
        {
            Hide();
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            Hide();
        }

        #endregion
    }
}
