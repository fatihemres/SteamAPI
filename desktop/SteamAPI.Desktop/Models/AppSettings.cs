namespace SteamAPI.Desktop.Models;

/// <summary>
/// User and application settings for SteamAPI Desktop.
/// </summary>
public class AppSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5271";
    public int TimeoutSeconds { get; set; } = 30;
    public bool DemoMode { get; set; } = false;
    public bool AutoStartApi { get; set; } = false;
    public string Theme { get; set; } = "Dark";
}
