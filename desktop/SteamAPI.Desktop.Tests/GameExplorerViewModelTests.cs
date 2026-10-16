using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;
using SteamAPI.Desktop.ViewModels;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class GameExplorerViewModelTests
{
#pragma warning disable CS0067
    private class FakeApiClient : ISteamApiClient
    {
        public string BaseUrl { get; set; } = "http://localhost:5271";
        public int TimeoutSeconds { get; set; } = 30;
        public event Action<ApiInspectionRecord>? OnInspectionRecordCreated;

        public bool WasCalled { get; private set; }
        public string? LastQuery { get; private set; }
        public int? LastCulture { get; private set; }

        public Task<ApiResponse<IReadOnlyList<SteamGameListing>>> SearchGamesAsync(string game, int culture, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastQuery = game;
            LastCulture = culture;

            IReadOnlyList<SteamGameListing> list = new List<SteamGameListing>
            {
                new() { Name = "Portal", AppId = "400", SaleType = 0, Price = 9.99, StoreLink = "https://store.steampowered.com/app/400" }
            };

            return Task.FromResult(ApiResponse<IReadOnlyList<SteamGameListing>>.Success(list, 200, 15));
        }

        public Task<ApiResponse<bool>> CheckConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<bool>.Success(true, 200, 10));
        }
    }

    private class FakeDemoDataService : IDemoDataService
    {
        public bool WasCalled { get; private set; }

        public Task<ApiResponse<IReadOnlyList<SteamGameListing>>> SearchSyntheticGamesAsync(string query, int culture, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            IReadOnlyList<SteamGameListing> list = new List<SteamGameListing>
            {
                new() { Name = "Demo Game", AppId = "123", SaleType = 1, Price = null, StoreLink = "https://store.steampowered.com/app/123" }
            };
            return Task.FromResult(ApiResponse<IReadOnlyList<SteamGameListing>>.Success(list, 200, 5));
        }
    }
#pragma warning restore CS0067

    private class FakeSettingsService : ISettingsService
    {
        public AppSettings CurrentSettings { get; set; } = new();
        public void SaveSettings(AppSettings settings) => CurrentSettings = settings;
        public void ResetToDefaults() => CurrentSettings = new();
    }

    private class FakeClipboardService : IClipboardService
    {
        public string? LastCopiedText { get; private set; }
        public Task SetTextAsync(string text)
        {
            LastCopiedText = text;
            return Task.CompletedTask;
        }
    }

    private class FakeLauncherService : ILauncherService
    {
        public Uri? LastLaunchedUri { get; private set; }
        public Task<bool> LaunchUriAsync(Uri uri)
        {
            LastLaunchedUri = uri;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_SetsValidationErrorAndDoesNotCallApi()
    {
        var api = new FakeApiClient();
        var demo = new FakeDemoDataService();
        var settings = new FakeSettingsService();
        var clip = new FakeClipboardService();
        var launch = new FakeLauncherService();

        var vm = new GameExplorerViewModel(api, demo, settings, clip, launch)
        {
            SearchQuery = "   "
        };

        await vm.SearchAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(api.WasCalled);
        Assert.False(demo.WasCalled);
    }

    [Fact]
    public async Task SearchAsync_WithQueryOver200Chars_SetsValidationError()
    {
        var api = new FakeApiClient();
        var demo = new FakeDemoDataService();
        var settings = new FakeSettingsService();
        var clip = new FakeClipboardService();
        var launch = new FakeLauncherService();

        var vm = new GameExplorerViewModel(api, demo, settings, clip, launch)
        {
            SearchQuery = new string('A', 201)
        };

        await vm.SearchAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("200", vm.ErrorMessage);
        Assert.False(api.WasCalled);
    }

    [Fact]
    public async Task SearchAsync_InLiveMode_CallsApiClientAndPopulatesResults()
    {
        var api = new FakeApiClient();
        var demo = new FakeDemoDataService();
        var settings = new FakeSettingsService { CurrentSettings = new AppSettings { DemoMode = false } };
        var clip = new FakeClipboardService();
        var launch = new FakeLauncherService();

        var vm = new GameExplorerViewModel(api, demo, settings, clip, launch)
        {
            SearchQuery = "Portal"
        };

        await vm.SearchAsync();

        Assert.True(api.WasCalled);
        Assert.False(demo.WasCalled);
        Assert.True(vm.HasSearched);
        Assert.Single(vm.Listings);
        Assert.Equal("Portal", vm.Listings[0].Name);
        Assert.NotNull(vm.SelectedListing);
        Assert.Equal("Portal", vm.SelectedListing.Name);
    }

    [Fact]
    public async Task SearchAsync_InDemoMode_CallsDemoService()
    {
        var api = new FakeApiClient();
        var demo = new FakeDemoDataService();
        var settings = new FakeSettingsService { CurrentSettings = new AppSettings { DemoMode = true } };
        var clip = new FakeClipboardService();
        var launch = new FakeLauncherService();

        var vm = new GameExplorerViewModel(api, demo, settings, clip, launch);
        vm.UpdateDemoMode(true);
        vm.SearchQuery = "Demo";

        await vm.SearchAsync();

        Assert.False(api.WasCalled);
        Assert.True(demo.WasCalled);
        Assert.Single(vm.Listings);
        Assert.Equal("Demo Game", vm.Listings[0].Name);
    }

    [Fact]
    public void ClearSearch_ResetsAllSearchFields()
    {
        var vm = new GameExplorerViewModel(
            new FakeApiClient(),
            new FakeDemoDataService(),
            new FakeSettingsService(),
            new FakeClipboardService(),
            new FakeLauncherService())
        {
            SearchQuery = "Half-Life",
            ErrorMessage = "Some error",
            StatusMessage = "Found 2 games",
            HasSearched = true
        };

        vm.ClearSearch();

        Assert.Empty(vm.SearchQuery);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.StatusMessage);
        Assert.False(vm.HasSearched);
        Assert.Empty(vm.Listings);
        Assert.Null(vm.SelectedListing);
    }

    [Fact]
    public async Task CopyAppIdAsync_CopiesSelectedAppIdToClipboard()
    {
        var clip = new FakeClipboardService();
        var vm = new GameExplorerViewModel(
            new FakeApiClient(),
            new FakeDemoDataService(),
            new FakeSettingsService(),
            clip,
            new FakeLauncherService());

        var listing = new SteamGameListing { AppId = "730", Name = "CS2" };
        vm.SelectListing(listing);

        await vm.CopyAppIdAsync(listing);

        Assert.Equal("730", clip.LastCopiedText);
        Assert.NotNull(vm.CopiedFeedbackMessage);
    }

    [Fact]
    public async Task OpenInSteamAsync_LaunchesSteamProtocolUri()
    {
        var launch = new FakeLauncherService();
        var vm = new GameExplorerViewModel(
            new FakeApiClient(),
            new FakeDemoDataService(),
            new FakeSettingsService(),
            new FakeClipboardService(),
            launch);

        var listing = new SteamGameListing { AppId = "570", Name = "Dota 2" };

        await vm.OpenInSteamAsync(listing);

        Assert.NotNull(launch.LastLaunchedUri);
        Assert.Equal("steam://store/570", launch.LastLaunchedUri.ToString());
    }
}
