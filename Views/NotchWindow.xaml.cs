using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Animation;
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
    public partial class NotchWindow : Window
    {
        private readonly ObservableCollection<NotchItem> _items = new();
        private bool _isExpanded;
        private System.Windows.Point _dragStartPoint;
        private readonly Storyboard? _expandAnimation;
        private readonly Storyboard? _collapseAnimation;

        public event Action<string>? RequestPinToDock;

        public NotchWindow()
        {
            InitializeComponent();

            NotchItemsControl.ItemsSource = _items;
            _items.CollectionChanged += (s, e) => UpdateBadge();

            _expandAnimation = (Storyboard)FindResource("ExpandStoryboard");
            _collapseAnimation = (Storyboard)FindResource("CollapseStoryboard");

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionAtTopCenter();
        }

        public void PositionAtTopCenter()
        {
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var bounds = primaryScreen.Bounds;

            Left = bounds.Left + (bounds.Width - Width) / 2;
            Top = bounds.Top;
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
                ItemCountBadge.Visibility = Visibility.Visible;
                BadgeCountText.Text = count.ToString();
                EmptyDropPlaceholder.Visibility = Visibility.Collapsed;
                ItemsScrollViewer.Visibility = Visibility.Visible;
                NotchStatusText.Text = $"{count} Item{(count > 1 ? "s" : "")}";
            }
            else
            {
                ItemCountBadge.Visibility = Visibility.Collapsed;
                EmptyDropPlaceholder.Visibility = Visibility.Visible;
                ItemsScrollViewer.Visibility = Visibility.Collapsed;
                NotchStatusText.Text = "Drop Shelf";
            }
        }

        #region Drag and Drop In

        private void OnWindowDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
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
            ExpandNotch();
        }

        private void OnNotchMouseLeave(object sender, MouseEventArgs e)
        {
            // If no items dropped, automatically collapse when mouse leaves
            if (_items.Count == 0)
            {
                CollapseNotch();
            }
        }

        private void OnNotchMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isExpanded)
            {
                ExpandNotch();
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

        private void OnCopyPathClicked(object sender, RoutedEventArgs e)
        {
            if (_items.Count > 0)
            {
                var first = _items[0];
                string content = first.FilePath ?? first.TextContent ?? string.Empty;
                if (!string.IsNullOrEmpty(content))
                {
                    System.Windows.Clipboard.SetText(content);
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
                    var openItem = new MenuItem { Header = "📂 Reveal in File Explorer" };
                    openItem.Click += (s, ev) => DockManager.LaunchProcess("explorer.exe", false);
                    menu.Items.Add(openItem);

                    var pinItem = new MenuItem { Header = "📌 Pin to Dock" };
                    pinItem.Click += (s, ev) => RequestPinToDock?.Invoke(item.FilePath);
                    menu.Items.Add(pinItem);

                    var copyItem = new MenuItem { Header = "📋 Copy Full Path" };
                    copyItem.Click += (s, ev) => System.Windows.Clipboard.SetText(item.FilePath);
                    menu.Items.Add(copyItem);

                    menu.Items.Add(new Separator());
                }

                var removeItem = new MenuItem { Header = "✕ Remove from Shelf" };
                removeItem.Click += (s, ev) => _items.Remove(item);
                menu.Items.Add(removeItem);

                menu.IsOpen = true;
            }
        }

        #endregion
    }
}
