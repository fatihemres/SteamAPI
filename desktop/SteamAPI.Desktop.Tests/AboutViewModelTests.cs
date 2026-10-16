using SteamAPI.Desktop.Services;
using SteamAPI.Desktop.ViewModels;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class AboutViewModelTests
{
    private class DummyLauncher : ILauncherService
    {
        public Uri? LaunchedUri { get; private set; }
        public Task<bool> LaunchUriAsync(Uri uri)
        {
            LaunchedUri = uri;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public void Properties_ExposeVersionAndLicenseInformation()
    {
        var launcher = new DummyLauncher();
        var vm = new AboutViewModel(launcher);

        Assert.Contains("v0.1.0", vm.AppVersion);
        Assert.Contains("SteamAPI v0.2.0", vm.ApiCompatibilityVersion);
        Assert.Contains(".NET 10", vm.FrameworkVersion);
        Assert.Equal("MIT License", vm.LicenseType);
        Assert.Contains("github.com", vm.GitHubRepoUrl);
    }

    [Fact]
    public async Task OpenCommands_LaunchProperUris()
    {
        var launcher = new DummyLauncher();
        var vm = new AboutViewModel(launcher);

        await vm.OpenGitHubRepoAsync();
        Assert.Equal(new Uri(vm.GitHubRepoUrl), launcher.LaunchedUri);

        await vm.OpenGitHubReleasesAsync();
        Assert.Equal(new Uri(vm.GitHubReleasesUrl), launcher.LaunchedUri);

        await vm.OpenApiDocsAsync();
        Assert.Equal(new Uri(vm.ApiDocsUrl), launcher.LaunchedUri);
    }

    [Fact]
    public void CreatorAttribution_ContainsExpectedAuthorAndRepositoryLinks()
    {
        var launcher = new DummyLauncher();
        var vm = new AboutViewModel(launcher);

        Assert.Equal("Designed & Developed by", vm.CreatorTitle);
        Assert.Equal("Fatih Emre Sarman", vm.CreatorName);
        Assert.Equal("@fatihemres", vm.CreatorGitHub);
        Assert.Equal("https://github.com/fatihemres", vm.CreatorProfileUrl);
        Assert.Contains("Fatih Emre Sarman", vm.CopyrightNotice);
        Assert.Contains("2026", vm.CopyrightNotice);
    }

    [Fact]
    public async Task OpenCreatorProfileAsync_LaunchesCorrectProfileUri()
    {
        var launcher = new DummyLauncher();
        var vm = new AboutViewModel(launcher);

        await vm.OpenCreatorProfileAsync();

        Assert.NotNull(launcher.LaunchedUri);
        Assert.Equal(new Uri("https://github.com/fatihemres"), launcher.LaunchedUri);
    }
}
