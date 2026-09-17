using System.Collections.Specialized;
using System.Globalization;

namespace GitTfs.Core
{
    /// <summary>
    /// The rate-limit information returned by Azure DevOps.
    /// Azure DevOps communicates throttling through response headers rather than a
    /// separate rate-limit endpoint, so these values are deliberately kept
    /// transport-neutral and can be populated from either HttpWebResponse or a
    /// newer HTTP client response.
    /// </summary>
    public sealed class AzureDevOpsRateLimit
    {
        public int? StatusCode { get; private set; }
        public string RetryAfter { get; private set; }
        public string Delay { get; private set; }
        public string Reset { get; private set; }
        public string Remaining { get; private set; }
        public string Limit { get; private set; }
        public string Resource { get; private set; }
        public string Cost { get; private set; }

        public bool HasHeaders => RetryAfter != null || Delay != null || Reset != null || Remaining != null || Limit != null;

        public bool IsThrottled => StatusCode == 429 || StatusCode == 503 || RetryAfter != null;

        public static AzureDevOpsRateLimit FromHeaders(NameValueCollection headers, int? statusCode = null)
        {
            if (headers == null)
                return new AzureDevOpsRateLimit { StatusCode = statusCode };

            return FromValues(name => headers[name], statusCode);
        }

        public static AzureDevOpsRateLimit FromValues(Func<string, string> getHeader, int? statusCode = null)
        {
            if (getHeader == null)
                throw new ArgumentNullException(nameof(getHeader));

            return new AzureDevOpsRateLimit
            {
                StatusCode = statusCode,
                RetryAfter = HeaderValue(getHeader, "Retry-After"),
                Delay = HeaderValue(getHeader, "X-RateLimit-Delay"),
                Reset = HeaderValue(getHeader, "X-RateLimit-Reset"),
                Remaining = HeaderValue(getHeader, "X-RateLimit-Remaining"),
                Limit = HeaderValue(getHeader, "X-RateLimit-Limit"),
                Resource = HeaderValue(getHeader, "X-RateLimit-Resource"),
                Cost = HeaderValue(getHeader, "X-RateLimit-Cost")
            };
        }

        /// <summary>
        /// Gets the server-requested delay. Retry-After has precedence, followed by
        /// the reset time and then X-RateLimit-Delay, as recommended by Azure DevOps.
        /// </summary>
        public TimeSpan? GetServerDelay(DateTimeOffset now)
        {
            var retryAfter = ParseRetryAfter(RetryAfter, now);
            if (retryAfter.HasValue)
                return retryAfter.Value;

            if (long.TryParse(Reset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetEpoch))
            {
                try
                {
                    var resetAt = DateTimeOffset.FromUnixTimeSeconds(resetEpoch);
                    var resetDelay = resetAt - now;
                    if (resetDelay > TimeSpan.Zero)
                        return resetDelay;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Ignore malformed server metadata and continue with the
                    // next supported rate-limit hint.
                }
            }

            if (double.TryParse(Delay, NumberStyles.Float, CultureInfo.InvariantCulture, out var delaySeconds)
                && delaySeconds >= 0)
                return TimeSpan.FromSeconds(delaySeconds);

            return null;
        }

        public string ToLogString()
        {
            var values = new List<string>();
            if (StatusCode.HasValue) values.Add("status=" + StatusCode.Value.ToString(CultureInfo.InvariantCulture));
            if (RetryAfter != null) values.Add("Retry-After=" + RetryAfter);
            if (Delay != null) values.Add("X-RateLimit-Delay=" + Delay);
            if (Reset != null) values.Add("X-RateLimit-Reset=" + Reset);
            if (Remaining != null) values.Add("X-RateLimit-Remaining=" + Remaining);
            if (Limit != null) values.Add("X-RateLimit-Limit=" + Limit);
            if (Resource != null) values.Add("X-RateLimit-Resource=" + Resource);
            if (Cost != null) values.Add("X-RateLimit-Cost=" + Cost);
            return string.Join(", ", values);
        }

        private static string HeaderValue(Func<string, string> getHeader, string name)
        {
            var value = getHeader(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static TimeSpan? ParseRetryAfter(string value, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                return TimeSpan.FromSeconds(seconds);

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retryAt))
            {
                var delay = retryAt - now;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }

            return null;
        }
    }
}
