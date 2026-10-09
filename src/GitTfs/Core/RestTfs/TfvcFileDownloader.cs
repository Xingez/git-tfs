namespace GitTfs.Core.RestTfs
{
    using Microsoft.Extensions.Logging;

    internal sealed class TfvcFileDownloader
    {
        private readonly LegacyTfvcHistoryProvider legacy;
        private readonly ILogger logger;

        public TfvcFileDownloader(LegacyTfvcHistoryProvider legacy, ILogger logger)
        {
            this.legacy = legacy;
            this.logger = logger;
        }

        public DownloadResult Download(IRestTfsClient client, RestChange change, int changesetId,
            string targetServer, string relativePath, bool noFallback)
        {
            RestTfsException missingVersion = null;
            var classification = new TfvcChange(change);
            foreach (var (versionType, option) in Versions(classification))
            {
                try
                {
                    return new DownloadResult(client.DownloadFile(change.Item.Path, changesetId, versionType, option), false);
                }
                catch (RestTfsException exception) when (exception.StatusCode == 404)
                {
                    missingVersion = exception;
                    if (classification.IsMerge && option == null)
                    {
                        logger?.LogDebug("Skipping merge-only file {RelativePath} from C{ChangesetId}; the target changeset returned 404.",
                            relativePath, changesetId);
                        return new DownloadResult(null, false);
                    }
                }
            }

            var failure = "The REST version downloads failed for " + relativePath + " at C" + changesetId;
            if (noFallback)
                throw new GitTfsException(failure + "; legacy TFVC fallback is disabled.", missingVersion);
            if (legacy?.IsAvailable != true)
                throw new GitTfsException(failure + ".", missingVersion);

            try
            {
                return new DownloadResult(legacy.DownloadFile(targetServer, change.Item.Path, changesetId, change.Item.DeletionId), true);
            }
            catch (Exception exception)
            {
                throw new GitTfsException(failure + "; the legacy TFVC download also failed.", exception);
            }
        }

        private static IEnumerable<(string VersionType, string Option)> Versions(TfvcChange change)
        {
            yield return ("Changeset", null);
            yield return ("Changeset", "Previous");
            if (change.HasMergeSource)
                yield return ("MergeSource", "UseRename");
        }

        public readonly record struct DownloadResult(byte[] Content, bool LegacyFallbackUsed);
    }
}
