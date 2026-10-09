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
        private readonly string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        private readonly TimeProvider clock = clock ?? TimeProvider.System;
        private IRenderable cached;
        private long refreshed;
        private int cachedNodeLimit;
        private int cachedDepthLimit;
        private string latestDownload;
        private string cachedDownload;

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
            var download = Volatile.Read(ref latestDownload);
            if (!refresh && cached != null && cachedDownload == download && cachedNodeLimit == maxNodes
                && cachedDepthLimit == maxDepth && clock.GetElapsedTime(refreshed) < TimeSpan.FromSeconds(1))
                return cached;

            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            var tree = new Tree(new Text(string.Empty));
            var remaining = maxNodes;
            var exists = Directory.Exists(root);
            var activeFile = VisibleDownload(download);
            if (exists)
                AddChildren(tree.AddNode, root, activeFile, ref remaining, maxDepth);
            else
                tree.AddNode(new Text("Waiting for folder", new Style(Color.Grey)));
            IRenderable contents = !exists ? new TreeContents(tree)
                : activeFile != null && Path.GetDirectoryName(activeFile) == root
                    ? new Rows(FileSummary(root), LatestFile(activeFile), new TreeContents(tree))
                    : new Rows(FileSummary(root), new TreeContents(tree));
            cached = new Panel(contents).RoundedBorder()
                .Header(Markup.Escape(string.IsNullOrEmpty(name) ? root : name), Justify.Center);
            refreshed = clock.GetTimestamp();
            cachedNodeLimit = maxNodes;
            cachedDepthLimit = maxDepth;
            cachedDownload = download;
            return cached;
        }

        private sealed class TreeContents(Tree tree) : IRenderable
        {
            public Measurement Measure(RenderOptions options, int maxWidth)
                => ((IRenderable)tree).Measure(options, maxWidth);

            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
                => ((IRenderable)tree).Render(options, maxWidth).SkipWhile(segment => !segment.IsLineBreak).Skip(1);
        }

        private static IRenderable LatestFile(string path)
        {
            try
            {
                return new Rows(new Text("> " + Path.GetFileName(path), new Style(Color.Green, decoration: Decoration.Bold)).Ellipsis(),
                    new Text("Latest · " + Size(new FileInfo(path).Length), new Style(Color.Green)).Ellipsis());
            }
            catch (IOException) { return new Text("Updating…", new Style(Color.Grey)); }
            catch (UnauthorizedAccessException) { return new Text("Unavailable", new Style(Color.Grey)); }
        }

        private static void AddChildren(Func<IRenderable, TreeNode> addNode, string directory, string activeFile, ref int remaining, int depth)
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
                        addNode(new Text("… more", new Style(Color.Grey)));
                        break;
                    }
                    var focused = string.Equals(entry, activeChild, StringComparison.OrdinalIgnoreCase);
                    var (folder, label) = CompactFolder(entry, focused ? activeFile : null, Math.Min(remaining, depth));
                    remaining--;
                    var rows = new List<IRenderable>
                    {
                        new Text(label, new Style(Color.Cyan, decoration: focused ? Decoration.Bold : Decoration.None)).Ellipsis(),
                        FileSummary(folder)
                    };
                    if (focused && Path.GetDirectoryName(activeFile) == folder) rows.Add(LatestFile(activeFile));
                    var node = addNode(new Rows(rows));
                    AddChildren(node.AddNode, folder, focused ? activeFile : null, ref remaining, depth - 1);
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
