using System.Linq;
using System;
using System.IO;
using System.Text.Json;
using WinDock.Models;

namespace WinDock.Services
{
    public class SettingsService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinDock");

        private static readonly string SettingsFile = Path.Combine(AppDataFolder, "dock_settings.json");

        private static SettingsService? _instance;
        public static SettingsService Instance => _instance ??= new SettingsService();

        public DockSettings CurrentSettings { get; private set; }

        public event Action? SettingsChanged;

        public SettingsService()
        {
            CurrentSettings = LoadSettings();
        }

        public DockSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<DockSettings>(json);
                    if (settings != null)
                    {
                        if (settings.PinnedApps == null || settings.PinnedApps.Count == 0)
                        {
                            settings.PinnedApps = new DockSettings().PinnedApps;
                        }
                        else if (!settings.PinnedApps.Any(p => p.Title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)))
                        {
                            // Ensure WhatsApp default pinned app is present
                            settings.PinnedApps.Insert(Math.Min(2, settings.PinnedApps.Count), new PinnedAppConfig
                            {
                                Title = "WhatsApp",
                                ProcessName = "WhatsApp",
                                LaunchPath = "whatsapp:"
                            });
                        }
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Load error: {ex.Message}");
            }

            return new DockSettings();
        }

        public void SaveSettings(DockSettings settings)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                CurrentSettings = settings;
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
                SettingsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Save error: {ex.Message}");
            }
        }
    }
}
