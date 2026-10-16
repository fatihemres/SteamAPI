using System.Net;
using System.Text;
using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class SteamApiClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task SearchGamesAsync_On200OK_ReturnsListingsAndFiresTelemetryEvent()
    {
        const string responseBody = """
        [
            {
                "name": "Left 4 Dead 2",
                "storeLink": "https://store.steampowered.com/app/550/Left_4_Dead_2/",
                "imageLink": "https://cdn.akamai.steamstatic.com/steam/apps/550/capsule_sm_120.jpg",
                "price": 9.99,
                "saleType": 0,
                "appId": "550"
            }
        ]
        """;

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var client = new SteamApiClient("http://localhost:5271", 30, new HttpClient(handler));
        ApiInspectionRecord? recordedTelemetry = null;
        client.OnInspectionRecordCreated += record => recordedTelemetry = record;

        var result = await client.SearchGamesAsync("Left 4 Dead", 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("Left 4 Dead 2", result.Data[0].Name);
        Assert.Equal("550", result.Data[0].AppId);

        // Verify telemetry
        Assert.NotNull(recordedTelemetry);
        Assert.Equal(200, recordedTelemetry.ResponseStatusCode);
        Assert.Equal("POST", recordedTelemetry.HttpMethod);
        Assert.Contains("Left 4 Dead", recordedTelemetry.RequestBodyJson);
    }

    [Fact]
    public async Task SearchGamesAsync_On400BadRequest_ParsesValidationErrors()
    {
        const string problemJson = """
        {
            "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "title": "One or more validation errors occurred.",
            "status": 400,
            "errors": {
                "Game": [
                    "The Game field is required."
                ]
            }
        }
        """;

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(problemJson, Encoding.UTF8, "application/problem+json")
        });

        var client = new SteamApiClient("http://localhost:5271", 30, new HttpClient(handler));
        var result = await client.SearchGamesAsync("", 2);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("validation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchGamesAsync_On502BadGateway_HandlesUpstreamError()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("Upstream Steam store unreachable", Encoding.UTF8, "text/plain")
        });

        var client = new SteamApiClient("http://localhost:5271", 30, new HttpClient(handler));
        var result = await client.SearchGamesAsync("portal", 2);

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.StatusCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckConnectionAsync_On200OK_ReportsSuccess()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });

        var client = new SteamApiClient("http://localhost:5271", 30, new HttpClient(handler));
        var result = await client.CheckConnectionAsync();

        Assert.True(result.IsSuccess);
    }
}
