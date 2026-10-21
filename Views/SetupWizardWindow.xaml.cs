using System;
using System.Windows;
using System.Windows.Media;
using WinDock.Models;
using WinDock.Native;
using WinDock.Services;

namespace WinDock.Views
{
    public partial class SetupWizardWindow : Window
    {
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private bool _isLoaded = false;
        
        public SetupWizardWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _isLoaded = true;
                LoadSettings();
                UpdatePreview();
            };
        }

        private void LoadSettings()
        {
            var s = _settingsService.CurrentSettings;
            RbFullReplacement.IsChecked = s.HideWindowsTaskbar;
            RbCompanion.IsChecked = !s.HideWindowsTaskbar;
            WizardChkNotch.IsChecked = s.EnableDropNotch;

            WizardSliderIconSize.Value = s.IconSize;
            WizardSliderGlass.Value = s.GlassOpacity;
            WizardSliderRadius.Value = s.BorderRadius;

            UpdateGlassLabel(s.GlassOpacity);
            UpdateRadiusLabel(s.BorderRadius);
            UpdateIconSizeLabel(s.IconSize);
        }

        private void OnNextClicked(object sender, RoutedEventArgs e)
        {
            Page1.Visibility = Visibility.Collapsed;
            Page2.Visibility = Visibility.Visible;

            BtnBack.Visibility = Visibility.Visible;
            BtnNext.Visibility = Visibility.Collapsed;
            BtnFinish.Visibility = Visibility.Visible;
            UpdatePreview();
        }

        private void OnBackClicked(object sender, RoutedEventArgs e)
        {
            Page2.Visibility = Visibility.Collapsed;
            Page1.Visibility = Visibility.Visible;

            BtnBack.Visibility = Visibility.Collapsed;
            BtnNext.Visibility = Visibility.Visible;
            BtnFinish.Visibility = Visibility.Collapsed;
        }

        private void OnWizardIconSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            UpdateIconSizeLabel(e.NewValue);
        }

        private void OnWizardGlassChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            UpdateGlassLabel(e.NewValue);
            UpdatePreview();
        }

        private void OnWizardRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            UpdateRadiusLabel(e.NewValue);
            UpdatePreview();
        }

        private void UpdateIconSizeLabel(double val)
        {
            if (WizardTxtIconSize != null)
            {
                WizardTxtIconSize.Text = $"{(int)val}px";
            }
        }

        private void UpdateGlassLabel(double val)
        {
            if (WizardTxtGlass == null) return;
            if (val >= 0.95)
                WizardTxtGlass.Text = "100% Solid (Matte)";
            else if (val <= 0.20)
                WizardTxtGlass.Text = "Ultra Frosted Glass";
            else
            {
                int solidPct = (int)Math.Round(val * 100);
                WizardTxtGlass.Text = $"{100 - solidPct}% Glass / {solidPct}% Solid";
            }
        }

        private void UpdateRadiusLabel(double val)
        {
            if (WizardTxtRadius == null) return;
            int r = (int)Math.Round(val);
            if (r == 0)
                WizardTxtRadius.Text = "0px (Square)";
            else if (r >= 26)
                WizardTxtRadius.Text = $"{r}px (Capsule / Pill)";
            else
                WizardTxtRadius.Text = $"{r}px (Rounded)";
        }

        private void UpdatePreview()
        {
            if (!_isLoaded || WizardPreviewDock == null || WizardSliderRadius == null || WizardSliderGlass == null) return;

            double radius = WizardSliderRadius.Value;
            double glass = WizardSliderGlass.Value;

            WizardPreviewDock.CornerRadius = new CornerRadius(radius);

            var tint = ThemeHelper.ParseColor(_settingsService.CurrentSettings.DockTint, Color.FromRgb(75, 85, 99));
            ThemeHelper.PaintDock(WizardPreviewDock, tint, glass);
        }

        private void OnFinishClicked(object sender, RoutedEventArgs e)
        {
            var s = _settingsService.CurrentSettings;
            s.HideWindowsTaskbar = (RbFullReplacement.IsChecked == true);
            s.ReserveScreenSpace = (RbFullReplacement.IsChecked == true);
            s.EnableDropNotch = (WizardChkNotch.IsChecked == true);

            s.IconSize = (int)WizardSliderIconSize.Value;
            s.GlassOpacity = Math.Round(WizardSliderGlass.Value, 2);
            s.BorderRadius = Math.Round(WizardSliderRadius.Value, 0);

            _settingsService.SaveSettings(s);
            DialogResult = true;
            Close();
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
