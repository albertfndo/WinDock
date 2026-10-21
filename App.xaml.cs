using System;
using System.Threading;
using System.Windows;
using WinDock.Native;

namespace WinDock
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        private const string MutexName = "WinDock_SingleInstance_Mutex_99F";

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. Single Instance Protection
            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // App is already running
                MessageBox.Show(
                    "WinDock is already running. You can control it from the dock or press Win+Alt+D.",
                    "WinDock Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }

            // 2. Global Exception Safety to prevent leaving the taskbar hidden on unhandled crash
            DispatcherUnhandledException += (sender, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"[WinDock Crash] {args.Exception}");
                TaskbarController.ShowTaskbar();
                MessageBox.Show($"WinDock encountered a handled error:
{args.Exception.Message}", "WinDock Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true; // Prevent app force-close!
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                TaskbarController.ShowTaskbar();
            };

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Always ensure the native Windows taskbar is restored when exiting
            TaskbarController.ShowTaskbar();

            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();

            base.OnExit(e);
        }
    }
}
