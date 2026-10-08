namespace GitTfs.LegacyHistory
{
    using System;

    internal sealed class HistoryChangeset
    {
        public string Type { get; } = "changeset";
        public int ChangesetId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Comment { get; set; }
        public HistoryIdentity Author { get; set; }
    }
}
