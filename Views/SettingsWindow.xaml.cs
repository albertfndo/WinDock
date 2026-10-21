using System;
using System.Windows;
using System.Windows.Media;
using WinDock.Models;
using WinDock.Native;
using WinDock.Services;

namespace WinDock.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private string _selectedTint = "#4B5563";

        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var s = _settingsService.CurrentSettings;
            ChkHideTaskbar.IsChecked = s.HideWindowsTaskbar;
            ChkReserveSpace.IsChecked = s.ReserveScreenSpace;
            ChkAutoHide.IsChecked = s.AutoHideDock;
            ChkEnableNotch.IsChecked = s.EnableDropNotch;

            _selectedTint = s.DockTint ?? "#4B5563";

            SliderGlassOpacity.Value = s.GlassOpacity;
            SliderBorderRadius.Value = s.BorderRadius;
            SliderMagnification.Value = s.MagnificationScale;
            SliderIconSize.Value = s.IconSize;

            UpdateGlassLabel(s.GlassOpacity);
            UpdateRadiusLabel(s.BorderRadius);
            UpdateMagnificationLabel(s.MagnificationScale);
            UpdateIconSizeLabel(s.IconSize);

            UpdatePreview();
        }

        private void OnGlassOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateGlassLabel(e.NewValue);
            UpdatePreview();
        }

        private void OnBorderRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateRadiusLabel(e.NewValue);
            UpdatePreview();
        }

        private void OnMagnificationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMagnificationLabel(e.NewValue);
        }

        private void OnIconSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateIconSizeLabel(e.NewValue);
        }

        private void UpdateGlassLabel(double val)
        {
            if (TxtGlassValue == null) return;

            if (val >= 0.95)
            {
                TxtGlassValue.Text = "100% Solid (Matte)";
            }
            else if (val <= 0.20)
            {
                TxtGlassValue.Text = "Ultra Frosted Glass";
            }
            else
            {
                int solidPct = (int)Math.Round(val * 100);
                TxtGlassValue.Text = $"{100 - solidPct}% Glass / {solidPct}% Solid";
            }
        }

        private void UpdateRadiusLabel(double val)
        {
            if (TxtRadiusValue == null) return;
            int r = (int)Math.Round(val);
            if (r == 0)
                TxtRadiusValue.Text = "0px (Square)";
            else if (r >= 26)
                TxtRadiusValue.Text = $"{r}px (Capsule / Pill)";
            else
                TxtRadiusValue.Text = $"{r}px (Rounded)";
        }

        private void UpdateMagnificationLabel(double val)
        {
            if (TxtMagValue != null)
            {
                TxtMagValue.Text = $"{val:F2}x";
            }
        }

        private void UpdateIconSizeLabel(double val)
        {
            if (TxtIconSizeValue != null)
            {
                TxtIconSizeValue.Text = $"{(int)val}px";
            }
        }

        private void UpdatePreview()
        {
            if (PreviewDock == null) return;

            double radius = SliderBorderRadius.Value;
            double glass = SliderGlassOpacity.Value;

            PreviewDock.CornerRadius = new CornerRadius(radius);

            Color baseColor = Color.FromRgb(75, 85, 99);
            try
            {
                var converted = ColorConverter.ConvertFromString(_selectedTint);
                if (converted is Color c) baseColor = c;
            }
            catch { }

            if (glass >= 0.95)
            {
                PreviewDock.Background = new SolidColorBrush(baseColor);
                PreviewDock.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            }
            else
            {
                byte alpha = (byte)Math.Clamp((int)(glass * 255), 25, 255);
                var col1 = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
                byte darkerAlpha = (byte)Math.Min(255, alpha + 20);
                var col2 = Color.FromArgb(darkerAlpha, (byte)(baseColor.R * 0.7), (byte)(baseColor.G * 0.7), (byte)(baseColor.B * 0.7));

                PreviewDock.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(col1, 0.0),
                        new GradientStop(col2, 1.0)
                    }
                };
                PreviewDock.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.35 + 25), 255, 255, 255));
            }
        }

        #region Tint Preset Handlers

        private void OnTintSlateClicked(object sender, RoutedEventArgs e)
        {
            _selectedTint = "#4B5563";
            UpdatePreview();
        }

        private void OnTintCharcoalClicked(object sender, RoutedEventArgs e)
        {
            _selectedTint = "#1F2937";
            UpdatePreview();
        }

        private void OnTintOledClicked(object sender, RoutedEventArgs e)
        {
            _selectedTint = "#0B0F17";
            UpdatePreview();
        }

        private void OnTintMidnightClicked(object sender, RoutedEventArgs e)
        {
            _selectedTint = "#0F172A";
            UpdatePreview();
        }

        #endregion

        private void OnEmergencyRestoreClicked(object sender, RoutedEventArgs e)
        {
            TaskbarController.ShowTaskbar();
            MessageBox.Show(
                "Windows Taskbar has been restored and made visible.",
                "Taskbar Restored",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            var current = _settingsService.CurrentSettings;
            current.HideWindowsTaskbar = ChkHideTaskbar.IsChecked == true;
            current.ReserveScreenSpace = ChkReserveSpace.IsChecked == true;
            current.AutoHideDock = ChkAutoHide.IsChecked == true;
            current.EnableDropNotch = ChkEnableNotch.IsChecked == true;

            current.GlassOpacity = Math.Round(SliderGlassOpacity.Value, 2);
            current.BorderRadius = Math.Round(SliderBorderRadius.Value, 0);
            current.DockTint = _selectedTint;
            current.MagnificationScale = Math.Round(SliderMagnification.Value, 2);
            current.IconSize = (int)SliderIconSize.Value;

            _settingsService.SaveSettings(current);
            DialogResult = true;
            Close();
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
