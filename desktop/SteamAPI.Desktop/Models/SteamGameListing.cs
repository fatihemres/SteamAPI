using System.Globalization;
using System.Text.Json.Serialization;

namespace SteamAPI.Desktop.Models;

/// <summary>
/// Represents a game listing returned by the SteamAPI POST /steam endpoint.
/// Conforms to the verified 6-field contract: name, storeLink, imageLink, price, saleType, appId.
/// </summary>
public class SteamGameListing
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("storeLink")]
    public string StoreLink { get; set; } = string.Empty;

    [JsonPropertyName("imageLink")]
    public string ImageLink { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public double? Price { get; set; }

    [JsonPropertyName("saleType")]
    public int SaleType { get; set; }

    [JsonPropertyName("appId")]
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display text for the price or availability state.
    /// Does NOT fabricate currency symbols or unverified currency metadata.
    /// </summary>
    [JsonIgnore]
    public string DisplayPrice
    {
        get
        {
            return SaleType switch
            {
                1 => "Free to Play",
                2 => "Not Available",
                0 => Price.HasValue ? Price.Value.ToString("0.00", CultureInfo.InvariantCulture) : "Paid",
                _ => Price.HasValue ? Price.Value.ToString("0.00", CultureInfo.InvariantCulture) : "Unknown"
            };
        }
    }

    [JsonIgnore]
    public string SaleTypeBadgeText
    {
        get
        {
            return SaleType switch
            {
                0 => "PAID",
                1 => "FREE TO PLAY",
                2 => "NOT AVAILABLE",
                _ => "UNKNOWN"
            };
        }
    }

    [JsonIgnore]
    public bool IsFree => SaleType == 1;

    [JsonIgnore]
    public bool IsPaid => SaleType == 0;

    [JsonIgnore]
    public bool IsUnavailable => SaleType == 2;

    [JsonIgnore]
    public string SteamProtocolUrl => !string.IsNullOrWhiteSpace(AppId) 
        ? $"steam://store/{AppId}" 
        : StoreLink;
}
