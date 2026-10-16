using System.Text.Json;
using SteamAPI.Desktop.Models;

namespace SteamAPI.Desktop.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings _currentSettings;

    public AppSettings CurrentSettings => _currentSettings;

    public SettingsService(string? customFilePath = null)
    {
        if (customFilePath != null)
        {
            _settingsFilePath = customFilePath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "SteamAPI.Desktop");
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }

        _currentSettings = LoadSettings();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults on corrupt file or read error
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        _currentSettings = settings ?? new AppSettings();
        try
        {
            var folder = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Suppress IO failure in restricted sandbox environments
        }
    }

    public void ResetToDefaults()
    {
        SaveSettings(new AppSettings());
    }
}
