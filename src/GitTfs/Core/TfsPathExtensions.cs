namespace GitTfs.Commands
{
    using global::System.Text.RegularExpressions;
    using global::GitTfs.Core;

    public static class Ext
    {
        private static readonly Regex ValidTfsPath = new Regex("^\\$/.+");

        public static bool IsValidTfsPath(this string tfsPath) => ValidTfsPath.IsMatch(tfsPath);

        public static void AssertValidTfsPathOrRoot(this string tfsPath)
        {
            if (tfsPath == GitTfsConstants.TfsRoot)
                return;
            AssertValidTfsPath(tfsPath);
        }

        public static void AssertValidTfsPath(this string tfsPath)
        {
            if (!ValidTfsPath.IsMatch(tfsPath))
                throw new GitTfsException("TFS repository can not be root and must start with \"$/\".", SuggestPaths(tfsPath));
        }

        private static IEnumerable<string> SuggestPaths(string tfsPath)
        {
            if (tfsPath == "$" || tfsPath == "$/")
                yield return "Cloning an entire TFS repository is not supported. Try using a subdirectory of the root (e.g. $/MyProject).";
            else if (tfsPath.StartsWith("$"))
                yield return "Try using $/" + tfsPath.Substring(1);
            else
                yield return "Try using $/" + tfsPath;
        }

        public static string ToGitRefName(this string expectedRefName)
        {
            expectedRefName = Regex.Replace(expectedRefName, @"[!~$?[*^: \\]", string.Empty);
            expectedRefName = expectedRefName.Replace("@{", string.Empty);
            expectedRefName = expectedRefName.Replace("..", string.Empty);
            expectedRefName = expectedRefName.Replace("//", string.Empty);
            expectedRefName = expectedRefName.Replace("/.", "/");
            expectedRefName = expectedRefName.TrimEnd('.', '/');
            return expectedRefName.Trim('/');
        }

        public static string ToGitBranchNameFromTfsRepositoryPath(this string tfsRepositoryPath,
            bool includeTeamProjectName = false)
        {
            if (includeTeamProjectName)
            {
                return tfsRepositoryPath
                    .Replace("$/", string.Empty)
                    .ToGitRefName();
            }

            var gitBranchNameExpected = tfsRepositoryPath.IndexOf("$/", StringComparison.Ordinal) == 0
                ? tfsRepositoryPath.Remove(0, tfsRepositoryPath.IndexOf('/', 2) + 1)
                : tfsRepositoryPath;

            return gitBranchNameExpected.ToGitRefName();
        }

        public static string ToTfsTeamProjectRepositoryPath(this string tfsRepositoryPath)
        {
            if (!tfsRepositoryPath.StartsWith("$/", StringComparison.Ordinal))
                return tfsRepositoryPath;

            var index = tfsRepositoryPath.IndexOf('/', 2);
            return index == -1 ? tfsRepositoryPath : tfsRepositoryPath.Remove(index, tfsRepositoryPath.Length - index);
        }

        public static string ToLocalGitRef(this string refName) => "refs/heads/" + refName;
    }
}
