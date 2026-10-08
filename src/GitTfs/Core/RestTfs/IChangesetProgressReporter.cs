namespace GitTfs.Core.RestTfs
{
    public interface IChangesetProgressReporter
    {
        void StartChangeset(int changesetId, int totalFiles);

        void ReportFiles(int changesetId, int processedFiles, int totalFiles);

        void CompleteChangeset(int changesetId, string commitSha);
    }
}
