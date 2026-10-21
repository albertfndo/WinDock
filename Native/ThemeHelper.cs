using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        public static Color ParseColor(string? hex, Color fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(hex) && ColorConverter.ConvertFromString(hex) is Color c) return c;
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// True when a tint is light enough that dock glyphs/text must be dark to stay readable.
        /// </summary>
        public static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150;

        /// <summary>
        /// Paints the solid-to-glass dock material. Shared by the dock and the Settings/Wizard live previews.
        /// glass: 1.0 = solid, 0.1 = ultra frosted.
        /// </summary>
        public static void PaintDock(Border border, Color baseColor, double glass)
        {
            if (glass >= 0.95)
            {
                border.Background = new SolidColorBrush(baseColor);
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                return;
            }

            byte alpha = (byte)Math.Clamp((int)(glass * 255), 25, 255);
            var top = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
            var bottom = Color.FromArgb((byte)Math.Min(255, alpha + 20), (byte)(baseColor.R * 0.7), (byte)(baseColor.G * 0.7), (byte)(baseColor.B * 0.7));
            border.Background = new LinearGradientBrush(top, bottom, 90);
            border.BorderBrush = new LinearGradientBrush(
                Color.FromArgb((byte)Math.Min(255, alpha * 0.5 + 40), 255, 255, 255),
                Color.FromArgb((byte)Math.Min(255, alpha * 0.2 + 15), 255, 255, 255),
                90);
        }
    }
}
