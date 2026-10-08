namespace GitTfs.LegacyHistory
{
    using System;
    using System.IO;
    using Microsoft.TeamFoundation.VersionControl.Client;
    using Newtonsoft.Json;

    internal static class LegacyHistoryOperations
    {
        public static void Run(VersionControlServer versionControl, HistoryRequest request)
        {
            if (!string.Equals(request.Operation, "download", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The legacy helper only supports file downloads.");

            DownloadFile(versionControl, request);
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

    }
}
