

namespace GitTfs.Test.Integration
{
    using global::GitTfs.Core.TfsInterop;
    [TestClass]
    public class InitTests : BaseTest, IDisposable
    {
        private readonly IntegrationHelper h;

        public InitTests()
        {
            h = new IntegrationHelper();
            Console.WriteLine("Repository in folder: " + h.Workdir);
        }

        public void Dispose() => h.Dispose();

        [TestMethod]
        public void InitializesConfig()
        {
            h.SetupFake(r => { });
            h.Run("init", "http://my-tfs.local/tfs", "$/MyProject", "MyProject");
            h.AssertConfig("MyProject", "tfs-remote.default.url", "http://my-tfs.local/tfs");
            h.AssertConfig("MyProject", "tfs-remote.default.repository", "$/MyProject");
        }

        [TestMethod]
        public void CanUseThatConfig()
        {
            h.SetupFake(r =>
            {
                r.Changeset(1, "Project created from template", DateTime.Parse("2012-01-01 12:12:12 -05:00"))
                    .Change(TfsChangeType.Add, TfsItemType.Folder, "$/MyProject");
            });
            h.Run("init", "http://my-tfs.local/tfs", "$/MyProject", "MyProject");
            h.SetConfig("MyProject", "tfs-remote.default.autotag", "true");
            h.RunIn("MyProject", "fetch");
            var expectedSha = "f8b247c3298f4189c6c9ff701f147af6e1428f97";
            h.AssertRef("MyProject", "refs/remotes/tfs/default", expectedSha);
            h.AssertRef("MyProject", "refs/tags/tfs/default/C1", expectedSha);
        }

        [TestMethod]
        public void InitializesConfigUsingNoParallel()
        {
            h.SetupFake(r => { });
            h.Run("init", "http://my-tfs.local/tfs", "$/MyProject", "MyProject", "--no-parallel");
            h.AssertConfig("MyProject", "tfs-remote.default.noparallel", "true");
        }

        [TestMethod]
        public void InitializesWithInitialBranchArg()
        {
            h.SetupFake(r => { });
            h.Run("init", "http://my-tfs.local/tfs", "$/MyProject", "MyProject", "--initial-branch=customInitialBranch");
            h.AssertHead("MyProject", "refs/heads/customInitialBranch");
        }

        [TestMethod]
        public void InitializesWithGitignore()
        {
            // Tests both:
            //   1. No extraneous "master" branch is introduced when gitconfig calls for "main" as the initial branch
            //   2. The initial .gitignore commit is on both the main branch and the tfs remote so they have common history

            string gitignoreFile = Path.Combine(h.Workdir, "gitignore");
            string gitignoreContent = "*.exe\r\n*.com\r\n";
            File.WriteAllText(gitignoreFile, gitignoreContent);

            h.SetupFake(r => { });
            h.RunInWithConfig(".", "GitTfs.Test.Integration.GlobalConfigs.mainDefaultBranch.gitconfig", "init", "http://my-tfs.local/tfs", "$/MyProject", "MyProject", $"--gitignore={gitignoreFile}");
            h.AssertNoRef("MyProject", "refs/heads/master");
            h.AssertRef("MyProject", "refs/heads/main", "077fd68c084ef718a505f0a7375330c68d699f40");
            h.AssertRef("MyProject", "refs/remotes/tfs/default", "077fd68c084ef718a505f0a7375330c68d699f40");
        }
    }
}
