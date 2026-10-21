using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Screen = System.Windows.Forms.Screen;
using System.Windows.Input;
using WinDock.Models;
using WinDock.Native;
using WinDock.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace WinDock.Views
{
    public partial class AppListWindow : Window
    {
        private readonly SpotlightService _spotlightService = SpotlightService.Instance;
        private DockManager? _dockManager;
        private List<SpotlightItem> _allApps = new();

        public event Action<string>? RequestPinToDock;
        public event Action? RequestToggleNotch;

        public AppListWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public void AttachDockManager(DockManager manager)
        {
            _dockManager = manager;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            WindowHelper.HideFromAltTab(hwnd);

            SetUserInfo();
            LoadApplications();
            PositionAboveDock();
        }

        private void SetUserInfo()
        {
            string username = Environment.UserName;
            UserNameText.Text = username;
            UserInitialText.Text = !string.IsNullOrEmpty(username) ? username.Substring(0, 1).ToUpper() : "U";
        }

        public void PositionAboveDock()
        {
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var bounds = primaryScreen.Bounds;

            Left = bounds.Left + (bounds.Width - Width) / 2;
            Top = bounds.Bottom - Height - 85; // Float directly above the bottom dock
        }

        public void LoadApplications()
        {
            _allApps = _spotlightService.GetAllIndexedApps();
            AppsItemsControl.ItemsSource = _allApps;
        }

        public void ShowAndFocus()
        {
            PositionAboveDock();
            Show();
            Activate();
            SearchBox.Text = string.Empty;
            SearchBox.Focus();
        }

        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                ShowAndFocus();
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim();
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

            if (string.IsNullOrEmpty(query))
            {
                AppsItemsControl.ItemsSource = _allApps;
            }
            else
            {
                var filtered = _allApps
                    .Where(a => a.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                AppsItemsControl.ItemsSource = filtered;
            }
        }

        private void OnAppClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is SpotlightItem item)
            {
                Hide();
                item.ExecuteAction?.Invoke();
            }
        }

        private void OnAppRightClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is SpotlightItem item)
            {
                var menu = new System.Windows.Controls.ContextMenu();

                var openItem = new System.Windows.Controls.MenuItem { Header = "Open Application" };
                openItem.Click += (s, ev) =>
                {
                    Hide();
                    item.ExecuteAction?.Invoke();
                };
                menu.Items.Add(openItem);

                var pinItem = new System.Windows.Controls.MenuItem { Header = "📌 Pin to Dock" };
                pinItem.Click += (s, ev) =>
                {
                    if (item.Subtitle.Contains("•"))
                    {
                        string path = item.Subtitle.Split('•').Last().Trim();
                        RequestPinToDock?.Invoke(path);
                    }
                };
                menu.Items.Add(pinItem);

                menu.IsOpen = true;
            }
        }

        #region Custom Actions & Power Controls

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void OnDesktopClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            WindowHelper.ToggleShowDesktop();
        }

        private void OnNotchClicked(object sender, RoutedEventArgs e)
        {
            Hide();
            RequestToggleNotch?.Invoke();
        }


        private void OnPowerDropdownClicked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
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
            var result = MessageBox.Show(
                "Are you sure you want to restart your computer?",
                "Restart Computer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Hide();
                TaskbarController.Restart();
            }
        }

        private void OnShutdownClicked(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to shut down your computer?",
                "Shut Down Computer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Hide();
                TaskbarController.Shutdown();
            }
        }

        private void OnExitWinDockClicked(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to exit WinDock? The Windows taskbar will be restored.",
                "Exit WinDock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Hide();
                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ExitApplication();
                }
                else
                {
                    Application.Current.Shutdown();
                }
            }
        }

        #endregion

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            Hide();
        }
    }
}
