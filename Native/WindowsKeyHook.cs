using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinDock.Native
{
    /// <summary>
    /// Intercepts the Windows Key (VK_LWIN / VK_RWIN) using a low-level keyboard hook (WH_KEYBOARD_LL)
    /// and invokes the WinDock Start Menu without triggering the standard Windows Start Menu.
    /// Allows combinations like Win+D, Win+E, Win+R, Win+Tab to pass through to Windows normally.
    /// </summary>
    public class WindowsKeyHook : IDisposable
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private readonly NativeMethods.HookProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _winKeyPressed = false;
        private bool _comboKeyPressed = false;
        private bool _isDisposed = false;

        public event Action? WindowsKeyPressed;

        private const byte VK_MASK = 0xE8; // unassigned virtual key

        public WindowsKeyHook()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
        }

        private IntPtr SetHook(NativeMethods.HookProc proc)
        {
            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule?.ModuleName);
                return NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc, hMod, 0);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                int vkCode = Marshal.ReadInt32(lParam);

                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (!_winKeyPressed)
                        {
                            _winKeyPressed = true;
                            _comboKeyPressed = false;
                        }
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        if (_winKeyPressed)
                        {
                            _winKeyPressed = false;
                            if (!_comboKeyPressed)
                            {
                                // Clean standalone Windows key tap -> Trigger WinDock Start Menu.
                                WindowsKeyPressed?.Invoke();
                                // Swallow this key-up, then replay mask-key + Win key-up so Windows sees "Win+<key>"
                                // (no Start Menu) yet still releases Win. Dropping the key-up alone leaves Win stuck down.
                                NativeMethods.keybd_event(VK_MASK, 0, 0, 0);
                                NativeMethods.keybd_event(VK_MASK, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                                NativeMethods.keybd_event((byte)vkCode, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                                return (IntPtr)1;
                            }
                        }
                    }
                }
                else
                {
                    // A non-Win key was pressed while Win key was held (e.g. Win+D, Win+R, Win+E)
                    if (_winKeyPressed && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
                    {
                        _comboKeyPressed = true;
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                if (_hookId != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            }
        }
    }
}
