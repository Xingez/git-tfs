
namespace GitTfs
{
    using System.Diagnostics;
    using System.Reflection;
    using GitTfs.Core;
    using GitTfs.Core.RestTfs;
    using GitTfs.Core.TfsInterop;
    using GitTfs.Util;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Console;
    using Microsoft.Extensions.Options;
    using Spectre.Console;
    public class Program
    {
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
            return services.GetRequiredService<GitTfsApplication>().Run(new List<string>(args));
        }

        private static void ReportException(Exception e)
        {
            var gitTfsException = e as GitTfsException;
            if (gitTfsException != null)
            {
                Trace.WriteLine(gitTfsException);
                Trace.TraceError(gitTfsException.Message);
                AnsiConsole.WriteLine(gitTfsException.Message);
                if (gitTfsException.InnerException != null)
                    ReportException(gitTfsException.InnerException);
                if (!gitTfsException.RecommendedSolutions.IsEmpty())
                {
                    Trace.TraceError("You may be able to resolve this problem.");
                    AnsiConsole.WriteLine("You may be able to resolve this problem.");
                    foreach (var solution in gitTfsException.RecommendedSolutions)
                    {
                        Trace.TraceError("- " + solution);
                        AnsiConsole.WriteLine("- " + solution);
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
                AnsiConsole.WriteLine(e.Message);
                e = e.InnerException;
            }
        }

        private static ServiceProvider Initialize()
        {
            var settings = GitTfsSettings.Load();
            var globals = new Globals { DebugOutput = settings.Debug };
            var tfsPlugin = LoadTfsPlugin();
            var services = new ServiceCollection();
            var catalog = new ServiceCatalog(GetAvailableCommands());

            services.AddLogging(logging => ConfigureLogging(logging, globals));
            services.AddSingleton(catalog);
            services.AddGitTfsServices(catalog,
                new[] { typeof(Program).Assembly }
                    .Concat(tfsPlugin.GetServiceAssemblies())
                    .Distinct()
                    .ToArray());
            services.AddTransient<IGitHelpers, GitHelpers>();
            services.AddSingleton(globals);
            services.AddSingleton<IOptions<GitTfsSettings>>(Options.Create(settings));
            services.AddHttpClient(RestTfsClient.HttpClientName,
                    (provider, client) => RestTfsClient.ConfigureHttpClient(
                        client, provider.GetRequiredService<IOptions<GitTfsSettings>>().Value))
                .ConfigurePrimaryHttpMessageHandler(provider =>
                    RestTfsClient.CreateHttpMessageHandler(provider.GetRequiredService<IOptions<GitTfsSettings>>().Value));
            tfsPlugin.ConfigureServices(services);

            var serviceProvider = services.BuildServiceProvider();
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new MicrosoftLoggingTraceListener(
                serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GitTfs.Trace")));
            settings.ApplyProxySettings();
            return serviceProvider;
        }

        private static IEnumerable<string> GetAvailableCommands()
            => new[] { "changeset", "help" };

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

        private static void ConfigureLogging(ILoggingBuilder logging, Globals globals)
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter((_, level) => globals.DebugOutput && level >= LogLevel.Debug);
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.UseUtcTimestamp = false;
                options.ColorBehavior = LoggerColorBehavior.Enabled;
            });
        }

    }
}
