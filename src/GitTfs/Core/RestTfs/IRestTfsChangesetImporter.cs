namespace GitTfs.Core.RestTfs
{
    using global::System.Collections.Generic;
    using global::LibGit2Sharp;

    public interface IRestTfsChangesetImporter
    {
        RestTfsChangesetImportResult Import(IRestTfsClient client, Repository repository,
            RestChangesetReference changesetReference, string targetServer, string repositoryPath,
            string outputPath, IDictionary<string, string> pathMap, Commit parent, bool noFallback);
    }
}
