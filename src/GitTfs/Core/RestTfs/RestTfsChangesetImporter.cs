namespace GitTfs.Core.RestTfs
{
    using System.Buffers;
    using GitTfs.Core;
    using GitTfs.Util;
    using LibGit2Sharp;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    public sealed class RestTfsChangesetImporter : IRestTfsChangesetImporter
    {
        private readonly AuthorsFile authorsFileField;
        private readonly TfvcFileDownloader fileDownloaderField;
        private readonly ILogger<RestTfsChangesetImporter> loggerField;

        public RestTfsChangesetImporter(AuthorsFile authorsFile,
            LegacyTfvcHistoryProvider legacyHistoryProvider = null,
            ILogger<RestTfsChangesetImporter> logger = null)
        {
            authorsFileField = authorsFile;
            loggerField = logger;
            fileDownloaderField = new TfvcFileDownloader(legacyHistoryProvider, logger);
        }

        public RestTfsChangesetImportResult Import(IRestTfsClient client, Repository repository,
            RestChangesetReference changesetReference, string targetServer, string repositoryPath,
            string outputPath, IDictionary<string, string> pathMap, Commit parent, bool noFallback,
            IChangesetProgressReporter progressReporter = null)
        {
            using var importMeasurement = GitTfsMetrics.MeasureChangesetImport();
            progressReporter?.DescribeChangeset(changesetReference.ChangesetId, changesetReference.Comment);
            progressReporter?.ReportActivity("Loading C" + changesetReference.ChangesetId);
            var changeset = client.GetChangeset(changesetReference.ChangesetId);
            progressReporter?.DescribeChangeset(changeset.ChangesetId, changeset.Comment ?? changesetReference.Comment);
            changeset.Changes ??= new List<RestChange>();
            if (!HasChangesWithinRepository(changeset, repositoryPath)
                && !HasTrackedSourceRename(changeset, repositoryPath, parent?.Tree))
            {
                var sourceRenameCount = changeset.Changes.Count(IsSourceRename);
                var skipReason = sourceRenameCount > 0
                    ? "source rename records contain no tracked old path"
                    : "no changes";
                progressReporter?.ReportActivity("Skipped C" + changeset.ChangesetId + ": " + skipReason);
                if (progressReporter == null)
                    loggerField?.LogInformation("C{ChangesetId}: skipped; {SkipReason} under {RepositoryPath}.",
                        changeset.ChangesetId, skipReason, repositoryPath);
                else
                    loggerField?.LogDebug("C{ChangesetId}: skipped; {SkipReason} under {RepositoryPath}.",
                        changeset.ChangesetId, skipReason, repositoryPath);
                GitTfsMetrics.RecordChangesetSkipped();
                return new RestTfsChangesetImportResult(true, changeset.ChangesetId, null,
                    0, 0, 0, 0, legacyFallbackUsed: false);
            }

            var treeDefinition = parent == null
                ? new TreeDefinition()
                : TreeDefinition.From(parent.Tree);
            var summary = new ChangesetFileSummary();
            ApplyChanges(client, repository, treeDefinition, pathMap, changeset, targetServer,
                repositoryPath, outputPath, noFallback, summary, progressReporter);

            var tree = repository.ObjectDatabase.CreateTree(treeDefinition);
            var message = BuildCommitMessage(changeset, changesetReference, targetServer, repositoryPath);
            var identity = ResolveIdentity(changeset.Author ?? changeset.CheckedInBy ?? changesetReference.Author);
            var signature = new Signature(identity.Name, identity.Email, GetCommitDate(changeset, changesetReference));
            var parents = parent == null ? Enumerable.Empty<Commit>() : new[] { parent };
            var commit = repository.ObjectDatabase.CreateCommit(signature, signature, message, tree, parents, false);
            UpdateRefs(repository, commit, changeset.ChangesetId);
            GitTfsMetrics.RecordChangesetImported(summary.FilesProcessed, summary.FilesDownloaded,
                summary.FilesReused, summary.FilesDeleted, summary.BytesDownloaded);
            progressReporter?.CompleteChangeset(changeset.ChangesetId, commit.Sha);

            return new RestTfsChangesetImportResult(false, changeset.ChangesetId, commit,
                summary.FilesProcessed, summary.FilesDownloaded, summary.FilesReused, summary.FilesDeleted,
                summary.LegacyFallbackUsed);
        }

        private void ApplyChanges(IRestTfsClient client, Repository repository, TreeDefinition treeDefinition,
            IDictionary<string, string> pathMap, RestChangeset changeset, string targetServer,
            string repositoryPath, string outputPath, bool noFallback, ChangesetFileSummary summary,
            IChangesetProgressReporter progressReporter)
        {
            var changes = changeset.Changes
                .Where(change => new TfvcChange(change).IsRelevantTo(repositoryPath))
                .ToArray();
            var filesToProcess = changes.Count(change => !IsDelete(change) && !change.Item.IsFolder
                && !IsSourceRename(change));
            var downloaded = 0;
            var reused = 0;
            var processed = 0;
            if (progressReporter == null)
            {
                loggerField?.LogInformation("C{ChangesetId}: processing {FileCount} file(s) (0%).",
                    changeset.ChangesetId, filesToProcess);
            }
            else
            {
                progressReporter.StartChangeset(changeset.ChangesetId, filesToProcess);
            }

            foreach (var change in changes.OrderBy(change => IsSourceRename(change) ? 1 : 0))
            {
                if (IsSourceRename(change))
                {
                    if (!IsWithinRepository(change.Item.Path, repositoryPath))
                        continue;

                    var sourcePath = ToRelativeGitPath(change.Item.Path, repositoryPath);
                    if (change.Item.IsFolder)
                        summary.FilesDeleted += RemovePathAndChildren(repository, treeDefinition,
                            pathMap, sourcePath, outputPath);
                    else
                    {
                        RemovePath(treeDefinition, pathMap, sourcePath, outputPath);
                        summary.FilesDeleted++;
                    }

                    continue;
                }

                if (change.Item.IsFolder)
                {
                    var targetWithinRepository = IsWithinRepository(change.Item.Path, repositoryPath);
                    var targetPath = targetWithinRepository
                        ? ToRelativeGitPath(change.Item.Path, repositoryPath)
                        : null;

                    if (IsDelete(change))
                    {
                        if (targetWithinRepository)
                            summary.FilesDeleted += RemovePathAndChildren(repository, treeDefinition,
                                pathMap, targetPath, outputPath);
                        continue;
                    }

                    var renameSources = GetRenameSources(change);
                    if (IsRename(change) && targetWithinRepository)
                    {
                        foreach (var source in renameSources.Where(source => IsWithinRepository(source, repositoryPath)))
                        {
                            var sourcePath = ToRelativeGitPath(source, repositoryPath);
                            MovePathAndChildren(repository, treeDefinition, pathMap, sourcePath, targetPath, outputPath);
                        }
                    }
                    else
                    {
                        foreach (var source in renameSources.Where(source => IsWithinRepository(source, repositoryPath)))
                        {
                            var sourcePath = ToRelativeGitPath(source, repositoryPath);
                            RemovePathAndChildren(repository, treeDefinition, pathMap, sourcePath, outputPath);
                        }
                    }

                    continue;
                }

                if (!IsWithinRepository(change.Item.Path, repositoryPath))
                {
                    RemoveRenameSources(treeDefinition, pathMap, change, repositoryPath, outputPath);
                    continue;
                }

                var relativePath = ToRelativeGitPath(change.Item.Path, repositoryPath);
                if (string.IsNullOrEmpty(relativePath))
                {
                    RemoveRenameSources(treeDefinition, pathMap, change, repositoryPath, outputPath);
                    continue;
                }

                if (IsDelete(change))
                {
                    RemoveRenameSources(treeDefinition, pathMap, change, repositoryPath, outputPath);
                    RemovePath(treeDefinition, pathMap, relativePath, outputPath);
                    summary.FilesDeleted++;
                    continue;
                }

                var isMergeChange = IsMergeChange(change);
                var existingPath = pathMap.TryGetValue(relativePath, out var currentPath) ? currentPath : relativePath;
                byte[] content = null;
                var reusedLocalFile = !isMergeChange
                    && TryReadMatchingLocalFile(outputPath, relativePath, change.Item.HashValue, out content);
                if (!reusedLocalFile)
                {
                    var download = fileDownloaderField.Download(client, change, changeset.ChangesetId,
                        targetServer, relativePath, noFallback);
                    content = download.Content;
                    summary.LegacyFallbackUsed |= download.LegacyFallbackUsed;
                }
                if (content == null)
                {
                    processed++;
                    progressReporter?.ReportFiles(changeset.ChangesetId, processed, filesToProcess);
                    continue;
                }

                RemoveRenameSources(treeDefinition, pathMap, change, repositoryPath, outputPath);
                if (!string.Equals(existingPath, relativePath, StringComparison.Ordinal))
                    treeDefinition.Remove(existingPath);

                var blob = repository.ObjectDatabase.CreateBlob(new MemoryStream(content, writable: false));
                treeDefinition.Add(relativePath, blob, LibGit2Sharp.Mode.NonExecutableFile);
                pathMap.Remove(relativePath);
                pathMap[relativePath] = relativePath;
                if (!reusedLocalFile)
                {
                    WriteWorkingFile(outputPath, relativePath, content);
                    downloaded++;
                    summary.FilesDownloaded++;
                    summary.BytesDownloaded += content.LongLength;
                }
                else
                {
                    reused++;
                    summary.FilesReused++;
                    loggerField?.LogDebug("C{ChangesetId}: reusing local file {RelativePath}; its TFVC hash matches.",
                        changeset.ChangesetId, relativePath);
                }

                processed++;
                summary.FilesProcessed++;
                progressReporter?.ReportFiles(changeset.ChangesetId, processed, filesToProcess);
                var percent = filesToProcess == 0 ? 100 : processed * 100 / filesToProcess;
                if (progressReporter == null)
                {
                    loggerField?.LogInformation("C{ChangesetId}: processed {ProcessedFiles}/{TotalFiles} file(s) ({Percent}%; downloaded {Downloaded}, reused {Reused}).",
                        changeset.ChangesetId, processed, filesToProcess, percent, downloaded, reused);
                }
            }

            if (filesToProcess == 0 && progressReporter == null)
                loggerField?.LogInformation("C{ChangesetId}: no file content to download.", changeset.ChangesetId);
        }

        private static bool IsMergeChange(RestChange change)
            => new TfvcChange(change).IsMerge;

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
                    && new TfvcChange(change).IsRelevantTo(repositoryPath));

        private static bool HasTrackedSourceRename(RestChangeset changeset, string repositoryPath, Tree currentTree)
        {
            if (currentTree == null)
                return false;

            var sourceRenames = (changeset.Changes ?? new List<RestChange>())
                .Where(change => IsSourceRename(change) && change.Item != null
                    && IsWithinRepository(change.Item.Path, repositoryPath))
                .Select(change => new
                {
                    Path = ToRelativeGitPath(change.Item.Path, repositoryPath),
                    change.Item.IsFolder,
                })
                .ToArray();
            if (sourceRenames.Length == 0)
                return false;

            return EnumerateFiles(currentTree).Any(file => sourceRenames.Any(source =>
                source.IsFolder
                    ? IsSameOrChildPath(file.Path, source.Path)
                    : string.Equals(file.Path, source.Path, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsSourceRename(RestChange change)
            => new TfvcChange(change).IsSourceRename;

        private static void RemoveRenameSources(TreeDefinition treeDefinition, IDictionary<string, string> pathMap,
            RestChange change, string repositoryPath, string outputPath)
        {
            foreach (var source in GetRenameSources(change))
            {
                if (IsWithinRepository(source, repositoryPath))
                    RemovePath(treeDefinition, pathMap, ToRelativeGitPath(source, repositoryPath), outputPath);
            }
        }

        private static IEnumerable<string> GetRenameSources(RestChange change)
            => new TfvcChange(change).RenameSources();

        private static int RemovePathAndChildren(Repository repository, TreeDefinition treeDefinition,
            IDictionary<string, string> pathMap, string relativePath, string outputPath)
        {
            if (relativePath == null)
                return 0;

            var files = EnumerateFiles(repository.ObjectDatabase.CreateTree(treeDefinition))
                .Where(file => IsSameOrChildPath(file.Path, relativePath))
                .ToArray();
            foreach (ref readonly var file in files.AsSpan())
            {
                treeDefinition.Remove(file.Path);
                DeleteWorkingFile(outputPath, file.Path);
            }

            RemovePathMappings(pathMap, relativePath);
            return files.Length;
        }

        private static void MovePathAndChildren(Repository repository, TreeDefinition treeDefinition,
            IDictionary<string, string> pathMap, string sourcePath, string targetPath, string outputPath)
        {
            if (sourcePath == null || targetPath == null
                || string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
                return;

            var sourcePrefix = string.IsNullOrEmpty(sourcePath) ? null : sourcePath.TrimEnd('/') + "/";
            var files = EnumerateFiles(repository.ObjectDatabase.CreateTree(treeDefinition))
                .Where(file => sourcePrefix == null
                    || file.Path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (ref readonly var file in files.AsSpan())
            {
                treeDefinition.Remove(file.Path);
                DeleteWorkingFile(outputPath, file.Path);
            }
            RemovePathMappings(pathMap, sourcePath);

            var copyBuffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                foreach (ref readonly var file in files.AsSpan())
                {
                    var suffix = sourcePrefix == null ? file.Path : file.Path.Substring(sourcePrefix.Length);
                    var movedPath = string.IsNullOrEmpty(targetPath) ? suffix : targetPath.TrimEnd('/') + "/" + suffix;
                    treeDefinition.Remove(movedPath);
                    treeDefinition.Add(movedPath, (Blob)file.Entry.Target, file.Entry.Mode);
                    pathMap[movedPath] = movedPath;

                    using var input = ((Blob)file.Entry.Target).GetContentStream();
                    WriteWorkingFile(outputPath, movedPath, input, copyBuffer);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copyBuffer);
            }
        }

        private static void RemovePathMappings(IDictionary<string, string> pathMap, string relativePath)
        {
            var keysToRemove = pathMap
                .Where(pair => IsSameOrChildPath(pair.Key, relativePath)
                    || IsSameOrChildPath(pair.Value, relativePath))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in keysToRemove)
                pathMap.Remove(key);
        }

        private static bool IsSameOrChildPath(string path, string parentPath)
        {
            if (string.IsNullOrEmpty(parentPath))
                return !string.IsNullOrEmpty(path);

            var pathSpan = path.AsSpan();
            var parentPathSpan = parentPath.AsSpan();
            if (pathSpan.Equals(parentPathSpan, StringComparison.OrdinalIgnoreCase))
                return true;

            var parentSpan = parentPath.AsSpan().TrimEnd('/');
            return pathSpan.Length > parentSpan.Length
                && pathSpan.StartsWith(parentSpan, StringComparison.OrdinalIgnoreCase)
                && pathSpan[parentSpan.Length] == '/';
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

        private string BuildCommitMessage(RestChangeset changeset, RestChangesetReference reference,
            string targetServer, string repositoryPath)
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

        private static void UpdateRefs(Repository repository, Commit commit, int changesetId)
        {
            var headRef = repository.Head?.CanonicalName ?? "refs/heads/master";
            repository.Refs.Add(headRef, commit.Sha, allowOverwrite: true);
            repository.Refs.Add(GitRepository.ShortToTfsRemoteName(GitTfsConstants.DefaultRepositoryId), commit.Sha,
                "C" + changesetId.ToString(CultureInfo.InvariantCulture), allowOverwrite: true);
        }

        private static bool IsWithinRepository(string serverPath, string repositoryPath)
        {
            if (string.IsNullOrWhiteSpace(serverPath))
                return false;

            var serverPathSpan = serverPath.AsSpan();
            var repositoryPathSpan = repositoryPath.AsSpan();
            return serverPathSpan.Equals(repositoryPathSpan, StringComparison.OrdinalIgnoreCase)
                || serverPathSpan.Length > repositoryPathSpan.Length
                    && serverPathSpan.StartsWith(repositoryPathSpan, StringComparison.OrdinalIgnoreCase)
                    && serverPathSpan[repositoryPathSpan.Length] == '/';
        }

        private static string ToRelativeGitPath(string serverPath, string repositoryPath)
            => serverPath.AsSpan(repositoryPath.Length).Trim('/').ToString().Replace('\\', '/');

        private static bool IsDelete(RestChange change)
            => new TfvcChange(change).IsDelete;

        private static bool IsRename(RestChange change)
            => new TfvcChange(change).IsRename;

        private static string GetWorkingFilePath(string outputPath, string relativePath)
            => Path.Combine(outputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static void WriteWorkingFile(string outputPath, string relativePath, byte[] content)
        {
            var filePath = GetWorkingFilePath(outputPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllBytes(filePath, content);
        }

        private static void WriteWorkingFile(string outputPath, string relativePath, Stream content, byte[] buffer)
        {
            var filePath = GetWorkingFilePath(outputPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            using var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
                buffer.Length, FileOptions.SequentialScan);

            int bytesRead;
            while ((bytesRead = content.Read(buffer.AsSpan())) > 0)
                output.Write(buffer.AsSpan(0, bytesRead));
        }

        private static void DeleteWorkingFile(string outputPath, string relativePath)
        {
            var filePath = GetWorkingFilePath(outputPath, relativePath);
            if (File.Exists(filePath))
                File.Delete(filePath);
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

        private sealed class ChangesetFileSummary
        {
            public int FilesProcessed { get; set; }
            public int FilesDownloaded { get; set; }
            public int FilesReused { get; set; }
            public int FilesDeleted { get; set; }
            public long BytesDownloaded { get; set; }
            public bool LegacyFallbackUsed { get; set; }
        }

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
