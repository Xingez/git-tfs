
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("info")]
    [Description("info")]
    [RequiresValidGitRepository]
    public class Info : GitTfsCommand
    {
        private readonly Globals globalsField;
        private readonly IGitTfsVersionProvider versionProviderField;

        public Info(Globals globals, IGitTfsVersionProvider versionProvider)
        {
            globalsField = globals;
            versionProviderField = versionProvider;
        }

        public OptionSet OptionSet => globalsField.OptionSet;

        public int Run()
        {
            DescribeGit();

            DescribeGitTfs();

            var tfsRemotes = globalsField.Repository.ReadAllTfsRemotes();
            foreach (var remote in tfsRemotes)
            {
                DescribeTfsRemotes(remote);
            }

            return GitTfsExitCodes.OK;
        }

        private void DescribeGit()
        {
            DisplayReadabilityLineJump();

            Trace.TraceInformation(globalsField.GitVersion);
        }

        private void DescribeGitTfs()
        {
            DisplayReadabilityLineJump();
            Trace.TraceInformation(versionProviderField.GetVersionString());
            Trace.TraceInformation(" " + versionProviderField.GetPathToGitTfsExecutable());

            Trace.TraceInformation(GitTfsConstants.MessageForceVersion);

            DescribeGitRepository();
        }

        private void DescribeGitRepository()
        {
            try
            {
                var repoDescription = File.ReadAllLines(Path.Combine(globalsField.GitDir, "description"));
                if (repoDescription.Length == 0 || !repoDescription[0].StartsWith("$/"))
                    return;

                DisplayReadabilityLineJump();

                Trace.TraceInformation("cloned from tfs path:" + string.Join(Environment.NewLine, repoDescription));
            }
            catch (Exception)
            {
                Trace.WriteLine("warning: unable to read the repository description!");
            }
        }

        private void DescribeTfsRemotes(IGitTfsRemote remote)
        {
            DisplayReadabilityLineJump();
            Trace.TraceInformation("remote tfs id: '{0}' {1} {2}", remote.Id, remote.TfsUrl, remote.TfsRepositoryPath);
            Trace.TraceInformation("               {0} - {1} @ {2}", remote.RemoteRef, remote.MaxCommitHash, remote.MaxChangesetId);
        }

        private void DisplayReadabilityLineJump() => Trace.TraceInformation(string.Empty);
    }
}
