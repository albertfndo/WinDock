@echo off
echo ========================================================
echo   Building WinDock (.NET 8 WPF x64)
echo ========================================================
echo.

dotnet --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] .NET SDK is not found. Please install .NET 8 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo Restoring and Building...
dotnet build WinDock.csproj -c Release -r win-x64

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUCCESS] Build succeeded!
    echo Output executable: bin\Release\net8.0-windows\win-x64\WinDock.exe
) else (
    echo.
    echo [ERROR] Build failed. Review errors above.
)

pause
