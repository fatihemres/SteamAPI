using SteamAPI.Desktop.Models;

namespace SteamAPI.Desktop.Services;

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }
    void SaveSettings(AppSettings settings);
    void ResetToDefaults();
}
