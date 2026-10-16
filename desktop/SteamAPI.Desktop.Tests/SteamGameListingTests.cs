using System.Text.Json;
using SteamAPI.Desktop.Models;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class SteamGameListingTests
{
    [Fact]
    public void Deserialization_MatchesVerifiedSixFieldContract()
    {
        // Arrange
        const string json = """
        {
            "name": "Portal 2",
            "storeLink": "https://store.steampowered.com/app/620/Portal_2/",
            "imageLink": "https://cdn.akamai.steamstatic.com/steam/apps/620/capsule_sm_120.jpg",
            "price": 9.99,
            "saleType": 0,
            "appId": "620"
        }
        """;

        // Act
        var listing = JsonSerializer.Deserialize<SteamGameListing>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        Assert.NotNull(listing);
        Assert.Equal("Portal 2", listing.Name);
        Assert.Equal("https://store.steampowered.com/app/620/Portal_2/", listing.StoreLink);
        Assert.Equal("https://cdn.akamai.steamstatic.com/steam/apps/620/capsule_sm_120.jpg", listing.ImageLink);
        Assert.Equal(9.99, listing.Price);
        Assert.Equal(0, listing.SaleType);
        Assert.Equal("620", listing.AppId);
        Assert.True(listing.IsPaid);
        Assert.False(listing.IsFree);
        Assert.False(listing.IsUnavailable);
        Assert.Equal("9.99", listing.DisplayPrice);
        Assert.Equal("PAID", listing.SaleTypeBadgeText);
        Assert.Equal("steam://store/620", listing.SteamProtocolUrl);
    }

    [Theory]
    [InlineData(1, null, "Free to Play", true, false, false, "FREE TO PLAY")]
    [InlineData(2, null, "Not Available", false, false, true, "NOT AVAILABLE")]
    [InlineData(0, 14.50, "14.50", false, true, false, "PAID")]
    [InlineData(0, null, "Paid", false, true, false, "PAID")]
    public void DisplayPriceAndBadges_FormatCorrectlyAccordingToSaleType(
        int saleType,
        double? price,
        string expectedDisplayPrice,
        bool expectedIsFree,
        bool expectedIsPaid,
        bool expectedIsUnavailable,
        string expectedBadge)
    {
        // Arrange
        var listing = new SteamGameListing
        {
            Name = "Test Game",
            SaleType = saleType,
            Price = price,
            AppId = "100"
        };

        // Act & Assert
        Assert.Equal(expectedDisplayPrice, listing.DisplayPrice);
        Assert.Equal(expectedIsFree, listing.IsFree);
        Assert.Equal(expectedIsPaid, listing.IsPaid);
        Assert.Equal(expectedIsUnavailable, listing.IsUnavailable);
        Assert.Equal(expectedBadge, listing.SaleTypeBadgeText);
    }

    [Fact]
    public void SteamProtocolUrl_FallsBackToStoreLink_WhenAppIdMissing()
    {
        // Arrange
        var listing = new SteamGameListing
        {
            Name = "Web Game",
            AppId = "",
            StoreLink = "https://store.steampowered.com/app/999/"
        };

        // Act & Assert
        Assert.Equal("https://store.steampowered.com/app/999/", listing.SteamProtocolUrl);
    }
}
