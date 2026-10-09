namespace GitTfs.Core.RestTfs
{
    using GitTfs.Core;
    using GitTfs.Util;
    using System.Diagnostics;
    using System.Text.Json;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Uses the legacy TFVC client object model for exact historical file
    /// downloads when REST content and rename-version endpoints cannot resolve
    /// an item. The helper process starts on the first download request.
    /// </summary>
    [SingletonService]
    public sealed class LegacyTfvcHistoryProvider : IDisposable
    {
        private readonly GitTfsSettings settingsField;
        private readonly object helperSyncField = new object();
        private readonly JsonSerializerOptions jsonOptionsField = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        private Process helperProcessField;
        private StreamWriter helperInputField;
        private StreamReader helperOutputField;
        private Task<string> helperErrorTaskField;
        private string helperServerField;

        public LegacyTfvcHistoryProvider(IOptions<GitTfsSettings> settings)
        {
            settingsField = settings.Value;
        }

        public bool IsAvailable => File.Exists(GetHelperPath());

        public byte[] DownloadFile(string targetServer, string itemPath, int changesetId, int deletionId = 0)
        {
            if (!IsAvailable)
                throw new GitTfsException("The legacy TFVC helper is not available.");

            var messages = RunHelper(new LegacyHistoryRequest
            {
                Operation = "download",
                ServerUrl = targetServer,
                ItemPath = itemPath,
                ChangesetId = changesetId,
                DeletionId = deletionId,
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

        public void Dispose()
        {
            lock (helperSyncField)
                StopHelper();
        }

        private IReadOnlyList<LegacyHistoryMessage> RunHelper(LegacyHistoryRequest request)
        {
            var helperPath = GetHelperPath();
            if (!File.Exists(helperPath))
                throw new GitTfsException("The legacy TFVC helper is not available.");

            request.Pat = GetPat();

            lock (helperSyncField)
            {
                EnsureHelper(helperPath, request.ServerUrl);
                try
                {
                    helperInputField.WriteLine(JsonSerializer.Serialize(request, jsonOptionsField));
                    helperInputField.Flush();

                    var messages = new List<LegacyHistoryMessage>();
                    string error = null;
                    var completed = false;
                    string line;
                    while ((line = helperOutputField.ReadLine()) != null)
                    {
                        var message = JsonSerializer.Deserialize<LegacyHistoryMessage>(line, jsonOptionsField);
                        if (message == null)
                            continue;

                        if (string.Equals(message.Type, "complete", StringComparison.OrdinalIgnoreCase))
                        {
                            completed = true;
                            break;
                        }

                        if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
                        {
                            error = message.Message;
                            continue;
                        }

                        messages.Add(message);
                    }

                    if (!completed)
                    {
                        var processError = GetHelperError();
                        throw new GitTfsException("The legacy TFVC download helper ended without completing its response."
                            + (string.IsNullOrWhiteSpace(processError) ? string.Empty : " " + processError));
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                        throw new GitTfsException("The legacy TFVC download helper failed: " + error);

                    return messages;
                }
                catch
                {
                    StopHelper();
                    throw;
                }
            }
        }

        private void EnsureHelper(string helperPath, string serverUrl)
        {
            if (helperProcessField != null && !helperProcessField.HasExited
                && string.Equals(helperServerField, serverUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StopHelper();
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
            var process = Process.Start(startInfo);
            if (process == null)
                throw new GitTfsException("Unable to start the legacy TFVC download helper.");

            helperProcessField = process;
            helperInputField = process.StandardInput;
            helperOutputField = process.StandardOutput;
            helperErrorTaskField = process.StandardError.ReadToEndAsync();
            helperServerField = serverUrl;
        }

        private string GetHelperError()
        {
            if (helperErrorTaskField == null)
                return null;

            try
            {
                return helperErrorTaskField.GetAwaiter().GetResult()?.Trim();
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        private void StopHelper()
        {
            var process = helperProcessField;
            var input = helperInputField;
            var output = helperOutputField;
            helperProcessField = null;
            helperInputField = null;
            helperOutputField = null;
            helperErrorTaskField = null;
            helperServerField = null;

            if (process == null)
                return;

            try
            {
                input?.Close();
                if (!process.HasExited && !process.WaitForExit(2000))
                    process.Kill();
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
            }
            finally
            {
                output?.Dispose();
                process.Dispose();
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
            => settingsField.Pat;

        private sealed class LegacyHistoryRequest
        {
            public string Operation { get; set; }
            public string ServerUrl { get; set; }
            public string ItemPath { get; set; }
            public int ChangesetId { get; set; }
            public int DeletionId { get; set; }
            public string Pat { get; set; }
        }

        private sealed class LegacyHistoryMessage
        {
            public string Type { get; set; }
            public string Content { get; set; }
            public string Message { get; set; }
        }
    }
}
