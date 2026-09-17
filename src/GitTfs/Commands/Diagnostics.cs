
namespace GitTfs.Commands
{
    using global::GitTfs.Util;
    using global::System.Diagnostics;
    [Pluggable("diagnostics")]
    public class Diagnostics : GitTfsCommand
    {
        private readonly ServiceCatalog catalogField;

        public Diagnostics(ServiceCatalog catalog)
        {
            catalogField = catalog;
        }

        public OptionSet OptionSet => new OptionSet();

        public int Run()
        {
            Trace.TraceInformation(catalogField.Describe());
            return GitTfsExitCodes.OK;
        }
    }
}
