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

        public static void Initialize()
        {
            if (!_hasSavedOriginal)
            {
                NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref _originalWorkArea, 0);
                _hasSavedOriginal = true;
            }
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
        /// Expands the desktop work area to the FULL screen bounds.
        /// Maximized windows (Chrome, VS Code, Explorer, etc.) will fill the entire display without any cut-off!
        /// </summary>
        public static void SetFullScreenWorkArea()
        {
            Initialize();

            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var fullRect = new NativeMethods.RECT
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Right = screen.Bounds.Right,
                Bottom = screen.Bounds.Bottom
            };

            NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETWORKAREA,
                0,
                ref fullRect,
                NativeMethods.SPIF_SENDCHANGE | NativeMethods.SPIF_UPDATEINIFILE);
        }

        /// <summary>
        /// Sets a custom work area reserving space only at the bottom for the dock,
        /// accurately calculated using DPI scaling so maximized windows never cut into the dock.
        /// </summary>
        public static void SetReservedWorkArea(double dockBarHeightDip, double marginDip = 8)
        {
            Initialize();

            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            double dpi = GetDpiScale();
            int physicalReservedHeight = (int)Math.Ceiling((dockBarHeightDip + marginDip) * dpi);

            var customRect = new NativeMethods.RECT
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Right = screen.Bounds.Right,
                Bottom = screen.Bounds.Bottom - physicalReservedHeight
            };

            NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETWORKAREA,
                0,
                ref customRect,
                NativeMethods.SPIF_SENDCHANGE | NativeMethods.SPIF_UPDATEINIFILE);
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
            }
        }
    }
}
