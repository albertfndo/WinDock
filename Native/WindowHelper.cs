using System.Linq;
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

namespace WinDock.Native
{
    public static class WindowHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();

        /// <summary>
        /// Determines if the given window handle is a valid, user-facing application window (like Alt-Tab).
        /// Supports normal Win32 apps and modern Windows 10/11 UWP apps (WhatsApp, Settings, etc.).
        /// </summary>
        public static bool IsAltTabWindow(IntPtr hWnd, IntPtr dockHwnd)
        {
            if (hWnd == IntPtr.Zero || hWnd == dockHwnd) return false;
            if (!NativeMethods.IsWindow(hWnd)) return false;

            // Must be visible OR iconic (minimized window is still an open user app)
            bool isVisible = NativeMethods.IsWindowVisible(hWnd);
            bool isIconic = NativeMethods.IsIconic(hWnd);
            if (!isVisible && !isIconic) return false;

            // Exclude Windows Shell, Desktop, and known background system classes
            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, className, 256);
            string cls = className.ToString();

            if (cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" ||
                cls == "Progman" ||
                cls == "WorkerW" ||
                cls == "Windows.UI.Core.CoreWindow" ||
                cls == "ApplicationFrameTitleBarWindow" ||
                cls == "EdgeUiInputTopWndClass" ||
                cls == "DummyDWMListenerWindow" ||
                cls == "GDI+ Window" ||
                cls == "MSCTFIME UI" ||
                cls == "Default IME")
            {
                return false;
            }

            var (procName, exePath) = GetProcessInfo(hWnd);
            string title = GetWindowTitle(hWnd);

            // Special handling for WhatsApp & recognized modern user applications:
            // Modern WinUI 3 WhatsApp Desktop may have an empty Win32 window text,
            // or may have an owner anchor window, or be cloaked when minimized/inactive!
            bool isWhatsApp = procName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
                              title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
                              cls.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
                              (!string.IsNullOrEmpty(exePath) && exePath.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase));

            // If this is WhatsApp, always allow it as an active application window!
            if (isWhatsApp)
            {
                return true;
            }

            // If title is empty, background daemons have empty titles
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            // Check Extended Window Styles: exclude ToolWindows unless AppWindow is explicitly set
            int exStyle = (int)NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
            {
                return false;
            }

            // Exclude owned windows (secondary dialogs, popup windows) unless marked as standalone AppWindow
            // WinUI 3 (WhatsApp) and modern Store apps can have an owner anchor without WS_EX_APPWINDOW
            IntPtr owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
            if (owner != IntPtr.Zero && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
            {
                bool isUserApp = isWhatsApp || cls == "WinUIDesktopWin32WindowClass" || cls == "ApplicationFrameWindow";
                if (!isUserApp)
                {
                    return false;
                }
            }

            // Check Window Rect & Off-screen positioning (used by Realtek and background tray daemons):
            // Minimized windows always report -32000,-32000 here, so the rect checks only apply to restored windows;
            // minimized ones are validated via their normal placement below.
            if (NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT rect) && !isIconic)
            {
                // 1. Zero size windows
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return false;
                }

                // 2. Off-screen parked coordinates (< -5000), standard for background tray utilities like Realtek
                if (rect.Left < -5000 || rect.Top < -5000 || rect.Right < -5000 || rect.Bottom < -5000)
                {
                    return false;
                }
            }

            // 3. Monitor check: If not minimized, window MUST touch at least one physical monitor
            if (!isIconic)
            {
                IntPtr hMon = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONULL);
                if (hMon == IntPtr.Zero)
                {
                    return false;
                }
            }
            else
            {
                // If minimized, check that normal placement was not off-screen
                var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
                if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
                {
                    if (wp.rcNormalPosition.Left < -5000 || wp.rcNormalPosition.Top < -5000)
                    {
                        return false;
                    }
                }
            }

            // Cloaked check:
            // Exclude windows cloaked by other virtual desktops (DWM_CLOAKED_INHERITED = 4).
            // For UWP/WinUI apps like WhatsApp:
            // When minimized, cloaked is 1 (DWM_CLOAKED_APP) or 2 (DWM_CLOAKED_SHELL), which is normal!
            int cloaked = 0;
            int hr = NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int));
            if (hr == 0 && cloaked != 0)
            {
                // If on another virtual desktop, always hide
                if ((cloaked & NativeMethods.DWM_CLOAKED_INHERITED) != 0)
                {
                    return false;
                }

                // If not iconic and not WhatsApp, hide background suspended tasks
                if (!isIconic && !isWhatsApp)
                {
                    return false;
                }
            }

            // Audio & Hardware Drivers background utilities (Realtek, Nahimic, Waves, Intel, etc.) - ALWAYS EXCLUDED
            if (procName.StartsWith("Rtk", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("RAVCpl", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("RAVBg", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("Realtek", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("HDMSvr", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("Waves", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("Nahimic", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("igfx", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("nvtray", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("nvxdsync", StringComparison.OrdinalIgnoreCase) ||
                procName.StartsWith("SynTP", StringComparison.OrdinalIgnoreCase))
            {
                if (isIconic) return false;
                if (rect.Left < -100 || rect.Top < -100) return false;
            }

            // Background system hosts - ALWAYS EXCLUDED
            if (string.Equals(procName, "SearchHost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "LockApp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "SystemSettingsBroker", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "PhoneExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "Widgets", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "SecurityHealthSystray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "SecurityHealthHost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "conhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "dwm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "taskhostw", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "sihost", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

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
        /// Resolves UWP apps (WhatsApp, SystemSettings, etc.) hosted by ApplicationFrameHost to their real process!
        /// </summary>
        public static (string ProcessName, string? ExecutablePath) GetProcessInfo(IntPtr hWnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0) return ("Unknown", null);

                string procName = "Unknown";
                string? exePath = null;

                // 1. Query path using PROCESS_QUERY_LIMITED_INFORMATION (works even on packaged Store apps like WhatsApp without Access Denied!)
                IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        uint size = (uint)sb.Capacity;
                        if (NativeMethods.QueryFullProcessImageName(hProc, 0, sb, ref size))
                        {
                            exePath = sb.ToString();
                            procName = Path.GetFileNameWithoutExtension(exePath);
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProc);
                    }
                }

                // 2. Fallback to Process.GetProcessById if needed
                if (string.IsNullOrEmpty(exePath) || procName == "Unknown")
                {
                    try
                    {
                        using Process process = Process.GetProcessById((int)processId);
                        procName = process.ProcessName;
                        try { exePath ??= process.MainModule?.FileName; } catch { }
                    }
                    catch { }
                }

                // 3. If this is ApplicationFrameHost (classic UWP wrapper), resolve the real hosted UWP process!
                if (string.Equals(procName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    uint hostedPid = 0;
                    string childTitle = string.Empty;

                    // Enumerate all child windows to find the real hosted UWP process and child title
                    NativeMethods.EnumChildWindows(hWnd, (childHwnd, l) =>
                    {
                        NativeMethods.GetWindowThreadProcessId(childHwnd, out uint cPid);
                        if (cPid != 0 && cPid != processId)
                        {
                            hostedPid = cPid;
                        }

                        if (string.IsNullOrEmpty(childTitle))
                        {
                            string t = GetWindowTitle(childHwnd);
                            if (!string.IsNullOrWhiteSpace(t))
                            {
                                childTitle = t.Trim();
                            }
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (hostedPid != 0)
                    {
                        IntPtr hUwpProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, hostedPid);
                        if (hUwpProc != IntPtr.Zero)
                        {
                            try
                            {
                                var sb = new StringBuilder(1024);
                                uint size = (uint)sb.Capacity;
                                if (NativeMethods.QueryFullProcessImageName(hUwpProc, 0, sb, ref size))
                                {
                                    exePath = sb.ToString();
                                    procName = Path.GetFileNameWithoutExtension(exePath);
                                }
                            }
                            finally
                            {
                                NativeMethods.CloseHandle(hUwpProc);
                            }
                        }

                        if (string.IsNullOrEmpty(exePath) || string.Equals(procName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using Process uwpProc = Process.GetProcessById((int)hostedPid);
                                procName = uwpProc.ProcessName;
                                try { exePath ??= uwpProc.MainModule?.FileName; } catch { }
                            }
                            catch { }
                        }
                    }

                    if (string.Equals(procName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(childTitle))
                        {
                            procName = childTitle;
                        }
                        else
                        {
                            string title = GetWindowTitle(hWnd);
                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                procName = title.Trim();
                            }
                        }
                    }
                }

                return (procName, exePath);
            }
            catch
            {
                return ("Unknown", null);
            }
        }

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
        /// Resolves shortcuts, system app filenames (explorer.exe, notepad.exe, wt.exe), and registry paths.
        /// </summary>
        public static string ResolveExecutablePath(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName)) return string.Empty;

            // Resolve shortcut (.lnk) FIRST before checking File.Exists,
            // because .lnk files exist on disk, but we need their target executable!
            if (pathOrName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = ShortcutHelper.ResolveShortcut(pathOrName);
                if (!string.IsNullOrWhiteSpace(resolved) && (File.Exists(resolved) || Directory.Exists(resolved)))
                {
                    return Path.GetFullPath(resolved);
                }
            }

            // Direct file or folder exists
            if (File.Exists(pathOrName) || Directory.Exists(pathOrName))
            {
                return Path.GetFullPath(pathOrName);
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
        /// Fully supports UWP Store apps (WhatsApp, Settings), Shell Jumbo icons, PrivateExtractIcons (256px),
        /// and modern vector branding. 100% crash-proof with fallback handling.
        /// </summary>
        public static ImageSource GetWindowIcon(IntPtr hWnd, string? rawPath = null, string? title = null)
        {
            try
            {
                string? targetPath = !string.IsNullOrEmpty(rawPath) ? ResolveExecutablePath(rawPath) : null;
                string cacheKey = !string.IsNullOrEmpty(rawPath)
                    ? rawPath
                    : (!string.IsNullOrEmpty(targetPath) ? targetPath : (!string.IsNullOrEmpty(title) ? title : (hWnd != IntPtr.Zero ? $"HWND_{hWnd}" : "App")));

                if (_iconCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }

                ImageSource? result = null;
                BitmapSource? fallbackLowRes = null;

                bool isWhatsApp = (!string.IsNullOrEmpty(targetPath) && targetPath.Contains("whatsapp", StringComparison.OrdinalIgnoreCase)) ||
                                  (!string.IsNullOrEmpty(title) && title.Contains("whatsapp", StringComparison.OrdinalIgnoreCase)) ||
                                  (!string.IsNullOrEmpty(rawPath) && rawPath.Contains("whatsapp", StringComparison.OrdinalIgnoreCase));

                bool isSettings = (!string.IsNullOrEmpty(targetPath) && (targetPath.Contains("ms-settings", StringComparison.OrdinalIgnoreCase) || targetPath.Contains("systemsettings", StringComparison.OrdinalIgnoreCase))) ||
                                  (!string.IsNullOrEmpty(title) && title.Equals("Settings", StringComparison.OrdinalIgnoreCase)) ||
                                  (!string.IsNullOrEmpty(rawPath) && rawPath.Contains("ms-settings", StringComparison.OrdinalIgnoreCase));

                // 1. High-resolution branded vector icons (WhatsApp & Settings)
                if (isWhatsApp)
                {
                    result = CreateWhatsAppVectorIcon();
                    _iconCache[cacheKey] = result;
                    return result;
                }
                if (isSettings)
                {
                    result = CreateSettingsVectorIcon();
                    _iconCache[cacheKey] = result;
                    return result;
                }

                // 2. If rawPath was a shortcut (.lnk), check for custom icon location specified in shortcut
                if (!string.IsNullOrEmpty(rawPath) && rawPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var (_, iconLocation, iconIndex) = ShortcutHelper.ResolveShortcutDetails(rawPath);
                    if (!string.IsNullOrEmpty(iconLocation) && File.Exists(iconLocation))
                    {
                        var customIcon = ExtractPrivateIcon(iconLocation, 256, iconIndex);
                        if (customIcon != null && customIcon.PixelWidth >= 48)
                        {
                            result = customIcon;
                        }
                        else if (customIcon != null && fallbackLowRes == null)
                        {
                            fallbackLowRes = customIcon;
                        }
                    }
                }

                // 3. Try PrivateExtractIcons on targetPath (256x256) directly from EXE/ICO
                if (result == null && !string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                {
                    string ext = Path.GetExtension(targetPath);
                    if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var privateIcon = ExtractPrivateIcon(targetPath, 256, 0);
                        if (privateIcon != null && privateIcon.PixelWidth >= 48)
                        {
                            result = privateIcon;
                        }
                        else if (privateIcon != null && fallbackLowRes == null)
                        {
                            fallbackLowRes = privateIcon;
                        }
                    }
                }

                // 4. Try Store App package assets if path points to WindowsApps
                if (result == null && !string.IsNullOrEmpty(targetPath) && targetPath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                {
                    result = TryExtractStoreAppLogo(targetPath);
                }

                // 5. Try Shell Jumbo 256x256 image list
                if (result == null)
                {
                    string? pathForShell = (!string.IsNullOrEmpty(targetPath) && (File.Exists(targetPath) || Directory.Exists(targetPath)))
                        ? targetPath
                        : ((!string.IsNullOrEmpty(rawPath) && (File.Exists(rawPath) || Directory.Exists(rawPath))) ? rawPath : null);

                    if (!string.IsNullOrEmpty(pathForShell))
                    {
                        var jumbo = ExtractShellJumboIcon(pathForShell);
                        if (jumbo is BitmapSource bs && bs.PixelWidth >= 48)
                        {
                            result = jumbo;
                        }
                        else if (jumbo is BitmapSource bsSmall && fallbackLowRes == null)
                        {
                            fallbackLowRes = bsSmall;
                        }
                    }
                }

                // 6. If HWND is valid, query process info or WM_GETICON / class icon
                if (result == null && hWnd != IntPtr.Zero)
                {
                    var (procName, exePath) = GetProcessInfo(hWnd);
                    if (procName.Contains("whatsapp", StringComparison.OrdinalIgnoreCase))
                    {
                        result = CreateWhatsAppVectorIcon();
                    }
                    else if (procName.Contains("systemsettings", StringComparison.OrdinalIgnoreCase))
                    {
                        result = CreateSettingsVectorIcon();
                    }
                    else if (!string.IsNullOrEmpty(exePath))
                    {
                        if (exePath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                        {
                            result = TryExtractStoreAppLogo(exePath);
                        }
                        if (result == null && File.Exists(exePath))
                        {
                            result = ExtractPrivateIcon(exePath, 256, 0);
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

                // 7. Try System.Drawing AssociatedIcon
                if (result == null && !string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                {
                    try
                    {
                        using Icon? icon = Icon.ExtractAssociatedIcon(targetPath);
                        if (icon != null)
                        {
                            var assoc = CreateBitmapSourceFromHIcon(icon.Handle);
                            if (assoc != null && fallbackLowRes == null)
                            {
                                fallbackLowRes = assoc;
                            }
                        }
                    }
                    catch { }
                }

                // 8. Use low-res candidate if no high-res was found
                if (result == null && fallbackLowRes != null)
                {
                    result = fallbackLowRes;
                }

                // 9. Fallback: generate a crisp modern letter squircle badge if all extraction fails
                if (result == null)
                {
                    string fallbackText = !string.IsNullOrWhiteSpace(title)
                        ? title
                        : (!string.IsNullOrEmpty(rawPath) ? Path.GetFileNameWithoutExtension(rawPath) : "App");
                    result = CreateFallbackIcon(fallbackText);
                }

                _iconCache[cacheKey] = result;
                return result;
            }
            catch
            {
                string fallbackText = !string.IsNullOrWhiteSpace(title)
                    ? title
                    : (!string.IsNullOrEmpty(rawPath) ? Path.GetFileNameWithoutExtension(rawPath) : "App");
                return CreateFallbackIcon(fallbackText);
            }
        }

        private static ImageSource? TryExtractStoreAppLogo(string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath)) return null;
                string dir = Path.GetDirectoryName(exePath) ?? string.Empty;
                if (!Directory.Exists(dir)) return null;

                string assetsDir = Path.Combine(dir, "Assets");
                string searchDir = Directory.Exists(assetsDir) ? assetsDir : dir;

                string[] patterns = new[]
                {
                    "*targetsize-256*.png",
                    "*targetsize-128*.png",
                    "*targetsize-96*.png",
                    "*targetsize-48*.png",
                    "*Square150x150Logo*.png",
                    "*Square44x44Logo*.png",
                    "*AppList*.png",
                    "*Logo*.png"
                };

                foreach (var pattern in patterns)
                {
                    var matches = Directory.GetFiles(searchDir, pattern, SearchOption.TopDirectoryOnly);
                    if (matches.Length > 0)
                    {
                        var best = matches.OrderByDescending(f => new FileInfo(f).Length).First();
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(best, UriKind.Absolute);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.DecodePixelWidth = 256;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                }
            }
            catch { }
            return null;
        }

        public static ImageSource CreateWhatsAppVectorIcon()
        {
            int size = 256;
            var renderTarget = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Background: Emerald green WhatsApp gradient (#25D366 to #128C7E)
                var bgBrush = new LinearGradientBrush(
                    Color.FromRgb(37, 211, 102), // #25D366
                    Color.FromRgb(18, 140, 126), // #128C7E
                    new Point(0, 0),
                    new Point(1, 1));

                dc.DrawRoundedRectangle(bgBrush, null, new Rect(12, 12, 232, 232), 52, 52);

                // Official WhatsApp Bubble & Phone handset SVG path
                string pathData = "M175.7 141.2c-2.8-1.4-16.5-8.1-19-9.1-2.6-1-4.5-1.4-6.4 1.4-1.9 2.8-7.3 9.1-9 11-1.6 1.9-3.3 2.1-6.1.7-2.8-1.4-11.8-4.4-22.5-13.9-8.3-7.4-13.9-16.6-15.5-19.4-1.6-2.8-.2-4.3 1.2-5.7 1.3-1.3 2.8-3.3 4.2-5 1.4-1.6 1.9-2.8 2.8-4.6.9-1.9.5-3.5-.2-4.9-.8-1.4-6.4-15.4-8.7-21.1-2.3-5.5-4.6-4.8-6.4-4.9-1.6 0-3.5-.1-5.4-.1-1.9 0-4.9.7-7.5 3.5-2.6 2.8-9.8 9.6-9.8 23.4s10 27.2 11.4 29.1c1.4 1.9 19.7 30.1 47.7 42.2 6.7 2.9 11.9 4.6 16 5.9 6.7 2.1 12.8 1.8 17.6 1.1 5.4-.8 16.5-6.7 18.8-13.2 2.3-6.5 2.3-12 1.6-13.2-.7-1.2-2.6-1.9-5.4-3.3zM128 32C75 32 32 75 32 128c0 19.5 5.8 37.6 15.9 52.8L35.2 224l44.6-11.7C94.4 220.3 110.8 224 128 224c53 0 96-43 96-96s-43-96-96-96zm0 172.8c-15.3 0-29.6-4.5-41.6-12.3l-3-1.8-26.4 6.9 7-25.7-2-3.1C53.7 156.4 48 142.6 48 128c0-44.1 35.9-80 80-80s80 35.9 80 80-35.9 80-80 80z";

                var geom = Geometry.Parse(pathData);

                dc.PushTransform(new ScaleTransform(0.70, 0.70, 128, 128));
                dc.DrawGeometry(System.Windows.Media.Brushes.White, null, geom);
                dc.Pop();
            }
            renderTarget.Render(visual);
            renderTarget.Freeze();
            return renderTarget;
        }

        public static ImageSource CreateSettingsVectorIcon()
        {
            int size = 256;
            var renderTarget = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bgBrush = new LinearGradientBrush(
                    Color.FromRgb(71, 85, 105), // #475569
                    Color.FromRgb(30, 41, 59),  // #1E293B
                    new Point(0, 0),
                    new Point(1, 1));

                dc.DrawRoundedRectangle(bgBrush, null, new Rect(12, 12, 232, 232), 52, 52);

                string gearPath = "M194.3 129.9c0-3.3-.3-6.6-.8-9.8l21.1-16.5c1.9-1.5 2.4-4.2 1.2-6.4l-20-34.6c-1.2-2.2-3.8-3.1-6.1-2.2l-24.9 10c-5.2-4-10.9-7.3-17.1-9.8l-3.8-26.5C143.6 31.6 141.2 30 138.8 30h-40c-2.4 0-4.8 1.6-5.1 4.1l-3.8 26.5c-6.2 2.5-11.9 5.8-17.1 9.8l-24.9-10c-2.3-.9-4.9 0-6.1 2.2l-20 34.6c-1.2 2.2-.7 4.9 1.2 6.4l21.1 16.5c-.5 3.2-.8 6.5-.8 9.8s.3 6.6.8 9.8l-21.1 16.5c-1.9 1.5-2.4 4.2-1.2 6.4l20 34.6c1.2 2.2 3.8 3.1 6.1 2.2l24.9-10c5.2 4 10.9 7.3 17.1 9.8l3.8 26.5c.3 2.5 2.7 4.1 5.1 4.1h40c2.4 0 4.8-1.6 5.1-4.1l3.8-26.5c6.2-2.5 11.9-5.8 17.1-9.8l24.9 10c2.3.9 4.9 0 6.1-2.2l20-34.6c1.2-2.2.7-4.9-1.2-6.4l-21.1-16.5c.5-3.2.8-6.5.8-9.8zm-66.3 38.1c-22.1 0-40-17.9-40-40s17.9-40 40-40 40 17.9 40 40-17.9 40-40 40z";

                var geom = Geometry.Parse(gearPath);

                dc.PushTransform(new ScaleTransform(0.72, 0.72, 128, 128));
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(241, 245, 249)), null, geom);
                dc.Pop();
            }
            renderTarget.Render(visual);
            renderTarget.Freeze();
            return renderTarget;
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
                    if (hr == 0 && hIcon != IntPtr.Zero && CreateBitmapSourceFromHIcon(hIcon) is BitmapSource jumbo)
                    {
                        return CropLegacyJumbo(jumbo);
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

        /// <summary>
        /// Apps without a 256px icon come back from SHIL_JUMBO as a 32/48px icon in the top-left of a 256 canvas,
        /// which renders tiny and blurry. Crop to the real icon so WPF scales it like a normal icon.
        /// </summary>
        private static BitmapSource CropLegacyJumbo(BitmapSource jumbo)
        {
            if (jumbo.PixelWidth < 256 || jumbo.PixelHeight < 256) return jumbo;
            try
            {
                var bgra = jumbo.Format == PixelFormats.Bgra32 ? jumbo : new FormatConvertedBitmap(jumbo, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
                var px = new byte[stride * h];
                bgra.CopyPixels(px, stride, 0);

                foreach (int size in new[] { 32, 48 })
                {
                    bool emptyOutside = true;
                    for (int y = 0; y < h && emptyOutside; y++)
                        for (int x = y < size ? size : 0; x < w; x++)
                            if (px[y * stride + x * 4 + 3] != 0) { emptyOutside = false; break; }

                    if (emptyOutside)
                    {
                        var cropped = new CroppedBitmap(bgra, new Int32Rect(0, 0, size, size));
                        cropped.Freeze();
                        return cropped;
                    }
                }
            }
            catch { }
            return jumbo;
        }

        private static BitmapSource? ExtractPrivateIcon(string exePath, int size = 256, int iconIndex = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
                IntPtr[] phicon = new IntPtr[1];
                uint[] piconid = new uint[1];
                uint count = NativeMethods.PrivateExtractIcons(exePath, iconIndex, size, size, phicon, piconid, 1, 0);
                if (count > 0 && phicon[0] != IntPtr.Zero)
                {
                    return CreateBitmapSourceFromHIcon(phicon[0]);
                }
            }
            catch { }
            return null;
        }

        private static BitmapSource? CreateBitmapSourceFromHIcon(IntPtr hIcon)
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
        /// Removes the window from Windows Alt+Tab switcher by setting WS_EX_TOOLWINDOW and clearing WS_EX_APPWINDOW.
        /// </summary>
        public static void HideFromAltTab(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
                NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowHelper] HideFromAltTab failed: {ex.Message}");
            }
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
