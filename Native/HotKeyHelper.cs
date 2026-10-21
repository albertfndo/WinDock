using System;
using System.Windows.Interop;

namespace WinDock.Native
{
    public class HotKeyHelper : IDisposable
    {
        private readonly IntPtr _hwnd;
        private HwndSource? _source;
        private int _currentId = 9000;
        private bool _isDisposed;

        public event Action<int>? HotKeyPressed;

        public HotKeyHelper(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(HwndHook);
        }

        public int Register(uint modifiers, uint key)
        {
            int id = ++_currentId;
            bool success = NativeMethods.RegisterHotKey(_hwnd, id, modifiers | NativeMethods.MOD_NOREPEAT, key);
            return success ? id : -1;
        }

        public void Unregister(int id)
        {
            if (id > 0)
            {
                NativeMethods.UnregisterHotKey(_hwnd, id);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                HotKeyPressed?.Invoke(id);
                handled = true;
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _source?.RemoveHook(HwndHook);
            }
        }
    }
}
