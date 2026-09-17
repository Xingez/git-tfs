using System.Collections.Specialized;
using GitTfs.Core;

namespace GitTfs.Test.Core
{
    [TestClass]
    public class AzureDevOpsRateLimitTests : BaseTest
    {
        [TestMethod]
        public void RetryAfterSecondsTakesPrecedence()
        {
            var headers = new NameValueCollection
            {
                { "Retry-After", "12" },
                { "X-RateLimit-Delay", "1.5" },
                { "X-RateLimit-Reset", "4102444800" }
            };

            var unixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var delay = AzureDevOpsRateLimit.FromHeaders(headers, 429).GetServerDelay(unixEpoch);

            Assert.Equal(TimeSpan.FromSeconds(12), delay);
        }

        [TestMethod]
        public void RetryAfterHttpDateIsSupported()
        {
            var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
            var headers = new NameValueCollection { { "Retry-After", now.AddSeconds(20).ToString("R") } };

            var delay = AzureDevOpsRateLimit.FromHeaders(headers, 429).GetServerDelay(now);

            Assert.Equal(TimeSpan.FromSeconds(20), delay);
        }

        [TestMethod]
        public void XAfterSecondsIsSupported()
        {
            var headers = new NameValueCollection { { "X-After", "7" } };

            var delay = AzureDevOpsRateLimit.FromHeaders(headers, 503).GetServerDelay(DateTimeOffset.UtcNow, out var source);

            Assert.Equal(TimeSpan.FromSeconds(7), delay);
            Assert.Equal("X-After", source);
        }

        [TestMethod]
        public void MicrosoftRetryAfterMillisecondsAreSupported()
        {
            var headers = new NameValueCollection { { "X-MS-Retry-After-MS", "250" } };

            var delay = AzureDevOpsRateLimit.FromHeaders(headers, 429).GetServerDelay(DateTimeOffset.UtcNow, out var source);

            Assert.Equal(TimeSpan.FromMilliseconds(250), delay);
            Assert.Equal("X-MS-Retry-After-MS", source);
        }

        [TestMethod]
        public void ResetAndDecimalDelayAreParsed()
        {
            var now = DateTimeOffset.FromUnixTimeSeconds(4102444790);
            var headers = new NameValueCollection
            {
                { "X-RateLimit-Reset", "4102444800" },
                { "X-RateLimit-Delay", "0.250" }
            };

            var delay = AzureDevOpsRateLimit.FromHeaders(headers, 200).GetServerDelay(now);

            Assert.Equal(TimeSpan.FromSeconds(10), delay);
        }

        [TestMethod]
        public void HeaderNamesAreCaseInsensitive()
        {
            var delay = AzureDevOpsRateLimit.FromValues(name => name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) ? "3" : null, 429)
                .GetServerDelay(DateTimeOffset.UtcNow);

            Assert.Equal(TimeSpan.FromSeconds(3), delay);
        }

        [TestMethod]
        public void LogsRateLimitMetadata()
        {
            var headers = new NameValueCollection
            {
                { "X-RateLimit-Limit", "200" },
                { "X-RateLimit-Remaining", "4" },
                { "X-RateLimit-Resource", "vso.code" }
            };

            var result = AzureDevOpsRateLimit.FromHeaders(headers).ToLogString();

            StringAssert.Contains(result, "X-RateLimit-Limit=200");
            StringAssert.Contains(result, "X-RateLimit-Remaining=4");
            StringAssert.Contains(result, "X-RateLimit-Resource=vso.code");
        }

        [TestMethod]
        public void LogsWaitHeaders()
        {
            var headers = new NameValueCollection
            {
                { "X-After", "7" },
                { "X-MS-Retry-After-MS", "250" }
            };

            var result = AzureDevOpsRateLimit.FromHeaders(headers).ToLogString();

            StringAssert.Contains(result, "X-After=7");
            StringAssert.Contains(result, "X-MS-Retry-After-MS=250");
        }
    }
}
