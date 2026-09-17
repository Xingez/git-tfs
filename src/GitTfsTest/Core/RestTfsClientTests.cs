namespace GitTfs.Test.Core
{
    using global::GitTfs.Core.RestTfs;
    using global::System.Net;
    using global::System.Net.Http;
    using global::System.Text;

    [TestClass]
    public class RestTfsClientTests
    {
        [TestMethod]
        public void RetriesWithResponseHeadersAndBuildsTfvcUrl()
        {
            var throttled = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("throttled", Encoding.UTF8, "text/plain"),
            };
            throttled.Headers.TryAddWithoutValidation("Retry-After", "0");
            var handler = new QueueHandler(throttled,
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"count\":0,\"value\":[]}", Encoding.UTF8, "application/json"),
                });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                var changesets = client.GetChangesets("$/Project/Branch", 0, 1);

                Assert.Equal(0, changesets.Count);
                Assert.Equal(2, handler.Requests.Count);
                StringAssert.Contains(handler.Requests[0].RequestUri.AbsoluteUri, "/Project/_apis/tfvc/changesets?");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "searchCriteria.itemPath=");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "api-version=7.1");
            }
        }

        [TestMethod]
        public void DownloadsBinaryContentThroughTheItemsEndpoint()
        {
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0, 1, 2, 255 }),
            });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                var content = client.DownloadFile("$/Project/Branch/file.bin", 42);

                CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 255 }, content);
                StringAssert.Contains(handler.Requests[0].RequestUri.AbsoluteUri, "/Project/_apis/tfvc/items?");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "download=true");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.version=42");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionType=Changeset");
            }
        }

        private sealed class QueueHandler : HttpMessageHandler
        {
            public QueueHandler(params HttpResponseMessage[] responses)
            {
                Responses = new Queue<HttpResponseMessage>(responses);
            }

            public Queue<HttpResponseMessage> Responses { get; }
            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(Responses.Dequeue());
            }
        }
    }
}
