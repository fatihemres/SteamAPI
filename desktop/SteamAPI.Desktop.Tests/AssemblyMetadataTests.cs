using System.Reflection;
using SteamAPI.Desktop.ViewModels;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class AssemblyMetadataTests
{
    [Fact]
    public void DesktopAssembly_ContainsExpectedAuthorAndCopyrightMetadata()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;

        var companyAttr = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
        Assert.NotNull(companyAttr);
        Assert.Equal("Fatih Emre Sarman", companyAttr.Company);

        var copyrightAttr = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
        Assert.NotNull(copyrightAttr);
        Assert.Contains("Copyright © 2026 Fatih Emre Sarman", copyrightAttr.Copyright);

        var productAttr = assembly.GetCustomAttribute<AssemblyProductAttribute>();
        Assert.NotNull(productAttr);
        Assert.Equal("SteamAPI Desktop", productAttr.Product);

        var descriptionAttr = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
        Assert.NotNull(descriptionAttr);
        Assert.Contains("SteamAPI", descriptionAttr.Description);
    }
}
