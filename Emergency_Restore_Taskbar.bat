@echo off
title WinDock - Emergency Taskbar Restorer
echo ========================================================
echo   WinDock Emergency Taskbar Restorer
echo ========================================================
echo.
echo Restoring Windows Taskbars...

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$code = @'
  [DllImport(\"user32.dll\")]
  public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
  [DllImport(\"user32.dll\")]
  public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string lpClassName, string lpWindowName);
  [DllImport(\"user32.dll\")]
  public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'@;
  $type = Add-Type -MemberDefinition $code -Name Win32Tray -Namespace WinDockHelper -PassThru;
  $p = $type::FindWindow('Shell_TrayWnd', $null);
  if ($p -ne [IntPtr]::Zero) { $type::ShowWindow($p, 5); }
  $s = [IntPtr]::Zero;
  while ($true) {
    $s = $type::FindWindowEx([IntPtr]::Zero, $s, 'Shell_SecondaryTrayWnd', $null);
    if ($s -eq [IntPtr]::Zero) { break; }
    $type::ShowWindow($s, 5);
  }
  Write-Host 'Taskbars made visible successfully.' -ForegroundColor Green;"

echo.
echo If the taskbar is still not responding, would you like to restart Explorer? (Y/N)
set /p choice="Enter choice: "
if /i "%choice%"=="Y" (
    taskkill /f /im explorer.exe
    start explorer.exe
    echo Explorer restarted.
)
echo Done!
pause
