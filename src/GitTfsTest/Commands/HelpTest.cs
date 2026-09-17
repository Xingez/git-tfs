using GitTfs.Commands;
using GitTfs;
using GitTfs.Test;
using GitTfs.Util;
using System.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GitTfs.Test.Commands
{
    [TestClass]
    public class HelpTest : BaseTest
    {
        private readonly MoqAutoMocker<Help> mocks;

        public HelpTest()
        {
            mocks = new MoqAutoMocker<Help>();
        }

        public MemorySink GetTestLogger()
        {
            var memorySink = new MemorySink();
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Sink(memorySink)
                .CreateLogger();

            Trace.Listeners.Clear();
            Trace.Listeners.Add(new SerilogTraceListener());

            return memorySink;
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

        public sealed class MemorySink : ILogEventSink
        {
            public List<string> Logs { get; } = new List<string>();

            public void Emit(LogEvent logEvent) => Logs.Add(logEvent.RenderMessage());
        }
    }
}
