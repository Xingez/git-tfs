namespace GitTfs.Commands
{
    using Spectre.Console;
    using Spectre.Console.Rendering;

    internal sealed class WorkingDirectoryTree(string path, TimeProvider clock = null)
    {
        private const int MaxNodes = 20;
        private static readonly EnumerationOptions Entries = new()
        {
            IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
        };
        private readonly string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        private readonly TimeProvider clock = clock ?? TimeProvider.System;
        private IRenderable cached;
        private long refreshed;
        private int cachedNodeLimit;
        private int cachedDepthLimit;
        private string latestDownload;

        public void ReportDownloadedFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return;
            var parts = relativePath.Replace('\\', '/').Split('/');
            if (parts.Any(part => part is ".." or "." or "" || part.Equals(".git", StringComparison.OrdinalIgnoreCase))) return;
            Volatile.Write(ref latestDownload, Path.Combine(root, Path.Combine(parts)));
        }

        public IRenderable Render(bool refresh = false, int maxNodes = MaxNodes, int maxDepth = MaxNodes)
        {
            maxNodes = Math.Clamp(maxNodes, 1, MaxNodes);
            maxDepth = Math.Clamp(maxDepth, 1, MaxNodes);
            if (!refresh && cached != null && cachedNodeLimit == maxNodes
                && cachedDepthLimit == maxDepth && clock.GetElapsedTime(refreshed) < TimeSpan.FromSeconds(1))
                return cached;

            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            var tree = new Tree(new Text(string.Empty));
            var remaining = maxNodes;
            var omitted = false;
            var exists = Directory.Exists(root);
            var activeFile = VisibleDownload(Volatile.Read(ref latestDownload));
            if (exists)
            {
                if (activeFile != null && Path.GetDirectoryName(activeFile) == root)
                    tree.AddNode(LatestFile(activeFile));
                AddChildren(tree.AddNode, root, activeFile, ref remaining, maxDepth, ref omitted);
                if (omitted) tree.AddNode(new Text("… more", new Style(Color.Grey)));
            }
            else
                tree.AddNode(new Text("Waiting for folder", new Style(Color.Grey)));
            cached = new Panel(new TreeContents(tree)).RoundedBorder().Expand()
                .Header(Markup.Escape(string.IsNullOrEmpty(name) ? root : name), Justify.Center);
            refreshed = clock.GetTimestamp();
            cachedNodeLimit = maxNodes;
            cachedDepthLimit = maxDepth;
            return cached;
        }

        private sealed class TreeContents(Tree tree) : IRenderable
        {
            public Measurement Measure(RenderOptions options, int maxWidth)
                => ((IRenderable)tree).Measure(options, maxWidth);

            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
                => ((IRenderable)tree).Render(options, maxWidth).SkipWhile(segment => !segment.IsLineBreak).Skip(1);
        }

        // Text.Ellipsis still wraps at spaces; directory and file names must occupy exactly one line.
        private sealed class SingleLine(string text, Style style) : IRenderable
        {
            private readonly Segment segment = new(text, style);

            public Measurement Measure(RenderOptions options, int maxWidth)
                => new(0, Math.Min(maxWidth, segment.CellCount()));

            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
            {
                if (maxWidth <= 0) yield break;
                if (segment.CellCount() <= maxWidth) { yield return segment; yield break; }
                if (maxWidth > 1) yield return Segment.Truncate(segment, maxWidth - 1);
                yield return new Segment("…", style);
            }
        }

        private static IRenderable LatestFile(string path)
            => new SingleLine("> " + Path.GetFileName(path), new Style(Color.Green, decoration: Decoration.Bold));

        private static void AddChildren(Func<IRenderable, TreeNode> addNode, string directory, string activeFile,
            ref int remaining, int depth, ref bool omitted)
        {
            try
            {
                var activeChild = ActiveChild(directory, activeFile);
                var entries = Subdirectories(directory)
                    .Where(entry => !string.Equals(entry, activeChild, StringComparison.OrdinalIgnoreCase))
                    .Take(remaining + 1).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).AsEnumerable();
                // Keep the downloaded file visible even when its folder is beyond the ordinary row limit.
                if (activeChild != null && (File.GetAttributes(activeChild) & FileAttributes.ReparsePoint) == 0)
                    entries = new[] { activeChild }.Concat(entries);
                foreach (var entry in entries)
                {
                    if (remaining == 0 || depth == 0)
                    {
                        omitted = true;
                        break;
                    }
                    var focused = string.Equals(entry, activeChild, StringComparison.OrdinalIgnoreCase);
                    var (folder, label) = CompactFolder(entry, focused ? activeFile : null, Math.Min(remaining, depth));
                    remaining--;
                    var node = addNode(new SingleLine(label, new Style(Color.Cyan, decoration: focused ? Decoration.Bold : Decoration.None)));
                    if (focused && Path.GetDirectoryName(activeFile) == folder) node.AddNode(LatestFile(activeFile));
                    AddChildren(node.AddNode, folder, focused ? activeFile : null, ref remaining, depth - 1, ref omitted);
                }
            }
            catch (IOException) { addNode(new Text("Updating…", new Style(Color.Grey))); }
            catch (UnauthorizedAccessException) { addNode(new Text("Unavailable", new Style(Color.Grey))); }
        }

        private static IEnumerable<string> Subdirectories(string directory)
            => Directory.EnumerateDirectories(directory, "*", Entries)
                .Where(entry => !string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase));

        private static string ActiveChild(string directory, string activeFile)
        {
            if (activeFile == null) return null;
            var relative = Path.GetRelativePath(directory, activeFile);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            return parts.Length > 1 ? Path.Combine(directory, parts[0]) : null;
        }

        private string VisibleDownload(string download)
        {
            if (!File.Exists(download)) return null;
            try
            {
                for (var directory = Path.GetDirectoryName(download); directory != root; directory = Path.GetDirectoryName(directory))
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return null;
                return (File.GetAttributes(download) & FileAttributes.ReparsePoint) == 0 ? download : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static (string Folder, string Label) CompactFolder(string directory, string activeFile, int remaining)
        {
            var label = Path.GetFileName(directory);
            if (activeFile != null)
            {
                var ancestors = Path.GetRelativePath(directory, Path.GetDirectoryName(activeFile))
                    .Split(Path.DirectorySeparatorChar).Where(part => part != ".").ToArray();
                // Compress only the excess depth so the latest file still fits in short terminals.
                foreach (var part in ancestors.Take(Math.Max(0, ancestors.Length + 1 - remaining)))
                {
                    directory = Path.Combine(directory, part);
                    label += "/" + part;
                }
                return (directory, label);
            }
            while (!Directory.EnumerateFiles(directory, "*", Entries)
                .Any(file => !string.Equals(Path.GetFileName(file), ".git", StringComparison.OrdinalIgnoreCase)))
            {
                var children = Subdirectories(directory).Take(2).ToArray();
                if (children.Length != 1) break;
                directory = children[0];
                label += "/" + Path.GetFileName(directory);
            }
            return (directory, label);
        }
    }
}
