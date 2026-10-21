using System;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace WinDock.Native
{
    public static class NetworkHelper
    {
        public static (bool IsConnected, bool IsWifi, string StatusText) GetStatus()
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    return (false, false, "Network: Disconnected");
                }

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        bool isWifi = (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                        string typeStr = isWifi ? "Wi-Fi" : "Ethernet";
                        return (true, isWifi, $"Internet Connected ({typeStr})");
                    }
                }
            }
            catch { }

            return (false, false, "Network: Disconnected");
        }

        public static void OpenNetworkSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:network", UseShellExecute = true });
            }
            catch { }
        }
    }
}
