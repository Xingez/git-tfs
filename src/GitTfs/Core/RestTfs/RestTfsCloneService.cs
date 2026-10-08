namespace GitTfs.Core.RestTfs
{
    using global::GitTfs.Util;
    using global::GitTfs.Commands;
    using global::LibGit2Sharp;
    using global::System.Diagnostics;
    using global::System.Globalization;
    using global::System.Security.Cryptography;
    using global::System.Text;
    using global::Microsoft.Extensions.Http;
    using global::Microsoft.Extensions.Logging;

    /// <summary>
    /// Imports one TFVC folder into Git without creating or using a TFVC workspace.
    /// </summary>
    public sealed class RestTfsCloneService
    {
        private readonly GitTfsSettings settingsField;
        private readonly AuthorsFile authorsFileField;
        private readonly LegacyTfvcHistoryProvider legacyHistoryProviderField;
        private readonly ILogger<RestTfsCloneService> loggerField;
        private readonly ILoggerFactory loggerFactoryField;
        private readonly IHttpClientFactory httpClientFactoryField;
        private readonly IGitHelpers gitHelpersField;

        public RestTfsCloneService(GitTfsSettings settings, AuthorsFile authorsFile,
            LegacyTfvcHistoryProvider legacyHistoryProvider = null,
            ILogger<RestTfsCloneService> logger = null,
            ILoggerFactory loggerFactory = null,
            IHttpClientFactory httpClientFactory = null,
            IGitHelpers gitHelpers = null)
        {
            settingsField = settings;
            authorsFileField = authorsFile;
            legacyHistoryProviderField = legacyHistoryProvider;
            loggerField = logger;
            loggerFactoryField = loggerFactory;
            httpClientFactoryField = httpClientFactory;
            gitHelpersField = gitHelpers;
        }

        public int Run(string targetServer, string repositoryPath, string outputPath, bool noFallback = false)
        {
            repositoryPath = repositoryPath.TrimEnd('/');
            repositoryPath.AssertValidTfsPath();
            targetServer = targetServer?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(targetServer))
                throw new GitTfsException("TargetServer is not configured in appsettings.json.");

            var absoluteOutputPath = Path.GetFullPath(outputPath);
            var gitDirectory = Path.Combine(absoluteOutputPath, ".git");
            var repositoryExists = Directory.Exists(gitDirectory);
            var repositoryCreated = false;

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
                    repositoryCreated = true;
                }

                using (var repository = new Repository(absoluteOutputPath))
                using (var client = CreateRestClient(targetServer, repositoryPath))
                {
                    ConfigureRepository(repository, targetServer, repositoryPath);
                    var parent = repository.Head?.Tip;
                    var lastChangesetId = FindLastChangesetId(parent);
                    if (parent != null && lastChangesetId <= 0)
                        throw new GitTfsException("The existing repository does not contain a git-tfs changeset marker and cannot be resumed safely.");

                    var pathMap = parent == null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : GetTreePathMap(parent.Tree);
                    var batchSize = settingsField.BatchSize > 0 ? settingsField.BatchSize : 100;
                    var fetchedChangesets = 0;
                    var newestCommit = parent;
                    var fromChangesetId = lastChangesetId;
                    var lastScannedChangesetId = lastChangesetId;

                    loggerField?.LogInformation("Using REST TFVC clone for {RepositoryPath}.", repositoryPath);
                    loggerField?.LogDebug("Workspace creation is disabled; files are downloaded directly from the TFVC REST API.");
                    var legacyChangesetReferences = !noFallback && legacyHistoryProviderField?.IsAvailable == true
                        ? legacyHistoryProviderField.GetChangesets(targetServer, repositoryPath, lastChangesetId)
                        : null;
                    if (legacyChangesetReferences != null)
                    {
                        loggerField?.LogInformation("Using legacy TFVC recursive history for {RepositoryPath}.", repositoryPath);
                        loggerField?.LogInformation("Legacy history returned {ChangesetCount} relevant changeset reference(s).",
                            legacyChangesetReferences.Count);
                        foreach (var changesetReference in legacyChangesetReferences)
                        {
                            if (changesetReference.ChangesetId <= lastChangesetId)
                                continue;

                            ImportChangeset(client, repository, changesetReference, targetServer, repositoryPath,
                                absoluteOutputPath, pathMap, ref newestCommit, ref lastChangesetId, ref fetchedChangesets,
                                legacyChangesetReferences.Count, noFallback);
                        }
                    }
                    else
                    {
                        if (noFallback)
                            loggerField?.LogInformation("Legacy TFVC helper is disabled by --no-fallback; scanning project changesets with REST.");
                        else
                            loggerField?.LogWarning("Legacy TFVC history helper is unavailable; scanning project changesets as a REST fallback.");
                        loggerField?.LogInformation("Scanning project changesets and filtering changes under {RepositoryPath}.", repositoryPath);

                        while (true)
                        {
                            var pageStartChangesetId = fromChangesetId;
                            var changesetReferences = client.GetChangesets(repositoryPath, fromChangesetId, batchSize,
                                filterByItemPath: false);
                            if (changesetReferences.Count == 0)
                                break;

                            loggerField?.LogInformation("Changeset scan after C{StartingChangesetId} returned {ChangesetCount} reference(s), through C{EndingChangesetId}.",
                                pageStartChangesetId, changesetReferences.Count,
                                changesetReferences.Max(reference => reference.ChangesetId));

                            foreach (var changesetReference in changesetReferences.OrderBy(reference => reference.ChangesetId))
                            {
                                if (changesetReference.ChangesetId <= lastScannedChangesetId)
                                    continue;

                                lastScannedChangesetId = changesetReference.ChangesetId;
                                ImportChangeset(client, repository, changesetReference, targetServer, repositoryPath,
                                    absoluteOutputPath, pathMap, ref newestCommit, ref lastChangesetId, ref fetchedChangesets,
                                    null, noFallback);
                            }

                            var lastReferenceId = changesetReferences.Max(reference => reference.ChangesetId);
                            if (lastReferenceId <= pageStartChangesetId)
                            {
                                if (pageStartChangesetId == int.MaxValue)
                                    break;

                                // Some TFVC-compatible servers treat fromId as inclusive even though
                                // the Azure DevOps REST contract describes it as exclusive. Move past
                                // the repeated result so a folder history cannot stop at its first page.
                                fromChangesetId = pageStartChangesetId + 1;
                                loggerField?.LogWarning("Changeset scan page did not advance; retrying after C{StartingChangesetId}.",
                                    pageStartChangesetId);
                                continue;
                            }
                            fromChangesetId = lastReferenceId;
                        }
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
                        loggerField?.LogInformation("Clone complete: {ChangesetCount} changeset(s), latest C{ChangesetId}.",
                            fetchedChangesets, lastChangesetId);
                    }
                    else
                    {
                        loggerField?.LogInformation("Clone complete: no changesets found under {RepositoryPath}.", repositoryPath);
                    }

                    RunGitMaintenance(absoluteOutputPath, newestCommit != null);
                }

                return GitTfsExitCodes.OK;
            }
            catch
            {
                if (repositoryCreated && !settingsField.Resumable)
                {
                    try
                    {
                        Directory.Delete(absoluteOutputPath, true);
                    }
                    catch (Exception cleanupException)
                    {
                        loggerField?.LogWarning(cleanupException, "Unable to clean failed clone directory.");
                    }
                }
                throw;
            }
        }

        private void RunGitMaintenance(string outputPath, bool repairIndex)
        {
            if (gitHelpersField == null)
                return;

            loggerField?.LogDebug("Running forced Git index repair and maintenance.");
            if (repairIndex)
                gitHelpersField.CommandNoisy("-C", outputPath, "reset", "--mixed", "HEAD");

            try
            {
                gitHelpersField.CommandNoisy("-C", outputPath, "maintenance", "run", "--force");
            }
            catch (GitCommandException)
            {
                // Git maintenance is unavailable before Git 2.29. Keep the
                // forced cleanup behavior for older Git installations.
                loggerField?.LogDebug("Git maintenance is unavailable; running forced git gc instead.");
                try
                {
                    gitHelpersField.CommandNoisy("-C", outputPath, "gc", "--force");
                }
                catch (GitCommandException exception)
                {
                    loggerField?.LogWarning(exception, "Forced Git maintenance failed after clone.");
                }
            }
        }

        private void ImportChangeset(RestTfsClient client, Repository repository, RestChangesetReference changesetReference,
            string targetServer, string repositoryPath, string outputPath, IDictionary<string, string> pathMap,
            ref Commit newestCommit, ref int lastChangesetId, ref int fetchedChangesets, int? totalChangesets,
            bool noFallback)
        {
            var changeset = client.GetChangeset(changesetReference.ChangesetId);
            changeset.Changes ??= new List<RestChange>();
            if (!HasChangesWithinRepository(changeset, repositoryPath))
            {
                var sourceRenameCount = changeset.Changes.Count(IsSourceRename);
                loggerField?.LogInformation("C{ChangesetId}: skipped; {SkipReason} under {RepositoryPath}.",
                    changeset.ChangesetId,
                    sourceRenameCount > 0
                        ? "source rename records contain no downloadable content"
                        : "no changes",
                    repositoryPath);
                return;
            }

            var treeDefinition = newestCommit == null
                ? new TreeDefinition()
                : TreeDefinition.From(newestCommit.Tree);
            ApplyChanges(client, repository, treeDefinition, pathMap, changeset, targetServer, repositoryPath, outputPath, noFallback);

            var tree = repository.ObjectDatabase.CreateTree(treeDefinition);
            var message = BuildCommitMessage(changeset, changesetReference, targetServer, repositoryPath);
            var identity = ResolveIdentity(changeset.Author ?? changeset.CheckedInBy ?? changesetReference.Author);
            var signature = new Signature(identity.Name, identity.Email, GetCommitDate(changeset, changesetReference));
            var parents = newestCommit == null ? Enumerable.Empty<Commit>() : new[] { newestCommit };
            var commit = repository.ObjectDatabase.CreateCommit(signature, signature, message, tree, parents, false);

            UpdateRefs(repository, commit, changeset.ChangesetId);
            newestCommit = commit;
            lastChangesetId = changeset.ChangesetId;
            fetchedChangesets++;

            var progress = totalChangesets.HasValue
                ? fetchedChangesets.ToString(CultureInfo.InvariantCulture) + "/"
                    + totalChangesets.Value.ToString(CultureInfo.InvariantCulture)
                : fetchedChangesets.ToString(CultureInfo.InvariantCulture) + "/?";
            loggerField?.LogInformation("[{Progress}] C{ChangesetId} committed as {CommitSha}.",
                progress, changeset.ChangesetId, commit.Sha);
        }

        private RestTfsClient CreateRestClient(string targetServer, string repositoryPath)
        {
            var logger = loggerFactoryField?.CreateLogger<RestTfsClient>();
            if (httpClientFactoryField == null)
                return new RestTfsClient(targetServer, repositoryPath, settingsField, logger);

            var httpClient = httpClientFactoryField.CreateClient(RestTfsClient.HttpClientName);
            return new RestTfsClient(httpClient, targetServer, repositoryPath, settingsField.ApiVersion, logger);
        }

        private void ApplyChanges(RestTfsClient client, Repository repository, TreeDefinition treeDefinition,
            IDictionary<string, string> pathMap, RestChangeset changeset, string targetServer,
            string repositoryPath, string outputPath, bool noFallback)
        {
            var changes = changeset.Changes
                .Where(change => change?.Item != null)
                .Where(change => !IsSourceRename(change))
                .Where(change => IsWithinRepository(change.Item.Path, repositoryPath)
                    || IsWithinRepository(change.SourceServerItem, repositoryPath)
                    || (change.MergeSources ?? new List<RestMergeSource>())
                        .Any(source => IsWithinRepository(source.ServerItem, repositoryPath)))
                .ToArray();
            var filesToProcess = changes.Count(change => !IsDelete(change) && !change.Item.IsFolder);
            var downloaded = 0;
            var reused = 0;
            var processed = 0;
            loggerField?.LogInformation("C{ChangesetId}: processing {FileCount} file(s) (0%).",
                changeset.ChangesetId, filesToProcess);

            foreach (var change in changes)
            {
                RemoveRenameSources(treeDefinition, pathMap, change, repositoryPath, outputPath);
                if (!IsWithinRepository(change.Item.Path, repositoryPath))
                    continue;

                var relativePath = ToRelativeGitPath(change.Item.Path, repositoryPath);
                if (string.IsNullOrEmpty(relativePath) || change.Item.IsFolder)
                    continue;

                if (IsDelete(change))
                {
                    RemovePath(treeDefinition, pathMap, relativePath, outputPath);
                    continue;
                }

                var existingPath = pathMap.TryGetValue(relativePath, out var currentPath) ? currentPath : relativePath;
                if (!string.Equals(existingPath, relativePath, StringComparison.Ordinal))
                    treeDefinition.Remove(existingPath);

                var reusedLocalFile = TryReadMatchingLocalFile(outputPath, relativePath, change.Item.HashValue,
                    out var content);
                if (!reusedLocalFile)
                    content = DownloadFileWithFallback(client, targetServer, change,
                        changeset.ChangesetId, change.Item.DeletionId, relativePath, noFallback);
                var blob = repository.ObjectDatabase.CreateBlob(new MemoryStream(content, writable: false));
                treeDefinition.Add(relativePath, blob, Mode.NonExecutableFile);
                pathMap.Remove(relativePath);
                pathMap[relativePath] = relativePath;
                if (!reusedLocalFile)
                {
                    WriteWorkingFile(outputPath, relativePath, content);
                    downloaded++;
                }
                else
                {
                    reused++;
                    loggerField?.LogDebug("C{ChangesetId}: reusing local file {RelativePath}; its TFVC hash matches.",
                        changeset.ChangesetId, relativePath);
                }

                processed++;
                var percent = filesToProcess == 0 ? 100 : processed * 100 / filesToProcess;
                loggerField?.LogInformation("C{ChangesetId}: processed {ProcessedFiles}/{TotalFiles} file(s) ({Percent}%; downloaded {Downloaded}, reused {Reused}).",
                    changeset.ChangesetId, processed, filesToProcess, percent, downloaded, reused);
            }

            if (filesToProcess == 0)
                loggerField?.LogInformation("C{ChangesetId}: no file content to download.", changeset.ChangesetId);
        }

        private byte[] DownloadFileWithFallback(RestTfsClient client, string targetServer, RestChange change,
            int changesetId, int deletionId, string relativePath, bool noFallback)
        {
            var itemPath = change.Item.Path;
            try
            {
                return client.DownloadFile(itemPath, changesetId);
            }
            catch (RestTfsException exception) when (exception.StatusCode == 404)
            {
                try
                {
                    return client.DownloadFile(itemPath, changesetId, "Changeset", "Previous");
                }
                catch (RestTfsException previousException) when (previousException.StatusCode == 404)
                {
                    if (HasMergeSource(change))
                    {
                        try
                        {
                            return client.DownloadFile(itemPath, changesetId, "MergeSource", "UseRename");
                        }
                        catch (RestTfsException renameException) when (renameException.StatusCode == 404)
                        {
                        }
                    }

                    if (noFallback)
                    {
                        throw new GitTfsException("The REST version, previous version, and rename version downloads failed for "
                            + relativePath + " at C" + changesetId + "; legacy TFVC fallback is disabled.", previousException);
                    }

                    if (legacyHistoryProviderField?.IsAvailable == true)
                    {
                        try
                        {
                            return legacyHistoryProviderField.DownloadFile(
                                targetServer, itemPath, changesetId, deletionId);
                        }
                        catch (Exception fallbackException)
                        {
                            throw new GitTfsException("The REST version, previous version, rename version, and legacy TFVC downloads failed for "
                                + relativePath + " at C" + changesetId + ".", fallbackException);
                        }
                    }

                    throw new GitTfsException("The REST version, previous version, and rename version downloads failed for "
                        + relativePath + " at C" + changesetId + ".", previousException);
                }
            }
        }

        private static bool HasMergeSource(RestChange change)
            => (change.MergeSources ?? new List<RestMergeSource>()).Any(source => source != null)
                || (change.ChangeType ?? string.Empty)
                    .Split(',')
                    .Select(type => type.Trim())
                    .Any(type => string.Equals(type, "merge", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "rename", StringComparison.OrdinalIgnoreCase));

        private static bool TryReadMatchingLocalFile(string outputPath, string relativePath, string hashValue,
            out byte[] content)
        {
            content = null;
            if (string.IsNullOrWhiteSpace(hashValue))
                return false;

            var filePath = GetWorkingFilePath(outputPath, relativePath);
            if (!File.Exists(filePath))
                return false;

            content = File.ReadAllBytes(filePath);
            var localHash = Convert.ToBase64String(MD5.HashData(content));
            if (string.Equals(localHash, hashValue.Trim(), StringComparison.Ordinal))
                return true;

            content = null;
            return false;
        }

        private static bool HasChangesWithinRepository(RestChangeset changeset, string repositoryPath)
            => (changeset.Changes ?? new List<RestChange>())
                .Any(change => change?.Item != null
                    && !IsSourceRename(change)
                    && (IsWithinRepository(change.Item.Path, repositoryPath)
                        || IsWithinRepository(change.SourceServerItem, repositoryPath)
                        || (change.MergeSources ?? new List<RestMergeSource>())
                            .Any(source => IsWithinRepository(source.ServerItem, repositoryPath))));

        // A sourceRename describes the old side of a rename. Its item URL can
        // legitimately be gone at the changeset version; the target record is
        // the one that carries the content to import.
        private static bool IsSourceRename(RestChange change)
            => (change?.ChangeType ?? string.Empty)
                .Split(',')
                .Select(type => type.Trim())
                .Any(type => string.Equals(type, "sourceRename", StringComparison.OrdinalIgnoreCase));

        private static void RemoveRenameSources(TreeDefinition treeDefinition, IDictionary<string, string> pathMap,
            RestChange change, string repositoryPath, string outputPath)
        {
            var sources = new List<string>();
            if (!string.IsNullOrWhiteSpace(change.SourceServerItem))
                sources.Add(change.SourceServerItem);
            sources.AddRange((change.MergeSources ?? new List<RestMergeSource>())
                .Where(source => source.IsRename && !string.IsNullOrWhiteSpace(source.ServerItem))
                .Select(source => source.ServerItem));

            foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IsWithinRepository(source, repositoryPath))
                    RemovePath(treeDefinition, pathMap, ToRelativeGitPath(source, repositoryPath), outputPath);
            }
        }

        private static void RemovePath(TreeDefinition treeDefinition, IDictionary<string, string> pathMap,
            string relativePath, string outputPath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return;

            if (pathMap.TryGetValue(relativePath, out var existingPath))
            {
                treeDefinition.Remove(existingPath);
                pathMap.Remove(relativePath);
                DeleteWorkingFile(outputPath, existingPath);
            }
            else
            {
                treeDefinition.Remove(relativePath);
                DeleteWorkingFile(outputPath, relativePath);
            }
        }

        private string BuildCommitMessage(RestChangeset changeset, RestChangesetReference reference, string targetServer, string repositoryPath)
        {
            var comment = string.IsNullOrWhiteSpace(changeset.Comment) ? reference.Comment : changeset.Comment;
            if (string.IsNullOrWhiteSpace(comment))
                comment = "TFS changeset C" + changeset.ChangesetId.ToString(CultureInfo.InvariantCulture);

            var builder = new StringBuilder();
            builder.AppendLine(comment.TrimEnd());
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, GitTfsConstants.TfsCommitInfoFormat,
                targetServer, repositoryPath, changeset.ChangesetId));
            return builder.ToString();
        }

        private AuthorIdentity ResolveIdentity(RestIdentity identity)
        {
            var key = identity?.UniqueName;
            if (!string.IsNullOrWhiteSpace(key) && authorsFileField?.Authors?.TryGetValue(key, out var author) == true)
                return new AuthorIdentity(author.Name, author.Email);

            var name = identity?.DisplayName;
            var uniqueName = identity?.UniqueName;
            if (string.IsNullOrWhiteSpace(name))
                name = uniqueName;
            if (string.IsNullOrWhiteSpace(name))
                name = "Unknown TFS user";

            var email = uniqueName;
            if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@') < 0)
            {
                var separator = uniqueName?.IndexOf('\\') ?? -1;
                if (separator > 0 && separator + 1 < uniqueName.Length)
                    email = uniqueName.Substring(separator + 1).ToLowerInvariant() + "@" + uniqueName.Substring(0, separator).ToLowerInvariant() + ".tfs.local";
                else
                    email = name.ToLowerInvariant().Replace(' ', '.') + "@tfs.local";
            }

            return new AuthorIdentity(name, email);
        }

        private static DateTime GetCommitDate(RestChangeset changeset, RestChangesetReference reference)
        {
            var date = changeset.CreatedDate == default ? reference.CreatedDate : changeset.CreatedDate;
            return (date == default ? DateTimeOffset.UtcNow : date).UtcDateTime;
        }

        private static void ConfigureRepository(Repository repository, string targetServer, string repositoryPath)
        {
            repository.Config.Set("tfs-remote.default.url", targetServer, ConfigurationLevel.Local);
            repository.Config.Set("tfs-remote.default.repository", repositoryPath, ConfigurationLevel.Local);
            repository.Config.Set(GitTfsConstants.IgnoreBranches, "true", ConfigurationLevel.Local);
            repository.Config.Set(GitTfsConstants.DisableGitignoreSupport, "true", ConfigurationLevel.Local);
        }

        private static void UpdateRefs(Repository repository, Commit commit, int changesetId)
        {
            var headRef = repository.Head?.CanonicalName ?? "refs/heads/master";
            repository.Refs.Add(headRef, commit.Sha, allowOverwrite: true);
            repository.Refs.Add(GitRepository.ShortToTfsRemoteName(GitTfsConstants.DefaultRepositoryId), commit.Sha,
                "C" + changesetId.ToString(CultureInfo.InvariantCulture), allowOverwrite: true);
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
            foreach (var entry in EnumerateFiles(tree))
            {
                var filePath = Path.Combine(outputPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                using (var input = ((Blob)entry.Entry.Target).GetContentStream())
                using (var output = File.Create(filePath))
                    input.CopyTo(output);
            }
        }

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

        private static bool IsDelete(RestChange change)
            => (change.ChangeType ?? string.Empty).Split(',')
                .Select(type => type.Trim())
                .Any(type => string.Equals(type, "delete", StringComparison.OrdinalIgnoreCase));

        private static void WriteWorkingFile(string outputPath, string relativePath, byte[] content)
        {
            var filePath = GetWorkingFilePath(outputPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllBytes(filePath, content);
        }

        private static void DeleteWorkingFile(string outputPath, string relativePath)
        {
            var filePath = GetWorkingFilePath(outputPath, relativePath);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        private static string GetWorkingFilePath(string outputPath, string relativePath)
            => Path.Combine(outputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private readonly struct AuthorIdentity
        {
            public AuthorIdentity(string name, string email)
            {
                Name = name;
                Email = email;
            }

            public string Name { get; }
            public string Email { get; }
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
