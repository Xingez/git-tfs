namespace GitTfs.LegacyHistory
{
    internal sealed class HistoryError
    {
        public string Type { get; } = "error";
        public string Message { get; set; }
    }
}
