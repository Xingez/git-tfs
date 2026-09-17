
namespace GitTfs.Core
{
    using global::System.Reflection;

    public class GitTfsVersionProvider : IGitTfsVersionProvider
    {
        public string GetVersionString() => $"git-tfs version {GetType().Assembly.GetName().Version} (REST TFVC client) ({(Environment.Is64BitProcess ? "64" : "32")}-bit)";

        public string GetPathToGitTfsExecutable() => Assembly.GetExecutingAssembly().Location;
    }
}
