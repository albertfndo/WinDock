using System;
using System.Windows.Interop;

namespace WinDock.Native
{
    /// <summary>
    /// Listens to Windows Shell Hooks for real-time window creation, destruction, and activation events.
    /// </summary>
    public class ShellHookListener : IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly int _shellHookMsg;
        private HwndSource? _hwndSource;
        private bool _isDisposed;

        public event Action<IntPtr>? WindowCreated;
        public event Action<IntPtr>? WindowDestroyed;
        public event Action<IntPtr>? WindowActivated;
        public event Action<IntPtr>? WindowRedrawn;

        public ShellHookListener(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _shellHookMsg = NativeMethods.RegisterWindowMessage("SHELLHOOK");

            NativeMethods.RegisterShellHookWindow(_hwnd);

            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _shellHookMsg)
            {
                int code = wParam.ToInt32();
                IntPtr targetHwnd = lParam;

                switch (code)
                {
                    case NativeMethods.HSHELL_WINDOWCREATED:
                        WindowCreated?.Invoke(targetHwnd);
                        break;

                    case NativeMethods.HSHELL_WINDOWDESTROYED:
                        WindowDestroyed?.Invoke(targetHwnd);
                        break;

                    case NativeMethods.HSHELL_WINDOWACTIVATED:
                    case NativeMethods.HSHELL_ACTIVATESHELLWINDOW:
                        WindowActivated?.Invoke(targetHwnd);
                        break;

                    case NativeMethods.HSHELL_REDRAW:
                        WindowRedrawn?.Invoke(targetHwnd);
                        break;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _hwndSource?.RemoveHook(WndProc);
                if (_hwnd != IntPtr.Zero)
                {
                    NativeMethods.DeregisterShellHookWindow(_hwnd);
                }
            }
        }
    }
}
