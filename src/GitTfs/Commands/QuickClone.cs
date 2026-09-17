
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::GitTfs.Core.RestTfs;
    [Pluggable("quick-clone")]
    [Description("quick-clone [options] tfs-url-or-instance-name repository-path <git-repository-path>")]
    public class QuickClone : Clone
    {
        public QuickClone(Globals globals, Init init, QuickFetch fetch, GitTfsSettings settings,
            ConfigProperties properties, RemoteOptions remoteOptions, RestTfsCloneService restCloneService)
            : base(globals, fetch, init, null, settings, properties, remoteOptions, restCloneService)
        {
        }
    }
}
