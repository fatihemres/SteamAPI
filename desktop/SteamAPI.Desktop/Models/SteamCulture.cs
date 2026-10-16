namespace SteamAPI.Desktop.Models;

/// <summary>
/// Represents one of the 3 supported regional store cultures in SteamAPI v0.2.0.
/// </summary>
public class SteamCulture
{
    public int Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string LanguageName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string FlagEmoji { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public static readonly IReadOnlyList<SteamCulture> SupportedCultures = new List<SteamCulture>
    {
        new()
        {
            Id = 2,
            DisplayName = "United States (English)",
            LanguageName = "english",
            CountryCode = "US",
            FlagEmoji = "🇺🇸",
            Description = "US Store (cc=us, lang=english)"
        },
        new()
        {
            Id = 1,
            DisplayName = "Turkey (Turkish)",
            LanguageName = "turkish",
            CountryCode = "TR",
            FlagEmoji = "🇹🇷",
            Description = "Turkish Store (cc=tr, lang=turkish)"
        },
        new()
        {
            Id = 3,
            DisplayName = "Spain (Spanish)",
            LanguageName = "spanish",
            CountryCode = "ES",
            FlagEmoji = "🇪🇸",
            Description = "Spanish Store (cc=es, lang=spanish)"
        }
    };
}
