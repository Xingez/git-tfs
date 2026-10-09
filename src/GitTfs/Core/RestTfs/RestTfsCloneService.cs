namespace GitTfs.Core.RestTfs
{
    using global::GitTfs.Util;
    using global::GitTfs.Commands;
    using global::LibGit2Sharp;
    using global::System.Globalization;
    using global::System.Security.Cryptography;
    using global::Microsoft.Extensions.Http;
    using global::Microsoft.Extensions.Logging;

    /// <summary>
    /// Imports one TFVC folder into Git without creating or using a TFVC workspace.
    /// </summary>
    public sealed class RestTfsCloneService : IRestTfsCloneService
    {
        private readonly GitTfsSettings settingsField;
        private readonly ILogger<RestTfsCloneService> loggerField;
        private readonly ILoggerFactory loggerFactoryField;
        private readonly IHttpClientFactory httpClientFactoryField;
        private readonly IGitHelpers gitHelpersField;
        private readonly IRestTfsChangesetImporter changesetImporterField;

        public RestTfsCloneService(GitTfsSettings settings, AuthorsFile authorsFile,
            LegacyTfvcHistoryProvider legacyHistoryProvider = null,
            ILogger<RestTfsCloneService> logger = null,
            ILoggerFactory loggerFactory = null,
            IHttpClientFactory httpClientFactory = null,
            IGitHelpers gitHelpers = null,
            IRestTfsChangesetImporter changesetImporter = null)
        {
            settingsField = settings;
            loggerField = logger;
            loggerFactoryField = loggerFactory;
            httpClientFactoryField = httpClientFactory;
            gitHelpersField = gitHelpers;
            changesetImporterField = changesetImporter
                ?? new RestTfsChangesetImporter(authorsFile, legacyHistoryProvider);
        }

        public int Run(string targetServer, string repositoryPath, string outputPath, bool noFallback = false,
            string targetCloneUrl = null, string targetBranch = "main",
            IChangesetProgressReporter progressReporter = null)
        {
            repositoryPath = repositoryPath.TrimEnd('/');
            repositoryPath.AssertValidTfsPath();
            targetServer = targetServer?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(targetServer))
                throw new GitTfsException("TargetServer is not configured in appsettings.json.");

            var absoluteOutputPath = Path.GetFullPath(outputPath);
            var gitDirectory = Path.Combine(absoluteOutputPath, ".git");
            var repositoryExists = Directory.Exists(gitDirectory);

            if (Directory.Exists(absoluteOutputPath) && !repositoryExists
                && Directory.EnumerateFileSystemEntries(absoluteOutputPath).Any())
            {
                throw new GitTfsException("error: Specified git repository directory is not empty");
            }

            try
            {
                Directory.CreateDirectory(absoluteOutputPath);
                if (!repositoryExists)
                {
                    Repository.Init(absoluteOutputPath);
                }

                var summary = new CloneSummary();
                FileVerificationSummary verification;
                int lastChangesetId;
                string commitSha;
                string sourceBranch;

                using (var repository = new Repository(absoluteOutputPath))
                using (var client = CreateRestClient(targetServer, repositoryPath))
                {
                    ConfigureRepository(repository, targetServer, repositoryPath);
                    var parent = repository.Head?.Tip;
                    lastChangesetId = FindLastChangesetId(parent);
                    if (parent != null && lastChangesetId <= 0)
                        throw new GitTfsException("The existing repository does not contain a git-tfs changeset marker and cannot be resumed safely.");

                    var pathMap = parent == null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : GetTreePathMap(parent.Tree);
                    var batchSize = settingsField.BatchSize > 0 ? settingsField.BatchSize : 100;
                    var newestCommit = parent;

                    loggerField?.LogDebug("Using REST TFVC clone for {RepositoryPath}.", repositoryPath);
                    loggerField?.LogDebug("Workspace creation is disabled; files are downloaded directly from the TFVC REST API.");
                    if (noFallback)
                    {
                        loggerField?.LogDebug("Legacy TFVC fallback helper is disabled by --no-fallback.");
                    }
                    loggerField?.LogDebug("Scanning changesets for {RepositoryPath}.", repositoryPath);

                    var scanner = new RestChangesetScanner(client, loggerField);
                    foreach (var changesetReference in scanner.Scan(repositoryPath, lastChangesetId, batchSize))
                    {
                        ImportChangeset(client, repository, changesetReference, targetServer, repositoryPath,
                            absoluteOutputPath, pathMap, ref newestCommit, ref lastChangesetId,
                            noFallback, summary, progressReporter);
                    }

                    if (newestCommit != null)
                    {
                        MaterializeTree(repository, newestCommit.Tree, absoluteOutputPath);
                        // Commits are created directly from the imported tree, so
                        // LibGit2Sharp does not update the index as part of the
                        // commit operation. Rebuild it from HEAD without touching
                        // the files we just materialized. This is equivalent to
                        // `git reset --mixed HEAD` and leaves a fresh clone clean.
                        repository.Reset(ResetMode.Mixed, newestCommit);
                        summary.TrackedFiles = EnumerateFiles(newestCommit.Tree).Count();
                    }

                    verification = newestCommit == null
                        ? null
                        : VerifyLatestFiles(client, repositoryPath, absoluteOutputPath, newestCommit.Tree, lastChangesetId);
                    commitSha = newestCommit?.Sha;
                    sourceBranch = repository.Head?.FriendlyName ?? "source";
                }

                // Release LibGit2Sharp's pack handles before external Git repacks or changes refs.
                var maintenanceMode = RunGitMaintenance(absoluteOutputPath, commitSha != null);
                if (!string.IsNullOrWhiteSpace(targetCloneUrl))
                {
                    if (commitSha == null)
                        throw new GitTfsException("A target Git repository requires an imported changeset to merge.");

                    targetBranch = string.IsNullOrWhiteSpace(targetBranch) ? "main" : targetBranch.Trim();
                    MergeIntoTargetRepository(absoluteOutputPath, targetCloneUrl, targetBranch,
                        sourceBranch, commitSha);
                    var targetMaintenanceMode = RunGitMaintenance(absoluteOutputPath, repairIndex: true);
                    loggerField?.LogInformation("Target Git sync complete: {SourceBranch} merged into "
                        + "origin/{TargetBranch} and pushed; Git maintenance: {MaintenanceMode}.",
                        sourceBranch, targetBranch, targetMaintenanceMode);
                }

                if (commitSha == null)
                {
                    loggerField?.LogInformation("Clone complete for {RepositoryPath}: no changesets imported "
                        + "({ChangesetsConsidered} considered, {ChangesetsSkipped} skipped); "
                        + "{TrackedFiles} tracked file(s); legacy fallback helper used: {LegacyFallbackUsed}; "
                        + "Git maintenance: {MaintenanceMode}.",
                        repositoryPath, summary.ChangesetsConsidered, summary.ChangesetsSkipped,
                        summary.TrackedFiles, summary.LegacyFallbackUsed, maintenanceMode);
                }
                else
                {
                    loggerField?.LogInformation("Clone complete for {RepositoryPath}: "
                        + "{ChangesetsImported}/{ChangesetsConsidered} changeset(s) imported "
                        + "({ChangesetsSkipped} skipped); {FilesProcessed} file change(s) "
                        + "({FilesDownloaded} downloaded, {FilesReused} reused, {FilesDeleted} deleted); "
                        + "{TrackedFiles} tracked file(s); latest C{ChangesetId} ({CommitSha}); "
                        + "verification: {VerifiedFiles}/{CheckedFiles} checksum(s) matched "
                        + "({HashlessFiles} without hash); legacy fallback helper used: {LegacyFallbackUsed}; "
                        + "Git maintenance: {MaintenanceMode}.",
                        repositoryPath, summary.ChangesetsImported, summary.ChangesetsConsidered,
                        summary.ChangesetsSkipped, summary.FilesProcessed, summary.FilesDownloaded,
                        summary.FilesReused, summary.FilesDeleted, summary.TrackedFiles,
                        lastChangesetId, commitSha, verification.MatchedFiles,
                        verification.CheckedFiles, verification.HashlessFiles, summary.LegacyFallbackUsed,
                        maintenanceMode);
                }
                return GitTfsExitCodes.OK;
            }
            catch
            {
                loggerField?.LogDebug("Clone failed; leaving the partial repository in place so it can be resumed.");
                throw;
            }
        }

        public int RunChangeset(string targetServer, string repositoryPath, string outputPath,
            int changesetId, bool noFallback = false,
            IChangesetProgressReporter progressReporter = null)
        {
            repositoryPath = repositoryPath?.TrimEnd('/');
            repositoryPath.AssertValidTfsPath();
            targetServer = targetServer?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(targetServer))
                throw new GitTfsException("TargetServer is not configured in appsettings.json.");
            if (changesetId <= 0)
                throw new GitTfsException("A positive TFVC changeset ID is required.");

            var absoluteOutputPath = Path.GetFullPath(outputPath);
            if (!Directory.Exists(Path.Combine(absoluteOutputPath, ".git")))
                throw new GitTfsException("The changeset command requires an existing git-tfs clone at "
                    + absoluteOutputPath + ".");

            RestTfsChangesetImportResult result;
            FileVerificationSummary verification;
            string commitSha;

            using (var repository = new Repository(absoluteOutputPath))
            using (var client = CreateRestClient(targetServer, repositoryPath))
            {
                var configuredServer = repository.Config.Get<string>("tfs-remote.default.url")?.Value;
                var configuredPath = repository.Config.Get<string>("tfs-remote.default.repository")?.Value;
                if (!string.Equals(configuredServer?.TrimEnd('/'), targetServer, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(configuredPath, repositoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new GitTfsException("The output repository is not configured for the requested TFS server "
                        + "and subfolder.");
                }

                ConfigureRepository(repository, targetServer, repositoryPath);
                var parent = repository.Head?.Tip;
                var lastChangesetId = FindLastChangesetId(parent);
                if (parent == null || lastChangesetId <= 0)
                    throw new GitTfsException("The repository must contain a git-tfs changeset commit before "
                        + "a single changeset can be imported.");
                if (changesetId <= lastChangesetId)
                    throw new GitTfsException("C" + changesetId.ToString(CultureInfo.InvariantCulture)
                        + " is not newer than the current HEAD changeset C"
                        + lastChangesetId.ToString(CultureInfo.InvariantCulture) + ".");

                var pathMap = GetTreePathMap(parent.Tree);
                var reference = new RestChangesetReference { ChangesetId = changesetId };
                result = changesetImporterField.Import(client, repository, reference, targetServer,
                    repositoryPath, absoluteOutputPath, pathMap, parent, noFallback, progressReporter);
                if (result.Skipped)
                {
                    loggerField?.LogInformation("C{ChangesetId} does not change {RepositoryPath}; no commit created; "
                        + "legacy fallback helper used: {LegacyFallbackUsed}.",
                        changesetId, repositoryPath, result.LegacyFallbackUsed);
                    return GitTfsExitCodes.OK;
                }

                MaterializeTree(repository, result.Commit.Tree, absoluteOutputPath);
                repository.Reset(ResetMode.Mixed, result.Commit);
                verification = VerifyLatestFiles(client, repositoryPath, absoluteOutputPath,
                    result.Commit.Tree, changesetId);
                commitSha = result.Commit.Sha;
            }

            var maintenanceMode = RunGitMaintenance(absoluteOutputPath, repairIndex: true);
            loggerField?.LogInformation("Single changeset import complete: C{ChangesetId} committed as {CommitSha}; "
                + "{FilesProcessed} file change(s) ({FilesDownloaded} downloaded, {FilesReused} reused, "
                + "{FilesDeleted} deleted); verification: {VerifiedFiles}/{CheckedFiles} checksum(s) matched "
                + "({HashlessFiles} without hash); legacy fallback helper used: {LegacyFallbackUsed}; "
                + "Git maintenance: {MaintenanceMode}.",
                result.ChangesetId, commitSha, result.FilesProcessed, result.FilesDownloaded,
                result.FilesReused, result.FilesDeleted, verification.MatchedFiles,
                verification.CheckedFiles, verification.HashlessFiles, result.LegacyFallbackUsed,
                maintenanceMode);
            return GitTfsExitCodes.OK;
        }

        private void MergeIntoTargetRepository(string outputPath, string targetCloneUrl, string targetBranch,
            string sourceBranch, string sourceCommitSha)
        {
            targetBranch = string.IsNullOrWhiteSpace(targetBranch) ? "main" : targetBranch.Trim();
            if (!Reference.IsValidName("refs/heads/" + targetBranch))
                throw new GitTfsException("The target Git branch name is invalid: " + targetBranch + ".");

            if (gitHelpersField == null)
                throw new GitTfsException("Git helpers are not available for target repository synchronization.");

            try
            {
                gitHelpersField.CommandNoisy("-C", outputPath, "remote", "add", "origin", targetCloneUrl);
            }
            catch (GitCommandException)
            {
                gitHelpersField.CommandNoisy("-C", outputPath, "remote", "set-url", "origin", targetCloneUrl);
            }

            try
            {
                gitHelpersField.CommandNoisy("-C", outputPath, "fetch", "origin");
                gitHelpersField.CommandOneline("-C", outputPath, "rev-parse", "--verify",
                    "refs/remotes/origin/" + targetBranch);
            }
            catch (GitCommandException exception)
            {
                throw new GitTfsException("Unable to fetch target Git branch origin/" + targetBranch + ".", exception);
            }

            try
            {
                gitHelpersField.CommandNoisy("-C", outputPath, "checkout", "-B", targetBranch,
                    "refs/remotes/origin/" + targetBranch);
                gitHelpersField.CommandNoisy("-C", outputPath, "-c", "user.name=git-tfs",
                    "-c", "user.email=git-tfs@noreply", "merge", "--allow-unrelated-histories",
                    "--no-edit", "-m", "GIT-TFS Merge (" + sourceBranch + ") with " + targetBranch,
                    sourceCommitSha);
                gitHelpersField.CommandNoisy("-C", outputPath, "push", "origin", targetBranch);
            }
            catch (GitCommandException exception)
            {
                throw new GitTfsException("Unable to merge and push the TFVC clone into origin/"
                    + targetBranch + ". Resolve the target repository state and retry.", exception);
            }
        }

        private string RunGitMaintenance(string outputPath, bool repairIndex)
        {
            if (gitHelpersField == null)
                return "not configured";

            loggerField?.LogDebug("Running forced Git index repair and maintenance.");
            if (repairIndex)
                gitHelpersField.CommandNoisy("-C", outputPath, "reset", "--mixed", "HEAD");

            try
            {
                gitHelpersField.CommandNoisy("-C", outputPath, "maintenance", "run", "--force");
                return "maintenance";
            }
            catch (GitCommandException)
            {
                // Git maintenance is unavailable before Git 2.29. Keep the
                // forced cleanup behavior for older Git installations.
                loggerField?.LogDebug("Git maintenance is unavailable; running forced git gc instead.");
                try
                {
                    gitHelpersField.CommandNoisy("-C", outputPath, "gc", "--force");
                    return "gc";
                }
                catch (GitCommandException exception)
                {
                    loggerField?.LogWarning(exception, "Forced Git maintenance failed after clone.");
                    return "failed";
                }
            }
        }

        private void ImportChangeset(IRestTfsClient client, Repository repository, RestChangesetReference changesetReference,
            string targetServer, string repositoryPath, string outputPath, IDictionary<string, string> pathMap,
            ref Commit newestCommit, ref int lastChangesetId, bool noFallback, CloneSummary summary,
            IChangesetProgressReporter progressReporter)
        {
            summary.ChangesetsConsidered++;
            var result = changesetImporterField.Import(client, repository, changesetReference,
                targetServer, repositoryPath, outputPath, pathMap, newestCommit, noFallback, progressReporter);
            if (result.Skipped)
            {
                summary.ChangesetsSkipped++;
                return;
            }

            newestCommit = result.Commit;
            lastChangesetId = result.ChangesetId;
            summary.ChangesetsImported++;
            summary.FilesProcessed += result.FilesProcessed;
            summary.FilesDownloaded += result.FilesDownloaded;
            summary.FilesReused += result.FilesReused;
            summary.FilesDeleted += result.FilesDeleted;
            summary.LegacyFallbackUsed |= result.LegacyFallbackUsed;

            if (progressReporter == null)
            {
                var progress = summary.ChangesetsImported.ToString(CultureInfo.InvariantCulture) + "/?";
                loggerField?.LogInformation("[{Progress}] C{ChangesetId} committed as {CommitSha}.",
                    progress, result.ChangesetId, result.Commit.Sha);
            }
        }

        private IRestTfsClient CreateRestClient(string targetServer, string repositoryPath)
        {
            var logger = loggerFactoryField?.CreateLogger<RestTfsClient>();
            if (httpClientFactoryField == null)
                return new RestTfsClient(targetServer, repositoryPath, settingsField, logger);

            var httpClient = httpClientFactoryField.CreateClient(RestTfsClient.HttpClientName);
            return new RestTfsClient(httpClient, targetServer, repositoryPath, settingsField.ApiVersion, logger);
        }

        private FileVerificationSummary VerifyLatestFiles(IRestTfsClient client, string repositoryPath,
            string outputPath, Tree tree, int changesetId)
        {
            var summary = new FileVerificationSummary();
            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var timestampUpdates = new List<KeyValuePair<string, DateTime>>();
            var items = client.GetItems(repositoryPath, changesetId);
            foreach (var item in items)
            {
                if (item == null || item.IsFolder || item.DeletionId > 0 || !IsWithinRepository(item.Path, repositoryPath))
                    continue;

                var relativePath = ToRelativeGitPath(item.Path, repositoryPath);
                if (string.IsNullOrWhiteSpace(relativePath))
                    continue;

                expectedPaths.Add(relativePath);
                var filePath = GetWorkingFilePath(outputPath, relativePath);
                if (!File.Exists(filePath))
                {
                    summary.MissingFiles++;
                    loggerField?.LogError("TFVC verification failed: missing relative file {RelativePath} at C{ChangesetId}.",
                        relativePath, changesetId);
                    continue;
                }

                if (item.ChangeDate != default)
                    timestampUpdates.Add(new KeyValuePair<string, DateTime>(filePath, item.ChangeDate.UtcDateTime));

                if (string.IsNullOrWhiteSpace(item.HashValue))
                {
                    summary.HashlessFiles++;
                    continue;
                }

                summary.CheckedFiles++;
                var localHash = Convert.ToBase64String(MD5.HashData(File.ReadAllBytes(filePath)));
                if (string.Equals(localHash, item.HashValue.Trim(), StringComparison.Ordinal))
                {
                    summary.MatchedFiles++;
                }
                else
                {
                    summary.MismatchedFiles++;
                    loggerField?.LogError("TFVC verification failed: checksum mismatch for relative file {RelativePath} at C{ChangesetId}.",
                        relativePath, changesetId);
                }
            }

            foreach (var treeFile in EnumerateFiles(tree))
            {
                if (!expectedPaths.Contains(treeFile.Path))
                {
                    summary.MetadataMissingFiles++;
                    loggerField?.LogError("TFVC verification failed: latest metadata did not include relative file {RelativePath}.",
                        treeFile.Path);
                }
            }

            loggerField?.LogInformation("TFVC verification at C{ChangesetId}: {MatchedFiles}/{CheckedFiles} checksum(s) matched; "
                + "{HashlessFiles} file(s) without a hash; {MismatchedFiles} mismatch(es), {MissingFiles} missing file(s), "
                + "{MetadataMissingFiles} file(s) missing from metadata.",
                changesetId, summary.MatchedFiles, summary.CheckedFiles, summary.HashlessFiles,
                summary.MismatchedFiles, summary.MissingFiles, summary.MetadataMissingFiles);

            if (summary.MismatchedFiles > 0 || summary.MissingFiles > 0 || summary.MetadataMissingFiles > 0)
            {
                throw new GitTfsException("TFVC file verification failed at C"
                    + changesetId.ToString(CultureInfo.InvariantCulture) + ": "
                    + summary.MismatchedFiles.ToString(CultureInfo.InvariantCulture) + " checksum mismatch(es), "
                    + summary.MissingFiles.ToString(CultureInfo.InvariantCulture) + " missing file(s), and "
                    + summary.MetadataMissingFiles.ToString(CultureInfo.InvariantCulture)
                    + " file(s) missing from latest TFVC metadata.");
            }

            foreach (var timestampUpdate in timestampUpdates)
                File.SetLastWriteTimeUtc(timestampUpdate.Key, timestampUpdate.Value);

            return summary;
        }

        private static void ConfigureRepository(Repository repository, string targetServer, string repositoryPath)
        {
            repository.Config.Set("tfs-remote.default.url", targetServer, ConfigurationLevel.Local);
            repository.Config.Set("tfs-remote.default.repository", repositoryPath, ConfigurationLevel.Local);
            repository.Config.Set(GitTfsConstants.IgnoreBranches, "true", ConfigurationLevel.Local);
            repository.Config.Set(GitTfsConstants.DisableGitignoreSupport, "true", ConfigurationLevel.Local);
        }

        private static int FindLastChangesetId(Commit commit)
        {
            if (commit == null)
                return 0;
            var match = GitTfsConstants.TfsCommitInfoRegex.Match(commit.Message ?? string.Empty);
            return match.Success && int.TryParse(match.Groups["changeset"].Value, out var id) ? id : 0;
        }

        private static Dictionary<string, string> GetTreePathMap(Tree tree)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddTreePaths(tree, string.Empty, paths);
            return paths;
        }

        private static void AddTreePaths(Tree tree, string prefix, IDictionary<string, string> paths)
        {
            foreach (var entry in tree)
            {
                var path = string.IsNullOrEmpty(prefix) ? entry.Name : prefix + "/" + entry.Name;
                if (entry.TargetType == TreeEntryTargetType.Tree)
                    AddTreePaths((Tree)entry.Target, path, paths);
                else if (entry.TargetType == TreeEntryTargetType.Blob)
                    paths[path] = path;
            }
        }

        private static void MaterializeTree(Repository repository, Tree tree, string outputPath)
        {
            var expectedFiles = new HashSet<string>(
                EnumerateFiles(tree).Select(file => file.Path),
                StringComparer.OrdinalIgnoreCase);
            ReconcileWorkingTree(outputPath, expectedFiles);

            foreach (var entry in EnumerateFiles(tree))
            {
                var filePath = Path.Combine(outputPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                using (var input = ((Blob)entry.Entry.Target).GetContentStream())
                using (var output = File.Create(filePath))
                    input.CopyTo(output);
            }
        }

        private static void ReconcileWorkingTree(string outputPath, ISet<string> expectedFiles)
        {
            if (!Directory.Exists(outputPath))
                return;

            foreach (var filePath in Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = GetRelativeWorkingPath(outputPath, filePath);
                if (IsGitPath(relativePath) || expectedFiles.Contains(relativePath))
                    continue;

                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
            }

            var expectedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var expectedFile in expectedFiles)
            {
                var separator = expectedFile.LastIndexOf('/');
                while (separator > 0)
                {
                    var directory = expectedFile.Substring(0, separator);
                    expectedDirectories.Add(directory);
                    separator = directory.LastIndexOf('/');
                }
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(outputPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length))
            {
                var relativePath = GetRelativeWorkingPath(outputPath, directoryPath);
                if (IsGitPath(relativePath) || expectedDirectories.Contains(relativePath))
                    continue;

                File.SetAttributes(directoryPath, FileAttributes.Normal);
                Directory.Delete(directoryPath, recursive: true);
            }
        }

        private static string GetRelativeWorkingPath(string outputPath, string fullPath)
            => Path.GetRelativePath(outputPath, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

        private static bool IsGitPath(string relativePath)
            => string.Equals(relativePath, ".git", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<TreeFile> EnumerateFiles(Tree tree, string prefix = "")
        {
            foreach (var entry in tree)
            {
                var path = string.IsNullOrEmpty(prefix) ? entry.Name : prefix + "/" + entry.Name;
                if (entry.TargetType == TreeEntryTargetType.Tree)
                {
                    foreach (var child in EnumerateFiles((Tree)entry.Target, path))
                        yield return child;
                }
                else if (entry.TargetType == TreeEntryTargetType.Blob)
                {
                    yield return new TreeFile(path, entry);
                }
            }
        }

        private static bool IsWithinRepository(string serverPath, string repositoryPath)
            => !string.IsNullOrWhiteSpace(serverPath)
                && (string.Equals(serverPath, repositoryPath, StringComparison.OrdinalIgnoreCase)
                    || serverPath.StartsWith(repositoryPath + "/", StringComparison.OrdinalIgnoreCase));

        private static string ToRelativeGitPath(string serverPath, string repositoryPath)
            => serverPath.Substring(repositoryPath.Length).Trim('/').Replace('\\', '/');

        private static string GetWorkingFilePath(string outputPath, string relativePath)
            => Path.Combine(outputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private sealed class CloneSummary
        {
            public int ChangesetsConsidered { get; set; }
            public int ChangesetsImported { get; set; }
            public int ChangesetsSkipped { get; set; }
            public int FilesProcessed { get; set; }
            public int FilesDownloaded { get; set; }
            public int FilesReused { get; set; }
            public int FilesDeleted { get; set; }
            public int TrackedFiles { get; set; }
            public bool LegacyFallbackUsed { get; set; }
        }

        private sealed class FileVerificationSummary
        {
            public int CheckedFiles { get; set; }
            public int MatchedFiles { get; set; }
            public int HashlessFiles { get; set; }
            public int MismatchedFiles { get; set; }
            public int MissingFiles { get; set; }
            public int MetadataMissingFiles { get; set; }
        }

        private readonly struct TreeFile
        {
            public TreeFile(string path, TreeEntry entry)
            {
                Path = path;
                Entry = entry;
            }

            public string Path { get; }
            public TreeEntry Entry { get; }
        }
    }
}
