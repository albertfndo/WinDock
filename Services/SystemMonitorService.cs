using System;
using System.Windows.Threading;
using WinDock.Native;

namespace WinDock.Services
{
    /// <summary>
    /// Snapshot of real-time hardware usage and power statistics.
    /// </summary>
    public class SystemStats
    {
        public double CpuUsagePercentage { get; set; }
        public double RamUsagePercentage { get; set; }
        public double RamUsedGB { get; set; }
        public double RamTotalGB { get; set; }
        public double BatteryPercentage { get; set; }
        public bool IsBatteryCharging { get; set; }
        public bool HasBattery { get; set; }

        public string CpuFormatted => $"{Math.Round(CpuUsagePercentage, 0)}%";
        public string RamFormatted => $"{Math.Round(RamUsagePercentage, 0)}%";
        public string RamDetailFormatted => $"{RamUsedGB:F1} / {RamTotalGB:F1} GB";
        public string BatteryFormatted => $"{Math.Round(BatteryPercentage, 0)}%";
    }

    /// <summary>
    /// Fast, non-blocking hardware monitor service computing CPU, RAM, and Battery statistics
    /// via lightweight Win32 APIs (GetSystemTimes, GlobalMemoryStatusEx) without PerformanceCounter latency.
    /// </summary>
    public class SystemMonitorService
    {
        private static SystemMonitorService? _instance;
        public static SystemMonitorService Instance => _instance ??= new SystemMonitorService();

        private readonly DispatcherTimer _timer;
        private ulong _prevIdleTime;
        private ulong _prevKernelTime;
        private ulong _prevUserTime;
        private bool _isFirstSample = true;

        public event Action<SystemStats>? StatsUpdated;

        public SystemStats CurrentStats { get; private set; } = new();

        public bool IsRunning => _timer.IsEnabled;

        public SystemMonitorService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _timer.Tick += (s, e) => UpdateStats();
        }

        public void Start()
        {
            if (_timer.IsEnabled) return;

            // Take initial CPU baseline snapshot
            TakeBaselineCpuSample();

            // Populate RAM and Battery immediately so UI is populated without delay
            UpdateStats();

            _timer.Start();
        }

        public void Stop()
        {
            if (!_timer.IsEnabled) return;

            _timer.Stop();
            _isFirstSample = true;
        }

        public void UpdateStats()
        {
            try
            {
                var stats = new SystemStats();

                // 1. Calculate CPU usage via GetSystemTimes diff
                stats.CpuUsagePercentage = CalculateCpuUsage();

                // 2. Calculate RAM usage via GlobalMemoryStatusEx
                var memStatus = new NativeMethods.MEMORYSTATUSEX();
                if (NativeMethods.GlobalMemoryStatusEx(memStatus))
                {
                    stats.RamUsagePercentage = memStatus.dwMemoryLoad;
                    stats.RamTotalGB = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    stats.RamUsedGB = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
                }

                // 3. Calculate Battery status via SystemInformation.PowerStatus
                try
                {
                    var power = System.Windows.Forms.SystemInformation.PowerStatus;
                    float lifePercent = power.BatteryLifePercent;
                    bool hasBattery = power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery
                                      && lifePercent >= 0.0f
                                      && lifePercent <= 1.0f;

                    stats.HasBattery = hasBattery;
                    stats.BatteryPercentage = hasBattery
                        ? Math.Round(lifePercent * 100.0, 1)
                        : 100.0;
                    stats.IsBatteryCharging = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SystemMonitorService] Battery read error: {ex.Message}");
                    stats.HasBattery = false;
                    stats.BatteryPercentage = 100.0;
                    stats.IsBatteryCharging = true;
                }

                CurrentStats = stats;
                StatsUpdated?.Invoke(stats);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SystemMonitorService] UpdateStats error: {ex.Message}");
            }
        }

        private void TakeBaselineCpuSample()
        {
            if (NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                _prevIdleTime = FileTimeToUInt64(idleTime);
                _prevKernelTime = FileTimeToUInt64(kernelTime);
                _prevUserTime = FileTimeToUInt64(userTime);
                _isFirstSample = false;
            }
        }

        private double CalculateCpuUsage()
        {
            if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                return 0.0;
            }

            ulong currentIdle = FileTimeToUInt64(idleTime);
            ulong currentKernel = FileTimeToUInt64(kernelTime);
            ulong currentUser = FileTimeToUInt64(userTime);

            if (_isFirstSample)
            {
                _prevIdleTime = currentIdle;
                _prevKernelTime = currentKernel;
                _prevUserTime = currentUser;
                _isFirstSample = false;
                return 0.0;
            }

            ulong deltaIdle = currentIdle >= _prevIdleTime ? currentIdle - _prevIdleTime : 0;
            ulong deltaKernel = currentKernel >= _prevKernelTime ? currentKernel - _prevKernelTime : 0;
            ulong deltaUser = currentUser >= _prevUserTime ? currentUser - _prevUserTime : 0;

            _prevIdleTime = currentIdle;
            _prevKernelTime = currentKernel;
            _prevUserTime = currentUser;

            // In Win32 GetSystemTimes, kernelTime includes idleTime.
            // Total system time = deltaKernel + deltaUser.
            ulong totalSystem = deltaKernel + deltaUser;
            if (totalSystem == 0)
            {
                return 0.0;
            }

            ulong deltaBusy = totalSystem > deltaIdle ? totalSystem - deltaIdle : 0;
            double cpuPercent = (double)deltaBusy / totalSystem * 100.0;

            return Math.Clamp(Math.Round(cpuPercent, 1), 0.0, 100.0);
        }

        private static ulong FileTimeToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        }
    }
}
