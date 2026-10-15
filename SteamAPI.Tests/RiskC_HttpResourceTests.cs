using System.Net;
using System.Net.Sockets;
using System.Text;
using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Services.Implementation;
using Microsoft.Extensions.DependencyInjection;
using SteamAPI.Controllers;
using SteamAPI.Models;
using Xunit;

namespace SteamAPI.Tests
{
    public class RiskC_HttpResourceTests : IClassFixture<SteamApiWebApplicationFactory>
    {
        private readonly SteamApiWebApplicationFactory _factory;

        public RiskC_HttpResourceTests(SteamApiWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private const string SampleValidHtml =
            "<a class=\"match ds_collapse_flag \" data-ds-appid=\"400\" href=\"https://store.steampowered.com/app/400/Portal/\">" +
            "<div class=\"match_name\">Portal</div>" +
            "<div class=\"match_img\"><img src=\"https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg\"></div>" +
            "<div class=\"match_price\">$9.99</div></a>";

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #region 1. Production DI Configuration Assertions

        [Fact]
        public void ProductionDI_ConfiguresMaxResponseContentBufferSize_To1MiB()
        {
            // Requirement for 3C-C02: Production typed client must have MaxResponseContentBufferSize = 1 MiB (1,048,576 bytes)
            var httpClientFactory = _factory.Services.GetRequiredService<IHttpClientFactory>();
            using var client = httpClientFactory.CreateClient("ISteamService");

            Assert.Equal(1024 * 1024, client.MaxResponseContentBufferSize);
        }

        [Fact]
        public void ProductionDI_ConfiguresPrimaryHandler_WithAllowAutoRedirectFalse()
        {
            // Requirement for 3C-C01: Production typed client primary message handler must have AllowAutoRedirect = false
            var handlerFactory = _factory.Services.GetRequiredService<IHttpMessageHandlerFactory>();
            var handler = handlerFactory.CreateHandler("ISteamService");

            HttpMessageHandler? current = handler;
            while (current is DelegatingHandler delegatingHandler)
            {
                current = delegatingHandler.InnerHandler;
            }

            Assert.NotNull(current);
            var socketsHandler = Assert.IsType<SocketsHttpHandler>(current);
            Assert.False(socketsHandler.AllowAutoRedirect);
        }

        #endregion

        #region 2. Loopback Real SocketsHttpHandler Redirect Tests

        [Fact]
        public async Task Loopback_SocketsHttpHandler_WithAllowAutoRedirectFalse_RefusesToFollowRedirect()
        {
            // Requirement for 3C-C01: Real SocketsHttpHandler must have AllowAutoRedirect = false,
            // preventing the client from following 302 redirects to internal or external locations.
            int port = GetAvailablePort();
            string prefix = $"http://127.0.0.1:{port}/";

            using var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            var receivedPaths = new List<string>();

            // Background task to serve loopback requests
            var serverTask = Task.Run(async () =>
            {
                // Serve initial request with 302
                var context = await listener.GetContextAsync();
                receivedPaths.Add(context.Request.Url?.AbsolutePath ?? string.Empty);
                context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                context.Response.RedirectLocation = $"http://127.0.0.1:{port}/redirected_target";
                context.Response.Close();

                // If client wrongly followed redirect, it would hit /redirected_target:
                var secondWaitTask = listener.GetContextAsync();
                var completed = await Task.WhenAny(secondWaitTask, Task.Delay(200));
                if (completed == secondWaitTask)
                {
                    var secondContext = await secondWaitTask;
                    receivedPaths.Add(secondContext.Request.Url?.AbsolutePath ?? string.Empty);
                    secondContext.Response.StatusCode = 200;
                    secondContext.Response.Close();
                }
            });

            // Use SocketsHttpHandler with AllowAutoRedirect = false (as configured in production)
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(prefix) };
            var service = new SteamService(client, new CultureService());

            // Because AllowAutoRedirect is false, the 302 status is returned to SteamService,
            // which calls response.EnsureSuccessStatusCode(), throwing HttpRequestException.
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.GetListingsAsync("Portal", Culture.en_US));

            Assert.Equal(HttpStatusCode.Redirect, ex.StatusCode);
            await serverTask;

            // Assert that ONLY the initial request was sent and the redirect target was never visited
            Assert.Single(receivedPaths);
            Assert.Equal("/search/suggest", receivedPaths[0]);
        }

        #endregion

        #region 3. Response Content Size Limit Tests (1 MiB)

        [Fact]
        public async Task ResponseContentSize_Under1MiB_Succeeds()
        {
            var content = new StringContent(SampleValidHtml, Encoding.UTF8, "text/html");
            var testHandler = new DelegatingTestHandler((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

            using var client = new HttpClient(testHandler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                MaxResponseContentBufferSize = 1024 * 1024
            };

            var service = new SteamService(client, new CultureService());
            var results = await service.GetListingsAsync("Portal", Culture.en_US);

            Assert.Single(results);
            Assert.Equal("Portal", results[0].Name);
        }

        [Fact]
        public async Task ResponseContentSize_Exceeding1MiB_ThrowsHttpRequestException()
        {
            // Requirement for 3C-C02: When response exceeds 1 MiB (1,048,576 bytes),
            // HttpClient with MaxResponseContentBufferSize = 1MB throws HttpRequestException.
            byte[] oversizedBytes = new byte[1024 * 1024 + 1]; // 1 MiB + 1 byte
            var content = new ByteArrayContent(oversizedBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");

            var testHandler = new DelegatingTestHandler((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

            using var client = new HttpClient(testHandler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                MaxResponseContentBufferSize = 1024 * 1024
            };

            var service = new SteamService(client, new CultureService());

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.GetListingsAsync("Portal", Culture.en_US));

            Assert.Contains("buffer size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ResponseContentSize_ChunkedStreamingExceeding1MiB_ThrowsHttpRequestException()
        {
            // Streaming/chunked response without Content-Length header that exceeds 1 MiB
            var testHandler = new DelegatingTestHandler((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new GeneratingStream(targetBytes: 1024 * 1024 + 500))
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
                return Task.FromResult(response);
            });

            using var client = new HttpClient(testHandler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                MaxResponseContentBufferSize = 1024 * 1024
            };

            var service = new SteamService(client, new CultureService());

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.GetListingsAsync("Portal", Culture.en_US));

            Assert.Contains("buffer size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 4. Timeout and Cancellation Retained

        [Fact]
        public async Task HttpResource_SlowResponseBodyTransfer_EnforcesConfiguredTimeout()
        {
            var testHandler = new DelegatingTestHandler(async (req, ct) =>
            {
                await Task.Delay(5000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) };
            });

            using var client = new HttpClient(testHandler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                Timeout = TimeSpan.FromMilliseconds(100)
            };

            var service = new SteamService(client, new CultureService());

            var ex = await Assert.ThrowsAsync<TaskCanceledException>(() =>
                service.GetListingsAsync("Portal", Culture.en_US));

            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Fact]
        public async Task HttpResource_CallerCancellationDuringTransfer_AbortsPromptly()
        {
            var testHandler = new DelegatingTestHandler(async (req, ct) =>
            {
                await Task.Delay(5000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleValidHtml) };
            });

            using var client = new HttpClient(testHandler)
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                Timeout = TimeSpan.FromSeconds(30)
            };

            var service = new SteamService(client, new CultureService());

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50);

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.GetListingsAsync("Portal", Culture.en_US, cts.Token));

            Assert.True(cts.IsCancellationRequested);
        }

        #endregion

        private class DelegatingTestHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public DelegatingTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request, cancellationToken);
            }
        }

        private class GeneratingStream : Stream
        {
            private readonly int _targetBytes;
            private int _bytesProduced;

            public GeneratingStream(int targetBytes)
            {
                _targetBytes = targetBytes;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_bytesProduced >= _targetBytes)
                {
                    return Task.FromResult(0);
                }

                int toProduce = Math.Min(count, _targetBytes - _bytesProduced);
                Array.Fill(buffer, (byte)'A', offset, toProduce);
                _bytesProduced += toProduce;
                return Task.FromResult(toProduce);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
