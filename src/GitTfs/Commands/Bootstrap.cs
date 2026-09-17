
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::System.Diagnostics;
    [Pluggable("bootstrap")]
    [RequiresValidGitRepository]
    [Description("bootstrap [parent-commit]\n" +
        " info: if none of your tfs remote exists, always checkout and bootstrap your main remote first.\n")]
    public class Bootstrap : GitTfsCommand
    {
        private readonly RemoteOptions remoteOptionsField;
        private readonly Globals globalsField;
        private readonly Bootstrapper bootstrapperField;

        public Bootstrap(RemoteOptions remoteOptions, Globals globals, Bootstrapper bootstrapper)
        {
            remoteOptionsField = remoteOptions;
            globalsField = globals;
            bootstrapperField = bootstrapper;
        }

        public OptionSet OptionSet => remoteOptionsField.OptionSet;

        public int Run() => Run("HEAD");

        public int Run(string commitish)
        {
            var tfsParents = globalsField.Repository.GetLastParentTfsCommits(commitish);
            foreach (var parent in tfsParents)
            {
                GitCommit commit = globalsField.Repository.GetCommit(parent.GitCommit);
                Trace.TraceInformation("commit {0}\nAuthor: {1} <{2}>\nDate:   {3}\n\n    {4}",
                    commit.Sha,
                    commit.AuthorAndEmail.Item1, commit.AuthorAndEmail.Item2,
                    commit.When.ToString("ddd MMM d HH:mm:ss zzz"),
                    commit.Message.Replace("\n", "\n    ").TrimEnd(' '));
                bootstrapperField.CreateRemote(parent);
                Trace.TraceInformation(string.Empty);
            }
            return GitTfsExitCodes.OK;
        }
    }
}
