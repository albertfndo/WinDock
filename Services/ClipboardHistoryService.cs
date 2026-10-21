using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinDock.Models;
using WinDock.Native;

namespace WinDock.Services
{
    /// <summary>
    /// Service that monitors OS-level clipboard events via Win32 Clipboard Format Listener
    /// and maintains an in-memory history of recent text and image items (capped at 20).
    /// </summary>
    public class ClipboardHistoryService
    {
        private static ClipboardHistoryService? _instance;
        public static ClipboardHistoryService Instance => _instance ??= new ClipboardHistoryService();

        private IntPtr _hwnd = IntPtr.Zero;
        private HwndSource? _hwndSource;
        private bool _isInternalCopy;

        public ObservableCollection<ClipboardItem> History { get; } = new();

        public void Attach(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || _hwnd != IntPtr.Zero) return;
            _hwnd = hwnd;

            try
            {
                NativeMethods.AddClipboardFormatListener(_hwnd);
                _hwndSource = HwndSource.FromHwnd(_hwnd);
                _hwndSource?.AddHook(HwndHook);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardHistoryService] Attach failed: {ex.Message}");
            }
        }

        public void Detach()
        {
            if (_hwnd == IntPtr.Zero) return;

            try
            {
                _hwndSource?.RemoveHook(HwndHook);
                _hwndSource = null;
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardHistoryService] Detach failed: {ex.Message}");
            }
            finally
            {
                _hwnd = IntPtr.Zero;
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                ProcessClipboardUpdateAsync();
            }
            return IntPtr.Zero;
        }

        private async void ProcessClipboardUpdateAsync()
        {
            if (_isInternalCopy)
            {
                _isInternalCopy = false;
                return;
            }

            // Clipboard may momentarily be locked by other apps while copying; retry up to 5 times.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(80);
                }

                try
                {
                    if (System.Windows.Clipboard.ContainsImage())
                    {
                        var img = System.Windows.Clipboard.GetImage();
                        if (img != null)
                        {
                            img.Freeze();

                            // Avoid adding if same as the last item
                            if (History.Count > 0 && History[0].IsImage &&
                                History[0].ImagePreview is BitmapSource prevImg &&
                                prevImg.PixelWidth == img.PixelWidth &&
                                prevImg.PixelHeight == img.PixelHeight)
                            {
                                return;
                            }

                            int w = img.PixelWidth;
                            int h = img.PixelHeight;

                            var item = new ClipboardItem
                            {
                                Title = $"Image ({w} × {h})",
                                ImagePreview = img,
                                IsImage = true,
                                TextContent = null,
                                CopiedAt = DateTime.Now,
                                MetaInfo = $"PNG Image ({w}×{h})"
                            };

                            AddItem(item);
                            return;
                        }
                    }
                    else if (System.Windows.Clipboard.ContainsText())
                    {
                        string text = System.Windows.Clipboard.GetText();
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            return;
                        }

                        // Avoid adding if same as the last item
                        if (History.Count > 0 && !History[0].IsImage && string.Equals(History[0].TextContent, text, StringComparison.Ordinal))
                        {
                            return;
                        }

                        string title = text.Trim();
                        int newline = title.IndexOfAny(new[] { '\r', '\n' });
                        if (newline >= 0)
                        {
                            title = title.Substring(0, newline).Trim();
                        }
                        if (title.Length > 45)
                        {
                            title = title.Substring(0, 42) + "...";
                        }
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            title = "Text snippet";
                        }

                        int charCount = text.Length;
                        int lineCount = text.Split('\n').Length;
                        string meta = lineCount > 1 ? $"{charCount} chars • {lineCount} lines" : $"{charCount} chars";

                        var item = new ClipboardItem
                        {
                            Title = title,
                            TextContent = text,
                            ImagePreview = null,
                            IsImage = false,
                            CopiedAt = DateTime.Now,
                            MetaInfo = meta
                        };

                        AddItem(item);
                        return;
                    }
                    else
                    {
                        // Other formats or empty
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClipboardHistoryService] ProcessClipboard attempt {attempt} failed: {ex.Message}");
                }
            }
        }

        private void AddItem(ClipboardItem item)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => AddItem(item));
                return;
            }

            // Deduplicate: If same text exists elsewhere in history, remove old instance so it promotes to top
            if (!item.IsImage && !string.IsNullOrEmpty(item.TextContent))
            {
                for (int i = History.Count - 1; i >= 0; i--)
                {
                    if (!History[i].IsImage && string.Equals(History[i].TextContent, item.TextContent, StringComparison.Ordinal))
                    {
                        History.RemoveAt(i);
                    }
                }
            }

            History.Insert(0, item);
            while (History.Count > 20)
            {
                History.RemoveAt(History.Count - 1);
            }
        }

        public void CopyToClipboard(ClipboardItem item)
        {
            if (item == null) return;

            try
            {
                _isInternalCopy = true;

                if (item.IsImage && item.ImagePreview is BitmapSource bmp)
                {
                    System.Windows.Clipboard.SetImage(bmp);
                }
                else if (!string.IsNullOrEmpty(item.TextContent))
                {
                    System.Windows.Clipboard.SetText(item.TextContent);
                }

                // Promote item to top of history
                int idx = History.IndexOf(item);
                if (idx > 0)
                {
                    History.Move(idx, 0);
                    item.CopiedAt = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardHistoryService] CopyToClipboard failed: {ex.Message}");
                _isInternalCopy = false;
            }
        }

        public void RemoveItem(ClipboardItem item)
        {
            if (item == null) return;

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => RemoveItem(item));
                return;
            }

            History.Remove(item);
        }

        public void ClearHistory()
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(ClearHistory);
                return;
            }

            History.Clear();
        }
    }
}
