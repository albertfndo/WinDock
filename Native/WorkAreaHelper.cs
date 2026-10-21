using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinDock.Native
{
    /// <summary>
    /// Manages Windows desktop work area (the area available to maximized applications)
    /// to ensure windows can maximize completely to full screen without cut-offs or gaps.
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
        /// Sets a custom work area reserving space only at the bottom for the dock.
        /// </summary>
        public static void SetReservedWorkArea(int reservedBottomHeight)
        {
            Initialize();

            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var customRect = new NativeMethods.RECT
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Right = screen.Bounds.Right,
                Bottom = screen.Bounds.Bottom - reservedBottomHeight
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
