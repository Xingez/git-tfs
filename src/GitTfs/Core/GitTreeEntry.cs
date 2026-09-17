
namespace GitTfs.Core
{
    using global::LibGit2Sharp;
    public class GitTreeEntry : ITreeEntry
    {
        private readonly TreeEntry entryField;

        public GitTreeEntry(TreeEntry entry)
        {
            entryField = entry;
        }

        public TreeEntry Entry => entryField;

        public string FullName => entryField.Path;

        public Stream OpenRead()
        {
            if (entryField.TargetType == TreeEntryTargetType.Blob)
            {
                return ((Blob)entryField.Target).GetContentStream();
            }
            throw new InvalidOperationException("Invalid object type");
        }
    }
}