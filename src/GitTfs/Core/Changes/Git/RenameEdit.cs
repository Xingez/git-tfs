
namespace GitTfs.Core.Changes.Git
{
    public class RenameEdit : IGitChangedFile
    {
        public string Path { get; private set; }
        public string PathTo { get; private set; }
        public string NewSha { get; private set; }
        public string Score { get; private set; }
        public IGitRepository Repository { get; private set; }

        public RenameEdit(IGitRepository repository, GitChangeInfo changeInfo)
        {
            Repository = repository;
            NewSha = changeInfo.newSha;
            Path = changeInfo.path;
            PathTo = changeInfo.pathTo;
            Score = changeInfo.score;
        }

        public void Apply(ITfsWorkspaceModifier workspace)
        {
            workspace.Edit(Path);
            workspace.Rename(Path, PathTo, Score);
            var workspaceFile = workspace.GetLocalPath(PathTo);
            Repository.CopyBlob(NewSha, workspaceFile);
        }
    }
}