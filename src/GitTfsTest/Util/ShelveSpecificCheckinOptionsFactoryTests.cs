
namespace GitTfs.Test.Util
{
    using global::GitTfs.Commands;
    using global::GitTfs.Core;
    using global::GitTfs.Util;
    using global::GitTfs.Test;
    [TestClass]
    public class ShelveSpecificCheckinOptionsFactoryTests
    {
        private readonly MoqAutoMocker<CheckinOptionsFactory> mocks;

        public ShelveSpecificCheckinOptionsFactoryTests()
        {
            mocks = new MoqAutoMocker<CheckinOptionsFactory>();
            mocks.Get<Globals>().Repository = mocks.Get<IGitRepository>();
        }

        [TestMethod]
        public void Adds_work_item_to_associate_and_removes_checkin_command_comment()
        {
            string commitMessage = @"test message

		formatted git commit message

		git-tfs-work-item: 1234 associate";

            string expectedCheckinComment = @"test message

		formatted git commit message

		";

            var specificCheckinOptions = GetCheckinOptionsFactory().BuildShelveSetSpecificCheckinOptions(new CheckinOptions(), commitMessage);

            Assert.Single(specificCheckinOptions.WorkItemsToAssociate);
            Assert.Contains("1234", specificCheckinOptions.WorkItemsToAssociate);
            Assert.Equal(expectedCheckinComment, specificCheckinOptions.CheckinComment);
        }

        private CheckinOptionsFactory GetCheckinOptionsFactory() => new CheckinOptionsFactory(mocks.Get<Globals>());
    }
}
