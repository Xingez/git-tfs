
namespace GitTfs.Test.Commands
{
    using global::GitTfs.Commands;
    using global::GitTfs.Core;
    using global::Moq;
    using global::GitTfs.Test;
    [TestClass]
    public class ShelveDeleteTest : BaseTest
    {
        private readonly MoqAutoMocker<ShelveDelete> mocksField;

        public ShelveDeleteTest()
        {
            mocksField = new MoqAutoMocker<ShelveDelete>();
        }

        private void InitMocks4Tests(out Mock<IGitRepository> gitRepositoryMock, out Mock<IGitTfsRemote> remoteMock)
        {
            // mock git repository
            gitRepositoryMock = new Mock<IGitRepository>();
            gitRepositoryMock.Setup(r => r.HasRemote(It.IsAny<string>())).Returns(true);
            mocksField.Get<Globals>().Repository = gitRepositoryMock.Object;

            // mock tfs remote
            mocksField.Get<Globals>().UserSpecifiedRemoteId = "default";
            remoteMock = new Mock<IGitTfsRemote>();
            gitRepositoryMock.Setup(r => r.ReadTfsRemote(It.IsAny<string>())).Returns(remoteMock.Object);
        }

        [TestMethod]
        public void ShouldFailIfNoShelvesetNameProvided()
        {
            const string SHELVESET_NAME = "";

            Assert.NotEqual(GitTfsExitCodes.OK, mocksField.ClassUnderTest.Run(SHELVESET_NAME));
        }

        [TestMethod]
        public void ShouldFailIfInvalidShelvesetNameProvided()
        {
            const string NONEXISTENT_SHELVESET_NAME = "no-such-shelveset";

            InitMocks4Tests(out _, out var remote);
            remote.Setup(r => r.HasShelveset(NONEXISTENT_SHELVESET_NAME)).Returns(false);

            Assert.NotEqual(GitTfsExitCodes.OK, mocksField.ClassUnderTest.Run(NONEXISTENT_SHELVESET_NAME));
        }

        [TestMethod]
        public void ShouldTellRemoteToDeleteShelveset()
        {
            const string SHELVESET_NAME = "Shelveset name";
            InitMocks4Tests(out var repository, out var remote);
            remote.Setup(r => r.HasShelveset(It.IsAny<string>())).Returns(true);

            mocksField.ClassUnderTest.Run(SHELVESET_NAME);

            remote.Verify(r => r.DeleteShelveset(SHELVESET_NAME), Times.Once);
        }
    }
}
