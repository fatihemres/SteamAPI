using SteamAPI.Desktop.Models;

namespace SteamAPI.Desktop.Services;

public interface IDemoDataService
{
    Task<ApiResponse<IReadOnlyList<SteamGameListing>>> SearchSyntheticGamesAsync(
        string query, 
        int culture, 
        CancellationToken cancellationToken = default);
}
