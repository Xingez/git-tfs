namespace GitTfs.Core.RestTfs
{
    using System.Text.Json.Serialization;

    public sealed record RestPage<T>
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new List<T>();
    }

    public sealed record RestChangesetReference
    {
        public int ChangesetId { get; set; }
        public DateTimeOffset CreatedDate { get; set; }
        public string Comment { get; set; }
        public RestIdentity Author { get; set; }
    }

    public sealed record RestChangeset
    {
        public int ChangesetId { get; set; }
        public DateTimeOffset CreatedDate { get; set; }
        public string Comment { get; set; }
        public RestIdentity Author { get; set; }
        public RestIdentity CheckedInBy { get; set; }
        public List<RestChange> Changes { get; set; } = new List<RestChange>();
        public bool HasMoreChanges { get; set; }
    }

    public sealed record RestChange
    {
        public string ChangeType { get; set; }
        public RestItem Item { get; set; }
        public string SourceServerItem { get; set; }
        public string Url { get; set; }
        public List<RestMergeSource> MergeSources { get; set; } = new List<RestMergeSource>();
    }

    public sealed record RestMergeSource
    {
        public bool IsRename { get; set; }
        public string ServerItem { get; set; }
        public int VersionFrom { get; set; }
        public int VersionTo { get; set; }
    }

    public sealed record RestItem
    {
        public int Version { get; set; }
        public DateTimeOffset ChangeDate { get; set; }
        public long Size { get; set; }
        public string HashValue { get; set; }
        public string Path { get; set; }
        public bool IsFolder { get; set; }
        public int DeletionId { get; set; }
        public string Url { get; set; }
    }

    public sealed record RestIdentity
    {
        public string DisplayName { get; set; }
        public string UniqueName { get; set; }
        public string Id { get; set; }
    }

    public sealed record RestResponse<T>(T Value, IReadOnlyDictionary<string, string> Headers, int StatusCode);
}
