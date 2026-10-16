namespace SteamAPI.Desktop.Models;

/// <summary>
/// Detailed inspection record capturing HTTP telemetry for the API Inspector panel.
/// </summary>
public class ApiInspectionRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string RequestUrl { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = "POST";
    public string RequestBodyJson { get; init; } = string.Empty;
    public int ResponseStatusCode { get; init; }
    public string ResponseStatusText { get; init; } = string.Empty;
    public long DurationMs { get; init; }
    public string ResponseBodyJson { get; init; } = string.Empty;
    public bool IsSuccess => ResponseStatusCode >= 200 && ResponseStatusCode < 300;
    public string? ErrorMessage { get; init; }
    public int? ItemCount { get; init; }
    public bool IsDemoMode { get; init; }

    public string StatusBadgeColor => ResponseStatusCode switch
    {
        200 => "#238636", // Success Green
        400 => "#D29922", // Validation Yellow
        502 => "#F85149", // Upstream Error Red
        504 => "#DA3633", // Gateway Timeout Red
        _ => "#8B949E"    // Muted Gray / Connection Failed
    };

    public string DurationFormatted => $"{DurationMs} ms";
    public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");
}
