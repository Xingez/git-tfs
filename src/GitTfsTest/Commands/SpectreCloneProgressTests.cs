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
                Assert.IsTrue(frames.Split("Scanning TFVC").Length > 3,
                    "The live display must refresh independently of the blocked request.");
                StringAssert.Contains(frames, "Scanning TFVC");
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
