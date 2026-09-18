namespace GitTfs.Core.RestTfs
{
    using global::System.Text.Json.Serialization;

    public sealed class RestPage<T>
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new List<T>();
    }

    public sealed class RestChangesetReference
    {
        public int ChangesetId { get; set; }
        public DateTimeOffset CreatedDate { get; set; }
        public string Comment { get; set; }
        public RestIdentity Author { get; set; }
    }

    public sealed class RestChangeset
    {
        public int ChangesetId { get; set; }
        public DateTimeOffset CreatedDate { get; set; }
        public string Comment { get; set; }
        public RestIdentity Author { get; set; }
        public RestIdentity CheckedInBy { get; set; }
        public List<RestChange> Changes { get; set; } = new List<RestChange>();
        public bool HasMoreChanges { get; set; }
    }

    public sealed class RestChange
    {
        public string ChangeType { get; set; }
        public RestItem Item { get; set; }
        public string SourceServerItem { get; set; }
        public string Url { get; set; }
        public List<RestMergeSource> MergeSources { get; set; } = new List<RestMergeSource>();
    }

    public sealed class RestMergeSource
    {
        public bool IsRename { get; set; }
        public string ServerItem { get; set; }
        public int VersionFrom { get; set; }
        public int VersionTo { get; set; }
    }

    public sealed class RestItem
    {
        public int Version { get; set; }
        public long Size { get; set; }
        public string HashValue { get; set; }
        public string Path { get; set; }
        public bool IsFolder { get; set; }
        public int DeletionId { get; set; }
        public string Url { get; set; }
    }

    public sealed class RestIdentity
    {
        public string DisplayName { get; set; }
        public string UniqueName { get; set; }
        public string Id { get; set; }
    }

    public sealed class RestResponse<T>
    {
        public RestResponse(T value, IReadOnlyDictionary<string, string> headers, int statusCode)
        {
            Value = value;
            Headers = headers;
            StatusCode = statusCode;
        }

        public T Value { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public int StatusCode { get; }
    }
}
