using System;
using System.Collections.Generic;

namespace WinDock.Native
{
    /// <summary>
    /// Controls Windows taskbar visibility and autohide state across primary and secondary monitors.
    /// </summary>
    public static class TaskbarController
    {
        private static bool _isTaskbarHidden = false;

        public static bool IsTaskbarHidden => _isTaskbarHidden;

        /// <summary>
        /// Retrieves the HWND of the primary taskbar.
        /// </summary>
        public static IntPtr GetPrimaryTaskbarHandle()
        {
            return NativeMethods.FindWindow("Shell_TrayWnd", null);
        }

        /// <summary>
        /// Retrieves the HWNDs of all secondary taskbars on multi-monitor systems.
        /// </summary>
        public static List<IntPtr> GetSecondaryTaskbarHandles()
        {
            var list = new List<IntPtr>();
            IntPtr current = IntPtr.Zero;

            while (true)
            {
                current = NativeMethods.FindWindowEx(IntPtr.Zero, current, "Shell_SecondaryTrayWnd", null);
                if (current == IntPtr.Zero)
                    break;
                list.Add(current);
            }

            return list;
        }

        /// <summary>
        /// Hides the Windows taskbar on all monitors.
        /// </summary>
        public static void HideTaskbar()
        {
            try
            {
                IntPtr primary = GetPrimaryTaskbarHandle();
                if (primary != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(primary, NativeMethods.SW_HIDE);
                }

                foreach (IntPtr secondary in GetSecondaryTaskbarHandles())
                {
                    NativeMethods.ShowWindow(secondary, NativeMethods.SW_HIDE);
                }

                // Windows 7/8/early 10 standalone start button if present
                IntPtr startBtn = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Button", "Start");
                if (startBtn != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(startBtn, NativeMethods.SW_HIDE);
                }

                _isTaskbarHidden = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskbarController] Error hiding taskbar: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores and shows the Windows taskbar on all monitors.
        /// </summary>
        public static void ShowTaskbar()
        {
            try
            {
                IntPtr primary = GetPrimaryTaskbarHandle();
                if (primary != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(primary, NativeMethods.SW_SHOW);
                }

                foreach (IntPtr secondary in GetSecondaryTaskbarHandles())
                {
                    NativeMethods.ShowWindow(secondary, NativeMethods.SW_SHOW);
                }

                IntPtr startBtn = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Button", "Start");
                if (startBtn != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(startBtn, NativeMethods.SW_SHOW);
                }

                _isTaskbarHidden = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskbarController] Error showing taskbar: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles taskbar visibility.
        /// </summary>
        public static void ToggleTaskbar()
        {
            if (_isTaskbarHidden)
            {
                ShowTaskbar();
            }
            else
            {
                HideTaskbar();
            }
        }

        /// <summary>
        /// Sets taskbar autohide mode via SHAppBarMessage.
        /// </summary>
        public static void SetTaskbarAutoHide(bool enable)
        {
            try
            {
                IntPtr primary = GetPrimaryTaskbarHandle();
                if (primary == IntPtr.Zero) return;

                var data = new NativeMethods.APPBARDATA
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = primary,
                    lParam = (IntPtr)(enable ? NativeMethods.ABS_AUTOHIDE : NativeMethods.ABS_ALWAYSONTOP)
                };

                NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskbarController] Error setting autohide: {ex.Message}");
            }
        }

        /// <summary>
        /// Triggers the Windows Start Menu by simulating the Windows key.
        /// </summary>
        public static void OpenStartMenu()
        {
            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, 0);
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskbarController] Error opening start menu: {ex.Message}");
            }
        }
    }
}
