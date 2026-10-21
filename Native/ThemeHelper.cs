using System;
using Microsoft.Win32;

namespace WinDock.Native
{
    public static class ThemeHelper
    {
        public static event Action? SystemThemeChanged;

        static ThemeHelper()
        {
            try
            {
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                    {
                        SystemThemeChanged?.Invoke();
                    }
                };
            }
            catch { }
        }

        /// <summary>
        /// Returns true if Windows is using Dark theme for apps, false if Light theme.
        /// </summary>
        public static bool IsWindowsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int intVal)
                {
                    return intVal == 0;
                }
            }
            catch { }
            return true; // Default to dark theme
        }
    }
}
