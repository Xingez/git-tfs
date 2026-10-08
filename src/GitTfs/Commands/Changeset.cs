namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::System.Globalization;
    using global::GitTfs;
    using global::GitTfs.Core;
    using global::GitTfs.Core.RestTfs;
    using global::GitTfs.Util;

    [Pluggable("changeset")]
    [Description("<tfs-subfolder> <output-path> <changeset-id>\n  Import exactly one changeset into an existing git-tfs clone. HEAD must be the state immediately before the requested changeset.\n  ex : git tfs changeset $/ProjectName/ProjectBranch C:\\repo 12345\n")]
    public sealed class Changeset : GitTfsCommand
    {
        private readonly GitTfsSettings settingsField;
        private readonly Globals globalsField;
        private readonly IRestTfsCloneService cloneServiceField;
        private bool noFallbackField;

        public Changeset(GitTfsSettings settings, Globals globals, IRestTfsCloneService cloneService)
        {
            settingsField = settings;
            globalsField = globals;
            cloneServiceField = cloneService;
        }

        public OptionSet OptionSet => new OptionSet()
            .Add("no-fallback", "stop when REST cannot download a file; do not use the legacy TFVC helper",
                value => noFallbackField = value != null);

        public int Run(string tfsRepositoryPath, string gitRepositoryPath, string changesetId)
        {
            if (!int.TryParse(changesetId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedChangesetId))
                throw new GitTfsException("The changeset ID must be a positive integer.");

            return globalsField.DebugOutput
                ? cloneServiceField.RunChangeset(settingsField.TargetServer, tfsRepositoryPath,
                    gitRepositoryPath, parsedChangesetId, noFallbackField)
                : SpectreCloneProgress.Run(progressReporter => cloneServiceField.RunChangeset(
                    settingsField.TargetServer, tfsRepositoryPath, gitRepositoryPath, parsedChangesetId,
                    noFallbackField, progressReporter));
        }
    }
}
