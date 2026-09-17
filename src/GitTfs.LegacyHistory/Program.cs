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
            try
            {
                var request = JsonConvert.DeserializeObject<HistoryRequest>(Console.In.ReadToEnd());
                if (request == null)
                    throw new InvalidOperationException("The legacy history request was empty.");

                Run(request);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void Run(HistoryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ServerUrl))
                throw new ArgumentException("The TFS server URL is required.");
            if (string.IsNullOrWhiteSpace(request.RepositoryPath))
                throw new ArgumentException("The TFS repository path is required.");

            var serverUri = new Uri(request.ServerUrl, UriKind.Absolute);
            var credentials = CreateCredentials(request);
            using (var collection = new TfsTeamProjectCollection(serverUri, credentials))
            {
                collection.EnsureAuthenticated();
                var versionControl = collection.GetService<VersionControlServer>();
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
            public string ServerUrl { get; set; }
            public string RepositoryPath { get; set; }
            public int FromChangesetId { get; set; }
            public int BatchSize { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string Pat { get; set; }
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

        private sealed class HistoryIdentity
        {
            public string DisplayName { get; set; }
            public string UniqueName { get; set; }
        }
    }
}
