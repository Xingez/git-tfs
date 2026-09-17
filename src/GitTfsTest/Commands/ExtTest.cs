
namespace GitTfs.Test.Commands
{
    using global::GitTfs.Commands;
    using global::GitTfs.Core;
    [TestClass]
    public class ExtTest : BaseTest
    {
        [TestMethod]
        public void AssertValidTfsPathTest()
        {
            "$/test".AssertValidTfsPath();
            Assert.Throws<GitTfsException>(() => "$test".AssertValidTfsPath());
            Assert.Throws<GitTfsException>(() => "/test".AssertValidTfsPath());
            Assert.Throws<GitTfsException>(() => "test".AssertValidTfsPath());
            Assert.Throws<GitTfsException>(() => "$/".AssertValidTfsPath());
            "$/".AssertValidTfsPathOrRoot();
        }

        [TestMethod]
        public void ToGitRefNameTest()
        {
            Assert.Equal("test", "test".ToGitRefName());
            Assert.Equal("test", "te^st".ToGitRefName());
            Assert.Equal("test", "te~st".ToGitRefName());
            Assert.Equal("test", "te st".ToGitRefName());
            Assert.Equal("test", "te:st".ToGitRefName());
            Assert.Equal("test", "te*st".ToGitRefName());
            Assert.Equal("test", "te?st".ToGitRefName());
            Assert.Equal("test", "te[st".ToGitRefName());
            Assert.Equal("test", "test/".ToGitRefName());
            Assert.Equal("test", "test.".ToGitRefName());
            Assert.Equal("test", "te..st".ToGitRefName());
            Assert.Equal("test", "test.".ToGitRefName());
            Assert.Equal("test", "te\\st.".ToGitRefName());
            Assert.Equal("test", "te@{st.".ToGitRefName());
            Assert.Equal("test", "/test././".ToGitRefName());
            Assert.Equal("bugs/nameOfTheBug", "/bu$gs/name:OfTheBug".ToGitRefName());
            Assert.Equal("repo/test/test2", "$/repo/te:st/test2".ToGitRefName());
        }

        [TestMethod]
        public void GetAGitBranchNameFromTfsRepositoryPath()
        {
            Assert.Equal("test", "test".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te^st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te~st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te:st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te*st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te?st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te[st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "test/".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "test.".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "$/repo/te:st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test/test2", "$/repo/te:st/test2".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te..st".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "test.".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te\\st.".ToGitBranchNameFromTfsRepositoryPath());
            Assert.Equal("test", "te@{st.".ToGitBranchNameFromTfsRepositoryPath());
        }
    }
}
