namespace GitTfs.Core.RestTfs
{
    using System.Diagnostics;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;

    /// <summary>Opt-in capture of application HTTP exchanges, including retries and binary bodies.</summary>
    public sealed class HttpTrafficCaptureHandler : DelegatingHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly string directory;
        private int sequence;

        public HttpTrafficCaptureHandler(string captureDirectory, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            directory = Path.Combine(Path.GetFullPath(captureDirectory),
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var exchange = Path.Combine(directory, Interlocked.Increment(ref sequence).ToString("D6"));
            Directory.CreateDirectory(exchange);
            var uri = request.RequestUri == null ? null : new UriBuilder(request.RequestUri)
            {
                UserName = string.Empty,
                Password = string.Empty
            }.Uri.AbsoluteUri;
            await SaveMetadata(exchange, "request", new
            {
                method = request.Method.Method,
                uri,
                headers = CaptureHeaders(request.Headers, request.Content?.Headers),
                timestamp = DateTimeOffset.UtcNow
            });
            await SaveBody(exchange, "request", request.Content, cancellationToken);
            var timer = Stopwatch.StartNew();
            HttpResponseMessage response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                await SaveMetadata(exchange, "response", new
                {
                    statusCode = (int)response.StatusCode,
                    reasonPhrase = response.ReasonPhrase,
                    headers = CaptureHeaders(response.Headers, response.Content?.Headers),
                    elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
                });
                // HttpContent caches the bytes, leaving them available to the REST client.
                await SaveBody(exchange, "response", response.Content, cancellationToken);
                return response;
            }
            catch (Exception exception)
            {
                response?.Dispose();
                await SaveMetadata(exchange, "error", new
                {
                    type = exception.GetType().FullName,
                    message = exception.Message,
                    elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
                });
                throw;
            }
        }

        private static Dictionary<string, string[]> CaptureHeaders(params HttpHeaders[] headerSets)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in headerSets.Where(set => set != null))
                foreach (var header in set)
                    headers[header.Key] = IsCredentialHeader(header.Key)
                        ? new[] { "[redacted]" } : header.Value.ToArray();
            return headers;
        }

        private static bool IsCredentialHeader(string name)
            => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase);

        private static Task SaveMetadata(string exchange, string name, object value)
            => File.WriteAllTextAsync(Path.Combine(exchange, name + ".json"), JsonSerializer.Serialize(value, JsonOptions));

        private static async Task SaveBody(string exchange, string name, HttpContent content,
            CancellationToken cancellationToken)
        {
            var bytes = content == null ? Array.Empty<byte>()
                : await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(exchange, name + ".body"), bytes, cancellationToken);
        }
    }
}
