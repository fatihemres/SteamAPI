using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;

namespace Infrastructer.Steam.Services.Abstraction
{
    public interface ISteamService
    {

        Task<List<Listing>> GetListingsAsync(string game, Culture culture, CancellationToken cancellationToken = default);
    }
}
