# WinDock Emergency Taskbar Restorer PowerShell Script
# Unhides all primary and secondary Windows taskbars

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class TaskbarRestorer {
    [DllImport("user32.dll")]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$SW_SHOW = 5

# Restore Primary Taskbar
$primary = [TaskbarRestorer]::FindWindow("Shell_TrayWnd", $null)
if ($primary -ne [IntPtr]::Zero) {
    [TaskbarRestorer]::ShowWindow($primary, $SW_SHOW)
    Write-Host "[✓] Restored Primary Taskbar" -ForegroundColor Green
} else {
    Write-Host "[!] Primary taskbar handle not found" -ForegroundColor Yellow
}

# Restore Secondary Taskbars (Multi-monitor setups)
$secondary = [IntPtr]::Zero
$count = 0
while ($true) {
    $secondary = [TaskbarRestorer]::FindWindowEx([IntPtr]::Zero, $secondary, "Shell_SecondaryTrayWnd", $null)
    if ($secondary -eq [IntPtr]::Zero) { break }
    [TaskbarRestorer]::ShowWindow($secondary, $SW_SHOW)
    $count++
}

if ($count -gt 0) {
    Write-Host "[✓] Restored $count Secondary Taskbar(s)" -ForegroundColor Green
}

Write-Host "Taskbars successfully restored." -ForegroundColor Cyan
