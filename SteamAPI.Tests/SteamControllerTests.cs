using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Services.Abstraction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SteamAPI.Controllers;
using SteamAPI.Models;
using Xunit;

namespace SteamAPI.Tests
{
    public class SteamControllerTests
    {
        private class FakeSteamService : ISteamService
        {
            private readonly Func<string, Culture, CancellationToken, Task<List<Listing>>> _handler;

            public FakeSteamService(Func<string, Culture, CancellationToken, Task<List<Listing>>> handler)
            {
                _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }

            public Task<List<Listing>> GetListingsAsync(string game, Culture culture, CancellationToken cancellationToken = default)
            {
                return _handler(game, culture, cancellationToken);
            }
        }

        private static Listing CreateSampleListing(
            string name = "Portal 2",
            string appId = "12345",
            double? price = 9.99,
            sType saleType = sType.CostsMoney)
        {
            var cultureInfo = new System.Globalization.CultureInfo("en-US");
            var html = $"<a class=\"match ds_collapse_flag\" data-ds-appid=\"{appId}\" href=\"https://store.steampowered.com/app/{appId}/{name}/\">" +
                       $"<div class=\"match_name\">{name}</div>" +
                       $"<div class=\"match_img\"><img src=\"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/capsule_sm_120.jpg\"></div>" +
                       $"<div class=\"match_price\">{(price.HasValue ? "$" + price.Value.ToString("F2", cultureInfo) : "Free")}</div></a>";
            return new Listing(html, cultureInfo);
        }

        // ====================================================================
        // A. VALID REQUEST SCENARIOS
        // ====================================================================

        [Theory]
        [InlineData(Culture.tr_TR)]
        [InlineData(Culture.en_US)]
        [InlineData(Culture.es_ES)]
        public async Task GetByCulture_ValidRequestWithSupportedCultures_Returns200WithListings(Culture culture)
        {
            var expectedListings = new List<Listing> { CreateSampleListing() };
            string? capturedGame = null;
            Culture? capturedCulture = null;

            var service = new FakeSteamService((game, cult, ct) =>
            {
                capturedGame = game;
                capturedCulture = cult;
                return Task.FromResult(expectedListings);
            });

            var controller = new SteamController(service);
            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = culture
            };

            var actionResult = await controller.GetByCulture(request, CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

            var returnedListings = Assert.IsAssignableFrom<IEnumerable<Listing>>(okResult.Value);
            Assert.Single(returnedListings);
            Assert.Equal("Portal", capturedGame);
            Assert.Equal(culture, capturedCulture);
        }

        [Fact]
        public async Task GetByCulture_LegitimateEmptySearchResults_Returns200WithEmptyArray()
        {
            var service = new FakeSteamService((game, cult, ct) =>
                Task.FromResult(new List<Listing>()));

            var controller = new SteamController(service);
            var request = new GetSteamGameInfoRequestModel
            {
                Game = "NonExistentGameXYZ12345",
                Culture = Culture.en_US
            };

            var actionResult = await controller.GetByCulture(request, CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

            var returnedListings = Assert.IsAssignableFrom<IEnumerable<Listing>>(okResult.Value);
            Assert.Empty(returnedListings);
        }

        [Fact]
        public async Task GetByCulture_PreservesApprovedListingPublicContract()
        {
            var sampleListing = CreateSampleListing("Half-Life", "70", 9.99, sType.CostsMoney);
            var service = new FakeSteamService((g, c, ct) => Task.FromResult(new List<Listing> { sampleListing }));
            var controller = new SteamController(service);

            var actionResult = await controller.GetByCulture(new GetSteamGameInfoRequestModel
            {
                Game = "Half-Life",
                Culture = Culture.en_US
            }, CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var listings = Assert.IsAssignableFrom<IEnumerable<Listing>>(okResult.Value);

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(listings, jsonOptions);

            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement;
            Assert.Equal(JsonValueKind.Array, array.ValueKind);
            Assert.Equal(1, array.GetArrayLength());

            var item = array[0];
            var properties = item.EnumerateObject().Select(p => p.Name).ToList();

            // Assert exactly 6 approved public properties
            Assert.Equal(6, properties.Count);
            Assert.Contains("name", properties);
            Assert.Contains("storeLink", properties);
            Assert.Contains("imageLink", properties);
            Assert.Contains("price", properties);
            Assert.Contains("saleType", properties);
            Assert.Contains("appId", properties);

            // Assert types
            Assert.Equal(JsonValueKind.Number, item.GetProperty("saleType").ValueKind);
            Assert.Equal((int)sType.CostsMoney, item.GetProperty("saleType").GetInt32());
            Assert.Equal(9.99, item.GetProperty("price").GetDouble());
        }

        [Fact]
        public async Task GetByCulture_FreeGame_SerializesNullablePriceAsNullAndNumericSaleType()
        {
            var freeListing = CreateSampleListing("Dota 2", "570", null, sType.FreeToPlay);
            var service = new FakeSteamService((g, c, ct) => Task.FromResult(new List<Listing> { freeListing }));
            var controller = new SteamController(service);

            var actionResult = await controller.GetByCulture(new GetSteamGameInfoRequestModel
            {
                Game = "Dota 2",
                Culture = Culture.en_US
            }, CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(okResult.Value, jsonOptions);

            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement[0];

            Assert.Equal(JsonValueKind.Null, item.GetProperty("price").ValueKind);
            Assert.Equal((int)sType.FreeToPlay, item.GetProperty("saleType").GetInt32());
        }

        // ====================================================================
        // B. INVALID REQUEST SCENARIOS (VALIDATION 400 BAD REQUEST)
        // ====================================================================

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\r\n")]
        public async Task GetByCulture_EmptyOrWhitespaceGame_Returns400ValidationProblem(string? invalidGame)
        {
            var service = new FakeSteamService((g, c, ct) => Task.FromResult(new List<Listing>()));
            var controller = new SteamController(service);

            var request = new GetSteamGameInfoRequestModel
            {
                Game = invalidGame!,
                Culture = Culture.en_US
            };

            var actionResult = await controller.GetByCulture(request, CancellationToken.None);

            var badRequest = Assert.IsAssignableFrom<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);

            var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
            Assert.True(problem.Errors.ContainsKey(nameof(request.Game)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(99)]
        [InlineData(-1)]
        [InlineData(999)]
        public async Task GetByCulture_InvalidOrUndefinedCulture_Returns400ValidationProblem(int invalidCultureInt)
        {
            var service = new FakeSteamService((g, c, ct) => Task.FromResult(new List<Listing>()));
            var controller = new SteamController(service);

            var request = new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = (Culture)invalidCultureInt
            };

            var actionResult = await controller.GetByCulture(request, CancellationToken.None);

            var badRequest = Assert.IsAssignableFrom<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);

            var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
            Assert.True(problem.Errors.ContainsKey(nameof(request.Culture)));
        }

        [Fact]
        public async Task GetByCulture_NullRequest_Returns400ValidationProblem()
        {
            var service = new FakeSteamService((g, c, ct) => Task.FromResult(new List<Listing>()));
            var controller = new SteamController(service);

            var actionResult = await controller.GetByCulture(null!, CancellationToken.None);

            var badRequest = Assert.IsAssignableFrom<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
            Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        }

        // ====================================================================
        // C. UPSTREAM HTTP AND NETWORK ERRORS (502 BAD GATEWAY)
        // ====================================================================

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        public async Task GetByCulture_UpstreamNonSuccessStatusCode_Returns502BadGatewayWithProblemDetails(HttpStatusCode statusCode)
        {
            var service = new FakeSteamService((g, c, ct) =>
                throw new HttpRequestException($"Response status code does not indicate success: {(int)statusCode} ({statusCode}).", null, statusCode));

            var controller = new SteamController(service);

            var actionResult = await controller.GetByCulture(new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            }, CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);

            var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
            Assert.Equal("Bad Gateway", problem.Title);
            Assert.NotNull(problem.Detail);
            // Ensure no exception message or stack trace is leaked in detail
            Assert.DoesNotContain("HttpRequestException", problem.Detail);
            Assert.DoesNotContain("at ", problem.Detail);
        }

        [Fact]
        public async Task GetByCulture_NetworkConnectionFailure_Returns502BadGateway()
        {
            var service = new FakeSteamService((g, c, ct) =>
                throw new HttpRequestException("No connection could be made because the target machine actively refused it."));

            var controller = new SteamController(service);

            var actionResult = await controller.GetByCulture(new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            }, CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);

            var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
            Assert.Equal("Bad Gateway", problem.Title);
            Assert.DoesNotContain("actively refused", problem.Detail);
        }

        // ====================================================================
        // D. TIMEOUT AND CANCELLATION HANDLING (504 GATEWAY TIMEOUT VS CANCELED)
        // ====================================================================

        [Fact]
        public async Task GetByCulture_HttpClientTimeout_Returns504GatewayTimeoutWithProblemDetails()
        {
            // Simulate HttpClient internal timeout: TaskCanceledException with TimeoutException as InnerException
            // and caller token is NOT cancelled.
            var service = new FakeSteamService((g, c, ct) =>
                throw new TaskCanceledException("The request was canceled due to configured HttpClient.Timeout of 30 seconds elapsing.",
                    new TimeoutException("The operation has timed out.")));

            var controller = new SteamController(service);
            using var callerCts = new CancellationTokenSource(); // Caller NOT cancelled

            var actionResult = await controller.GetByCulture(new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            }, callerCts.Token);

            var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status504GatewayTimeout, objectResult.StatusCode);

            var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
            Assert.Equal(StatusCodes.Status504GatewayTimeout, problem.Status);
            Assert.Equal("Gateway Timeout", problem.Title);
            Assert.NotNull(problem.Detail);
            Assert.DoesNotContain("TaskCanceledException", problem.Detail);
        }

        [Fact]
        public async Task GetByCulture_CallerCancellation_PreservesCancellationWithoutConvertingTo504()
        {
            using var callerCts = new CancellationTokenSource();
            callerCts.Cancel(); // Caller explicitly cancels

            var service = new FakeSteamService((g, c, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new List<Listing>());
            });

            var controller = new SteamController(service);

            // Must throw OperationCanceledException rather than returning HTTP 504
            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                controller.GetByCulture(new GetSteamGameInfoRequestModel
                {
                    Game = "Portal",
                    Culture = Culture.en_US
                }, callerCts.Token));

            Assert.True(callerCts.IsCancellationRequested);
        }

        [Fact]
        public async Task GetByCulture_UnexpectedUpstreamTaskCanceledWithoutTimeout_Returns502BadGatewayWithProblemDetails()
        {
            var service = new FakeSteamService((g, c, ct) =>
                throw new TaskCanceledException("Upstream internal cancellation without timeout"));

            var controller = new SteamController(service);
            using var callerCts = new CancellationTokenSource(); // Caller NOT cancelled

            var actionResult = await controller.GetByCulture(new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            }, callerCts.Token);

            var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);

            var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
            Assert.Equal("Bad Gateway", problem.Title);
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", problem.Detail);
            Assert.DoesNotContain("TaskCanceledException", problem.Detail);
        }

        [Fact]
        public async Task GetByCulture_UnexpectedUpstreamOperationCanceledWithoutTimeout_Returns502BadGatewayWithProblemDetails()
        {
            var service = new FakeSteamService((g, c, ct) =>
                throw new OperationCanceledException("Upstream operation canceled without timeout"));

            var controller = new SteamController(service);
            using var callerCts = new CancellationTokenSource(); // Caller NOT cancelled

            var actionResult = await controller.GetByCulture(new GetSteamGameInfoRequestModel
            {
                Game = "Portal",
                Culture = Culture.en_US
            }, callerCts.Token);

            var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);

            var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
            Assert.Equal("Bad Gateway", problem.Title);
            Assert.Equal("The upstream Steam search service returned an error or was unreachable.", problem.Detail);
            Assert.DoesNotContain("OperationCanceledException", problem.Detail);
        }

        [Fact]
        public void Constructor_NullSteamService_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>("steamService", () => new SteamController(null!));
        }
    }
}
