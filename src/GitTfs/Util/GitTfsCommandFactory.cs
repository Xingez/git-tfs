namespace GitTfs.Util
{
    [SingletonService]
    public class GitTfsCommandFactory
    {
        private readonly IServiceProvider _services;
        private readonly ServiceCatalog _catalog;

        public GitTfsCommandFactory(IServiceProvider services, ServiceCatalog catalog)
        {
            _services = services;
            _catalog = catalog;
        }

        private Dictionary<string, string> _aliasMap;
        public Dictionary<string, string> AliasMap => _aliasMap ?? (_aliasMap = CreateAliasMap());

        private Dictionary<string, string> CreateAliasMap()
        {
            var aliasMap = new Dictionary<string, string>();
            foreach (var instance in _catalog.Commands)
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
            var commandType = _catalog.GetCommandType(GetCommandName(name));
            return commandType == null ? null : (GitTfsCommand)_services.GetRequiredService(commandType);
        }

        private string GetCommandName(string name)
        {
            string commandName;
            return AliasMap.TryGetValue(name, out commandName) ? commandName : name;
        }

        public IEnumerable<string> GetAliasesForCommandName(string name) => AliasMap.Where(p => p.Value == name).Select(p => p.Key);
    }
}
