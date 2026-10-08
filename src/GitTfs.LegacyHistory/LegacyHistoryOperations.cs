namespace GitTfs.LegacyHistory
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.TeamFoundation.VersionControl.Client;
    using Newtonsoft.Json;

    internal static class LegacyHistoryOperations
    {
        private const int DefaultBatchSize = 100;

        public static void Run(VersionControlServer versionControl, HistoryRequest request)
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

            var version = new ChangesetVersionSpec(request.ChangesetId);
            var item = request.DeletionId > 0
                ? versionControl.GetItem(request.ItemPath, version, request.DeletionId, GetItemsOptions.Download)
                : versionControl.GetItem(request.ItemPath, version, DeletedState.Any, GetItemsOptions.Download);
            if (item == null || item.ItemType != ItemType.File)
            {
                throw new InvalidOperationException("The requested TFS item is not a file at changeset C"
                    + request.ChangesetId + ".");
            }

            using (var input = item.DownloadFile())
            using (var output = new MemoryStream())
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
    }
}
