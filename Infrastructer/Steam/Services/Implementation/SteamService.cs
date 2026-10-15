using HtmlAgilityPack;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Exceptions;
using Infrastructer.Steam.Services.Abstraction;
using System.Globalization;

namespace Infrastructer.Steam.Services.Implementation
{
    public class SteamService : ISteamService
    {
        private readonly HttpClient _httpClient;
        private readonly ICultureService _cultureService;

        public SteamService(HttpClient httpClient, ICultureService cultureService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _cultureService = cultureService ?? throw new ArgumentNullException(nameof(cultureService));
        }

        public async Task<List<Listing>> GetListingsAsync(string game, Culture culture, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cultureInfo = _cultureService.GetCultureValue(culture);
            var encodedTerm = Uri.EscapeDataString(game ?? string.Empty);
            var relativePath = $"search/suggest?term={encodedTerm}&f=games&cc={cultureInfo.CultureShortName}&lang={cultureInfo.CultureName}&v=2286217";

            var requestUri = _httpClient.BaseAddress != null
                ? new Uri(relativePath, UriKind.Relative)
                : new Uri($"https://store.steampowered.com/{relativePath}", UriKind.Absolute);

            using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseResponse(responseContent, cultureInfo.CultureInfo);
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
                throw new SteamResponseParseException("Detected upstream Steam search markup failure; matched listing elements could not be parsed.");
            }

            return results;
        }
    }
}
