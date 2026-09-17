using GitTfs.Util;
using System.Diagnostics;

namespace GitTfs.Commands
{
    [Pluggable("diagnostics")]
    public class Diagnostics : GitTfsCommand
    {
        private readonly ServiceCatalog _catalog;

        public Diagnostics(ServiceCatalog catalog)
        {
            _catalog = catalog;
        }

        public OptionSet OptionSet => new OptionSet();

        public int Run()
        {
            Trace.TraceInformation(_catalog.Describe());
            return GitTfsExitCodes.OK;
        }
    }
}
