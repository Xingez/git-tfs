
namespace GitTfs.Test.Util
{
    using global::GitTfs.Util;
    [TestClass]
    public class BouncerTest : BaseTest
    {
        private readonly Bouncer bouncer = new Bouncer();

        [TestMethod]
        public void NoExpressionsMeansNotMatched() => Assert.False(bouncer.IsIncluded("$/Any/Path"));

        [TestMethod]
        public void IgnoreNullExpressions()
        {
            bouncer.Include(null);
            bouncer.Exclude(null);
            Assert.False(bouncer.IsIncluded("$/Any/Path"));
        }

        [TestMethod]
        public void IncludesEverything()
        {
            bouncer.Include(".*");
            Assert.True(bouncer.IsIncluded("$/Any/Path"));
        }

        [TestMethod]
        public void IncludesEverythingExceptSomething()
        {
            bouncer.Include(".*");
            bouncer.Exclude("something");
            Assert.True(bouncer.IsIncluded("$/Any/Path"));
            Assert.False(bouncer.IsIncluded("$/something/Path"));
        }

        [TestMethod]
        public void IgnoresCase()
        {
            bouncer.Include("thing");
            bouncer.Exclude("other/thing");
            Assert.True(bouncer.IsIncluded("$/Thing/Path"));
            Assert.False(bouncer.IsIncluded("$/Other/Thing/Path"));
        }

        [TestMethod]
        public void PrefersExclusion()
        {
            bouncer.Include(".*");
            bouncer.Exclude(".*");
            Assert.False(bouncer.IsIncluded("$/Any/Path"));
        }

        [TestMethod]
        public void IncludesAndExcludesAll()
        {
            bouncer.Include("\\.exe$");
            bouncer.Include("\\.dll$");
            bouncer.Exclude("/ext/");
            bouncer.Exclude("/deps/");
            Assert.True(bouncer.IsIncluded("$/Any/Path/bin/example.exe"));
            Assert.True(bouncer.IsIncluded("$/Any/Path/bin/example.dll"));
            Assert.False(bouncer.IsIncluded("$/Any/Path/ext/example.exe"));
            Assert.False(bouncer.IsIncluded("$/Any/Path/ext/example.dll"));
            Assert.False(bouncer.IsIncluded("$/Any/Path/deps/example.exe"));
            Assert.False(bouncer.IsIncluded("$/Any/Path/deps/example.dll"));
        }
    }
}
