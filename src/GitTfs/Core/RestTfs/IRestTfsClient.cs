namespace GitTfs.Core.RestTfs
{
    using global::System;
    using global::System.Collections.Generic;

    public interface IRestTfsClient : IDisposable
    {
        IReadOnlyList<RestChangesetReference> GetChangesets(string repositoryPath, int fromChangesetId,
            int batchSize, bool filterByItemPath = true);

        RestChangeset GetChangeset(int changesetId);

        IReadOnlyList<RestItem> GetItems(string repositoryPath, int changesetId);

        byte[] DownloadFile(string path, int changesetId, string versionType = "Changeset",
            string versionOption = null);
    }
}
