
namespace GitTfs.Commands
{
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("cleanup")]
    [RequiresValidGitRepository]
    public class Cleanup : GitTfsCommand
    {
        private readonly CleanupWorkspaces cleanupWorkspacesField;
        private readonly CleanupWorkspaceLocal cleanupWorkspaceLocalField;

        public Cleanup(CleanupWorkspaces cleanupWorkspaces, CleanupWorkspaceLocal cleanupWorkspaceLocal)
        {
            cleanupWorkspacesField = cleanupWorkspaces;
            cleanupWorkspaceLocalField = cleanupWorkspaceLocal;
        }

        public OptionSet OptionSet => cleanupWorkspacesField.OptionSet;

        public int Run() => RunAll(cleanupWorkspacesField.Run, cleanupWorkspaceLocalField.Run);

        private int RunAll(params Func<int>[] cleaners)
        {
            foreach (var cleaner in cleaners)
            {
                var result = cleaner();
                if (result != GitTfsExitCodes.OK)
                    return result;
            }
            return GitTfsExitCodes.OK;
        }
    }
}
