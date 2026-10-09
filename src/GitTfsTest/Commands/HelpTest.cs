
namespace GitTfs.Test.Commands
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Assert = GitTfs.Test.TestAssert;
    using GitTfs.Commands;
    using GitTfs;
    using GitTfs.Test;
    using GitTfs.Util;
    using Spectre.Console;
    [TestClass]
    public class HelpTest : BaseTest
    {
        private readonly MoqAutoMocker<Help> mocks;
        private IAnsiConsole originalConsole;
        private StringWriter output;

        public HelpTest()
        {
            mocks = new MoqAutoMocker<Help>();
        }

        [TestInitialize]
        public void CaptureConsole()
        {
            originalConsole = AnsiConsole.Console;
            output = new StringWriter();
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                Out = new AnsiConsoleOutput(output)
            });
            AnsiConsole.Profile.Width = 200;
        }

        [TestCleanup]
        public void RestoreConsole()
        {
            AnsiConsole.Console = originalConsole;
            output.Dispose();
        }

        [TestMethod]
        public void ShouldWriteGeneralHelp()
        {
            mocks.RegisterCommand("test", new TestCommand());
            mocks.ClassUnderTest.Run();

            var lines = output.ToString().Split(Environment.NewLine);
            Assert.Equal("Usage: git-tfs [options] <tfs-subfolder> <output-path> [target-git-url] [target-branch]", lines[0]);
            Assert.Contains("test", lines[1]);
            Assert.Equal(" (use 'git-tfs --help' for more information)", lines[2]);
            Assert.Contains("Find more help in our online help : https://github.com/git-tfs/git-tfs", output.ToString());
        }

        [TestMethod]
        public void ShouldWriteCommandHelp()
        {
            mocks.RegisterCommand("test", new TestCommand());
            mocks.ClassUnderTest.Run(new[] { "test" });

            Assert.Contains("Usage: git-tfs test [options]", output.ToString());
        }

        public class TestCommand : GitTfsCommand
        {
            public bool Flag { get; set; }

            private readonly OptionSet TestOptions = new OptionSet();

            public OptionSet OptionSet => TestOptions;

            public int Run(IList<string> args) => throw new System.NotImplementedException();
        }

    }
}
