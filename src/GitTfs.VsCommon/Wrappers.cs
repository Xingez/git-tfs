
namespace GitTfs.VsCommon
{
    using global::GitTfs.Core;
    using global::GitTfs.Core.TfsInterop;
    using global::GitTfs.Util;

    using global::Microsoft.TeamFoundation.Server;
    using global::Microsoft.TeamFoundation.VersionControl.Client;
    using global::Microsoft.TeamFoundation.VersionControl.Common;

    using global::System.Diagnostics;
    public class WrapperForVersionControlServer : WrapperFor<VersionControlServer>, IVersionControlServer
    {
        private readonly TfsApiBridge bridgeField;
        private readonly VersionControlServer versionControlServerField;

        public WrapperForVersionControlServer(TfsApiBridge bridge, VersionControlServer versionControlServer) : base(versionControlServer)
        {
            bridgeField = bridge;
            versionControlServerField = versionControlServer;
        }

        public IItem GetItem(int itemId, int changesetNumber)
            => bridgeField.Wrap<WrapperForItem, Item>(versionControlServerField.GetItem(itemId, changesetNumber));

        public IItem GetItem(string itemPath, int changesetNumber)
            => bridgeField.Wrap<WrapperForItem, Item>(versionControlServerField.GetItem(itemPath, new ChangesetVersionSpec(changesetNumber)));

        public IItem[] GetItems(string itemPath, int changesetNumber, TfsRecursionType recursionType)
        {
            var itemSet = versionControlServerField.GetItems(
                new ItemSpec(itemPath, bridgeField.Convert<RecursionType>(recursionType), 0),
                new ChangesetVersionSpec(changesetNumber),
                DeletedState.NonDeleted,
                ItemType.Any,
                // do not load the loading info
                false);

            return bridgeField.Wrap<WrapperForItem, Item>(itemSet.Items);
        }

        public IEnumerable<IChangeset> QueryHistory(string path, int version, int deletionId,
                                                    TfsRecursionType recursion, string user, int versionFrom, int versionTo, int maxCount,
                                                    bool includeChanges, bool slotMode, bool includeDownloadInfo)
        {
            var history = versionControlServerField.QueryHistory(path, new ChangesetVersionSpec(version), deletionId,
                                                             bridgeField.Convert<RecursionType>(recursion), user, new ChangesetVersionSpec(versionFrom),
                                                             new ChangesetVersionSpec(versionTo), maxCount, includeChanges, slotMode,
                                                             includeDownloadInfo);
            return bridgeField.Wrap<WrapperForChangeset, Changeset>(history);
        }
    }

    public class WrapperForChangeset : WrapperFor<Changeset>, IChangeset
    {
        private readonly TfsApiBridge bridgeField;
        private readonly Changeset changesetField;

        public WrapperForChangeset(TfsApiBridge bridge, Changeset changeset) : base(changeset)
        {
            bridgeField = bridge;
            changesetField = changeset;
        }

        public IChange[] Changes => bridgeField.Wrap<WrapperForChange, Change>(changesetField.Changes);

        public string Committer
        {
            get
            {
                var committer = changesetField.Committer;
                var owner = changesetField.Owner;

                // Sometimes TFS itself commits the changeset
                if (owner != committer)
                    return owner;

                return committer;
            }
        }

        public DateTime CreationDate => changesetField.CreationDate;
        public string Comment => changesetField.Comment;
        public int ChangesetId => changesetField.ChangesetId;

        public IVersionControlServer VersionControlServer
            => bridgeField.Wrap<WrapperForVersionControlServer, VersionControlServer>(changesetField.VersionControlServer);

        public void Get(ITfsWorkspace workspace, IEnumerable<IChange> changes, Action<Exception> ignorableErrorHandler) => workspace.Get(ChangesetId, changes);
    }

    public class WrapperForChange : WrapperFor<Change>, IChange
    {
        private readonly TfsApiBridge bridgeField;
        private readonly Change changeField;

        public WrapperForChange(TfsApiBridge bridge, Change change) : base(change)
        {
            bridgeField = bridge;
            changeField = change;
        }

        public TfsChangeType ChangeType => bridgeField.Convert<TfsChangeType>(changeField.ChangeType);

        public IItem Item => bridgeField.Wrap<WrapperForItem, Item>(changeField.Item);
    }

    public class WrapperForItem : WrapperFor<Item>, IItem
    {
        private readonly TfsApiBridge bridgeField;
        private readonly Item itemField;

        public WrapperForItem(TfsApiBridge bridge, Item item) : base(item)
        {
            bridgeField = bridge;
            itemField = item;
        }

        public IVersionControlServer VersionControlServer
            => bridgeField.Wrap<WrapperForVersionControlServer, VersionControlServer>(itemField.VersionControlServer);

        public int ChangesetId => itemField.ChangesetId;
        public string ServerItem => itemField.ServerItem;
        public int DeletionId => itemField.DeletionId;
        public TfsItemType ItemType => bridgeField.Convert<TfsItemType>(itemField.ItemType);
        public int ItemId => itemField.ItemId;
        public long ContentLength => itemField.ContentLength;

        public TemporaryFile DownloadFile()
        {
            var temp = new TemporaryFile();
            try
            {
                itemField.DownloadFile(temp);
                return temp;
            }
            catch (Exception)
            {
                Trace.WriteLine($"Something went wrong when downloading \"{itemField.ServerItem}\" from changeset {itemField.ChangesetId}");
                temp.Dispose();
                throw;
            }
        }
    }

    public class WrapperForIdentity : WrapperFor<Identity>, IIdentity
    {
        private readonly Identity identityField;

        public WrapperForIdentity(Identity identity) : base(identity)
        {
            Debug.Assert(identity != null, "wrapped property must not be null.");
            identityField = identity;
        }

        public string MailAddress => identityField.MailAddress;

        public string DisplayName => identityField.DisplayName;
    }

    public class WrapperForShelveset : WrapperFor<Shelveset>, IShelveset
    {
        private readonly Shelveset shelvesetField;
        private readonly TfsApiBridge bridgeField;

        public WrapperForShelveset(TfsApiBridge bridge, Shelveset shelveset) : base(shelveset)
        {
            shelvesetField = shelveset;
            bridgeField = bridge;
        }

        public string Comment
        {
            get => shelvesetField.Comment;
            set => shelvesetField.Comment = value;
        }

        public IWorkItemCheckinInfo[] WorkItemInfo
        {
            get => bridgeField.Wrap<WrapperForWorkItemCheckinInfo, WorkItemCheckinInfo>(shelvesetField.WorkItemInfo);
            set => shelvesetField.WorkItemInfo = bridgeField.Unwrap<WorkItemCheckinInfo>(value);
        }
    }

    public class WrapperForWorkItemCheckinInfo : WrapperFor<WorkItemCheckinInfo>, IWorkItemCheckinInfo
    {
        public WrapperForWorkItemCheckinInfo(WorkItemCheckinInfo workItemCheckinInfo) : base(workItemCheckinInfo)
        {
        }
    }

    public class WrapperForWorkItemCheckedInfo : WrapperFor<WorkItemCheckedInfo>, IWorkItemCheckedInfo
    {
        public WrapperForWorkItemCheckedInfo(WorkItemCheckedInfo workItemCheckinInfo)
            : base(workItemCheckinInfo)
        {
        }
    }

    public class WrapperForPendingChange : WrapperFor<PendingChange>, IPendingChange
    {
        public WrapperForPendingChange(PendingChange pendingChange) : base(pendingChange)
        {
        }
    }

    public class WrapperForCheckinNote : WrapperFor<CheckinNote>, ICheckinNote
    {
        public WrapperForCheckinNote(CheckinNote checkiNote) : base(checkiNote)
        {
        }
    }

    public class WrapperForCheckinEvaluationResult : WrapperFor<CheckinEvaluationResult>, ICheckinEvaluationResult
    {
        private readonly TfsApiBridge bridgeField;
        private readonly CheckinEvaluationResult resultField;

        public WrapperForCheckinEvaluationResult(TfsApiBridge bridge, CheckinEvaluationResult result) : base(result)
        {
            bridgeField = bridge;
            resultField = result;
        }

        public ICheckinConflict[] Conflicts => bridgeField.Wrap<WrapperForCheckinConflict, CheckinConflict>(resultField.Conflicts);

        public ICheckinNoteFailure[] NoteFailures => bridgeField.Wrap<WrapperForCheckinNoteFailure, CheckinNoteFailure>(resultField.NoteFailures);

        public IPolicyFailure[] PolicyFailures => bridgeField.Wrap<WrapperForPolicyFailure, PolicyFailure>(resultField.PolicyFailures);

        public Exception PolicyEvaluationException => resultField.PolicyEvaluationException;
    }

    public class WrapperForCheckinConflict : WrapperFor<CheckinConflict>, ICheckinConflict
    {
        private readonly CheckinConflict conflictField;

        public WrapperForCheckinConflict(CheckinConflict conflict) : base(conflict)
        {
            conflictField = conflict;
        }

        public string ServerItem => conflictField.ServerItem;
        public string Message => conflictField.Message;
        public bool Resolvable => conflictField.Resolvable;
    }

    public class WrapperForCheckinNoteFailure : WrapperFor<CheckinNoteFailure>, ICheckinNoteFailure
    {
        private readonly TfsApiBridge bridgeField;
        private readonly CheckinNoteFailure failureField;

        public WrapperForCheckinNoteFailure(TfsApiBridge bridge, CheckinNoteFailure failure) : base(failure)
        {
            bridgeField = bridge;
            failureField = failure;
        }

        public ICheckinNoteFieldDefinition Definition
            => bridgeField.Wrap<WrapperForCheckinNoteFieldDefinition, CheckinNoteFieldDefinition>(failureField.Definition);

        public string Message => failureField.Message;
    }

    public class WrapperForCheckinNoteFieldDefinition : WrapperFor<CheckinNoteFieldDefinition>, ICheckinNoteFieldDefinition
    {
        private readonly CheckinNoteFieldDefinition fieldDefinitionField;

        public WrapperForCheckinNoteFieldDefinition(CheckinNoteFieldDefinition fieldDefinition) : base(fieldDefinition)
        {
            fieldDefinitionField = fieldDefinition;
        }

        public string ServerItem => fieldDefinitionField.ServerItem;
        public string Name => fieldDefinitionField.Name;
        public bool Required => fieldDefinitionField.Required;
        public int DisplayOrder => fieldDefinitionField.DisplayOrder;
    }

    public class WrapperForPolicyFailure : WrapperFor<PolicyFailure>, IPolicyFailure
    {
        private readonly PolicyFailure failureField;

        public WrapperForPolicyFailure(PolicyFailure failure) : base(failure)
        {
            failureField = failure;
        }

        public string Message => failureField.Message;
    }

    public class WrapperForWorkspace : WrapperFor<Workspace>, IWorkspace
    {
        private readonly TfsApiBridge bridgeField;
        private readonly Workspace workspaceField;

        public WrapperForWorkspace(TfsApiBridge bridge, Workspace workspace) : base(workspace)
        {
            bridgeField = bridge;
            workspaceField = workspace;
        }

        public IPendingChange[] GetPendingChanges() => bridgeField.Wrap<WrapperForPendingChange, PendingChange>(workspaceField.GetPendingChanges());

        public void Shelve(IShelveset shelveset, IPendingChange[] changes, TfsShelvingOptions options) => workspaceField.Shelve(bridgeField.Unwrap<Shelveset>(shelveset), bridgeField.Unwrap<PendingChange>(changes), bridgeField.Convert<ShelvingOptions>(options));

        private PolicyOverrideInfo ToTfs(TfsPolicyOverrideInfo policyOverrideInfo)
        {
            if (policyOverrideInfo == null)
                return null;
            return new PolicyOverrideInfo(policyOverrideInfo.Comment,
                                          bridgeField.Unwrap<PolicyFailure>(policyOverrideInfo.Failures));
        }

        public ICheckinEvaluationResult EvaluateCheckin(TfsCheckinEvaluationOptions options, IPendingChange[] allChanges, IPendingChange[] changes,
                                                        string comment, string author, ICheckinNote checkinNote, IEnumerable<IWorkItemCheckinInfo> workItemChanges) => bridgeField.Wrap<WrapperForCheckinEvaluationResult, CheckinEvaluationResult>(workspaceField.EvaluateCheckin(
                bridgeField.Convert<CheckinEvaluationOptions>(options),
                bridgeField.Unwrap<PendingChange>(allChanges),
                bridgeField.Unwrap<PendingChange>(changes),
                comment,
                bridgeField.Unwrap<CheckinNote>(checkinNote),
                bridgeField.Unwrap<WorkItemCheckinInfo>(workItemChanges)));

        public int PendAdd(string path) => workspaceField.PendAdd(path);

        public int PendEdit(string path)
            => workspaceField.PendEdit(new string[] { path }, RecursionType.None, null, LockLevel.Unchanged, false, PendChangesOptions.ForceCheckOutLocalVersion);

        public int PendDelete(string path) => workspaceField.PendDelete(path);

        public int PendRename(string pathFrom, string pathTo)
        {
            FileInfo info = new FileInfo(pathTo);
            if (info.Exists)
                info.Delete();
            return workspaceField.PendRename(pathFrom, pathTo);
        }

        private void DoUntilNoFailures(Func<GetStatus> get) => Retry.DoWhile(() => get().NumFailures != 0);

        public void ForceGetFile(string path, int changeset)
        {
            var item = new ItemSpec(path, RecursionType.None);
            DoUntilNoFailures(() => workspaceField.Get(new GetRequest(item, changeset), GetOptions.Overwrite | GetOptions.GetAll));
        }

        public void GetSpecificVersion(int changeset) => Retry.Do(() => DoUntilNoFailures(() => workspaceField.Get(new ChangesetVersionSpec(changeset), GetOptions.Overwrite | GetOptions.GetAll)));

        public void GetSpecificVersion(int changesetId, IEnumerable<IItem> items, bool noParallel)
        {
            var version = new ChangesetVersionSpec(changesetId);
            GetRequests(items.Select(e => new GetRequest(new ItemSpec(e.ServerItem, RecursionType.Full), version)), noParallel);
        }

        public void GetSpecificVersion(IChangeset changeset, bool noParallel) => GetSpecificVersion(changeset.ChangesetId, changeset.Changes, noParallel);

        public void GetSpecificVersion(int changesetId, IEnumerable<IChange> changes, bool noParallel) => GetRequests(changes.Select(change => new GetRequest(new ItemSpec(change.Item.ServerItem, RecursionType.None, change.Item.DeletionId), changesetId)), noParallel);

        public string GetLocalItemForServerItem(string serverItem) => workspaceField.GetLocalItemForServerItem(serverItem);

        public string GetServerItemForLocalItem(string localItem) => workspaceField.GetServerItemForLocalItem(localItem);

        public string OwnerName => workspaceField.OwnerName;

        public void Merge(string sourceTfsPath, string targetTfsPath)
        {
            var status = workspaceField.Merge(sourceTfsPath, targetTfsPath, null, null, LockLevel.None, RecursionType.Full,
                MergeOptions.AlwaysAcceptMine);
            var conflicts = workspaceField.QueryConflicts(null, true);
            foreach (var conflict in conflicts)
            {
                conflict.Resolution = Resolution.AcceptYours;
                workspaceField.ResolveConflict(conflict);
            }
        }

        public int Checkin(IPendingChange[] changes, string comment, string author, ICheckinNote checkinNote, IEnumerable<IWorkItemCheckinInfo> workItemChanges,
           TfsPolicyOverrideInfo policyOverrideInfo, bool overrideGatedCheckIn)
        {
            var checkinParameters = new WorkspaceCheckInParameters(bridgeField.Unwrap<PendingChange>(changes), comment)
            {
                CheckinNotes = bridgeField.Unwrap<CheckinNote>(checkinNote),
                AssociatedWorkItems = bridgeField.Unwrap<WorkItemCheckinInfo>(workItemChanges),
                PolicyOverride = ToTfs(policyOverrideInfo),
                OverrideGatedCheckIn = overrideGatedCheckIn
            };

            if (author != null)
                checkinParameters.Author = author;

            try
            {
                return workspaceField.CheckIn(checkinParameters);
            }
            catch (GatedCheckinException gatedException)
            {
                throw new GitTfsGatedCheckinException(gatedException.ShelvesetName, gatedException.AffectedBuildDefinitions, gatedException.CheckInTicket);
            }
        }

        public void GetRequests(IEnumerable<GetRequest> source, bool noParallel, int batchSize = 20) => source.ToBatch(batchSize).ForEach(batch =>
                                                                                                                 {
                                                                                                                     var items = batch;
                                                                                                                     Retry.Do(() =>
                                                                                                                     {
                                                                                                                         while (items.Length > 0)
                                                                                                                         {
                                                                                                                             var status = workspaceField.Get(items.ToArray(), GetOptions.Overwrite | GetOptions.GetAll);
                                                                                                                             if (status.NumFailures == 0)
                                                                                                                             {
                                                                                                                                 break;
                                                                                                                             }

                                                                                                                             items = status.GetFailures().Join(items, e => e.ServerItem, e => e.ItemSpec.Item, (failure, request) => request).ToArray();
                                                                                                                         }
                                                                                                                     });
                                                                                                                 }, !noParallel);
    }

    public class WrapperForBranchObject : WrapperFor<BranchObject>, IBranchObject
    {
        private readonly BranchObject branchField;

        public WrapperForBranchObject(BranchObject branch)
            : base(branch)
        {
            branchField = branch;
        }

        public string Path => branchField.Properties.RootItem.Item;
        public bool IsRoot => branchField.Properties.ParentBranch == null;
        public string ParentPath => branchField.Properties.ParentBranch.Item;
    }
}