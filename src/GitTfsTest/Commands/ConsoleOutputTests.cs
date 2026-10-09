namespace GitTfs.Test.Commands
{
    using System.Diagnostics;
    using GitTfs.Core;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Spectre.Console;

    [TestClass]
    public class ConsoleOutputTests
    {
        [TestMethod]
        public void DebugLoggingDoesNotLeakIntoTheNextRunAndHelpStaysVisible()
        {
            var directory = Path.Combine(Path.GetTempPath(), "git-tfs-log-mode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var settingsPath = Path.Combine(directory, "appsettings.json");
            File.WriteAllText(settingsPath, "{\"debug\":false}");
            var oldSettings = Environment.GetEnvironmentVariable("GIT_TFS_APPSETTINGS");
            var oldDebug = Environment.GetEnvironmentVariable("GIT_TFS_DEBUG");
            var originalOutput = Console.Out;
            var originalConsole = AnsiConsole.Console;
            var originalListeners = Trace.Listeners.Cast<TraceListener>().ToArray();
            try
            {
                Environment.SetEnvironmentVariable("GIT_TFS_APPSETTINGS", settingsPath);
                Environment.SetEnvironmentVariable("GIT_TFS_DEBUG", "false");
                using var helpOutput = new StringWriter();
                AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Ansi = AnsiSupport.No,
                    Out = new AnsiConsoleOutput(helpOutput)
                });
                using var debugOutput = new StringWriter();
                Console.SetOut(debugOutput);
                Assert.AreEqual(GitTfsExitCodes.Help, Program.MainCore(new[] { "help", "--debug" }));
                StringAssert.Contains(debugOutput.ToString(), "Command run:");

                using var normalOutput = new StringWriter();
                Console.SetOut(normalOutput);
                Assert.AreEqual(GitTfsExitCodes.Help, Program.MainCore(new[] { "help" }));
                Assert.AreEqual(string.Empty, normalOutput.ToString());
                StringAssert.Contains(helpOutput.ToString(), "Usage: git-tfs");

                helpOutput.GetStringBuilder().Clear();
                Assert.AreEqual(GitTfsExitCodes.OK, Program.MainCore(new[] { "--version" }));
                Assert.IsTrue(helpOutput.ToString().Length > 0);
                Assert.AreEqual(string.Empty, normalOutput.ToString());
            }
            finally
            {
                Console.SetOut(originalOutput);
                AnsiConsole.Console = originalConsole;
                Trace.Listeners.Clear();
                Trace.Listeners.AddRange(originalListeners);
                Environment.SetEnvironmentVariable("GIT_TFS_APPSETTINGS", oldSettings);
                Environment.SetEnvironmentVariable("GIT_TFS_DEBUG", oldDebug);
                Directory.Delete(directory, true);
            }
        }
    }
}
