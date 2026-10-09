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
                File.WriteAllBytes(Path.Combine(root, ".git", "hidden.txt"), new byte[1024]);
                Directory.CreateDirectory(Path.Combine(root, "src", "nested"));
                File.WriteAllBytes(Path.Combine(root, "src", "[old].txt"), new byte[6]);
                File.WriteAllBytes(Path.Combine(root, "src", "nested", "child.txt"), new byte[100]);
                var clock = new ManualTimeProvider();
                var view = new WorkingDirectoryTree(root, clock);
                var initial = view.Render();
                var text = Render(initial);
                StringAssert.Contains(text, "src");
                Assert.IsTrue(text.Split('\n')[1].Contains("0 files · 0 B"));
                StringAssert.Contains(text, "1 file · 6 B");
                StringAssert.Contains(text, "1 file · 100 B");
                Assert.IsFalse(text.Contains("[old].txt"));
                Assert.IsFalse(text.Contains("child.txt"));
                Assert.IsFalse(text.Contains("hidden.txt"));
                File.Move(Path.Combine(root, "src", "[old].txt"), Path.Combine(root, "new.txt"));
                clock.Advance(TimeSpan.FromMilliseconds(999));
                Assert.AreSame(initial, view.Render());
                clock.Advance(TimeSpan.FromMilliseconds(1));
                text = Render(view.Render());
                Assert.IsTrue(text.Split('\n')[1].Contains("1 file · 6 B"));
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(text, @"src[^\n]*\n[^\n]*0 files · 0 B"));
                Assert.IsFalse(text.Contains("new.txt"));
                Assert.IsFalse(text.Contains("[old].txt"));
                File.Delete(Path.Combine(root, "new.txt"));
                Assert.IsTrue(Render(view.Render(refresh: true)).Split('\n')[1].Contains("0 files · 0 B"));
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
                for (var i = 0; i < 100; i++)
                {
                    File.WriteAllBytes(Path.Combine(root, $"file-{i:D3}.txt"), new byte[1024]);
                    Directory.CreateDirectory(Path.Combine(root, $"dir-{i:D3}"));
                }
                var text = Render(view.Render(refresh: true));
                Assert.AreEqual(20, System.Text.RegularExpressions.Regex.Matches(text, @"dir-\d+").Count);
                Assert.IsFalse(text.Contains(".txt"));
                StringAssert.Contains(text, "100 files · 100 KB");
                StringAssert.Contains(text, "… more");
                var smaller = Render(view.Render(maxNodes: 2));
                Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(smaller, @"dir-\d+").Count);
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
