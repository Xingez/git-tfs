namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs;
    using global::GitTfs.Core;
    using global::GitTfs.Core.RestTfs;
    using global::GitTfs.Util;

    [Description("[options] <tfs-subfolder> <output-path> [target-git-url] [target-branch]\n  Clone is the default workflow. The target server and clone defaults are read from appsettings.json.\n  target-branch defaults to main.\n  ex : git tfs $/ProjectName/ProjectBranch .\n")]
    public class Clone : GitTfsCommand
    {
        private const string DefaultTargetBranch = "main";
        private readonly GitTfsSettings settingsField;
        private readonly Globals globalsField;
        private readonly IRestTfsCloneService restCloneServiceField;
        private bool noFallbackField;

        public Clone(GitTfsSettings settings, Globals globals, IRestTfsCloneService restCloneService)
        {
            settingsField = settings;
            globalsField = globals;
            restCloneServiceField = restCloneService;
        }

        public OptionSet OptionSet => new OptionSet()
            .Add("no-fallback", "stop when REST cannot download a file; do not use the legacy TFVC helper",
                value => noFallbackField = value != null);

        public int Run(string tfsRepositoryPath, string gitRepositoryPath)
            => RunConfiguredClone(tfsRepositoryPath, gitRepositoryPath, null, DefaultTargetBranch);

        public int Run(string tfsRepositoryPath, string gitRepositoryPath, string targetCloneUrl)
            => RunConfiguredClone(tfsRepositoryPath, gitRepositoryPath, targetCloneUrl, DefaultTargetBranch);

        public int Run(string tfsRepositoryPath, string gitRepositoryPath, string targetCloneUrl, string targetBranch)
            => RunConfiguredClone(tfsRepositoryPath, gitRepositoryPath, targetCloneUrl, targetBranch);

        private int RunConfiguredClone(string tfsRepositoryPath, string gitRepositoryPath,
            string targetCloneUrl, string targetBranch)
        {
            if (string.IsNullOrWhiteSpace(tfsRepositoryPath)
                || string.Equals(tfsRepositoryPath, GitTfsConstants.TfsRoot, StringComparison.OrdinalIgnoreCase))
                throw new GitTfsException("Clone requires a TFS subfolder, not the TFS root.");

            if (string.IsNullOrWhiteSpace(gitRepositoryPath))
                throw new GitTfsException("Clone requires an output path.");

            tfsRepositoryPath.AssertValidTfsPath();

            if (string.IsNullOrWhiteSpace(settingsField.TargetServer))
            {
                var source = string.IsNullOrWhiteSpace(settingsField.SourcePath)
                    ? "appsettings.json"
                    : settingsField.SourcePath;
                throw new GitTfsException("TargetServer is not configured in " + source
                    + ". Set it before using 'git tfs <tfs-subfolder> <output-path>'.");
            }

            var result = globalsField.DebugOutput
                ? restCloneServiceField.Run(settingsField.TargetServer, tfsRepositoryPath,
                    gitRepositoryPath, noFallbackField, targetCloneUrl, targetBranch)
                : SpectreCloneProgress.Run(progressReporter => restCloneServiceField.Run(
                    settingsField.TargetServer, tfsRepositoryPath, gitRepositoryPath, noFallbackField,
                    targetCloneUrl, targetBranch, progressReporter));
            Environment.CurrentDirectory = Path.GetFullPath(gitRepositoryPath);
            return result;
        }

        public static string HideUserCredentials(string commandLineRun)
        {
            var usernameRegex = new System.Text.RegularExpressions.Regex("((--|/)username|[/-]u)(=| +)[^ ]+");
            commandLineRun = usernameRegex.Replace(commandLineRun, "--username=xxx");
            var passwordRegex = new System.Text.RegularExpressions.Regex("((--|/)password|[/-]p)(=| +)[^ ]+");
            return passwordRegex.Replace(commandLineRun, "--password=xxx");
        }
    }
}
