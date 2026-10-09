namespace GitTfs.Test.Commands
{
    using GitTfs.Commands;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Spectre.Console;
    using Spectre.Console.Rendering;

    [TestClass]
    public class WorkingDirectoryTreeTests
    {
        [TestMethod]
        public void TreeRefreshesAfterFileMovesAndDeletesAndExcludesGit()
        {
            var root = Path.Combine(Path.GetTempPath(), "git-tfs-tree-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, ".git"));
                File.WriteAllText(Path.Combine(root, ".git", "hidden.txt"), "");
                Directory.CreateDirectory(Path.Combine(root, "src"));
                File.WriteAllText(Path.Combine(root, "src", "[old].txt"), "");
                var clock = new ManualTimeProvider();
                var view = new WorkingDirectoryTree(root, clock);
                var initial = view.Render();
                var text = Render(initial);
                StringAssert.Contains(text, "src");
                StringAssert.Contains(text, "[old].txt");
                Assert.IsFalse(text.Contains("hidden.txt"));
                File.Move(Path.Combine(root, "src", "[old].txt"), Path.Combine(root, "new.txt"));
                clock.Advance(TimeSpan.FromMilliseconds(999));
                Assert.AreSame(initial, view.Render());
                clock.Advance(TimeSpan.FromMilliseconds(1));
                text = Render(view.Render());
                StringAssert.Contains(text, "new.txt");
                Assert.IsFalse(text.Contains("[old].txt"));
                File.Delete(Path.Combine(root, "new.txt"));
                Assert.IsFalse(Render(view.Render(refresh: true)).Contains("new.txt"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void TreeWaitsForNewFolderAndBoundsLargeDirectories()
        {
            var root = Path.Combine(Path.GetTempPath(), "git-tfs-tree-" + Guid.NewGuid().ToString("N"));
            try
            {
                var view = new WorkingDirectoryTree(root);
                StringAssert.Contains(Render(view.Render()), "Waiting for folder");
                Directory.CreateDirectory(root);
                for (var i = 0; i < 100; i++) File.WriteAllText(Path.Combine(root, $"file-{i:D3}.txt"), "");
                var text = Render(view.Render(refresh: true));
                Assert.AreEqual(20, System.Text.RegularExpressions.Regex.Matches(text, @"file-\d+\.txt").Count);
                StringAssert.Contains(text, "… more");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        [DataRow(70, false)]
        [DataRow(80, true)]
        [DataRow(180, true)]
        public void FolderPanelAppearsOnTheRightWhenThereIsRoom(int width, bool beside)
        {
            using var output = new StringWriter();
            var console = CreateConsole(output, width);
            console.Write(SpectreCloneProgress.Dashboard(console, new Text("metrics"), new Text("progress"),
                "Importing", new Panel(new Text("file.txt")).Header("Folder")));
            var text = output.ToString();
            Assert.AreEqual(beside, text.Split('\n')[0].Contains("Folder"));
            StringAssert.Contains(text, "file.txt");
            if (beside)
                Assert.IsTrue(text.Split('\n').Single(line => line.Contains("file.txt")).IndexOf("file.txt", StringComparison.Ordinal)
                    > text.Split('\n').Single(line => line.Contains("progress")).IndexOf("progress", StringComparison.Ordinal));
            else
                Assert.IsTrue(text.IndexOf("Folder", StringComparison.Ordinal) > text.IndexOf("progress", StringComparison.Ordinal));
            Assert.IsTrue(text.IndexOf("metrics", StringComparison.Ordinal) > text.IndexOf("file.txt", StringComparison.Ordinal));
        }

        private static string Render(IRenderable display)
        {
            using var output = new StringWriter();
            CreateConsole(output, 180).Write(display);
            return output.ToString();
        }

        private static IAnsiConsole CreateConsole(StringWriter output, int width)
        {
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output),
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
            });
            console.Profile.Width = width;
            return console;
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long timestamp;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => timestamp;
            public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
        }
    }
}
