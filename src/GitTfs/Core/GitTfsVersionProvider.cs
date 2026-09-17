
namespace GitTfs.Core
{
    using global::System.Reflection;

    using global::GitTfs.Core.TfsInterop;
    public class GitTfsVersionProvider : IGitTfsVersionProvider
    {
        private readonly ITfsHelper tfsHelperField;

        public GitTfsVersionProvider(ITfsHelper tfsHelper)
        {
            tfsHelperField = tfsHelper;
        }

        public string GetVersionString() => $"git-tfs version {GetType().Assembly.GetName().Version} (TFS client library {tfsHelperField.TfsClientLibraryVersion}) ({(Environment.Is64BitProcess ? "64" : "32")}-bit)";

        public string GetPathToGitTfsExecutable() => Assembly.GetExecutingAssembly().Location;
    }
}