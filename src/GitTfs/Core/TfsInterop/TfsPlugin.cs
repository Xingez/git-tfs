
namespace GitTfs.Core.TfsInterop
{
    using global::System.Diagnostics;
    using global::System.Reflection;
    using global::Microsoft.Extensions.DependencyInjection;
    public abstract class TfsPlugin
    {
        public static TfsPlugin Find()
        {
            var pluginLoader = new PluginLoader();
            var explicitVersion = Environment.GetEnvironmentVariable("GIT_TFS_CLIENT");
            if (!string.IsNullOrEmpty(explicitVersion))
            {
                return pluginLoader.TryLoadVsPluginVersion(explicitVersion) ??
                       pluginLoader.Fail("Unable to load TFS version specified in GIT_TFS_CLIENT (" + explicitVersion + ")!");
            }

            foreach (string version in SupportedVersions)
            {
                TfsPlugin plugin = pluginLoader.TryLoadVsPluginVersion(version);
                if (plugin != null)
                    return plugin;
            }

            return pluginLoader.Fail();
        }

        public static IReadOnlyList<string> SupportedVersions =>
            PluginLoader.SupportedVersions.Except(new[] { "Fake" }).ToList();

        private class PluginLoader
        {
            private readonly List<Exception> failuresField = new List<Exception>();
            private static string VsPluginAssemblyFolder { get; set; }

            public static IReadOnlyList<string> SupportedVersions => new List<string>
            {
                "2022",
                "Fake"
            };

            public TfsPlugin TryLoadVsPluginVersion(string version)
            {
                if (!SupportedVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
                {
                    Trace.WriteLine("Visual Studio " + version + " not supported...");
                    return null;
                }
                var assembly = "GitTfs.Vs" + version;
                return Try(assembly, "GitTfs.TfsPlugin");
            }

            public TfsPlugin Try(string assembly, string pluginType)
            {
                VsPluginAssemblyFolder = assembly;
                AppDomain currentDomain = AppDomain.CurrentDomain;
                currentDomain.AssemblyResolve += LoadFromSameFolder;
                try
                {
                    var plugin = (TfsPlugin)Activator.CreateInstance(Assembly.Load(assembly).GetType(pluginType));
                    if (plugin.IsViable())
                        return plugin;
                }
                catch (Exception e)
                {
                    failuresField.Add(e);
                }
                currentDomain.AssemblyResolve -= LoadFromSameFolder;
                return null;
            }

            private static Assembly LoadFromSameFolder(object sender, ResolveEventArgs args)
            {
                string folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string assemblyPath = Path.Combine(folderPath, VsPluginAssemblyFolder, new AssemblyName(args.Name).Name + ".dll");
                if (File.Exists(assemblyPath) == false) return null;
                return Assembly.LoadFrom(assemblyPath);
            }

            public TfsPlugin Fail() => throw new PluginLoaderException(failuresField);

            public TfsPlugin Fail(string message) => throw new PluginLoaderException(message, failuresField);

            private class PluginLoaderException : Exception
            {
                public IEnumerable<Exception> InnerExceptions { get; private set; }

                public PluginLoaderException(string message, IEnumerable<Exception> failures) : base(message, failures.LastOrDefault())
                {
                    InnerExceptions = failures;
                }

                public PluginLoaderException(IEnumerable<Exception> failures) : this("Unable to load any TFS assemblies!", failures)
                { }
            }
        }

        public virtual IEnumerable<Assembly> GetServiceAssemblies() => new[] { GetType().Assembly };

        public virtual void ConfigureServices(IServiceCollection services)
        {
            // Mark the ITfsHelper as a singleton to ensure that we create it only once.
            // Otherwise, it is created e.g. for every remote which is wasteful.
            var helperType = GetType().Assembly.GetTypes()
                .FirstOrDefault(type => type.IsClass && !type.IsAbstract && typeof(ITfsHelper).IsAssignableFrom(type));
            if (helperType != null)
                services.AddSingleton(typeof(ITfsHelper), helperType);
        }

        public abstract bool IsViable();
    }
}
