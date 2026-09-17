
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::GitTfs.Core.TfsInterop;
    [Pluggable("clone")]
    [Description("clone [options] <tfs-subfolder> <output-path>\n  The target server and clone defaults are read from appsettings.json.\n  ex : git tfs clone $/ProjectName/ProjectBranch .\n")]
    public class Clone : GitTfsCommand
    {
        private readonly Fetch fetchField;
        private readonly Init initField;
        private readonly Globals globalsField;
        private readonly InitBranch initBranchField;
        private readonly GitTfsSettings settingsField;
        private readonly ConfigProperties propertiesField;
        private readonly RemoteOptions remoteOptionsField;
        private bool resumableField;

        public Clone(Globals globals, Fetch fetch, Init init, InitBranch initBranch, GitTfsSettings settings,
            ConfigProperties properties, RemoteOptions remoteOptions)
        {
            fetchField = fetch;
            initField = init;
            globalsField = globals;
            initBranchField = initBranch;
            settingsField = settings;
            propertiesField = properties;
            remoteOptionsField = remoteOptions;
            resumableField = settings.Resumable;
            propertiesField.BatchSize = settings.BatchSize;
            remoteOptionsField.NoParallel = settings.NoParallel;
            globals.GcCountdown = globals.GcPeriod;
        }

        public OptionSet OptionSet => initField.OptionSet.Merge(fetchField.OptionSet)
                           .Add("resumable", "if an error occurred, try to continue when you restart clone with same parameters", v => resumableField = v != null);

        public int Run(string tfsRepositoryPath, string gitRepositoryPath)
        {
            if (string.IsNullOrWhiteSpace(tfsRepositoryPath) || string.Equals(tfsRepositoryPath, GitTfsConstants.TfsRoot, StringComparison.OrdinalIgnoreCase))
                throw new GitTfsException("Clone requires a TFS subfolder, not the TFS root.");

            if (string.IsNullOrWhiteSpace(gitRepositoryPath))
                throw new GitTfsException("Clone requires an output path.");

            tfsRepositoryPath.AssertValidTfsPath();

            if (string.IsNullOrWhiteSpace(settingsField.TargetServer))
            {
                var source = string.IsNullOrWhiteSpace(settingsField.SourcePath) ? "appsettings.json" : settingsField.SourcePath;
                throw new GitTfsException("TargetServer is not configured in " + source + ". Set it before using 'git tfs clone <tfs-subfolder> <output-path>'.");
            }

            return Run(settingsField.TargetServer, tfsRepositoryPath, gitRepositoryPath);
        }

        public int Run(string tfsUrl, string tfsRepositoryPath, string gitRepositoryPath)
        {
            var currentDir = Environment.CurrentDirectory;
            var repositoryDirCreated = InitGitDir(gitRepositoryPath);

            // TFS string representations of repository paths do not end in trailing slashes
            if (tfsRepositoryPath != GitTfsConstants.TfsRoot)
                tfsRepositoryPath = (tfsRepositoryPath ?? string.Empty).TrimEnd('/');

            int retVal = 0;
            try
            {
                if (repositoryDirCreated)
                {
                    retVal = initField.Run(tfsUrl, tfsRepositoryPath, gitRepositoryPath);
                }
                else
                {
                    try
                    {
                        Environment.CurrentDirectory = gitRepositoryPath;
                        globalsField.Repository = initField.GitHelper.MakeRepository(globalsField.GitDir);
                    }
                    catch (Exception)
                    {
                        retVal = initField.Run(tfsUrl, tfsRepositoryPath, gitRepositoryPath);
                    }
                }

                VerifyTfsPathToClone(tfsRepositoryPath);
            }
            catch
            {
                if (!resumableField)
                {
                    try
                    {
                        // if we appeared to be inside repository dir when exception was thrown - we won't be able to delete it
                        Environment.CurrentDirectory = currentDir;
                        if (repositoryDirCreated)
                            Directory.Delete(gitRepositoryPath, recursive: true);
                        else
                            CleanDirectory(gitRepositoryPath);
                    }
                    catch (IOException e)
                    {
                        // swallow IOException. Smth went wrong before this and we're much more interested in that error
                        string msg = $"warning: Something went wrong while cleaning file after internal error (See below).\n    Can't clean up files because of IOException:\n{e.IndentExceptionMessage()}\n";
                        Trace.WriteLine(msg);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        // swallow it also
                        string msg = $"warning: Something went wrong while cleaning file after internal error (See below).\n    Can't clean up files because of UnauthorizedAccessException:\n{e.IndentExceptionMessage()}\n";
                        Trace.WriteLine(msg);
                    }
                }

                throw;
            }
            bool errorOccurs = false;
            try
            {
                if (tfsRepositoryPath == GitTfsConstants.TfsRoot)
                    fetchField.BranchStrategy = BranchStrategy.None;

                globalsField.Repository.SetConfig(GitTfsConstants.IgnoreBranches, fetchField.BranchStrategy == BranchStrategy.None);

                if (retVal == 0)
                {
                    fetchField.Run(fetchField.BranchStrategy == BranchStrategy.All);
                    globalsField.Repository.GarbageCollect();
                }

                if (fetchField.BranchStrategy == BranchStrategy.All && initBranchField != null)
                {
                    initBranchField.CloneAllBranches = true;

                    retVal = initBranchField.Run();
                }
            }
            catch (GitTfsException)
            {
                errorOccurs = true;
                throw;
            }
            catch (Exception ex)
            {
                errorOccurs = true;
                throw new GitTfsException("error: a problem occurred when trying to clone the repository. Try to solve the problem described below.\nIn any case, after, try to continue using command `git tfs "
                    + "clone <tfs-subfolder> <output-path>`\n", ex);
            }
            finally
            {
                try
                {
                    if (!initField.IsBare) globalsField.Repository.Merge(globalsField.Repository.ReadTfsRemote(globalsField.RemoteId).RemoteRef);
                }
                catch (Exception)
                {
                    //Swallow exception because the previously thrown exception is more important...
                    if (!errorOccurs)
                        throw;
                }
            }
            return retVal;
        }

        private void VerifyTfsPathToClone(string tfsRepositoryPath)
        {
            if (initBranchField == null)
                return;
            try
            {
                var remote = globalsField.Repository.ReadTfsRemote(GitTfsConstants.DefaultRepositoryId);

                if (!remote.Tfs.IsExistingInTfs(tfsRepositoryPath))
                    throw new GitTfsException("error: the path " + tfsRepositoryPath + " you want to clone doesn't exist!")
                        .WithRecommendation("To discover which branch to clone, you could use the command :\ngit tfs list-remote-branches " + remote.TfsUrl);

                if (fetchField.BranchStrategy == BranchStrategy.None)
                    return;

                var tfsTrunkRepository = remote.Tfs.GetRootTfsBranchForRemotePath(tfsRepositoryPath, false);
                if (tfsTrunkRepository == null)
                {
                    var tfsRootBranches = remote.Tfs.GetAllTfsRootBranchesOrderedByCreation();
                    if (!tfsRootBranches.Any())
                    {
                        Trace.TraceInformation("info: no TFS root found !\n\nPS:perhaps you should convert your trunk folder into a branch in TFS.");
                        return;
                    }
                    if (fetchField.BranchStrategy == BranchStrategy.All)
                        throw new GitTfsException("error: cloning the whole repository or too high in the repository path doesn't permit to manage branches!");
                    Trace.TraceWarning("warning: you are going to clone the whole repository or too high in the repository path!");
                    return;
                }

                var tfsBranchesPath = tfsTrunkRepository.GetAllChildren();
                var tfsPathToClone = tfsRepositoryPath.TrimEnd('/').ToLower();
                var tfsTrunkRepositoryPath = tfsTrunkRepository.Path;
                if (tfsPathToClone != tfsTrunkRepositoryPath.ToLower())
                {
                    if (tfsBranchesPath.Select(e => e.Path.ToLower()).Contains(tfsPathToClone))
                        Trace.TraceInformation("info: you are going to clone a branch instead of the trunk ( {0} )\n"
                            + "   => If you want to manage branches with git-tfs, clone {0} with '--branches=all' option instead...)", tfsTrunkRepositoryPath);
                    else
                        Trace.TraceWarning("warning: you are going to clone a subdirectory of a branch and won't be able to manage branches :(\n"
                            + "   => If you want to manage branches with git-tfs, clone " + tfsTrunkRepositoryPath + " with '--branches=all' option instead...)");
                }
            }
            catch (GitTfsException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("warning: a server error occurs when trying to verify the tfs path cloned:\n   " + ex.Message
                    + "\n   try to continue anyway...");
            }
        }

        private bool InitGitDir(string gitRepositoryPath)
        {
            bool repositoryDirCreated = false;
            var di = new DirectoryInfo(gitRepositoryPath);
            if (di.Exists)
            {
                bool isDebuggerAttached = false;
#if DEBUG
                isDebuggerAttached = Debugger.IsAttached;
#endif
                if (!isDebuggerAttached && !resumableField)
                {
                    if (di.EnumerateFileSystemInfos().Any())
                        throw new GitTfsException("error: Specified git repository directory is not empty");
                }
            }
            else
            {
                repositoryDirCreated = true;
                di.Create();
            }
            return repositoryDirCreated;
        }

        private static void CleanDirectory(string gitRepositoryPath)
        {
            var di = new DirectoryInfo(gitRepositoryPath);
            foreach (var fileSystemInfo in di.EnumerateDirectories())
                fileSystemInfo.Delete(true);
            foreach (var fileSystemInfo in di.EnumerateFiles())
                fileSystemInfo.Delete();
        }
    }
}
