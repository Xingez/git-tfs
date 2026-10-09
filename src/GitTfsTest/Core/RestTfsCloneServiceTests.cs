namespace GitTfs.Test.Core
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Assert = GitTfs.Test.TestAssert;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using GitTfs.Core;
    using GitTfs.Core.RestTfs;
    using GitTfs.Util;
    using LibGit2Sharp;
    using Microsoft.Extensions.Options;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    [TestClass]
    public class RestTfsCloneServiceTests
    {
        [TestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        public async Task ConsoleUsesSpectreUnlessDebugIsEnabled(bool configuredDebug, bool commandLineDebug)
        {
            using var server = new FakeTfvcServer();
            var directory = Path.Combine(Path.GetTempPath(), "git-tfs-console-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var settingsPath = Path.Combine(directory, "appsettings.json");
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(new GitTfsSettings
                {
                    TargetServer = server.ServerUrl,
                    Debug = configuredDebug,
                    BatchSize = 1
                }));
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = directory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                start.ArgumentList.Add(typeof(Program).Assembly.Location);
                if (commandLineDebug)
                    start.ArgumentList.Add("--debug");
                start.ArgumentList.Add("--no-fallback");
                start.ArgumentList.Add("$/Project/Branch");
                start.ArgumentList.Add(Path.Combine(directory, "clone"));
                start.Environment["GIT_TFS_APPSETTINGS"] = settingsPath;
                start.Environment["GIT_TFS_DEBUG"] = configuredDebug.ToString();
                start.Environment["GIT_TFS_TARGET_SERVER"] = server.ServerUrl;
                start.Environment.Remove("GIT_TFS_CLIENT");
                using var process = Process.Start(start);
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
                var output = await outputTask + await errorTask;
                Assert.Equal(GitTfsExitCodes.OK, process.ExitCode);
                if (configuredDebug || commandLineDebug)
                {
                    Assert.Contains("Clone complete", output);
                    Assert.Contains("TFS request:", output);
                    Assert.False(output.Contains("TFVC changeset scan complete"));
                }
                else
                {
                    Assert.Contains("TFVC changeset scan complete", output);
                    Assert.Contains("C1", output);
                    Assert.Contains("C2", output);
                    Assert.False(output.Contains("info:"));
                    Assert.False(output.Contains("dbug:"));
                    Assert.False(output.Contains("Clone complete"));
                }
            }
            finally { DeleteDirectory(directory); }
        }

        [TestMethod]
        public void ReleasesPackFilesBeforeRunningMaintenanceOnResume()
        {
            using var server = new FakeTfvcServer();
            var output = Path.Combine(Path.GetTempPath(), "git-tfs-repack-" + Guid.NewGuid().ToString("N"));
            var logger = new MaintenanceLogger();
            try
            {
                var service = new RestTfsCloneService(Options.Create(new GitTfsSettings { BatchSize = 1 }),
                    new AuthorsFile(), logger: logger, gitHelpers: new GitHelpers(null));
                service.Run(server.ServerUrl, "$/Project/Branch", output);
                Assert.True(Directory.GetFiles(Path.Combine(output, ".git", "objects", "pack"), "*.pack").Length > 0);

                service.Run(server.ServerUrl, "$/Project/Branch", output);

                Assert.False(logger.MaintenanceFailed, "External Git must be able to repack a resumed repository.");
                using var repository = new Repository(output);
                Assert.Equal(2, repository.Commits.Count());
                Assert.Empty(repository.RetrieveStatus());
            }
            finally { DeleteDirectory(output); }
        }

        private sealed class MaintenanceLogger : Microsoft.Extensions.Logging.ILogger<RestTfsCloneService>
        {
            public bool MaintenanceFailed { get; private set; }
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
                TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                MaintenanceFailed |= state.ToString().Contains("Forced Git maintenance failed");
            }
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(100)]
        public void ClonesAndResumesInclusivePagesEndingWithChangesetNotFound(int batchSize)
        {
            using var server = new FakeTfvcServer { InclusiveChangesetHistory = true };
            var output = Path.Combine(Path.GetTempPath(), "git-tfs-inclusive-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new GitTfsSettings { BatchSize = batchSize, Proxy = "none" };
                var service = new RestTfsCloneService(Options.Create(settings), new AuthorsFile());
                Assert.Equal(GitTfsExitCodes.OK, service.Run(server.ServerUrl, "$/Project/Branch", output, noFallback: true));
                Assert.True(server.ChangesetCursors.Count <= 5, "Inclusive history should need at most one repeated boundary page.");
                using (var repository = new Repository(output))
                {
                    Assert.Equal(2, repository.Commits.Count());
                    Assert.Empty(repository.RetrieveStatus());
                }
                Assert.Equal(GitTfsExitCodes.OK, service.Run(server.ServerUrl, "$/Project/Branch", output, noFallback: true));
                using var resumed = new Repository(output);
                Assert.Equal(2, resumed.Commits.Count());
                Assert.Empty(resumed.RetrieveStatus());
            }
            finally { DeleteDirectory(output); }
        }

        [TestMethod]
        public void ClonesChangesetsIntoGitCommitsWithoutAWorkspace()
        {
            using (var server = new FakeTfvcServer())
            {
                var outputPath = Path.Combine(Path.GetTempPath(), "git-tfs-rest-test-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var settings = new GitTfsSettings
                    {
                        BatchSize = 1,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(Options.Create(settings), new AuthorsFile(), gitHelpers: new GitHelpers(null));

                    var result = service.Run(server.ServerUrl, "$/Project/Branch", outputPath);

                    Assert.Equal(GitTfsExitCodes.OK, result);
                    using (var repository = new Repository(outputPath))
                    {
                        Assert.Equal(2, repository.Commits.Count());
                        Assert.Equal("two", File.ReadAllText(Path.Combine(outputPath, "a.txt")));
                        Assert.Equal("git-tfs-id: [" + server.ServerUrl.TrimEnd('/') + "]$/Project/Branch;C2",
                            repository.Head.Tip.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Last());
                        Assert.True(repository.Index.Any(entry => entry.Path == "a.txt"));
                        Assert.Empty(repository.RetrieveStatus());
                        Assert.Equal(outputPath, repository.Info.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
                    }

                    Assert.True(server.ChangesetItemPaths.Count > 0);
                    Assert.True(server.ChangesetItemPaths.All(path => path == "$/Project/Branch"),
                        "Every changeset query must be scoped to the requested TFVC path.");
                }
                finally
                {
                    DeleteDirectory(outputPath);
                }
            }
        }

        [TestMethod]
        public void ReusesMatchingTfvcHashAndRepairsMismatchedLocalFile()
        {
            using (var server = new FakeTfvcServer())
            {
                var outputPath = Path.Combine(Path.GetTempPath(), "git-tfs-rest-hash-test-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var settings = new GitTfsSettings
                    {
                        BatchSize = 1,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(Options.Create(settings), new AuthorsFile(), gitHelpers: new GitHelpers(null));

                    service.Run(server.ServerUrl, "$/Project/Branch", outputPath);
                    Assert.Equal(2, server.FileDownloadCount);

                    Commit firstCommit;
                    using (var repository = new Repository(outputPath))
                    {
                        firstCommit = repository.Commits.Single(commit => commit.Message.Contains(";C1"));
                        repository.Refs.UpdateTarget(repository.Head.CanonicalName, firstCommit.Sha);
                    }

                    service.Run(server.ServerUrl, "$/Project/Branch", outputPath);

                    Assert.Equal(2, server.FileDownloadCount);
                    Assert.Equal("two", File.ReadAllText(Path.Combine(outputPath, "a.txt")));

                    File.WriteAllText(Path.Combine(outputPath, "a.txt"), "tampered");
                    using (var repository = new Repository(outputPath))
                        repository.Refs.UpdateTarget(repository.Head.CanonicalName, firstCommit.Sha);

                    service.Run(server.ServerUrl, "$/Project/Branch", outputPath);

                    Assert.Equal(3, server.FileDownloadCount);
                    Assert.Equal("two", File.ReadAllText(Path.Combine(outputPath, "a.txt")));
                }
                finally
                {
                    DeleteDirectory(outputPath);
                }
            }
        }

        [TestMethod]
        public void SkipsMissingMergeOnlyFileAndStillAppliesExplicitDelete()
        {
            using (var server = new FakeTfvcServer { ReturnMissingMergeTargetAndDeleteAFile = true })
            {
                var outputPath = Path.Combine(Path.GetTempPath(), "git-tfs-rest-merge-404-test-"
                    + Guid.NewGuid().ToString("N"));
                try
                {
                    var settings = new GitTfsSettings
                    {
                        BatchSize = 1,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(Options.Create(settings), new AuthorsFile(),
                        gitHelpers: new GitHelpers(null));

                    var result = service.Run(server.ServerUrl, "$/Project/Branch", outputPath);

                    Assert.Equal(GitTfsExitCodes.OK, result);
                    Assert.False(File.Exists(Path.Combine(outputPath, "a.txt")));
                    Assert.False(File.Exists(Path.Combine(outputPath, "merged.txt")));
                    Assert.Equal(2, server.FileDownloadCount);
                    Assert.True(server.FileDownloadQueries.All(query =>
                        !query.Contains("versionDescriptor.versionType=MergeSource", StringComparison.OrdinalIgnoreCase)
                        && !query.Contains("versionDescriptor.versionOption=Previous", StringComparison.OrdinalIgnoreCase)));

                    using (var repository = new Repository(outputPath))
                    {
                        Assert.Equal(2, repository.Commits.Count());
                        Assert.Empty(repository.Head.Tip.Tree);
                        Assert.Empty(repository.RetrieveStatus());
                    }
                }
                finally
                {
                    DeleteDirectory(outputPath);
                }
            }
        }

        [TestMethod]
        public void RemovesStaleFilesAndDirectoriesNotPresentInTheLatestTree()
        {
            using (var server = new FakeTfvcServer())
            {
                var outputPath = Path.Combine(Path.GetTempPath(), "git-tfs-rest-cleanup-test-"
                    + Guid.NewGuid().ToString("N"));
                try
                {
                    var settings = new GitTfsSettings
                    {
                        BatchSize = 1,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(Options.Create(settings), new AuthorsFile(),
                        gitHelpers: new GitHelpers(null));

                    service.Run(server.ServerUrl, "$/Project/Branch", outputPath);
                    File.WriteAllText(Path.Combine(outputPath, "stale.txt"), "stale");
                    var staleDirectory = Path.Combine(outputPath, "stale-folder", "nested");
                    Directory.CreateDirectory(staleDirectory);
                    File.WriteAllText(Path.Combine(staleDirectory, "old.txt"), "old");

                    service.Run(server.ServerUrl, "$/Project/Branch", outputPath);

                    Assert.False(File.Exists(Path.Combine(outputPath, "stale.txt")));
                    Assert.False(Directory.Exists(Path.Combine(outputPath, "stale-folder")));
                    Assert.True(File.Exists(Path.Combine(outputPath, "a.txt")));
                }
                finally
                {
                    DeleteDirectory(outputPath);
                }
            }
        }

        [TestMethod]
        public void StopsWhenLatestTfvcChecksumDoesNotMatchTheDownloadedFile()
        {
            using (var server = new FakeTfvcServer { ReturnMismatchedLatestHash = true })
            {
                var outputPath = Path.Combine(Path.GetTempPath(), "git-tfs-rest-verification-test-"
                    + Guid.NewGuid().ToString("N"));
                try
                {
                    var settings = new GitTfsSettings
                    {
                        BatchSize = 1,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(Options.Create(settings), new AuthorsFile(),
                        gitHelpers: new GitHelpers(null));

                    var exception = Assert.Throws<GitTfsException>(() =>
                        service.Run(server.ServerUrl, "$/Project/Branch", outputPath));

                    Assert.Contains("checksum mismatch", exception.Message);
                }
                finally
                {
                    DeleteDirectory(outputPath);
                }
            }
        }

        [TestMethod]
        public void MergesCloneIntoTargetBranchAndPushes()
        {
            using (var server = new FakeTfvcServer())
            {
                var outputPath = Path.Combine(Path.GetTempPath(), "git-tfs-rest-target-test-" + Guid.NewGuid().ToString("N"));
                var targetPath = Path.Combine(Path.GetTempPath(), "git-tfs-rest-target-bare-test-" + Guid.NewGuid().ToString("N"));
                try
                {
                    CreateTargetRepository(targetPath);
                    var settings = new GitTfsSettings
                    {
                        BatchSize = 1,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(Options.Create(settings), new AuthorsFile(),
                        gitHelpers: new GitHelpers(null));

                    var result = service.Run(server.ServerUrl, "$/Project/Branch", outputPath,
                        targetCloneUrl: targetPath.Replace('\\', '/'), targetBranch: "main");

                    Assert.Equal(GitTfsExitCodes.OK, result);
                    using (var repository = new Repository(outputPath))
                    {
                        Assert.Equal("main", repository.Head.FriendlyName);
                        Assert.Contains("GIT-TFS Merge (master) with main", repository.Head.Tip.Message);
                        Assert.True(repository.Index.Any(entry => entry.Path == "a.txt"));
                        Assert.True(repository.Index.Any(entry => entry.Path == "target.txt"));
                        Assert.Empty(repository.RetrieveStatus());
                    }

                    using (var targetRepository = new Repository(targetPath))
                    {
                        Assert.Contains("GIT-TFS Merge (master) with main",
                            targetRepository.Branches["main"].Tip.Message);
                    }
                }
                finally
                {
                    DeleteDirectory(outputPath);
                    DeleteDirectory(targetPath);
                }
            }
        }

        private static void CreateTargetRepository(string path)
        {
            Repository.Init(path, isBare: true);
            using (var repository = new Repository(path))
            {
                var blob = repository.ObjectDatabase.CreateBlob(
                    new MemoryStream(Encoding.UTF8.GetBytes("target"), writable: false));
                var treeDefinition = new TreeDefinition();
                treeDefinition.Add("target.txt", blob, LibGit2Sharp.Mode.NonExecutableFile);
                var tree = repository.ObjectDatabase.CreateTree(treeDefinition);
                var signature = new Signature("Target User", "target@example.com", DateTimeOffset.UtcNow);
                var commit = repository.ObjectDatabase.CreateCommit(signature, signature, "target", tree,
                    Enumerable.Empty<Commit>(), false);
                repository.Refs.Add("refs/heads/main", commit.Sha, allowOverwrite: true);
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(directory, FileAttributes.Normal);

            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(path, true);
        }

        private sealed class FakeTfvcServer : IDisposable
        {
            private readonly TcpListener listenerField;
            private readonly CancellationTokenSource cancellationField = new CancellationTokenSource();
            private readonly Task serverTaskField;
            private int fileDownloadCountField;

            public FakeTfvcServer()
            {
                listenerField = new TcpListener(IPAddress.Loopback, 0);
                listenerField.Start();
                var endpoint = (IPEndPoint)listenerField.LocalEndpoint;
                ServerUrl = "http://127.0.0.1:" + endpoint.Port + "/tfs/DefaultCollection";
                serverTaskField = Task.Run(RunAsync);
            }

            public string ServerUrl { get; }
            public int FileDownloadCount => Volatile.Read(ref fileDownloadCountField);
            public bool ReturnMismatchedLatestHash { get; set; }
            public bool ReturnMissingMergeTargetAndDeleteAFile { get; set; }
            public bool InclusiveChangesetHistory { get; set; }
            public ConcurrentQueue<int> ChangesetCursors { get; } = new ConcurrentQueue<int>();
            public ConcurrentQueue<string> FileDownloadQueries { get; } = new ConcurrentQueue<string>();
            public ConcurrentQueue<string> ChangesetItemPaths { get; } = new ConcurrentQueue<string>();

            public void Dispose()
            {
                cancellationField.Cancel();
                listenerField.Stop();
                try
                {
                    serverTaskField.GetAwaiter().GetResult();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SocketException)
                {
                }
            }

            private async Task RunAsync()
            {
                while (!cancellationField.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listenerField.AcceptTcpClientAsync(cancellationField.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }

                    _ = HandleAsync(client, this);
                }
            }

            private static async Task HandleAsync(TcpClient client, FakeTfvcServer server)
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
                {
                    var requestLine = await reader.ReadLineAsync();
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                    {
                    }

                    var target = requestLine.Split(' ')[1];
                    var uri = new Uri("http://localhost" + target);
                    var response = BuildResponse(uri, server);
                    var header = "HTTP/1.1 " + response.Status + "\r\nContent-Type: " + response.ContentType
                        + "\r\nContent-Length: " + response.Content.Length
                        + "\r\nConnection: close\r\n\r\n";
                    var headerBytes = Encoding.ASCII.GetBytes(header);
                    await stream.WriteAsync(headerBytes);
                    await stream.WriteAsync(response.Content);
                }
            }

            private static FakeResponse BuildResponse(Uri uri, FakeTfvcServer server)
            {
                var path = uri.AbsolutePath;
                if (path.EndsWith("/Project/_apis/tfvc/changesets", StringComparison.OrdinalIgnoreCase))
                {
                    var fromId = GetQueryValue(uri, "searchCriteria.fromId");
                    var itemPath = GetQueryValue(uri, "searchCriteria.itemPath");
                    if (server.InclusiveChangesetHistory)
                    {
                        var cursor = string.IsNullOrEmpty(fromId) ? 0 : int.Parse(fromId);
                        server.ChangesetCursors.Enqueue(cursor);
                        if (cursor > 3)
                            return Json("{\"typeKey\":\"ChangesetNotFoundException\"}", "404 Not Found");
                        var top = int.Parse(GetQueryValue(uri, "$top"));
                        var references = Enumerable.Range(1, 3).Where(id => id >= cursor).Take(top)
                            .Select(id => new { changesetId = id });
                        return Json(System.Text.Json.JsonSerializer.Serialize(new { value = references }));
                    }
                    if (!string.IsNullOrWhiteSpace(itemPath))
                    {
                        server.ChangesetItemPaths.Enqueue(itemPath);
                        if (fromId == "2")
                            return Json("{\"count\":1,\"value\":["
                                + "{\"changesetId\":3,\"createdDate\":\"2020-01-03T00:00:00Z\",\"comment\":\"source rename\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                        if (fromId == "1")
                            return Json("{\"count\":1,\"value\":["
                                + "{\"changesetId\":2,\"createdDate\":\"2020-01-02T00:00:00Z\",\"comment\":\"second\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                        if (fromId == "3")
                            return Json("{\"count\":0,\"value\":[]}");
                        return Json("{\"count\":1,\"value\":["
                            + "{\"changesetId\":1,\"createdDate\":\"2020-01-01T00:00:00Z\",\"comment\":\"first\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                    }
                    if (fromId == "2")
                        return Json("{\"count\":1,\"value\":["
                            + "{\"changesetId\":3,\"createdDate\":\"2020-01-03T00:00:00Z\",\"comment\":\"source rename\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                    if (fromId == "1")
                        return Json("{\"count\":1,\"value\":["
                            + "{\"changesetId\":2,\"createdDate\":\"2020-01-02T00:00:00Z\",\"comment\":\"second\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                    if (fromId == "3")
                        return Json("{\"count\":1,\"value\":["
                            + "{\"changesetId\":4,\"createdDate\":\"2020-01-04T00:00:00Z\",\"comment\":\"outside\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                    if (fromId == "4")
                        return Json("{\"count\":0,\"value\":[]}");
                    return Json("{\"count\":1,\"value\":["
                        + "{\"changesetId\":1,\"createdDate\":\"2020-01-01T00:00:00Z\",\"comment\":\"first\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                }

                if (path.EndsWith("/Project/_apis/tfvc/changesets/1", StringComparison.OrdinalIgnoreCase))
                    return Json(ChangeSet(1, "first", "add"));
                if (path.EndsWith("/Project/_apis/tfvc/changesets/2", StringComparison.OrdinalIgnoreCase))
                {
                    if (server.ReturnMissingMergeTargetAndDeleteAFile)
                        return Json("{\"changesetId\":2,\"createdDate\":\"2020-01-02T00:00:00Z\","
                            + "\"comment\":\"merge and delete\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"},"
                            + "\"changes\":["
                            + "{\"changeType\":\"merge\",\"item\":{\"path\":\"$/Project/Branch/merged.txt\",\"isFolder\":false},"
                            + "\"mergeSources\":[{\"serverItem\":\"$/Project/Other/merged.txt\",\"versionFrom\":1,\"versionTo\":1}]},"
                            + "{\"changeType\":\"delete\",\"item\":{\"path\":\"$/Project/Branch/a.txt\",\"isFolder\":false,\"deletionId\":1}}]}");
                    return Json(ChangeSet(2, "second", "edit"));
                }
                if (path.EndsWith("/Project/_apis/tfvc/changesets/3", StringComparison.OrdinalIgnoreCase))
                    return Json("{\"changesetId\":3,\"createdDate\":\"2020-01-03T00:00:00Z\",\"comment\":\"source rename\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"},\"changes\":[{\"changeType\":\"sourceRename\",\"item\":{\"path\":\"$/Project/Branch/old.txt\",\"isFolder\":false,\"hashValue\":\"FJYD5sA1FjYqjaI/Yk25RQ==\"}}]}");
                if (path.EndsWith("/Project/_apis/tfvc/changesets/4", StringComparison.OrdinalIgnoreCase))
                    return Json("{\"changesetId\":4,\"createdDate\":\"2020-01-04T00:00:00Z\",\"comment\":\"outside\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"},\"changes\":[{\"changeType\":\"edit\",\"item\":{\"path\":\"$/Project/Other/out.txt\",\"isFolder\":false}}]}");
                if (path.EndsWith("/Project/_apis/tfvc/items", StringComparison.OrdinalIgnoreCase))
                {
                    var version = GetQueryValue(uri, "versionDescriptor.version");
                    if (string.Equals(GetQueryValue(uri, "download"), "true", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref server.fileDownloadCountField);
                        server.FileDownloadQueries.Enqueue(uri.Query);
                        if (server.ReturnMissingMergeTargetAndDeleteAFile
                            && version == "2"
                            && string.Equals(GetQueryValue(uri, "path"), "$/Project/Branch/merged.txt", StringComparison.OrdinalIgnoreCase))
                            return Json("{\"message\":\"not found\"}", "404 Not Found");
                        return new FakeResponse(Encoding.UTF8.GetBytes(version == "1" ? "one" : "two"), "application/octet-stream");
                    }

                    if (server.ReturnMissingMergeTargetAndDeleteAFile)
                        return Json("{\"count\":0,\"value\":[]}");

                    var hash = server.ReturnMismatchedLatestHash
                        ? Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes("not-two")))
                        : Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes("two")));
                    return Json("{\"count\":1,\"value\":[{\"path\":\"$/Project/Branch/a.txt\",\"isFolder\":false,\"hashValue\":\""
                        + hash + "\"}]}");
                }

                return Json("{\"message\":\"not found\"}", "404 Not Found");
            }

            private static string ChangeSet(int id, string comment, string changeType)
            {
                var content = id == 1 ? "one" : "two";
                var hash = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(content)));
                return "{\"changesetId\":" + id + ",\"createdDate\":\"2020-01-0" + id
                    + "T00:00:00Z\",\"comment\":\"" + comment
                    + "\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"},\"changes\":[{\"changeType\":\""
                    + changeType + "\",\"item\":{\"path\":\"$/Project/Branch/a.txt\",\"isFolder\":false,\"hashValue\":\""
                    + hash + "\"}}]}";
            }

            private static string GetQueryValue(Uri uri, string key)
            {
                foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var separator = pair.IndexOf('=');
                    if (separator < 0)
                        continue;
                    var name = Uri.UnescapeDataString(pair.Substring(0, separator));
                    if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                        return Uri.UnescapeDataString(pair.Substring(separator + 1));
                }
                return null;
            }

            private static FakeResponse Json(string content, string status = "200 OK")
                => new FakeResponse(Encoding.UTF8.GetBytes(content), "application/json", status);
        }

        private sealed class FakeResponse
        {
            public FakeResponse(byte[] content, string contentType, string status = "200 OK")
            {
                Content = content;
                ContentType = contentType;
                Status = status;
            }

            public byte[] Content { get; }
            public string ContentType { get; }
            public string Status { get; }
        }
    }
}
