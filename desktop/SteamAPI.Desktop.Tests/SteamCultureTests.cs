using SteamAPI.Desktop.Models;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class SteamCultureTests
{
    [Fact]
    public void SupportedCultures_ContainsExpectedThreeRegionalCultures()
    {
        var cultures = SteamCulture.SupportedCultures;

        Assert.Equal(3, cultures.Count);

        var tr = cultures.FirstOrDefault(c => c.Id == 1);
        Assert.NotNull(tr);
        Assert.Equal("TR", tr.CountryCode);
        Assert.Equal("turkish", tr.LanguageName);

        var us = cultures.FirstOrDefault(c => c.Id == 2);
        Assert.NotNull(us);
        Assert.Equal("US", us.CountryCode);
        Assert.Equal("english", us.LanguageName);

        var es = cultures.FirstOrDefault(c => c.Id == 3);
        Assert.NotNull(es);
        Assert.Equal("ES", es.CountryCode);
        Assert.Equal("spanish", es.LanguageName);
    }
}
