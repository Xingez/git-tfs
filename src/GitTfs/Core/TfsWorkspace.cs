
namespace GitTfs.Core
{
    using global::GitTfs.Commands;
    using global::GitTfs.Core.TfsInterop;

    using global::System.Collections.ObjectModel;
    using global::System.Diagnostics;
    public class TfsWorkspace : ITfsWorkspace
    {
        private readonly IWorkspace workspaceField;
        private readonly string localDirectoryField;
        private readonly TfsChangesetInfo contextVersionField;
        private readonly CheckinOptions checkinOptionsField;
        private readonly ITfsHelper tfsHelperField;
        private readonly CheckinPolicyEvaluator policyEvaluatorField;

        private const string CheckinPolicyNoteMessage =
            "Note: If the checkin policy fails because the assemblies failed to load, please run the file `enable_checkin_policies_support.bat` in the git-tfs directory and try again.";

        public IGitTfsRemote Remote { get; private set; }

        public TfsWorkspace(IWorkspace workspace, string localDirectory, TfsChangesetInfo contextVersion, IGitTfsRemote remote, CheckinOptions checkinOptions, ITfsHelper tfsHelper, CheckinPolicyEvaluator policyEvaluator)
        {
            workspaceField = workspace;
            policyEvaluatorField = policyEvaluator;
            contextVersionField = contextVersion;
            checkinOptionsField = checkinOptions;
            tfsHelperField = tfsHelper;
            localDirectoryField = remote.Repository.IsBare ? Path.GetFullPath(localDirectory) : localDirectory;

            Remote = remote;
        }

        public void Shelve(string shelvesetName, bool evaluateCheckinPolicies, CheckinOptions checkinOptions, Func<string> generateCheckinComment)
        {
            var pendingChanges = workspaceField.GetPendingChanges();

            if (pendingChanges.IsEmpty())
                throw new GitTfsException("Nothing to shelve!");

            var shelveset = tfsHelperField.CreateShelveset(workspaceField, shelvesetName);
            shelveset.Comment = string.IsNullOrWhiteSpace(checkinOptionsField.CheckinComment) && !checkinOptionsField.NoGenerateCheckinComment ? generateCheckinComment() : checkinOptionsField.CheckinComment;
            shelveset.WorkItemInfo = GetWorkItemInfos(checkinOptions).ToArray();
            if (evaluateCheckinPolicies)
            {
                var checkinProblems = policyEvaluatorField.EvaluateCheckin(workspaceField, pendingChanges, shelveset.Comment, null, shelveset.WorkItemInfo);
                TraceCheckinPolicyErrors(checkinProblems, false);
            }
            workspaceField.Shelve(shelveset, pendingChanges, checkinOptionsField.Force ? TfsShelvingOptions.Replace : TfsShelvingOptions.None);
        }

        public void DeleteShelveset(string shelvesetName) => tfsHelperField.DeleteShelveset(workspaceField, shelvesetName);

        public int CheckinTool(Func<string> generateCheckinComment)
        {
            var pendingChanges = workspaceField.GetPendingChanges();

            if (pendingChanges.IsEmpty())
                throw new GitTfsException("Nothing to checkin!");

            var checkinComment = checkinOptionsField.CheckinComment;
            if (string.IsNullOrWhiteSpace(checkinComment) && !checkinOptionsField.NoGenerateCheckinComment)
                checkinComment = generateCheckinComment();

            var newChangesetId = tfsHelperField.ShowCheckinDialog(workspaceField, pendingChanges, GetWorkItemCheckedInfos(), checkinComment);
            if (newChangesetId <= 0)
                throw new GitTfsException("Checkin canceled!");
            return newChangesetId;
        }

        public void Merge(string sourceTfsPath, string tfsRepositoryPath) => workspaceField.Merge(sourceTfsPath, tfsRepositoryPath);

        private static void TraceCheckinPolicyErrors(CheckinPolicyEvaluator.CheckinPolicyEvaluationResult checkinProblems, bool overridePolicyErrors)
        {
            string prefix = overridePolicyErrors ? "[OVERRIDDEN] " : "[ERROR] ";
            foreach (var message in checkinProblems.Messages)
            {
                Trace.TraceWarning(prefix + message);
            }

            if (checkinProblems.HasErrors && !overridePolicyErrors)
                Trace.TraceInformation("Note: If the checkin policy fails because the assemblies failed to load, please run the file `enable_checkin_policies_support.bat` in the git-tfs directory and try again.");
        }

        public int Checkin(CheckinOptions options, Func<string> generateCheckinComment = null)
        {
            if (options == null) options = checkinOptionsField;

            var checkinComment = options.CheckinComment;
            if (string.IsNullOrWhiteSpace(checkinComment) && !options.NoGenerateCheckinComment && generateCheckinComment != null)
                checkinComment = generateCheckinComment();

            var pendingChanges = workspaceField.GetPendingChanges();

            if (pendingChanges.IsEmpty())
                throw new GitTfsException("Nothing to checkin!");

            var workItemInfos = GetWorkItemInfos(options);
            var checkinNote = tfsHelperField.CreateCheckinNote(options.CheckinNotes);

            var checkinProblems = policyEvaluatorField.EvaluateCheckin(workspaceField, pendingChanges, checkinComment, checkinNote, workItemInfos);
            if (checkinProblems.HasErrors)
            {
                bool overridePolicyErrors = options.Force && !string.IsNullOrWhiteSpace(options.OverrideReason);
                TraceCheckinPolicyErrors(checkinProblems, overridePolicyErrors);

                if (!options.Force)
                {
                    throw new GitTfsException("No changes checked in.");
                }
                if (string.IsNullOrWhiteSpace(options.OverrideReason))
                {
                    throw new GitTfsException("A reason must be supplied (-f REASON) to override the policy violations.");
                }
            }

            var policyOverride = GetPolicyOverrides(options, checkinProblems.Result);
            try
            {
                var newChangeset = workspaceField.Checkin(pendingChanges, checkinComment, options.AuthorTfsUserId, checkinNote, workItemInfos, policyOverride, options.OverrideGatedCheckIn);
                if (newChangeset == 0)
                {
                    throw new GitTfsException("Checkin failed!");
                }
                else
                {
                    return newChangeset;
                }
            }
            catch (GitTfsGatedCheckinException e)
            {
                return LaunchGatedCheckinBuild(e.AffectedBuildDefinitions, e.ShelvesetName, e.CheckInTicket);
            }
        }

        private int LaunchGatedCheckinBuild(ReadOnlyCollection<KeyValuePair<string, Uri>> affectedBuildDefinitions, string shelvesetName, string checkInTicket)
        {
            Trace.TraceInformation("Due to a gated check-in, a shelveset '" + shelvesetName + "' containing your changes has been created and need to be built before it can be committed.");
            KeyValuePair<string, Uri> buildDefinition;
            if (affectedBuildDefinitions.Count == 1)
            {
                buildDefinition = affectedBuildDefinitions.First();
            }
            else
            {
                int choice;
                do
                {
                    Trace.TraceInformation("Build definitions that can be used:");
                    for (int i = 0; i < affectedBuildDefinitions.Count; i++)
                    {
                        Trace.TraceInformation((i + 1) + ": " + affectedBuildDefinitions[i].Key);
                    }
                    Trace.TraceInformation("Please choose the build definition to trigger?");
                } while (!int.TryParse(Console.ReadLine(), out choice) || choice <= 0 || choice > affectedBuildDefinitions.Count);
                buildDefinition = affectedBuildDefinitions.ElementAt(choice - 1);
            }
            return Remote.Tfs.QueueGatedCheckinBuild(buildDefinition.Value, buildDefinition.Key, shelvesetName, checkInTicket);
        }

        private TfsPolicyOverrideInfo GetPolicyOverrides(CheckinOptions options, ICheckinEvaluationResult checkinProblems)
        {
            if (!options.Force || string.IsNullOrWhiteSpace(options.OverrideReason))
                return null;
            return new TfsPolicyOverrideInfo { Comment = options.OverrideReason, Failures = checkinProblems.PolicyFailures };
        }

        public string GetLocalPath(string path) => Path.Combine(localDirectoryField, path);

        public void Add(string path)
        {
            Trace.TraceInformation(" add " + path);
            var added = workspaceField.PendAdd(GetLocalPath(path));
            if (added != 1) throw new Exception("One item should have been added, but actually added " + added + " items.");
        }

        public void Edit(string path)
        {
            var localPath = GetLocalPath(path);
            Trace.TraceInformation(" edit " + localPath);
            GetFromTfs(localPath);
            var edited = workspaceField.PendEdit(localPath);
            if (edited != 1)
            {
                if (checkinOptionsField.IgnoreMissingItems)
                {
                    Trace.TraceWarning("Warning: One item should have been edited, but actually edited " + edited + ". Ignoring item.");
                }
                else if (edited == 0 && checkinOptionsField.AddMissingItems)
                {
                    Trace.TraceWarning("Warning: One item should have been edited, but was not found. Adding the file instead.");
                    Add(path);
                }
                else
                {
                    throw new Exception("One item should have been edited, but actually edited " + edited + " items.");
                }
            }
        }

        public void Delete(string path)
        {
            path = GetLocalPath(path);
            Trace.TraceInformation(" delete " + path);
            GetFromTfs(path);
            var deleted = workspaceField.PendDelete(path);
            if (deleted != 1) throw new Exception("One item should have been deleted, but actually deleted " + deleted + " items.");
        }

        public void Rename(string pathFrom, string pathTo, string score)
        {
            Trace.TraceInformation(" rename " + pathFrom + " to " + pathTo + " (score: " + score + ")");
            GetFromTfs(GetLocalPath(pathFrom));
            var result = workspaceField.PendRename(GetLocalPath(pathFrom), GetLocalPath(pathTo));
            if (result != 1) throw new ApplicationException("Unable to rename item from " + pathFrom + " to " + pathTo);
        }

        private void GetFromTfs(string path) => workspaceField.ForceGetFile(workspaceField.GetServerItemForLocalItem(path), contextVersionField.ChangesetId);

        public void Get(int changesetId) => workspaceField.GetSpecificVersion(changesetId);

        public void Get(int changesetId, IEnumerable<IItem> items) => workspaceField.GetSpecificVersion(changesetId, items, noParallel: true);

        public void Get(IChangeset changeset) => workspaceField.GetSpecificVersion(changeset, noParallel: true);

        public void Get(int changesetId, IEnumerable<IChange> changes)
        {
            if (changes.Any())
            {
                workspaceField.GetSpecificVersion(changesetId, changes, noParallel: true);
            }
        }

        public string GetLocalItemForServerItem(string serverItem) => workspaceField.GetLocalItemForServerItem(serverItem);

        private IEnumerable<IWorkItemCheckinInfo> GetWorkItemInfos(CheckinOptions options = null) => GetWorkItemInfosHelper<IWorkItemCheckinInfo>(tfsHelperField.GetWorkItemInfos, options);

        private IEnumerable<IWorkItemCheckedInfo> GetWorkItemCheckedInfos() => GetWorkItemInfosHelper<IWorkItemCheckedInfo>(tfsHelperField.GetWorkItemCheckedInfos);

        private IEnumerable<T> GetWorkItemInfosHelper<T>(Func<IEnumerable<string>, TfsWorkItemCheckinAction, IEnumerable<T>> func, CheckinOptions options = null)
        {
            var checkinOptions = options ?? checkinOptionsField;

            var workItemInfos = func(checkinOptions.WorkItemsToAssociate, TfsWorkItemCheckinAction.Associate);
            workItemInfos = workItemInfos.Append(
                func(checkinOptions.WorkItemsToResolve, TfsWorkItemCheckinAction.Resolve));
            return workItemInfos;
        }
    }
}
