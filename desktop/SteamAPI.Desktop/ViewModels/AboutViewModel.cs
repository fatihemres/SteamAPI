using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamAPI.Desktop.Services;

namespace SteamAPI.Desktop.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    private readonly ILauncherService _launcherService;

    public string AppVersion => "v0.1.0 (Release Candidate)";
    public string ApiCompatibilityVersion => "SteamAPI v0.2.0 (.NET 10)";
    public string FrameworkVersion => ".NET 10.0 (C# 14, Avalonia UI 12.0)";
    public string LicenseType => "MIT License";

    public string GitHubRepoUrl => "https://github.com/fatihemres/SteamAPI";
    public string GitHubReleasesUrl => "https://github.com/fatihemres/SteamAPI/releases";
    public string ApiDocsUrl => "https://github.com/fatihemres/SteamAPI/blob/master/docs/API.md";

    public string CreatorTitle => "Designed & Developed by";
    public string CreatorName => "Fatih Emre Sarman";
    public string CreatorGitHub => "@fatihemres";
    public string CreatorProfileUrl => "https://github.com/fatihemres";
    public string CopyrightNotice => "Copyright © 2026 Fatih Emre Sarman";

    public AboutViewModel(ILauncherService launcherService)
    {
        _launcherService = launcherService;
    }

    [RelayCommand]
    public async Task OpenCreatorProfileAsync()
    {
        await _launcherService.LaunchUriAsync(new Uri(CreatorProfileUrl)).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task OpenGitHubRepoAsync()
    {
        await _launcherService.LaunchUriAsync(new Uri(GitHubRepoUrl)).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task OpenGitHubReleasesAsync()
    {
        await _launcherService.LaunchUriAsync(new Uri(GitHubReleasesUrl)).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task OpenApiDocsAsync()
    {
        await _launcherService.LaunchUriAsync(new Uri(ApiDocsUrl)).ConfigureAwait(false);
    }
}
