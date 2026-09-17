
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("pull")]
    [Description("pull [options]")]
    [RequiresValidGitRepository]
    public class Pull : GitTfsCommand
    {
        private readonly Fetch fetchField;
        private readonly Globals globalsField;
        private bool shouldRebaseField;

        public OptionSet OptionSet => fetchField.OptionSet
                            .Add("r|rebase", "Rebase your modifications on tfs changes", v => shouldRebaseField = v != null);

        public Pull(Globals globals, Fetch fetch)
        {
            fetchField = fetch;
            globalsField = globals;
        }

        public int Run() => Run(globalsField.RemoteId);

        public int Run(string remoteId)
        {
            var retVal = fetchField.Run(remoteId);

            if (retVal == 0)
            {
                var remote = globalsField.Repository.ReadTfsRemote(remoteId);
                if (shouldRebaseField)
                {
                    globalsField.WarnOnGitVersion();

                    if (globalsField.Repository.WorkingCopyHasUnstagedOrUncommitedChanges)
                    {
                        throw new GitTfsException("error: You have local changes; rebase-workflow only possible with clean working directory.")
                            .WithRecommendation("Try 'git stash' to stash your local changes and pull again.");
                    }
                    globalsField.Repository.CommandNoisy("rebase", "--rebase-merges", remote.RemoteRef);
                }
                else
                    globalsField.Repository.Merge(remote.RemoteRef);
            }

            return retVal;
        }
    }
}
