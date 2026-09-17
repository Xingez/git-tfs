
namespace GitTfs.Util
{
    using global::System.Reflection;
    using global::Microsoft.Extensions.DependencyInjection;
    /// <summary>
    /// Contains the application-specific metadata needed for command and changed-file resolution.
    /// </summary>
    public sealed class ServiceCatalog
    {
        private readonly Dictionary<string, Type> commandsField = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Type> changedFilesField = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> allowedCommandsField;

        public ServiceCatalog(IEnumerable<string> allowedCommands = null)
        {
            if (allowedCommands != null)
                allowedCommandsField = new HashSet<string>(allowedCommands, StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<ServiceRegistration> Commands =>
            commandsField.OrderBy(pair => pair.Key).Select(pair => new ServiceRegistration(pair.Key, pair.Value));

        public void AddCommand(string name, Type implementationType)
        {
            if (allowedCommandsField != null && !allowedCommandsField.Contains(name))
                return;

            if (commandsField.ContainsKey(name))
                throw new InvalidOperationException($"A command named '{name}' is already registered.");

            commandsField.Add(name, implementationType);
        }

        public void AddChangedFile(string status, Type implementationType) => changedFilesField[status] = implementationType;

        public Type GetCommandType(string name) => commandsField.TryGetValue(name, out var type) ? type : null;

        public Type GetChangedFileType(string status) =>
            changedFilesField.TryGetValue(status, out var type)
                ? type
                : throw new InvalidOperationException($"No changed-file handler is registered for status '{status}'.");

        public string Describe()
        {
            var registrations = Commands.Select(command => $"{command.Name}: {command.ImplementationType.FullName}")
                .Concat(changedFilesField.OrderBy(pair => pair.Key)
                    .Select(pair => $"IGitChangedFile[{pair.Key}]: {pair.Value.FullName}"));
            return string.Join(Environment.NewLine, registrations);
        }

        public readonly struct ServiceRegistration
        {
            public ServiceRegistration(string name, Type implementationType)
            {
                Name = name;
                ImplementationType = implementationType;
            }

            public string Name { get; }

            public Type ImplementationType { get; }
        }
    }

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGitTfsServices(this IServiceCollection services, ServiceCatalog catalog, params Assembly[] assemblies)
        {
            var types = assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
                .Distinct()
                .ToArray();

            foreach (var type in types)
            {
                if (type == typeof(ServiceCatalog))
                {
                    continue;
                }

                var lifetime = type.IsDefined(typeof(SingletonServiceAttribute), inherit: false)
                    ? ServiceLifetime.Singleton
                    : ServiceLifetime.Transient;

                services.Add(new ServiceDescriptor(type, type, lifetime));

                var command = type.GetCustomAttribute<PluggableAttribute>();
                if (command != null && typeof(GitTfsCommand).IsAssignableFrom(type))
                {
                    catalog.AddCommand(command.Name, type);
                    continue;
                }

                foreach (var serviceType in type.GetInterfaces().Where(IsApplicationInterface))
                {
                    var descriptor = lifetime == ServiceLifetime.Singleton
                        ? ServiceDescriptor.Singleton(serviceType, provider => provider.GetRequiredService(type))
                        : ServiceDescriptor.Transient(serviceType, type);
                    services.Add(descriptor);
                }
            }

            return services;
        }

        private static bool IsApplicationInterface(Type type) =>
            type.Namespace?.StartsWith("GitTfs", StringComparison.Ordinal) == true &&
            type != typeof(GitTfsCommand);
    }

    public static class ServiceProviderExtensions
    {
        public static T CreateInstance<T>(this IServiceProvider services, params object[] arguments) =>
            (T)CreateInstance(services, typeof(T), arguments);

        public static object CreateInstance(this IServiceProvider services, Type implementationType, params object[] arguments)
        {
            foreach (var constructor in implementationType.GetConstructors().OrderByDescending(ctor => ctor.GetParameters().Length))
            {
                var parameters = constructor.GetParameters();
                var usedArguments = new bool[arguments.Length];
                var values = new object[parameters.Length];
                var canInvoke = true;

                for (var parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    var parameter = parameters[parameterIndex];
                    var argumentIndex = -1;
                    for (var candidateIndex = 0; candidateIndex < arguments.Length; candidateIndex++)
                    {
                        if (!usedArguments[candidateIndex] && arguments[candidateIndex] is not null &&
                            parameter.ParameterType.IsInstanceOfType(arguments[candidateIndex]))
                        {
                            argumentIndex = candidateIndex;
                            break;
                        }
                    }

                    if (argumentIndex >= 0)
                    {
                        usedArguments[argumentIndex] = true;
                        values[parameterIndex] = arguments[argumentIndex];
                        continue;
                    }

                    var service = services.GetService(parameter.ParameterType);
                    if (service is not null)
                    {
                        values[parameterIndex] = service;
                    }
                    else if (parameter.HasDefaultValue)
                    {
                        values[parameterIndex] = parameter.DefaultValue;
                    }
                    else
                    {
                        canInvoke = false;
                        break;
                    }
                }

                if (canInvoke)
                {
                    return constructor.Invoke(values);
                }
            }

            throw new InvalidOperationException($"A suitable constructor for type {implementationType.FullName} could not be located.");
        }
    }
}
