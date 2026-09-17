using System.Diagnostics;
using System.Reflection;
using GitTfs.Core;
using GitTfs.Core.Changes.Git;
using GitTfs.Core.TfsInterop;
using GitTfs.Util;
using StructureMap;
using StructureMap.Graph;
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
            var container = Initialize();
            return container.GetInstance<GitTfs>().Run(new List<string>(args));
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
                var gitCommandException = e as GitCommandException;
                if (gitCommandException != null)
                    Trace.TraceError("error running command: " + gitCommandException.Process.StartInfo.FileName + " " + gitCommandException.Process.StartInfo.Arguments);

                Trace.TraceError(e.Message);
                e = e.InnerException;
            }
        }

        private static IContainer Initialize() => new Container(Initialize);

        private static void Initialize(ConfigurationExpression initializer)
        {
            ConfigureLogger();
            var tfsPlugin = TfsPlugin.Find();
            initializer.Scan(x => { Initialize(x); tfsPlugin.Initialize(x); });
            initializer.For<GitTfsSettings>().Use(GitTfsSettings.Load());
            initializer.For<IGitRepository>().Add<GitRepository>();
            AddGitChangeTypes(initializer);
            DoCustomConfiguration(initializer);
            tfsPlugin.Initialize(initializer);
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

        public static void AddGitChangeTypes(ConfigurationExpression initializer)
        {
            // See git-diff-tree(1).
            initializer.For<IGitChangedFile>().Use<Add>().Named(GitChangeInfo.ChangeType.ADD);
            initializer.For<IGitChangedFile>().Use<Copy>().Named(GitChangeInfo.ChangeType.COPY);
            initializer.For<IGitChangedFile>().Use<Modify>().Named(GitChangeInfo.ChangeType.MODIFY);
            //initializer.For<IGitChangedFile>().Use<TypeChange>().Named(GitChangeInfo.GitChange.TYPECHANGE);
            initializer.For<IGitChangedFile>().Use<Delete>().Named(GitChangeInfo.ChangeType.DELETE);
            initializer.For<IGitChangedFile>().Use<RenameEdit>().Named(GitChangeInfo.ChangeType.RENAMEEDIT);
            //initializer.For<IGitChangedFile>().Use<Unmerged>().Named(GitChangeInfo.GitChange.UNMERGED);
            //initializer.For<IGitChangedFile>().Use<Unknown>().Named(GitChangeInfo.GitChange.UNKNOWN);
        }

        private static void Initialize(IAssemblyScanner scan)
        {
            scan.WithDefaultConventions();
            scan.TheCallingAssembly();
        }

        private static void DoCustomConfiguration(ConfigurationExpression initializer)
        {
            foreach (var type in typeof(Program).Assembly.GetTypes())
            {
                foreach (ConfiguresStructureMap attribute in type.GetCustomAttributes(typeof(ConfiguresStructureMap), false))
                {
                    attribute.Initialize(initializer, type);
                }
            }
        }
    }
}
