# WinDock: Modern Floating Dock & Windows Taskbar Controller

**WinDock** is a modern, high-performance Windows desktop application built in **C# / WPF (.NET 8)** that transforms the Windows desktop experience. It can control or completely replace the native Windows taskbar (`Shell_TrayWnd`) with a sleek floating dock, and features a **Top Dynamic Drop Notch Shelf** for holding and moving files between apps.

---

## 🌟 Key Features

### 1. Solid-to-Glass & Border Radius Customizer (As Seen in Settings)
- **Solid ↔ Glass Slider**: Smoothly adjust the dock material from **100% Solid Matte** (slate/gray finish matching the modern squircle aesthetic) down to **Ultra Frosted Glass** (translucent acrylic with soft diffused border glow).
- **Border Radius Slider**: Customize corner rounding from **0px (sharp rectangular)** to **40px (pill / capsule)**.
- **Tint Presets**: Slate Gray, Dark Charcoal, OLED Black, and Midnight Blue.
- **Interactive Live Preview**: A real-time preview box directly in Preferences reflects your changes instantly.

### 2. Full Windows Taskbar Control & Replacement
- **Taskbar Hiding & Management**: Hides the native Windows 10/11 taskbar (`Shell_TrayWnd` and multi-monitor `Shell_SecondaryTrayWnd`) smoothly.
- **AppBar Screen Reservation**: Automatically reserves screen work area (`SHAppBarMessage`) so maximized windows don't overlap the dock.
- **Global Hotkey**: Press **`Win + Alt + D`** at any time to instantly toggle between WinDock and the native Windows Taskbar.
- **Start Menu Integration**: Built-in Start button triggers the native Windows Start Menu (`keybd_event(VK_LWIN)`).
- **Auto-Hide Dock**: Slides down when idle and reveals when your mouse touches the bottom edge of the screen.

### 3. Dynamic Drop Notch Shelf (Top Screen)
- **Top Notch Drop Zone**: Inspired by macOS Dynamic Island & Dropover/Yoink shelf.
- **Drag In**: Drag any files, folders, images, URLs, or text snippets to the top notch. The notch expands smoothly into a shelf and safely holds them.
- **Drag Out**: Once dropped, you can pick up items from the notch and drag them directly into Discord, Slack, email, browser, or File Explorer folders!
- **Pin from Shelf**: One-click "Pin to Dock" button on any dropped application.

### 4. Running Apps, Pinned Favorites & Notification Badges
- **Real-Time Shell Hook**: Listens to Windows `HSHELL_WINDOWCREATED`, `HSHELL_WINDOWDESTROYED`, and `HSHELL_WINDOWACTIVATED` for zero-polling instant window tracking.
- **Notification Badges**: Displays vibrant red circular badges (e.g. `6` on WhatsApp, `1` on Discord) for multi-window counts and notifications.
- **Active Window Indicator**: Cyan glowing dot under the currently focused application.
- **Hover Magnification**: Smooth zoom scaling on hover with customizable magnification factor.
- **Drag & Drop Pinning**: Drag any `.exe` or `.lnk` shortcut directly onto the dock to pin it.

### 5. MacBook Spotlight Wanna-Be Launcher (`Alt + Space`)
- **Universal Fast Search**: Summoned via **`Alt + Space`** or by clicking the 🔍 icon on the dock tray.
- **Application Search & Launch**: Instant index of all Start Menu apps and Windows utilities (Notepad, Terminal, Settings, Calc, etc.).
- **Running Window Switcher**: Search through open windows and bring any window directly to the front.
- **Instant Math & Calculator**: Type arithmetic expressions directly (e.g. `(45 * 12) + 20` -> shows `= 560`). Press `Enter` to copy to clipboard!
- **System Commands**: Type `lock`, `sleep`, `restart`, `shutdown`, or `recycle bin` for instant execution.
- **Web Search & URL Open**: Directly opens links or queries Google in your default browser.
- **Spotlight Keyboard Flow**: Arrow keys `↑` `↓` to navigate, `Enter` to open, `Esc` or click outside to dismiss.

### 6. Failsafe Taskbar Recovery
- **Automatic Crash & Exit Recovery**: If WinDock is closed or encounters an exception, it automatically invokes `TaskbarController.ShowTaskbar()` to restore your native Windows taskbar.
- **Emergency Restorer**: Includes `Emergency_Restore_Taskbar.bat` and `Emergency_Restore_Taskbar.ps1` for one-click manual restoration at any time.

---

## ❓ Note on the XAML XMLNS URI

> **Why did Firefox say "The resource you are looking for has been removed"?**
> 
> The string `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` is an **XML Namespace Identifier (URI)** used by the C# XAML compiler locally to resolve WPF elements (like `<Window>`, `<Border>`, `<Grid>`). 
> 
> It is **not a website** intended to be opened in a web browser. Microsoft has never hosted a webpage at that URL; it is strictly a static identifier required by the .NET XAML parser. When you build the project in Windows with `dotnet build`, the compiler recognizes this URI offline without contacting the internet.

---

## 📁 Project Structure

```
WinDock/
├── WinDock.csproj                     # .NET 8 WPF Project Configuration
├── app.manifest                       # PerMonitorV2 DPI awareness & Win10/11 compatibility
├── App.xaml / App.xaml.cs             # Application lifecycle, single-instance mutex & failsafes
├── build.bat                          # One-click Windows build script
├── Emergency_Restore_Taskbar.bat      # One-click batch taskbar unhide tool
├── Emergency_Restore_Taskbar.ps1      # PowerShell taskbar unhide script
│
├── Models/
│   ├── DockItem.cs                    # Dock item model (Title, Icon, Scale, Badge, Active state)
│   ├── DockSettings.cs                # Settings (BorderRadius, GlassOpacity, DockTint, Sizes)
│   └── NotchItem.cs                   # File/text shelf item for the Dynamic Drop Notch
│
├── Native/
│   ├── NativeMethods.cs               # Win32 P/Invoke declarations (Shell, DWM, User32, AppBar)
│   ├── TaskbarController.cs           # Shell_TrayWnd hide/show and Start Menu trigger
│   ├── AppBarHelper.cs                # SHAppBarMessage desktop work area reservation
│   ├── ShellHookListener.cs           # Window creation/destruction/activation event hook
│   ├── WindowHelper.cs                # Window enumeration, icon extraction & filtering
│   ├── ShortcutHelper.cs              # Native COM resolver for .lnk shortcuts
│   └── HotKeyHelper.cs                # Global keyboard shortcut registration
│
├── Services/
│   ├── DockManager.cs                 # Coordinates running windows, pinned apps, and launches
│   └── SettingsService.cs             # JSON settings persistence in %AppData%\WinDock
│
├── Converters/
│   └── Converters.cs                  # Value converters (CornerRadius, Visibility, Badges)
│
└── Views/
    ├── MainWindow.xaml / .cs          # Floating Dock UI with clock, tray toggles & animations
    ├── NotchWindow.xaml / .cs         # Dynamic Drop Notch Window (Top Shelf)
    └── SettingsWindow.xaml / .cs      # Settings modal with live Solid-to-Glass & Radius sliders
```

---

## 🚀 Building & Running on Windows

### Prerequisites
- Windows 10 (1809+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. Build and Run
Open Command Prompt or PowerShell in the `WinDock` folder:
```powershell
# Restore dependencies and build
dotnet build -c Release

# Run the application
dotnet run -c Release
```
Or simply double-click **`build.bat`**.

### 2. Publish as a Single Standalone Executable (.exe)
To produce a portable `.exe` that runs without installing .NET SDK:
```powershell
dotnet publish WinDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```
The resulting `WinDock.exe` will be located in the `publish` folder and can be run anywhere.

---

## ⌨️ Shortcuts & Controls

| Action | Control |
|---|---|
| **Spotlight Search** | Press `Alt + Space` or click the 🔍 icon on the dock tray |
| **Toggle Taskbar & Dock** | Press `Win + Alt + D` or click the green taskbar widget |
| **Open Start Menu** | Click the Windows logo button on the left of the dock |
| **Toggle Drop Notch Shelf** | Click the Notch button on the dock tray |
| **Drop Items into Shelf** | Drag files, folders, or links to the top center of the screen |
| **Drag Items out of Shelf** | Click and drag any item from the notch into another application |
| **Pin New App to Dock** | Drag any `.exe` or shortcut file directly onto the dock |
| **App Right-Click Menu** | Right-click any dock icon for Close, Minimize, Run as Admin, Pin/Unpin |
| **Dock Preferences** | Click the gear icon on the right side of the dock |
