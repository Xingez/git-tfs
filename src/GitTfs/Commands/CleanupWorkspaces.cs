
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::System.Diagnostics;
    [Pluggable("cleanup-workspaces")]
    [Description("cleanup-workspaces [tfs-remote-id]...")]
    [RequiresValidGitRepository]
    public class CleanupWorkspaces : GitTfsCommand
    {
        private readonly Globals globalsField;
        private readonly CleanupOptions cleanupOptionsField;

        public CleanupWorkspaces(Globals globals, CleanupOptions cleanupOptions)
        {
            globalsField = globals;
            cleanupOptionsField = cleanupOptions;
        }

        public OptionSet OptionSet => cleanupOptionsField.OptionSet;

        public int Run()
        {
            cleanupOptionsField.Init();
            foreach (var remote in globalsField.Repository.ReadAllTfsRemotes())
            {
                Cleanup(remote);
            }
            return GitTfsExitCodes.OK;
        }

        public int Run(IList<string> remoteIds)
        {
            cleanupOptionsField.Init();
            foreach (var remoteId in remoteIds)
            {
                var remote = globalsField.Repository.ReadTfsRemote(remoteId);
                Cleanup(remote);
            }
            return GitTfsExitCodes.OK;
        }

        private void Cleanup(IGitTfsRemote remote)
        {
            Trace.TraceInformation("Cleaning up workspaces for TFS remote " + remote.Id);
            remote.CleanupWorkspace();
        }
    }
}
