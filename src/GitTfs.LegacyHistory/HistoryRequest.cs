namespace GitTfs.LegacyHistory
{
    internal sealed class HistoryRequest
    {
        public string Operation { get; set; }
        public string ServerUrl { get; set; }
        public string ItemPath { get; set; }
        public int ChangesetId { get; set; }
        public int DeletionId { get; set; }
        public string Pat { get; set; }
    }
}
