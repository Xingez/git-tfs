
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core.TfsInterop;
    using global::System.Diagnostics;
    [Pluggable("create")]
    [Description(@"create [options] tfs-url-or-instance-name project-name -t=trunk-name <git-repository-path>
ex : git tfs create http://myTfsServer:8080/tfs/TfsRepository myProjectName
     git tfs create http://myTfsServer:8080/tfs/TfsRepository myProjectName -t=myTrunkName
")]
    public class Create : GitTfsCommand
    {
        private readonly Clone cloneField;
        private readonly ITfsHelper tfsHelperField;
        private readonly RemoteOptions remoteOptionsField;
        private string trunkNameField = "trunk";
        private bool createTeamProjectFolderField;

        public Create(ITfsHelper tfsHelper, Clone clone, RemoteOptions remoteOptions)
        {
            tfsHelperField = tfsHelper;
            cloneField = clone;
            remoteOptionsField = remoteOptions;
        }

        public OptionSet OptionSet => new OptionSet
                    {
                        {"c|create-project-folder", "Create also the team project folder if it doesn't exist!", v => createTeamProjectFolderField = v != null},
                        {"t|trunk-name=", "Name of the main branch that will be created on TFS (default: \"trunk\")", v => trunkNameField = v},
                    }.Merge(cloneField.OptionSet);

        public int Run(string tfsUrl, string projectName) => Run(tfsUrl, projectName, Path.GetFileName(projectName));

        public int Run(string tfsUrl, string projectName, string gitRepositoryPath)
        {
            tfsHelperField.Url = tfsUrl;
            tfsHelperField.Username = remoteOptionsField.Username;
            tfsHelperField.Password = remoteOptionsField.Password;

            var absoluteGitRepositoryPath = Path.GetFullPath(gitRepositoryPath);
            Trace.TraceInformation("Creating project folder...");
            tfsHelperField.CreateTfsRootBranch(projectName, trunkNameField, absoluteGitRepositoryPath, createTeamProjectFolderField);
            Trace.TraceInformation("Cloning new project...");
            cloneField.Run(tfsUrl, "$/" + projectName + "/" + trunkNameField, gitRepositoryPath);

            return GitTfsExitCodes.OK;
        }
    }
}
