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

        private bool _isLoaded = false;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WindowHelper.HideFromAltTab(hwnd);
                _isLoaded = true;
                UpdatePreview();
            };
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var s = _settingsService.CurrentSettings;
            ChkLaunchAtStartup.IsChecked = s.LaunchAtStartup;
            ChkHideTaskbar.IsChecked = s.HideWindowsTaskbar;
            ChkReserveSpace.IsChecked = s.ReserveScreenSpace;
            ChkAutoHide.IsChecked = s.AutoHideDock;
            ChkEnableNotch.IsChecked = s.EnableDropNotch;
            ChkAutoTheme.IsChecked = s.AutoSystemTheme;

            SelectComboByTag(ComboSpotlightKey, s.SpotlightHotKey ?? "Alt+Space");
            SelectComboByTag(ComboTaskbarKey, s.TaskbarHotKey ?? "Win+Alt+D");

            _selectedTint = s.DockTint ?? "#4B5563";

            SliderGlassOpacity.Value = s.GlassOpacity;
            SliderBlurIntensity.Value = s.BlurIntensity;
            SliderBorderRadius.Value = s.BorderRadius;
            SliderMagnification.Value = s.MagnificationScale;
            SliderIconSize.Value = s.IconSize;

            UpdateGlassLabel(s.GlassOpacity);
            UpdateBlurLabel(s.BlurIntensity);
            UpdateRadiusLabel(s.BorderRadius);
            UpdateMagnificationLabel(s.MagnificationScale);
            UpdateIconSizeLabel(s.IconSize);

            UpdatePreview();
        }

        private void OnGlassOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            UpdateGlassLabel(e.NewValue);
            UpdatePreview();

            var current = _settingsService.CurrentSettings;
            current.GlassOpacity = Math.Round(e.NewValue, 2);
            _settingsService.SaveSettings(current);
        }

        private void OnBlurIntensityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            UpdateBlurLabel(e.NewValue);
            UpdatePreview();

            var current = _settingsService.CurrentSettings;
            current.BlurIntensity = Math.Round(e.NewValue, 2);
            _settingsService.SaveSettings(current);
        }

        private void OnBorderRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            UpdateRadiusLabel(e.NewValue);
            UpdatePreview();

            var current = _settingsService.CurrentSettings;
            current.BorderRadius = Math.Round(e.NewValue, 0);
            _settingsService.SaveSettings(current);
        }

        private void OnMagnificationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            UpdateMagnificationLabel(e.NewValue);
        }

        private void OnIconSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
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

        private void UpdateBlurLabel(double val)
        {
            if (TxtBlurValue == null) return;
            if (val <= 0.05)
                TxtBlurValue.Text = "Blur Disabled (Flat)";
            else if (val >= 0.90)
                TxtBlurValue.Text = "Maximum Frosted Blur (100%)";
            else
                TxtBlurValue.Text = $"{(int)Math.Round(val * 100)}% Frosted Blur";
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
            if (!_isLoaded || PreviewDock == null || SliderBorderRadius == null || SliderGlassOpacity == null) return;

            double radius = SliderBorderRadius.Value;
            double glass = SliderGlassOpacity.Value;
            double blur = SliderBlurIntensity != null ? SliderBlurIntensity.Value : 0.85;

            PreviewDock.CornerRadius = new CornerRadius(radius);

            ThemeHelper.PaintDock(PreviewDock, ThemeHelper.ParseColor(_selectedTint, Color.FromRgb(75, 85, 99)), glass, blur);
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

        private void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tag)
        {
            foreach (var item in combo.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private string GetSelectedTag(System.Windows.Controls.ComboBox combo, string defaultVal)
        {
            if (combo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag != null)
            {
                return cbi.Tag.ToString() ?? defaultVal;
            }
            return defaultVal;
        }

        private void OnOpenShortcutsGuideClicked(object sender, RoutedEventArgs e)
        {
            var help = new HelpWindow();
            help.Owner = this;
            help.ShowDialog();
        }

        private void OnRunSetupWizardClicked(object sender, RoutedEventArgs e)
        {
            var wizard = new SetupWizardWindow();
            wizard.Owner = this;
            if (wizard.ShowDialog() == true)
            {
                LoadCurrentSettings();
            }
        }

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
            current.LaunchAtStartup = ChkLaunchAtStartup.IsChecked == true;
            StartupService.SetStartup(current.LaunchAtStartup);
            current.HideWindowsTaskbar = ChkHideTaskbar.IsChecked == true;
            current.ReserveScreenSpace = ChkReserveSpace.IsChecked == true;
            current.AutoHideDock = ChkAutoHide.IsChecked == true;
            current.EnableDropNotch = ChkEnableNotch.IsChecked == true;
            current.AutoSystemTheme = ChkAutoTheme.IsChecked == true;

            current.SpotlightHotKey = GetSelectedTag(ComboSpotlightKey, "Alt+Space");
            current.TaskbarHotKey = GetSelectedTag(ComboTaskbarKey, "Win+Alt+D");

            current.GlassOpacity = Math.Round(SliderGlassOpacity.Value, 2);
            if (SliderBlurIntensity != null)
            {
                current.BlurIntensity = Math.Round(SliderBlurIntensity.Value, 2);
            }
            current.BorderRadius = Math.Round(SliderBorderRadius.Value, 0);
            current.DockTint = _selectedTint;
            current.MagnificationScale = Math.Round(SliderMagnification.Value, 2);
            current.IconSize = (int)SliderIconSize.Value;

            _settingsService.SaveSettings(current);

            // Give sleek live confirmation without abruptly dismissing the window
            if (TxtSaveStatus != null)
            {
                TxtSaveStatus.Visibility = Visibility.Visible;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (s, ev) =>
                {
                    TxtSaveStatus.Visibility = Visibility.Collapsed;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnExitWinDockClicked(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Are you sure you want to exit WinDock? The Windows taskbar will be restored.",
                "Exit WinDock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ExitApplication();
                }
                else
                {
                    Application.Current.Shutdown();
                }
            }
        }
    }
}
