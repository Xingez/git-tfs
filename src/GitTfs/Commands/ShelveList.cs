
namespace GitTfs.Commands
{
    using global::System.ComponentModel;
    using global::GitTfs.Util;
    using global::GitTfs.Core;
    [Pluggable("shelve-list")]
    [Description("shelve-list -u <shelve-owner-name> [options]")]
    [RequiresValidGitRepository]
    public class ShelveList : GitTfsCommand
    {
        private readonly Globals globalsField;

        public string SortBy { get; set; }
        public bool FullFormat { get; set; }
        public string Owner { get; set; }

        public OptionSet OptionSet => new OptionSet
                {
                    { "s|sort=", "How to sort shelvesets\ndate, owner, name, comment",
                        v => SortBy = v },
                    { "f|full", "Detailed output",
                        v => FullFormat = v != null },
                    { "u|user=", "Shelveset owner (default: current user)\nUse 'all' to get all shelvesets.",
                        v => Owner = v },
                };

        public ShelveList(Globals globals)
        {
            globalsField = globals;
        }

        public int Run()
        {
            var remote = globalsField.Repository.ReadTfsRemote(globalsField.RemoteId);
            return remote.Tfs.ListShelvesets(this, remote);
        }
    }
}
