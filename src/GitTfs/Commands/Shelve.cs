
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    using global::System.Diagnostics;
    [Pluggable("shelve")]
    [Description("shelve [options] shelveset-name [ref-to-shelve]")]
    [RequiresValidGitRepository]
    public class Shelve : GitTfsCommand
    {
        private readonly CheckinOptions checkinOptionsField;
        private readonly CheckinOptionsFactory checkinOptionsFactoryField;
        private readonly TfsWriter writerField;
        private readonly Globals globalsField;

        private bool EvaluateCheckinPolicies { get; set; }

        public Shelve(CheckinOptions checkinOptions, TfsWriter writer, Globals globals)
        {
            globalsField = globals;
            checkinOptionsField = checkinOptions;
            checkinOptionsFactoryField = new CheckinOptionsFactory(globalsField);
            writerField = writer;
        }

        public OptionSet OptionSet => new OptionSet
                {
                    { "p|evaluate-policies", "Evaluate checkin policies (default: false)",
                        v => EvaluateCheckinPolicies = v != null },
                    { "f|force", "Force a shelve, and overwrite an existing shelveset",
                        v => { checkinOptionsField.Force = true; } },
                }.Merge(checkinOptionsField.OptionSet);

        public int Run(string shelvesetName) => Run(shelvesetName, "HEAD");

        public int Run(string shelvesetName, string refToShelve) => writerField.Write(refToShelve, (changeset, referenceToShelve) =>
                                                                             {
                                                                                 if (!checkinOptionsField.Force && changeset.Remote.HasShelveset(shelvesetName))
                                                                                 {
                                                                                     Trace.TraceInformation("Shelveset \"" + shelvesetName + "\" already exists. Use -f to replace it.");
                                                                                     return GitTfsExitCodes.ForceRequired;
                                                                                 }

                                                                                 var commit = globalsField.Repository.GetCommit(refToShelve);
                                                                                 var message = commit != null // this is only null in the unit tests
                                                                                     ? BuildCommitMessage(commit, !checkinOptionsField.NoGenerateCheckinComment,
                                                                                         changeset.Remote.MaxCommitHash)
                                                                                     : string.Empty;

                                                                                 var shelveSpecificCheckinOptions = checkinOptionsFactoryField.BuildShelveSetSpecificCheckinOptions(checkinOptionsField, message);

                                                                                 changeset.Remote.Shelve(shelvesetName, referenceToShelve, changeset, shelveSpecificCheckinOptions, EvaluateCheckinPolicies);
                                                                                 return GitTfsExitCodes.OK;
                                                                             });

        public string BuildCommitMessage(GitCommit commit, bool generateCheckinComment, string latest) => generateCheckinComment
                               ? globalsField.Repository.GetCommitMessage(commit.Sha, latest)
                               : globalsField.Repository.GetCommit(commit.Sha).Message;
    }
}
