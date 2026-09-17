
namespace GitTfs.Core
{
    using global::GitTfs.Core.TfsInterop;
    using global::GitTfs.Util;
    public class TfsTreeEntry : ITreeEntry
    {
        private readonly string pathInGitRepoField;
        private readonly IItem itemField;

        public TfsTreeEntry(string pathInGitRepo, IItem item)
        {
            pathInGitRepoField = pathInGitRepo;
            itemField = item;
        }

        public IItem Item => itemField;
        public string FullName => pathInGitRepoField;
        public Stream OpenRead() => new TemporaryFileStream(itemField.DownloadFile());
    }
}
