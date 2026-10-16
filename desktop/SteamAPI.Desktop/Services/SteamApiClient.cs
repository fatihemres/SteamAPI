using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SteamAPI.Desktop.Models;

namespace SteamAPI.Desktop.Services;

public class SteamApiClient : ISteamApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string BaseUrl { get; set; } = "http://localhost:5271";
    public int TimeoutSeconds { get; set; } = 30;
    public event Action<ApiInspectionRecord>? OnInspectionRecordCreated;

    public SteamApiClient(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
    }

    public SteamApiClient(string baseUrl, int timeoutSeconds = 30, HttpClient? httpClient = null)
        : this(httpClient)
    {
        BaseUrl = baseUrl;
        TimeoutSeconds = timeoutSeconds;
    }

    public async Task<ApiResponse<IReadOnlyList<SteamGameListing>>> SearchGamesAsync(
        string game, 
        int culture, 
        CancellationToken cancellationToken = default)
    {
        var requestPayload = new
        {
            game = game ?? string.Empty,
            culture
        };

        var requestJson = JsonSerializer.Serialize(requestPayload, JsonOptions);
        var targetUri = new Uri(new Uri(BaseUrl.TrimEnd('/') + "/"), "steam");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, targetUri)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            var durationMs = stopwatch.ElapsedMilliseconds;

            var responseRaw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var prettyResponseJson = FormatJsonIfPossible(responseRaw);

            if (response.IsSuccessStatusCode)
            {
                var listings = JsonSerializer.Deserialize<List<SteamGameListing>>(responseRaw, JsonOptions) 
                               ?? new List<SteamGameListing>();

                var record = new ApiInspectionRecord
                {
                    RequestUrl = targetUri.ToString(),
                    HttpMethod = "POST",
                    RequestBodyJson = requestJson,
                    ResponseStatusCode = (int)response.StatusCode,
                    ResponseStatusText = $"{(int)response.StatusCode} {response.ReasonPhrase}",
                    DurationMs = durationMs,
                    ResponseBodyJson = prettyResponseJson,
                    ItemCount = listings.Count,
                    IsDemoMode = false
                };
                OnInspectionRecordCreated?.Invoke(record);

                return ApiResponse<IReadOnlyList<SteamGameListing>>.Success(
                    listings, 
                    (int)response.StatusCode, 
                    durationMs, 
                    requestJson, 
                    prettyResponseJson);
            }
            else
            {
                // Error handling for 400, 502, 504 and others
                string title = response.ReasonPhrase ?? "API Error";
                string? detail = null;
                Dictionary<string, string[]>? errors = null;

                try
                {
                    using var doc = JsonDocument.Parse(responseRaw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("title", out var titleEl))
                    {
                        title = titleEl.GetString() ?? title;
                    }
                    if (root.TryGetProperty("detail", out var detailEl))
                    {
                        detail = detailEl.GetString();
                    }
                    if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Object)
                    {
                        errors = new Dictionary<string, string[]>();
                        foreach (var prop in errorsEl.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                var errList = new List<string>();
                                foreach (var item in prop.Value.EnumerateArray())
                                {
                                    if (item.GetString() is string s) errList.Add(s);
                                }
                                errors[prop.Name] = errList.ToArray();
                            }
                        }
                    }
                }
                catch
                {
                    // If not valid JSON, use raw string
                    detail = responseRaw;
                }

                var record = new ApiInspectionRecord
                {
                    RequestUrl = targetUri.ToString(),
                    HttpMethod = "POST",
                    RequestBodyJson = requestJson,
                    ResponseStatusCode = (int)response.StatusCode,
                    ResponseStatusText = $"{(int)response.StatusCode} {response.ReasonPhrase}",
                    DurationMs = durationMs,
                    ResponseBodyJson = prettyResponseJson,
                    ErrorMessage = $"{title}{(detail != null ? $": {detail}" : "")}",
                    ItemCount = 0,
                    IsDemoMode = false
                };
                OnInspectionRecordCreated?.Invoke(record);

                return ApiResponse<IReadOnlyList<SteamGameListing>>.Failure(
                    (int)response.StatusCode, 
                    title, 
                    detail, 
                    durationMs, 
                    requestJson, 
                    prettyResponseJson, 
                    errors);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var durationMs = stopwatch.ElapsedMilliseconds;
            var errorMsg = $"Request timed out after {TimeoutSeconds} seconds connecting to {targetUri}.";
            
            var record = new ApiInspectionRecord
            {
                RequestUrl = targetUri.ToString(),
                HttpMethod = "POST",
                RequestBodyJson = requestJson,
                ResponseStatusCode = 504,
                ResponseStatusText = "504 Gateway Timeout (Client-Enforced)",
                DurationMs = durationMs,
                ResponseBodyJson = $"{{\n  \"error\": \"Client-side timeout reached ({TimeoutSeconds}s).\"\n}}",
                ErrorMessage = errorMsg,
                IsDemoMode = false
            };
            OnInspectionRecordCreated?.Invoke(record);

            return ApiResponse<IReadOnlyList<SteamGameListing>>.Failure(
                504, 
                "Timeout", 
                errorMsg, 
                durationMs, 
                requestJson, 
                record.ResponseBodyJson);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var durationMs = stopwatch.ElapsedMilliseconds;
            var errorMsg = $"Unable to connect to SteamAPI at '{targetUri}': {ex.Message}";

            var record = new ApiInspectionRecord
            {
                RequestUrl = targetUri.ToString(),
                HttpMethod = "POST",
                RequestBodyJson = requestJson,
                ResponseStatusCode = 0,
                ResponseStatusText = "Connection Failed",
                DurationMs = durationMs,
                ResponseBodyJson = $"{{\n  \"error\": \"{JsonEncodedText.Encode(ex.Message)}\"\n}}",
                ErrorMessage = errorMsg,
                IsDemoMode = false
            };
            OnInspectionRecordCreated?.Invoke(record);

            return ApiResponse<IReadOnlyList<SteamGameListing>>.Failure(
                0, 
                "Connection Failed", 
                errorMsg, 
                durationMs, 
                requestJson, 
                record.ResponseBodyJson);
        }
    }

    public async Task<ApiResponse<bool>> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        // Tests connection to POST /steam with a harmless query "Portal"
        var result = await SearchGamesAsync("Portal", 2, cancellationToken).ConfigureAwait(false);
        return new ApiResponse<bool>
        {
            IsSuccess = result.IsSuccess,
            StatusCode = result.StatusCode,
            Data = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            ErrorDetail = result.ErrorDetail,
            DurationMs = result.DurationMs,
            RawRequestJson = result.RawRequestJson,
            RawResponseJson = result.RawResponseJson
        };
    }

    private static string FormatJsonIfPossible(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, JsonOptions);
        }
        catch
        {
            return raw;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
