using System.Globalization;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Xunit;

namespace SteamAPI.Tests
{
    public class LegacyParserRegressionTests
    {
        // Reconstructed documented structure for D-004 observed item with price "$9.99"
        // Note: Production HTML fixture reconstructed from documented Steam search suggest structure.
        private const string ReconstructedD004Fragment =
            "data-ds-appid=\"12345\" data-ds-tagids=\"[1,2]\" href=\"https://store.steampowered.com/app/12345/Portal_2/?snr=1_7_1_suggest__179\"><div class=\"match_name\">Portal 2</div><div class=\"match_img\"><img src=\"https://cdn.akamai.steamstatic.com/steam/apps/12345/capsule_sm_120.jpg?t=123\"></div><div class=\"match_price\">$9.99</div></a>";

        [Fact]
        public void Parse_ObservedPriceUsdNineNinetyNine_WithEnUsCulture_ShouldParseSuccessfully()
        {
            var cultureInfo = new CultureInfo("en-US");

            // Confirmed defect in legacy implementation:
            // double.Parse("$9.99", cultureInfo) throws System.FormatException
            var listing = new Listing(ReconstructedD004Fragment, cultureInfo);

            Assert.Equal("Portal 2", listing.Name);
            Assert.Equal("12345", listing.AppId);
            Assert.Equal("https://store.steampowered.com/app/12345/Portal_2", listing.StoreLink);
            Assert.Equal(sType.CostsMoney, listing.SaleType);
            Assert.Equal(9.99, listing.Price);
        }
    }
}
