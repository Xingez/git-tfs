namespace GitTfs.Test.Core
{
    using global::GitTfs.Core.RestTfs;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using Assert = global::GitTfs.Test.TestAssert;

    [TestClass]
    public class HttpTrafficCaptureHandlerTests
    {
        private string output;

        [TestInitialize]
        public void Initialize() => output = Path.Combine(Path.GetTempPath(), "git-tfs-http-" + Guid.NewGuid().ToString("N"));

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }

        [TestMethod]
        public async Task CapturesCompleteBodiesWithoutConsumingTheResponseAndRedactsCredentials()
        {
            var bytes = new byte[] { 0, 1, 127, 255 };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "session=secret");
            response.Headers.TryAddWithoutValidation("x-ms-continuationtoken", "next-page");
            using var http = new HttpClient(new HttpTrafficCaptureHandler(output, new Handler(_ => response)));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://tfs.example/items")
            {
                Content = new StringContent("request body")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "secret-token");
            request.Headers.TryAddWithoutValidation("Cookie", "session=secret");
            using var result = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            CollectionAssert.AreEqual(bytes, await result.Content.ReadAsByteArrayAsync());

            var exchange = Exchanges().Single();
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(exchange, "response.body")));
            Assert.Equal("request body", File.ReadAllText(Path.Combine(exchange, "request.body")));
            var metadata = File.ReadAllText(Path.Combine(exchange, "request.json"))
                + File.ReadAllText(Path.Combine(exchange, "response.json"));
            Assert.False(metadata.Contains("secret"));
            StringAssert.Contains(metadata, "[redacted]");
            StringAssert.Contains(metadata, "next-page");
        }

        [TestMethod]
        public void CapturesBothTheThrottledResponseAndTheRetry()
        {
            var throttled = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("throttled") };
            throttled.Headers.TryAddWithoutValidation("Retry-After", "0");
            var responses = new Queue<HttpResponseMessage>(new[]
            {
                throttled,
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}") }
            });
            using var http = new HttpClient(new HttpTrafficCaptureHandler(output, new Handler(_ => responses.Dequeue())));
            using var client = new RestTfsClient(http, "https://tfs.example/collection", "$/Project/Main");

            Assert.Empty(client.GetChangesets("$/Project/Main", 0, 1));

            var exchanges = Exchanges();
            Assert.Equal(2, exchanges.Length);
            Assert.Equal("throttled", File.ReadAllText(Path.Combine(exchanges[0], "response.body")));
            using var first = JsonDocument.Parse(File.ReadAllText(Path.Combine(exchanges[0], "response.json")));
            using var second = JsonDocument.Parse(File.ReadAllText(Path.Combine(exchanges[1], "response.json")));
            Assert.Equal(429, first.RootElement.GetProperty("statusCode").GetInt32());
            Assert.Equal(200, second.RootElement.GetProperty("statusCode").GetInt32());
        }

        [TestMethod]
        public async Task CapturesTransportFailuresAndPreservesTheException()
        {
            using var http = new HttpClient(new HttpTrafficCaptureHandler(output,
                new Handler(_ => throw new HttpRequestException("connection failed"))));
            try
            {
                await http.GetAsync("https://tfs.example/items");
                Assert.True(false, "Expected a transport error.");
            }
            catch (HttpRequestException exception)
            {
                Assert.Equal("connection failed", exception.Message);
            }
            var exchange = Exchanges().Single();
            Assert.True(File.Exists(Path.Combine(exchange, "request.json")));
            StringAssert.Contains(File.ReadAllText(Path.Combine(exchange, "error.json")), "connection failed");
            Assert.False(File.Exists(Path.Combine(exchange, "response.body")));
        }

        private string[] Exchanges() => Directory.GetFiles(output, "request.json", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName).OrderBy(path => path).ToArray();

        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(respond(request));
        }
    }
}
