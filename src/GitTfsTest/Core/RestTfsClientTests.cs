namespace GitTfs.Test.Core
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Assert = global::GitTfs.Test.TestAssert;
    using global::GitTfs.Core.RestTfs;
    using global::GitTfs.Core;
    using global::System.Net;
    using global::System.Net.Http;
    using global::System.Text;

    [TestClass]
    public class RestTfsClientTests
    {
        [TestMethod]
        public void ConfiguresJsonAsTheHttpClientDefaultAcceptedDataType()
        {
            using (var httpClient = new HttpClient())
            {
                RestTfsClient.ConfigureHttpClient(httpClient, new GitTfsSettings());

                Assert.Equal("application/json", httpClient.DefaultRequestHeaders.Accept.Single().MediaType);
            }
        }

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
            {
                RestTfsClient.ConfigureHttpClient(httpClient, new GitTfsSettings());
                using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
                {
                    var content = client.DownloadFile("$/Project/Branch/file.bin", 42);

                    CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 255 }, content);
                    Assert.Equal("application/octet-stream", handler.Requests[0].Headers.Accept.Single().MediaType);
                    StringAssert.Contains(handler.Requests[0].RequestUri.AbsoluteUri, "/Project/_apis/tfvc/items?");
                    StringAssert.Contains(handler.Requests[0].RequestUri.Query, "download=true");
                    StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.version=42");
                    StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionType=Changeset");
                }
            }
        }

        [TestMethod]
        public void ListsLatestItemsWithHashesAtAChangeset()
        {
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"count\":1,\"value\":[{\"path\":\"$/Project/Branch/file.bin\",\"isFolder\":false,\"hashValue\":\"AQI=\"}]} ",
                    Encoding.UTF8,
                    "application/json"),
            });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                var items = client.GetItems("$/Project/Branch", 42);

                Assert.Single(items);
                Assert.Equal("$/Project/Branch/file.bin", items[0].Path);
                Assert.Equal("AQI=", items[0].HashValue);
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "scopePath=");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "recursionLevel=Full");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "includeItems=true");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.version=42");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionType=Changeset");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionOption=none");
            }
        }

        [TestMethod]
        public void DownloadsPreviousTfvcVersionWhenRequested()
        {
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 4, 5, 6 }),
            });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                var content = client.DownloadFile("$/Project/Branch/deleted.bin", 42, "Changeset", "Previous");

                CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, content);
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.version=42");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionType=Changeset");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionOption=Previous");
            }
        }

        [TestMethod]
        public void DownloadsMergeSourceUsingRenameVersionOptionWhenRequested()
        {
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 7, 8, 9 }),
            });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                var content = client.DownloadFile("$/Project/Branch/renamed.bin", 42, "MergeSource", "UseRename");

                CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, content);
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionType=MergeSource");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "versionDescriptor.versionOption=UseRename");
            }
        }

        [TestMethod]
        public void ExposesNotFoundStatusForHistoricalFileFallbacks()
        {
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("missing", Encoding.UTF8, "text/plain"),
            });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                var exception = Assert.Throws<RestTfsException>(() => client.DownloadFile("$/Project/Branch/file.bin", 42));

                Assert.Equal(404, exception.StatusCode);
                Assert.Contains("file.bin", exception.Message);
            }
        }

        [TestMethod]
        public void CanListProjectChangesetsWithoutAnItemPathFilter()
        {
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"count\":0,\"value\":[]}", Encoding.UTF8, "application/json"),
            });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                client.GetChangesets("$/Project/Branch", 0, 100, filterByItemPath: false);

                Assert.False(handler.Requests[0].RequestUri.Query.IndexOf("searchCriteria.itemPath", StringComparison.Ordinal) >= 0,
                    "The project-wide changeset query must not include an item path filter.");
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "%24top=100");
            }
        }

        [TestMethod]
        public void UsesCollectionRouteForChangesetChangesPagination()
        {
            var handler = new QueueHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"changesetId\":7,\"hasMoreChanges\":true,\"changes\":[]}",
                        Encoding.UTF8,
                        "application/json"),
                },
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"count\":0,\"value\":[]}", Encoding.UTF8, "application/json"),
                });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                var changeset = client.GetChangeset(7);

                Assert.Empty(changeset.Changes);
                Assert.Equal(2, handler.Requests.Count);
                StringAssert.Contains(handler.Requests[0].RequestUri.AbsolutePath,
                    "/Project/_apis/tfvc/changesets/7");
                Assert.Equal("/tfs/DefaultCollection/_apis/tfvc/changesets/7/changes",
                    handler.Requests[1].RequestUri.AbsolutePath);
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
