
namespace GitTfs.Util
{
    using global::System.Text.RegularExpressions;

    using global::GitTfs.Core;
    public class PathResolver
    {
        private readonly IGitTfsRemote remoteField;
        private readonly string relativePathField;
        private readonly IDictionary<string, GitObject> initialTreeField;

        public PathResolver(IGitTfsRemote remote, string relativePath, IDictionary<string, GitObject> initialTree)
        {
            remoteField = remote;
            relativePathField = relativePath;
            initialTreeField = initialTree;
        }

        public string GetPathInGitRepo(string tfsPath) => GetGitObject(tfsPath).Try(x => x.Path);

        public GitObject GetGitObject(string tfsPath)
        {
            var pathInGitRepo = remoteField.GetPathInGitRepo(tfsPath);
            if (pathInGitRepo == null)
                return null;
            if (!string.IsNullOrEmpty(relativePathField))
                pathInGitRepo = relativePathField + "/" + pathInGitRepo;
            return Lookup(pathInGitRepo);
        }

        public bool IsIgnored(string path) => remoteField.IsIgnored(path);

        public bool IsInDotGit(string path) => remoteField.IsInDotGit(path);

        public bool Contains(string pathInGitRepo)
        {
            if (pathInGitRepo != null)
            {
                GitObject result;
                if (initialTreeField.TryGetValue(pathInGitRepo, out result))
                    return result.Commit != null;
            }
            return false;
        }

        private static readonly Regex SplitDirnameFilename = new Regex(@"(?<dir>.*)[/\\](?<file>[^/\\]+)", RegexOptions.Compiled);

        private GitObject Lookup(string pathInGitRepo)
        {
            GitObject result;
            if (initialTreeField.TryGetValue(pathInGitRepo, out result))
                return result;

            var fullPath = pathInGitRepo;
            var splitResult = SplitDirnameFilename.Match(pathInGitRepo);
            if (splitResult.Success)
            {
                var dirName = splitResult.Groups["dir"].Value;
                var fileName = splitResult.Groups["file"].Value;
                fullPath = Lookup(dirName).Path + "/" + fileName;
            }
            result = new GitObject { Path = fullPath };
            initialTreeField[fullPath] = result;
            return result;
        }
    }
}
