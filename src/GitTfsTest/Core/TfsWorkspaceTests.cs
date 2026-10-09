namespace GitTfs.Test.Core
{
    using GitTfs.Core;
    using GitTfs.Core.TfsInterop;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class TfsWorkspaceTests
    {
        [TestMethod]
        public void Get_fetches_the_requested_changeset()
        {
            var workspace = new Mock<IWorkspace>();
            var sut = CreateWorkspace(workspace.Object);

            sut.Get(42);

            workspace.Verify(item => item.GetSpecificVersion(42), Times.Once);
        }

        [TestMethod]
        public void Get_changes_uses_sequential_downloads()
        {
            var workspace = new Mock<IWorkspace>();
            var changes = new[] { new Mock<IChange>().Object };
            var sut = CreateWorkspace(workspace.Object);

            sut.Get(42, changes);

            workspace.Verify(item => item.GetSpecificVersion(42, changes, true), Times.Once);
        }

        [TestMethod]
        public void Get_empty_changes_does_not_request_a_download()
        {
            var workspace = new Mock<IWorkspace>();
            var sut = CreateWorkspace(workspace.Object);

            sut.Get(42, Enumerable.Empty<IChange>());

            workspace.Verify(item => item.GetSpecificVersion(
                It.IsAny<int>(), It.IsAny<IEnumerable<IChange>>(), It.IsAny<bool>()), Times.Never);
        }

        [TestMethod]
        public void Get_local_path_is_relative_to_the_workspace_directory()
        {
            var sut = CreateWorkspace(Mock.Of<IWorkspace>(), "workspace");

            Assert.AreEqual(Path.Combine("workspace", "src", "file.cs"), sut.GetLocalPath(Path.Combine("src", "file.cs")));
        }

        private static TfsWorkspace CreateWorkspace(IWorkspace workspace, string localDirectory = "workspace")
        {
            var repository = new Mock<IGitRepository>();
            var remote = new Mock<IGitTfsRemote>();
            remote.SetupGet(item => item.Repository).Returns(repository.Object);
            return new TfsWorkspace(workspace, localDirectory, remote.Object);
        }
    }
}
