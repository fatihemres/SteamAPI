using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamAPI.Desktop.Services;

namespace SteamAPI.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IApiProcessManager _processManager;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private string _apiStatusText = "Checking...";

    public GameExplorerViewModel GameExplorer { get; }
    public ApiInspectorViewModel ApiInspector { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }

    public ViewModelBase CurrentView => SelectedTabIndex switch
    {
        0 => GameExplorer,
        1 => ApiInspector,
        2 => Settings,
        3 => About,
        _ => GameExplorer
    };

    public MainWindowViewModel(
        GameExplorerViewModel gameExplorer,
        ApiInspectorViewModel apiInspector,
        SettingsViewModel settings,
        AboutViewModel about,
        ISettingsService settingsService,
        IApiProcessManager processManager)
    {
        GameExplorer = gameExplorer;
        ApiInspector = apiInspector;
        Settings = settings;
        About = about;
        _settingsService = settingsService;
        _processManager = processManager;

        _isDemoMode = _settingsService.CurrentSettings.DemoMode;
        UpdateApiStatus();

        Settings.OnDemoModeToggled += HandleDemoModeToggled;
        _processManager.OnStateChanged += HandleProcessStateChanged;
    }

    private void HandleProcessStateChanged(ApiProcessState _) => UpdateApiStatus();

    public bool IsExplorerActive => SelectedTabIndex == 0;
    public bool IsInspectorActive => SelectedTabIndex == 1;
    public bool IsSettingsActive => SelectedTabIndex == 2;
    public bool IsAboutActive => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(IsExplorerActive));
        OnPropertyChanged(nameof(IsInspectorActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsAboutActive));
    }

    [RelayCommand]
    public void SelectTab(object? parameter)
    {
        int index = parameter switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => -1
        };

        if (index >= 0 && index <= 3)
        {
            SelectedTabIndex = index;
        }
    }

    private void HandleDemoModeToggled(bool isDemo)
    {
        IsDemoMode = isDemo;
        GameExplorer.UpdateDemoMode(isDemo);
        UpdateApiStatus();
    }

    private bool _isShuttingDown;

    public void UpdateApiStatus()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (Application.Current != null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateApiStatusCore);
            return;
        }

        UpdateApiStatusCore();
    }

    private void UpdateApiStatusCore()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (IsDemoMode)
        {
            ApiStatusText = "Offline Demo Mode";
            return;
        }

        ApiStatusText = _processManager.State switch
        {
            ApiProcessState.Running => $"Loopback API Active ({_processManager.BoundUrl})",
            ApiProcessState.Starting => "Starting Loopback API...",
            ApiProcessState.Failed => "Loopback API Failed",
            _ => $"External API: {_settingsService.CurrentSettings.ApiBaseUrl}"
        };
    }

    public async Task ShutdownAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }
        _isShuttingDown = true;

        _processManager.OnStateChanged -= HandleProcessStateChanged;
        Settings.OnDemoModeToggled -= HandleDemoModeToggled;
        Settings.DetachEvents();
        GameExplorer.CancelPendingOperations();
        await _processManager.StopAsync().ConfigureAwait(false);
    }
}
