using System.Diagnostics;
using System.Reflection;
using GitTfs.Core;
using GitTfs.Core.Changes.Git;
using GitTfs.Core.TfsInterop;
using GitTfs.Util;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace GitTfs
{
    public class Program
    {
        private static string _logFilePath;
        private static LoggingLevelSwitch _consoleLevelSwitch;

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

            Trace.TraceWarning("All the logs could be found in the log file: " + _logFilePath);
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
            ConfigureLogger();
            var tfsPlugin = LoadTfsPlugin();
            var services = new ServiceCollection();
            var catalog = new ServiceCatalog();

            services.AddSingleton(catalog);
            services.AddGitTfsServices(catalog,
                new[] { typeof(Program).Assembly }
                    .Concat(tfsPlugin.GetServiceAssemblies())
                    .Distinct()
                    .ToArray());
            services.AddTransient<IGitHelpers, GitHelpers>();
            services.AddSingleton<GitTfsSettings>(_ => GitTfsSettings.Load());
            AddGitChangeTypes(catalog);
            tfsPlugin.ConfigureServices(services);

            return services.BuildServiceProvider();
        }

        private static Core.TfsInterop.TfsPlugin LoadTfsPlugin()
        {
            var requestedClient = Environment.GetEnvironmentVariable("GIT_TFS_CLIENT");
            if (string.IsNullOrWhiteSpace(requestedClient)
                || string.Equals(requestedClient, "2022", StringComparison.OrdinalIgnoreCase))
                return new TfsPlugin();

            // The Fake client is used by the integration test project. Keep its
            // test-only dynamic loading path without making the production
            // VS2022 client a runtime plugin.
            return TfsPlugin.Find();
        }

        private static void ConfigureLogger()
        {
            try
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "git-tfs");
                Directory.CreateDirectory(logDirectory);
                _logFilePath = Path.Combine(logDirectory, GitTfsConstants.LogFileName);
                _consoleLevelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console(
                        levelSwitch: _consoleLevelSwitch,
                        outputTemplate: "{Message:lj}{NewLine}",
                        theme: SystemConsoleTheme.Literate)
                    .WriteTo.File(
                        _logFilePath,
                        restrictedToMinimumLevel: LogEventLevel.Debug,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}")
                    .CreateLogger();

                Trace.Listeners.Add(new SerilogTraceListener());
            }
            catch (Exception ex)
            {
                Trace.Listeners.Add(new ConsoleTraceListener());
                Trace.TraceWarning("Fail to enable logging in file due to error:" + ex.Message);
            }
        }

        internal static void EnableDebugLogging() => _consoleLevelSwitch?.MinimumLevel = LogEventLevel.Debug;

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
