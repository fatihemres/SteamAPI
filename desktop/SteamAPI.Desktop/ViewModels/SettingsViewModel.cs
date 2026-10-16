using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;

namespace SteamAPI.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ISteamApiClient _apiClient;
    private readonly IApiProcessManager _processManager;

    [ObservableProperty]
    private string _apiBaseUrl = "http://localhost:5271";

    [ObservableProperty]
    private int _timeoutSeconds = 30;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private bool _autoStartApi;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string? _connectionStatusMessage;

    [ObservableProperty]
    private bool? _isConnectionSuccessful;

    [ObservableProperty]
    private string _apiProcessStateText = "Stopped";

    [ObservableProperty]
    private string? _apiProcessError;

    [ObservableProperty]
    private string? _saveFeedbackMessage;

    public event Action<bool>? OnDemoModeToggled;

    public SettingsViewModel(
        ISettingsService settingsService,
        ISteamApiClient apiClient,
        IApiProcessManager processManager)
    {
        _settingsService = settingsService;
        _apiClient = apiClient;
        _processManager = processManager;

        LoadSettings();
        _processManager.OnStateChanged += UpdateProcessState;
        UpdateProcessState(_processManager.State);
    }

    private void LoadSettings()
    {
        var current = _settingsService.CurrentSettings;
        ApiBaseUrl = current.ApiBaseUrl;
        TimeoutSeconds = current.TimeoutSeconds;
        IsDemoMode = current.DemoMode;
        AutoStartApi = current.AutoStartApi;

        _apiClient.BaseUrl = ApiBaseUrl;
        _apiClient.TimeoutSeconds = TimeoutSeconds;
    }

    [RelayCommand]
    public void SaveSettings()
    {
        var settings = new AppSettings
        {
            ApiBaseUrl = ApiBaseUrl.Trim(),
            TimeoutSeconds = TimeoutSeconds,
            DemoMode = IsDemoMode,
            AutoStartApi = AutoStartApi
        };

        _settingsService.SaveSettings(settings);
        _apiClient.BaseUrl = settings.ApiBaseUrl;
        _apiClient.TimeoutSeconds = settings.TimeoutSeconds;

        OnDemoModeToggled?.Invoke(IsDemoMode);
        SaveFeedbackMessage = "Settings saved successfully.";
    }

    [RelayCommand]
    public void ResetDefaults()
    {
        _settingsService.ResetToDefaults();
        LoadSettings();
        OnDemoModeToggled?.Invoke(IsDemoMode);
        SaveFeedbackMessage = "Settings reset to defaults.";
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        IsTestingConnection = true;
        ConnectionStatusMessage = "Testing connection to " + ApiBaseUrl + "...";
        IsConnectionSuccessful = null;

        try
        {
            _apiClient.BaseUrl = ApiBaseUrl.Trim();
            var response = await _apiClient.CheckConnectionAsync();

            if (response.IsSuccess)
            {
                IsConnectionSuccessful = true;
                ConnectionStatusMessage = $"Connection successful! Responded in {response.DurationMs} ms (HTTP {response.StatusCode}).";
            }
            else
            {
                IsConnectionSuccessful = false;
                ConnectionStatusMessage = $"Connection failed: {response.ErrorMessage ?? "Unreachable"}";
            }
        }
        catch (Exception ex)
        {
            IsConnectionSuccessful = false;
            ConnectionStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    public async Task StartApiProcessAsync()
    {
        ApiProcessError = null;
        var success = await _processManager.StartAsync();
        if (!success)
        {
            ApiProcessError = _processManager.LastError;
        }
        else
        {
            ApiBaseUrl = _processManager.BoundUrl;
            SaveSettings();
        }
    }

    [RelayCommand]
    public async Task StopApiProcessAsync()
    {
        await _processManager.StopAsync();
    }

    public void DetachEvents()
    {
        _processManager.OnStateChanged -= UpdateProcessState;
    }

    private void UpdateProcessState(ApiProcessState state)
    {
        if (Application.Current != null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyProcessState(state));
            return;
        }

        ApplyProcessState(state);
    }

    private void ApplyProcessState(ApiProcessState state)
    {
        ApiProcessStateText = state switch
        {
            ApiProcessState.Stopped => "Stopped",
            ApiProcessState.Starting => "Starting...",
            ApiProcessState.Running => $"Running (PID {_processManager.ProcessId})",
            ApiProcessState.Failed => "Failed to start",
            _ => "Unknown"
        };
        ApiProcessError = _processManager.LastError;
    }
}
