
namespace GitTfs.Commands
{
    using global::GitTfs.Util;
    [SingletonService]
    public class CleanupOptions
    {
        private readonly Globals globalsField;

        public CleanupOptions(Globals globals)
        {
            globalsField = globals;
        }

        public OptionSet OptionSet => new OptionSet
                {
                    { "v|verbose", v => IsVerbose = v != null },
                };

        private bool IsVerbose { get; set; }

        public void Init()
        {
            if (IsVerbose)
                globalsField.DebugOutput = true;
        }
    }
}
