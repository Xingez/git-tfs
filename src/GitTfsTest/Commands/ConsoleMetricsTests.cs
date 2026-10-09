namespace GitTfs.Test.Commands
{
    using System.Diagnostics;
    using System.Net;
    using System.Net.Http;
    using GitTfs.Commands;
    using GitTfs.Core;
    using GitTfs.Core.RestTfs;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class ConsoleMetricsTests
    {
        [TestMethod]
        public void RequestCountIncludesRetriesButAverageDurationExcludesBackoff()
        {
            using var metrics = new ConsoleMetrics();
            using var http = new HttpClient(new RetryHandler());
            using var client = new RestTfsClient(http, "https://tfs.example/collection", "$/Project/Main");
            var timer = Stopwatch.StartNew();
            client.GetChangesets("$/Project/Main", 0, 100);
            timer.Stop();
            var row = metrics.Snapshot().Single(row => row.Name == "History requests");
            Assert.AreEqual(2L, row.Count);
            Assert.IsNotNull(row.AverageMilliseconds);
            Assert.IsTrue(timer.Elapsed.TotalMilliseconds - row.AverageMilliseconds.Value * row.Count > 500,
                "The one-second retry backoff must not be included in HTTP request timings.");
        }

        [TestMethod]
        public void NewDisplayStartsWithEmptyCountersAndInFlightRequestsHaveNoAverage()
        {
            using (var first = new ConsoleMetrics())
            using (GitTfsMetrics.MeasureRequest("File downloads")) { }
            using var next = new ConsoleMetrics();
            var row = next.Snapshot().Single(row => row.Name == "File downloads");
            Assert.AreEqual(0L, row.Count);
            using (GitTfsMetrics.MeasureRequest("File downloads"))
            {
                row = next.Snapshot().Single(row => row.Name == "File downloads");
                Assert.AreEqual(1L, row.Count);
                Assert.IsNull(row.AverageMilliseconds);
            }
            Assert.IsNotNull(next.Snapshot().Single(row => row.Name == "File downloads").AverageMilliseconds);
        }

        private sealed class RetryHandler : HttpMessageHandler
        {
            private int requests;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
                => Task.FromResult(new HttpResponseMessage(++requests == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {
                    Content = new StringContent(requests == 1 ? "busy" : "{\"count\":0,\"value\":[]}")
                });
        }
    }
}
