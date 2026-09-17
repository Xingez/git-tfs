
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::System.Diagnostics;
    using global::System.Text.RegularExpressions;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("init")]
    [Description("init [options] tfs-url-or-instance-name repository-path [git-repository]")]
    public class Init : GitTfsCommand
    {
        private readonly InitOptions initOptionsField;
        private readonly RemoteOptions remoteOptionsField;
        private readonly Globals globalsField;
        private readonly IGitHelpers gitHelperField;
        private readonly AuthorsFile authorsFileHelperField;

        public Init(RemoteOptions remoteOptions, InitOptions initOptions, Globals globals, IGitHelpers gitHelper, AuthorsFile authorsFileHelper)
        {
            remoteOptionsField = remoteOptions;
            gitHelperField = gitHelper;
            authorsFileHelperField = authorsFileHelper;
            globalsField = globals;
            initOptionsField = initOptions;
        }

        public OptionSet OptionSet => initOptionsField.OptionSet.Merge(remoteOptionsField.OptionSet);

        public bool IsBare => initOptionsField.IsBare;

        public IGitHelpers GitHelper => gitHelperField;

        public int Run(string tfsUrl, string tfsRepositoryPath)
        {
            tfsRepositoryPath.AssertValidTfsPathOrRoot();
            DoGitInitDb();
            VerifyGitUserConfig();
            SaveAuthorFileInRepository();
            CommitTheGitIgnoreFile(remoteOptionsField.GitIgnorePath);
            UseTheGitIgnoreFile(remoteOptionsField.GitIgnorePath);
            GitTfsInit(tfsUrl, tfsRepositoryPath);
            return 0;
        }

        private void VerifyGitUserConfig()
        {
            var userName = globalsField.Repository.GetConfig<string>("user.name");
            var userEmail = globalsField.Repository.GetConfig<string>("user.email");
            if (string.IsNullOrWhiteSpace(userName)
                || string.IsNullOrWhiteSpace(userEmail))
            {
                throw new GitTfsException("Git-tfs requires that the user data in git config should be set. Please configure them before using git-tfs"
                                          + Environment.NewLine + "Actual config: "
                                          + Environment.NewLine + " * user name: " + (string.IsNullOrWhiteSpace(userName) ? "<not set>" : userName)
                                          + Environment.NewLine + " * user email: " + (string.IsNullOrWhiteSpace(userEmail) ? "<not set>" : userEmail)
                                          + Environment.NewLine + "For help on how to set user git config, see https://git-scm.com/book/en/v2/Getting-Started-First-Time-Git-Setup");
            }
        }

        private void SaveAuthorFileInRepository() => authorsFileHelperField.SaveAuthorFileInRepository(globalsField.AuthorsFilePath, globalsField.GitDir);

        private void CommitTheGitIgnoreFile(string pathToGitIgnoreFile)
        {
            if (string.IsNullOrWhiteSpace(pathToGitIgnoreFile))
            {
                Trace.WriteLine("No .gitignore file specified to commit...");
                return;
            }
            globalsField.Repository.CommitGitIgnore(pathToGitIgnoreFile);
        }

        private void UseTheGitIgnoreFile(string pathToGitIgnoreFile)
        {
            if (string.IsNullOrWhiteSpace(pathToGitIgnoreFile))
            {
                Trace.WriteLine("No .gitignore file specified to use...");
                return;
            }
            globalsField.Repository.UseGitIgnore(pathToGitIgnoreFile);
        }

        public int Run(string tfsUrl, string tfsRepositoryPath, string gitRepositoryPath)
        {
            tfsRepositoryPath.AssertValidTfsPathOrRoot();
            if (!initOptionsField.IsBare)
            {
                InitSubdir(gitRepositoryPath);
            }
            else
            {
                Environment.CurrentDirectory = gitRepositoryPath;
                globalsField.GitDir = ".";
            }
            var runResult = Run(tfsUrl, tfsRepositoryPath);
            try
            {
                File.WriteAllText(Path.Combine(globalsField.GitDir, "description"), tfsRepositoryPath + "\n" + HideUserCredentials(globalsField.CommandLineRun));
            }
            catch (Exception)
            {
                Trace.WriteLine("warning: Unable to update the repository description!");
            }
            return runResult;
        }

        public static string HideUserCredentials(string commandLineRun)
        {
            Regex rgx = new Regex("((--|/)username|[/-]u)(=| +)[^ ]+");
            commandLineRun = rgx.Replace(commandLineRun, "--username=xxx");
            rgx = new Regex("((--|/)password|[/-]p)(=| +)[^ ]+");
            return rgx.Replace(commandLineRun, "--password=xxx");
        }

        private void InitSubdir(string repositoryPath)
        {
            if (!Directory.Exists(repositoryPath))
                Directory.CreateDirectory(repositoryPath);
            Environment.CurrentDirectory = repositoryPath;
            globalsField.GitDir = ".git";
        }

        private void DoGitInitDb()
        {
            var initializedRepository = false;
            if (!Directory.Exists(globalsField.GitDir) || initOptionsField.IsBare)
            {
                gitHelperField.CommandNoisy(BuildInitCommand());
                initializedRepository = true;
            }
            globalsField.Repository = gitHelperField.MakeRepository(globalsField.GitDir);

            if (initializedRepository)
            {
                var initialBranch = initOptionsField.GitInitDefaultBranch
                    ?? globalsField.Repository.GetConfig<string>("init.defaultBranch");
                if (!string.IsNullOrWhiteSpace(initialBranch))
                    gitHelperField.CommandNoisy("symbolic-ref", "HEAD", "refs/heads/" + initialBranch);
            }

            if (!string.IsNullOrWhiteSpace(initOptionsField.WorkspacePath))
            {
                Trace.WriteLine("workspace path:" + initOptionsField.WorkspacePath);

                try
                {
                    Directory.CreateDirectory(initOptionsField.WorkspacePath);
                    globalsField.Repository.SetConfig(GitTfsConstants.WorkspaceConfigKey, initOptionsField.WorkspacePath);
                }
                catch (Exception)
                {
                    throw new GitTfsException("error: workspace path is invalid!");
                }
            }

            globalsField.Repository.SetConfig(GitTfsConstants.IgnoreBranches, false);
            globalsField.Repository.SetConfig(GitTfsConstants.IgnoreNotInitBranches, false);
            globalsField.Repository.SetConfig("core.autocrlf", initOptionsField.GitInitAutoCrlf);

            if (initOptionsField.GitInitIgnoreCase != null)
                globalsField.Repository.SetConfig("core.ignorecase", initOptionsField.GitInitIgnoreCase);
        }

        private string[] BuildInitCommand()
        {
            var initCommand = new List<string> { "init" };
            if (initOptionsField.GitInitTemplate != null)
                initCommand.Add("--template=" + initOptionsField.GitInitTemplate);
            if (initOptionsField.IsBare)
                initCommand.Add("--bare");
            if (initOptionsField.GitInitShared is string)
                initCommand.Add("--shared=" + initOptionsField.GitInitShared);
            else if (initOptionsField.GitInitShared != null)
                initCommand.Add("--shared");
            return initCommand.ToArray();
        }

        private void GitTfsInit(string tfsUrl, string tfsRepositoryPath)
        {
            // Azure DevOps throttles bursts aggressively. Keep the setting in the
            // repository config as an explicit record of the safe default.
            remoteOptionsField.NoParallel = true;
            globalsField.Repository.CreateTfsRemote(new RemoteInfo
            {
                Id = globalsField.RemoteId,
                Url = tfsUrl,
                Repository = tfsRepositoryPath,
                RemoteOptions = remoteOptionsField,
            });
        }
    }

    public static class Ext
    {
        private static readonly Regex ValidTfsPath = new Regex("^\\$/.+");
        public static bool IsValidTfsPath(this string tfsPath) => ValidTfsPath.IsMatch(tfsPath);

        public static void AssertValidTfsPathOrRoot(this string tfsPath)
        {
            if (tfsPath == GitTfsConstants.TfsRoot)
                return;
            AssertValidTfsPath(tfsPath);
        }

        public static void AssertValidTfsPath(this string tfsPath)
        {
            if (!ValidTfsPath.IsMatch(tfsPath))
                throw new GitTfsException("TFS repository can not be root and must start with \"$/\".", SuggestPaths(tfsPath));
        }

        private static IEnumerable<string> SuggestPaths(string tfsPath)
        {
            if (tfsPath == "$" || tfsPath == "$/")
                yield return "Cloning an entire TFS repository is not supported. Try using a subdirectory of the root (e.g. $/MyProject).";
            else if (tfsPath.StartsWith("$"))
                yield return "Try using $/" + tfsPath.Substring(1);
            else
                yield return "Try using $/" + tfsPath;
        }

        public static string ToGitRefName(this string expectedRefName)
        {
            expectedRefName = Regex.Replace(expectedRefName, @"[!~$?[*^: \\]", string.Empty);
            expectedRefName = expectedRefName.Replace("@{", string.Empty);
            expectedRefName = expectedRefName.Replace("..", string.Empty);
            expectedRefName = expectedRefName.Replace("//", string.Empty);
            expectedRefName = expectedRefName.Replace("/.", "/");
            expectedRefName = expectedRefName.TrimEnd('.', '/');
            return expectedRefName.Trim('/');
        }

        public static string ToGitBranchNameFromTfsRepositoryPath(this string tfsRepositoryPath, bool includeTeamProjectName = false)
        {
            if (includeTeamProjectName)
            {
                return tfsRepositoryPath
                    .Replace("$/", string.Empty)
                    .ToGitRefName();
            }

            string gitBranchNameExpected = tfsRepositoryPath.IndexOf("$/") == 0
                ? tfsRepositoryPath.Remove(0, tfsRepositoryPath.IndexOf('/', 2) + 1)
                : tfsRepositoryPath;

            return gitBranchNameExpected.ToGitRefName();
        }

        public static string ToTfsTeamProjectRepositoryPath(this string tfsRepositoryPath)
        {
            if (!tfsRepositoryPath.StartsWith("$/"))
            {
                return tfsRepositoryPath;
            }

            var index = tfsRepositoryPath.IndexOf('/', 2);
            return index == -1 ? tfsRepositoryPath : tfsRepositoryPath.Remove(index, tfsRepositoryPath.Length - index);
        }

        public static string ToLocalGitRef(this string refName) => "refs/heads/" + refName;
    }
}
