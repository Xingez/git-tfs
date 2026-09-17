
namespace GitTfs
{
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [SingletonService]
    public class Globals
    {
        public OptionSet OptionSet => new OptionSet
                {
                    { "h|H|help",
                        v => ShowHelp = v != null },
                    { "V|version",
                        v => ShowVersion = v != null },
                    { "d|debug", "Show debug output about everything git-tfs does",
                        v => DebugOutput = v != null },
                    { "i|tfs-remote|remote|id=", "The remote ID of the TFS to interact with\ndefault: default",
                        v => UserSpecifiedRemoteId = v },
                    { "A|authors=", "Path to an Authors file to map TFS users to Git users (will be kept in cache and used for all the following commands)",
                        v => AuthorsFilePath = Path.GetFullPath(v) },
                };

        public string AuthorsFilePath { get; set; }
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }

        // This build intentionally keeps the verbose request trace enabled so a
        // long-running/resumed import can always be diagnosed after the fact.
        public bool DebugOutput { get; set; } = true;

        public string UserSpecifiedRemoteId { get; set; }

        private string remoteIdField = null;
        public string RemoteId
        {
            get
            {
                if (!string.IsNullOrEmpty(remoteIdField))
                    return remoteIdField;

                if (!string.IsNullOrEmpty(UserSpecifiedRemoteId))
                    return UserSpecifiedRemoteId;

                var changesetsWithRemote = Repository.GetLastParentTfsCommits("HEAD");
                if (changesetsWithRemote.Any())
                {
                    var foundRemote = changesetsWithRemote.First().Remote;
                    if (foundRemote.IsDerived)
                    {
                        Trace.TraceInformation("Bootstraping tfs remote...");
                        foundRemote = Bootstrapper.CreateRemote(changesetsWithRemote.First());
                    }

                    remoteIdField = foundRemote.Id;
                    Trace.TraceInformation("Working with tfs remote: " + remoteIdField + " => " + foundRemote.TfsRepositoryPath);
                    return remoteIdField;
                }

                var allRemotes = Repository.ReadAllTfsRemotes();
                //Case where the repository is cloned
                if (!allRemotes.Any())
                    return remoteIdField = GitTfsConstants.DefaultRepositoryId;

                if (allRemotes.Count() == 1)
                {
                    //Case where the repository is just initialised
                    var foundRemote = allRemotes.First();
                    remoteIdField = foundRemote.Id;
                    if (remoteIdField == GitTfsConstants.DefaultRepositoryId)
                    {
                        Trace.TraceInformation("Working with tfs remote: " + remoteIdField + " => " + foundRemote.TfsRepositoryPath);
                        return remoteIdField;
                    }
                }
                //We could no choose for the user which remote is the good one (if, eventualy we found one...)
                throw new GitTfsException("error: no tfs remote to use found in parent commits.",
                    new List<string> { "Checkout a current tfs branch", "Use '-i' option to define which one to use." });
            }
        }

        public string GitDir
        {
            get => Environment.GetEnvironmentVariable("GIT_DIR");
            set => Environment.SetEnvironmentVariable("GIT_DIR", value);
        }

        public bool GitDirSetByUser { get; set; }

        public IGitRepository Repository { get; set; }

        public int GcCountdown { get; set; }

        private string gitVersionField;
        public string GitVersion
        {
            get
            {
                if (gitVersionField != null)
                    return gitVersionField;
                if (Repository == null)
                    return null;
                return gitVersionField = Repository.CommandOneline("--version");
            }
        }

        public void WarnOnGitVersion()
        {
            if (GitVersion != null && GitVersion.Contains("git version 1.8.4"))
                Trace.TraceWarning(@"WARNING!!!! You are using a version of git (1.8.4) that causes problems when using git-tfs!
If you are experiencing some crashes using git-tfs, perhaps you could get a newer or older version of git.
For more information, see https://github.com/git-tfs/git-tfs/issues/448 ");
        }

        public int GcPeriod => 200;

        public Bootstrapper Bootstrapper { get; set; }
        public string CommandLineRun { get; set; }
        public static bool DisableGarbageCollect = false;
    }
}
