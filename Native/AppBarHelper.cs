using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Forms;

namespace WinDock.Native
{
    /// <summary>
    /// Registers a window as a Windows Application Desktop Toolbar (AppBar)
    /// to reserve desktop work area so maximized windows do not overlap it.
    /// Used by MenuBarWindow (top edge) and MainWindow (bottom edge reservation).
    /// </summary>
    public class AppBarHelper
    {
        private readonly IntPtr _hwnd;
        private bool _isRegistered;
        private int _callbackMessage;

        public AppBarHelper(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public bool IsRegistered => _isRegistered;

        public void Register(int edge = NativeMethods.ABE_BOTTOM, int height = 80)
        {
            if (_isRegistered || _hwnd == IntPtr.Zero) return;

            try
            {
                _callbackMessage = NativeMethods.RegisterWindowMessage("WinDockAppBarMsg");

                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = _hwnd,
                    uCallbackMessage = _callbackMessage
                };

                NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
                _isRegistered = true;

                SetPosition(edge, height);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppBarHelper] Register failed: {ex.Message}");
            }
        }

        public void SetPosition(int edge, int height)
        {
            if (!_isRegistered || _hwnd == IntPtr.Zero) return;

            try
            {
                var bounds = WorkAreaHelper.GetPrimaryMonitorBounds();

                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = _hwnd,
                    uEdge = edge,
                    rc = new NativeMethods.RECT
                    {
                        Left = bounds.Left,
                        Right = bounds.Right,
                        Top = edge == NativeMethods.ABE_TOP ? bounds.Top : bounds.Bottom - height,
                        Bottom = edge == NativeMethods.ABE_TOP ? bounds.Top + height : bounds.Bottom
                    }
                };

                NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);

                switch (edge)
                {
                    case NativeMethods.ABE_BOTTOM:
                        abd.rc.Top = abd.rc.Bottom - height;
                        break;
                    case NativeMethods.ABE_TOP:
                        abd.rc.Bottom = abd.rc.Top + height;
                        break;
                }

                NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

                NativeMethods.SetWindowPos(
                    _hwnd,
                    IntPtr.Zero,
                    abd.rc.Left, abd.rc.Top,
                    abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppBarHelper] SetPosition failed: {ex.Message}");
            }
        }

        public void Unregister()
        {
            if (!_isRegistered || _hwnd == IntPtr.Zero) return;

            try
            {
                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = _hwnd
                };

                NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
                _isRegistered = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppBarHelper] Unregister failed: {ex.Message}");
            }
        }

        #region Global Static Reservation Window for Dock (Bottom)

        private static Window? _bottomReservationWindow;
        private static AppBarHelper? _bottomAppBar;

        public static void SetReservedSpace(double dockBarHeightDip, double marginDip = 8)
        {
            double dpi = WorkAreaHelper.GetDpiScale();
            int reservedHeightPx = (int)Math.Ceiling((dockBarHeightDip + marginDip) * dpi);
            var bounds = WorkAreaHelper.GetPrimaryMonitorBounds();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_bottomReservationWindow == null)
                {
                    _bottomReservationWindow = new Window
                    {
                        Title = "WinDockBottomAppBarReservation",
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = System.Windows.Media.Brushes.Transparent,
                        ShowInTaskbar = false,
                        Topmost = false,
                        ResizeMode = ResizeMode.NoResize,
                        Focusable = false,
                        ShowActivated = false
                    };

                    _bottomReservationWindow.SourceInitialized += (s, e) =>
                    {
                        var helper = new WindowInteropHelper(_bottomReservationWindow);
                        IntPtr hwnd = helper.Handle;

                        int exStyle = (int)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
                        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
                            (IntPtr)(exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));

                        _bottomAppBar = new AppBarHelper(hwnd);
                        _bottomAppBar.Register(NativeMethods.ABE_BOTTOM, reservedHeightPx);
                    };

                    _bottomReservationWindow.Left = bounds.Left / dpi;
                    _bottomReservationWindow.Top = (bounds.Bottom - reservedHeightPx) / dpi;
                    _bottomReservationWindow.Width = (bounds.Right - bounds.Left) / dpi;
                    _bottomReservationWindow.Height = reservedHeightPx / dpi;

                    _bottomReservationWindow.Show();
                }
                else
                {
                    _bottomAppBar?.SetPosition(NativeMethods.ABE_BOTTOM, reservedHeightPx);
                }
            });
        }

        public static void ClearReservedSpace()
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (_bottomReservationWindow != null)
                {
                    _bottomAppBar?.Unregister();
                    _bottomAppBar = null;
                    _bottomReservationWindow.Close();
                    _bottomReservationWindow = null;
                }
            });
        }

        #endregion
    }
}
