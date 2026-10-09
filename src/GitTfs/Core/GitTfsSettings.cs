
namespace GitTfs.Core
{
    using System.Diagnostics;
    using System.Globalization;
    using System.Net;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    /// <summary>
    /// Settings that apply to the local git-tfs executable.
    /// </summary>
    public sealed class GitTfsSettings
    {
        public string TargetServer { get; set; }

        [JsonPropertyName("api-version")]
        public string ApiVersion { get; set; } = "7.1";

        public string Pat { get; set; }

        [JsonPropertyName("batch-size")]
        public int BatchSize { get; set; } = 1;

        public bool Debug { get; set; }

        /// <summary>
        /// HTTP(S) proxy URL. Null, empty, or "none" disables proxy use.
        /// </summary>
        public string Proxy { get; set; }

        /// <summary>Optional directory for complete HTTP request/response captures.</summary>
        public string HttpCaptureDirectory { get; set; }

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
            Trace.WriteLine("HTTP(S) proxy enabled from configuration.");
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
                    settings.SourcePath = path;
                    settings.ApplyEnvironmentOverrides();
                    settings.TargetServer = settings.TargetServer?.Trim().TrimEnd('/');
                    return settings;
                }
                catch (JsonException ex)
                {
                    throw new GitTfsException("Unable to read git-tfs appsettings.json at '" + path + "'.", ex);
                }
            }

            var defaultSettings = new GitTfsSettings();
            defaultSettings.ApplyEnvironmentOverrides();
            defaultSettings.TargetServer = defaultSettings.TargetServer?.Trim().TrimEnd('/');
            return defaultSettings;
        }

        private void ApplyEnvironmentOverrides()
        {
            TargetServer = GetEnvironmentSetting("GIT_TFS_TARGET_SERVER") ?? TargetServer;
            ApiVersion = GetEnvironmentSetting("GIT_TFS_API_VERSION") ?? ApiVersion;
            Pat = GetEnvironmentSetting("GIT_TFS_PAT") ?? Pat;
            Proxy = GetEnvironmentSetting("GIT_TFS_PROXY") ?? Proxy;
            HttpCaptureDirectory = GetEnvironmentSetting("GIT_TFS_HTTP_CAPTURE") ?? HttpCaptureDirectory;

            var batchSize = GetEnvironmentSetting("GIT_TFS_BATCH_SIZE");
            if (batchSize != null)
            {
                if (!int.TryParse(batchSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBatchSize))
                    throw new GitTfsException("GIT_TFS_BATCH_SIZE must be an integer.");

                BatchSize = parsedBatchSize;
            }

            var debug = GetEnvironmentSetting("GIT_TFS_DEBUG");
            if (debug == null)
                return;

            if (!bool.TryParse(debug, out var parsedDebug))
                throw new GitTfsException("GIT_TFS_DEBUG must be 'true' or 'false'.");

            Debug = parsedDebug;
        }

        private static string GetEnvironmentSetting(string name)
        {
            foreach (var target in new[]
            {
                EnvironmentVariableTarget.Process,
                EnvironmentVariableTarget.User,
                EnvironmentVariableTarget.Machine
            })
            {
                var value = Environment.GetEnvironmentVariable(name, target);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }
    }
}
