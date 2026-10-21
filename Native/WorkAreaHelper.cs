using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinDock.Native
{
    /// <summary>
    /// Manages Windows desktop work area (the area available to maximized applications)
    /// to ensure windows can maximize completely to full screen or stop exactly above the dock without overlapping.
    /// </summary>
    public static class WorkAreaHelper
    {
        private static NativeMethods.RECT _originalWorkArea;
        private static bool _hasSavedOriginal = false;

        /// <summary>
        /// Height (DIP) kept free at the top of the primary screen for the WinDock menu bar.
        /// </summary>
        public static double TopReservedDip { get; set; }

        private static int TopReservedPx => (int)Math.Ceiling(TopReservedDip * GetDpiScale());

        public static void Initialize()
        {
            if (!_hasSavedOriginal)
            {
                NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref _originalWorkArea, 0);
                _hasSavedOriginal = true;
            }
        }

        public static double GetTaskbarHeightDip()
        {
            Initialize();
            var bounds = GetPrimaryMonitorBounds();
            double dpi = GetDpiScale();
            int diff = bounds.Bottom - _originalWorkArea.Bottom;
            return diff > 0 ? diff / dpi : 0;
        }

        public static double GetDpiScale()
        {
            try
            {
                uint dpi = NativeMethods.GetDpiForSystem();
                if (dpi > 0) return dpi / 96.0;
            }
            catch { }

            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiY / 96.0;
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>
        /// Retrieves the exact physical pixel bounds of the primary monitor via Win32 GetMonitorInfo/GetSystemMetrics,
        /// avoiding WinForms DPI-virtualization inaccuracies that cause maximized windows to leave gaps at the bottom.
        /// </summary>
        public static NativeMethods.RECT GetPrimaryMonitorBounds()
        {
            try
            {
                IntPtr hMonitor = NativeMethods.MonitorFromWindow(IntPtr.Zero, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new NativeMethods.MONITORINFO
                    {
                        cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
                    };
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    {
                        return mi.rcMonitor;
                    }
                }
            }
            catch { }

            int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            if (width > 0 && height > 0)
            {
                return new NativeMethods.RECT { Left = 0, Top = 0, Right = width, Bottom = height };
            }

            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            return new NativeMethods.RECT
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Right = screen.Bounds.Right,
                Bottom = screen.Bounds.Bottom
            };
        }

        /// <summary>
        /// Expands the desktop work area to the FULL screen bounds.
        /// Maximized windows (Chrome, VS Code, Explorer, etc.) will fill the entire display without any cut-off!
        /// </summary>
        public static void SetFullScreenWorkArea()
        {
            Initialize();

            var bounds = GetPrimaryMonitorBounds();
            var fullRect = new NativeMethods.RECT
            {
                Left = bounds.Left,
                Top = bounds.Top + TopReservedPx,
                Right = bounds.Right,
                Bottom = bounds.Bottom
            };

            NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETWORKAREA,
                0,
                ref fullRect,
                NativeMethods.SPIF_SENDCHANGE | NativeMethods.SPIF_UPDATEINIFILE);

            RefreshMaximizedWindows();
        }

        /// <summary>
        /// Sets a custom work area reserving space only at the bottom for the dock,
        /// accurately calculated using DPI scaling so maximized windows never cut into the dock.
        /// </summary>
        public static void SetReservedWorkArea(double dockBarHeightDip, double marginDip = 8)
        {
            Initialize();

            var bounds = GetPrimaryMonitorBounds();
            double dpi = GetDpiScale();
            int physicalReservedHeight = (int)Math.Ceiling((dockBarHeightDip + marginDip) * dpi);

            var customRect = new NativeMethods.RECT
            {
                Left = bounds.Left,
                Top = bounds.Top + TopReservedPx,
                Right = bounds.Right,
                Bottom = bounds.Bottom - physicalReservedHeight
            };

            NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETWORKAREA,
                0,
                ref customRect,
                NativeMethods.SPIF_SENDCHANGE | NativeMethods.SPIF_UPDATEINIFILE);

            RefreshMaximizedWindows();
        }

        /// <summary>
        /// Restores the original Windows work area (with native taskbar spacing).
        /// </summary>
        public static void RestoreOriginalWorkArea()
        {
            if (_hasSavedOriginal)
            {
                NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_SETWORKAREA,
                    0,
                    ref _originalWorkArea,
                    NativeMethods.SPIF_SENDCHANGE | NativeMethods.SPIF_UPDATEINIFILE);

                RefreshMaximizedWindows();
            }
        }

        /// <summary>
        /// Forces all open maximized windows (browsers, editors) to instantly re-calculate their bounds
        /// and snap to the updated work area without requiring manual un-maximize/re-maximize.
        /// </summary>
        public static void RefreshMaximizedWindows()
        {
            try
            {
                NativeMethods.EnumWindows((hWnd, lParam) =>
                {
                    if (NativeMethods.IsWindowVisible(hWnd) && !NativeMethods.IsIconic(hWnd))
                    {
                        var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
                        if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
                        {
                            if (wp.showCmd == NativeMethods.SW_SHOWMAXIMIZED)
                            {
                                NativeMethods.SetWindowPos(
                                    hWnd,
                                    IntPtr.Zero,
                                    0, 0, 0, 0,
                                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
                                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }
    }
}
