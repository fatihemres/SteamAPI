using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Services.Implementation;
using Microsoft.AspNetCore.Mvc.Testing;
using SteamAPI.Models;
using Xunit;

namespace SteamAPI.Tests
{
    public class RiskA_InputBoundaryTests : IClassFixture<SteamApiWebApplicationFactory>
    {
        private readonly SteamApiWebApplicationFactory _factory;

        public RiskA_InputBoundaryTests(SteamApiWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Post_SearchInput_NormalShortTitle_Returns200()
        {
            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
                Task.FromResult(new List<Listing>()));

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Post_SearchInput_MultiwordTitle_Returns200()
        {
            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
                Task.FromResult(new List<Listing>()));

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Counter-Strike Global Offensive",
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Post_SearchInput_UnicodeTitle_Returns200()
        {
            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
                Task.FromResult(new List<Listing>()));

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Türkçe Oyun Başlığı 2026 🎮",
                Culture = Culture.tr_TR
            };

            var response = await client.PostAsJsonAsync("/steam", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Post_SearchInput_Exact199Chars_Returns200()
        {
            var term199 = new string('A', 199);
            string? capturedGame = null;

            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
            {
                capturedGame = game;
                return Task.FromResult(new List<Listing>());
            });

            var request = new GetSteamGameInfoRequestModel
            {
                Game = term199,
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(199, capturedGame?.Length);
        }

        [Fact]
        public async Task Post_SearchInput_Exact200Chars_Returns200()
        {
            var term200 = new string('B', 200);
            string? capturedGame = null;

            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
            {
                capturedGame = game;
                return Task.FromResult(new List<Listing>());
            });

            var request = new GetSteamGameInfoRequestModel
            {
                Game = term200,
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(200, capturedGame?.Length);
        }

        [Fact]
        public async Task Post_SearchInput_201Chars_RejectsWith400ValidationProblem()
        {
            // Boundary assertion for 3C-A01: 201 UTF-16 code units exceeds the approved 200-character domain limit.
            // Expected RFC 9457 ValidationProblemDetails response with 400 Bad Request.
            // Service must NOT be invoked.
            var term201 = new string('C', 201);
            int serviceCallCount = 0;

            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
            {
                serviceCallCount++;
                return Task.FromResult(new List<Listing>());
            });

            var request = new GetSteamGameInfoRequestModel
            {
                Game = term201,
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, serviceCallCount);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            Assert.True(contentType == "application/problem+json" || contentType == "application/json");

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());

            var errors = doc.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty("Game", out var gameErrors));
            var errorList = gameErrors.EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(errorList, msg => msg != null && msg.Contains("cannot exceed 200 characters"));
        }

        [Fact]
        public async Task Post_SearchInput_Oversized10000Chars_RejectsWith400ValidationProblem()
        {
            // Boundary assertion for 3C-A01: 10,000 UTF-16 code units must be rejected at API boundary.
            var oversizedTerm = new string('D', 10000);
            int serviceCallCount = 0;

            using var client = _factory.CreateClientWithHandler((game, cult, ct) =>
            {
                serviceCallCount++;
                return Task.FromResult(new List<Listing>());
            });

            var request = new GetSteamGameInfoRequestModel
            {
                Game = oversizedTerm,
                Culture = Culture.en_US
            };

            var response = await client.PostAsJsonAsync("/steam", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, serviceCallCount);

            var rawJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(rawJson);
            Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
            var errors = doc.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty("Game", out var gameErrors));
            var errorList = gameErrors.EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(errorList, msg => msg != null && msg.Contains("cannot exceed 200 characters"));
        }

        [Fact]
        public async Task Service_RequestConstruction_OversizedTerm_ConstructsExcessiveUri()
        {
            // Internal service behavior test: Verifies that if an unbounded term reaches SteamService directly,
            // it produces a long URI string. This justifies the API-boundary gate in SteamController and DTO.
            var capturedRequests = new List<HttpRequestMessage>();
            var handler = new SteamServiceHttpTests_TestHandler((req, ct) =>
            {
                capturedRequests.Add(req);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Empty)
                });
            });

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/")
            };

            var service = new SteamService(httpClient, new CultureService());
            var inputTerm = new string('X', 5000);

            await service.GetListingsAsync(inputTerm, Culture.en_US);

            Assert.Single(capturedRequests);
            var uriString = capturedRequests[0].RequestUri?.ToString();
            Assert.NotNull(uriString);
            Assert.True(uriString.Length > 5000, $"Expected URI length > 5000, got {uriString.Length}");
        }

        private class SteamServiceHttpTests_TestHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public SteamServiceHttpTests_TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request, cancellationToken);
            }
        }
    }
}
