namespace GitTfs.Test.Commands
{
    using System.Diagnostics;
    using System.Globalization;
    using System.Net;
    using System.Net.Http;
    using System.Diagnostics.Metrics;
    using GitTfs.Commands;
    using GitTfs.Core;
    using GitTfs.Core.RestTfs;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Spectre.Console;

    [TestClass]
    public class ConsoleMetricsTests
    {
        [TestMethod]
        public void FooterShowsCountsAndLastRequestTimesInsteadOfAverages()
        {
            using var metrics = new ConsoleMetrics();
            using var meter = new Meter(GitTfsMetrics.MeterName);
            var requests = meter.CreateCounter<long>("gittfs.requests");
            var duration = meter.CreateHistogram<double>("gittfs.request.duration", "ms");
            var history = new KeyValuePair<string, object>("operation", "History requests");
            var download = new KeyValuePair<string, object>("operation", "File downloads");
            requests.Add(2, history);
            requests.Add(2, download);
            duration.Record(10, history);
            duration.Record(15, download);
            duration.Record(5, download);
            duration.Record(23, history);
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output),
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
            });
            console.Profile.Width = 180;
            console.Write(metrics.RenderDisplay(live: false));
            StringAssert.Contains(output.ToString(), "🌐 4 23ms   ⬇ 2 5ms");
            Assert.IsFalse(output.ToString().Contains("Avg"));
            Assert.AreEqual(10d, metrics.Snapshot().Single(row => row.Name == "File downloads").AverageMilliseconds);
        }

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
            Assert.AreEqual(1L, metrics.Snapshot().Single(row => row.Name == "Retries").Count);
            Assert.AreEqual(0L, metrics.Snapshot().Single(row => row.Name == "Throttles").Count,
                "A temporary HTTP 503 without throttle headers must not be reported as a rate limit.");
            Assert.IsNotNull(row.AverageMilliseconds);
            Assert.IsTrue(timer.Elapsed.TotalMilliseconds - row.AverageMilliseconds.Value * row.Count > 500,
                "The one-second retry backoff must not be included in HTTP request timings.");
        }

        [TestMethod]
        [DataRow("en-US")]
        [DataRow("sv-SE")]
        public void ThrottleHeadersAndRetryCountdownAreVisibleAndUseInvariantUnits(string culture)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var clock = new ManualTimeProvider();
                using var metrics = new ConsoleMetrics(clock);
                var headers = new Dictionary<string, string>
                {
                    ["Retry-After"] = "3", ["X-RateLimit-Remaining"] = "0",
                    ["X-RateLimit-Limit"] = "200", ["X-RateLimit-Delay"] = "0.125"
                };
                GitTfsMetrics.RecordResponse(AzureDevOpsRateLimit.FromValues(name => headers.GetValueOrDefault(name), 429));
                using (GitTfsMetrics.MeasureWait(TimeSpan.FromSeconds(3), "Retry-After", throttled: true, 2, 3))
                {
                    clock.Advance(TimeSpan.FromSeconds(1.25));
                    var api = metrics.ApiSnapshot();
                    Assert.IsTrue(api.Waiting);
                    Assert.IsTrue(api.Throttled);
                    Assert.AreEqual(1.75, api.RemainingSeconds, .001);
                    Assert.AreEqual(0d, api.RateRemaining);
                    Assert.AreEqual(200d, api.RateLimit);
                    Assert.AreEqual(.125, api.ServerDelaySeconds);
                    using var output = new StringWriter();
                    var console = AnsiConsole.Create(new AnsiConsoleSettings
                    {
                        Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output),
                        Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
                    });
                    console.Profile.Width = 180;
                    console.Write(metrics.RenderDisplay(live: false));
                    StringAssert.Contains(output.ToString(), "API throttled · attempt 2/3 · 2s");
                    StringAssert.Contains(output.ToString(), "Rate 0/200 TSTU");
                    StringAssert.Contains(output.ToString(), "Throttles 1 · Retries 1");
                    StringAssert.Contains(output.ToString(), "Last Retry-After: 3s");
                    StringAssert.Contains(output.ToString(), "Server delay: 0.125s");
                }
                GitTfsMetrics.RecordResponse(AzureDevOpsRateLimit.FromValues(name => name == "X-RateLimit-Remaining" ? "197" : null, 200));
                Assert.IsFalse(metrics.ApiSnapshot().Waiting);
                Assert.AreEqual(197d, metrics.ApiSnapshot().RateRemaining);
                Assert.AreEqual(1L, metrics.Snapshot().Single(row => row.Name == "Retries").Count);
                Assert.AreEqual(1L, metrics.Snapshot().Single(row => row.Name == "Throttles").Count);
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }
        }

        [TestMethod]
        [DataRow("X-MS-Retry-After-MS", "1250", "Last X-MS-Retry-After-MS: 1250ms")]
        [DataRow("X-After", "1.25", "Last X-After: 1.25s")]
        public void AlternateRetryHeadersDisplayTheirCorrectUnits(string header, string value, string expected)
        {
            using var metrics = new ConsoleMetrics();
            GitTfsMetrics.RecordResponse(AzureDevOpsRateLimit.FromValues(name => name == header ? value : null, 429));
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output),
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
            });
            console.Profile.Width = 180;
            console.Write(metrics.RenderDisplay(live: false));
            StringAssert.Contains(output.ToString(), expected);
        }

        [TestMethod]
        public void HealthyRateBudgetResetIsNotPresentedAsARequestedRetryDelay()
        {
            using var metrics = new ConsoleMetrics();
            var headers = new Dictionary<string, string>
            {
                ["X-RateLimit-Remaining"] = "150", ["X-RateLimit-Limit"] = "200",
                ["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            };
            GitTfsMetrics.RecordResponse(AzureDevOpsRateLimit.FromValues(name => headers.GetValueOrDefault(name), 200));
            Assert.AreEqual(150d, metrics.ApiSnapshot().RateRemaining);
            Assert.IsNull(metrics.ApiSnapshot().SuggestedDelaySeconds);
            Assert.AreEqual(0L, metrics.Snapshot().Single(row => row.Name == "Throttles").Count);
        }

        [TestMethod]
        public void SuccessfulResponseRetryAfterDelaysTheNextRequestWithoutCountingARetry()
        {
            using var metrics = new ConsoleMetrics();
            using var http = new HttpClient(new SuccessfulThrottleHandler());
            using var client = new RestTfsClient(http, "https://tfs.example/collection", "$/Project/Main");
            client.GetChangesets("$/Project/Main", 0, 100);
            Assert.AreEqual(.05, metrics.ApiSnapshot().SuggestedDelaySeconds);
            client.GetChangesets("$/Project/Main", 0, 100);
            Assert.AreEqual(2L, metrics.Snapshot().Single(row => row.Name == "History requests").Count);
            Assert.AreEqual(0L, metrics.Snapshot().Single(row => row.Name == "Retries").Count);
            Assert.AreEqual(1L, metrics.Snapshot().Single(row => row.Name == "Throttles").Count);
            Assert.IsFalse(metrics.ApiSnapshot().Waiting);
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

        [TestMethod]
        public void LiveTableRefreshesOncePerSecondAndFinalTableIsAlwaysFresh()
        {
            var clock = new ManualTimeProvider();
            using var metrics = new ConsoleMetrics(clock);
            var initial = metrics.RenderLive();
            GitTfsMetrics.RecordResponse(AzureDevOpsRateLimit.FromValues(name => name switch
            {
                "X-RateLimit-Limit" => "200",
                "X-RateLimit-Remaining" => "150.5",
                _ => null
            }, 200));
            using (GitTfsMetrics.MeasureRequest("History requests")) { }
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.AreSame(initial, metrics.RenderLive());
            Assert.AreEqual(1L, metrics.Snapshot().Single(row => row.Name == "History requests").Count,
                "Measurements must continue to be collected between table refreshes.");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var refreshed = metrics.RenderLive();
            Assert.AreNotSame(initial, refreshed);
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output),
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
            });
            console.Profile.Width = 180;
            console.Write(initial);
            StringAssert.Contains(output.ToString(), "Rate —/— TSTU");
            output.GetStringBuilder().Clear();
            console.Write(refreshed);
            StringAssert.Contains(output.ToString(), "Rate 150.5/200 TSTU");
            using (GitTfsMetrics.MeasureRequest("History requests")) { }
            Assert.AreSame(refreshed, metrics.RenderLive());
            Assert.AreNotSame(refreshed, metrics.Render(), "The final summary must bypass the live refresh interval.");
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long timestamp;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => timestamp;
            public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
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

        private sealed class SuccessfulThrottleHandler : HttpMessageHandler
        {
            private int requests;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"count\":0,\"value\":[]}")
                };
                if (++requests == 1) response.Headers.TryAddWithoutValidation("Retry-After", "0.05");
                return Task.FromResult(response);
            }
        }
    }
}
