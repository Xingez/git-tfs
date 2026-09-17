namespace GitTfs.Core.Changes.Git
{
    public class Modify : IGitChangedFile
    {
        public string Path { get; private set; }
        public string NewSha { get; private set; }
        public IGitRepository Repository { get; private set; }

        public Modify(IGitRepository repository, GitChangeInfo changeInfo)
        {
            Repository = repository;
            NewSha = changeInfo.newSha;
            Path = changeInfo.path;
        }

        public void Apply(ITfsWorkspaceModifier workspace)
        {
            workspace.Edit(Path);
            var workspaceFile = workspace.GetLocalPath(Path);
            Repository.CopyBlob(NewSha, workspaceFile);
        }
    }
}
