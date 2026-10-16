using SteamAPI.Desktop.Services;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class DemoDataServiceTests
{
    private readonly DemoDataService _demoService = new();

    [Fact]
    public async Task SearchSyntheticGamesAsync_WithMatchingQuery_ReturnsMatchingResults()
    {
        var response = await _demoService.SearchSyntheticGamesAsync("portal", culture: 2);

        Assert.True(response.IsSuccess);
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Data);
        Assert.NotEmpty(response.Data);
        Assert.Contains(response.Data, g => g.Name.Contains("Portal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchSyntheticGamesAsync_AdjustsPricesByCulture()
    {
        var usResponse = await _demoService.SearchSyntheticGamesAsync("Half-Life", culture: 2);
        var trResponse = await _demoService.SearchSyntheticGamesAsync("Half-Life", culture: 1);

        Assert.True(usResponse.IsSuccess);
        Assert.True(trResponse.IsSuccess);
        Assert.NotNull(usResponse.Data);
        Assert.NotNull(trResponse.Data);

        var usGame = usResponse.Data.First(g => g.Price.HasValue);
        var trGame = trResponse.Data.First(g => g.Price.HasValue);

        // Turkey regional pricing is discounted in synthetic fixture
        Assert.True(trGame.Price < usGame.Price);
    }

    [Fact]
    public async Task SearchSyntheticGamesAsync_HandlesFreeToPlayAndPaidListings()
    {
        var response = await _demoService.SearchSyntheticGamesAsync("Counter-Strike", culture: 2);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
        Assert.Contains(response.Data, g => g.IsFree && g.SaleType == 1);
    }
}
