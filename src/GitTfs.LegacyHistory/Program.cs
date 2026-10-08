namespace GitTfs.LegacyHistory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;

    using Microsoft.TeamFoundation.Client;
    using Microsoft.TeamFoundation.VersionControl.Client;
    using Microsoft.VisualStudio.Services.Client;
    using Microsoft.VisualStudio.Services.Common;
    using Newtonsoft.Json;

    using VssWindowsCredential = Microsoft.VisualStudio.Services.Common.WindowsCredential;

    internal static class Program
    {
        private const int DefaultBatchSize = 100;

        public static int Main(string[] args)
        {
            TfsTeamProjectCollection collection = null;
            VersionControlServer versionControl = null;
            string connectedServer = null;
            try
            {
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var request = JsonConvert.DeserializeObject<HistoryRequest>(line);
                        if (request == null)
                            throw new InvalidOperationException("The legacy history request was empty.");

                        if (versionControl == null
                            || !string.Equals(connectedServer, request.ServerUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            collection?.Dispose();
                            collection = Connect(request);
                            versionControl = collection.GetService<VersionControlServer>();
                            connectedServer = request.ServerUrl;
                        }

                        Run(versionControl, request);
                    }
                    catch (Exception exception)
                    {
                        collection?.Dispose();
                        collection = null;
                        versionControl = null;
                        connectedServer = null;
                        Console.WriteLine(JsonConvert.SerializeObject(new HistoryError
                        {
                            Message = exception.ToString(),
                        }));
                        Console.WriteLine(JsonConvert.SerializeObject(new HistoryComplete()));
                    }

                    Console.Out.Flush();
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                collection?.Dispose();
            }
        }

        private static TfsTeamProjectCollection Connect(HistoryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ServerUrl))
                throw new ArgumentException("The TFS server URL is required.");

            var serverUri = new Uri(request.ServerUrl, UriKind.Absolute);
            var credentials = CreateCredentials(request);
            var collection = new TfsTeamProjectCollection(serverUri, credentials);
            collection.EnsureAuthenticated();
            return collection;
        }

        private static void Run(VersionControlServer versionControl, HistoryRequest request)
        {
            if (string.Equals(request.Operation, "download", StringComparison.OrdinalIgnoreCase))
            {
                DownloadFile(versionControl, request);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.RepositoryPath))
                throw new ArgumentException("The TFS repository path is required.");

            var nextChangesetId = request.FromChangesetId >= int.MaxValue
                ? int.MaxValue
                : request.FromChangesetId + 1;
            var batchSize = request.BatchSize > 0 ? request.BatchSize : DefaultBatchSize;

            while (nextChangesetId < int.MaxValue)
            {
                var changesets = QueryHistory(versionControl, request.RepositoryPath, nextChangesetId, batchSize);
                if (changesets.Count == 0)
                    break;

                foreach (var changeset in changesets)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new HistoryChangeset
                    {
                        ChangesetId = changeset.ChangesetId,
                        CreatedDate = changeset.CreationDate,
                        Comment = changeset.Comment,
                        Author = new HistoryIdentity
                        {
                            DisplayName = changeset.OwnerDisplayName,
                            UniqueName = changeset.Owner,
                        },
                    }));
                }

                var lastChangesetId = changesets.Max(changeset => changeset.ChangesetId);
                if (lastChangesetId < nextChangesetId)
                    break;
                nextChangesetId = lastChangesetId >= int.MaxValue - 1
                    ? int.MaxValue
                    : lastChangesetId + 1;
            }

            Console.WriteLine(JsonConvert.SerializeObject(new HistoryComplete()));
        }

        private static void DownloadFile(VersionControlServer versionControl, HistoryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ItemPath))
                throw new ArgumentException("The TFS item path is required for a file download.");
            if (request.ChangesetId <= 0)
                throw new ArgumentException("A positive changeset ID is required for a file download.");

            var item = versionControl.GetItem(
                request.ItemPath,
                new ChangesetVersionSpec(request.ChangesetId),
                DeletedState.Any,
                GetItemsOptions.Download);
            if (item == null || item.ItemType != ItemType.File)
                throw new InvalidOperationException("The requested TFS item is not a file at changeset C"
                    + request.ChangesetId + ".");

            using (var input = item.DownloadFile())
            using (var output = new System.IO.MemoryStream())
            {
                input.CopyTo(output);
                Console.WriteLine(JsonConvert.SerializeObject(new FileContent
                {
                    Content = Convert.ToBase64String(output.ToArray()),
                }));
            }

            Console.WriteLine(JsonConvert.SerializeObject(new HistoryComplete()));
        }

        private static List<Changeset> QueryHistory(VersionControlServer versionControl, string repositoryPath,
            int fromChangesetId, int batchSize)
        {
            var history = versionControl.QueryHistory(
                repositoryPath,
                VersionSpec.Latest,
                0,
                RecursionType.Full,
                null,
                new ChangesetVersionSpec(fromChangesetId),
                VersionSpec.Latest,
                batchSize,
                includeChanges: false,
                slotMode: false,
                includeDownloadInfo: false);

            return history.Cast<Changeset>()
                .Where(changeset => changeset.ChangesetId >= fromChangesetId)
                .OrderBy(changeset => changeset.ChangesetId)
                .ToList();
        }

        private static VssCredentials CreateCredentials(HistoryRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.Pat))
                return new VssBasicCredential(string.Empty, request.Pat);

            if (!string.IsNullOrWhiteSpace(request.Username))
                return new VssClientCredentials(new VssWindowsCredential(CreateNetworkCredential(request.Username, request.Password)));

            return new VssClientCredentials();
        }

        private static NetworkCredential CreateNetworkCredential(string username, string password)
        {
            var separator = username.IndexOf('\\');
            if (separator > 0)
                return new NetworkCredential(username.Substring(separator + 1), password, username.Substring(0, separator));
            return new NetworkCredential(username, password);
        }

        private sealed class HistoryRequest
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

        private sealed class FileContent
        {
            public string Type { get; } = "file";
            public string Content { get; set; }
        }

        private sealed class HistoryChangeset
        {
            public string Type { get; } = "changeset";
            public int ChangesetId { get; set; }
            public DateTime CreatedDate { get; set; }
            public string Comment { get; set; }
            public HistoryIdentity Author { get; set; }
        }

        private sealed class HistoryComplete
        {
            public string Type { get; } = "complete";
        }

        private sealed class HistoryError
        {
            public string Type { get; } = "error";
            public string Message { get; set; }
        }

        private sealed class HistoryIdentity
        {
            public string DisplayName { get; set; }
            public string UniqueName { get; set; }
        }
    }
}
