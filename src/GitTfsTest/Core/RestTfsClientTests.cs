namespace GitTfs.Test.Core
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Assert = GitTfs.Test.TestAssert;
    using GitTfs.Core.RestTfs;
    using GitTfs.Core;
    using System.Net;
    using System.Net.Http;
    using System.Text;

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
        public void UsesPatInsteadOfDefaultCredentialsWhenConfigured()
        {
            var settings = new GitTfsSettings { Pat = "test-pat" };
            using (var handler = (HttpClientHandler)RestTfsClient.CreateHttpMessageHandler(settings))
            using (var httpClient = new HttpClient(handler))
            {
                RestTfsClient.ConfigureHttpClient(httpClient, settings);

                Assert.False(handler.UseDefaultCredentials);
                Assert.Equal("Basic", httpClient.DefaultRequestHeaders.Authorization.Scheme);
            }
        }

        [TestMethod]
        public void UsesCurrentWindowsCredentialsWhenPatIsNotConfigured()
        {
            using (var handler = (HttpClientHandler)RestTfsClient.CreateHttpMessageHandler(new GitTfsSettings()))
            {
                Assert.True(handler.UseDefaultCredentials);
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
        public void ScopesChangesetQueriesToTheRequestedPath()
        {
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"count\":0,\"value\":[]}", Encoding.UTF8, "application/json"),
            });

            using (var httpClient = new HttpClient(handler))
            using (var client = new RestTfsClient(httpClient, "https://tfs.example/tfs/DefaultCollection", "$/Project/Branch"))
            {
                client.GetChangesets("$/Project/Branch", 0, 100);

                Assert.Equal("$/Project/Branch", GetQueryValue(handler.Requests[0].RequestUri,
                    "searchCriteria.itemPath"));
                StringAssert.Contains(handler.Requests[0].RequestUri.Query, "%24top=100");
            }
        }

        private static string GetQueryValue(Uri uri, string key)
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                if (separator < 0)
                    continue;

                var name = Uri.UnescapeDataString(pair.Substring(0, separator));
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pair.Substring(separator + 1));
            }

            return null;
        }

        [TestMethod]
        public void ChangesetScanBeyondTheLastChangesetIsAnEmptyPage()
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"TF14019: The changeset 43 does not exist.\",\"typeKey\":\"ChangesetNotFoundException\"}")
            };
            using var http = new HttpClient(new QueueHandler(response));
            using var client = new RestTfsClient(http, "https://tfs.example/collection", "$/Project/Main");

            Assert.Empty(client.GetChangesets("$/Project/Main", 43, 1));
        }

        [TestMethod]
        public void MissingProjectDuringChangesetScanStillFails()
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"typeKey\":\"ProjectDoesNotExistException\"}")
            };
            using var http = new HttpClient(new QueueHandler(response));
            using var client = new RestTfsClient(http, "https://tfs.example/collection", "$/Project/Main");

            Assert.Throws<RestTfsException>(() => client.GetChangesets("$/Project/Main", 43, 1));
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
