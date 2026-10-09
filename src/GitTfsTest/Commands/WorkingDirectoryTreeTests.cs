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
                view.ReportDownloadedFile("src/[old].txt");
                var initial = view.Render();
                var text = Render(initial);
                StringAssert.Contains(text, "src");
                StringAssert.Contains(text, "> [old].txt");
                Assert.IsFalse(text.Contains("files ·"));
                Assert.IsFalse(text.Contains("child.txt"));
                Assert.IsFalse(text.Contains("hidden.txt"));
                File.Move(Path.Combine(root, "src", "[old].txt"), Path.Combine(root, "new.txt"));
                view.ReportDownloadedFile("new.txt");
                clock.Advance(TimeSpan.FromMilliseconds(999));
                Assert.AreSame(initial, view.Render());
                clock.Advance(TimeSpan.FromMilliseconds(1));
                text = Render(view.Render());
                StringAssert.Contains(text, "> new.txt");
                StringAssert.Contains(text, "src/nested");
                Assert.IsFalse(text.Contains("[old].txt"));
                File.Delete(Path.Combine(root, "new.txt"));
                Assert.IsFalse(Render(view.Render(refresh: true)).Contains("new.txt"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void CompactChainsExpandWhenFoldersBranchOrContainFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "git-tfs-tree-" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = Path.Combine(root, "src");
                var core = Path.Combine(source, "Core");
                var leaf = Path.Combine(core, "Import");
                Directory.CreateDirectory(leaf);
                File.WriteAllBytes(Path.Combine(leaf, "hidden.txt"), new byte[16]);
                var view = new WorkingDirectoryTree(root);
                var text = Render(view.Render());
                StringAssert.Contains(text, "src/Core/Import");
                Assert.IsFalse(text.Contains("hidden.txt"));
                Directory.CreateDirectory(Path.Combine(source, "Other"));
                text = Render(view.Render(refresh: true));
                Assert.IsFalse(text.Contains("src/Core/Import"));
                StringAssert.Contains(text, "Core/Import");
                StringAssert.Contains(text, "Other");
                File.WriteAllBytes(Path.Combine(core, "parent.txt"), new byte[3]);
                text = Render(view.Render(refresh: true));
                Assert.IsFalse(text.Contains("Core/Import"));
                StringAssert.Contains(text, "Core");
                StringAssert.Contains(text, "Import");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void LatestDownloadExpandsItsPathAndRefreshesAtMostOncePerSecond()
        {
            var root = Path.Combine(Path.GetTempPath(), "git-tfs-tree-" + Guid.NewGuid().ToString("N"));
            try
            {
                var leaf = Path.Combine(root, "src", "Core", "Import");
                Directory.CreateDirectory(leaf);
                File.WriteAllBytes(Path.Combine(leaf, "[latest].cs"), new byte[23]);
                File.WriteAllBytes(Path.Combine(leaf, "hidden.cs"), new byte[100]);
                var clock = new ManualTimeProvider();
                var view = new WorkingDirectoryTree(root, clock);
                var initial = view.Render();
                StringAssert.Contains(Render(initial), "src/Core/Import");
                view.ReportDownloadedFile("src/Core/Import/[latest].cs");
                Assert.AreSame(initial, view.Render());
                clock.Advance(TimeSpan.FromSeconds(1));
                var expanded = Render(view.Render());
                Assert.IsFalse(expanded.Contains("src/Core/Import"));
                StringAssert.Contains(expanded, "> [latest].cs");
                Assert.IsFalse(expanded.Contains("23 B"));
                Assert.IsFalse(expanded.Contains("files ·"));
                Assert.IsFalse(expanded.Contains("hidden.cs"));

                File.WriteAllBytes(Path.Combine(root, "root.txt"), new byte[3]);
                view.ReportDownloadedFile("root.txt");
                clock.Advance(TimeSpan.FromMilliseconds(999));
                StringAssert.Contains(Render(view.Render()), "> [latest].cs");
                clock.Advance(TimeSpan.FromMilliseconds(1));
                var next = Render(view.Render());
                StringAssert.Contains(next, "> root.txt");
                StringAssert.Contains(next, "src/Core/Import");
                Assert.IsFalse(next.Contains("[latest].cs"));
                File.Delete(Path.Combine(root, "root.txt"));
                Assert.IsFalse(Render(view.Render(refresh: true)).Contains("root.txt"));
                foreach (var invalid in new[] { "../outside.txt", ".git/config", Path.Combine(root, "absolute.txt") })
                    view.ReportDownloadedFile(invalid);
                Assert.IsFalse(Render(view.Render(refresh: true)).Contains("outside.txt"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void LatestDownloadTakesPriorityOverOtherFoldersAndFitsTheNodeLimit()
        {
            var root = Path.Combine(Path.GetTempPath(), "git-tfs-tree-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "z-source", "Core", "Import"));
                for (var i = 0; i < 100; i++) Directory.CreateDirectory(Path.Combine(root, $"dir-{i:D3}"));
                File.WriteAllBytes(Path.Combine(root, "z-source", "Core", "Import", "latest.cs"), new byte[1024]);
                var view = new WorkingDirectoryTree(root);
                view.ReportDownloadedFile("z-source/Core/Import/latest.cs");
                var text = Render(view.Render(maxNodes: 2));
                StringAssert.Contains(text, "z-source/Core");
                StringAssert.Contains(text, "Import");
                StringAssert.Contains(text, "> latest.cs");
                Assert.IsFalse(text.Contains("dir-"));
                var narrow = Render(view.Render(maxNodes: 20, maxDepth: 1));
                StringAssert.Contains(narrow, "z-source/Core/Import");
                StringAssert.Contains(narrow, "> latest.cs");
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
                StringAssert.Contains(text, "… more");
                var smaller = Render(view.Render(maxNodes: 2));
                Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(smaller, @"dir-\d+").Count);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        [DataRow(80, 1)]
        [DataRow(120, 3)]
        [DataRow(240, 8)]
        public void EightLevelRandomTreeKeepsLatestLongFilenameVisible(int width, int depth)
        {
            var root = Path.Combine(Path.GetTempPath(), "tree-" + Guid.NewGuid().ToString("N")[..8]);
            const string filename = "Very long downloaded file with a descriptive name and generated source content.cs";
            try
            {
                var random = new Random(7291);
                var relative = "";
                for (var level = 1; level <= 8; level++)
                {
                    relative = Path.Combine(relative, $"Level {level} source folder {random.Next(100, 999)}");
                    var directory = Path.Combine(root, relative);
                    Directory.CreateDirectory(directory);
                    var files = random.Next(1, 12);
                    for (var file = 0; file < files; file++)
                        File.WriteAllText(Path.Combine(directory, $"Other generated file {file}.cs"), "sample");
                    var siblings = random.Next(1, 5);
                    for (var sibling = 0; sibling < siblings; sibling++)
                        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(directory), $"Sibling {level}-{sibling} with long names"));
                }
                File.WriteAllText(Path.Combine(root, relative, filename), "sample");
                var view = new WorkingDirectoryTree(root);
                view.ReportDownloadedFile(Path.Combine(relative, filename));
                var text = Dashboard(view.Render(maxDepth: depth), width);
                StringAssert.Contains(text, "> Very");
                Assert.AreEqual(1, text.Split('\n').Count(line => line.Contains("> Very")));
                Assert.IsTrue(text.Split('\n').All(line => line.TrimEnd('\r').Length <= width));
                Assert.IsTrue(text.Split('\n').Length < 30, "Deep paths must stay within the visible tree budget.");
                Assert.IsTrue(System.Text.RegularExpressions.Regex.Matches(text, "… more").Count <= 1);
                Assert.IsFalse(text.Contains("Other generated file"));
                Assert.IsFalse(text.Contains("files ·"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        [DataRow(70)]
        [DataRow(80)]
        [DataRow(120)]
        [DataRow(240)]
        public void LongNamesStayOnOneLineAndUseTheAvailablePanelWidth(int width)
        {
            var root = Path.Combine(Path.GetTempPath(), "tree-" + Guid.NewGuid().ToString("N")[..8]);
            const string folder = "Source files with a very long directory name containing multiple words";
            const string filename = "Very long [generated] changeset import file with spaces and a detailed description of the downloaded content.cs";
            try
            {
                Directory.CreateDirectory(Path.Combine(root, folder));
                File.WriteAllBytes(Path.Combine(root, folder, "short.cs"), new byte[1024]);
                File.WriteAllBytes(Path.Combine(root, folder, filename), new byte[1024]);
                var clock = new ManualTimeProvider();
                var view = new WorkingDirectoryTree(root, clock);
                view.ReportDownloadedFile(folder + "/short.cs");
                var shortName = Dashboard(view.Render(maxDepth: 1), width);
                view.ReportDownloadedFile(folder + "/" + filename);
                clock.Advance(TimeSpan.FromSeconds(1));
                var longName = Dashboard(view.Render(maxDepth: 1), width);
                Assert.AreEqual(shortName.Split('\n').Length, longName.Split('\n').Length,
                    "Long names must not add wrapped lines to the tree.");
                var fileLine = longName.Split('\n').Single(line => line.Contains("> Very"));
                StringAssert.Contains(fileLine, "…");
                Assert.IsTrue(fileLine.TrimEnd('\r').Length <= width);
                var widerLine = Dashboard(view.Render(maxDepth: 1), width < 80 ? 79 : width * 2)
                    .Split('\n').Single(line => line.Contains("> Very"));
                Assert.IsTrue(widerLine[(widerLine.IndexOf("> Very", StringComparison.Ordinal))..].TrimEnd().Length
                    > fileLine[(fileLine.IndexOf("> Very", StringComparison.Ordinal))..].TrimEnd().Length,
                    "A wider terminal must reveal more of the filename even when the tree is cached.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static string Dashboard(IRenderable folder, int width)
        {
            using var output = new StringWriter();
            var console = CreateConsole(output, width);
            console.Write(SpectreCloneProgress.Dashboard(console,
                new Text("metrics"), new Text("progress"), "Importing", folder));
            return output.ToString();
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
