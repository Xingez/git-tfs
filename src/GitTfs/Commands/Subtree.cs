
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("subtree")]
    [Description("subtree [add|pull|split] [options] [remote | ( [tfs-url] [repository-path] )]")]
    [RequiresValidGitRepository]
    public class Subtree : GitTfsCommand
    {
        private readonly Fetch fetchField;
        private readonly QuickFetch quickFetchField;
        private readonly Globals globalsField;
        private readonly RemoteOptions remoteOptionsField;

        private string Prefix;
        private bool Squash;

        public OptionSet OptionSet => new OptionSet
                {
                    { "p|prefix=",
                        v => Prefix = v},
                    { "squash",
                        v => Squash = v != null}
                    //                    { "r|revision=",
                    //                        v => RevisionToFetch = Convert.ToInt32(v) },
                }
                .Merge(fetchField.OptionSet);

        public Subtree(Fetch fetch, QuickFetch quickFetch, Globals globals, RemoteOptions remoteOptions)
        {
            fetchField = fetch;
            quickFetchField = quickFetch;
            globalsField = globals;
            remoteOptionsField = remoteOptions;
        }

        public int Run(IList<string> args)
        {
            string command = args.FirstOrDefault() ?? "";
            Trace.TraceInformation("executing subtree " + command);

            if (string.IsNullOrEmpty(Prefix))
            {
                Trace.TraceInformation("Prefix must be specified, use -p or -prefix");
                return GitTfsExitCodes.InvalidArguments;
            }

            switch (command.ToLower())
            {
                case "add":
                    return DoAdd(args.ElementAtOrDefault(1) ?? "", args.ElementAtOrDefault(2) ?? "");

                case "pull":
                    return DoPull(args.ElementAtOrDefault(1));

                case "split":
                    return DoSplit();

                default:
                    Trace.TraceInformation("Expected one of [add, pull, split]");
                    return GitTfsExitCodes.InvalidArguments;
            }
        }

        public int DoAdd(string tfsUrl, string tfsRepositoryPath)
        {
            if (File.Exists(Prefix) || Directory.Exists(Prefix))
            {
                Trace.TraceInformation("Directory {0} already exists", Prefix);
                return GitTfsExitCodes.InvalidArguments;
            }


            var fetch = Squash ? quickFetchField : fetchField;

            IGitTfsRemote owner = null;
            string ownerId = globalsField.RemoteId;
            if (!string.IsNullOrEmpty(ownerId))
            {
                //check for the specified remote
                owner = globalsField.Repository.HasRemote(globalsField.RemoteId) ? globalsField.Repository.ReadTfsRemote(globalsField.RemoteId) : null;
                if (owner != null && !string.IsNullOrEmpty(owner.TfsRepositoryPath))
                {
                    owner = null;
                    ownerId = null;
                }
            }

            if (string.IsNullOrEmpty(ownerId))
            {
                //check for any remote that has no TfsRepositoryPath
                owner = globalsField.Repository.ReadAllTfsRemotes().FirstOrDefault(x => string.IsNullOrEmpty(x.TfsRepositoryPath) && !x.IsSubtree);
            }

            if (owner == null)
            {
                owner = globalsField.Repository.CreateTfsRemote(new RemoteInfo
                {
                    Id = ownerId ?? GitTfsConstants.DefaultRepositoryId,
                    Url = tfsUrl,
                    Repository = null,
                    RemoteOptions = remoteOptionsField
                });
                Trace.TraceInformation("-> new owning remote " + owner.Id);
            }
            else
            {
                ownerId = owner.Id;
                Trace.TraceInformation("Attaching subtree to owning remote " + owner.Id);
            }


            //create a remote for the new subtree
            string remoteId = string.Format(GitTfsConstants.RemoteSubtreeFormat, owner.Id, Prefix);
            IGitTfsRemote remote = globalsField.Repository.HasRemote(remoteId) ?
                globalsField.Repository.ReadTfsRemote(remoteId) :
                globalsField.Repository.CreateTfsRemote(new RemoteInfo
                {
                    Id = remoteId,
                    Url = tfsUrl,
                    Repository = tfsRepositoryPath,
                    RemoteOptions = remoteOptionsField
                });

            Trace.TraceInformation("-> new remote " + remote.Id);

            fetch.BranchStrategy = BranchStrategy.None;

            int result = fetch.Run(remote.Id);

            if (result == GitTfsExitCodes.OK)
            {
                var p = Prefix.Replace(" ", "\\ ");

                int latest = Math.Max(owner.MaxChangesetId, remote.MaxChangesetId);
                string msg = string.Format(GitTfsConstants.TfsCommitInfoFormat, owner.TfsUrl, owner.TfsRepositoryPath, latest);
                msg = $@"Add '{Prefix}/' from commit '{remote.MaxCommitHash}'

{msg}";

                globalsField.Repository.CommandNoisy("subtree", "add", "--prefix=" + p, $"-m {msg}", remote.RemoteRef);

                //update the owner remote to point at the commit where the newly created subtree was merged.
                var commit = globalsField.Repository.GetCurrentCommit();
                owner.UpdateTfsHead(commit, latest);

                result = GitTfsExitCodes.OK;
            }


            return result;
        }

        public int DoPull(string remoteId)
        {
            ValidatePrefix();

            remoteId = remoteId ?? string.Format(GitTfsConstants.RemoteSubtreeFormat, globalsField.RemoteId ?? GitTfsConstants.DefaultRepositoryId, Prefix);
            IGitTfsRemote remote = globalsField.Repository.ReadTfsRemote(remoteId);

            int result = fetchField.Run(remote.Id);
            if (result == GitTfsExitCodes.OK)
            {
                var p = Prefix.Replace(" ", "\\ ");
                globalsField.Repository.CommandNoisy("subtree", "merge", "--prefix=" + p, remote.RemoteRef);
                result = GitTfsExitCodes.OK;
            }

            return result;
        }

        public int DoSplit()
        {
            ValidatePrefix();

            var p = Prefix.Replace(" ", "\\ ");
            globalsField.Repository.CommandNoisy("subtree", "split", "--prefix=" + p, "-b", p);
            globalsField.Repository.CommandNoisy("checkout", p);

            //update subtree refs if needed
            var owners = globalsField.Repository.GetLastParentTfsCommits("HEAD").Where(x => !x.Remote.IsSubtree && x.Remote.TfsRepositoryPath == null).ToList();
            foreach (var subtree in globalsField.Repository.ReadAllTfsRemotes().Where(x => x.IsSubtree && string.Equals(x.Prefix, Prefix)))
            {
                var updateTo = owners.FirstOrDefault(x => string.Equals(x.Remote.Id, subtree.OwningRemoteId));
                if (updateTo != null && updateTo.ChangesetId > subtree.MaxChangesetId)
                {
                    subtree.UpdateTfsHead(updateTo.GitCommit, updateTo.ChangesetId);
                }
            }

            return GitTfsExitCodes.OK;
        }

        private void ValidatePrefix()
        {
            if (!Directory.Exists(Prefix))
            {
                throw new GitTfsException($"Directory {Prefix} does not exist")
                    .WithRecommendation("Add the subtree using 'git tfs subtree add -p=<prefix> [tfs-server] [tfs-repository]'");
            }
        }
    }
}
