
namespace GitTfs.Commands
{
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("rcheckin")]
    [RequiresValidGitRepository]
    public class Rcheckin : GitTfsCommand
    {
        private readonly CheckinOptions checkinOptionsField;
        private readonly CheckinOptionsFactory checkinOptionsFactoryField;
        private readonly TfsWriter writerField;
        private readonly Globals globalsField;
        private readonly AuthorsFile authorsField;

        private bool AutoRebase { get; set; }
        private bool ForceCheckin { get; set; }

        public Rcheckin(CheckinOptions checkinOptions, TfsWriter writer, Globals globals, AuthorsFile authors)
        {
            checkinOptionsField = checkinOptions;
            checkinOptionsFactoryField = new CheckinOptionsFactory(globals);
            writerField = writer;
            globalsField = globals;
            authorsField = authors;
        }

        public OptionSet OptionSet => new OptionSet
                    {
                        {"a|autorebase", "Continue and rebase if new TFS changesets found", v => AutoRebase = v != null},
                        {"ignore-merge", "Force check in ignoring parent tfs branches in merge commits", v => ForceCheckin = v != null},
                    }.Merge(checkinOptionsField.OptionSet);

        // uses rebase and works only with HEAD
        public int Run()
        {
            globalsField.WarnOnGitVersion();

            if (globalsField.Repository.IsBare)
                throw new GitTfsException("error: you should specify the local branch to checkin for a bare repository.");

            return writerField.Write("HEAD", PerformRCheckin);
        }

        // uses rebase and works only with HEAD in a none bare repository
        public int Run(string localBranch)
        {
            globalsField.WarnOnGitVersion();

            if (!globalsField.Repository.IsBare)
                throw new GitTfsException("error: This syntax with one parameter is only allowed in bare repository.");

            authorsField.LoadAuthorsFromSavedFile(globalsField.GitDir);

            return writerField.Write(GitRepository.ShortToLocalName(localBranch), PerformRCheckin);
        }

        private int PerformRCheckin(TfsChangesetInfo parentChangeset, string refToCheckin)
        {
            if (globalsField.Repository.IsBare)
                AutoRebase = false;

            if (globalsField.Repository.WorkingCopyHasUnstagedOrUncommitedChanges)
            {
                throw new GitTfsException("error: You have local changes; rebase-workflow checkin only possible with clean working directory.")
                    .WithRecommendation("Try 'git stash' to stash your local changes and checkin again.");
            }

            // get latest changes from TFS to minimize possibility of late conflict
            Trace.TraceInformation("Fetching changes from TFS to minimize possibility of late conflict...");
            parentChangeset.Remote.Fetch();
            if (parentChangeset.ChangesetId != parentChangeset.Remote.MaxChangesetId)
            {
                if (AutoRebase)
                {
                    globalsField.Repository.CommandNoisy("rebase", "--rebase-merges", parentChangeset.Remote.RemoteRef);
                    parentChangeset = globalsField.Repository.GetTfsCommit(parentChangeset.Remote.MaxCommitHash);
                }
                else
                {
                    if (globalsField.Repository.IsBare)
                        globalsField.Repository.UpdateRef(refToCheckin, parentChangeset.Remote.MaxCommitHash);

                    throw new GitTfsException("error: New TFS changesets were found.")
                        .WithRecommendation("Try to rebase HEAD onto latest TFS checkin and repeat rcheckin or alternatively checkins");
                }
            }

            IEnumerable<GitCommit> commitsToCheckin = globalsField.Repository.FindParentCommits(refToCheckin, parentChangeset.Remote.MaxCommitHash);
            Trace.WriteLine("Commit to checkin count:" + commitsToCheckin.Count());
            if (!commitsToCheckin.Any())
                throw new GitTfsException("error: latest TFS commit should be parent of commits being checked in");

            SetupMetadataExport(parentChangeset.Remote);

            return _PerformRCheckinQuick(parentChangeset, refToCheckin, commitsToCheckin);
        }

        private void SetupMetadataExport(IGitTfsRemote remote)
        {
            var exportInitializer = new ExportMetadatasInitializer(globalsField);
            var shouldExport = globalsField.Repository.GetConfig(GitTfsConstants.ExportMetadatasConfigKey) == "true";
            exportInitializer.InitializeRemote(remote, shouldExport);
        }

        private int _PerformRCheckinQuick(TfsChangesetInfo parentChangeset, string refToCheckin, IEnumerable<GitCommit> commitsToCheckin)
        {
            var tfsRemote = parentChangeset.Remote;
            string currentParent = parentChangeset.Remote.MaxCommitHash;
            int newChangesetId = 0;

            foreach (var commit in commitsToCheckin)
            {
                var message = BuildCommitMessage(commit, !checkinOptionsField.NoGenerateCheckinComment, currentParent);
                string target = commit.Sha;
                var parents = commit.Parents.Where(c => c.Sha != currentParent).ToArray();
                string tfsRepositoryPathOfMergedBranch = checkinOptionsField.NoMerge
                                                             ? null
                                                             : FindTfsRepositoryPathOfMergedBranch(tfsRemote, parents, target);

                var commitSpecificCheckinOptions = checkinOptionsFactoryField.BuildCommitSpecificCheckinOptions(checkinOptionsField, message, commit, authorsField);

                Trace.TraceInformation("Starting checkin of {0} '{1}'", target.Substring(0, 8), commitSpecificCheckinOptions.CheckinComment);
                try
                {
                    newChangesetId = tfsRemote.Checkin(target, currentParent, parentChangeset, commitSpecificCheckinOptions, tfsRepositoryPathOfMergedBranch);
                    var fetchResult = tfsRemote.FetchWithMerge(newChangesetId, false, parents.Select(c => c.Sha).ToArray());
                    if (fetchResult.NewChangesetCount != 1)
                    {
                        var lastCommit = globalsField.Repository.FindCommitHashByChangesetId(newChangesetId);
                        RebaseOnto(lastCommit, target);
                        if (AutoRebase)
                            tfsRemote.Repository.CommandNoisy("rebase", "--rebase-merges", tfsRemote.RemoteRef);
                        else
                            throw new GitTfsException("error: New TFS changesets were found. Rcheckin was not finished.");
                    }

                    currentParent = target;
                    parentChangeset = new TfsChangesetInfo { ChangesetId = newChangesetId, GitCommit = tfsRemote.MaxCommitHash, Remote = tfsRemote };
                    Trace.TraceInformation("Done with {0}.", target);
                }
                catch (Exception)
                {
                    if (newChangesetId != 0)
                    {
                        var lastCommit = globalsField.Repository.FindCommitHashByChangesetId(newChangesetId);
                        RebaseOnto(lastCommit, currentParent);
                    }
                    throw;
                }
            }

            if (globalsField.Repository.IsBare)
                globalsField.Repository.UpdateRef(refToCheckin, tfsRemote.MaxCommitHash);
            else
                globalsField.Repository.ResetHard(tfsRemote.MaxCommitHash);
            Trace.TraceInformation("No more to rcheckin.");

            Trace.WriteLine("Cleaning...");
            tfsRemote.CleanupWorkspaceDirectory();

            return GitTfsExitCodes.OK;
        }

        public string BuildCommitMessage(GitCommit commit, bool generateCheckinComment, string latest) => generateCheckinComment
                               ? globalsField.Repository.GetCommitMessage(commit.Sha, latest)
                               : globalsField.Repository.GetCommit(commit.Sha).Message;

        private string FindTfsRepositoryPathOfMergedBranch(IGitTfsRemote remoteToCheckin, GitCommit[] gitParents, string target)
        {
            if (gitParents.Length != 0)
            {
                Trace.TraceInformation("Working on the merge commit: " + target);
                if (gitParents.Length > 1)
                    Trace.TraceWarning("warning: only 1 parent is supported by TFS for a merge changeset. The other parents won't be materialized in the TFS merge!");
                foreach (var gitParent in gitParents)
                {
                    var tfsCommit = globalsField.Repository.GetTfsCommit(gitParent);
                    if (tfsCommit != null)
                        return tfsCommit.Remote.TfsRepositoryPath;
                    var lastCheckinCommit = globalsField.Repository.GetLastParentTfsCommits(gitParent.Sha).FirstOrDefault();
                    if (lastCheckinCommit != null)
                    {
                        if (!ForceCheckin && lastCheckinCommit.Remote.Id != remoteToCheckin.Id)
                            throw new GitTfsException("error: the merged branch '" + lastCheckinCommit.Remote.Id
                                + "' is a TFS tracked branch (" + lastCheckinCommit.Remote.TfsRepositoryPath
                                + ") with some commits not checked in.\nIn this case, the local merge won't be materialized as a merge in tfs...")
                                .WithRecommendation("check in all the commits of the tfs merged branch in TFS before trying to check in a merge commit",
                                "use --ignore-merge option to ignore merged TFS branch and check in commit as a normal changeset (not a merge).");
                    }
                    else
                    {
                        Trace.TraceWarning("warning: the parent " + gitParent + " does not belong to a TFS tracked branch (not checked in TFS) and will be ignored!");
                    }
                }
            }
            return null;
        }

        public void RebaseOnto(string newBaseCommit, string oldBaseCommit) => globalsField.Repository.CommandNoisy("rebase", "--rebase-merges", "--onto", newBaseCommit, oldBaseCommit);
    }
}
