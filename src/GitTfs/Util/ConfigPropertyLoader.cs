namespace GitTfs.Util
{
    // Manages configurable values.
    [SingletonService]
    public class ConfigPropertyLoader
    {
        private readonly Globals globalsField;
        private readonly Dictionary<string, object> overridesField = new Dictionary<string, object>();

        public ConfigPropertyLoader(Globals globals)
        {
            globalsField = globals;
        }

        // Sets a value for the duration of this run of git-tfs.
        public void Override<T>(string key, T value) => overridesField[key] = value;

        // Gets the value to use. Order of precedence:
        //
        // * temporary value, set with Override<T>().
        // * configured value, retrieved via git. This value is set external to any invocation of git-tfs.
        // * a default value, provided in the call to Get().
        public T Get<T>(string key, T defaultValue)
        {
            if (overridesField.ContainsKey(key))
                return (T)overridesField[key];

            return globalsField.Repository.GetConfig<T>(key, defaultValue);
        }

        public void PersistAllOverrides()
        {
            foreach (var key in overridesField.Keys)
            {
                globalsField.Repository.SetConfig(key, overridesField[key].ToString());
            }
        }
    }
}
