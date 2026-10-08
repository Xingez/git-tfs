
namespace GitTfs
{
    using global::System.Diagnostics;
    using global::System.Reflection;
    using global::GitTfs.Core;
    using global::GitTfs.Core.Changes.Git;
    using global::GitTfs.Core.RestTfs;
    using global::GitTfs.Core.TfsInterop;
    using global::GitTfs.Util;
    using global::Microsoft.Extensions.DependencyInjection;
    using global::Microsoft.Extensions.Logging;
    using global::Microsoft.Extensions.Logging.Console;
    public class Program
    {
        private static LogLevel consoleMinimumLevelField = LogLevel.Information;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                Environment.ExitCode = MainCore(args);
            }
            catch (Exception e)
            {
                ReportException(e);
                Environment.ExitCode = GitTfsExitCodes.ExceptionThrown;
            }
        }

        public static int MainCore(string[] args)
        {
            using var services = Initialize();
            return services.GetRequiredService<GitTfs>().Run(new List<string>(args));
        }

        private static void ReportException(Exception e)
        {
            var gitTfsException = e as GitTfsException;
            if (gitTfsException != null)
            {
                Trace.WriteLine(gitTfsException);
                Trace.TraceError(gitTfsException.Message);
                if (gitTfsException.InnerException != null)
                    ReportException(gitTfsException.InnerException);
                if (!gitTfsException.RecommendedSolutions.IsEmpty())
                {
                    Trace.TraceError("You may be able to resolve this problem.");
                    foreach (var solution in gitTfsException.RecommendedSolutions)
                    {
                        Trace.TraceError("- " + solution);
                    }
                }
            }
            else
            {
                ReportInternalException(e);
            }

            Trace.TraceWarning("The command failed; review the messages above for details.");
        }

        private static void ReportInternalException(Exception e)
        {
            Trace.WriteLine(e);
            while (e is TargetInvocationException && e.InnerException != null)
                e = e.InnerException;
            while (e != null)
            {
                if (e is GitCommandException gitCommandException)
                    Trace.TraceError("error running command: " + gitCommandException.Process.StartInfo.FileName + " " + gitCommandException.Process.StartInfo.Arguments);

                Trace.TraceError(e.Message);
                e = e.InnerException;
            }
        }

        private static ServiceProvider Initialize()
        {
            var settings = GitTfsSettings.Load();
            settings.ApplyProxySettings();
            var tfsPlugin = LoadTfsPlugin();
            var services = new ServiceCollection();
            var catalog = new ServiceCatalog(GetAvailableCommands());

            services.AddLogging(ConfigureLogging);
            services.AddSingleton(catalog);
            services.AddGitTfsServices(catalog,
                new[] { typeof(Program).Assembly }
                    .Concat(tfsPlugin.GetServiceAssemblies())
                    .Distinct()
                    .ToArray());
            services.AddTransient<IGitHelpers, GitHelpers>();
            services.AddSingleton(settings);
            services.AddHttpClient(RestTfsClient.HttpClientName,
                    (provider, client) => RestTfsClient.ConfigureHttpClient(
                        client, provider.GetRequiredService<GitTfsSettings>()))
                .ConfigurePrimaryHttpMessageHandler(provider =>
                    RestTfsClient.CreateHttpMessageHandler(provider.GetRequiredService<GitTfsSettings>()));
            AddGitChangeTypes(catalog);
            tfsPlugin.ConfigureServices(services);

            var serviceProvider = services.BuildServiceProvider();
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new MicrosoftLoggingTraceListener(
                serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GitTfs.Trace")));
            return serviceProvider;
        }

        private static IEnumerable<string> GetAvailableCommands()
            => Array.Empty<string>();

        private static Core.TfsInterop.TfsPlugin LoadTfsPlugin()
        {
            var requestedClient = Environment.GetEnvironmentVariable("GIT_TFS_CLIENT");
            if (string.Equals(requestedClient, "Fake", StringComparison.OrdinalIgnoreCase))
                return Core.TfsInterop.TfsPlugin.Find();

            return new RestTfsPlugin();
        }

        private sealed class RestTfsPlugin : Core.TfsInterop.TfsPlugin
        {
            public override IEnumerable<Assembly> GetServiceAssemblies() => Enumerable.Empty<Assembly>();

            public override void ConfigureServices(IServiceCollection services)
            {
            }

            public override bool IsViable() => true;
        }

        private static void ConfigureLogging(ILoggingBuilder logging)
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter((_, level) => level >= consoleMinimumLevelField);
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.UseUtcTimestamp = false;
                options.ColorBehavior = LoggerColorBehavior.Enabled;
            });
        }

        internal static void EnableDebugLogging() => consoleMinimumLevelField = LogLevel.Debug;

        public static void AddGitChangeTypes(ServiceCatalog catalog)
        {
            // See git-diff-tree(1).
            catalog.AddChangedFile(GitChangeInfo.ChangeType.ADD, typeof(Add));
            catalog.AddChangedFile(GitChangeInfo.ChangeType.COPY, typeof(Copy));
            catalog.AddChangedFile(GitChangeInfo.ChangeType.MODIFY, typeof(Modify));
            //catalog.AddChangedFile(GitChangeInfo.ChangeType.TYPECHANGE, typeof(TypeChange));
            catalog.AddChangedFile(GitChangeInfo.ChangeType.DELETE, typeof(Delete));
            catalog.AddChangedFile(GitChangeInfo.ChangeType.RENAMEEDIT, typeof(RenameEdit));
            //catalog.AddChangedFile(GitChangeInfo.ChangeType.UNMERGED, typeof(Unmerged));
            //catalog.AddChangedFile(GitChangeInfo.ChangeType.UNKNOWN, typeof(Unknown));
        }
    }
}
