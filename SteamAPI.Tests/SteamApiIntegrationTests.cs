using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Services.Abstraction;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SteamAPI.Controllers;
using SteamAPI.Models;
using Xunit;

namespace SteamAPI.Tests
{
    public class FakeSteamService : ISteamService
    {
        public Func<string, Culture, CancellationToken, Task<List<Listing>>>? Handler { get; set; }

        public Task<List<Listing>> GetListingsAsync(string game, Culture culture, CancellationToken cancellationToken = default)
        {
            if (Handler != null)
            {
                return Handler(game, culture, cancellationToken);
            }

            return Task.FromResult(new List<Listing>());
        }
    }

    public class TestListing : Listing
    {
        public TestListing(string name, string appId, double? price, sType saleType, string storeLink, string imageLink)
            : base(string.Empty, CultureInfo.InvariantCulture)
        {
            Name = name;
            AppId = appId;
            Price = price;
            SaleType = saleType;
            StoreLink = storeLink;
            ImageLink = imageLink;
        }
    }

    public class SteamApiWebApplicationFactory : WebApplicationFactory<SteamController>
    {
        public FakeSteamService FakeSteamService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                var descriptors = services.Where(d => d.ServiceType == typeof(ISteamService)).ToList();
                foreach (var d in descriptors)
                {
                    services.Remove(d);
                }
                services.AddSingleton<ISteamService>(FakeSteamService);
            });
        }

        public HttpClient CreateClientWithHandler(Func<string, Culture, CancellationToken, Task<List<Listing>>> handler)
        {
            return WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    var descriptors = services.Where(d => d.ServiceType == typeof(ISteamService)).ToList();
                    foreach (var d in descriptors)
                    {
                        services.Remove(d);
                    }
                    var fake = new FakeSteamService { Handler = handler };
                    services.AddSingleton<ISteamService>(fake);
                });
            }).CreateClient();
        }
    }

    public class SteamApiIntegrationTests : IClassFixture<SteamApiWebApplicationFactory>
    {
        private readonly SteamApiWebApplicationFactory _factory;
        private readonly HttpClient _client;
        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public SteamApiIntegrationTests(SteamApiWebApplicationFactory factory, Xunit.Abstractions.ITestOutputHelper output)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _output = output;
        }

        #region A. Valid Requests

        [Theory]
        [InlineData(Culture.tr_TR, 1)]
        [InlineData(Culture.en_US, 2)]
        [InlineData(Culture.es_ES, 3)]
        public async Task Post_ValidRequest_AllSupportedCultures_Returns200_WithExpectedJsonContract(Culture culture, int cultureId)
        {
            // Verify cultureId matches enum value
            Assert.Equal((int)culture, cultureId);

            // Arrange
            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
            {
                Assert.Equal("Portal", game);
                Assert.Equal(culture, cult);

                var list = new List<Listing>
                {
                    new TestListing("Portal", "400", 9.99, sType.CostsMoney, "https://store.steampowered.com/app/400", "https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg"),
                    new TestListing("Portal 2", "620", null, sType.FreeToPlay, "https://store.steampowered.com/app/620", "https://cdn.akamai.steamstatic.com/steam/apps/620/header.jpg")
                };
                return Task.FromResult(list);
            });

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = culture
            };

            // Act
            var response = await client.PostAsJsonAsync("/steam", request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(2, doc.RootElement.GetArrayLength());

            // First element: CostsMoney (0) with numeric price
            var first = doc.RootElement[0];
            Assert.Equal(JsonValueKind.Object, first.ValueKind);
            Assert.Equal("Portal", first.GetProperty("name").GetString());
            Assert.Equal("400", first.GetProperty("appId").GetString());
            Assert.Equal("https://store.steampowered.com/app/400", first.GetProperty("storeLink").GetString());
            Assert.Equal("https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg", first.GetProperty("imageLink").GetString());
            Assert.Equal((int)sType.CostsMoney, first.GetProperty("saleType").GetInt32());
            Assert.Equal(JsonValueKind.Number, first.GetProperty("saleType").ValueKind);
            Assert.Equal(9.99, first.GetProperty("price").GetDouble());
            Assert.Equal(JsonValueKind.Number, first.GetProperty("price").ValueKind);

            // Verify exactly the 6 public Listing fields (camelCase)
            var propertyNames = first.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
            var expectedPropertyNames = new List<string> { "appId", "imageLink", "name", "price", "saleType", "storeLink" };
            Assert.Equal(expectedPropertyNames, propertyNames);

            // Second element: FreeToPlay (1) with null price
            var second = doc.RootElement[1];
            Assert.Equal("Portal 2", second.GetProperty("name").GetString());
            Assert.Equal("620", second.GetProperty("appId").GetString());
            Assert.Equal((int)sType.FreeToPlay, second.GetProperty("saleType").GetInt32());
            Assert.Equal(JsonValueKind.Number, second.GetProperty("saleType").ValueKind);
            Assert.Equal(JsonValueKind.Null, second.GetProperty("price").ValueKind);
        }

        [Fact]
        public async Task Post_ValidRequest_ZeroListings_Returns200_WithEmptyJsonArray()
        {
            // Arrange
            using var client = _factory.CreateClientWithHandler((game, cult, ct) => Task.FromResult(new List<Listing>()));

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "NonExistentGame12345",
                Culture = Culture.en_US
            };

            // Act
            var response = await client.PostAsJsonAsync("/steam", request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(0, doc.RootElement.GetArrayLength());
        }

        #endregion

        #region B. Invalid Requests

        [Fact]
        public async Task Post_MissingRequestBody_Returns400()
        {
            using var emptyContent = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", emptyContent);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_JsonNullBody_Returns400()
        {
            using var nullContent = new StringContent("null", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", nullContent);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_MissingGameProperty_Returns400()
        {
            using var content = new StringContent("{\"culture\":1}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_NullGameProperty_Returns400()
        {
            using var content = new StringContent("{\"game\":null,\"culture\":1}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_EmptyGameProperty_Returns400()
        {
            using var content = new StringContent("{\"game\":\"\",\"culture\":1}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_WhitespaceGameProperty_Returns400()
        {
            using var content = new StringContent("{\"game\":\"   \",\"culture\":1}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_MissingCultureProperty_Returns400_RejectsDefaultZero()
        {
            using var content = new StringContent("{\"game\":\"Portal\"}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_CultureZero_Returns400()
        {
            using var content = new StringContent("{\"game\":\"Portal\",\"culture\":0}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_Culture99_Returns400()
        {
            using var content = new StringContent("{\"game\":\"Portal\",\"culture\":99}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_NegativeCulture_Returns400()
        {
            using var content = new StringContent("{\"game\":\"Portal\",\"culture\":-1}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_InvalidCultureJsonType_Returns400()
        {
            using var content = new StringContent("{\"game\":\"Portal\",\"culture\":\"en_US\"}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_MalformedJson_Returns400()
        {
            using var content = new StringContent("{\"game\":\"Portal\", malformed", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/steam", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        #endregion

        #region C. Upstream Failures (502 Bad Gateway)

        [Fact]
        public async Task Post_WhenUpstreamReturns404_Returns502_WithSanitizedProblemDetails()
        {
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(new HttpRequestException("Steam search suggest returned 404 Not Found", null, HttpStatusCode.NotFound)));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(502, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Bad Gateway", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", doc.RootElement.GetProperty("detail").GetString());

            // Sanitization: No upstream error leakage or stack trace
            Assert.DoesNotContain("404", rawJson);
            Assert.DoesNotContain("stackTrace", rawJson, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Post_WhenUpstreamReturns500_Returns502_WithSanitizedProblemDetails()
        {
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(new HttpRequestException("Internal Server Error from Steam upstream", null, HttpStatusCode.InternalServerError)));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(502, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Bad Gateway", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", doc.RootElement.GetProperty("detail").GetString());

            Assert.DoesNotContain("Internal Server Error", rawJson);
            Assert.DoesNotContain("stackTrace", rawJson, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Post_WhenUpstreamReturns503_Returns502_WithSanitizedProblemDetails()
        {
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(new HttpRequestException("Service Unavailable from Steam upstream", null, HttpStatusCode.ServiceUnavailable)));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(502, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Bad Gateway", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", doc.RootElement.GetProperty("detail").GetString());

            Assert.DoesNotContain("Service Unavailable", rawJson);
            Assert.DoesNotContain("stackTrace", rawJson, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Post_WhenUpstreamConnectionFails_Returns502_WithSanitizedProblemDetails()
        {
            var socketEx = new SocketException((int)SocketError.ConnectionRefused);
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(new HttpRequestException("Connection refused to store.steampowered.com", socketEx)));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(502, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Bad Gateway", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", doc.RootElement.GetProperty("detail").GetString());

            Assert.DoesNotContain("store.steampowered.com", rawJson);
            Assert.DoesNotContain("SocketException", rawJson);
            Assert.DoesNotContain("stackTrace", rawJson, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region D. Confirmed Timeout (504 Gateway Timeout)

        [Fact]
        public async Task Post_WhenUpstreamTimesOut_Returns504_WithSanitizedProblemDetails()
        {
            var timeoutEx = new OperationCanceledException("Upstream request timed out.", new TimeoutException("The HTTP operation timed out."));
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(timeoutEx));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(504, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Gateway Timeout", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service did not respond within the configured timeout.", doc.RootElement.GetProperty("detail").GetString());

            Assert.DoesNotContain("TimeoutException", rawJson);
            Assert.DoesNotContain("stackTrace", rawJson, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region E. Cancellation Classification — Negative Regression

        [Fact]
        public async Task Post_WhenUpstreamTaskCanceledWithoutTimeout_ShouldNotReturnGatewayTimeout_DemonstratesMisclassification()
        {
            // Scenario: Upstream throws TaskCanceledException without an inner TimeoutException,
            // while the caller's cancellation token is NOT cancelled.
            // Expected RFC 9110 behavior: This is NOT a gateway timeout (504). It is an upstream failure (502 Bad Gateway).
            var canceledEx = new TaskCanceledException("Upstream internal cancellation without timeout");
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(canceledEx));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            // Verified remediation: no longer returns 504 Gateway Timeout, now returns 502 Bad Gateway
            Assert.NotEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(502, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Bad Gateway", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", doc.RootElement.GetProperty("detail").GetString());
            Assert.DoesNotContain("TaskCanceledException", rawJson);
        }

        [Fact]
        public async Task Post_WhenUpstreamOperationCanceledWithoutTimeout_Returns502_WithSanitizedProblemDetails()
        {
            var canceledEx = new OperationCanceledException("Upstream operation canceled without timeout");
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(canceledEx));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(502, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Bad Gateway", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", doc.RootElement.GetProperty("detail").GetString());
            Assert.DoesNotContain("OperationCanceledException", rawJson);
        }

        [Fact]
        public async Task Post_WhenUpstreamTaskCanceledWithInnerTimeout_Returns504_WithSanitizedProblemDetails()
        {
            var timeoutEx = new TaskCanceledException("HttpClient timeout", new TimeoutException("The operation has timed out."));
            using var client = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(timeoutEx));

            var response = await client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(504, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Gateway Timeout", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("The upstream Steam search service did not respond within the configured timeout.", doc.RootElement.GetProperty("detail").GetString());
            Assert.DoesNotContain("TimeoutException", rawJson);
        }

        #endregion

        #region F. Caller Cancellation Preservation

        [Fact]
        public async Task Post_WhenCallerCancelsToken_PreservesCancellationOnClient()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _client.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US }, cts.Token));
        }

        #endregion

        #region G. Content-Type Inspection

        [Fact]
        public async Task Inspect_ActualContentTypes_ForValidationAndErrorResponses()
        {
            // 0. Inspect 200 OK
            using var client200 = _factory.CreateClientWithHandler((g, c, ct) =>
            {
                var list = new List<Listing>
                {
                    new TestListing("Portal", "400", 9.99, sType.CostsMoney, "https://store.steampowered.com/app/400", "https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg")
                };
                return Task.FromResult(list);
            });
            var response200 = await client200.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });
            Assert.Equal(HttpStatusCode.OK, response200.StatusCode);
            var ct200 = response200.Content.Headers.ContentType?.ToString();
            var body200 = await response200.Content.ReadAsStringAsync();
            _output.WriteLine($"[200 OK] Content-Type: {ct200}");
            _output.WriteLine($"[200 OK] Body: {body200}");

            // 1. Inspect 400 Bad Request
            using var badContent = new StringContent("{\"game\":\"\"}", Encoding.UTF8, "application/json");
            var response400 = await _client.PostAsync("/steam", badContent);
            Assert.Equal(HttpStatusCode.BadRequest, response400.StatusCode);
            var ct400 = response400.Content.Headers.ContentType?.ToString();
            var body400 = await response400.Content.ReadAsStringAsync();
            _output.WriteLine($"[400 Bad Request] Content-Type: {ct400}");
            _output.WriteLine($"[400 Bad Request] Body: {body400}");

            // 2. Inspect 502 Bad Gateway
            using var client502 = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(new HttpRequestException("Simulated upstream 502", null, HttpStatusCode.BadGateway)));
            var response502 = await client502.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });
            Assert.Equal(HttpStatusCode.BadGateway, response502.StatusCode);
            var ct502 = response502.Content.Headers.ContentType?.ToString();
            var body502 = await response502.Content.ReadAsStringAsync();
            _output.WriteLine($"[502 Bad Gateway] Content-Type: {ct502}");
            _output.WriteLine($"[502 Bad Gateway] Body: {body502}");

            // 3. Inspect 504 Gateway Timeout
            using var client504 = _factory.CreateClientWithHandler((g, c, ct) =>
                Task.FromException<List<Listing>>(new OperationCanceledException("Simulated timeout", new TimeoutException())));
            var response504 = await client504.PostAsJsonAsync("/steam", new GetSteamGameInfoRequestModel { Game = "Portal", Culture = Culture.en_US });
            Assert.Equal(HttpStatusCode.GatewayTimeout, response504.StatusCode);
            var ct504 = response504.Content.Headers.ContentType?.ToString();
            var body504 = await response504.Content.ReadAsStringAsync();
            _output.WriteLine($"[504 Gateway Timeout] Content-Type: {ct504}");
            _output.WriteLine($"[504 Gateway Timeout] Body: {body504}");

            // Assert that content types are valid JSON media types (either application/problem+json or application/json)
            Assert.True(response400.Content.Headers.ContentType?.MediaType == "application/problem+json" || response400.Content.Headers.ContentType?.MediaType == "application/json", $"Unexpected 400 content type: {ct400}");
            Assert.True(response502.Content.Headers.ContentType?.MediaType == "application/problem+json" || response502.Content.Headers.ContentType?.MediaType == "application/json", $"Unexpected 502 content type: {ct502}");
            Assert.True(response504.Content.Headers.ContentType?.MediaType == "application/problem+json" || response504.Content.Headers.ContentType?.MediaType == "application/json", $"Unexpected 504 content type: {ct504}");
        }

        #endregion
    }
}
