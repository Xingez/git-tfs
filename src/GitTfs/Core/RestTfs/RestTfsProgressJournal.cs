namespace GitTfs.Core.RestTfs
{
    using Microsoft.Extensions.Logging;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    // Presentation state only: it never changes which changesets the importer processes.
    internal sealed class RestTfsProgressJournal(string gitDirectory, string server, string repositoryPath, ILogger logger)
    {
        private readonly string path = Path.Combine(gitDirectory, "git-tfs-progress.log");
        private readonly string source = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(server.ToLowerInvariant() + "\n" + repositoryPath.ToLowerInvariant())));
        private bool writable = true;
        public SortedSet<int> Completed { get; } = new();

        public bool Restore(string head, int lastChangesetId)
        {
            try
            {
                using var reader = File.OpenText(path);
                var header = reader.ReadLine()?.Split(' ');
                if (header?.Length != 2 || header[0] != source || !ValidHead(header[1])) return false;
                var lastHead = header[1];
                var ids = new SortedSet<int>();
                while (reader.ReadLine() is { } line)
                {
                    var parts = line.Split(' ');
                    if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        || id <= 0 || !ValidHead(parts[1])) return false;
                    ids.Add(id);
                    lastHead = parts[1];
                }
                // A reset may move HEAD behind the saved progress. Discard the later portion.
                if (lastHead != (head ?? "-")) ids.RemoveWhere(id => id > lastChangesetId);
                if (lastChangesetId > 0 && !ids.Contains(lastChangesetId)) return false;
                Completed.UnionWith(ids);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public void Reset(string head, IEnumerable<int> completed)
        {
            Completed.Clear();
            Completed.UnionWith(completed);
            Write(() =>
            {
                var temporary = path + ".tmp";
                using (var writer = File.CreateText(temporary))
                {
                    writer.WriteLine(source + " " + (head ?? "-"));
                    foreach (var id in Completed) writer.WriteLine(Record(id, head));
                }
                File.Move(temporary, path, overwrite: true);
            });
        }

        public void Complete(int id, string head)
        {
            if (Completed.Add(id)) Write(() => File.AppendAllText(path, Record(id, head) + "\n"));
        }

        private void Write(Action write)
        {
            if (!writable) return;
            try { write(); }
            catch (IOException exception) { Disable(exception); }
            catch (UnauthorizedAccessException exception) { Disable(exception); }
        }

        private void Disable(Exception exception)
        {
            writable = false;
            logger?.LogDebug(exception, "Unable to save clone progress; the next resume will reconstruct it from TFVC.");
        }

        private static string Record(int id, string head) => id.ToString(CultureInfo.InvariantCulture) + " " + (head ?? "-");
        private static bool ValidHead(string head) => head == "-" || head.Length == 40 && head.All(Uri.IsHexDigit);
    }
}
