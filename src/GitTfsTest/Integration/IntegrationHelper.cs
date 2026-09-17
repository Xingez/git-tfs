
namespace GitTfs.Test.Integration
{
    using global::System.Text;
    using global::LibGit2Sharp;
    using global::GitTfs.Core;
    using global::GitTfs.Core.TfsInterop;
    using global::GitTfs.VsFake;
    internal class IntegrationHelper : IDisposable
    {
        #region manage the work directory

        private string workdirField;

        public string Workdir
        {
            get
            {
                if (workdirField == null)
                {
                    workdirField = Path.GetTempFileName();
                    File.Delete(workdirField);
                    Directory.CreateDirectory(workdirField);
                }
                return workdirField;
            }
        }

        public void Dispose()
        {
            while (!repositoriesField.Empty())
            {
                var repo = repositoriesField.First();
                repo.Value.Dispose();
                repositoriesField.Remove(repo.Key);
            }
            if (workdirField != null)
            {
                try
                {
                    Directory.Delete(workdirField);
                    workdirField = null;
                }
                catch (Exception)
                {
                }
            }
        }

        private readonly Dictionary<string, Repository> repositoriesField = new Dictionary<string, Repository>();
        public Repository Repository(string path)
        {
            path = Path.Combine(Workdir, path);
            if (!repositoriesField.ContainsKey(path))
                repositoriesField.Add(path, new Repository(path));
            return repositoriesField[path];
        }

        #endregion

        #region set up a git repository

        public void SetConfig<T>(string repodir, string key, T value) => Repository(repodir).Config.Set(key, value);

        public T GetConfig<T>(string repodir, string key) => Repository(repodir).Config.Get<T>(key, ConfigurationLevel.Local).Value;

        public void SetupGitRepo(string path, Action<RepoBuilder> buildIt)
        {
            var fullPath = Path.Combine(Workdir, path);
            Console.WriteLine("Repository path:" + fullPath);
            var repoPath = LibGit2Sharp.Repository.Init(fullPath);
            using (var repo = new Repository(repoPath))
                buildIt(new RepoBuilder(repo));
        }

        public class RepoBuilder
        {
            private readonly Repository repoField;

            public RepoBuilder(Repository repo)
            {
                repoField = repo;
            }

            private Signature GetCommitter() => new Signature("Test User", "test@example.com", new DateTimeOffset(DateTime.Now));

            public string Commit(string message, string filename = "README.txt")
            {
                File.WriteAllText(Path.Combine(repoField.Info.WorkingDirectory, filename), message);
                LibGit2Sharp.Commands.Stage(repoField, filename);
                var committer = GetCommitter();
                return repoField.Commit(message, committer, committer, new CommitOptions() { AllowEmptyCommit = true }).Id.Sha;
            }

            public void CreateBranch(string branchName) => LibGit2Sharp.Commands.Checkout(repoField, repoField.CreateBranch(branchName));

            public void Checkout(string commitishName) => LibGit2Sharp.Commands.Checkout(repoField, commitishName);

            public string Merge(string branch)
            {
                var mergeResult = repoField.Merge(repoField.Branches[branch].Commits.First(), GetCommitter());
                return mergeResult.Commit.Sha;
            }

            public string Amend(string message)
            {
                var committer = GetCommitter();
                return repoField.Commit(message, committer, committer, new CommitOptions() { AmendPreviousCommit = true }).Id.Sha;
            }
        }

        #endregion

        #region set up vsfake script

        public string FakeScript => Path.Combine(Workdir, "_fakescript");

        public void SetupFake(Action<FakeHistoryBuilder> scripter) => new Script().Tap(script => scripter(new FakeHistoryBuilder(script))).Save(FakeScript);

        public class FakeHistoryBuilder
        {
            private readonly Script scriptField;
            public FakeHistoryBuilder(Script script)
            {
                scriptField = script;
            }

            public string FakeCommiter;
            public FakeChangesetBuilder Changeset(int changesetId, string message, DateTime checkinDate)
            {
                var changeset = new ScriptedChangeset
                {
                    Id = changesetId,
                    Comment = message,
                    CheckinDate = checkinDate,
                    IsBranchChangeset = false,
                    IsMergeChangeset = false,
                    Committer = FakeCommiter,
                };
                scriptField.Changesets.Add(changeset);
                return new FakeChangesetBuilder(changeset);
            }

            public FakeChangesetBuilder BranchChangeset(int changesetId, string message, DateTime checkinDate, string fromBranch, string toBranch, int rootChangesetId)
            {
                var branchChangeset = new ScriptedChangeset
                {
                    Id = changesetId,
                    Comment = message,
                    CheckinDate = checkinDate,
                    IsBranchChangeset = true,
                    IsMergeChangeset = false,
                    Committer = FakeCommiter,
                    BranchChangesetDatas = new BranchChangesetDatas
                    {
                        RootChangesetId = rootChangesetId,
                        BranchPath = toBranch,
                        ParentBranch = fromBranch
                    }
                };
                scriptField.Changesets.Add(branchChangeset);
                return new FakeChangesetBuilder(branchChangeset);
            }

            public FakeChangesetBuilder MergeChangeset(int changesetId, string message, DateTime checkinDate, string fromBranch, string intoBranch, int lastChangesetId)
            {
                var mergeChangeset = new ScriptedChangeset
                {
                    Id = changesetId,
                    Comment = message,
                    CheckinDate = checkinDate,
                    IsBranchChangeset = false,
                    IsMergeChangeset = true,
                    Committer = FakeCommiter,
                    MergeChangesetDatas = new MergeChangesetDatas
                    {
                        BeforeMergeChangesetId = lastChangesetId,
                        BranchPath = fromBranch,
                        MergeIntoBranch = intoBranch
                    }
                };
                scriptField.Changesets.Add(mergeChangeset);
                return new FakeChangesetBuilder(mergeChangeset);
            }

            public void SetRootBranch(string rootBranchPath) => scriptField.RootBranches.Add(new ScriptedRootBranch() { BranchPath = rootBranchPath });
        }

        public class FakeChangesetBuilder
        {
            private readonly ScriptedChangeset changesetField;

            public FakeChangesetBuilder(ScriptedChangeset changeset)
            {
                changesetField = changeset;
            }

            public FakeChangesetBuilder Change(TfsChangeType changeType, TfsItemType itemType, string tfsPath, string contents, int? itemId = null) => Change(changeType, itemType, tfsPath, Encoding.UTF8.GetBytes(contents), itemId);

            public FakeChangesetBuilder Change(TfsChangeType changeType, TfsItemType itemType, string tfsPath, byte[] contents = null, int? itemId = null)
            {
                changesetField.Changes.Add(new ScriptedChange
                {
                    ChangeType = changeType,
                    ItemType = itemType,
                    RepositoryPath = tfsPath,
                    Content = contents,
                    ItemId = itemId
                });
                return this;
            }
        }

        #endregion

        #region run git-tfs

        private string tfsUrlField = "http://does/not/matter";
        public string TfsUrl { get => tfsUrlField; set => tfsUrlField = value; }

        public int Run(params string[] args) => RunIn(".", args);

        public int RunIn(string workPath, params string[] args) => RunInWithConfig(workPath, "GitTfs.Test.Integration.GlobalConfigs.standard.gitconfig", args);

        public int RunInWithConfig(string workPath, string configResource, params string[] args)
        {
            var origPwd = Environment.CurrentDirectory;
            var origClient = Environment.GetEnvironmentVariable("GIT_TFS_CLIENT");
            var origScript = Environment.GetEnvironmentVariable(Script.EnvVar);
            var origNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
            var origGlobalConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
            var origLibGitGlobalPaths = GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.Global).ToArray();

            try
            {
                string testDirectory = Path.Combine(Workdir, workPath);
                string globalConfigPath = Path.Combine(testDirectory, ".gitconfig");
                WriteResourceToFile(configResource, globalConfigPath);

                Environment.CurrentDirectory = testDirectory;
                Environment.SetEnvironmentVariable("GIT_TFS_CLIENT", "Fake");
                Environment.SetEnvironmentVariable(Script.EnvVar, FakeScript);
                Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "true");
                Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", globalConfigPath);
                GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, testDirectory);

                Console.WriteLine(">> git tfs " + QuoteArgs(args));
                var argsWithDebug = new List<string>();
                if (BaseTest.DisplayTrace)
                {
                    argsWithDebug.Add("--debug");
                }
                argsWithDebug.AddRange(args);
                return Program.MainCore(argsWithDebug.ToArray());
            }
            finally
            {
                Environment.SetEnvironmentVariable("GIT_TFS_CLIENT", origClient);
                Environment.SetEnvironmentVariable(Script.EnvVar, origScript);
                Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", origNoSystem);
                Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", origGlobalConfig);
                GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, origLibGitGlobalPaths);
                Environment.CurrentDirectory = origPwd;
            }
        }

        private string QuoteArgs(string[] args) => string.Join(" ", args.Select(arg => QuoteArg(arg)).ToArray());

        private string QuoteArg(string arg)
        {
            // This is not complete, but it is adequate for these tests.
            if (arg.Contains(' '))
                return '"' + arg + '"';
            return arg;
        }

        public void ChangeConfigSetting(string repodir, string key, string value)
        {
            var repo = new Repository(Path.Combine(Workdir, repodir));
            repo.Config.Set(key, value);
        }

        #endregion

        #region assertions

        public int GetCommitCount(string repodir) => Repository(repodir).Commits.Count();

        public void AssertGitRepo(string repodir)
        {
            var path = Path.Combine(Workdir, repodir);
            Assert.True(Directory.Exists(path), path + " should be a directory");
            Assert.True(Directory.Exists(Path.Combine(path, ".git")), path + " should have a .git dir inside of it");
        }

        public void AssertNoRef(string repodir, string gitref) => AssertEqual(null, RevParse(repodir, gitref), "Expected no ref " + gitref);

        public void AssertRef(string repodir, string gitref, string expectedSha)
        {
            Assert.NotNull(expectedSha);
            AssertEqual(expectedSha, RevParse(repodir, gitref), "Expected " + gitref + " to be " + expectedSha);
        }

        public void AssertTree(string repodir, string gitref, string expectedTreeSha)
        {
            var commit = Repository(repodir).Lookup<Commit>(gitref);
            Assert.NotNull(commit);
            AssertEqual(expectedTreeSha, commit.Tree.Sha, "Expected tree at " + gitref + " to be " + expectedTreeSha);
        }

        public Commit RevParseCommit(string repodir, string gitref) => Repository(repodir).Lookup<Commit>(gitref);

        private string RevParse(string repodir, string gitref)
        {
            var parsed = RevParseCommit(repodir, gitref);
            return parsed == null ? null : parsed.Sha;
        }

        public void AssertEmptyWorkspace(string repodir)
        {
            var entries = new List<string>(Directory.GetFileSystemEntries(Path.Combine(Workdir, repodir)));
            entries = entries.Where(f => Path.GetFileName(f) != ".git").ToList();
            AssertEqual(new List<string>(), entries, "entries in " + repodir);
        }

        public void AssertCleanWorkspace(string repodir)
        {
            var status = Repository(repodir).RetrieveStatus();
            AssertEqual(new List<string>(), status.Select(statusEntry => "" + statusEntry.State + ": " + statusEntry.FilePath).ToList(), "repo status");
        }

        public void AssertFileInWorkspace(string repodir, string file, string contents)
        {
            var path = Path.Combine(Workdir, repodir, file);
            var actual = File.ReadAllText(path, Encoding.UTF8);
            AssertEqual(contents, actual, "Contents of " + path);
        }

        public void AssertFileInIndex(string repodir, string file, string contents)
        {
            var repo = Repository(repodir);
            var indexEntry = repo.Index.FirstOrDefault(x => x.Path == file);
            var blob = repo.Lookup<Blob>(indexEntry.Id);
            var actual = blob.GetContentText();
            Assert.Equal(contents, actual);
        }

        public void AssertNoFileInWorkspace(string repodir, string file)
        {
            var path = Path.Combine(Workdir, repodir, file);
            Assert.False(File.Exists(path), "Expect " + file + " to be absent from " + repodir);
        }

        public void AssertTreeEntries(string repodir, string treeish, params string[] expectedPaths)
        {
            var obj = Repository(repodir).Lookup(treeish);
            var tree = obj is Tree ? (Tree)obj : ((Commit)obj).Tree;
            var entries = tree.Select(entry => entry.Path);
            Assert.Equal(expectedPaths.OrderBy(s => s), entries.OrderBy(s => s));
        }

        public void AssertCommitMessage(string repodir, string commitish, params string[] expectedMessageLines)
        {
            var commit = Repository(repodir).Lookup<Commit>(commitish);
            AssertEqual(expectedMessageLines, Lines(commit.Message), "Commit message of " + commitish);
        }

        private static string[] Lines(string s)
        {
            var result = new List<string>();
            var reader = new StringReader(s);
            while (true)
            {
                var line = reader.ReadLine();
                if (line == null)
                    break;
                result.Add(line);
            }
            return result.ToArray();
        }

        public void AssertConfig<T>(string repodir, string key, T expectedValue)
        {
            var config = Repository(repodir).Config.Get<T>(key);
            Assert.NotNull(config);
            Assert.Equal(expectedValue, config.Value);
        }

        public void AssertHead(string repodir, string headRef) => Assert.Equal(headRef, Repository(repodir).Head.CanonicalName);

        private void AssertEqual<T>(T expected, T actual, string message) => Assert.Equal(expected, actual, message);

        private void WriteResourceToFile(string resourceName, string fileName)
        {
            using (var resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var file = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                {
                    resource.CopyTo(file);
                }
            }
        }

        #endregion
    }
}
