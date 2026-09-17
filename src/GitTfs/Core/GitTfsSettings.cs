using System.Text.Json;

namespace GitTfs.Core
{
    /// <summary>
    /// Settings that apply to the local git-tfs executable. Credentials are kept
    /// out of this file and continue to use the existing TFS credential flow.
    /// </summary>
    public sealed class GitTfsSettings
    {
        public string TargetServer { get; set; }

        public string SourcePath { get; private set; }

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
