namespace GitTfs.Core.TfsInterop
{
    public interface IWorkspace
    {
        void GetSpecificVersion(int changeset);
        void GetSpecificVersion(int changeset, IEnumerable<IItem> items, bool noParallel);
        void GetSpecificVersion(IChangeset changeset, bool noParallel);
        void GetSpecificVersion(int changeset, IEnumerable<IChange> changes, bool noParallel);
    }
}
