using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Exceptions;
using Infrastructer.Steam.Services.Implementation;
using SteamAPI.Models;
using Xunit;

namespace SteamAPI.Tests
{
    public class RiskB_MarkupDriftTests : IClassFixture<SteamApiWebApplicationFactory>
    {
        private readonly SteamApiWebApplicationFactory _factory;
        private static readonly CultureInfo UsCulture = new("en-US");

        public RiskB_MarkupDriftTests(SteamApiWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private const string ValidMatchHtml =
            "<a class=\"match ds_collapse_flag \" data-ds-appid=\"400\" href=\"https://store.steampowered.com/app/400/Portal/\">" +
            "<div class=\"match_name\">Portal</div>" +
            "<div class=\"match_img\"><img src=\"https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg\"></div>" +
            "<div class=\"match_price\">$9.99</div></a>";

        // Case A: Genuinely empty upstream response
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\t")]
        public void ScenarioA_GenuinelyEmptyUpstreamResponse_ReturnsEmptyList(string emptyHtml)
        {
            var results = SteamService.ParseResponse(emptyHtml, UsCulture);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        // Case B: Legitimate zero-result suggestion HTML
        [Theory]
        [InlineData("<!-- empty suggestion response -->")]
        [InlineData("<div class=\"no_matches\">No matches found</div>")]
        [InlineData("<div></div>")]
        public void ScenarioB_LegitimateZeroResultSuggestionHtml_ReturnsEmptyList(string zeroResultHtml)
        {
            var results = SteamService.ParseResponse(zeroResultHtml, UsCulture);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        // Case C: Valid Steam suggestion HTML containing recognized listings
        [Fact]
        public void ScenarioC_ValidSteamSuggestionHtml_ReturnsParsedListings()
        {
            var results = SteamService.ParseResponse(ValidMatchHtml, UsCulture);

            Assert.NotNull(results);
            Assert.Single(results);
            Assert.Equal("Portal", results[0].Name);
            Assert.Equal("400", results[0].AppId);
            Assert.Equal(9.99, results[0].Price);
            Assert.Equal(sType.CostsMoney, results[0].SaleType);
        }

        // Case D: Nonempty HTML with unexpected structure (structural DOM overhaul by Valve)
        [Fact]
        public void ScenarioD_NonemptyHtmlWithUnexpectedStructure_PreservesExistingEmptyListBehavior()
        {
            var modernizedMarkup =
                "<div class=\"steam-suggestion-card\" data-appid=\"400\">" +
                "  <span class=\"title\">Portal</span>" +
                "  <span class=\"price\">$9.99</span>" +
                "</div>";

            var results = SteamService.ParseResponse(modernizedMarkup, UsCulture);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        // Case E: Upstream error or challenge page returned with HTTP 200
        [Fact]
        public void ScenarioE_UpstreamChallengeOrErrorPage_PreservesExistingEmptyListBehavior()
        {
            var challengeHtml =
                "<!DOCTYPE html><html><head><title>Access Denied</title></head>" +
                "<body><h1>403 Forbidden - Access Denied</h1>" +
                "<p>Your request was blocked by Cloudflare/Akamai security.</p></body></html>";

            var results = SteamService.ParseResponse(challengeHtml, UsCulture);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        // Case F: Matched elements present but unparseable -> Throws typed SteamResponseParseException
        [Fact]
        public void ScenarioF_MatchedElementsPresentButUnparseable_ThrowsSteamResponseParseException()
        {
            // Scenario: Upstream markup contains <a class="match"> but attributes (appId/name) are corrupt or missing.
            // Requirement for 3C-B01: Must throw the dedicated SteamResponseParseException (not generic InvalidOperationException).
            var corruptMatchHtml =
                "<a class=\"match\" href=\"https://store.steampowered.com/category/games\">" +
                "  <div>Corrupt entry with no valid appId and no match_name div</div>" +
                "</a>";

            var ex = Assert.Throws<SteamResponseParseException>(() =>
                SteamService.ParseResponse(corruptMatchHtml, UsCulture));

            Assert.Contains("Detected upstream Steam search markup failure", ex.Message);
        }

        // Case F via Controller: When SteamResponseParseException reaches controller, returns HTTP 502 with ProblemDetails
        [Fact]
        public async Task ScenarioF_MatchedElementsUnparseable_ViaController_Returns502BadGatewayWithProblemDetails()
        {
            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
                Task.FromException<List<Listing>>(new SteamResponseParseException("Detected upstream Steam search markup failure; matched listing elements could not be parsed.")));

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            Assert.True(contentType == "application/problem+json" || contentType == "application/json");

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(502, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Bad Gateway", doc.RootElement.GetProperty("title").GetString());
            Assert.Contains("unrecognized markup", doc.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

            // Verify sanitization: no internal exception types or stack traces in wire payload
            Assert.DoesNotContain("SteamResponseParseException", rawJson);
            Assert.DoesNotContain("stackTrace", rawJson, StringComparison.OrdinalIgnoreCase);
        }

        // Precision check: Unrelated InvalidOperationException must NOT be caught as 502
        [Fact]
        public async Task Scenario_UnrelatedInvalidOperationException_ViaController_IsNotConvertedTo502()
        {
            // Requirement: SteamController must NOT use a broad catch (InvalidOperationException).
            // Any unrelated InvalidOperationException must remain an unhandled server error (HTTP 500).
            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
                Task.FromException<List<Listing>>(new InvalidOperationException("Unrelated internal programming error.")));

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
    }
}
