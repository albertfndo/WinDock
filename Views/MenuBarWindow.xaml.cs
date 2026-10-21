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
        private AppBarHelper? _appBar;
        private static readonly string OwnProcessName = Process.GetCurrentProcess().ProcessName;

        public event Action? RequestLaunchpad;
        public event Action? RequestToggleNotch;
        public event Action? RequestReassertNotch;

        public MenuBarWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                WindowHelper.HideFromAltTab(_hwnd);
                PositionAtTop();
                ReserveSpace(IsVisible);
                Refresh();
                _timer.Start();
            };
            _timer.Tick += (s, e) => Refresh();
            IsVisibleChanged += (s, e) => ReserveSpace(IsVisible);
            Closed += (s, e) =>
            {
                _timer.Stop();
                _appBar?.Unregister(); // otherwise the strip stays reserved after WinDock exits
            };
        }

        public void PositionAtTop()
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
        }

        /// <summary>
        /// Registers the bar as a top AppBar so Explorer pushes maximized windows (Chrome tabs etc.) below it.
        /// A plain SPI_SETWORKAREA only affects windows maximized afterwards.
        /// </summary>
        private void ReserveSpace(bool reserve)
        {
            if (_hwnd == IntPtr.Zero) return;
            _appBar ??= new AppBarHelper(_hwnd);
            if (reserve)
            {
                _appBar.Register(NativeMethods.ABE_TOP, (int)Math.Ceiling(BarHeight * WorkAreaHelper.GetDpiScale()));
                PositionAtTop();
                RequestReassertNotch?.Invoke();
            }
            else
            {
                _appBar.Unregister();
            }
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
                    var info = exePath != null ? FileVersionInfo.GetVersionInfo(exePath) : null;
                    string? description = info?.FileDescription?.Trim();
                    // Packaged apps often carry ids like "WhatsApp.Root" as description; ProductName is the real name then
                    if (description != null && !description.Contains(' ') && description.Contains('.'))
                        description = info?.ProductName?.Trim();
                    if (!string.IsNullOrWhiteSpace(description) && name != "File Explorer") name = description;
                }
                catch { }
                _appNameCache[key] = name;
            }
            ActiveAppText.Text = name;
        }

        private void OnLaunchpadClicked(object sender, RoutedEventArgs e) => RequestLaunchpad?.Invoke();

        private void OnNotchClicked(object sender, RoutedEventArgs e) => RequestToggleNotch?.Invoke();

        private void OnShowDesktopClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    object? shell = Activator.CreateInstance(shellType);
                    shellType.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                    return;
                }
            }
            catch { }

            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, 0);
                NativeMethods.keybd_event(0x44, 0, 0, 0); // 'D'
                NativeMethods.keybd_event(0x44, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            }
            catch { }
        }

        private void OnNetworkClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Opens Windows Available Networks / Wi-Fi flyout
                Process.Start(new ProcessStartInfo { FileName = "ms-availablenetworks:", UseShellExecute = true });
            }
            catch
            {
                OpenUri("ms-settings:network");
            }
        }

        private void OnVolumeClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Opens Windows Sound Settings / Volume Mixer
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:sound", UseShellExecute = true });
            }
            catch
            {
                try { Process.Start("sndvol.exe"); } catch { }
            }
        }

        private void OnControlCenterClicked(object sender, RoutedEventArgs e) => ToggleQuickSettings();

        private void OnQuickSettingsClicked(object sender, RoutedEventArgs e) => ToggleQuickSettings();

        private void OnClockClicked(object sender, RoutedEventArgs e) => ToggleNotificationCenter();

        public static void ToggleQuickSettings()
        {
            try
            {
                // Win + A toggles Windows Quick Settings / Action Center on and off
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, 0);
                NativeMethods.keybd_event(0x41, 0, 0, 0); // 'A' keydown
                NativeMethods.keybd_event(0x41, 0, NativeMethods.KEYEVENTF_KEYUP, 0); // 'A' keyup
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, 0); // Win keyup
            }
            catch
            {
                OpenUri("ms-actioncenter:controlcenter/&");
            }
        }

        public static void ToggleNotificationCenter()
        {
            try
            {
                // Win + N toggles Windows Calendar & Notification Center on and off
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, 0);
                NativeMethods.keybd_event(0x4E, 0, 0, 0); // 'N' keydown
                NativeMethods.keybd_event(0x4E, 0, NativeMethods.KEYEVENTF_KEYUP, 0); // 'N' keyup
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, 0); // Win keyup
            }
            catch
            {
                OpenUri("ms-actioncenter:");
            }
        }

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
