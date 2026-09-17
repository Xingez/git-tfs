using System.ComponentModel;
using StructureMap;
using GitTfs.Core;

namespace GitTfs.Commands
{
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
