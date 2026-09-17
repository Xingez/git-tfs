
namespace GitTfs.Core
{
    using global::GitTfs.Commands; // ToGitRefName() and RemoteOptions
    using global::System.Diagnostics;
    public class Bootstrapper
    {
        private readonly Globals globalsField;
        private readonly RemoteOptions remoteOptionsField;

        public Bootstrapper(Globals globals, RemoteOptions remoteOptions)
        {
            globalsField = globals;
            remoteOptionsField = remoteOptions;
        }

        public IGitTfsRemote CreateRemote(TfsChangesetInfo changeset)
        {
            IGitTfsRemote remote;
            if (changeset.Remote.IsDerived)
            {
                var remoteId = GetRemoteId(changeset);
                remote = globalsField.Repository.CreateTfsRemote(new RemoteInfo
                {
                    Id = remoteId,
                    Url = changeset.Remote.TfsUrl,
                    Repository = changeset.Remote.TfsRepositoryPath,
                    RemoteOptions = remoteOptionsField,
                });
                remote.UpdateTfsHead(changeset.GitCommit, changeset.ChangesetId);
                Trace.TraceInformation("-> new remote '" + remote.Id + "'");
            }
            else
            {
                remote = changeset.Remote;
                if (changeset.Remote.MaxChangesetId < changeset.ChangesetId)
                {
                    int oldChangeset = changeset.Remote.MaxChangesetId;
                    globalsField.Repository.MoveTfsRefForwardIfNeeded(changeset.Remote);
                    Trace.TraceInformation("-> existing remote {0} (updated from changeset {1})", changeset.Remote.Id, oldChangeset);
                }
                else
                {
                    Trace.TraceInformation("-> existing remote {0} (up to date)", changeset.Remote.Id);
                }
            }
            return remote;
        }


        private string GetRemoteId(TfsChangesetInfo changeset)
        {
            if (IsAvailable(GitTfsConstants.DefaultRepositoryId))
            {
                Trace.TraceInformation("info: '" + changeset.Remote.TfsRepositoryPath + "' will be bootstraped as your main remote...");
                return GitTfsConstants.DefaultRepositoryId;
            }

            //Remove '$/'!
            var expectedRemoteId = changeset.Remote.TfsRepositoryPath.Substring(2).Trim('/');
            var indexOfSlash = expectedRemoteId.IndexOf('/');
            if (indexOfSlash != 0)
                expectedRemoteId = expectedRemoteId.Substring(indexOfSlash + 1);
            var remoteId = expectedRemoteId.ToGitRefName();
            var suffix = 0;
            while (!IsAvailable(remoteId))
                remoteId = expectedRemoteId + "-" + (suffix++);
            return remoteId;
        }

        private bool IsAvailable(string remoteName) => !globalsField.Repository.HasRemote(remoteName);
    }
}
