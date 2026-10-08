namespace GitTfs.Core.RestTfs
{
    using global::GitTfs.Core;
    using global::System.Diagnostics;
    using global::System.Globalization;
    using global::System.Net;
    using global::System.Net.Http;
    using global::System.Net.Http.Headers;
    using global::System.Text;
    using global::System.Text.Json;
    using global::Microsoft.Extensions.Logging;

    /// <summary>
    /// Small, transport-level TFVC REST client used by the REST clone pipeline.
    /// It deliberately returns the response boundary instead of hiding headers
    /// behind the legacy TFS object model.
    /// </summary>
    public sealed class RestTfsClient : IDisposable
    {
        private const int MaxRequestAttempts = 10;
        private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

        private readonly HttpClient httpClientField;
        private readonly bool ownsHttpClientField;
        private readonly Uri serverUriField;
        private readonly string projectField;
        private readonly string repositoryPathField;
        private readonly string apiVersionField;
        private readonly ILogger<RestTfsClient> loggerField;
        private readonly JsonSerializerOptions jsonOptionsField = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        private TimeSpan pendingServerDelayField;

        public RestTfsClient(string serverUrl, string repositoryPath, GitTfsSettings settings,
            ILogger<RestTfsClient> logger = null)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri))
                throw new GitTfsException("TargetServer must be an absolute HTTP(S) URI.");

            if (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps)
                throw new GitTfsException("TargetServer must use HTTP or HTTPS.");

            serverUriField = new Uri(serverUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
            projectField = ExtractProject(repositoryPath);
            repositoryPathField = NormalizeServerPath(repositoryPath);
            apiVersionField = string.IsNullOrWhiteSpace(settings.ApiVersion) ? "7.1" : settings.ApiVersion.Trim();
            loggerField = logger;
            httpClientField = CreateHttpClient(settings);
            ownsHttpClientField = true;
        }

        public RestTfsClient(HttpClient httpClient, string serverUrl, string repositoryPath, string apiVersion = "7.1",
            ILogger<RestTfsClient> logger = null)
        {
            httpClientField = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ownsHttpClientField = false;
            loggerField = logger;

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri))
                throw new ArgumentException("The server URL must be absolute.", nameof(serverUrl));

            serverUriField = new Uri(serverUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
            projectField = ExtractProject(repositoryPath);
            repositoryPathField = NormalizeServerPath(repositoryPath);
            apiVersionField = string.IsNullOrWhiteSpace(apiVersion) ? "7.1" : apiVersion.Trim();
        }

        public IReadOnlyList<RestChangesetReference> GetChangesets(string repositoryPath, int fromChangesetId,
            int batchSize, bool filterByItemPath = true)
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("$orderby", "id asc"),
                new KeyValuePair<string, string>("$top", (batchSize > 0 ? batchSize : 100).ToString(CultureInfo.InvariantCulture)),
            };
            if (filterByItemPath)
                query.Insert(0, new KeyValuePair<string, string>("searchCriteria.itemPath", repositoryPath));
            if (fromChangesetId > 0)
                query.Add(new KeyValuePair<string, string>("searchCriteria.fromId", fromChangesetId.ToString(CultureInfo.InvariantCulture)));

            var response = GetJson<RestPage<RestChangesetReference>>(BuildUri("changesets", query));
            return response.Value?.Value ?? new List<RestChangesetReference>();
        }

        public RestChangeset GetChangeset(int changesetId)
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("maxChangeCount", "100"),
                new KeyValuePair<string, string>("includeSourceRename", "true"),
            };

            var response = GetJson<RestChangeset>(BuildUri("changesets/" + changesetId.ToString(CultureInfo.InvariantCulture), query));
            var changeset = response.Value ?? new RestChangeset { ChangesetId = changesetId };
            changeset.Changes ??= new List<RestChange>();

            if (!changeset.HasMoreChanges)
                return changeset;

            var continuationToken = FindHeader(response.Headers, "x-ms-continuationtoken");
            var skip = changeset.Changes.Count;
            while (changeset.HasMoreChanges)
            {
                var pageQuery = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("$top", "100"),
                };
                if (!string.IsNullOrWhiteSpace(continuationToken))
                    pageQuery.Add(new KeyValuePair<string, string>("continuationToken", continuationToken));
                else
                    pageQuery.Add(new KeyValuePair<string, string>("$skip", skip.ToString(CultureInfo.InvariantCulture)));

                // The changeset-changes route is collection-scoped; unlike the
                // list, detail, and item routes it does not contain the project.
                var page = GetJson<RestPage<RestChange>>(BuildUri(
                    "changesets/" + changesetId.ToString(CultureInfo.InvariantCulture) + "/changes",
                    pageQuery,
                    includeProject: false));
                var pageChanges = page.Value?.Value ?? new List<RestChange>();
                changeset.Changes.AddRange(pageChanges);
                skip += pageChanges.Count;
                continuationToken = FindHeader(page.Headers, "x-ms-continuationtoken");
                changeset.HasMoreChanges = !string.IsNullOrWhiteSpace(continuationToken)
                    || pageChanges.Count > 0 && pageChanges.Count >= 100;

                if (pageChanges.Count == 0 && string.IsNullOrWhiteSpace(continuationToken))
                    changeset.HasMoreChanges = false;
            }

            return changeset;
        }

        public byte[] DownloadFile(string path, int changesetId)
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("path", path),
                new KeyValuePair<string, string>("download", "true"),
                new KeyValuePair<string, string>("versionDescriptor.version", changesetId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("versionDescriptor.versionType", "Changeset"),
            };

            return GetBytes(BuildUri("items", query), GetRelativeFilePath(path)).Value ?? Array.Empty<byte>();
        }

        public void Dispose()
        {
            if (ownsHttpClientField)
                httpClientField.Dispose();
        }

        private RestResponse<T> GetJson<T>(Uri uri)
        {
            return Send(uri, false, response =>
            {
                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonSerializer.Deserialize<T>(json, jsonOptionsField);
            }, uri.AbsolutePath);
        }

        private RestResponse<byte[]> GetBytes(Uri uri, string relativeFilePath)
        {
            return Send(uri, true, response => response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(), relativeFilePath);
        }

        private RestResponse<T> Send<T>(Uri uri, bool binary, Func<HttpResponseMessage, T> readResponse, string logTarget)
        {
            logTarget = string.IsNullOrWhiteSpace(logTarget) ? uri.AbsolutePath : logTarget;
            for (var attempt = 1; attempt <= MaxRequestAttempts; attempt++)
            {
                WaitForPendingServerDelay(logTarget);
                var requestTimer = Stopwatch.StartNew();
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/octet-stream" : "application/json"));
                    loggerField?.LogDebug("TFS request: GET {RequestTarget}", logTarget);

                    HttpResponseMessage response;
                    try
                    {
                        response = httpClientField.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                    }
                    catch (Exception ex) when (IsTransientException(ex) && attempt < MaxRequestAttempts)
                    {
                        WaitBeforeRetry(uri, attempt, null, DefaultRetryDelay, "transport failure", ex.Message);
                        continue;
                    }

                    using (response)
                    {
                        var headers = ReadHeaders(response);
                        var rateLimit = AzureDevOpsRateLimit.FromValues(
                            name => headers.TryGetValue(name, out var value) ? value : null,
                            (int)response.StatusCode);
                        if (rateLimit.HasHeaders || response.StatusCode >= HttpStatusCode.BadRequest)
                            loggerField?.LogDebug("TFS response {StatusCode} for {RequestUrl}: {RateLimit}",
                                (int)response.StatusCode, uri, rateLimit.ToLogString());

                        if (response.IsSuccessStatusCode)
                        {
                            var serverDelay = rateLimit.GetServerDelay(DateTimeOffset.UtcNow, out var delaySource);
                            if (rateLimit.IsThrottled && serverDelay.HasValue)
                            {
                                pendingServerDelayField = Max(pendingServerDelayField, serverDelay.Value);
                                loggerField?.LogInformation("TFS server requested {Delay} before the next request (source: {DelaySource}).",
                                    FormatDuration(serverDelay.Value), delaySource);
                            }

                            var result = readResponse(response);
                            var completionMessage = attempt == 1
                                ? "TFS request completed in " + FormatDuration(requestTimer.Elapsed) + "."
                                : "TFS retry request " + attempt + "/" + MaxRequestAttempts
                                    + " completed in " + FormatDuration(requestTimer.Elapsed) + ".";
                            loggerField?.LogDebug("{CompletionMessage}", completionMessage);
                            return new RestResponse<T>(result, headers, (int)response.StatusCode);
                        }

                        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        var retryable = IsRetryableStatus(response.StatusCode) || rateLimit.IsThrottled;
                        var serverDelayForRetry = rateLimit.GetServerDelay(DateTimeOffset.UtcNow, out var delaySourceForRetry);
                        if (!retryable || attempt >= MaxRequestAttempts)
                        {
                            throw new RestTfsException("TFS REST request failed with HTTP "
                                + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " for " + uri
                                + ". " + TrimBody(body), (int)response.StatusCode, uri);
                        }

                        WaitBeforeRetry(uri, attempt, serverDelayForRetry, DefaultRetryDelay,
                            delaySourceForRetry ?? "default retry interval",
                            "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ": " + TrimBody(body));
                    }
                }
            }

            throw new GitTfsException("TFS REST request retry limit was reached for " + uri + ".");
        }

        private void WaitForPendingServerDelay(string logTarget)
        {
            if (pendingServerDelayField <= TimeSpan.Zero)
                return;

            var delay = pendingServerDelayField;
            pendingServerDelayField = TimeSpan.Zero;
            loggerField?.LogInformation("Waiting {Delay} before TFS request: {RequestTarget}.",
                FormatDuration(delay), logTarget);
            var waitTimer = Stopwatch.StartNew();
            Thread.Sleep(delay);
            loggerField?.LogInformation("TFS request wait completed in {Elapsed} (requested {Delay}).",
                FormatDuration(waitTimer.Elapsed), FormatDuration(delay));
        }

        private void WaitBeforeRetry(Uri uri, int attempt, TimeSpan? serverDelay, TimeSpan defaultDelay, string source, string reason)
        {
            var delay = serverDelay ?? defaultDelay;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            loggerField?.LogWarning("Waiting {Delay} before TFS retry request {RetryAttempt}/{MaxAttempts} (source: {DelaySource}, url: {RequestUrl}). {Reason}",
                FormatDuration(delay), attempt + 1, MaxRequestAttempts, source, uri, reason);
            var waitTimer = Stopwatch.StartNew();
            Thread.Sleep(delay);
            loggerField?.LogInformation("TFS retry wait completed in {Elapsed} (requested {Delay}).",
                FormatDuration(waitTimer.Elapsed), FormatDuration(delay));
        }

        private Uri BuildUri(string resource, IEnumerable<KeyValuePair<string, string>> query = null,
            bool includeProject = true)
        {
            var path = (includeProject ? Uri.EscapeDataString(projectField) + "/" : string.Empty)
                + "_apis/tfvc/" + resource;
            var values = (query ?? Enumerable.Empty<KeyValuePair<string, string>>()).ToList();
            values.Add(new KeyValuePair<string, string>("api-version", apiVersionField));
            var queryString = string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value ?? string.Empty)));
            return new Uri(serverUriField, path + "?" + queryString);
        }

        private static string ExtractProject(string repositoryPath)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath) || !repositoryPath.StartsWith("$/", StringComparison.Ordinal))
                throw new GitTfsException("The TFS repository path must start with '$/'.");

            var path = repositoryPath.Substring(2).Trim('/');
            var separator = path.IndexOf('/');
            var project = separator < 0 ? path : path.Substring(0, separator);
            if (string.IsNullOrWhiteSpace(project))
                throw new GitTfsException("The TFS repository path must include a team project.");
            return project;
        }

        private static HttpClient CreateHttpClient(GitTfsSettings settings)
        {
            var handler = new HttpClientHandler();
            if (string.IsNullOrWhiteSpace(settings.Proxy) || string.Equals(settings.Proxy.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                handler.UseProxy = false;
            }
            else
            {
                if (!Uri.TryCreate(settings.Proxy.Trim(), UriKind.Absolute, out var proxyUri))
                    throw new GitTfsException("The configured proxy must be an absolute HTTP(S) URI or 'none'.");
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(proxyUri, false);
            }

            var pat = string.IsNullOrWhiteSpace(settings.Pat)
                ? Environment.GetEnvironmentVariable("GIT_TFS_PAT", EnvironmentVariableTarget.Process)
                    ?? Environment.GetEnvironmentVariable("GIT_TFS_PAT", EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable("GIT_TFS_PAT", EnvironmentVariableTarget.Machine)
                : settings.Pat;
            if (!string.IsNullOrWhiteSpace(pat))
            {
                handler.UseDefaultCredentials = false;
                var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pat));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                return client;
            }

            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                handler.UseDefaultCredentials = false;
                handler.Credentials = BuildCredential(settings.Username, settings.Password);
            }
            else
            {
                handler.UseDefaultCredentials = true;
            }

            return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        }

        private static NetworkCredential BuildCredential(string username, string password)
        {
            var separator = username.IndexOf('\\');
            if (separator > 0)
                return new NetworkCredential(username.Substring(separator + 1), password, username.Substring(0, separator));
            return new NetworkCredential(username, password);
        }

        private static IReadOnlyDictionary<string, string> ReadHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in response.Headers)
                headers[pair.Key] = string.Join(",", pair.Value);
            foreach (var pair in response.Content.Headers)
                headers[pair.Key] = string.Join(",", pair.Value);
            return headers;
        }

        private static string FindHeader(IReadOnlyDictionary<string, string> headers, string name)
            => headers != null && headers.TryGetValue(name, out var value) ? value : null;

        private static bool IsTransientException(Exception exception)
            => exception is HttpRequestException || exception is TaskCanceledException || exception is IOException || exception is WebException;

        private static bool IsRetryableStatus(HttpStatusCode statusCode)
            => statusCode == HttpStatusCode.RequestTimeout
                || statusCode == (HttpStatusCode)429
                || statusCode == HttpStatusCode.BadGateway
                || statusCode == HttpStatusCode.ServiceUnavailable
                || statusCode == HttpStatusCode.GatewayTimeout
                || (int)statusCode >= 500;

        private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

        private static string FormatDuration(TimeSpan duration) => duration.ToString("c", CultureInfo.InvariantCulture);

        private string GetRelativeFilePath(string path)
        {
            var normalizedPath = NormalizeServerPath(path);
            var repositoryPrefix = repositoryPathField + "/";
            return normalizedPath.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(repositoryPrefix.Length)
                : normalizedPath;
        }

        private static string NormalizeServerPath(string path) => (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');

        private static string TrimBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "No response body.";
            var trimmed = body.Trim();
            return trimmed.Length <= 500 ? trimmed : trimmed.Substring(0, 500) + "...";
        }
    }
}
