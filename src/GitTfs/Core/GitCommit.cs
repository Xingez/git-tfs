
namespace GitTfs.Core
{
    using global::System.Diagnostics;

    using global::LibGit2Sharp;
    public class GitCommit
    {
        private readonly Commit commitField;

        public GitCommit(Commit commit)
        {
            commitField = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        public IEnumerable<GitTreeEntry> GetTree()
        {
            var treesToDescend = new Queue<Tree>(new[] { commitField.Tree });
            while (treesToDescend.Any())
            {
                var currentTree = treesToDescend.Dequeue();
                foreach (var entry in currentTree)
                {
                    if (entry.TargetType == TreeEntryTargetType.Tree)
                    {
                        treesToDescend.Enqueue((Tree)entry.Target);
                    }
                    else if (entry.TargetType == TreeEntryTargetType.Blob)
                    {
                        yield return new GitTreeEntry(entry);
                    }
                    else
                    {
                        Trace.WriteLine("Not including " + entry.Name + ": type is " + entry.GetType().Name);
                    }
                }
            }
        }

        public Tuple<string, string> AuthorAndEmail => new Tuple<string, string>(commitField.Author.Name, commitField.Author.Email);

        public DateTimeOffset When => commitField.Author.When;

        public string Sha => commitField.Sha;

        public string Message => commitField.Message;

        public IEnumerable<GitCommit> Parents => commitField.Parents.Select(c => new GitCommit(c));
    }
}

