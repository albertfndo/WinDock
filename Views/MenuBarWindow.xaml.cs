using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WinDock.Native;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using BatteryChargeStatus = System.Windows.Forms.BatteryChargeStatus;
using SystemInformation = System.Windows.Forms.SystemInformation;

namespace WinDock.Views
{
    /// <summary>
    /// macOS-style top menu bar: Launchpad, frontmost app name, and the status items the hidden taskbar used to show.
    /// </summary>
    public partial class MenuBarWindow : Window
    {
        public const double BarHeight = 28;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Dictionary<string, string> _appNameCache = new(StringComparer.OrdinalIgnoreCase);
        private IntPtr _hwnd;
        private static readonly string OwnProcessName = Process.GetCurrentProcess().ProcessName;

        public event Action? RequestLaunchpad;
        public event Action? RequestToggleNotch;

        public MenuBarWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                WindowHelper.HideFromAltTab(_hwnd);
                PositionAtTop();
                Refresh();
                _timer.Start();
            };
            _timer.Tick += (s, e) => Refresh();
            Closed += (s, e) => _timer.Stop();
        }

        public void PositionAtTop()
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
        }

        private void Refresh()
        {
            ClockText.Text = DateTime.Now.ToString("ddd d MMM   HH:mm");

            var (isConnected, isWifi, statusText) = NetworkHelper.GetStatus();
            NetworkItem.ToolTip = statusText;
            PathWifi.Visibility = isConnected && isWifi ? Visibility.Visible : Visibility.Collapsed;
            PathEthernet.Visibility = isConnected && !isWifi ? Visibility.Visible : Visibility.Collapsed;
            PathOffline.Visibility = isConnected ? Visibility.Collapsed : Visibility.Visible;

            var (vol, isMuted) = AudioHelper.GetMasterVolume();
            VolumeItem.ToolTip = isMuted ? "Muted (scroll to adjust)" : $"Volume {vol}% (scroll to adjust)";
            PathVolumeNormal.Visibility = isMuted ? Visibility.Collapsed : Visibility.Visible;
            PathVolumeMuted.Visibility = isMuted ? Visibility.Visible : Visibility.Collapsed;

            var power = SystemInformation.PowerStatus;
            bool hasBattery = (power.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0
                              && power.BatteryChargeStatus != BatteryChargeStatus.Unknown;
            BatteryItem.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
            if (hasBattery)
            {
                float pct = Math.Clamp(power.BatteryLifePercent, 0f, 1f);
                bool charging = power.PowerLineStatus == PowerLineStatus.Online;
                BatteryText.Text = $"{pct * 100:0}%";
                BatteryFill.Width = Math.Max(1.5, 18 * pct);
                BatteryFill.Background = pct <= 0.2 && !charging
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x45, 0x3A))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF2, 255, 255, 255));
                ChargingGlyph.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
                BatteryItem.ToolTip = charging ? $"Battery {pct * 100:0}%, charging" : $"Battery {pct * 100:0}%";
            }

            UpdateActiveApp();
        }

        private void UpdateActiveApp()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;

            var (procName, exePath) = WindowHelper.GetProcessInfo(fg);
            if (string.IsNullOrEmpty(procName)) return;

            // Our own windows (dock, Launchpad, Spotlight) keep the previous app name, like macOS
            if (procName.Equals(OwnProcessName, StringComparison.OrdinalIgnoreCase)) return;

            string key = exePath ?? procName;
            if (!_appNameCache.TryGetValue(key, out var name))
            {
                name = procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "File Explorer" : procName;
                try
                {
                    string? description = exePath != null ? FileVersionInfo.GetVersionInfo(exePath).FileDescription : null;
                    if (!string.IsNullOrWhiteSpace(description) && name != "File Explorer") name = description.Trim();
                }
                catch { }
                _appNameCache[key] = name;
            }
            ActiveAppText.Text = name;
        }

        private void OnLaunchpadClicked(object sender, RoutedEventArgs e) => RequestLaunchpad?.Invoke();

        private void OnNotchClicked(object sender, RoutedEventArgs e) => RequestToggleNotch?.Invoke();

        private void OnQuickSettingsClicked(object sender, RoutedEventArgs e) => OpenUri("ms-actioncenter:controlcenter/&");

        private void OnClockClicked(object sender, RoutedEventArgs e) => OpenUri("ms-actioncenter:");

        private void OnVolumeMouseWheel(object sender, MouseWheelEventArgs e)
        {
            AudioHelper.VolumeStep(e.Delta > 0);
            Refresh();
            e.Handled = true;
        }

        private static void OpenUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
            }
            catch { }
        }
    }
}
