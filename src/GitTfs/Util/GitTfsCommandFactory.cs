namespace GitTfs.Util
{
    [SingletonService]
    public class GitTfsCommandFactory
    {
        private readonly IServiceProvider servicesField;
        private readonly ServiceCatalog catalogField;

        public GitTfsCommandFactory(IServiceProvider services, ServiceCatalog catalog)
        {
            servicesField = services;
            catalogField = catalog;
        }

        private Dictionary<string, string> aliasMapField;
        public Dictionary<string, string> AliasMap => aliasMapField ?? (aliasMapField = CreateAliasMap());

        private Dictionary<string, string> CreateAliasMap()
        {
            var aliasMap = new Dictionary<string, string>();
            foreach (var instance in catalogField.Commands)
            {
                var attribute = instance.ImplementationType.GetCustomAttributes(typeof(PluggableWithAliases), true)
                    .Cast<PluggableWithAliases>().FirstOrDefault();

                if (attribute != null)
                {
                    foreach (var alias in attribute.Aliases)
                    {
                        aliasMap[alias] = instance.Name;
                    }
                }
            }

            return aliasMap;
        }

        public GitTfsCommand GetCommand(string name)
        {
            var commandType = catalogField.GetCommandType(GetCommandName(name));
            return commandType == null ? null : (GitTfsCommand)servicesField.GetRequiredService(commandType);
        }

        private string GetCommandName(string name)
        {
            string commandName;
            return AliasMap.TryGetValue(name, out commandName) ? commandName : name;
        }

        public IEnumerable<string> GetAliasesForCommandName(string name) => AliasMap.Where(p => p.Value == name).Select(p => p.Key);
    }
}
