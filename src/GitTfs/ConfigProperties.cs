
namespace GitTfs
{
    using global::GitTfs.Util;
    // Like Globals, but for values that can be set in the git config
    // or overridden by some other means, like from the command line.
    public class ConfigProperties
    {
        private readonly ConfigPropertyLoader loaderField;

        public ConfigProperties(ConfigPropertyLoader loader)
        {
            loaderField = loader;
        }

        public void PersistAllOverrides() => loaderField.PersistAllOverrides();

        public int BatchSize
        {
            set => loaderField.Override(GitTfsConstants.BatchSize, value);
            get => loaderField.Get(GitTfsConstants.BatchSize, 100);
        }

        public int? InitialChangeset
        {
            set => loaderField.Override(GitTfsConstants.InitialChangeset, value ?? -1);
            get
            {
                int? initialChangeset = loaderField.Get(GitTfsConstants.InitialChangeset, -1);
                return initialChangeset == -1 ? null : initialChangeset;
            }
        }
    }
}
