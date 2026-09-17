using System.Diagnostics;
using System.Text.Json;

using GitTfs.Core;
using GitTfs.Core.TfsInterop;

namespace GitTfs.VsFake
{
    public class Script
    {
        public const string EnvVar = "GIT_TFS_VSFAKE_SCRIPT";

        public static Script Load(string path) =>
            JsonSerializer.Deserialize<Script>(File.ReadAllText(path)) ?? new Script();

        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));

        public List<ScriptedChangeset> Changesets { get; set; } = new List<ScriptedChangeset>();

        public List<ScriptedRootBranch> RootBranches { get; set; } = new List<ScriptedRootBranch>();
    }

    [Serializable]
    [DebuggerDisplay("{Id}")]
    public class ScriptedChangeset
    {
        public int Id { get; set; }
        public string Comment { get; set; }
        public DateTime CheckinDate { get; set; }
        public List<ScriptedChange> Changes { get; set; } = new List<ScriptedChange>();

        public bool IsBranchChangeset { get; set; }
        public BranchChangesetDatas BranchChangesetDatas { get; set; }

        public bool IsMergeChangeset { get; set; }
        public MergeChangesetDatas MergeChangesetDatas { get; set; }
        public string Committer { get; set; }

    }

    [Serializable]
    [DebuggerDisplay("{ChangeType}/{ItemType}/{RepositoryPath}/{ItemId}")]
    public class ScriptedChange
    {
        public TfsChangeType ChangeType { get; set; }
        public TfsItemType ItemType { get; set; }
        public string RepositoryPath { get; set; }
        public byte[] Content { get; set; }
        public int? ItemId { get; set; }
    }

    [Serializable]
    public class BranchChangesetDatas
    {
        public int RootChangesetId { get; set; }
        public string BranchPath { get; set; }
        public string ParentBranch { get; set; }
    }

    [Serializable]
    public class MergeChangesetDatas
    {
        public int BeforeMergeChangesetId { get; set; }
        public string BranchPath { get; set; }
        public string MergeIntoBranch { get; set; }
    }

    [Serializable]
    public class ScriptedRootBranch
    {
        public string BranchPath { get; set; }
    }
}
