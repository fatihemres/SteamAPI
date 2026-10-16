namespace SteamAPI.Desktop.Services;

public interface ILauncherService
{
    Task<bool> LaunchUriAsync(Uri uri);
}
