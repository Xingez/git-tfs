
namespace GitTfs.Commands
{
    using global::System.Diagnostics;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    public abstract class CheckinBase : GitTfsCommand
    {
        protected readonly CheckinOptions checkinOptionsField;
        private readonly TfsWriter writerField;

        protected CheckinBase(CheckinOptions checkinOptions, TfsWriter writer)
        {
            checkinOptionsField = checkinOptions;
            writerField = writer;
        }

        public OptionSet OptionSet => checkinOptionsField.OptionSet;

        public int Run() => Run("HEAD");

        public int Run(string refToCheckin) => writerField.Write(refToCheckin, PerformCheckin);

        private int PerformCheckin(TfsChangesetInfo parentChangeset, string refToCheckin)
        {
            var newChangesetId = DoCheckin(parentChangeset, refToCheckin);

            if (checkinOptionsField.NoMerge)
            {
                Trace.TraceInformation($"TFS Changeset #{newChangesetId} was created.");
                parentChangeset.Remote.Fetch();
            }
            else
            {
                Trace.TraceInformation($"TFS Changeset #{newChangesetId} was created. Marking it as a merge commit...");
                parentChangeset.Remote.FetchWithMerge(newChangesetId, false, refToCheckin);

                if (refToCheckin == "HEAD")
                    parentChangeset.Remote.Repository.Merge(parentChangeset.Remote.MaxCommitHash);
            }

            Trace.WriteLine("Cleaning...");
            parentChangeset.Remote.CleanupWorkspaceDirectory();

            return GitTfsExitCodes.OK;
        }

        protected abstract int DoCheckin(TfsChangesetInfo changeset, string refToCheckin);
    }
}
