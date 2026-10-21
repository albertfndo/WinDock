using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinDock.Services
{
    public static class StartupService
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "WinDock";

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                if (key != null)
                {
                    string? val = key.GetValue(AppName) as string;
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupService] Error reading startup: {ex.Message}");
            }
            return false;
        }

        public static void SetStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null) return;

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    }

                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupService] Error setting startup: {ex.Message}");
            }
        }
    }
}
