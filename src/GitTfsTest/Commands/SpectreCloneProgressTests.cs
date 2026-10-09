namespace GitTfs.Test.Commands
{
    using GitTfs.Commands;
    using GitTfs.Core.RestTfs;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using Spectre.Console;

    [TestClass]
    public class SpectreCloneProgressTests
    {
        [TestMethod]
        public void LiveDashboardUsesNewWidthAfterResizingWithALongFilename()
        {
            var root = Path.Combine(Path.GetTempPath(), "tree-" + Guid.NewGuid().ToString("N")[..8]);
            const string filename = "Very long generated ChangesetImporterWithDownloadProgressAndRateLimit.cs";
            using var output = new LockedStringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 180;
            console.Profile.Height = 40;
            var original = AnsiConsole.Console;
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "src", "Core"));
                File.WriteAllBytes(Path.Combine(root, "src", "Core", filename), new byte[1024]);
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.CompleteScan(1);
                    progress.StartChangeset(1, 2);
                    progress.ReportDownloadedFile("src/Core/" + filename);
                    progress.ReportFiles(1, 1, 2);
                    var narrow = WaitForFrame(frame => frame.Contains("> Very long") && frame.Contains("…"));
                    var narrowBar = BarWidth(narrow);
                    console.Profile.Width = 300;
                    var wide = WaitForFrame(frame => frame.Contains("> " + filename) && BarWidth(frame) > narrowBar);
                    Assert.IsTrue(BarWidth(wide) > narrowBar);
                    console.Profile.Width = 80;
                    var smaller = WaitForFrame(frame => frame.Contains("> Very") && !frame.Contains(filename)
                        && BarWidth(frame) < narrowBar && frame.Split('\n').All(line => line.TrimEnd('\r').Length <= 80));
                    Assert.AreEqual(narrow.Split('\n').Length, wide.Split('\n').Length,
                        "Revealing a longer filename must not make the tree taller.");
                    Assert.IsFalse(smaller.Contains(filename));
                    progress.CompleteChangeset(1, "abcdef1234");
                    return 0;
                }, root);
            }
            finally
            {
                AnsiConsole.Console = original;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }

            string WaitForFrame(Func<string, bool> predicate)
            {
                string frame = null;
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                {
                    frame = LastImportFrame(output.ToString());
                    return frame.Contains("Rate") && predicate(frame);
                }, TimeSpan.FromSeconds(4)), "Both the tree and progress bar must follow terminal resizing.");
                return frame;
            }

            static int BarWidth(string frame)
                => System.Text.RegularExpressions.Regex.Match(frame, @"\bC1\b[^\r\n]*50%").Value.Count(character => character == '━');
        }

        [TestMethod]
        public void LiveTreeFollowsTheLatestDownload()
        {
            var root = Path.Combine(Path.GetTempPath(), "git-tfs-live-tree-" + Guid.NewGuid().ToString("N"));
            using var output = new LockedStringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 120;
            console.Profile.Height = 40;
            var original = AnsiConsole.Console;
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "src", "Core"));
                File.WriteAllText(Path.Combine(root, "src", "Core", "first.cs"), "first");
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.CompleteScan(1);
                    progress.StartChangeset(1, 2);
                    progress.ReportDownloadedFile("src/Core/first.cs");
                    Assert.IsTrue(SpinWait.SpinUntil(() =>
                    {
                        var frame = LastImportFrame(output.ToString());
                        return frame.Contains("> first.cs") && frame.Contains("Rate");
                    }, TimeSpan.FromSeconds(3)), "The live tree must show the successful download.");
                    File.WriteAllText(Path.Combine(root, "second.cs"), "second");
                    progress.ReportDownloadedFile("second.cs");
                    Assert.IsTrue(SpinWait.SpinUntil(() =>
                    {
                        var frame = LastImportFrame(output.ToString());
                        return frame.Contains("> second.cs") && !frame.Contains("first.cs") && frame.Contains("Rate");
                    }, TimeSpan.FromSeconds(3)), "The next download must replace the highlight.");
                    progress.CompleteChangeset(1, "abcdef1234");
                    return 0;
                }, root);
            }
            finally
            {
                AnsiConsole.Console = original;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void ActiveChangesetShowsElapsedSecondsAndNextSevenWaitWithoutSpinners()
        {
            using var output = new LockedStringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 180;
            var original = AnsiConsole.Console;
            string first = null, second = null;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    for (var id = 1; id <= 20; id++)
                        progress.ReportScan(1, id, 0, new RestChangesetReference { ChangesetId = id });
                    progress.CompleteScan(20);
                    progress.DescribeChangeset(1, "ignored");
                    progress.StartChangeset(1, 2);
                    progress.ReportFiles(1, 1, 2);
                    Assert.IsTrue(SpinWait.SpinUntil(() =>
                    {
                        var frame = LastImportFrame(output.ToString());
                        if (!System.Text.RegularExpressions.Regex.IsMatch(frame, @"C1\b[^\r\n]*50%\s+[1-9]\d*s")
                            || !System.Text.RegularExpressions.Regex.IsMatch(frame, @"\bC8\b[^\r\n]*0%\s+0s[\s\S]*Rate")) return false;
                        first = frame;
                        return true;
                    }, TimeSpan.FromSeconds(3)), "The active timer and seven queued rows must be rendered.");
                    progress.CompleteChangeset(1, "abcdef1234");
                    progress.StartChangeset(2, 2);
                    Assert.IsTrue(SpinWait.SpinUntil(() =>
                    {
                        var frame = LastImportFrame(output.ToString());
                        if (!System.Text.RegularExpressions.Regex.IsMatch(frame, @"\bC9\b[^\r\n]*0%\s+0s[\s\S]*Rate")) return false;
                        second = frame;
                        return true;
                    }, TimeSpan.FromSeconds(3)), "The next queued changeset must enter the visible list.");
                    return 1;
                });
            }
            finally { AnsiConsole.Console = original; }
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(first, @"C1\b[^\r\n]*50%\s+[1-9]\d*s"));
            for (var id = 2; id <= 8; id++)
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(first, $@"│\s+C{id}\b[^\r\n]*0%\s+0s"),
                    "Queued changesets must remain at zero without an active spinner.");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(first, @"\bC9\b"));
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(second, @"\bC1\b[^\r\n]*100%"));
            for (var id = 3; id <= 9; id++)
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(second, $@"│\s+C{id}\b[^\r\n]*0%\s+0s"));
        }

        private static string LastImportFrame(string output)
        {
            var text = System.Text.RegularExpressions.Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            var start = text.LastIndexOf("Changesets · Importing", StringComparison.Ordinal);
            return start < 0 ? string.Empty : text[start..];
        }

        [TestMethod]
        [DataRow(80)]
        [DataRow(180)]
        public void LiveWindowKeepsEightCompletedAndEightCurrentOrUpcomingChangesets(int width)
        {
            using var output = new LockedStringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = width;
            var original = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    for (var id = 1; id <= 32; id++)
                        progress.ReportScan(1, id, 0, new RestChangesetReference { ChangesetId = id });
                    progress.CompleteScan(32);
                    for (var id = 1; id <= 8; id++) progress.SkipChangeset(id);
                    progress.StartChangeset(9, 2);
                    progress.ReportFiles(9, 1, 2);
                    CheckFrame(1, 9, 16);
                    progress.CompleteChangeset(9, "abcdef1234");
                    progress.SkipChangeset(10);
                    progress.StartChangeset(11, 2);
                    progress.ReportFiles(11, 1, 2);
                    CheckFrame(3, 11, 18);
                    return 1;
                });
            }
            finally { AnsiConsole.Console = original; }

            void CheckFrame(int firstCompleted, int active, int lastQueued)
            {
                string frame = null;
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                {
                    frame = LastImportFrame(output.ToString());
                    return System.Text.RegularExpressions.Regex.IsMatch(frame, $@"\bC{active}\b[^\r\n]*50%")
                        && System.Text.RegularExpressions.Regex.IsMatch(frame, $@"\bC{lastQueued}\b[^\r\n]*0%\s+0s[\s\S]*Rate");
                }, TimeSpan.FromSeconds(3)), "The completed and upcoming halves must roll forward together.");
                Assert.AreEqual(16, System.Text.RegularExpressions.Regex.Matches(frame, @"\bC\d+\b").Count);
                for (var id = firstCompleted; id < active; id++)
                    Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(frame, $@"[✓v]\s+C{id}\b[^\r\n]*100%"));
                for (var id = active + 1; id <= lastQueued; id++)
                    Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(frame, $@"│\s+C{id}\b[^\r\n]*0%\s+0s"));
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(frame, $@"\bC{firstCompleted - 1}\b"));
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(frame, $@"\bC{lastQueued + 1}\b"));
            }
        }

        private sealed class LockedStringWriter : StringWriter
        {
            private readonly object gate = new();
            public override void Write(string value) { lock (gate) base.Write(value); }
            public override void Write(char value) { lock (gate) base.Write(value); }
            public override void Write(char[] buffer, int index, int count) { lock (gate) base.Write(buffer, index, count); }
            public override void Write(ReadOnlySpan<char> value) { lock (gate) base.Write(value); }
            public override string ToString() { lock (gate) return base.ToString(); }
        }

        [TestMethod]
        public void LiveDisplayRefreshesWhileHistoryRequestIsBlockedAndShowsOnlyChangesetIds()
        {
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.Yes,
                // CI enrichers override Interactive; this test explicitly simulates a live terminal.
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 180;
            var originalConsole = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                var client = new Mock<IRestTfsClient>();
                client.Setup(c => c.GetChangesets("$/Project/Main", 0, 100)).Returns(() =>
                {
                    Thread.Sleep(1500);
                    return new[] { new RestChangesetReference { ChangesetId = 42, Comment = "Release [blue] build" } };
                });
                client.Setup(c => c.GetChangesets("$/Project/Main", 42, 100))
                    .Returns(Array.Empty<RestChangesetReference>());
                SpectreCloneProgress.Run(progress =>
                {
                    foreach (var reference in new RestChangesetScanner(client.Object, null).Scan("$/Project/Main", 0, 100, progress))
                    {
                        progress.DescribeChangeset(reference.ChangesetId, reference.Comment);
                        progress.StartChangeset(reference.ChangesetId, 2);
                        progress.ReportFiles(reference.ChangesetId, 1, 2);
                        Thread.Sleep(300);
                        progress.CompleteChangeset(reference.ChangesetId, "abcdef1234");
                        progress.StartChangeset(43, 4);
                        progress.ReportFiles(43, 1, 4);
                        Thread.Sleep(350);
                        progress.CompleteChangeset(43, "fedcba4321");
                    }
                    progress.ReportActivity("Verifying checksums");
                    Thread.Sleep(350);
                    return 0;
                });
                var frames = output.ToString();
                Assert.IsTrue(frames.Split("Overall").Length > 3,
                    "The live display must refresh independently of the blocked request.");
                StringAssert.Contains(frames, "Overall");
                Assert.IsFalse(frames.Contains("page "), "Page details must not appear in the live view.");
                Assert.IsFalse(frames.Contains(" found"), "Found counts must not appear in the live titles.");
                StringAssert.Contains(frames, "C42");
                Assert.IsFalse(frames.Contains("Release"), "Changeset comments must not appear in the live view.");
                StringAssert.Contains(frames, "C43");
                StringAssert.Contains(frames, "50%");
                StringAssert.Contains(frames, "25%");
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(frames, @"C42[^\r\n]*100%"),
                    "Completed changesets must remain visible at 100% while later changesets run.");
                Assert.IsFalse(frames.Contains("files"), "Changeset rows must show only their ID and percentage.");
                Assert.IsFalse(frames.Contains("Verifying checksums"));
                Assert.IsFalse(frames.Contains("committed so far"));
            }
            finally { AnsiConsole.Console = originalConsole; }
        }

        [TestMethod]
        [DataRow(80)]
        [DataRow(180)]
        public void DashboardKeepsMetricsInTheFooterAndLastEightCompletedChangesets(int width)
        {
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = width;
            var originalConsole = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.CompleteScan(19);
                    for (var id = 1; id <= 18; id++)
                    {
                        progress.StartChangeset(id, 1);
                        progress.CompleteChangeset(id, "abcdef1234");
                    }
                    progress.SkipChangeset(19);
                    return 0;
                });
                var text = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                var finalFrame = text[text.LastIndexOf("Changesets · Complete", StringComparison.Ordinal)..];
                Assert.IsTrue(finalFrame.IndexOf("Rate", StringComparison.Ordinal) > finalFrame.IndexOf("C19", StringComparison.Ordinal),
                    "Compact metrics belong below the changeset list.");
                for (var id = 1; id <= 11; id++)
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(finalFrame, $@"\bC{id}\b"));
                for (var id = 12; id <= 19; id++)
                    Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(finalFrame, $@"\bC{id}\b"));
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(finalFrame, @"Overall[^\r\n]*100%"));
            }
            finally { AnsiConsole.Console = originalConsole; }
        }

        [TestMethod]
        public void ResumedOverallIncludesEarlierChangesetsAndKeepsRecentCompletedRows()
        {
            using var output = new LockedStringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 180;
            var original = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.ReportResume(Enumerable.Range(1, 13).Select(id => id * 10).ToArray());
                    foreach (var id in new[] { 140, 150, 160 })
                        progress.ReportScan(1, 1, 130, new RestChangesetReference { ChangesetId = id });
                    progress.CompleteScan(3);
                    CheckPercentage(81);
                    progress.StartChangeset(140, 2);
                    progress.ReportFiles(140, 1, 2);
                    var frame = CheckPercentage(84);
                    for (var id = 60; id <= 130; id += 10)
                        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(frame, $@"\bC{id}\b[^\r\n]*100%"));
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(frame, @"\bC50\b"));
                    progress.CompleteChangeset(140, "abcdef1234");
                    progress.SkipChangeset(150);
                    progress.StartChangeset(160, 4);
                    progress.ReportFiles(160, 1, 4);
                    CheckPercentage(95);
                    progress.CompleteChangeset(160, "fedcba4321");
                    return 0;
                });
                StringAssert.Contains(output.ToString(), "Changesets · Complete");
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(output.ToString(), @"Overall[^\r\n]*100%"));
            }
            finally { AnsiConsole.Console = original; }

            string CheckPercentage(int percentage)
            {
                string frame = null;
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                {
                    frame = LastImportFrame(output.ToString());
                    return System.Text.RegularExpressions.Regex.IsMatch(frame, $@"Overall[^\r\n]*{percentage}%")
                        && frame.Contains("Rate");
                }, TimeSpan.FromSeconds(3)), "Overall must include the completed baseline and partial current changeset.");
                return frame;
            }
        }

        [TestMethod]
        public void ResumedOverallDoesNotCountRepeatedSkippedChangesetsTwice()
        {
            using var output = new LockedStringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 120;
            var original = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.ReportResume(new[] { 1, 2, 3 });
                    for (var id = 4; id <= 5; id++)
                        progress.ReportScan(1, id - 3, 2, new RestChangesetReference { ChangesetId = id });
                    progress.CompleteScan(2);
                    CheckPercentage(60);
                    progress.DescribeChangeset(3, "repeated skip");
                    progress.SkipChangeset(3);
                    var repeated = CheckPercentage(60);
                    Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(repeated, @"\bC3\b").Count);
                    progress.StartChangeset(4, 2);
                    progress.ReportFiles(4, 1, 2);
                    CheckPercentage(70);
                    progress.CompleteChangeset(4, "abcdef1234");
                    CheckPercentage(80);
                    progress.SkipChangeset(5);
                    return 0;
                });
                var text = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                var final = text[text.LastIndexOf("Changesets · Complete", StringComparison.Ordinal)..];
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(final, @"Overall[^\r\n]*100%"));
            }
            finally { AnsiConsole.Console = original; }

            string CheckPercentage(int percentage)
            {
                string frame = null;
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                {
                    frame = LastImportFrame(output.ToString());
                    return System.Text.RegularExpressions.Regex.IsMatch(frame, $@"Overall[^\r\n]*{percentage}%")
                        && frame.Contains("Rate");
                }, TimeSpan.FromSeconds(3)), "Restored skips must count exactly once, including partial current progress.");
                return frame;
            }
        }

        [TestMethod]
        public void InterruptedResumeScanDoesNotInventACompletedPercentage()
        {
            using var output = new LockedStringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            var original = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.ReportResume(new[] { 1, 2 });
                    progress.ReportScan(1, 0, 2);
                    return 1;
                });
                var text = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                var final = text[text.LastIndexOf("Changesets · Failed", StringComparison.Ordinal)..];
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(final, @"Overall[^\r\n]*\.\.\."));
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(final, @"Overall[^\r\n]*100%"));
            }
            finally { AnsiConsole.Console = original; }
        }

        [TestMethod]
        public void ResumedOverallWithNoRemainingChangesetsIsComplete()
        {
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            var original = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.ReportResume(new[] { 10, 70 });
                    progress.CompleteScan(0);
                    return 0;
                });
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(output.ToString(), @"Overall[^\r\n]*100%"));
            }
            finally { AnsiConsole.Console = original; }
        }

        [TestMethod]
        public void OverallPercentageIncludesSkippedChangesetsAndPartialFileProgress()
        {
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 180;
            var originalConsole = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.CompleteScan(4);
                    progress.StartChangeset(1, 1);
                    progress.CompleteChangeset(1, "abcdef1234");
                    progress.SkipChangeset(2);
                    progress.StartChangeset(3, 4);
                    progress.ReportFiles(3, 1, 4);
                    return 1;
                });
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(output.ToString(), @"Overall[^\r\n]*56%"),
                    "Two finished changesets and one quarter of the next must show 56% of four changesets.");
                StringAssert.Contains(output.ToString(), "Changesets · Failed");
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(output.ToString(), @"×[^\r\n]*Overall"),
                    "A partial failed operation must not be shown with a completion checkmark.");
            }
            finally { AnsiConsole.Console = originalConsole; }
        }

        [TestMethod]
        public void FileProgressWaitsForTheCommitAndVerificationHasAClearPhase()
        {
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            console.Profile.Width = 180;
            var originalConsole = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    progress.CompleteScan(1);
                    progress.StartChangeset(42, 1);
                    progress.ReportFiles(42, 1, 1);
                    Thread.Sleep(350);
                    progress.CompleteChangeset(42, "abcdef1234");
                    progress.ReportActivity("Verifying latest TFVC file checksums");
                    Thread.Sleep(350);
                    return 0;
                });
                var text = output.ToString();
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(text, @"C42[^\r\n]*99%"));
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(text, @"C42[^\r\n]*100%"));
                StringAssert.Contains(text, "Changesets · Verifying");
                StringAssert.Contains(text, "Changesets · Complete");
            }
            finally { AnsiConsole.Console = originalConsole; }
        }

        [TestMethod]
        public void StaticDisplayShowsChangesetIdsWithoutComments()
        {
            using var output = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                Interactive = InteractionSupport.No,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(output)
            });
            var originalConsole = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = console;
                SpectreCloneProgress.Run(progress =>
                {
                    const string comment = "Release [blue] build";
                    progress.ReportScan(1, 1, 0, new RestChangesetReference { ChangesetId = 42, Comment = comment });
                    progress.CompleteScan(1);
                    progress.DescribeChangeset(42, comment);
                    progress.StartChangeset(42, 2);
                    progress.ReportFiles(42, 1, 2);
                    progress.ReportActivity("0 committed so far");
                    progress.CompleteChangeset(42, "abcdef1234");
                    return 0;
                });
                var text = output.ToString();
                StringAssert.Contains(text, "C42");
                StringAssert.Contains(text, "0%");
                StringAssert.Contains(text, "50%");
                StringAssert.Contains(text, "100%");
                Assert.IsFalse(text.Contains("abcdef1"));
                Assert.IsFalse(text.Contains("committed so far"));
                Assert.IsFalse(text.Contains("Release"), "Changeset comments must not appear in the static view.");
            }
            finally { AnsiConsole.Console = originalConsole; }
        }

        [TestMethod]
        public void InclusiveScanReportsActivityBeforeRequestsAndCountsEachChangesetOnce()
        {
            var progress = new ScanRecorder();
            var client = new Mock<IRestTfsClient>();
            client.Setup(c => c.GetChangesets("$/Project/Main", 1, 2)).Returns(() =>
            {
                Assert.AreEqual((1, 0, 1), progress.Requests.Single());
                return new[] { new RestChangesetReference { ChangesetId = 1 },
                    new RestChangesetReference { ChangesetId = 2, Comment = "Rename files" } };
            });
            client.Setup(c => c.GetChangesets("$/Project/Main", 3, 2)).Returns(Array.Empty<RestChangesetReference>());
            var changes = new RestChangesetScanner(client.Object, null).Scan("$/Project/Main", 1, 2, progress).ToArray();
            Assert.AreEqual(1, changes.Length);
            Assert.AreEqual("Rename files", progress.Found.Single().Comment);
            Assert.AreEqual(1, progress.CompletedCount);
            Assert.AreEqual((2, 1, 3), progress.Requests.Last());
        }

        private sealed class ScanRecorder : IChangesetProgressReporter
        {
            public List<(int Page, int Found, int Cursor)> Requests { get; } = new();
            public List<RestChangesetReference> Found { get; } = new();
            public int CompletedCount { get; private set; } = -1;
            public void ReportScan(int page, int found, int cursor, RestChangesetReference latest = null)
            {
                if (latest == null) Requests.Add((page, found, cursor));
                else Found.Add(latest);
            }
            public void CompleteScan(int found) => CompletedCount = found;
            public void StartChangeset(int changesetId, int totalFiles) { }
            public void ReportFiles(int changesetId, int processedFiles, int totalFiles) { }
            public void CompleteChangeset(int changesetId, string commitSha) { }
        }
    }
}
