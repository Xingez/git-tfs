
namespace GitTfs.Test.Commands
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Assert = global::GitTfs.Test.TestAssert;
    using global::GitTfs.Commands;
    using global::GitTfs;
    using global::GitTfs.Test;
    using global::GitTfs.Util;
    using global::System.Diagnostics;
    using global::System.Globalization;
    [TestClass]
    public class HelpTest : BaseTest
    {
        private readonly MoqAutoMocker<Help> mocks;

        public HelpTest()
        {
            mocks = new MoqAutoMocker<Help>();
        }

        public MemoryTraceListener GetTestLogger()
        {
            var memoryListener = new MemoryTraceListener();
            Trace.Listeners.Clear();
            Trace.Listeners.Add(memoryListener);

            return memoryListener;
        }

        [TestMethod]
        public void ShouldWriteGeneralHelp()
        {
            var memoryTarget = GetTestLogger();

            mocks.RegisterCommand("test", new TestCommand());
            mocks.ClassUnderTest.Run();

            Assert.Equal("Usage: git-tfs [command] [options]", memoryTarget.Logs[0]);
            Assert.Contains("test", memoryTarget.Logs[1]);
            Assert.Equal(" (use 'git-tfs help [command]' or 'git-tfs [command] --help' for more information)", memoryTarget.Logs[2]);
            Assert.Contains("Find more help in our online help : https://github.com/git-tfs/git-tfs", memoryTarget.Logs[3]);
        }

        [TestMethod]
        public void ShouldWriteCommandHelp()
        {
            var memoryTarget = GetTestLogger();
            mocks.RegisterCommand("test", new TestCommand());
            mocks.ClassUnderTest.Run(new[] { "test" });

            memoryTarget.Logs[0].Equals("Usage: git-tfs test [options]");
        }

        public class TestCommand : GitTfsCommand
        {
            public bool Flag { get; set; }

            private readonly OptionSet TestOptions = new OptionSet();

            public OptionSet OptionSet => TestOptions;

            public int Run(IList<string> args) => throw new System.NotImplementedException();
        }

        public sealed class MemoryTraceListener : TraceListener
        {
            public List<string> Logs { get; } = new List<string>();

            public override void Write(string message) => Logs.Add(message);

            public override void WriteLine(string message) => Logs.Add(message);

            public override void TraceEvent(TraceEventCache eventCache, string source,
                TraceEventType eventType, int id, string message) => Logs.Add(message);

            public override void TraceEvent(TraceEventCache eventCache, string source,
                TraceEventType eventType, int id, string format, params object[] args)
                => Logs.Add(args == null || args.Length == 0
                    ? format
                    : string.Format(CultureInfo.CurrentCulture, format, args));
        }
    }
}
