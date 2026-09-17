using GitTfs.Core;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Reflection;

namespace GitTfs.VsCommon
{
    public static class Retry
    {
        public static void Do(Action action) => Do(action, TimeSpan.FromSeconds(1));

        public static void Do(Action action, TimeSpan retryInterval, int retryCount = 10) => Do<object>(() =>
                                                                                                      {
                                                                                                          action();
                                                                                                          return null;
                                                                                                      }, retryInterval, retryCount);

        public static T Do<T>(Func<T> action) => Do(action, TimeSpan.FromSeconds(1));

        public static T Do<T>(Func<T> action, TimeSpan retryInterval, int retryCount = 10)
        {
            var exceptions = new List<Exception>();

            for (int retry = 0; retry < retryCount; retry++)
            {
                try
                {
                    return action();
                }
                catch (Microsoft.TeamFoundation.TeamFoundationServerException ex)
                {
                    exceptions.Add(ex);
                    WaitBeforeRetry(ex, retryInterval, retry, retryCount);
                }
                catch (WebException ex)
                {
                    exceptions.Add(ex);
                    WaitBeforeRetry(ex, retryInterval, retry, retryCount);
                }
                catch (GitTfsException ex) // allows continue of catch (MappingConflictException e) throw as innerexception
                {
                    exceptions.Add(ex);
                    WaitBeforeRetry(ex, retryInterval, retry, retryCount);
                }
            }

            throw new AggregateException(exceptions);
        }

        private static void WaitBeforeRetry(Exception exception, TimeSpan retryInterval, int retry, int retryCount)
        {
            if (retry >= retryCount - 1)
                return;

            var rateLimit = FindRateLimit(exception);
            // Preserve the existing retry cadence for ordinary TFS errors. When
            // Azure DevOps sends rate-limit metadata, the server-directed delay
            // below takes precedence and can be fractional or much longer.
            var delay = rateLimit?.GetServerDelay(DateTimeOffset.UtcNow)
                        ?? retryInterval;

            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            var rateDescription = rateLimit == null || !rateLimit.HasHeaders
                ? string.Empty
                : " [Azure DevOps headers: " + rateLimit.ToLogString() + "]";
            Trace.WriteLine("Retrying TFS request (attempt " + (retry + 2) + "/" + retryCount + ") after "
                            + delay.ToString() + rateDescription + ". Error: " + exception.Message);
            Thread.Sleep(delay);
        }

        private static AzureDevOpsRateLimit FindRateLimit(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                var response = (current as WebException)?.Response;
                if (response != null)
                {
                    var webResponse = response as HttpWebResponse;
                    return AzureDevOpsRateLimit.FromHeaders(response.Headers, webResponse == null ? (int?)null : (int)webResponse.StatusCode);
                }

                var statusCode = GetStatusCode(current);
                var headers = GetHeaders(current);
                if (headers != null)
                {
                    var rateLimit = AzureDevOpsRateLimit.FromValues(GetHeaderGetter(headers), statusCode);
                    if (rateLimit.HasHeaders || rateLimit.StatusCode.HasValue)
                        return rateLimit;
                }

                if (statusCode.HasValue)
                    return AzureDevOpsRateLimit.FromValues(_ => null, statusCode);
            }

            return null;
        }

        private static int? GetStatusCode(object value)
        {
            var property = value.GetType().GetProperty("StatusCode", BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
                return null;

            try
            {
                return Convert.ToInt32(property.GetValue(value), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static object GetHeaders(object value)
        {
            foreach (var propertyName in new[] { "ResponseHeaders", "Headers" })
            {
                var property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null)
                    continue;

                try
                {
                    var headers = property.GetValue(value);
                    if (headers != null)
                        return headers;
                }
                catch
                {
                    // Some SDK exception properties throw when no response exists.
                }
            }

            var responseProperty = value.GetType().GetProperty("Response", BindingFlags.Instance | BindingFlags.Public);
            if (responseProperty == null)
                return null;

            try
            {
                var response = responseProperty.GetValue(value);
                return response?.GetType().GetProperty("Headers", BindingFlags.Instance | BindingFlags.Public)?.GetValue(response);
            }
            catch
            {
                return null;
            }
        }

        private static Func<string, string> GetHeaderGetter(object headers)
        {
            if (headers is NameValueCollection nameValueCollection)
                return name => nameValueCollection[name];

            if (headers is IDictionary dictionary)
                return name => dictionary[name]?.ToString();

            var tryGetValues = headers.GetType().GetMethod("TryGetValues", new[] { typeof(string), typeof(IEnumerable<string>).MakeByRefType() });
            if (tryGetValues != null)
            {
                return name =>
                {
                    var arguments = new object[] { name, null };
                    try
                    {
                        if ((bool)tryGetValues.Invoke(headers, arguments))
                            return (arguments[1] as IEnumerable)?.Cast<object>().FirstOrDefault()?.ToString();
                    }
                    catch
                    {
                        // Ignore unsupported SDK header implementations.
                    }
                    return null;
                };
            }

            var indexer = headers.GetType().GetProperty("Item", new[] { typeof(string) });
            if (indexer != null)
                return name =>
                {
                    try { return indexer.GetValue(headers, new object[] { name })?.ToString(); }
                    catch { return null; }
                };

            return _ => null;
        }

        public static void DoWhile(Func<bool> action, int retryCount = 10) => DoWhile(action, TimeSpan.FromSeconds(0), retryCount);

        public static void DoWhile(Func<bool> action, TimeSpan retryInterval, int retryCount = 10)
        {
            int count = 0;
            while (action())
            {
                count++;
                if (count > retryCount)
                    throw new GitTfsException("error: Action failed after " + retryCount + " retries!");
                Thread.Sleep(retryInterval);
            }
        }
    }
}
