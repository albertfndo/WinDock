using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using WinDock.Models;
using WinDock.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace WinDock.Views
{
    public partial class SpotlightWindow : Window
    {
        private readonly SpotlightService _spotlightService = SpotlightService.Instance;
        private DockManager? _dockManager;

        public SpotlightWindow()
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
            CenterOnScreen();
            PerformSearch(string.Empty);
        }

        public void CenterOnScreen()
        {
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var bounds = primaryScreen.Bounds;

            Left = bounds.Left + (bounds.Width - Width) / 2;
            Top = bounds.Top + bounds.Height * 0.18; // 18% down from top (MacBook Spotlight style)
        }

        public void ShowAndFocus()
        {
            CenterOnScreen();
            Show();
            Activate();
            SearchBox.Focus();
            SearchBox.SelectAll();
            PerformSearch(SearchBox.Text);
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
            string query = SearchBox.Text;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            PerformSearch(query);
        }

        private void PerformSearch(string query)
        {
            var results = _spotlightService.Search(query, _dockManager);
            ResultsList.ItemsSource = results;

            if (results.Count > 0)
            {
                ResultsList.SelectedIndex = 0;
            }
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (ResultsList.Items.Count > 0)
                {
                    int next = Math.Min(ResultsList.SelectedIndex + 1, ResultsList.Items.Count - 1);
                    ResultsList.SelectedIndex = next;
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (ResultsList.Items.Count > 0)
                {
                    int prev = Math.Max(ResultsList.SelectedIndex - 1, 0);
                    ResultsList.SelectedIndex = prev;
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ExecuteSelectedItem();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        }

        private void ExecuteSelectedItem()
        {
            if (ResultsList.SelectedItem is SpotlightItem item)
            {
                Hide();
                try
                {
                    item.ExecuteAction?.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SpotlightWindow] Execution error: {ex.Message}");
                }
            }
        }

        private void OnResultDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelectedItem();
        }

        private void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            // Auto hide when clicking outside like Spotlight
            Hide();
        }
    }
}
