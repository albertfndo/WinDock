using System;
using System.Collections.Generic;

namespace WinDock.Models
{
    public class PinnedAppConfig
    {
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string LaunchPath { get; set; } = string.Empty;
        public string? IconPath { get; set; }
    }

    public class DockSettings
    {
        public double IconSize { get; set; } = 52.0;
        public double MagnificationScale { get; set; } = 1.35;
        public bool AutoHideDock { get; set; } = false;
        public bool ReserveScreenSpace { get; set; } = false;
        public bool HideWindowsTaskbar { get; set; } = true;
        public bool EnableDropNotch { get; set; } = true;
        public double BorderRadius { get; set; } = 28.0; // 0px (sharp) to 40px (full capsule/pill)
        public double GlassOpacity { get; set; } = 0.65; // 1.0 = 100% Solid, 0.15 = Ultra Frosted Glass
        public string DockTint { get; set; } = "#4B5563"; // Elegant slate/gray background as seen in user screenshot
        public string Theme { get; set; } = "DarkGlass"; // DarkGlass, LightGlass, OLED, Cyberpunk
        public string AccentColor { get; set; } = "#3B82F6"; // Electric Blue
        public bool LaunchAtStartup { get; set; } = true;

        public List<PinnedAppConfig> PinnedApps { get; set; } = new()
        {
            new PinnedAppConfig
            {
                Title = "File Explorer",
                ProcessName = "explorer",
                LaunchPath = "explorer.exe"
            },
            new PinnedAppConfig
            {
                Title = "Microsoft Edge",
                ProcessName = "msedge",
                LaunchPath = "msedge.exe"
            },
            new PinnedAppConfig
            {
                Title = "Notepad",
                ProcessName = "notepad",
                LaunchPath = "notepad.exe"
            },
            new PinnedAppConfig
            {
                Title = "Task Manager",
                ProcessName = "Taskmgr",
                LaunchPath = "taskmgr.exe"
            }
        };
    }
}
