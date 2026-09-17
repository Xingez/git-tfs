

namespace GitTfs.Commands
{
    using global::System.ComponentModel;

    using global::GitTfs.Core;

    using global::GitTfs.Util;
    [Pluggable("exportmap")]
    [Description("exportmap -f <file>")]
    [RequiresValidGitRepository]
    public class ExportMap : GitTfsCommand
    {
        private readonly Globals globalsField;
        private readonly Help helperField;

        public ExportMap(Globals globals, Help helper)
        {
            globalsField = globals;
            helperField = helper;
        }

        public string FilePath { get; set; }

        public OptionSet OptionSet => new OptionSet
                {
                     { "f|file=", "The output file path",
                        f => FilePath = f }
                };

        public int Run()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return helperField.Run(this);
            }

            var commits = globalsField.Repository.GetCommitChangeSetPairs();
            File.WriteAllLines(FilePath, commits.Select(map => $"{map.Key}-{map.Value}"));

            return 0;
        }
    }
}
