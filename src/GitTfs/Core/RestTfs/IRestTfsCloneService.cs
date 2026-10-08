namespace GitTfs.Core.RestTfs
{
    public interface IRestTfsCloneService
    {
        int Run(string targetServer, string repositoryPath, string outputPath, bool noFallback = false,
            string targetCloneUrl = null, string targetBranch = "main");

        int RunChangeset(string targetServer, string repositoryPath, string outputPath,
            int changesetId, bool noFallback = false);
    }
}
