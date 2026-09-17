

namespace GitTfs.Test.Core
{
    using global::GitTfs.Commands;
    using global::GitTfs.Core;
    using global::GitTfs.Core.TfsInterop;

    using global::Moq;
    [TestClass]
    public class GlobalsTests : BaseTest
    {
        private readonly Globals globalsField;
        private readonly Mock<IGitRepository> gitRepositoryMockField;
        private readonly ITfsHelper tfsHelperField;

        public GlobalsTests()
        {
            gitRepositoryMockField = new Mock<IGitRepository>();
            globalsField = new Globals { Bootstrapper = null, Repository = gitRepositoryMockField.Object };
            tfsHelperField = new Mock<ITfsHelper>().Object;
        }

        [TestMethod]
        public void WhenUserSpecifyARemote_ThenReturnIt()
        {
            globalsField.UserSpecifiedRemoteId = "IWantThatRemote";

            Assert.Equal("IWantThatRemote", globalsField.RemoteId);
        }

        [TestMethod]
        public void WhenOnlyOneRemoteFoundInParentCommits_ThenReturnIt()
        {
            gitRepositoryMockField.Setup(r => r.GetLastParentTfsCommits("HEAD"))
                   .Returns(new List<TfsChangesetInfo>()
                       {
                           new TfsChangesetInfo()
                               {
                                   ChangesetId = 34,
                                   Remote = new GitTfsRemote(new RemoteInfo() {Id = "myRemote"}, gitRepositoryMockField.Object, new RemoteOptions(), globalsField, tfsHelperField, new ConfigProperties(null))
                               }
                       });


            Assert.Equal("myRemote", globalsField.RemoteId);
        }

        [TestMethod]
        public void WhenTwoRemotesFoundInParentCommits_ThenReturnTheFirst()
        {
            gitRepositoryMockField.Setup(r => r.GetLastParentTfsCommits("HEAD"))
                   .Returns(new List<TfsChangesetInfo>()
                       {
                           new TfsChangesetInfo()
                               {
                                   ChangesetId = 34,
                                   Remote = new GitTfsRemote(new RemoteInfo() {Id = "mainRemote"}, gitRepositoryMockField.Object, new RemoteOptions(), globalsField, tfsHelperField, new ConfigProperties(null))
                               },
                               new TfsChangesetInfo()
                               {
                                   ChangesetId = 34,
                                   Remote = new GitTfsRemote(new RemoteInfo() {Id = "myRemote"}, gitRepositoryMockField.Object, new RemoteOptions(), globalsField, tfsHelperField, new ConfigProperties(null))
                               },
                       });

            Assert.Equal("mainRemote", globalsField.RemoteId);
        }

        [TestMethod]
        public void WhenNoRemotesFoundInParentCommits_AndNoRemotesInRepository_ThenReturnDefaultOne()
        {
            gitRepositoryMockField.Setup(r => r.GetLastParentTfsCommits("HEAD"))
                   .Returns(new List<TfsChangesetInfo>());
            gitRepositoryMockField.Setup(r => r.ReadAllTfsRemotes())
                   .Returns(new List<GitTfsRemote>());
            Assert.Equal("default", globalsField.RemoteId);
        }

        [TestMethod]
        public void WhenNoRemotesFoundInParentCommits_ThereIsOnlyOneRemoteInRepository_AndThisIsTheDefaultOne_ThenReturnIt()
        {
            gitRepositoryMockField.Setup(r => r.GetLastParentTfsCommits("HEAD"))
                   .Returns(new List<TfsChangesetInfo>());
            gitRepositoryMockField.Setup(r => r.ReadAllTfsRemotes())
                   .Returns(new List<GitTfsRemote>() { new GitTfsRemote(new RemoteInfo() { Id = "default" }, gitRepositoryMockField.Object, new RemoteOptions(), globalsField, tfsHelperField, new ConfigProperties(null)) });
            Assert.Equal("default", globalsField.RemoteId);
        }

        [TestMethod]
        public void WhenNoRemotesFoundInParentCommits_AndThereIsOnlyOneRemoteInRepository_ThenThrowAnException()
        {
            gitRepositoryMockField.Setup(r => r.GetLastParentTfsCommits("HEAD"))
                   .Returns(new List<TfsChangesetInfo>());
            gitRepositoryMockField.Setup(r => r.ReadAllTfsRemotes())
                   .Returns(new List<GitTfsRemote>() { new GitTfsRemote(new RemoteInfo() { Id = "myRemote" }, gitRepositoryMockField.Object, new RemoteOptions(), globalsField, tfsHelperField, new ConfigProperties(null)) });
            Assert.Throws<GitTfsException>(() => globalsField.RemoteId);
        }

        [TestMethod]
        public void WhenNoRemotesFoundInParentCommits_AndThereIsARemoteInRepository_ThenThrowAnException()
        {
            gitRepositoryMockField.Setup(r => r.GetLastParentTfsCommits("HEAD"))
                   .Returns(new List<TfsChangesetInfo>());
            gitRepositoryMockField.Setup(r => r.ReadAllTfsRemotes())
                   .Returns(new List<GitTfsRemote>()
                       {
                           new GitTfsRemote(new RemoteInfo() { Id = "myRemote" }, gitRepositoryMockField.Object, new RemoteOptions(), globalsField, tfsHelperField, new ConfigProperties(null)),
                           new GitTfsRemote(new RemoteInfo() { Id = "myRemote2" }, gitRepositoryMockField.Object, new RemoteOptions(), globalsField, tfsHelperField, new ConfigProperties(null))
                       });
            Assert.Throws<GitTfsException>(() => globalsField.RemoteId);
        }
    }
}