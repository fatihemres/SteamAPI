using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Exceptions;
using Infrastructer.Steam.Services.Abstraction;
using Infrastructer.Steam.Services.Implementation;
using Xunit;

namespace SteamAPI.Tests
{
    public class SteamServiceHttpTests
    {
        private const string SampleValidHtml =
            "<a class=\"match ds_collapse_flag \" data-ds-appid=\"12345\" data-ds-tagids=\"[1,2]\" href=\"https://store.steampowered.com/app/12345/Portal_2/?snr=1_7_1_suggest__179\"><div class=\"match_name\">Portal 2</div><div class=\"match_img\"><img src=\"https://cdn.akamai.steamstatic.com/steam/apps/12345/capsule_sm_120.jpg?t=123\"></div><div class=\"match_price\">$9.99</div></a>";

        private class TestHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _handler(request, cancellationToken);
            }
        }

        private class StallingHttpContent : HttpContent
        {
            private readonly int _delayMs;

            public StallingHttpContent(int delayMs = 10000)
            {
                _delayMs = delayMs;
                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                return SerializeToStreamAsync(stream, context, CancellationToken.None);
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                await Task.Delay(_delayMs, cancellationToken);
                var bytes = System.Text.Encoding.UTF8.GetBytes(SampleValidHtml);
                await stream.WriteAsync(bytes, cancellationToken);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = -1;
                return false;
            }
        }

        private static (SteamService service, List<HttpRequestMessage> capturedRequests) CreateTestService(
            Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory,
            Uri? baseAddress = null)
        {
            var capturedRequests = new List<HttpRequestMessage>();
            var handler = new TestHttpMessageHandler((req, ct) =>
            {
                capturedRequests.Add(req);
                return Task.FromResult(responseFactory(req, ct));
            });

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = baseAddress ?? new Uri("https://store.steampowered.com/")
            };

            var cultureService = new CultureService();
            var service = new SteamService(httpClient, cultureService);
            return (service, capturedRequests);
        }

        // ====================================================================
        // A. REQUEST CONSTRUCTION AND URL ENCODING TESTS
        // ====================================================================

        [Fact]
        public async Task RequestConstruction_StandardQuery_ConstructsCorrectUrlAndParameters()
        {
            var (service, requests) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });

            var result = await service.GetListingsAsync("portal", Culture.en_US);

            Assert.Single(requests);
            var req = requests[0];
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://store.steampowered.com/search/suggest?term=portal&f=games&cc=us&lang=english&v=2286217", req.RequestUri?.ToString());
            Assert.Single(result);
        }

        [Theory]
        [InlineData(Culture.tr_TR, "tr", "turkish")]
        [InlineData(Culture.en_US, "us", "english")]
        [InlineData(Culture.es_ES, "es", "spanish")]
        public async Task RequestConstruction_AllSupportedCultures_ConstructsCorrectCultureParameters(
            Culture culture, string expectedCc, string expectedLang)
        {
            var (service, requests) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });

            await service.GetListingsAsync("game", culture);

            Assert.Single(requests);
            var uri = requests[0].RequestUri?.ToString();
            Assert.NotNull(uri);
            Assert.Contains($"cc={expectedCc}", uri);
            Assert.Contains($"lang={expectedLang}", uri);
            Assert.Contains("f=games", uri);
            Assert.Contains("v=2286217", uri);
        }

        [Theory]
        [InlineData("Portal 2", "Portal%202")]
        [InlineData("Half-Life", "Half-Life")]
        [InlineData("A&B", "A%26B")]
        [InlineData("C++", "C%2B%2B")]
        [InlineData("Türkçe oyun", "T%C3%BCrk%C3%A7e%20oyun")]
        [InlineData("game?name#1=2&x%", "game%3Fname%231%3D2%26x%25")]
        [InlineData("Space   Multiple", "Space%20%20%20Multiple")]
        public async Task RequestConstruction_SpecialCharacters_SafelyEncodesSearchTerm(
            string rawInput, string expectedEncoded)
        {
            var (service, requests) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });

            await service.GetListingsAsync(rawInput, Culture.en_US);

            Assert.Single(requests);
            var uri = requests[0].RequestUri?.PathAndQuery;
            Assert.NotNull(uri);
            Assert.Contains($"term={expectedEncoded}&", uri);
        }

        [Fact]
        public async Task RequestConstruction_NullOrEmptySearchTerm_DoesNotThrowAndEncodesEmptyTerm()
        {
            var (service, requests) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });

            await service.GetListingsAsync(string.Empty, Culture.en_US);
            Assert.Contains("term=&", requests[0].RequestUri?.ToString());

            await service.GetListingsAsync(null!, Culture.en_US);
            Assert.Contains("term=&", requests[1].RequestUri?.ToString());
        }

        [Fact]
        public async Task RequestConstruction_HttpClientWithoutBaseAddress_UsesAbsoluteFallbackUrl()
        {
            var capturedRequests = new List<HttpRequestMessage>();
            var handler = new TestHttpMessageHandler((req, ct) =>
            {
                capturedRequests.Add(req);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });
            });

            // HttpClient instantiated with null BaseAddress
            var httpClient = new HttpClient(handler);
            var service = new SteamService(httpClient, new CultureService());

            await service.GetListingsAsync("portal", Culture.en_US);

            Assert.Single(capturedRequests);
            Assert.Equal("https://store.steampowered.com/search/suggest?term=portal&f=games&cc=us&lang=english&v=2286217",
                capturedRequests[0].RequestUri?.ToString());
        }

        // ====================================================================
        // B. SUCCESSFUL HTTP RESPONSES AND PARSER INTEGRATION
        // ====================================================================

        [Fact]
        public async Task GetListingsAsync_Successful200Response_ReturnsParsedListings()
        {
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });

            var result = await service.GetListingsAsync("portal", Culture.en_US);

            Assert.NotNull(result);
            Assert.Single(result);
            var listing = result[0];
            Assert.Equal("Portal 2", listing.Name);
            Assert.Equal("12345", listing.AppId);
            Assert.Equal(9.99, listing.Price);
            Assert.Equal(sType.CostsMoney, listing.SaleType);
        }

        [Fact]
        public async Task GetListingsAsync_EmptyResponseContent_ReturnsEmptyList()
        {
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });

            var result = await service.GetListingsAsync("unknown", Culture.en_US);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetListingsAsync_ZeroMatchesHtml_ReturnsEmptyList()
        {
            var zeroMatchesHtml = "<div>No matches found</div>";
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(zeroMatchesHtml) });

            var result = await service.GetListingsAsync("unknown", Culture.en_US);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetListingsAsync_MatchedNodesUnparseable_ThrowsSteamResponseParseException()
        {
            // If markup contains matched nodes with class 'match' but none can be parsed into valid listings,
            // existing ParseResponse behavior explicitly throws SteamResponseParseException.
            var unparseableMatchHtml = "<a class=\"match\">Malformed without appId or name</a>";
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(unparseableMatchHtml) });

            await Assert.ThrowsAsync<SteamResponseParseException>(() =>
                service.GetListingsAsync("malformed", Culture.en_US));
        }

        // ====================================================================
        // C. UPSTREAM FAILURES (NON-SUCCESS STATUS CODES & NETWORK ERRORS)
        // ====================================================================

        [Fact]
        public async Task GetListingsAsync_UpstreamHttp404_ThrowsHttpRequestExceptionAndDoesNotReturnEmpty()
        {
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.NotFound));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.GetListingsAsync("portal", Culture.en_US));

            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        public async Task GetListingsAsync_UpstreamHttp500_ThrowsHttpRequestExceptionAndDoesNotReturnEmpty()
        {
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.GetListingsAsync("portal", Culture.en_US));

            Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        }

        [Fact]
        public async Task GetListingsAsync_UpstreamHttp503_ThrowsHttpRequestException()
        {
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.GetListingsAsync("portal", Culture.en_US));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }

        [Fact]
        public async Task GetListingsAsync_NetworkConnectionFailure_PropagatesException()
        {
            var handler = new TestHttpMessageHandler((req, ct) =>
                throw new HttpRequestException("Connection refused by remote host."));

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://store.steampowered.com/") };
            var service = new SteamService(httpClient, new CultureService());

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.GetListingsAsync("portal", Culture.en_US));

            Assert.Contains("Connection refused", ex.Message);
        }

        // ====================================================================
        // D. CANCELLATION AND TIMEOUT HANDLING
        // ====================================================================

        [Fact]
        public async Task GetListingsAsync_PreCancelledToken_ThrowsOperationCanceledExceptionWithoutSending()
        {
            var invocationCount = 0;
            var handler = new TestHttpMessageHandler((req, ct) =>
            {
                invocationCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });
            });

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://store.steampowered.com/") };
            var service = new SteamService(httpClient, new CultureService());

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancelled

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.GetListingsAsync("portal", Culture.en_US, cts.Token));

            Assert.Equal(0, invocationCount);
        }

        [Fact]
        public async Task GetListingsAsync_CallerCancellationDuringRequest_PropagatesCancellation()
        {
            using var cts = new CancellationTokenSource();
            var handler = new TestHttpMessageHandler((req, ct) =>
            {
                // Simulate caller token cancellation triggering during async wait
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://store.steampowered.com/") };
            var service = new SteamService(httpClient, new CultureService());

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.GetListingsAsync("portal", Culture.en_US, cts.Token));

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public async Task GetListingsAsync_ResponseBodyStalls_EnforcesHttpClientTimeout()
        {
            // Fake handler returns headers immediately, but the response body stalls
            var handler = new TestHttpMessageHandler((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StallingHttpContent(delayMs: 10000) // 10-second stall
                };
                return Task.FromResult(response);
            });

            // Configure short, test-specific HttpClient.Timeout (100ms) to exercise actual timeout mechanism
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                Timeout = TimeSpan.FromMilliseconds(100)
            };

            var service = new SteamService(httpClient, new CultureService());

            using var callerCts = new CancellationTokenSource(); // Caller does NOT cancel

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var ex = await Assert.ThrowsAsync<TaskCanceledException>(() =>
                service.GetListingsAsync("portal", Culture.en_US, callerCts.Token));
            stopwatch.Stop();

            // Verify timeout aborted the stalled body read promptly (well under the 10-second stall)
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Test took too long: {stopwatch.ElapsedMilliseconds}ms");

            // Verify timeout is not conflated with caller cancellation
            Assert.False(callerCts.IsCancellationRequested, "Caller token must not be marked as cancelled");
            Assert.NotNull(ex.InnerException);
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Fact]
        public async Task GetListingsAsync_CallerCancelsDuringBodyRead_DistinguishedFromTimeout()
        {
            var handler = new TestHttpMessageHandler((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StallingHttpContent(delayMs: 10000)
                };
                return Task.FromResult(response);
            });

            // Standard long timeout (30 seconds)
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                Timeout = TimeSpan.FromSeconds(30)
            };

            var service = new SteamService(httpClient, new CultureService());

            using var callerCts = new CancellationTokenSource();
            callerCts.CancelAfter(100); // Caller explicitly cancels after 100ms

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.GetListingsAsync("portal", Culture.en_US, callerCts.Token));

            // Verify this is caller cancellation, NOT HttpClient timeout
            Assert.True(callerCts.IsCancellationRequested, "Caller cancellation token must be cancelled");
            Assert.IsNotType<TimeoutException>(ex.InnerException);
        }

        // ====================================================================
        // E. PUBLIC CONTRACT VERIFICATION
        // ====================================================================

        [Fact]
        public async Task GetListingsAsync_ReturnedList_SerializesToApprovedPublicJsonContract()
        {
            var (service, _) = CreateTestService((req, ct) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) });

            var listings = await service.GetListingsAsync("portal", Culture.en_US);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonString = JsonSerializer.Serialize(listings, jsonOptions);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            Assert.Equal(JsonValueKind.Array, root.ValueKind);
            Assert.Equal(1, root.GetArrayLength());

            var element = root[0];
            Assert.True(element.TryGetProperty("name", out var nameProp));
            Assert.Equal("Portal 2", nameProp.GetString());

            Assert.True(element.TryGetProperty("storeLink", out var storeProp));
            Assert.Contains("/app/12345/Portal_2", storeProp.GetString());

            Assert.True(element.TryGetProperty("imageLink", out var imgProp));
            Assert.Contains("capsule_sm_120.jpg", imgProp.GetString());

            Assert.True(element.TryGetProperty("price", out var priceProp));
            Assert.Equal(9.99, priceProp.GetDouble());

            Assert.True(element.TryGetProperty("saleType", out var saleTypeProp));
            Assert.Equal((int)sType.CostsMoney, saleTypeProp.GetInt32());

            Assert.True(element.TryGetProperty("appId", out var appIdProp));
            Assert.Equal("12345", appIdProp.GetString());
        }

        [Fact]
        public void Constructor_NullArguments_ThrowsArgumentNullException()
        {
            var cultureService = new CultureService();
            var httpClient = new HttpClient();

            Assert.Throws<ArgumentNullException>("httpClient", () => new SteamService(null!, cultureService));
            Assert.Throws<ArgumentNullException>("cultureService", () => new SteamService(httpClient, null!));
        }
    }
}
