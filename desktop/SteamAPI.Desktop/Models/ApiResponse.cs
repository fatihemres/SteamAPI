namespace SteamAPI.Desktop.Models;

/// <summary>
/// Structured client response encapsulating data, HTTP status, and RFC 9457 ProblemDetails.
/// </summary>
/// <typeparam name="T">Payload data type.</typeparam>
public class ApiResponse<T>
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorDetail { get; init; }
    public Dictionary<string, string[]>? ValidationErrors { get; init; }
    public long DurationMs { get; init; }
    public string RawRequestJson { get; init; } = string.Empty;
    public string RawResponseJson { get; init; } = string.Empty;

    public static ApiResponse<T> Success(T data, int statusCode = 200, long durationMs = 0) =>
        Success(data, statusCode, durationMs, string.Empty, string.Empty);

    public static ApiResponse<T> Success(T data, int statusCode, long durationMs, string requestJson, string responseJson) =>
        new()
        {
            IsSuccess = true,
            StatusCode = statusCode,
            Data = data,
            DurationMs = durationMs,
            RawRequestJson = requestJson,
            RawResponseJson = responseJson
        };

    public static ApiResponse<T> Failure(int statusCode, string message, string? detail = null, long durationMs = 0) =>
        Failure(statusCode, message, detail, durationMs, string.Empty, string.Empty);

    public static ApiResponse<T> Failure(int statusCode, string message, string? detail, long durationMs, string requestJson, string responseJson, Dictionary<string, string[]>? validationErrors = null) =>
        new()
        {
            IsSuccess = false,
            StatusCode = statusCode,
            ErrorMessage = message,
            ErrorDetail = detail,
            ValidationErrors = validationErrors,
            DurationMs = durationMs,
            RawRequestJson = requestJson,
            RawResponseJson = responseJson
        };
}
