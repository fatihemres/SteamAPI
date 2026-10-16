using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SteamAPI.Desktop.Services;
using SteamAPI.Desktop.ViewModels;
using SteamAPI.Desktop.Views;

namespace SteamAPI.Desktop;

public partial class App : Application
{
    private MainWindowViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsService = new SettingsService();
            var processManager = new ApiProcessManager();
            var apiClient = new SteamApiClient(settingsService.CurrentSettings.ApiBaseUrl, settingsService.CurrentSettings.TimeoutSeconds);
            var demoDataService = new DemoDataService();
            var clipboardService = new DesktopClipboardService();
            var launcherService = new DesktopLauncherService();

            var gameExplorer = new GameExplorerViewModel(apiClient, demoDataService, settingsService, clipboardService, launcherService);
            var apiInspector = new ApiInspectorViewModel(clipboardService, apiClient);
            var settings = new SettingsViewModel(settingsService, apiClient, processManager);
            var about = new AboutViewModel(launcherService);

            _mainViewModel = new MainWindowViewModel(gameExplorer, apiInspector, settings, about, settingsService, processManager);

            var mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            mainWindow.Closing += (_, _) =>
            {
                _mainViewModel?.ShutdownAsync().GetAwaiter().GetResult();
            };

            desktop.Exit += (_, _) =>
            {
                _mainViewModel?.ShutdownAsync().GetAwaiter().GetResult();
                apiClient.Dispose();
            };

            desktop.MainWindow = mainWindow;

            // Auto-start loopback API if configured
            if (settingsService.CurrentSettings.AutoStartApi && !settingsService.CurrentSettings.DemoMode)
            {
                _ = processManager.StartAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}