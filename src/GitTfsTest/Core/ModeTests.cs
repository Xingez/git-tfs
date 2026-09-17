using GitTfs.Core;

namespace GitTfs.Test.Core
{
    [TestClass]
    public class ModeTests : BaseTest
    {
        [TestMethod]
        public void ShouldGetNewFileMode() => Assert.Equal("100644", Mode.NewFile);

        [TestMethod]
        public void ShouldFormatDirectoryFileMode() => Assert.Equal("040000", LibGit2Sharp.Mode.Directory.ToModeString());

        [TestMethod]
        public void ShouldDetectGitLink() => Assert.Equal(LibGit2Sharp.Mode.GitLink, "160000".ToFileMode());

        [TestMethod]
        public void ShouldDetectGitLinkWithEqualityBackwards() =>
            Assert.Equal("160000".ToFileMode(), actual: LibGit2Sharp.Mode.GitLink);
    }
}
