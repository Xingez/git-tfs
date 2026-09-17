
namespace GitTfs.Core
{
    using global::GitTfs.Core.TfsInterop;
    public class CheckinPolicyEvaluator
    {
        public CheckinPolicyEvaluationResult EvaluateCheckin(IWorkspace workspace, IPendingChange[] pendingChanges, string comment, ICheckinNote checkinNote, IEnumerable<IWorkItemCheckinInfo> workItemInfo)
        {
            var result = workspace.EvaluateCheckin(TfsCheckinEvaluationOptions.All, pendingChanges,
                                                   pendingChanges, comment, null, checkinNote,
                                                   workItemInfo);
            return new CheckinPolicyEvaluationResult(result);
        }

        public class CheckinPolicyEvaluationResult
        {
            private readonly ICheckinEvaluationResult resultField;

            public CheckinPolicyEvaluationResult(ICheckinEvaluationResult result)
            {
                resultField = result;
            }

            public bool HasErrors => Messages.Any();

            public IEnumerable<string> Messages => BuildMessages();

            public ICheckinEvaluationResult Result => resultField;

            private IEnumerable<string> BuildMessages()
            {
                foreach (var x in resultField.Conflicts)
                {
                    yield return "Conflict: " + x.ServerItem + ": " + x.Message;
                }
                foreach (var x in resultField.PolicyFailures)
                {
                    yield return "Policy: " + x.Message;
                }
                foreach (var x in resultField.NoteFailures)
                {
                    yield return "Checkin Note: " + x.Definition.Name + ": " + x.Message;
                }
                if (resultField.PolicyEvaluationException != null)
                {
                    yield return "Exception: " + resultField.PolicyEvaluationException.Message;
                }
            }
        }
    }
}
