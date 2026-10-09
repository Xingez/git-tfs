namespace GitTfs.Core.RestTfs
{
    /// <summary>Distinguishes target mutations from branch, merge, and rename provenance.</summary>
    internal readonly struct TfvcChange
    {
        [Flags]
        private enum ChangeKind { None = 0, Delete = 1, Rename = 2, SourceRename = 4, Merge = 8, Branch = 16 }

        private readonly RestChange change;
        private readonly ChangeKind kind;

        public TfvcChange(RestChange change)
        {
            this.change = change;
            kind = ChangeKind.None;
            foreach (var token in (change?.ChangeType ?? string.Empty).Split(','))
                if (Enum.TryParse<ChangeKind>(token.Trim(), ignoreCase: true, out var flag))
                    kind |= flag;
        }

        public bool IsDelete => (kind & ChangeKind.Delete) != 0;
        public bool IsRename => (kind & ChangeKind.Rename) != 0;
        public bool IsSourceRename => (kind & ChangeKind.SourceRename) != 0;
        public bool IsMerge => (kind & ChangeKind.Merge) != 0
            || (kind & ChangeKind.Branch) == 0 && !IsRename && HasSourceMetadata;
        public bool HasMergeSource => HasSourceMetadata || IsMerge || IsRename;
        private bool HasSourceMetadata => change?.MergeSources?.Any(source => source != null) == true;

        public IEnumerable<string> RenameSources()
        {
            // SourceServerItem also describes branch copies. Only a rename moves its source.
            if (!IsRename)
                return Enumerable.Empty<string>();

            var sources = new[] { change.SourceServerItem }
                .Concat((change.MergeSources ?? Enumerable.Empty<RestMergeSource>())
                    .Where(source => source?.IsRename == true).Select(source => source.ServerItem));
            return sources.Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsRelevantTo(string repositoryPath)
            => change?.Item != null && (IsWithin(change.Item.Path, repositoryPath)
                || RenameSources().Any(source => IsWithin(source, repositoryPath)));

        private static bool IsWithin(string path, string root)
            => !string.IsNullOrWhiteSpace(path) && (path.Equals(root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }
}
