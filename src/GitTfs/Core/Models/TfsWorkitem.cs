
namespace GitTfs.Core
{
    public sealed record TfsWorkitem : ITfsWorkitem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
    }
}
