namespace GitTfs.Core.RestTfs
{
    using LibGit2Sharp;

    public sealed record RestTfsChangesetImportResult
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

        public bool Skipped { get; init; }
        public int ChangesetId { get; init; }
        public Commit Commit { get; init; }
        public int FilesProcessed { get; init; }
        public int FilesDownloaded { get; init; }
        public int FilesReused { get; init; }
        public int FilesDeleted { get; init; }
        public bool LegacyFallbackUsed { get; init; }
    }
}
