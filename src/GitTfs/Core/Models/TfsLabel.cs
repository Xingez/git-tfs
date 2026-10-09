
namespace GitTfs.Core
{
    public sealed record TfsLabel
    {
        public int Id { get; set; }
        public int ChangesetId { get; set; }
        public string Name { get; set; }
        public string Comment { get; set; }
        public string Owner { get; set; }
        public System.DateTime Date { get; set; }
        public bool IsTransBranch { get; set; }
    }
}
