namespace GitTfs.LegacyHistory
{
    internal sealed class FileContent
    {
        public string Type { get; } = "file";
        public string Content { get; set; }
    }
}
