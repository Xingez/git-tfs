namespace GitTfs.Core.RestTfs
{
    using global::GitTfs.Util;
    using global::GitTfs.Commands;
    using global::LibGit2Sharp;
    using global::System.Diagnostics;
    using global::System.Globalization;
    using global::System.Security.Cryptography;
    using global::System.Text;

    /// <summary>
    /// Imports one TFVC folder into Git without creating or using a TFVC workspace.
    /// </summary>
    public sealed class RestTfsCloneService
    {
        private readonly GitTfsSettings settingsField;
        private readonly AuthorsFile authorsFileField;
        private readonly LegacyTfvcHistoryProvider legacyHistoryProviderField;

        public RestTfsCloneService(GitTfsSettings settings, AuthorsFile authorsFile,
            LegacyTfvcHistoryProvider legacyHistoryProvider = null)
        {
            settingsField = settings;
            authorsFileField = authorsFile;
            legacyHistoryProviderField = legacyHistoryProvider;
        }

        public int Run(string targetServer, string repositoryPath, string outputPath)
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
                using (var client = new RestTfsClient(targetServer, repositoryPath, settingsField))
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

                    Trace.TraceInformation("Using REST TFVC clone for " + repositoryPath + ".");
                    Trace.TraceInformation("Workspace creation is disabled; files are downloaded directly from the TFVC REST API.");
                    var legacyChangesetReferences = legacyHistoryProviderField?.IsAvailable == true
                        ? legacyHistoryProviderField.GetChangesets(targetServer, repositoryPath, lastChangesetId)
                        : null;
                    if (legacyChangesetReferences != null)
                    {
                        Trace.TraceInformation("Using legacy TFVC recursive history for " + repositoryPath + ".");
                        Trace.TraceInformation("Legacy history returned " + legacyChangesetReferences.Count
                            + " relevant changeset reference(s).");
                        foreach (var changesetReference in legacyChangesetReferences)
                        {
                            if (changesetReference.ChangesetId <= lastChangesetId)
                                continue;

                            ImportChangeset(client, repository, changesetReference, targetServer, repositoryPath,
                                absoluteOutputPath, pathMap, ref newestCommit, ref lastChangesetId, ref fetchedChangesets);
                        }
                    }
                    else
                    {
                        Trace.TraceWarning("Legacy TFVC history helper is unavailable; scanning project changesets as a REST fallback.");
                        Trace.TraceInformation("Scanning project changesets and filtering changes under " + repositoryPath + ".");

                        while (true)
                        {
                            var pageStartChangesetId = fromChangesetId;
                            var changesetReferences = client.GetChangesets(repositoryPath, fromChangesetId, batchSize,
                                filterByItemPath: false);
                            if (changesetReferences.Count == 0)
                                break;

                            Trace.TraceInformation("Changeset scan after C" + pageStartChangesetId + " returned "
                                + changesetReferences.Count + " reference(s), through C"
                                + changesetReferences.Max(reference => reference.ChangesetId) + ".");

                            foreach (var changesetReference in changesetReferences.OrderBy(reference => reference.ChangesetId))
                            {
                                if (changesetReference.ChangesetId <= lastScannedChangesetId)
                                    continue;

                                lastScannedChangesetId = changesetReference.ChangesetId;
                                ImportChangeset(client, repository, changesetReference, targetServer, repositoryPath,
                                    absoluteOutputPath, pathMap, ref newestCommit, ref lastChangesetId, ref fetchedChangesets);
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
                                Trace.TraceInformation("Changeset scan page did not advance; retrying after C"
                                    + pageStartChangesetId + ".");
                                continue;
                            }
                            fromChangesetId = lastReferenceId;
                        }
                    }

                    if (newestCommit != null)
                    {
                        MaterializeTree(repository, newestCommit.Tree, absoluteOutputPath);
                        Trace.TraceInformation("Clone complete: " + fetchedChangesets + " changeset(s), latest C" + lastChangesetId + ".");
                    }
                    else
                    {
                        Trace.TraceInformation("Clone complete: no changesets found under " + repositoryPath + ".");
                    }
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
                        Trace.WriteLine("Unable to clean failed clone directory: " + cleanupException.Message);
                    }
                }
                throw;
            }
        }

        private void ImportChangeset(RestTfsClient client, Repository repository, RestChangesetReference changesetReference,
            string targetServer, string repositoryPath, string outputPath, IDictionary<string, string> pathMap,
            ref Commit newestCommit, ref int lastChangesetId, ref int fetchedChangesets)
        {
            var changeset = client.GetChangeset(changesetReference.ChangesetId);
            changeset.Changes ??= new List<RestChange>();
            if (!HasChangesWithinRepository(changeset, repositoryPath))
            {
                Trace.TraceInformation("C" + changeset.ChangesetId + ": skipped; no changes under "
                    + repositoryPath + ".");
                return;
            }

            var treeDefinition = newestCommit == null
                ? new TreeDefinition()
                : TreeDefinition.From(newestCommit.Tree);
            ApplyChanges(client, repository, treeDefinition, pathMap, changeset, repositoryPath, outputPath);

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

            Trace.TraceInformation("C" + changeset.ChangesetId + " committed as " + commit.Sha + ".");
        }

        private void ApplyChanges(RestTfsClient client, Repository repository, TreeDefinition treeDefinition,
            IDictionary<string, string> pathMap, RestChangeset changeset, string repositoryPath, string outputPath)
        {
            var changes = changeset.Changes
                .Where(change => change?.Item != null)
                .Where(change => IsWithinRepository(change.Item.Path, repositoryPath)
                    || IsWithinRepository(change.SourceServerItem, repositoryPath)
                    || (change.MergeSources ?? new List<RestMergeSource>())
                        .Any(source => IsWithinRepository(source.ServerItem, repositoryPath)))
                .ToArray();
            var filesToProcess = changes.Count(change => !IsDelete(change) && !change.Item.IsFolder);
            var downloaded = 0;
            var reused = 0;
            var processed = 0;
            Trace.TraceInformation("C" + changeset.ChangesetId + ": processing " + filesToProcess + " file(s) (0%).");

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
                    content = client.DownloadFile(change.Item.Path, changeset.ChangesetId);
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
                    Trace.TraceInformation("C" + changeset.ChangesetId + ": reusing local file " + relativePath
                        + "; its TFVC hash matches.");
                }

                processed++;
                var percent = filesToProcess == 0 ? 100 : processed * 100 / filesToProcess;
                Trace.TraceInformation("C" + changeset.ChangesetId + ": processed " + processed + "/" + filesToProcess
                    + " file(s) (" + percent + "%; downloaded " + downloaded + ", reused " + reused + ").");
            }

            if (filesToProcess == 0)
                Trace.TraceInformation("C" + changeset.ChangesetId + ": no file content to download.");
        }

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
                    && (IsWithinRepository(change.Item.Path, repositoryPath)
                        || IsWithinRepository(change.SourceServerItem, repositoryPath)
                        || (change.MergeSources ?? new List<RestMergeSource>())
                            .Any(source => IsWithinRepository(source.ServerItem, repositoryPath))));

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
