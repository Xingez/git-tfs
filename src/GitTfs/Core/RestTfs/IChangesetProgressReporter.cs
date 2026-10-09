namespace GitTfs.Core.RestTfs
{
    public interface IChangesetProgressReporter
    {
        void ReportScan(int page, int found, int cursor, RestChangesetReference latest = null) { }

        void CompleteScan(int found) { }

        void ReportResume(IReadOnlyList<int> completedChangesets) { }

        void DescribeChangeset(int changesetId, string comment) { }

        void ReportActivity(string description) { }

        void StartChangeset(int changesetId, int totalFiles);

        void ReportFiles(int changesetId, int processedFiles, int totalFiles);

        void ReportDownloadedFile(string relativePath) { }

        void CompleteChangeset(int changesetId, string commitSha);

        void SkipChangeset(int changesetId) { }
    }
}
