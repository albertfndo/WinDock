using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinDock.Native
{
    public static class WindowHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();

        /// <summary>
        /// Determines if the given window handle is a valid, user-facing application window (like Alt-Tab).
        /// </summary>
        public static bool IsAltTabWindow(IntPtr hWnd, IntPtr dockHwnd)
        {
            if (hWnd == IntPtr.Zero || hWnd == dockHwnd) return false;
            if (!NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd)) return false;

            // Ignore Taskbar and Program Manager
            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetWindowText(hWnd, className, 256);
            if (className.Length == 0) return false;

            // Check Extended Window Styles
            int exStyle = (int)NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
            {
                return false;
            }

            // Exclude cloaked windows (Windows 10/11 UWP apps running in background or on other virtual desktops)
            int cloaked = 0;
            int hr = NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int));
            if (hr == 0 && cloaked != 0)
            {
                return false;
            }

            // Check if window has zero size
            if (NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT rect))
            {
                if (rect.Width <= 0 || rect.Height <= 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the window title.
        /// </summary>
        public static string GetWindowTitle(IntPtr hWnd)
        {
            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0) return string.Empty;

            StringBuilder sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// Gets the process name and full executable path for the window.
        /// </summary>
        public static (string ProcessName, string? ExecutablePath) GetProcessInfo(IntPtr hWnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0) return ("Unknown", null);

                using Process process = Process.GetProcessById((int)processId);
                string procName = process.ProcessName;
                string? exePath = null;
                try
                {
                    exePath = process.MainModule?.FileName;
                }
                catch
                {
                    // Access denied for system processes
                }

                return (procName, exePath);
            }
            catch
            {
                return ("Unknown", null);
            }
        }

        /// <summary>
        /// Activates the window, restoring it if minimized and bringing it to the foreground.
        /// </summary>
        public static void ActivateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            if (NativeMethods.IsIconic(hWnd))
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
            }
            else
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
            }

            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.BringWindowToTop(hWnd);
        }

        /// <summary>
        /// Minimizes the window.
        /// </summary>
        public static void MinimizeWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE);
        }

        /// <summary>
        /// Closes the window gracefully.
        /// </summary>
        public static void CloseWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Extracts the best available icon for a window handle or executable path.
        /// </summary>
        public static ImageSource? GetWindowIcon(IntPtr hWnd, string? exePath = null)
        {
            // 1. If we have exePath, check memory cache
            if (!string.IsNullOrEmpty(exePath) && _iconCache.TryGetValue(exePath, out var cached))
            {
                return cached;
            }

            IntPtr hIcon = IntPtr.Zero;

            // 2. Try WM_GETICON (BIG -> SMALL2 -> SMALL)
            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero, 0x0002, 150, out hIcon);
                if (hIcon == IntPtr.Zero)
                {
                    NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero, 0x0002, 150, out hIcon);
                }
                if (hIcon == IntPtr.Zero)
                {
                    NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL, IntPtr.Zero, 0x0002, 150, out hIcon);
                }

                // 3. Try Class Long HICON
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCLP_HICON);
                }
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCLP_HICONSM);
                }
            }

            if (hIcon != IntPtr.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                catch
                {
                    // Fallback to executable extraction below
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }

            // 4. Try Extracting Associated Icon from File Path
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    using Icon? icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        _iconCache[exePath] = source;
                        return source;
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            return null;
        }

        /// <summary>
        /// Clears the icon cache.
        /// </summary>
        public static void ClearIconCache()
        {
            _iconCache.Clear();
        }
    }
}
