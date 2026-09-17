
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::GitTfs.Core.TfsInterop;
    using global::System.Text;
    [Pluggable("branch")]
    [Description("branch\n\n" +
        "       * Display inited remote TFS branches:\n       git tfs branch\n\n" +
        "       * Display remote TFS branches:\n       git tfs branch -r\n       git tfs branch -r -all\n\n" +
        "       * Create a TFS branch from current commit:\n       git tfs branch $/Repository/ProjectBranchToCreate <myWishedRemoteName> --comment=\"Creation of my branch\"\n\n" +
        "       * Rename a remote branch:\n       git tfs branch --move oldTfsRemoteName newTfsRemoteName\n\n" +
        "       * Delete a remote branch:\n       git tfs branch --delete tfsRemoteName\n       git tfs branch --delete --all\n       git tfs branch --delete --delete-remotes-file=remotes.txt\n\n" +
        "       * Initialise an existing remote TFS branch:\n       git tfs branch --init $/Repository/ProjectBranch\n       git tfs branch --init $/Repository/ProjectBranch myNewBranch\n       git tfs branch --init --all\n       git tfs branch --init --tfs-parent-branch=$/Repository/ProjectParentBranch $/Repository/ProjectBranch\n")]
    [RequiresValidGitRepository]
    public class Branch : GitTfsCommand
    {
        private readonly Globals globalsField;
        private readonly Help helperField;
        private readonly Cleanup cleanupField;
        private readonly InitBranch initBranchField;
        private readonly Rcheckin rcheckinField;
        public bool DisplayRemotes { get; set; }
        public bool ManageAll { get; set; }
        public bool ShouldRenameRemote { get; set; }
        public bool ShouldDeleteRemote { get; set; }
        public string DeleteRemotesFilePath { get; set; }
        public bool ShouldInitBranch { get; set; }
        public string IgnoreRegex { get; set; }
        public string ExceptRegex { get; set; }
        public bool NoFetch { get; set; }
        public string Comment { get; set; }
        public string TfsUsername { get; set; }
        public string TfsPassword { get; set; }

        public OptionSet OptionSet => new OptionSet
                {
                    { "r|remotes", "Display the TFS branches of the current TFS root branch existing on the TFS server", v => DisplayRemotes = (v != null) },
                    { "all", "Display (used with option --remotes) the TFS branches of all the root branches existing on the TFS server\n" +
                    " or Initialize (used with option --init) all existing TFS branches (For TFS 2010 and later)\n" +
                    " or Delete (used with option --delete) all tfs remotes (for example after lfs migration).", v => ManageAll = (v != null) },
                    { "comment=", "Comment used for the creation of the TFS branch ", v => Comment = v },
                    { "m|move", "Rename a TFS remote", v => ShouldRenameRemote = (v != null) },
                    { "delete", "Delete a TFS remote", v => ShouldDeleteRemote = (v != null) },
                    { "delete-remotes-file=", "File with a list of remotes to delete", v => DeleteRemotesFilePath = v },
                    { "init", "Initialize an existing TFS branch", v => ShouldInitBranch = (v != null) },
                    { "ignore-regex=", "A regex of files to ignore", v => IgnoreRegex = v },
                    { "except-regex=", "A regex of exceptions to ignore-regex", v => ExceptRegex = v},
                    { "no-fetch", "Don't fetch changeset for newly initialized branch(es)", v => NoFetch = (v != null) },
                    { "u|username=", "TFS username", v => TfsUsername = v },
                    { "p|password=", "TFS password", v => TfsPassword = v },
                }
                .Merge(globalsField.OptionSet);

        public Branch(Globals globals, Help helper, Cleanup cleanup, InitBranch initBranch, Rcheckin rcheckin)
        {
            globalsField = globals;
            helperField = helper;
            cleanupField = cleanup;
            initBranchField = initBranch;
            rcheckinField = rcheckin;
        }

        public void SetInitBranchParameters()
        {
            initBranchField.TfsUsername = TfsUsername;
            initBranchField.TfsPassword = TfsPassword;
            initBranchField.CloneAllBranches = ManageAll;
            initBranchField.IgnoreRegex = IgnoreRegex;
            initBranchField.ExceptRegex = ExceptRegex;
            initBranchField.NoFetch = NoFetch;
        }

        public bool IsCommandWellUsed() =>
            //Verify that some mutual exclusive options are not used together
            new[] { ShouldDeleteRemote, ShouldInitBranch, ShouldRenameRemote }.Count(b => b) <= 1;

        public int Run()
        {
            if (!IsCommandWellUsed())
                return helperField.Run(this);

            globalsField.WarnOnGitVersion();

            VerifyCloneAllRepository();

            if (ShouldRenameRemote)
                return helperField.Run(this);

            if(ShouldDeleteRemote)
            {
                if (!string.IsNullOrWhiteSpace(DeleteRemotesFilePath))
                    return DeleteRemotesFromFile();
                else if (!ManageAll)
                    return helperField.Run(this);
                else
                    return DeleteAllRemotes();
            }

            if (ShouldInitBranch)
            {
                SetInitBranchParameters();
                return initBranchField.Run();
            }

            return DisplayBranchData();
        }

        public int Run(string param)
        {
            if (!IsCommandWellUsed())
                return helperField.Run(this);

            VerifyCloneAllRepository();

            globalsField.WarnOnGitVersion();

            if (ShouldRenameRemote)
                return helperField.Run(this);

            if (ShouldInitBranch)
            {
                SetInitBranchParameters();
                return initBranchField.Run(param);
            }

            if (ShouldDeleteRemote)
            {
                if (string.IsNullOrWhiteSpace(DeleteRemotesFilePath))
                    return DeleteRemotes(new List<string>() { param });
                else
                    return helperField.Run(this);
            }

            return CreateRemote(param);
        }

        public int Run(string param1, string param2)
        {
            if (!IsCommandWellUsed())
                return helperField.Run(this);

            VerifyCloneAllRepository();

            globalsField.WarnOnGitVersion();

            if (ShouldDeleteRemote)
                return helperField.Run(this);

            if (ShouldInitBranch)
            {
                SetInitBranchParameters();
                return initBranchField.Run(param1, param2);
            }

            if (ShouldRenameRemote)
                return RenameRemote(param1, param2);

            return CreateRemote(param1, param2);
        }

        private void VerifyCloneAllRepository()
        {
            if (!globalsField.Repository.HasRemote(GitTfsConstants.DefaultRepositoryId))
                return;

            if (globalsField.Repository.ReadTfsRemote(GitTfsConstants.DefaultRepositoryId).TfsRepositoryPath == GitTfsConstants.TfsRoot)
                throw new GitTfsException("error: you can't use the 'branch' command when you have cloned the whole repository '$/' !");
        }

        private int RenameRemote(string oldRemoteName, string newRemoteName)
        {
            var newRemoteNameExpected = globalsField.Repository.AssertValidBranchName(newRemoteName.ToGitRefName());
            if (newRemoteNameExpected != newRemoteName)
                Trace.TraceInformation("The name of the branch after renaming will be : " + newRemoteNameExpected);

            if (globalsField.Repository.HasRemote(newRemoteNameExpected))
            {
                throw new GitTfsException("error: this remote name is already used!");
            }

            Trace.TraceInformation("Cleaning before processing rename...");
            cleanupField.Run();

            globalsField.Repository.MoveRemote(oldRemoteName, newRemoteNameExpected);

            if (globalsField.Repository.RenameBranch(oldRemoteName, newRemoteName) == null)
                Trace.TraceWarning("warning: no local branch found to rename");

            return GitTfsExitCodes.OK;
        }

        private int CreateRemote(string tfsPath, string gitBranchNameExpected = null)
        {
            bool checkInCurrentBranch = false;
            tfsPath.AssertValidTfsPath();
            Trace.WriteLine("Getting commit informations...");
            var commit = globalsField.Repository.GetCurrentTfsCommit();
            if (commit == null)
            {
                checkInCurrentBranch = true;
                var parents = globalsField.Repository.GetLastParentTfsCommits(globalsField.Repository.GetCurrentCommit());
                if (!parents.Any())
                    throw new GitTfsException("error : no tfs remote parent found!");
                commit = parents.First();
            }
            var remote = commit.Remote;
            Trace.WriteLine("Creating branch in TFS...");
            remote.Tfs.CreateBranch(remote.TfsRepositoryPath, tfsPath, commit.ChangesetId, Comment ?? "Creation branch " + tfsPath);
            Trace.WriteLine("Init branch in local repository...");
            initBranchField.DontCreateGitBranch = true;
            var returnCode = initBranchField.Run(tfsPath, gitBranchNameExpected);

            if (returnCode != GitTfsExitCodes.OK || !checkInCurrentBranch)
                return returnCode;

            rcheckinField.RebaseOnto(initBranchField.RemoteCreated.RemoteRef, commit.GitCommit);
            globalsField.UserSpecifiedRemoteId = initBranchField.RemoteCreated.Id;
            return rcheckinField.Run();
        }

        private int DeleteRemotes(IEnumerable<string> remoteNames)
        {
            List<IGitTfsRemote> validRemotes = new List<IGitTfsRemote>();
            List<string> inValidRemoteNames = new List<string>();
            foreach (string remoteName in remoteNames)
            {
                IGitTfsRemote remote = globalsField.Repository.ReadTfsRemote(remoteName);
                if (remote != null)
                {
                    validRemotes.Add(remote);
                }
                else
                {
                    inValidRemoteNames.Add(remoteName);
                }
            }
            if (inValidRemoteNames.Count > 0)
            {
                throw new GitTfsException($"Error: Remotes not found: {string.Join(" ", inValidRemoteNames)}");
            }

            Trace.TraceInformation("Cleaning before processing delete...");
            cleanupField.Run();

            foreach (IGitTfsRemote validRemote in validRemotes)
            {
                globalsField.Repository.DeleteTfsRemote(validRemote);
            }

            return GitTfsExitCodes.OK;
        }

        private int DeleteRemotesFromFile() 
        {
            if (string.IsNullOrWhiteSpace(DeleteRemotesFilePath))
            {
                throw new GitTfsException($"{nameof(DeleteRemotesFilePath)} is empty.");
            }
            if (!File.Exists(DeleteRemotesFilePath))
            {
                throw new GitTfsException($"{DeleteRemotesFilePath} does not exist.");
            }
            List<string> remotesToDelete = File.ReadLines(DeleteRemotesFilePath, Encoding.Default)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).ToList();
            Trace.TraceInformation($"Deleting {remotesToDelete.Count} remotes!!");
            return DeleteRemotes(remotesToDelete);
        }

        private int DeleteAllRemotes()
        {
            Trace.TraceInformation("Deleting all remotes!!");
            Trace.TraceInformation("Cleaning before processing delete...");
            cleanupField.Run();

            foreach (var remote in globalsField.Repository.ReadAllTfsRemotes())
            {
                globalsField.Repository.DeleteTfsRemote(remote);
            }
            return GitTfsExitCodes.OK;
        }

        public int DisplayBranchData()
        {
            // should probably pull this from options so that it is settable from the command-line
            const string remoteId = GitTfsConstants.DefaultRepositoryId;

            var tfsRemotes = globalsField.Repository.ReadAllTfsRemotes();
            if (DisplayRemotes)
            {
                if (!ManageAll)
                {
                    var remote = globalsField.Repository.ReadTfsRemote(remoteId);

                    Trace.TraceInformation("\nTFS branch structure:");
                    WriteRemoteTfsBranchStructure(remote.Tfs, remote.TfsRepositoryPath, tfsRemotes);
                    return GitTfsExitCodes.OK;
                }
                else
                {
                    var remote = tfsRemotes.First(r => r.Id == remoteId);
                    foreach (var branch in remote.Tfs.GetBranches().Where(b => b.IsRoot))
                    {
                        var root = remote.Tfs.GetRootTfsBranchForRemotePath(branch.Path);
                        var visitor = new WriteBranchStructureTreeVisitor(remote.TfsRepositoryPath, tfsRemotes);
                        root.AcceptVisitor(visitor);
                    }
                    return GitTfsExitCodes.OK;
                }
            }

            WriteTfsRemoteDetails(tfsRemotes);
            return GitTfsExitCodes.OK;
        }

        public static void WriteRemoteTfsBranchStructure(ITfsHelper tfsHelper, string tfsRepositoryPath, IEnumerable<IGitTfsRemote> tfsRemotes = null)
        {
            var root = tfsHelper.GetRootTfsBranchForRemotePath(tfsRepositoryPath);
            var visitor = new WriteBranchStructureTreeVisitor(tfsRepositoryPath, tfsRemotes);
            root.AcceptVisitor(visitor);
        }

        private void WriteTfsRemoteDetails(IEnumerable<IGitTfsRemote> tfsRemotes)
        {
            Trace.TraceInformation("\nGit-tfs remote details:");
            foreach (var remote in tfsRemotes)
            {
                Trace.TraceInformation("\n {0} -> {1} {2}", remote.Id, remote.TfsUrl, remote.TfsRepositoryPath);
                Trace.TraceInformation("        {0} - {1} @ {2}", remote.RemoteRef, remote.MaxCommitHash, remote.MaxChangesetId);
            }
        }

        private class WriteBranchStructureTreeVisitor : IBranchTreeVisitor
        {
            private readonly string targetPathField;
            private readonly IEnumerable<IGitTfsRemote> tfsRemotesField;

            public WriteBranchStructureTreeVisitor(string targetPath, IEnumerable<IGitTfsRemote> tfsRemotes = null)
            {
                targetPathField = targetPath;
                tfsRemotesField = tfsRemotes;
            }

            public void Visit(BranchTree branch, int level)
            {
                var writer = new StringWriter();
                for (var i = 0; i < level; i++)
                    writer.Write(" | ");

                writer.WriteLine();

                for (var i = 0; i < level - 1; i++)
                    writer.Write(" | ");

                if (level > 0)
                    writer.Write(" +-");

                writer.Write(" {0}", branch.Path);

                if (tfsRemotesField != null)
                {
                    var remote = tfsRemotesField.FirstOrDefault(r => r.TfsRepositoryPath == branch.Path);
                    if (remote != null)
                        writer.Write(" -> " + remote.Id);
                }

                if (branch.Path.Equals(targetPathField))
                    writer.Write(" [*]");

                Trace.TraceInformation(writer.ToString());
            }
        }
    }
}
