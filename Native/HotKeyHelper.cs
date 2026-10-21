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

        public void UnregisterAll()
        {
            for (int i = 9001; i <= _currentId; i++)
            {
                NativeMethods.UnregisterHotKey(_hwnd, i);
            }
        }

        public static (uint modifiers, uint key) ParseHotKey(string hotkeyStr)
        {
            uint mods = 0;
            uint key = (uint)System.Windows.Forms.Keys.Space;

            if (string.IsNullOrWhiteSpace(hotkeyStr))
                return (NativeMethods.MOD_ALT, (uint)System.Windows.Forms.Keys.Space);

            var parts = hotkeyStr.Split(new[] { "+", " " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    mods |= NativeMethods.MOD_ALT;
                else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    mods |= NativeMethods.MOD_CONTROL;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    mods |= NativeMethods.MOD_SHIFT;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    mods |= NativeMethods.MOD_WIN;
                else
                {
                    if (Enum.TryParse<System.Windows.Forms.Keys>(part, true, out var parsedKey))
                    {
                        key = (uint)parsedKey;
                    }
                }
            }

            return (mods, key);
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
