using System;
using System.Windows;
using System.Windows.Media;
using WinDock.Models;
using WinDock.Services;

namespace WinDock.Views
{
    public partial class SetupWizardWindow : Window
    {
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private bool _isLoaded = false;
        private int _currentStep = 1;

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
            _currentStep = 2;
            Page1.Visibility = Visibility.Collapsed;
            Page2.Visibility = Visibility.Visible;

            BtnBack.Visibility = Visibility.Visible;
            BtnNext.Visibility = Visibility.Collapsed;
            BtnFinish.Visibility = Visibility.Visible;
            UpdatePreview();
        }

        private void OnBackClicked(object sender, RoutedEventArgs e)
        {
            _currentStep = 1;
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

            Color baseColor = Color.FromRgb(75, 85, 99);

            if (glass >= 0.95)
            {
                WizardPreviewDock.Background = new SolidColorBrush(baseColor);
                WizardPreviewDock.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            }
            else
            {
                byte alpha = (byte)Math.Clamp((int)(glass * 255), 25, 255);
                var col1 = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
                byte darkerAlpha = (byte)Math.Min(255, alpha + 20);
                var col2 = Color.FromArgb(darkerAlpha, (byte)(baseColor.R * 0.7), (byte)(baseColor.G * 0.7), (byte)(baseColor.B * 0.7));

                WizardPreviewDock.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(col1, 0.0),
                        new GradientStop(col2, 1.0)
                    }
                };
                WizardPreviewDock.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.35 + 25), 255, 255, 255));
            }
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
