
namespace GitTfs
{
    using global::System.Diagnostics;
    using global::Microsoft.Extensions.DependencyInjection;
    using global::GitTfs.Commands;
    using global::GitTfs.Core;
    using global::GitTfs.Util;
    public class GitTfs
    {
        private readonly IGitTfsVersionProvider gitTfsVersionProviderField;
        private readonly GitTfsCommandFactory commandFactoryField;
        private readonly IHelpHelper helpField;
        private readonly IServiceProvider servicesField;
        private readonly GitTfsCommandRunner runnerField;
        private readonly Globals globalsField;
        private readonly Bootstrapper bootstrapperField;
        private readonly AuthorsFile authorsFileHelperField;
        private readonly GitTfsSettings settingsField;

        public GitTfs(GitTfsCommandFactory commandFactory, IHelpHelper help, IServiceProvider services,
            IGitTfsVersionProvider gitTfsVersionProvider, GitTfsCommandRunner runner, Globals globals, Bootstrapper bootstrapper,
            AuthorsFile authorsFileHelper, GitTfsSettings settings)
        {
            commandFactoryField = commandFactory;
            helpField = help;
            servicesField = services;
            gitTfsVersionProviderField = gitTfsVersionProvider;
            runnerField = runner;
            globalsField = globals;
            bootstrapperField = bootstrapper;
            authorsFileHelperField = authorsFileHelper;
            settingsField = settings;
        }

        public int Run(IList<string> args)
        {
            InitializeGlobals();
            globalsField.CommandLineRun = "git tfs " + string.Join(" ", args);
            var command = ExtractCommand(args);
            var unparsedArgs = ParseOptions(command, args);
            UpdateLoggerOnDebugging();
            Trace.WriteLine("Command run:" + globalsField.CommandLineRun);
            if (RequiresValidGitRepository(command)) AssertValidGitRepository();
            bool willCreateRepository = command.GetType() == typeof(Clone) || command.GetType() == typeof(QuickClone) || command.GetType() == typeof(Init);
            ParseAuthorsAndSave(!willCreateRepository);
            var exitCode = Main(command, unparsedArgs);
            if (willCreateRepository)
            {
                authorsFileHelperField.SaveAuthorFileInRepository(globalsField.AuthorsFilePath, globalsField.GitDir);
            }
            return exitCode;
        }

        private void UpdateLoggerOnDebugging()
        {
            if (globalsField.DebugOutput)
                Program.EnableDebugLogging();
        }

        public int Main(GitTfsCommand command, IList<string> unparsedArgs)
        {
            Trace.WriteLine(gitTfsVersionProviderField.GetVersionString());
            if (globalsField.ShowHelp)
            {
                return helpField.ShowHelp(command);
            }
            if (globalsField.ShowVersion)
            {
                Trace.TraceInformation(gitTfsVersionProviderField.GetVersionString());
                Trace.TraceInformation(GitTfsConstants.MessageForceVersion);
                return GitTfsExitCodes.OK;
            }
            try
            {
                return runnerField.Run(command, unparsedArgs);
            }
            finally
            {
                servicesField.GetRequiredService<Janitor>().Dispose();
            }
        }

        public bool RequiresValidGitRepository(GitTfsCommand command) => !command.GetType().GetCustomAttributes(typeof(RequiresValidGitRepositoryAttribute), false).IsEmpty();

        private void ParseAuthorsAndSave(bool couldSaveAuthorFile)
        {
            try
            {
            servicesField.GetRequiredService<AuthorsFile>().Parse(globalsField.AuthorsFilePath, globalsField.GitDir, couldSaveAuthorFile);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error when parsing author file:" + ex);
                if (!string.IsNullOrEmpty(globalsField.AuthorsFilePath))
                    throw;
                Trace.TraceWarning("warning: author file ignored due to a problem occuring when reading it :\n\t" + ex.Message);
                Trace.TraceWarning("         Verify the file :" + Path.Combine(globalsField.GitDir, AuthorsFile.GitTfsCachedAuthorsFileName));
            }
        }

        public void InitializeGlobals()
        {
            globalsField.DebugOutput = settingsField.Debug;
            if (globalsField.GitDir != null)
            {
                globalsField.GitDirSetByUser = true;
            }
            else
            {
                globalsField.GitDir = ".git";
            }
            globalsField.Bootstrapper = bootstrapperField;
        }

        public void AssertValidGitRepository()
        {
            var git = servicesField.GetRequiredService<IGitHelpers>();
            if (!Directory.Exists(globalsField.GitDir))
            {
                if (globalsField.GitDirSetByUser)
                {
                    throw new Exception("This command must be run inside a git repository!\nGIT_DIR=" + globalsField.GitDir + " explicitly set, but it is not a directory.");
                }
                var gitDir = globalsField.GitDir;
                globalsField.GitDir = null;
                string cdUp = null;
                git.WrapGitCommandErrors("This command must be run inside a git repository!\nAlready at top level, but " + gitDir + " not found.",
                                         () =>
                                             {
                                                 cdUp = git.CommandOneline("rev-parse", "--show-cdup");
                                                 if (string.IsNullOrEmpty(cdUp))
                                                     gitDir = ".";
                                                 else
                                                     cdUp = cdUp.TrimEnd();
                                                 if (string.IsNullOrEmpty(cdUp))
                                                     cdUp = ".";
                                             });
                Environment.CurrentDirectory = cdUp;
                if (!Directory.Exists(gitDir))
                {
                    throw new Exception("This command must be run inside a git repository!\n" + gitDir + " still not found after going to " + cdUp);
                }
                globalsField.GitDir = gitDir;
            }
            globalsField.Repository = git.MakeRepository(globalsField.GitDir);
        }

        public GitTfsCommand ExtractCommand(IList<string> args)
        {
            for (int i = 0; i < args.Count; i++)
            {
                var command = commandFactoryField.GetCommand(args[i]);
                if (command != null)
                {
                    args.RemoveAt(i);
                    return command;
                }
            }
            return servicesField.GetRequiredService<Commands.Help>();
        }

        public IList<string> ParseOptions(GitTfsCommand command, IList<string> args) => command.GetAllOptions(servicesField).Parse(args);
    }
}
