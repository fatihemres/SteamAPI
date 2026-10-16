using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;
using SteamAPI.Desktop.ViewModels;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class SettingsViewModelTests
{
    private class FakeSettingsService : ISettingsService
    {
        public AppSettings CurrentSettings { get; set; } = new();
        public void SaveSettings(AppSettings settings) => CurrentSettings = settings;
        public void ResetToDefaults() => CurrentSettings = new();
    }

#pragma warning disable CS0067
    private class FakeApiClient : ISteamApiClient
    {
        public string BaseUrl { get; set; } = "http://localhost:5271";
        public int TimeoutSeconds { get; set; } = 30;
        public event Action<ApiInspectionRecord>? OnInspectionRecordCreated;
        public bool ConnectionSuccess { get; set; } = true;

        public Task<ApiResponse<IReadOnlyList<SteamGameListing>>> SearchGamesAsync(string game, int culture, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<IReadOnlyList<SteamGameListing>>.Success(new List<SteamGameListing>(), 200, 10));
        }

        public Task<ApiResponse<bool>> CheckConnectionAsync(CancellationToken cancellationToken = default)
        {
            return ConnectionSuccess
                ? Task.FromResult(ApiResponse<bool>.Success(true, 200, 12))
                : Task.FromResult(ApiResponse<bool>.Failure(502, "Failed to connect", null, 12));
        }
    }
#pragma warning restore CS0067

    private class FakeProcessManager : IApiProcessManager
    {
        public ApiProcessState State { get; set; } = ApiProcessState.Stopped;
        public string? LastError { get; set; }
        public int? ProcessId => State == ApiProcessState.Running ? 1234 : null;
        public string BoundUrl => "http://127.0.0.1:5271";
        public event Action<ApiProcessState>? OnStateChanged;

        public Task<bool> StartAsync(string? executablePath = null, CancellationToken cancellationToken = default)
        {
            State = ApiProcessState.Running;
            OnStateChanged?.Invoke(State);
            return Task.FromResult(true);
        }

        public Task StopAsync()
        {
            State = ApiProcessState.Stopped;
            OnStateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    [Fact]
    public void LoadSettings_InitializesPropertiesFromService()
    {
        var settingsService = new FakeSettingsService
        {
            CurrentSettings = new AppSettings
            {
                ApiBaseUrl = "http://127.0.0.1:9000",
                TimeoutSeconds = 45,
                DemoMode = true,
                AutoStartApi = true
            }
        };

        var vm = new SettingsViewModel(settingsService, new FakeApiClient(), new FakeProcessManager());

        Assert.Equal("http://127.0.0.1:9000", vm.ApiBaseUrl);
        Assert.Equal(45, vm.TimeoutSeconds);
        Assert.True(vm.IsDemoMode);
        Assert.True(vm.AutoStartApi);
    }

    [Fact]
    public void SaveSettings_PersistsChangesAndFiresDemoToggledEvent()
    {
        var settingsService = new FakeSettingsService();
        var client = new FakeApiClient();
        var vm = new SettingsViewModel(settingsService, client, new FakeProcessManager());

        bool? demoToggledValue = null;
        vm.OnDemoModeToggled += isDemo => demoToggledValue = isDemo;

        vm.ApiBaseUrl = "http://localhost:7030";
        vm.TimeoutSeconds = 60;
        vm.IsDemoMode = true;
        vm.AutoStartApi = false;

        vm.SaveSettings();

        Assert.Equal("http://localhost:7030", settingsService.CurrentSettings.ApiBaseUrl);
        Assert.Equal(60, settingsService.CurrentSettings.TimeoutSeconds);
        Assert.True(settingsService.CurrentSettings.DemoMode);
        Assert.Equal("http://localhost:7030", client.BaseUrl);
        Assert.True(demoToggledValue);
        Assert.NotNull(vm.SaveFeedbackMessage);
    }

    [Fact]
    public void ResetDefaults_RestoresInitialState()
    {
        var settingsService = new FakeSettingsService
        {
            CurrentSettings = new AppSettings { DemoMode = true, ApiBaseUrl = "http://custom:1234" }
        };

        var vm = new SettingsViewModel(settingsService, new FakeApiClient(), new FakeProcessManager());
        vm.ResetDefaults();

        Assert.Equal("http://localhost:5271", vm.ApiBaseUrl);
        Assert.False(vm.IsDemoMode);
    }

    [Fact]
    public async Task TestConnectionAsync_SetsSuccessStateWhenEndpointResponds()
    {
        var client = new FakeApiClient { ConnectionSuccess = true };
        var vm = new SettingsViewModel(new FakeSettingsService(), client, new FakeProcessManager());

        await vm.TestConnectionAsync();

        Assert.True(vm.IsConnectionSuccessful);
        Assert.Contains("successful", vm.ConnectionStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionAsync_SetsFailureStateWhenEndpointUnreachable()
    {
        var client = new FakeApiClient { ConnectionSuccess = false };
        var vm = new SettingsViewModel(new FakeSettingsService(), client, new FakeProcessManager());

        await vm.TestConnectionAsync();

        Assert.False(vm.IsConnectionSuccessful);
        Assert.Contains("failed", vm.ConnectionStatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
