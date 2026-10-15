using System.Globalization;
using System.Text.Json;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Exceptions;
using Infrastructer.Steam.Services.Implementation;
using Xunit;

namespace SteamAPI.Tests
{
    public class ListingParserTests
    {
        // Documented structure reconstructed for D-004 observed item with price "$9.99"
        // Note: Production HTML fixture reconstructed from documented Steam search suggest structure.
        private const string ReconstructedD004Fragment =
            "<a class=\"match ds_collapse_flag \" data-ds-appid=\"12345\" data-ds-tagids=\"[1,2]\" href=\"https://store.steampowered.com/app/12345/Portal_2/?snr=1_7_1_suggest__179\"><div class=\"match_name\">Portal 2</div><div class=\"match_img\"><img src=\"https://cdn.akamai.steamstatic.com/steam/apps/12345/capsule_sm_120.jpg?t=123\"></div><div class=\"match_price\">$9.99</div></a>";

        // Synthetic discount fixture: clearly labeled as unverified against current live Steam markup.
        private const string SyntheticDiscountFragment =
            "<a class=\"match ds_collapse_flag \" data-ds-appid=\"54321\" href=\"https://store.steampowered.com/app/54321/Half_Life_2/\"><div class=\"match_name\">Half-Life 2</div><div class=\"match_img\"><img src=\"https://cdn.akamai.steamstatic.com/steam/apps/54321/capsule_sm_120.jpg\"></div><div class=\"match_price\"><span class=\"discount_pct\">-50%</span><span class=\"strike\">$19.99</span>$9.99</div></a>";

        [Fact]
        public void Parse_ObservedPriceUsdNineNinetyNine_WithEnUsCulture_ShouldParseAsNineNinetyNine()
        {
            var cultureInfo = new CultureInfo("en-US");
            var listing = new Listing(ReconstructedD004Fragment, cultureInfo);

            Assert.Equal("Portal 2", listing.Name);
            Assert.Equal("12345", listing.AppId);
            Assert.Equal("https://store.steampowered.com/app/12345/Portal_2", listing.StoreLink);
            Assert.Equal("https://cdn.akamai.steamstatic.com/steam/apps/12345/capsule_sm_120.jpg", listing.ImageLink);
            Assert.Equal(sType.CostsMoney, listing.SaleType);
            Assert.Equal(9.99, listing.Price);
        }

        [Fact]
        public void Parse_SyntheticTurkishCultureUsdPrice_ShouldParseAsNineNinetyNine_NeverNineHundredNinetyNine()
        {
            // Synthetic test case:
            // Culture: tr_TR (where '.' is thousands separator and ',' is decimal separator)
            // Displayed price: $9.99
            // Expected: 9.99, NEVER 999
            var cultureInfo = new CultureInfo("tr-TR");
            var listing = new Listing(ReconstructedD004Fragment, cultureInfo);

            Assert.Equal(sType.CostsMoney, listing.SaleType);
            Assert.Equal(9.99, listing.Price);
            Assert.NotEqual(999.0, listing.Price);
        }

        [Theory]
        [InlineData("9,99€", "es-ES", 9.99)]
        [InlineData("9,99 €", "es-ES", 9.99)]
        [InlineData("19,99 TL", "tr-TR", 19.99)]
        [InlineData("₺19,99", "tr-TR", 19.99)]
        [InlineData("$1,299.99", "en-US", 1299.99)]
        public void ParsePrice_KnownFormats_ShouldParseCorrectly(string priceStr, string cultureCode, double expected)
        {
            var culture = new CultureInfo(cultureCode);
            var parsed = Listing.ParsePrice(priceStr, culture);

            Assert.NotNull(parsed);
            Assert.Equal(expected, parsed.Value);
        }

        [Theory]
        [InlineData("Free")]
        [InlineData("Free to Play")]
        [InlineData("Free-to-Play")]
        [InlineData("play for free")]
        [InlineData("ücretsiz")]
        [InlineData("gratis")]
        public void Parse_ConservativeFreeToPlayLabels_ShouldSetFreeToPlay(string freeLabel)
        {
            string html = $"<a class=\"match\" data-ds-appid=\"730\" href=\"https://store.steampowered.com/app/730/CS/\"><div class=\"match_name\">CS</div><div class=\"match_img\"><img src=\"https://img.jpg\"></div><div class=\"match_price\">{freeLabel}</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.Equal(sType.FreeToPlay, listing.SaleType);
            Assert.Null(listing.Price);
        }

        [Theory]
        [InlineData("Freedom Fighters")]
        [InlineData("Carefree Days")]
        [InlineData("Freezing Point")]
        public void Parse_ArbitraryPhrasesContainingWordFree_ShouldNotBeClassifiedAsFreeToPlay(string nonFreePrice)
        {
            string html = $"<a class=\"match\" data-ds-appid=\"999\" href=\"https://store.steampowered.com/app/999/Game/\"><div class=\"match_name\">Game</div><div class=\"match_img\"><img src=\"https://img.jpg\"></div><div class=\"match_price\">{nonFreePrice}</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.NotEqual(sType.FreeToPlay, listing.SaleType);
            Assert.Equal(sType.NotAvailable, listing.SaleType);
            Assert.Null(listing.Price);
        }

        [Fact]
        public void Parse_DiscountedMarkup_ShouldPreferFinalEffectivePrice()
        {
            // Synthetic discount fixture: prefers final price $9.99 over strike-through $19.99
            var cultureInfo = new CultureInfo("en-US");
            var listing = new Listing(SyntheticDiscountFragment, cultureInfo);

            Assert.Equal(sType.CostsMoney, listing.SaleType);
            Assert.Equal(9.99, listing.Price);
        }

        [Fact]
        public void Parse_HtmlEntitiesInGameName_ShouldDecodeProperly()
        {
            string html = "<a class=\"match\" data-ds-appid=\"10\" href=\"https://store.steampowered.com/app/10/CS/\"><div class=\"match_name\">Counter-Strike&#58; Global Offensive &amp; Tom Clancy&#39;s</div><div class=\"match_img\"><img src=\"https://img.jpg\"></div><div class=\"match_price\">$9.99</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.Equal("Counter-Strike: Global Offensive & Tom Clancy's", listing.Name);
        }

        [Fact]
        public void Parse_MalformedAndEmptyHtml_ShouldHandleSafelyWithoutCrashing()
        {
            var cultureInfo = new CultureInfo("en-US");

            var emptyListing = new Listing("", cultureInfo);
            Assert.False(emptyListing.IsValid);
            Assert.Equal(sType.NotAvailable, emptyListing.SaleType);
            Assert.Null(emptyListing.Price);

            var brokenHtml = "<a class=\"match\"><div>Broken</div></a>";
            var listing = new Listing(brokenHtml, cultureInfo);
            Assert.False(listing.IsValid);
            Assert.Equal(sType.NotAvailable, listing.SaleType);
            Assert.Null(listing.Price);
        }

        [Fact]
        public void Contract_Listing_ShouldOnlySerializeSixApprovedPublicProperties()
        {
            var cultureInfo = new CultureInfo("en-US");
            var listing = new Listing(ReconstructedD004Fragment, cultureInfo);

            string json = JsonSerializer.Serialize(listing);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Only the 6 pre-existing public properties must be present
            var propertyNames = root.EnumerateObject().Select(p => p.Name).ToList();

            Assert.Equal(6, propertyNames.Count);
            Assert.Contains(propertyNames, n => n.Equals("Name", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(propertyNames, n => n.Equals("StoreLink", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(propertyNames, n => n.Equals("ImageLink", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(propertyNames, n => n.Equals("Price", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(propertyNames, n => n.Equals("SaleType", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(propertyNames, n => n.Equals("AppId", StringComparison.OrdinalIgnoreCase));

            // IsValid or any other internal field MUST NOT be serialized
            Assert.DoesNotContain(propertyNames, n => n.Equals("IsValid", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseResponse_GenuinelyEmptyResponse_ShouldReturnEmptyList()
        {
            var results = SteamService.ParseResponse("", new CultureInfo("en-US"));
            Assert.Empty(results);
        }

        [Fact]
        public void ParseResponse_ZeroMatchesResponse_ShouldReturnEmptyList()
        {
            string html = "<div id=\"search_suggestion_contents\"><div>No results</div></div>";
            var results = SteamService.ParseResponse(html, new CultureInfo("en-US"));
            Assert.Empty(results);
        }

        [Fact]
        public void ParseResponse_MalformedListingAlongsideValidListing_ShouldFilterMalformedAndReturnValid()
        {
            string html = @"<a class=""match"" data-ds-appid=""123"" href=""https://store.steampowered.com/app/123/Game/""><div class=""match_name"">Game</div><div class=""match_price"">$9.99</div></a>
<a class=""match""><div>Malformed without name or id</div></a>";
            var results = SteamService.ParseResponse(html, new CultureInfo("en-US"));

            Assert.Single(results);
            Assert.Equal("123", results[0].AppId);
            Assert.Equal("Game", results[0].Name);
        }

        [Fact]
        public void ParseResponse_AllMatchedListingsUnparseable_ShouldThrowSteamResponseParseException()
        {
            // Upstream structure changed such that match elements exist, but not a single element can be parsed
            string html = @"<a class=""match""><div>Changed upstream completely</div></a>
<a class=""match""><div>Another unparseable element</div></a>";

            var ex = Assert.Throws<SteamResponseParseException>(() =>
                SteamService.ParseResponse(html, new CultureInfo("en-US")));

            Assert.Contains("Detected upstream Steam search markup failure", ex.Message);
        }

        // --- M-001A-QA2 Confirmed Finding A: Single-Digit Prices and Partial Numeric Matches ---

        [Theory]
        [InlineData("$5", 5.0)]
        [InlineData("$9.99", 9.99)]
        [InlineData("$10", 10.0)]
        public void ParsePrice_SingleDigitAndCommonUsdPrices_ShouldParseCorrectly(string input, double expected)
        {
            var culture = new CultureInfo("en-US");
            var result = Listing.ParsePrice(input, culture);

            Assert.NotNull(result);
            Assert.Equal(expected, result.Value);
        }

        [Theory]
        [InlineData("Save 50%")]
        [InlineData("Coming 2027")]
        [InlineData("Freedom Fighters")]
        [InlineData("Carefree Days")]
        public void ParsePrice_NonPriceTextContainingNumbersOrWords_ShouldReturnNull(string nonPriceText)
        {
            var culture = new CultureInfo("en-US");
            var result = Listing.ParsePrice(nonPriceText, culture);

            Assert.Null(result);
        }

        // --- M-001A-QA2 Confirmed Finding B: Incorrect Separator Interpretation ---

        [Theory]
        [InlineData("$9.99", "en-US", 9.99)]
        [InlineData("$1,299.99", "en-US", 1299.99)]
        [InlineData("9,99 €", "es-ES", 9.99)]
        [InlineData("19,99 TL", "tr-TR", 19.99)]
        [InlineData("€1,234.56", "es-ES", 1234.56)]
        public void ParsePrice_ValidGrammarPrices_ShouldParseCorrectly(string priceStr, string cultureCode, double expected)
        {
            var culture = new CultureInfo(cultureCode);
            var result = Listing.ParsePrice(priceStr, culture);

            Assert.NotNull(result);
            Assert.Equal(expected, result.Value);
        }

        [Fact]
        public void ParsePrice_UsdPriceWithTurkishRequestCulture_ShouldParseAsNineNinetyNine()
        {
            // Requiring that $9.99 with tr-TR request culture parses as 9.99, NEVER 999
            var culture = new CultureInfo("tr-TR");
            var result = Listing.ParsePrice("$9.99", culture);

            Assert.NotNull(result);
            Assert.Equal(9.99, result.Value);
        }

        [Fact]
        public void ParsePrice_AmbiguousDollarPriceWithCommaDecimal_ShouldBeRejectedAsNull()
        {
            // "$9,99" is ambiguous or unsupported under USD period-decimal rules
            var culture = new CultureInfo("en-US");
            var result = Listing.ParsePrice("$9,99", culture);

            Assert.Null(result);
        }

        [Fact]
        public void ParsePrice_EuroWithAmbiguousSeparators_ShouldBeRejectedAsNull()
        {
            // Mixed or malformed separators like €1,2.3 or €1.2,3 must be rejected
            var culture = new CultureInfo("es-ES");
            Assert.Null(Listing.ParsePrice("€1,2.3", culture));
            Assert.Null(Listing.ParsePrice("€1.2,3", culture));
        }

        // --- M-001A-QA2 Confirmed Finding C: Incomplete Listing Validation ---

        [Fact]
        public void Listing_Validation_ValidAppIdAndName_ShouldBeValid()
        {
            string html = @"<a class=""match"" data-ds-appid=""730"" href=""https://store.steampowered.com/app/730/CSGO/""><div class=""match_name"">Counter-Strike</div><div class=""match_price"">Free</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.True(listing.IsValid);
            Assert.Equal("730", listing.AppId);
            Assert.Equal("Counter-Strike", listing.Name);
        }

        [Fact]
        public void Listing_Validation_MissingAppIdWithValidName_ShouldBeInvalid()
        {
            string html = @"<a class=""match"" href=""https://store.steampowered.com/""><div class=""match_name"">Game Without AppId</div><div class=""match_price"">$9.99</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.False(listing.IsValid);
            Assert.Empty(listing.AppId);
            Assert.Equal("Game Without AppId", listing.Name);
        }

        [Fact]
        public void Listing_Validation_ValidAppIdWithMissingName_ShouldBeInvalid()
        {
            string html = @"<a class=""match"" data-ds-appid=""730"" href=""https://store.steampowered.com/app/730/""><div class=""match_price"">Free</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.False(listing.IsValid);
            Assert.Equal("730", listing.AppId);
            Assert.Empty(listing.Name);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("-5")]
        [InlineData("0")]
        [InlineData("12a34")]
        public void Listing_Validation_NonNumericOrMalformedAppId_ShouldBeInvalid(string malformedId)
        {
            string html = $@"<a class=""match"" data-ds-appid=""{malformedId}"" href=""https://store.steampowered.com/""><div class=""match_name"">Game</div><div class=""match_price"">$9.99</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.False(listing.IsValid);
        }

        [Fact]
        public void Listing_Validation_MissingBothFields_ShouldBeInvalid()
        {
            string html = @"<a class=""match""><div class=""match_price"">$9.99</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.False(listing.IsValid);
            Assert.Empty(listing.AppId);
            Assert.Empty(listing.Name);
        }

        [Fact]
        public void Listing_Validation_AppIdFallbackFromHref_ShouldBeExtractedAndValid()
        {
            // Missing data-ds-appid, but href contains /app/400/
            string html = @"<a class=""match"" href=""https://store.steampowered.com/app/400/Portal/""><div class=""match_name"">Portal</div><div class=""match_price"">$9.99</div></a>";
            var listing = new Listing(html, new CultureInfo("en-US"));

            Assert.True(listing.IsValid);
            Assert.Equal("400", listing.AppId);
            Assert.Equal("Portal", listing.Name);
        }
    }
}
