using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;
using SteamAPI.Desktop.ViewModels;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class MainWindowViewModelTests
{
    private class DummySettingsService : ISettingsService
    {
        public AppSettings CurrentSettings { get; set; } = new();
        public void SaveSettings(AppSettings settings) => CurrentSettings = settings;
        public void ResetToDefaults() => CurrentSettings = new();
    }

    private class DummyProcessManager : IApiProcessManager
    {
        public ApiProcessState State { get; set; } = ApiProcessState.Stopped;
        public string? LastError { get; set; }
        public int? ProcessId => null;
        public string BoundUrl => "http://127.0.0.1:5271";
        public event Action<ApiProcessState>? OnStateChanged;
        public bool StoppedCalled { get; private set; }

        public Task<bool> StartAsync(string? executablePath = null, CancellationToken cancellationToken = default)
        {
            State = ApiProcessState.Running;
            OnStateChanged?.Invoke(State);
            return Task.FromResult(true);
        }

        public Task StopAsync()
        {
            StoppedCalled = true;
            State = ApiProcessState.Stopped;
            OnStateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private class DummyClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private class DummyLauncher : ILauncherService
    {
        public Task<bool> LaunchUriAsync(Uri uri) => Task.FromResult(true);
    }

    [Fact]
    public void SelectTab_SwitchesActivePageCorrectly()
    {
        var settingsService = new DummySettingsService();
        var processManager = new DummyProcessManager();
        var client = new SteamApiClient();
        var demo = new DemoDataService();
        var clip = new DummyClipboard();
        var launcher = new DummyLauncher();

        var explorer = new GameExplorerViewModel(client, demo, settingsService, clip, launcher);
        var inspector = new ApiInspectorViewModel(clip, client);
        var settings = new SettingsViewModel(settingsService, client, processManager);
        var about = new AboutViewModel(launcher);

        var mainVm = new MainWindowViewModel(explorer, inspector, settings, about, settingsService, processManager);

        Assert.Equal(0, mainVm.SelectedTabIndex);
        Assert.Equal(explorer, mainVm.CurrentView);
        Assert.True(mainVm.IsExplorerActive);
        Assert.False(mainVm.IsInspectorActive);

        mainVm.SelectTab(1);
        Assert.Equal(1, mainVm.SelectedTabIndex);
        Assert.Equal(inspector, mainVm.CurrentView);
        Assert.True(mainVm.IsInspectorActive);
        Assert.False(mainVm.IsExplorerActive);

        mainVm.SelectTab(2);
        Assert.Equal(2, mainVm.SelectedTabIndex);
        Assert.Equal(settings, mainVm.CurrentView);
        Assert.True(mainVm.IsSettingsActive);

        mainVm.SelectTab(3);
        Assert.Equal(3, mainVm.SelectedTabIndex);
        Assert.Equal(about, mainVm.CurrentView);
        Assert.True(mainVm.IsAboutActive);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void SelectTabCommand_AcceptsStringAndIntegerParameters(object parameter, int expectedIndex)
    {
        var settingsService = new DummySettingsService();
        var processManager = new DummyProcessManager();
        var client = new SteamApiClient();
        var demo = new DemoDataService();
        var clip = new DummyClipboard();
        var launcher = new DummyLauncher();

        var explorer = new GameExplorerViewModel(client, demo, settingsService, clip, launcher);
        var inspector = new ApiInspectorViewModel(clip, client);
        var settings = new SettingsViewModel(settingsService, client, processManager);
        var about = new AboutViewModel(launcher);

        var mainVm = new MainWindowViewModel(explorer, inspector, settings, about, settingsService, processManager);

        // Execute via RelayCommand
        mainVm.SelectTabCommand.Execute(parameter);

        Assert.Equal(expectedIndex, mainVm.SelectedTabIndex);
        Assert.Equal(expectedIndex == 0, mainVm.IsExplorerActive);
        Assert.Equal(expectedIndex == 1, mainVm.IsInspectorActive);
        Assert.Equal(expectedIndex == 2, mainVm.IsSettingsActive);
        Assert.Equal(expectedIndex == 3, mainVm.IsAboutActive);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("-1")]
    [InlineData("4")]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(99)]
    public void SelectTabCommand_IgnoresInvalidParameters(object? invalidParameter)
    {
        var settingsService = new DummySettingsService();
        var processManager = new DummyProcessManager();
        var client = new SteamApiClient();
        var demo = new DemoDataService();
        var clip = new DummyClipboard();
        var launcher = new DummyLauncher();

        var explorer = new GameExplorerViewModel(client, demo, settingsService, clip, launcher);
        var inspector = new ApiInspectorViewModel(clip, client);
        var settings = new SettingsViewModel(settingsService, client, processManager);
        var about = new AboutViewModel(launcher);

        var mainVm = new MainWindowViewModel(explorer, inspector, settings, about, settingsService, processManager);

        // Move to tab 2 first
        mainVm.SelectTabCommand.Execute("2");
        Assert.Equal(2, mainVm.SelectedTabIndex);

        // Execute with invalid parameter - should maintain tab 2
        mainVm.SelectTabCommand.Execute(invalidParameter);
        Assert.Equal(2, mainVm.SelectedTabIndex);
        Assert.Equal(settings, mainVm.CurrentView);
        Assert.True(mainVm.IsSettingsActive);
    }

    [Theory]
    [InlineData(ApiProcessState.Running)]
    [InlineData(ApiProcessState.Starting)]
    [InlineData(ApiProcessState.Failed)]
    [InlineData(ApiProcessState.Stopped)]
    public async Task ShutdownAsync_StopsApiProcessAcrossAllStates(ApiProcessState initialState)
    {
        var settingsService = new DummySettingsService();
        var processManager = new DummyProcessManager { State = initialState };
        using var client = new SteamApiClient();
        var demo = new DemoDataService();
        var clip = new DummyClipboard();
        var launcher = new DummyLauncher();

        var explorer = new GameExplorerViewModel(client, demo, settingsService, clip, launcher);
        var mainVm = new MainWindowViewModel(
            explorer,
            new ApiInspectorViewModel(clip, client),
            new SettingsViewModel(settingsService, client, processManager),
            new AboutViewModel(launcher),
            settingsService,
            processManager);

        explorer.CancelPendingOperations();
        await mainVm.ShutdownAsync();

        Assert.True(processManager.StoppedCalled);
        Assert.Equal(ApiProcessState.Stopped, processManager.State);
    }
}
