namespace GitTfs.Commands
{
    using Spectre.Console;
    using Spectre.Console.Rendering;
    using System.Globalization;

    internal sealed class WorkingDirectoryTree(string path, TimeProvider clock = null)
    {
        private const int MaxNodes = 20;
        private static readonly EnumerationOptions Entries = new()
        {
            IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
        };
        private readonly string root = Path.GetFullPath(path);
        private readonly TimeProvider clock = clock ?? TimeProvider.System;
        private IRenderable cached;
        private long refreshed;
        private int cachedNodeLimit;

        public IRenderable Render(bool refresh = false, int maxNodes = MaxNodes)
        {
            maxNodes = Math.Clamp(maxNodes, 1, MaxNodes);
            if (!refresh && cached != null && cachedNodeLimit == maxNodes && clock.GetElapsedTime(refreshed) < TimeSpan.FromSeconds(1))
                return cached;

            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            var tree = new Tree(new Text(string.Empty));
            var remaining = maxNodes;
            var exists = Directory.Exists(root);
            if (exists)
                AddChildren(tree.AddNode, root, ref remaining);
            else
                tree.AddNode(new Text("Waiting for folder", new Style(Color.Grey)));
            IRenderable contents = exists ? new Rows(FileSummary(root), new TreeContents(tree)) : new TreeContents(tree);
            cached = new Panel(contents).RoundedBorder()
                .Header(Markup.Escape(string.IsNullOrEmpty(name) ? root : name), Justify.Center);
            refreshed = clock.GetTimestamp();
            cachedNodeLimit = maxNodes;
            return cached;
        }

        private sealed class TreeContents(Tree tree) : IRenderable
        {
            public Measurement Measure(RenderOptions options, int maxWidth)
                => ((IRenderable)tree).Measure(options, maxWidth);

            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
                => ((IRenderable)tree).Render(options, maxWidth).SkipWhile(segment => !segment.IsLineBreak).Skip(1);
        }

        private static void AddChildren(Func<IRenderable, TreeNode> addNode, string directory, ref int remaining)
        {
            try
            {
                var entries = Directory.EnumerateDirectories(directory, "*", Entries)
                    .Where(entry => !string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase))
                    .Take(remaining + 1).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    if (remaining == 0)
                    {
                        addNode(new Text("… more", new Style(Color.Grey)));
                        break;
                    }
                    remaining--;
                    var node = addNode(new Rows(new Text(Path.GetFileName(entry), new Style(Color.Cyan)).Ellipsis(), FileSummary(entry)));
                    AddChildren(node.AddNode, entry, ref remaining);
                }
            }
            catch (IOException) { addNode(new Text("Updating…", new Style(Color.Grey))); }
            catch (UnauthorizedAccessException) { addNode(new Text("Unavailable", new Style(Color.Grey))); }
        }

        private static Text FileSummary(string directory)
        {
            try
            {
                long count = 0, bytes = 0;
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", Entries))
                {
                    if (string.Equals(file.Name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
                    try { bytes += file.Length; count++; }
                    catch (IOException) { } // Files may move while the importer updates the folder.
                }
                return new Text($"{count} {(count == 1 ? "file" : "files")} · {Size(bytes)}", new Style(Color.Grey)).Ellipsis();
            }
            catch (IOException) { return new Text("Updating…", new Style(Color.Grey)); }
            catch (UnauthorizedAccessException) { return new Text("Unavailable", new Style(Color.Grey)); }
        }

        private static string Size(long bytes)
        {
            double value = bytes;
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
