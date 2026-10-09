
namespace GitTfs.Core
{
    public sealed record TfsCheckinNote : ITfsCheckinNote
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
