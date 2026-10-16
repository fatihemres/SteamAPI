namespace SteamAPI.Desktop.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}
