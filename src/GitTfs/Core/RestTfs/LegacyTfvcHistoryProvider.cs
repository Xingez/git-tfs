namespace GitTfs.Core.RestTfs
{
    using global::GitTfs.Core;
    using global::System.Diagnostics;
    using global::System.Text.Json;

    /// <summary>
    /// Uses the legacy TFVC client object model for its one operation that REST
    /// does not expose: recursive folder history. The helper only discovers
    /// changeset references; file content is still downloaded by RestTfsClient.
    /// </summary>
    public sealed class LegacyTfvcHistoryProvider
    {
        private readonly GitTfsSettings settingsField;
        private readonly JsonSerializerOptions jsonOptionsField = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public LegacyTfvcHistoryProvider(GitTfsSettings settings)
        {
            settingsField = settings;
        }

        public bool IsAvailable => File.Exists(GetHelperPath());

        public IReadOnlyList<RestChangesetReference> GetChangesets(string targetServer, string repositoryPath,
            int fromChangesetId)
        {
            var helperPath = GetHelperPath();
            if (!File.Exists(helperPath))
                return null;

            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                WorkingDirectory = Path.GetDirectoryName(helperPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    throw new GitTfsException("Unable to start the legacy TFVC history helper.");

                var request = new LegacyHistoryRequest
                {
                    ServerUrl = targetServer,
                    RepositoryPath = repositoryPath,
                    FromChangesetId = fromChangesetId,
                    BatchSize = 100,
                    Username = settingsField.Username,
                    Password = settingsField.Password,
                    Pat = GetPat(),
                };
                process.StandardInput.Write(JsonSerializer.Serialize(request, jsonOptionsField));
                process.StandardInput.Close();

                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();
                Task.WaitAll(standardOutputTask, standardErrorTask);
                process.WaitForExit();

                var error = standardErrorTask.Result?.Trim();
                if (process.ExitCode != 0)
                {
                    throw new GitTfsException("The legacy TFVC history helper failed with exit code "
                        + process.ExitCode + ". " + (string.IsNullOrWhiteSpace(error) ? "No error details were returned." : error));
                }

                var changesets = new List<RestChangesetReference>();
                var completed = false;
                foreach (var line in standardOutputTask.Result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var message = JsonSerializer.Deserialize<LegacyHistoryMessage>(line, jsonOptionsField);
                    if (message == null)
                        continue;

                    if (string.Equals(message.Type, "complete", StringComparison.OrdinalIgnoreCase))
                    {
                        completed = true;
                        continue;
                    }

                    if (!string.Equals(message.Type, "changeset", StringComparison.OrdinalIgnoreCase))
                        continue;

                    changesets.Add(new RestChangesetReference
                    {
                        ChangesetId = message.ChangesetId,
                        CreatedDate = message.CreatedDate,
                        Comment = message.Comment,
                        Author = message.Author == null
                            ? null
                            : new RestIdentity
                            {
                                DisplayName = message.Author.DisplayName,
                                UniqueName = message.Author.UniqueName,
                            },
                    });
                }

                if (!completed)
                    throw new GitTfsException("The legacy TFVC history helper ended without completing its response.");

                return changesets.OrderBy(changeset => changeset.ChangesetId).ToArray();
            }
        }

        private string GetHelperPath()
        {
            var configuredPath = Environment.GetEnvironmentVariable("GIT_TFS_LEGACY_HISTORY");
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return Path.GetFullPath(configuredPath);

            var helperDirectory = Path.Combine(AppContext.BaseDirectory, "legacy-history");
            var helperPath = Path.Combine(helperDirectory, "git-tfs-legacy-history.exe");
            if (File.Exists(helperPath))
                return helperPath;

            return Path.Combine(AppContext.BaseDirectory, "git-tfs-legacy-history.exe");
        }

        private string GetPat()
        {
            if (!string.IsNullOrWhiteSpace(settingsField.Pat))
                return settingsField.Pat;

            return Environment.GetEnvironmentVariable("GIT_TFS_PAT", EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable("GIT_TFS_PAT", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("GIT_TFS_PAT", EnvironmentVariableTarget.Machine);
        }

        private sealed class LegacyHistoryRequest
        {
            public string ServerUrl { get; set; }
            public string RepositoryPath { get; set; }
            public int FromChangesetId { get; set; }
            public int BatchSize { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string Pat { get; set; }
        }

        private sealed class LegacyHistoryMessage
        {
            public string Type { get; set; }
            public int ChangesetId { get; set; }
            public DateTimeOffset CreatedDate { get; set; }
            public string Comment { get; set; }
            public LegacyHistoryIdentity Author { get; set; }
        }

        private sealed class LegacyHistoryIdentity
        {
            public string DisplayName { get; set; }
            public string UniqueName { get; set; }
        }
    }
}
