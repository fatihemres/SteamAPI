using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempFile;

    public SettingsServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"steamapi_test_settings_{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }

    [Fact]
    public void LoadSettings_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var service = new SettingsService(_tempFile);

        Assert.NotNull(service.CurrentSettings);
        Assert.Equal("http://localhost:5271", service.CurrentSettings.ApiBaseUrl);
        Assert.Equal(30, service.CurrentSettings.TimeoutSeconds);
        Assert.False(service.CurrentSettings.DemoMode);
    }

    [Fact]
    public void SaveSettings_PersistsDataToDiskAndReloadsCorrectly()
    {
        var service = new SettingsService(_tempFile);

        var newSettings = new AppSettings
        {
            ApiBaseUrl = "http://127.0.0.1:8888",
            TimeoutSeconds = 42,
            DemoMode = true,
            AutoStartApi = true
        };

        service.SaveSettings(newSettings);

        // Read anew from disk
        var reloadedService = new SettingsService(_tempFile);
        Assert.Equal("http://127.0.0.1:8888", reloadedService.CurrentSettings.ApiBaseUrl);
        Assert.Equal(42, reloadedService.CurrentSettings.TimeoutSeconds);
        Assert.True(reloadedService.CurrentSettings.DemoMode);
        Assert.True(reloadedService.CurrentSettings.AutoStartApi);
    }

    [Fact]
    public void ResetToDefaults_OverwritesExistingSettingsWithDefaults()
    {
        var service = new SettingsService(_tempFile);
        service.SaveSettings(new AppSettings { DemoMode = true, TimeoutSeconds = 99 });

        service.ResetToDefaults();

        var reloaded = new SettingsService(_tempFile);
        Assert.False(reloaded.CurrentSettings.DemoMode);
        Assert.Equal(30, reloaded.CurrentSettings.TimeoutSeconds);
        Assert.Equal("http://localhost:5271", reloaded.CurrentSettings.ApiBaseUrl);
    }
}
