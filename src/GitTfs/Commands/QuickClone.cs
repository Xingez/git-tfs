
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("quick-clone")]
    [Description("quick-clone [options] tfs-url-or-instance-name repository-path <git-repository-path>")]
    public class QuickClone : Clone
    {
        public QuickClone(Globals globals, Init init, QuickFetch fetch, GitTfsSettings settings)
            : base(globals, fetch, init, null, settings)
        {
        }
    }
}
