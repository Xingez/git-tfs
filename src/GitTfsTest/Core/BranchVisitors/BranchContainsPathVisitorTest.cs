using GitTfs.Core.BranchVisitors;
using GitTfs.Core.TfsInterop;
using GitTfs.VsFake;

namespace GitTfs.Test.Core.BranchVisitors
{
    [TestClass]
    public class BranchContainsPathVisitorTest : BaseTest
    {
        private readonly BranchTree branch;

        public BranchContainsPathVisitorTest()
        {
            branch = new BranchTree(new MockBranchObject { Path = @"$/Scratch/Source/Main" });
        }

        [TestMethod]
        public void InexactMatch_WithoutTrailingSlash_IsFound()
        {
            var visitor = new BranchTreeContainsPathVisitor(@"$/Scratch/Source/Main", false);

            branch.AcceptVisitor(visitor);

            Assert.True(visitor.Found);
        }

        [TestMethod]
        public void InexactMatch_WithTrailingSlash_IsFound()
        {
            var visitor = new BranchTreeContainsPathVisitor(@"$/Scratch/Source/Main/", false);

            branch.AcceptVisitor(visitor);

            Assert.True(visitor.Found);
        }

        [TestMethod]
        public void ExactMatch_WithoutTrailingSlash_IsFound()
        {
            var visitor = new BranchTreeContainsPathVisitor(@"$/Scratch/Source/Main", true);

            branch.AcceptVisitor(visitor);

            Assert.True(visitor.Found);
        }

        [TestMethod]
        public void ExactMatch_WithTrailingSlash_IsNotFound()
        {
            var visitor = new BranchTreeContainsPathVisitor(@"$/Scratch/Source/Main/", true);

            branch.AcceptVisitor(visitor);

            Assert.False(visitor.Found);
        }
    }
}