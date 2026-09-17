
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::System.Diagnostics;
    [Pluggable("version")]
    [Description("version")]
    public class Version : GitTfsCommand
    {
        private readonly IGitTfsVersionProvider versionProviderField;

        /// <summary>
        /// Initializes a new instance of the Version class.
        /// </summary>
        /// <param name="globals"></param>
        /// <param name="versionProvider"></param>
        public Version(Globals globals, IGitTfsVersionProvider versionProvider)
        {
            versionProviderField = versionProvider;
            OptionSet = globals.OptionSet;
        }

        public int Run()
        {
            Trace.TraceInformation(versionProviderField.GetVersionString());
            Trace.TraceInformation(versionProviderField.GetPathToGitTfsExecutable());

            Trace.TraceInformation(GitTfsConstants.MessageForceVersion);

            return GitTfsExitCodes.OK;
        }

        public OptionSet OptionSet { get; private set; }
    }
}
