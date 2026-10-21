using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace WinDock.Native
{
    /// <summary>
    /// Registers the dock window as a Windows Application Desktop Toolbar (AppBar)
    /// to reserve desktop work area so maximized windows do not overlap it.
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

                uint ret = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
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
                Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var bounds = primaryScreen.Bounds;

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

                // Recalculate based on requested edge
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
    }
}
