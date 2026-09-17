
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Core;
    using global::GitTfs.Util;
    [Pluggable("checkin")]
    [Description("checkin [options] [ref-to-checkin]")]
    [RequiresValidGitRepository]
    public class Checkin : CheckinBase
    {
        public Checkin(CheckinOptions checkinOptions, TfsWriter writer)
            : base(checkinOptions, writer)
        {
        }

        protected override int DoCheckin(TfsChangesetInfo changeset, string refToCheckin) => changeset.Remote.Checkin(refToCheckin, changeset, checkinOptionsField);
    }
}
