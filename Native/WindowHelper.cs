using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

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
        /// Toggles Show Desktop (minimizes/restores all windows like Win+D).
        /// </summary>
        public static void ToggleShowDesktop()
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    shell?.ToggleDesktop();
                    return;
                }
            }
            catch { }

            NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, 0);
            NativeMethods.keybd_event(NativeMethods.VK_D, 0, 0, 0);
            NativeMethods.keybd_event(NativeMethods.VK_D, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        }

        /// <summary>
        /// Resolves shortcuts, system app filenames (explorer.exe, notepad.exe, wt.exe), and registry paths.
        /// </summary>
        public static string ResolveExecutablePath(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName)) return string.Empty;

            // Direct file or folder exists
            if (File.Exists(pathOrName) || Directory.Exists(pathOrName))
            {
                return Path.GetFullPath(pathOrName);
            }

            // Resolve shortcut (.lnk)
            if (pathOrName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = ShortcutHelper.ResolveShortcut(pathOrName);
                if (File.Exists(resolved) || Directory.Exists(resolved)) return resolved;
            }

            string nameOnly = Path.GetFileName(pathOrName);

            // Windows System32 and Windows directory
            string sysDir = Environment.SystemDirectory;
            string candSys = Path.Combine(sysDir, nameOnly);
            if (File.Exists(candSys)) return candSys;

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string candWin = Path.Combine(winDir, nameOnly);
            if (File.Exists(candWin)) return candWin;

            // Edge browser common paths
            if (nameOnly.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
            {
                string edge1 = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
                if (File.Exists(edge1)) return edge1;
                string edge2 = @"C:\Program Files\Microsoft\Edge\Application\msedge.exe";
                if (File.Exists(edge2)) return edge2;
            }

            // Chrome browser common paths
            if (nameOnly.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase))
            {
                string chrome1 = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
                if (File.Exists(chrome1)) return chrome1;
                string chrome2 = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
                if (File.Exists(chrome2)) return chrome2;
            }

            // PATH environment variables
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var folder in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        string full = Path.Combine(folder.Trim(), nameOnly);
                        if (File.Exists(full)) return full;
                    }
                    catch { }
                }
            }

            return pathOrName;
        }

        /// <summary>
        /// Extracts the highest resolution (256x256 Jumbo) icon available for a window handle, path, or title.
        /// </summary>
        public static ImageSource GetWindowIcon(IntPtr hWnd, string? rawPath = null, string? title = null)
        {
            string? targetPath = !string.IsNullOrEmpty(rawPath) ? ResolveExecutablePath(rawPath) : null;

            // If we have targetPath, check cache
            if (!string.IsNullOrEmpty(targetPath) && _iconCache.TryGetValue(targetPath, out var cached))
            {
                return cached;
            }

            ImageSource? result = null;

            // 1. If path points to an existing file or directory, extract Shell Jumbo 256x256 or PrivateExtractIcons
            if (!string.IsNullOrEmpty(targetPath) && (File.Exists(targetPath) || Directory.Exists(targetPath)))
            {
                result = ExtractShellJumboIcon(targetPath);
                if (result == null && File.Exists(targetPath))
                {
                    result = ExtractPrivateIcon(targetPath, 256);
                }
                if (result == null && File.Exists(targetPath))
                {
                    try
                    {
                        using Icon? icon = Icon.ExtractAssociatedIcon(targetPath);
                        if (icon != null)
                        {
                            result = CreateBitmapSourceFromHIcon(icon.Handle);
                        }
                    }
                    catch { }
                }
            }

            // 2. If no icon yet and we have a valid HWND, query process path or WM_GETICON
            if (result == null && hWnd != IntPtr.Zero)
            {
                var (_, exePath) = GetProcessInfo(hWnd);
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    result = ExtractShellJumboIcon(exePath);
                    if (result == null)
                    {
                        result = ExtractPrivateIcon(exePath, 256);
                    }
                }

                if (result == null)
                {
                    IntPtr hIcon = IntPtr.Zero;
                    NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero, 0x0002, 100, out hIcon);
                    if (hIcon == IntPtr.Zero)
                    {
                        NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero, 0x0002, 100, out hIcon);
                    }
                    if (hIcon == IntPtr.Zero)
                    {
                        hIcon = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCLP_HICON);
                    }

                    if (hIcon != IntPtr.Zero)
                    {
                        result = CreateBitmapSourceFromHIcon(hIcon);
                    }
                }
            }

            // 3. Fallback: generate a crisp modern letter squircle badge if all extraction fails
            if (result == null)
            {
                string fallbackText = !string.IsNullOrWhiteSpace(title)
                    ? title
                    : (!string.IsNullOrEmpty(rawPath) ? Path.GetFileNameWithoutExtension(rawPath) : "App");
                result = CreateFallbackIcon(fallbackText);
            }

            if (!string.IsNullOrEmpty(targetPath))
            {
                _iconCache[targetPath] = result;
            }

            return result;
        }

        private static ImageSource? ExtractShellJumboIcon(string path)
        {
            try
            {
                var shinfo = new NativeMethods.SHFILEINFO();
                uint flags = NativeMethods.SHGFI_SYSICONINDEX;
                IntPtr ret = NativeMethods.SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (ret == IntPtr.Zero) return null;

                var iidImageList = new Guid("46EB5927-58E5-402A-9F87-B287FA04D4A4");

                // 1. Try Jumbo (256x256)
                int hr = NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref iidImageList, out NativeMethods.IImageList iml);
                if (hr == 0 && iml != null)
                {
                    hr = iml.GetIcon(shinfo.iIcon, NativeMethods.ILD_TRANSPARENT, out IntPtr hIcon);
                    if (hr == 0 && hIcon != IntPtr.Zero)
                    {
                        return CreateBitmapSourceFromHIcon(hIcon);
                    }
                }

                // 2. Try Extra Large (48x48)
                hr = NativeMethods.SHGetImageList(NativeMethods.SHIL_EXTRALARGE, ref iidImageList, out iml);
                if (hr == 0 && iml != null)
                {
                    hr = iml.GetIcon(shinfo.iIcon, NativeMethods.ILD_TRANSPARENT, out IntPtr hIcon);
                    if (hr == 0 && hIcon != IntPtr.Zero)
                    {
                        return CreateBitmapSourceFromHIcon(hIcon);
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? ExtractPrivateIcon(string exePath, int size = 256)
        {
            try
            {
                IntPtr[] phicon = new IntPtr[1];
                uint[] piconid = new uint[1];
                uint count = NativeMethods.PrivateExtractIcons(exePath, 0, size, size, phicon, piconid, 1, 0);
                if (count > 0 && phicon[0] != IntPtr.Zero)
                {
                    return CreateBitmapSourceFromHIcon(phicon[0]);
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? CreateBitmapSourceFromHIcon(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch
            {
                return null;
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        private static ImageSource CreateFallbackIcon(string title)
        {
            int size = 128;
            var renderTarget = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = new LinearGradientBrush(
                    Color.FromRgb(30, 58, 138), // Deep blue
                    Color.FromRgb(14, 116, 144), // Cyan
                    new Point(0, 0),
                    new Point(1, 1));
                dc.DrawRoundedRectangle(brush, null, new Rect(4, 4, size - 8, size - 8), 28, 28);

                string letter = !string.IsNullOrWhiteSpace(title) ? title.Trim().Substring(0, 1).ToUpper() : "★";
                var typeface = new Typeface(new FontFamily("Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var formattedText = new FormattedText(
                    letter,
                    CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    typeface,
                    54,
                    System.Windows.Media.Brushes.White,
                    96);

                double textX = (size - formattedText.Width) / 2;
                double textY = (size - formattedText.Height) / 2;
                dc.DrawText(formattedText, new Point(textX, textY));
            }
            renderTarget.Render(visual);
            renderTarget.Freeze();
            return renderTarget;
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
