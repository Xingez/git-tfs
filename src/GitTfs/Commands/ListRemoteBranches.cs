
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::GitTfs.Core.TfsInterop;
    using global::System.Diagnostics;
    [Pluggable("list-remote-branches")]
    [Description("list-remote-branches tfs-url-or-instance-name \n       git tfs list-remote-branches http://myTfsServer:8080/tfs/TfsRepository\n")]
    public class ListRemoteBranches : GitTfsCommand
    {
        private readonly ITfsHelper tfsHelperField;
        private readonly RemoteOptions remoteOptionsField;

        public ListRemoteBranches(ITfsHelper tfsHelper, RemoteOptions remoteOptions)
        {
            tfsHelperField = tfsHelper;
            remoteOptionsField = remoteOptions;
        }

        public OptionSet OptionSet => remoteOptionsField.OptionSet;

        public int Run(string tfsUrl)
        {
            tfsHelperField.Url = tfsUrl;
            tfsHelperField.Username = remoteOptionsField.Username;
            tfsHelperField.Password = remoteOptionsField.Password;
            tfsHelperField.EnsureAuthenticated();

            string convertBranchMessage = "  -> Open 'Source Control Explorer' and for each folder corresponding to a branch, right click on the folder and select 'Branching and Merging' > 'Convert to branch'.";
            var branches = tfsHelperField.GetBranches().Where(b => b.IsRoot).ToList();
            if (branches.IsEmpty())
            {
                Trace.TraceWarning("No TFS branches were found!");
                Trace.TraceWarning("\n\nPerhaps you should convert your branch folders into a branch in TFS:");
                Trace.TraceWarning(convertBranchMessage);
            }
            else
            {
                Trace.TraceInformation("TFS branches that could be cloned:");
                foreach (var branchObject in branches.Where(b => b.IsRoot))
                {
                    Branch.WriteRemoteTfsBranchStructure(tfsHelperField, branchObject.Path);
                }
                Trace.TraceInformation("\nCloning root branches (marked by [*]) is recommended!");
                Trace.TraceInformation("\n\nPS:if your branch is not listed here, perhaps you should convert its containing folder into a branch in TFS:");
                Trace.TraceInformation(convertBranchMessage);
            }
            return GitTfsExitCodes.OK;
        }
    }
}
