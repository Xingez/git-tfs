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
        public void DashboardKeepsMetricsOnTheLeftAndOnlyTenRecentChangesets(int width)
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
                    progress.CompleteScan(13);
                    for (var id = 1; id <= 12; id++)
                    {
                        progress.StartChangeset(id, 1);
                        progress.CompleteChangeset(id, "abcdef1234");
                    }
                    progress.SkipChangeset(13);
                    return 0;
                });
                var text = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                var finalFrame = text[text.LastIndexOf("Live metrics", StringComparison.Ordinal)..];
                var header = finalFrame.Split('\n')[0];
                Assert.IsTrue(header.IndexOf("Changesets", StringComparison.Ordinal) > 0,
                    "Metrics and changesets must appear side by side, with metrics on the left.");
                for (var id = 1; id <= 3; id++)
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(finalFrame, $@"\bC{id}\b"));
                for (var id = 4; id <= 13; id++)
                    Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(finalFrame, $@"\bC{id}\b"));
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(finalFrame, @"Overall[^\r\n]*100%"));
            }
            finally { AnsiConsole.Console = originalConsole; }
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
