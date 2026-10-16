using SteamAPI.Desktop.Models;

namespace SteamAPI.Desktop.Services;

public interface ISteamApiClient
{
    string BaseUrl { get; set; }
    int TimeoutSeconds { get; set; }
    event Action<ApiInspectionRecord>? OnInspectionRecordCreated;

    Task<ApiResponse<IReadOnlyList<SteamGameListing>>> SearchGamesAsync(
        string game, 
        int culture, 
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> CheckConnectionAsync(CancellationToken cancellationToken = default);
}
