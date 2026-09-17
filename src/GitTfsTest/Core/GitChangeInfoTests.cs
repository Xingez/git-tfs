using GitTfs.Core;
using GitTfs.Core.Changes.Git;

using StructureMap;


namespace GitTfs.Test.Core
{
    [TestClass]
    public class GitChangeInfoTests : BaseTest
    {
        [TestMethod]
        public void GetsMode()
        {
            var line = ":000000 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab M\0blah\0";
            var info = GitChangeInfo.Parse(line);
            Assert.Equal("100644", info.NewMode.ToModeString());
        }

        [TestMethod]
        public void GetsLinkMode()
        {
            var line = ":000000 160000 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab M\0blah\0";
            var info = GitChangeInfo.Parse(line);
            Assert.Equal(LibGit2Sharp.Mode.GitLink, info.NewMode);
        }

        [TestMethod]
        public void GetsChangeType()
        {
            var line = ":000000 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab M\0blah\0";
            var info = GitChangeInfo.Parse(line);
            Assert.Equal("M", info.Status);
        }

        [TestMethod]
        public void GetsChangeTypeWhenScoreIsPresent()
        {
            var line = ":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0blah\0newblah\0";
            var info = GitChangeInfo.Parse(line);
            Assert.Equal("R", info.Status);
        }

        [TestMethod]
        public void GetsPath()
        {
            var line = ":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0Foo\0Bar\0";
            var info = GitChangeInfo.Parse(line);
            Assert.Equal("Foo", info.path);
        }

        [TestMethod]
        public void GetsPathTo()
        {
            var line = ":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0Foo\0Bar\0";
            var info = GitChangeInfo.Parse(line);
            Assert.Equal("Bar", info.pathTo);
        }

        private IGitChangedFile GetChangeItem(string diffTreeLine)
        {
            // This method is similar to BuildGitChangedFile in GitRepository.
            var container = new Container(x => { Program.AddGitChangeTypes(x); });
            return GitChangeInfo.Parse(diffTreeLine).ToGitChangedFile(container.With((IGitRepository)null));
        }

        [TestMethod]
        public void GetsInstanceOfAdd()
        {
            var change = GetChangeItem(":000000 100644 0000000000000000000000000000000000000000 01234567ab01234567ab01234567ab01234567ab A\0blah\0");
            Assert.IsType<Add>(change);
        }

        [TestMethod]
        public void GetsInstanceOfAddWithNewMode()
        {
            var change = (Add)GetChangeItem(":000000 100644 0000000000000000000000000000000000000000 01234567ab01234567ab01234567ab01234567ab A\0blah\0");
            Assert.Equal("01234567ab01234567ab01234567ab01234567ab", change.NewSha);
        }

        [TestMethod]
        public void GetsInstanceOfAddWithNewPath()
        {
            var change = (Add)GetChangeItem(":000000 100644 0000000000000000000000000000000000000000 01234567ab01234567ab01234567ab01234567ab A\0blah\0");
            Assert.Equal("blah", change.Path);
        }

        [TestMethod]
        public void GetsInstanceOfCopy()
        {
            var change = GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab C100\0oldname\0newname\0");
            Assert.IsType<Copy>(change);
        }

        [TestMethod]
        public void GetsInstanceOfAddForCopyWithPath()
        {
            var change = (Copy)GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab C100\0oldname\0newname\0");
            Assert.Equal("newname", change.Path);
        }

        [TestMethod]
        public void GetsInstanceOfModify()
        {
            var change = GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab M\0blah\0");
            Assert.IsType<Modify>(change);
        }

        [TestMethod]
        public void GetsInstanceOfModifyWithPath()
        {
            var change = (Modify)GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab M\0blah\0");
            Assert.Equal("blah", change.Path);
        }

        [TestMethod]
        public void GetsInstanceOfModifyWithNewSha()
        {
            var change = (Modify)GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab M\0blah\0");
            Assert.Equal("01234567ab01234567ab01234567ab01234567ab", change.NewSha);
        }

        [TestMethod]
        public void GetsInstanceOfDelete()
        {
            var change = GetChangeItem(":100644 000000 abcdef0123abcdef0123abcdef0123abcdef0123 0000000000000000000000000000000000000000 D\0blah\0");
            Assert.IsType<Delete>(change);
        }

        [TestMethod]
        public void GetsInstanceOfDeleteWithPath()
        {
            var change = (Delete)GetChangeItem(":100644 000000 abcdef0123abcdef0123abcdef0123abcdef0123 0000000000000000000000000000000000000000 D\0blah\0");
            Assert.Equal("blah", change.Path);
        }

        [TestMethod]
        public void GetsInstanceOfRenameEdit()
        {
            var change = GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0blah\0newblah\0");
            Assert.IsType<RenameEdit>(change);
        }

        [TestMethod]
        public void GetsInstanceOfRenameEditWithPath()
        {
            var change = (RenameEdit)GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0blah\0newblah\0");
            Assert.Equal("blah", change.Path);
        }

        [TestMethod]
        public void GetsInstanceOfRenameEditWithPathTo()
        {
            var change = (RenameEdit)GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0blah\0newblah\0");
            Assert.Equal("newblah", change.PathTo);
        }

        [TestMethod]
        public void GetsInstanceOfRenameEditWithNewSha()
        {
            var change = (RenameEdit)GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0blah\0newblah\0");
            Assert.Equal("01234567ab01234567ab01234567ab01234567ab", change.NewSha);
        }

        [TestMethod]
        public void GetsInstanceOfRenameEditWithScore()
        {
            var change = (RenameEdit)GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\0blah\0newblah\0");
            Assert.Equal("001", change.Score);
        }

        [TestMethod]
        public void ThrowsOnIncorrectInputLine() => Assert.Throws<Exception>(() =>
                                                                 GetChangeItem(":100644 100644 abcdef0123abcdef0123abcdef0123abcdef0123 01234567ab01234567ab01234567ab01234567ab R001\tblah\tnewblah"));

        [TestMethod]
        public void MultipleChanges()
        {
            string input =
                ":000000 100644 0000000000000000000000000000000000000000 ed61b923604692e7c8b14763bd94412f471d91cc A\0TestFiles/Test0.txt\0" +
                ":100644 100644 5f10a5d3fa9f56697881f8d9c49e20bcc541cc94 74e8a9318a5566812366a5b6005c94cfdd33036d M\0TestFiles/Test1.txt\0" +
                ":100644 000000 de4ea28b4e441777cf99329788d54598645618f3 0000000000000000000000000000000000000000 D\0TestFiles/Test2.txt\0" +
                ":100644 100644 fb6422c94fcb11e61378a231b0f3ce36958206d4 fb6422c94fcb11e61378a231b0f3ce36958206d4 R100\0TestFiles/Test3.txt\0TestFiles/Test3_moved.txt\0" +
                ":000000 100644 0000000000000000000000000000000000000000 5238c94b04f81776f57eed406484c0a0e0697749 A\0TestFiles/Test4.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Equal(5, changes.Length);

                Assert.Equal("A", changes[0].Status);
                Assert.Equal("TestFiles/Test0.txt", changes[0].path);

                Assert.Equal("M", changes[1].Status);
                Assert.Equal("TestFiles/Test1.txt", changes[1].path);

                Assert.Equal("D", changes[2].Status);
                Assert.Equal("TestFiles/Test2.txt", changes[2].path);

                Assert.Equal("R", changes[3].Status);
                Assert.Equal("100", changes[3].score);
                Assert.Equal("TestFiles/Test3.txt", changes[3].path);
                Assert.Equal("TestFiles/Test3_moved.txt", changes[3].pathTo);

                Assert.Equal("A", changes[4].Status);
                Assert.Equal("TestFiles/Test4.txt", changes[4].path);
            }
        }

        [TestMethod]
        public void MultipleChangesWithJapanese()
        {
            string input =
                ":000000 100644 0000000000000000000000000000000000000000 ed61b923604692e7c8b14763bd94412f471d91cc A\0TestFiles/試し0.txt\0" +
                ":100644 100644 5f10a5d3fa9f56697881f8d9c49e20bcc541cc94 74e8a9318a5566812366a5b6005c94cfdd33036d M\0TestFiles/試し1.txt\0" +
                ":100644 000000 de4ea28b4e441777cf99329788d54598645618f3 0000000000000000000000000000000000000000 D\0TestFiles/試し2.txt\0" +
                ":100644 100644 fb6422c94fcb11e61378a231b0f3ce36958206d4 fb6422c94fcb11e61378a231b0f3ce36958206d4 R100\0TestFiles/試し3.txt\0TestFiles/試し3_moved.txt\0" +
                ":000000 100644 0000000000000000000000000000000000000000 5238c94b04f81776f57eed406484c0a0e0697749 A\0TestFiles/試し4.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Equal(5, changes.Length);

                Assert.Equal("A", changes[0].Status);
                Assert.Equal("TestFiles/試し0.txt", changes[0].path);

                Assert.Equal("M", changes[1].Status);
                Assert.Equal("TestFiles/試し1.txt", changes[1].path);

                Assert.Equal("D", changes[2].Status);
                Assert.Equal("TestFiles/試し2.txt", changes[2].path);

                Assert.Equal("R", changes[3].Status);
                Assert.Equal("100", changes[3].score);
                Assert.Equal("TestFiles/試し3.txt", changes[3].path);
                Assert.Equal("TestFiles/試し3_moved.txt", changes[3].pathTo);

                Assert.Equal("A", changes[4].Status);
                Assert.Equal("TestFiles/試し4.txt", changes[4].path);
            }
        }

        [TestMethod]
        public void ShouldDetectNormalRename_AndReturnOneRenameChange()
        {
            string input = ":100644 100644 ab6422c94fcb11e61378a231b0f3ce36958206d4 bb6422c94fcb11e61378a231b0f3ce36958206d4 R100\0TestFiles/Test0.txt\0TestFiles/Test_moved.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Single(changes);

                Assert.Equal("R", changes[0].Status);
                Assert.Equal("TestFiles/Test_moved.txt", changes[0].pathTo);
            }
        }

        [TestMethod]
        public void ShouldDetectCaseOnlyRenameWithNoContentChange_AndReturnNoChanges()
        {
            string input = ":100644 100644 fb6422c94fcb11e61378a231b0f3ce36958206d4 fb6422c94fcb11e61378a231b0f3ce36958206d4 R100\0TestFiles/Test2.txt\0TestFiles/test2.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Empty(changes);
            }
        }

        [TestMethod]
        public void ShouldDetectCaseOnlyRenameWithContentChange_AndReturnOneModificationChanges()
        {
            string input = ":100644 100644 aaaac94fcb11e61378a231b0f3ce36958206d4dd bbbb22c94fcb11e61378a231b0f3ce36958206d4 R100\0TestFiles/Test1.txt\0TestFiles/test1.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Single(changes);

                Assert.Equal("M", changes[0].Status);
                Assert.Equal("TestFiles/Test1.txt", changes[0].path);
            }
        }

        [TestMethod]
        public void ShouldDetectAddDeleteCorrespondingToCaseRenameWithContentChange_AndReturnOneModificationChange()
        {
            string input =
                ":000000 100644 0000000000000000000000000000000000000000 ed61b923604692e7c8b14763bd94412f471d91cc A\0TestFiles/Test0.txt\0" +
                ":100644 000000 de4ea28b4e441777cf99329788d545986456183f 0000000000000000000000000000000000000000 D\0TestFiles/test0.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Single(changes);

                Assert.Equal("M", changes[0].Status);
                Assert.Equal("TestFiles/Test0.txt", changes[0].path);
            }
        }

        [TestMethod]
        public void ShouldDetectAddDeleteNotCorrespondingToCaseRenameWithContentChange_AndReturnOneAdditionAndOneDeletionChange()
        {
            string input =
                ":100644 000000 de4ea28b4e441777cf99329788d545986456183f 0000000000000000000000000000000000000000 D\0TestFiles/Test1.txt\0" +
                ":000000 100644 0000000000000000000000000000000000000000 ed61b923604692e7c8b14763bd94412f471d91cc A\0TestFiles/Test2.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Equal(2, changes.Length);

                Assert.Equal("D", changes[0].Status);
                Assert.Equal("TestFiles/Test1.txt", changes[0].path);

                Assert.Equal("A", changes[1].Status);
                Assert.Equal("TestFiles/Test2.txt", changes[1].path);
            }
        }

        [TestMethod]
        public void ShouldDetectAddDeleteCorrespondingToCaseRenameWithoutContentChange_AndReturnNoChanges()
        {
            string input =
                ":000000 100644 0000000000000000000000000000000000000000 ed61b923604692e7c8b14763bd94412f471d91cc A\0TestFiles/Test3.txt\0" +
                ":100644 000000 ed61b923604692e7c8b14763bd94412f471d91cc 0000000000000000000000000000000000000000 D\0TestFiles/test3.txt\0";

            using (var reader = new System.IO.StringReader(input))
            {
                var changes = GitChangeInfo.GetChangedFiles(reader).ToArray();

                Assert.Empty(changes);
            }
        }
    }
}
