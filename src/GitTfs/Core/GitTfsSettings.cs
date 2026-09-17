
namespace GitTfs.Core
{
    using global::System.Diagnostics;
    using global::System.Net;
    using global::System.Text.Json;
    using global::System.Text.Json.Serialization;
    /// <summary>
    /// Settings that apply to the local git-tfs executable. Credentials are kept
    /// out of this file and continue to use the existing TFS credential flow.
    /// </summary>
    public sealed class GitTfsSettings
    {
        public string TargetServer { get; set; }

        [JsonPropertyName("api-version")]
        public string ApiVersion { get; set; } = "7.1";

        public string Username { get; set; }

        public string Password { get; set; }

        public string Pat { get; set; }

        public bool Resumable { get; set; } = true;

        [JsonPropertyName("batch-size")]
        public int BatchSize { get; set; } = 1;

        [JsonPropertyName("no-parallel")]
        public bool NoParallel { get; set; } = true;

        public bool Debug { get; set; } = true;

        /// <summary>
        /// HTTP(S) proxy URL. Null, empty, or "none" disables proxy use.
        /// </summary>
        public string Proxy { get; set; }

        public string SourcePath { get; private set; }

        public void ApplyProxySettings()
        {
            if (string.IsNullOrWhiteSpace(Proxy) || string.Equals(Proxy.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                WebRequest.DefaultWebProxy = null;
                Trace.WriteLine("HTTP(S) proxy disabled; using direct connections.");
                return;
            }

            if (!Uri.TryCreate(Proxy.Trim(), UriKind.Absolute, out var proxyUri)
                || (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps))
                throw new GitTfsException("The configured proxy must be an absolute HTTP(S) URI or 'none'.");

            WebRequest.DefaultWebProxy = new WebProxy(proxyUri, false);
            Trace.WriteLine("HTTP(S) proxy enabled from appsettings.");
        }

        public static GitTfsSettings Load()
        {
            var configuredPath = Environment.GetEnvironmentVariable("GIT_TFS_APPSETTINGS");
            var candidates = new[]
            {
                configuredPath,
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                Path.Combine(Environment.CurrentDirectory, "appsettings.json")
            };

            foreach (var path in candidates.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    var settings = JsonSerializer.Deserialize<GitTfsSettings>(File.ReadAllText(path), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new GitTfsSettings();
                    settings.TargetServer = settings.TargetServer?.Trim().TrimEnd('/');
                    settings.SourcePath = path;
                    return settings;
                }
                catch (JsonException ex)
                {
                    throw new GitTfsException("Unable to read git-tfs appsettings.json at '" + path + "'.", ex);
                }
            }

            return new GitTfsSettings();
        }
    }
}
