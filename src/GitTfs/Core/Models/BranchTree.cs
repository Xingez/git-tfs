namespace GitTfs.Core.TfsInterop
{
    public sealed class BranchTree
    {
        public BranchTree(IBranchObject branch)
            : this(branch, new List<BranchTree>())
        {
        }

        public BranchTree(IBranchObject branch, IEnumerable<BranchTree> childBranches)
            : this(branch, childBranches.ToList())
        {
        }

        public BranchTree(IBranchObject branch, List<BranchTree> childBranches)
        {
            if (childBranches == null)
                throw new ArgumentNullException(nameof(childBranches));
            Branch = branch;
            ChildBranches = childBranches;
        }

        public IBranchObject Branch { get; }

        public List<BranchTree> ChildBranches { get; }

        public string Path => Branch.Path;
        public string ParentPath => Branch.ParentPath;
        public bool IsRoot => Branch.IsRoot;

        public override string ToString() => $"{Path} [{ChildBranches.Count} children]";
    }
}
