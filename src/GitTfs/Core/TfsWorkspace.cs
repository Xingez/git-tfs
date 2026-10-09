namespace GitTfs.Core
{
    using GitTfs.Core.TfsInterop;

    public sealed class TfsWorkspace : ITfsWorkspace
    {
        private readonly IWorkspace workspaceField;
        private readonly string localDirectoryField;

        public IGitTfsRemote Remote { get; }

        public TfsWorkspace(IWorkspace workspace, string localDirectory, IGitTfsRemote remote)
        {
            workspaceField = workspace;
            localDirectoryField = remote.Repository.IsBare ? Path.GetFullPath(localDirectory) : localDirectory;
            Remote = remote;
        }

        public string GetLocalPath(string path) => Path.Combine(localDirectoryField, path);

        public void Get(int changesetId) => workspaceField.GetSpecificVersion(changesetId);

        public void Get(int changesetId, IEnumerable<IItem> items) => workspaceField.GetSpecificVersion(changesetId, items, noParallel: true);

        public void Get(IChangeset changeset) => workspaceField.GetSpecificVersion(changeset, noParallel: true);

        public void Get(int changesetId, IEnumerable<IChange> changes)
        {
            if (changes.Any())
                workspaceField.GetSpecificVersion(changesetId, changes, noParallel: true);
        }
    }
}
