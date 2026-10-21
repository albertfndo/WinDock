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
        public static void PaintDock(Border border, Color baseColor, double glass, double blurIntensity = 0.85)
        {
            if (glass >= 0.95)
            {
                border.Background = new SolidColorBrush(baseColor);
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                return;
            }

            // Live responsive glass opacity from 0.05 (sheer glass) to 1.0 (solid matte)
            double effectiveGlass = Math.Clamp(glass, 0.05, 1.0);
            byte topAlpha = (byte)Math.Clamp((int)(effectiveGlass * 240), 25, 245);
            byte bottomAlpha = (byte)Math.Clamp((int)(topAlpha + 25), 45, 255);

            // Specular diffusion & luminous sheen controlled directly by BlurIntensity slider
            int highlight = (int)(32 * blurIntensity);
            var top = Color.FromArgb(
                topAlpha,
                (byte)Math.Min(255, baseColor.R + highlight),
                (byte)Math.Min(255, baseColor.G + highlight),
                (byte)Math.Min(255, baseColor.B + highlight + 5));

            var bottom = Color.FromArgb(
                bottomAlpha,
                (byte)(baseColor.R * 0.70),
                (byte)(baseColor.G * 0.70),
                (byte)(baseColor.B * 0.75));

            border.Background = new LinearGradientBrush(top, bottom, 90);

            // Specular border intensity scales dynamically with blurIntensity slider
            byte borderTopAlpha = (byte)Math.Clamp((int)(0x25 + 0x55 * blurIntensity), 0x20, 0x95);
            byte borderBottomAlpha = (byte)Math.Clamp((int)(0x10 + 0x25 * blurIntensity), 0x0C, 0x40);
            border.BorderThickness = new Thickness(1);
            border.BorderBrush = new LinearGradientBrush(
                Color.FromArgb(borderTopAlpha, 255, 255, 255),
                Color.FromArgb(borderBottomAlpha, 255, 255, 255),
                90);
        }

        /// <summary>
        /// Disables rectangular DWM composition blur (ACCENT_DISABLED) to eliminate square blur bleed and corners.
        /// Genuine anti-aliased rounded frosted glass is rendered natively in WPF vector layers with dynamic specular sheen.
        /// </summary>
        public static void ApplyWindowBlur(IntPtr hwnd, Color baseColor, double blurIntensity)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                var policy = new NativeMethods.AccentPolicy
                {
                    AccentState = NativeMethods.AccentState.ACCENT_DISABLED,
                    AccentFlags = 0,
                    GradientColor = 0,
                    AnimationId = 0
                };

                SetAccentPolicy(hwnd, policy);
                NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
            }
            catch { }
        }

        /// <summary>
        /// Reset region when DWM blur is disabled, allowing WPF subpixel anti-aliasing to render smooth rounded edges.
        /// </summary>
        public static void UpdateDockWindowRegion(IntPtr hwnd, FrameworkElement? dockBar = null, double borderRadius = 28)
        {
            if (hwnd != IntPtr.Zero)
            {
                try { NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true); } catch { }
            }
        }

        private static void SetAccentPolicy(IntPtr hwnd, NativeMethods.AccentPolicy policy)
        {
            int accentSize = System.Runtime.InteropServices.Marshal.SizeOf(policy);
            IntPtr accentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(accentSize);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(policy, accentPtr, false);
                var data = new NativeMethods.WindowCompositionAttributeData
                {
                    Attribute = NativeMethods.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentSize
                };
                NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(accentPtr);
            }
        }
    }
}
