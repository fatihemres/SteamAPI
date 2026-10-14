using HtmlAgilityPack;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Services.Abstraction;
using System.Globalization;
using System.Net;

namespace Infrastructer.Steam.Services.Implementation
{
    public class SteamService : ISteamService
    {
        private readonly ICultureService _cultureService;

        public SteamService(ICultureService cultureService)
        {
            _cultureService = cultureService;
        }

        public List<Listing> GetListings(string game, Culture culture)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var cultureInfo = _cultureService.GetCultureValue(culture);
            string response = new WebClient().DownloadString($"https://store.steampowered.com/search/suggest?term={game}&f=games&cc={cultureInfo.CultureShortName}&lang={cultureInfo.CultureName}&v=2286217");

            return ParseResponse(response, cultureInfo.CultureInfo);
        }

        internal static List<Listing> ParseResponse(string response, CultureInfo cultureInfo)
        {
            List<Listing> results = new List<Listing>();
            if (string.IsNullOrWhiteSpace(response))
                return results;

            var doc = new HtmlDocument();
            doc.LoadHtml(response);

            var listingNodes = doc.DocumentNode.Descendants("a")
                .Where(a => a.HasClass("match"))
                .ToList();

            if (!listingNodes.Any())
                return results;

            foreach (var node in listingNodes)
            {
                var listing = new Listing(node, cultureInfo);
                if (listing.IsValid)
                {
                    results.Add(listing);
                }
            }

            // If upstream provided match elements but none could be parsed, fail explicitly
            if (listingNodes.Any() && results.Count == 0)
            {
                throw new InvalidOperationException("Detected upstream Steam search markup failure; matched listing elements could not be parsed.");
            }

            return results;
        }
    }
}
