namespace GitTfs.Test.Core
{
    using global::GitTfs.Core.RestTfs;
    using global::GitTfs.Util;
    using LibGit2Sharp;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using System.Diagnostics.Metrics;
    using System.Text;
    using Assert = global::GitTfs.Test.TestAssert;

    [TestClass]
    public class RestTfsChangesetImporterTests
    {
        private const string Root = "$/Project/Main";
        private string output;
        private Repository repository;
        private Mock<IRestTfsClient> client;
        private RestTfsChangesetImporter importer;
        private IDictionary<string, string> paths;

        [TestInitialize]
        public void Initialize()
        {
            output = Path.Combine(Path.GetTempPath(), "git-tfs-import-" + Guid.NewGuid().ToString("N"));
            Repository.Init(output);
            repository = new Repository(output);
            client = new Mock<IRestTfsClient>(MockBehavior.Strict);
            importer = new RestTfsChangesetImporter(new AuthorsFile());
            paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        [TestCleanup]
        public void Cleanup()
        {
            repository?.Dispose();
            foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(output, true);
        }

        [TestMethod]
        public void BranchingOutOfTheImportedFolderDoesNotDeleteItsSourceTree()
        {
            var first = Import(1, FileChange("add, edit, encoding", Root + "/a.txt", "original"));
            var branchedFolder = new RestChange
            {
                ChangeType = "encoding, branch",
                Item = new RestItem { Path = "$/Project/Branches/Feature", IsFolder = true },
                SourceServerItem = Root,
                MergeSources = new() { new() { ServerItem = Root, VersionFrom = 1, VersionTo = 1 } }
            };
            var branchedFile = new RestChange
            {
                ChangeType = "encoding, branch",
                Item = new RestItem { Path = "$/Project/Branches/Feature/a.txt" },
                SourceServerItem = Root + "/a.txt"
            };
            var result = Import(2, branchedFolder, branchedFile);

            Assert.True(result.Skipped);
            Assert.Equal(first.Commit.Sha, repository.Head.Tip.Sha);
            Assert.Equal("original", File.ReadAllText(Path.Combine(output, "a.txt")));
        }

        [TestMethod]
        public void EmitsPerformanceMetricsForImportedChangesets()
        {
            var counters = new Dictionary<string, long>(StringComparer.Ordinal);
            var durations = new List<double>();
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Name == "GitTfs")
                        meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            {
                counters.TryGetValue(instrument.Name, out var current);
                counters[instrument.Name] = current + measurement;
            });
            listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
                durations.Add(measurement));
            listener.Start();

            var result = Import(1, FileChange("add", Root + "/metrics.txt", "metrics"));

            Assert.False(result.Skipped);
            Assert.Equal(1L, counters["gittfs.changesets.imported"]);
            Assert.Equal(1L, counters["gittfs.files.processed"]);
            Assert.Equal(1L, counters["gittfs.files.downloaded"]);
            Assert.Equal(Encoding.UTF8.GetByteCount("metrics"), counters["gittfs.file.bytes.downloaded"]);
            Assert.Equal(1, durations.Count);
            Assert.True(durations[0] >= 0);
        }

        [TestMethod]
        public void MixedBranchAndEditChangesPreserveUntouchedSourceFiles()
        {
            Import(1, FileChange("add", Root + "/a.txt", "first"), FileChange("add", Root + "/b.txt", "keep"));
            var branch = new RestChange
            {
                ChangeType = "branch, encoding",
                Item = new RestItem { Path = "$/Project/Branches/Feature", IsFolder = true },
                SourceServerItem = Root,
                MergeSources = new() { new() { ServerItem = Root } }
            };
            var result = Import(2, branch, FileChange("edit", Root + "/a.txt", "second"));

            Assert.Equal("second", ReadBlob(result.Commit, "a.txt"));
            Assert.Equal("keep", ReadBlob(result.Commit, "b.txt"));
        }

        [TestMethod]
        public void PairedSourceRenameAndRenameMoveAFileWithoutDownloadingTheDeletedSource()
        {
            Import(1, FileChange("add", Root + "/old.txt", "original"));
            var source = new RestChange { ChangeType = "delete, sourceRename", Item = new RestItem { Path = Root + "/old.txt" } };
            var destination = FileChange("rename", Root + "/folder/new.txt", "original");
            destination.SourceServerItem = source.Item.Path;
            destination.MergeSources = new() { new() { ServerItem = source.Item.Path, IsRename = true } };
            var result = Import(2, source, destination);

            Assert.Null(result.Commit.Tree["old.txt"]);
            Assert.Equal("original", ReadBlob(result.Commit, "folder/new.txt"));
            client.Verify(c => c.DownloadFile(Root + "/old.txt", 2, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public void BranchingIntoTheImportedFolderKeepsTheSourceAndDownloadsTheTarget()
        {
            var change = FileChange("branch, encoding", Root + "/copy.txt", "copy");
            change.SourceServerItem = "$/Project/Other/original.txt";
            change.MergeSources = new() { new() { ServerItem = change.SourceServerItem } };
            var result = Import(1, change);

            Assert.False(result.Skipped);
            Assert.Equal("copy", ReadBlob(result.Commit, "copy.txt"));
        }

        [TestMethod]
        public void RenameOutOfTheImportedFolderRemovesOnlyTheMovedFile()
        {
            Import(1, FileChange("add", Root + "/a.txt", "move"), FileChange("add", Root + "/b.txt", "keep"));
            var move = new RestChange
            {
                ChangeType = "rename",
                Item = new RestItem { Path = "$/Project/Other/a.txt" },
                SourceServerItem = Root + "/a.txt"
            };
            var result = Import(2, move);

            Assert.Null(result.Commit.Tree["a.txt"]);
            Assert.Equal("keep", ReadBlob(result.Commit, "b.txt"));
        }

        [TestMethod]
        public void RenameWithSourceMetadataTriesThePreviousVersionInsteadOfSkippingTheFile()
        {
            Import(1, FileChange("add", Root + "/old.txt", "original"));
            var destination = FileChange("rename", Root + "/new.txt", "original");
            destination.SourceServerItem = Root + "/old.txt";
            destination.MergeSources = new() { new() { IsRename = true, ServerItem = destination.SourceServerItem } };
            client.Setup(c => c.DownloadFile(destination.Item.Path, 2, "Changeset", null))
                .Throws(new RestTfsException("not found", 404, new Uri("https://tfs.example/items")));
            client.Setup(c => c.DownloadFile(destination.Item.Path, 2, "Changeset", "Previous"))
                .Returns(Encoding.UTF8.GetBytes("original"));

            var result = Import(2, destination);

            Assert.Null(result.Commit.Tree["old.txt"]);
            Assert.Equal("original", ReadBlob(result.Commit, "new.txt"));
            client.Verify(c => c.DownloadFile(destination.Item.Path, 2, "Changeset", "Previous"), Times.Once);
        }

        [TestMethod]
        public void NonNotFoundDownloadErrorsAreNotHiddenByVersionFallback()
        {
            var change = FileChange("add", Root + "/a.txt", "content");
            client.Setup(c => c.DownloadFile(change.Item.Path, 1, "Changeset", null))
                .Throws(new RestTfsException("server error", 500, new Uri("https://tfs.example/items")));

            Assert.Throws<RestTfsException>(() => Import(1, change));

            client.Verify(c => c.DownloadFile(change.Item.Path, 1, "Changeset", "Previous"), Times.Never);
            Assert.Null(repository.Head.Tip);
        }

        private RestChange FileChange(string type, string path, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            client.Setup(c => c.DownloadFile(path, It.IsAny<int>(), "Changeset", null)).Returns(bytes);
            return new RestChange { ChangeType = type, Item = new RestItem { Path = path } };
        }

        private RestTfsChangesetImportResult Import(int id, params RestChange[] changes)
        {
            client.Setup(c => c.GetChangeset(id)).Returns(new RestChangeset
            {
                ChangesetId = id,
                CreatedDate = new DateTimeOffset(2020, 1, id, 0, 0, 0, TimeSpan.Zero),
                Changes = changes.ToList()
            });
            return importer.Import(client.Object, repository, new RestChangesetReference { ChangesetId = id },
                "https://tfs.example/collection", Root, output, paths, repository.Head.Tip, noFallback: true);
        }

        private static string ReadBlob(Commit commit, string path)
            => ((Blob)commit.Tree[path].Target).GetContentText();
    }
}
