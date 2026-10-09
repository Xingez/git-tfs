
namespace GitTfs.Core
{
    public sealed record GitObject
    {
        public LibGit2Sharp.Mode Mode { get; set; } = LibGit2Sharp.Mode.NonExecutableFile;
        public LibGit2Sharp.TreeEntryTargetType ObjectType { get; set; }
        public string Sha { get; set; }
        public string Commit { get; set; }
        public string Path { get; set; }
    }
}
