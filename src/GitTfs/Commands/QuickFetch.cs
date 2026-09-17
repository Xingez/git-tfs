
namespace GitTfs.Commands
{
    using global::GitTfs.Core;
    // This isn't intended to ever be a command. The intent is that
    // It is used internally for fast initial imports and is not a command.
    //
    // This cannot be a command until the following are sorted out:
    //  1. How to choose a parent commit.
    //  2. Load the correct set of extant casing.
    public class QuickFetch : Fetch
    {
        private readonly ConfigProperties propertiesField;
        public QuickFetch(Globals globals, ConfigProperties properties, RemoteOptions remoteOptions)
            : base(globals, properties, remoteOptions, null)
        {
            propertiesField = properties;
        }

        protected override void DoFetch(IGitTfsRemote remote, bool stopOnFailMergeCommit)
        {
            if (InitialChangeset.HasValue)
                remote.QuickFetch(InitialChangeset.Value, false, false);
            else
                remote.QuickFetch(-1, false, false);
            propertiesField.InitialChangeset = remote.MaxChangesetId;
            propertiesField.PersistAllOverrides();
        }
    }
}
