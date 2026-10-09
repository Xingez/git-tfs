namespace GitTfs.Core
{
    using GitTfs.Core.TfsInterop;

    public interface ITfsWorkspace
    {
        void Get(int changesetId);
        void Get(int changesetId, IEnumerable<IItem> items);
        void Get(IChangeset changeset);
        void Get(int changesetId, IEnumerable<IChange> changes);
        string GetLocalPath(string path);
        IGitTfsRemote Remote { get; }
    }
}
