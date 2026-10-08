namespace GitTfs.Core.RestTfs
{
    using global::GitTfs.Core;
    using global::GitTfs.Util;
    using global::System.Diagnostics;
    using global::System.Text.Json;

    /// <summary>
    /// Uses the legacy TFVC client object model for recursive folder history and
    /// exact historical file downloads when the REST content endpoint cannot
    /// resolve an item.
    /// </summary>
    [SingletonService]
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
            if (!IsAvailable)
                return null;

            var messages = RunHelper(new LegacyHistoryRequest
            {
                Operation = "history",
                ServerUrl = targetServer,
                RepositoryPath = repositoryPath,
                FromChangesetId = fromChangesetId,
                BatchSize = 100,
            });

            return messages
                .Where(message => string.Equals(message.Type, "changeset", StringComparison.OrdinalIgnoreCase))
                .Select(message => new RestChangesetReference
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
                })
                .OrderBy(changeset => changeset.ChangesetId)
                .ToArray();
        }

        public byte[] DownloadFile(string targetServer, string itemPath, int changesetId)
        {
            if (!IsAvailable)
                throw new GitTfsException("The legacy TFVC helper is not available.");

            var messages = RunHelper(new LegacyHistoryRequest
            {
                Operation = "download",
                ServerUrl = targetServer,
                ItemPath = itemPath,
                ChangesetId = changesetId,
            });
            var file = messages.FirstOrDefault(message =>
                string.Equals(message.Type, "file", StringComparison.OrdinalIgnoreCase));
            if (file == null || file.Content == null)
                throw new GitTfsException("The legacy TFVC helper did not return file content for " + itemPath + ".");

            try
            {
                return Convert.FromBase64String(file.Content);
            }
            catch (FormatException exception)
            {
                throw new GitTfsException("The legacy TFVC helper returned invalid file content for " + itemPath + ".", exception);
            }
        }

        private IReadOnlyList<LegacyHistoryMessage> RunHelper(LegacyHistoryRequest request)
        {
            var helperPath = GetHelperPath();
            if (!File.Exists(helperPath))
                throw new GitTfsException("The legacy TFVC helper is not available.");

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

            request.Username = settingsField.Username;
            request.Password = settingsField.Password;
            request.Pat = GetPat();

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    throw new GitTfsException("Unable to start the legacy TFVC history helper.");

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

                var messages = standardOutputTask.Result
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => JsonSerializer.Deserialize<LegacyHistoryMessage>(line, jsonOptionsField))
                    .Where(message => message != null)
                    .ToArray();
                if (!messages.Any(message => string.Equals(message.Type, "complete", StringComparison.OrdinalIgnoreCase)))
                    throw new GitTfsException("The legacy TFVC history helper ended without completing its response.");

                return messages;
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
            public string Operation { get; set; }
            public string ServerUrl { get; set; }
            public string RepositoryPath { get; set; }
            public int FromChangesetId { get; set; }
            public int BatchSize { get; set; }
            public string ItemPath { get; set; }
            public int ChangesetId { get; set; }
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
            public string Content { get; set; }
        }

        private sealed class LegacyHistoryIdentity
        {
            public string DisplayName { get; set; }
            public string UniqueName { get; set; }
        }
    }
}
