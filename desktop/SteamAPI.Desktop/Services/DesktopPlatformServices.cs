using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace SteamAPI.Desktop.Services;

public class DesktopClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text ?? string.Empty).ConfigureAwait(false);
        }
    }
}

public class DesktopLauncherService : ILauncherService
{
    public Task<bool> LaunchUriAsync(Uri uri)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            };
            Process.Start(psi);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
