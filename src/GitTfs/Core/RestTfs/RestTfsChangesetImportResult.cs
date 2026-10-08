namespace GitTfs.Core.RestTfs
{
    using global::LibGit2Sharp;

    public sealed class RestTfsChangesetImportResult
    {
        public RestTfsChangesetImportResult(bool skipped, int changesetId, Commit commit,
            int filesProcessed, int filesDownloaded, int filesReused, int filesDeleted,
            bool legacyFallbackUsed)
        {
            Skipped = skipped;
            ChangesetId = changesetId;
            Commit = commit;
            FilesProcessed = filesProcessed;
            FilesDownloaded = filesDownloaded;
            FilesReused = filesReused;
            FilesDeleted = filesDeleted;
            LegacyFallbackUsed = legacyFallbackUsed;
        }

        public bool Skipped { get; }
        public int ChangesetId { get; }
        public Commit Commit { get; }
        public int FilesProcessed { get; }
        public int FilesDownloaded { get; }
        public int FilesReused { get; }
        public int FilesDeleted { get; }
        public bool LegacyFallbackUsed { get; }
    }
}
