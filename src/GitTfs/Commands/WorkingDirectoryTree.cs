namespace GitTfs.Commands
{
    using Spectre.Console;
    using Spectre.Console.Rendering;

    internal sealed class WorkingDirectoryTree(string path, TimeProvider clock = null)
    {
        private const int MaxNodes = 20;
        private readonly string root = Path.GetFullPath(path);
        private readonly TimeProvider clock = clock ?? TimeProvider.System;
        private IRenderable cached;
        private long refreshed;

        public IRenderable Render(bool refresh = false)
        {
            if (!refresh && cached != null && clock.GetElapsedTime(refreshed) < TimeSpan.FromSeconds(1))
                return cached;

            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            var tree = new Tree(new Text(string.Empty));
            var remaining = MaxNodes;
            if (Directory.Exists(root))
                AddChildren(tree.AddNode, root, ref remaining);
            else
                tree.AddNode(new Text("Waiting for folder", new Style(Color.Grey)));
            cached = new Panel(new TreeContents(tree)).RoundedBorder()
                .Header(Markup.Escape(string.IsNullOrEmpty(name) ? root : name), Justify.Center);
            refreshed = clock.GetTimestamp();
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
                var entries = Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
                }).Where(entry => !string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase))
                    .Take(remaining + 1).OrderBy(entry => !Directory.Exists(entry))
                    .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    if (remaining == 0)
                    {
                        addNode(new Text("… more", new Style(Color.Grey)));
                        break;
                    }
                    remaining--;
                    var isDirectory = Directory.Exists(entry);
                    var node = addNode(new Text(Path.GetFileName(entry), isDirectory ? new Style(Color.Cyan) : Style.Plain).Ellipsis());
                    if (isDirectory) AddChildren(node.AddNode, entry, ref remaining);
                }
            }
            catch (IOException) { addNode(new Text("Updating…", new Style(Color.Grey))); }
            catch (UnauthorizedAccessException) { addNode(new Text("Unavailable", new Style(Color.Grey))); }
        }
    }
}
