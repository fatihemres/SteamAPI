using System.Text.Json;
using SteamAPI.Desktop.Models;

namespace SteamAPI.Desktop.Services;

public class DemoDataService : IDemoDataService
{
    private static readonly IReadOnlyList<SteamGameListing> BaseSyntheticCatalog = new List<SteamGameListing>
    {
        new()
        {
            Name = "Portal: Quantum Paradox [SYNTHETIC DEMO]",
            AppId = "999001",
            StoreLink = "https://store.steampowered.com/app/999001/Portal_Quantum_Paradox_Demo",
            ImageLink = "https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg",
            Price = 9.99,
            SaleType = 0
        },
        new()
        {
            Name = "Counter-Strike: Tactical Protocol [SYNTHETIC DEMO]",
            AppId = "999002",
            StoreLink = "https://store.steampowered.com/app/999002/Counter_Strike_Tactical_Protocol_Demo",
            ImageLink = "https://cdn.akamai.steamstatic.com/steam/apps/730/header.jpg",
            Price = null,
            SaleType = 1 // Free to Play
        },
        new()
        {
            Name = "Half-Life: Echoes of Black Mesa [SYNTHETIC DEMO]",
            AppId = "999003",
            StoreLink = "https://store.steampowered.com/app/999003/Half_Life_Echoes_Demo",
            ImageLink = "https://cdn.akamai.steamstatic.com/steam/apps/220/header.jpg",
            Price = 19.99,
            SaleType = 0
        },
        new()
        {
            Name = "Chronicles of Nebula [SYNTHETIC DEMO]",
            AppId = "999004",
            StoreLink = "https://store.steampowered.com/app/999004/Chronicles_of_Nebula_Demo",
            ImageLink = "https://cdn.akamai.steamstatic.com/steam/apps/620/header.jpg",
            Price = 14.99,
            SaleType = 0
        },
        new()
        {
            Name = "Cyber Arena Champions [SYNTHETIC DEMO]",
            AppId = "999005",
            StoreLink = "https://store.steampowered.com/app/999005/Cyber_Arena_Demo",
            ImageLink = "https://cdn.akamai.steamstatic.com/steam/apps/570/header.jpg",
            Price = null,
            SaleType = 1 // Free to Play
        },
        new()
        {
            Name = "Project Aether (Unannounced) [SYNTHETIC DEMO]",
            AppId = "999006",
            StoreLink = "https://store.steampowered.com/app/999006/Project_Aether_Demo",
            ImageLink = "",
            Price = null,
            SaleType = 2 // Not Available
        }
    };

    public Task<ApiResponse<IReadOnlyList<SteamGameListing>>> SearchSyntheticGamesAsync(
        string query, 
        int culture, 
        CancellationToken cancellationToken = default)
    {
        var cleanQuery = (query ?? string.Empty).Trim();
        var rawMatches = string.IsNullOrWhiteSpace(cleanQuery)
            ? BaseSyntheticCatalog.ToList()
            : BaseSyntheticCatalog
                .Where(g => g.Name.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();

        // If no direct keyword match, generate dynamic realistic synthetic items for the user's query
        if (rawMatches.Count == 0 && !string.IsNullOrWhiteSpace(cleanQuery))
        {
            rawMatches = new List<SteamGameListing>
            {
                new()
                {
                    Name = $"{cleanQuery} [SYNTHETIC DEMO]",
                    AppId = "999101",
                    StoreLink = $"https://store.steampowered.com/app/999101/{Uri.EscapeDataString(cleanQuery)}_Demo",
                    ImageLink = "https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg",
                    Price = 19.99,
                    SaleType = 0
                },
                new()
                {
                    Name = $"{cleanQuery} Deluxe Edition [SYNTHETIC DEMO]",
                    AppId = "999102",
                    StoreLink = $"https://store.steampowered.com/app/999102/{Uri.EscapeDataString(cleanQuery)}_Deluxe_Demo",
                    ImageLink = "https://cdn.akamai.steamstatic.com/steam/apps/620/header.jpg",
                    Price = 29.99,
                    SaleType = 0
                }
            };
        }

        // Apply regional culture adjustments (e.g. Turkey culture 1 regional pricing)
        var adjustedMatches = rawMatches.Select(g =>
        {
            double? adjustedPrice = g.Price;
            if (g.Price.HasValue)
            {
                adjustedPrice = culture switch
                {
                    1 => Math.Round(g.Price.Value * 0.60, 2), // Turkey MENA regional discount
                    3 => Math.Round(g.Price.Value * 0.92, 2), // EUR exchange representation
                    _ => g.Price.Value                        // US baseline USD
                };
            }

            return new SteamGameListing
            {
                Name = g.Name,
                AppId = g.AppId,
                StoreLink = g.StoreLink,
                ImageLink = g.ImageLink,
                SaleType = g.SaleType,
                Price = adjustedPrice
            };
        }).ToList();

        var requestJson = JsonSerializer.Serialize(new
        {
            game = cleanQuery,
            culture,
            _demoNote = "OFFLINE DEMO MODE - Synthetic simulation only"
        }, new JsonSerializerOptions { WriteIndented = true });

        var responseJson = JsonSerializer.Serialize(adjustedMatches, new JsonSerializerOptions { WriteIndented = true });

        var response = ApiResponse<IReadOnlyList<SteamGameListing>>.Success(
            adjustedMatches, 
            200, 
            15, // simulated 15ms duration
            requestJson, 
            responseJson);

        return Task.FromResult(response);
    }
}
