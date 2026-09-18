namespace GitTfs.Test.Core
{
    using global::GitTfs.Core;
    using global::GitTfs.Core.RestTfs;
    using global::GitTfs.Util;
    using global::LibGit2Sharp;
    using global::System.Net;
    using global::System.Net.Sockets;
    using global::System.Security.Cryptography;
    using global::System.Text;

    [TestClass]
    public class RestTfsCloneServiceTests
    {
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
                        NoParallel = true,
                        Resumable = true,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(settings, new AuthorsFile());

                    var result = service.Run(server.ServerUrl, "$/Project/Branch", outputPath);

                    Assert.Equal(GitTfsExitCodes.OK, result);
                    using (var repository = new Repository(outputPath))
                    {
                        Assert.Equal(2, repository.Commits.Count());
                        Assert.Equal("two", File.ReadAllText(Path.Combine(outputPath, "a.txt")));
                        Assert.Equal("git-tfs-id: [" + server.ServerUrl.TrimEnd('/') + "]$/Project/Branch;C2",
                            repository.Head.Tip.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Last());
                        Assert.Equal(outputPath, repository.Info.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
                    }
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
                        NoParallel = true,
                        Resumable = true,
                        Proxy = "none",
                    };
                    var service = new RestTfsCloneService(settings, new AuthorsFile());

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
                    var header = "HTTP/1.1 200 OK\r\nContent-Type: " + response.ContentType
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
                    if (!string.IsNullOrWhiteSpace(itemPath))
                        return Json("{\"count\":1,\"value\":["
                            + "{\"changesetId\":1,\"createdDate\":\"2020-01-01T00:00:00Z\",\"comment\":\"first\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                    if (fromId == "3")
                        return Json("{\"count\":0,\"value\":[]}");
                    if (fromId == "2")
                        return Json("{\"count\":1,\"value\":["
                            + "{\"changesetId\":3,\"createdDate\":\"2020-01-03T00:00:00Z\",\"comment\":\"outside\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                    if (fromId == "1")
                        return Json("{\"count\":1,\"value\":["
                            + "{\"changesetId\":2,\"createdDate\":\"2020-01-02T00:00:00Z\",\"comment\":\"second\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                    return Json("{\"count\":1,\"value\":["
                        + "{\"changesetId\":1,\"createdDate\":\"2020-01-01T00:00:00Z\",\"comment\":\"first\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"}}]}");
                }

                if (path.EndsWith("/Project/_apis/tfvc/changesets/1", StringComparison.OrdinalIgnoreCase))
                    return Json(ChangeSet(1, "first", "add"));
                if (path.EndsWith("/Project/_apis/tfvc/changesets/2", StringComparison.OrdinalIgnoreCase))
                    return Json(ChangeSet(2, "second", "edit"));
                if (path.EndsWith("/Project/_apis/tfvc/changesets/3", StringComparison.OrdinalIgnoreCase))
                    return Json("{\"changesetId\":3,\"createdDate\":\"2020-01-03T00:00:00Z\",\"comment\":\"outside\",\"author\":{\"displayName\":\"Test User\",\"uniqueName\":\"test@example.com\"},\"changes\":[{\"changeType\":\"edit\",\"item\":{\"path\":\"$/Project/Other/out.txt\",\"isFolder\":false}}]}");
                if (path.EndsWith("/Project/_apis/tfvc/items", StringComparison.OrdinalIgnoreCase))
                {
                    var version = GetQueryValue(uri, "versionDescriptor.version");
                    Interlocked.Increment(ref server.fileDownloadCountField);
                    return new FakeResponse(Encoding.UTF8.GetBytes(version == "1" ? "one" : "two"), "application/octet-stream");
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
