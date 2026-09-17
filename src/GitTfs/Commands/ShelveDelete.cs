
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("shelve-delete")]
    [Description("shelve-delete shelveset-name")]
    [RequiresValidGitRepository]
    public class ShelveDelete : GitTfsCommand
    {
        private readonly Globals globalsField;

        public ShelveDelete(Globals globals)
        {
            globalsField = globals;
            OptionSet = new OptionSet();
        }

        public OptionSet OptionSet { get; private set; }

        public int Run(string shelvesetName)
        {
            if (string.IsNullOrEmpty(shelvesetName))
            {
                Trace.TraceError("error: no shelveset name specified...");
                return GitTfsExitCodes.InvalidArguments;
            }

            var remote = globalsField.Repository.ReadTfsRemote(globalsField.RemoteId);
            if (!remote.HasShelveset(shelvesetName))
            {
                Trace.TraceInformation("error: could not find shelveset \"{0}\"...", shelvesetName);
                return GitTfsExitCodes.InvalidArguments;
            }

            remote.DeleteShelveset(shelvesetName);
            Trace.TraceInformation("Shelveset \"{0}\" deleted.", shelvesetName);
            return GitTfsExitCodes.OK;
        }
    }
}
