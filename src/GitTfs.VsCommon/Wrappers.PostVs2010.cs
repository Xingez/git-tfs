namespace GitTfs.VsCommon
{
    using global::Microsoft.TeamFoundation.VersionControl.Client;
    using global::GitTfs.Core.TfsInterop;

    public class WrapperForBranchObject : WrapperFor<BranchObject>, IBranchObject
    {
        BranchObject branchField;

        public WrapperForBranchObject(BranchObject branch) : base(branch)
        {
            branchField = branch;
        }

        public string Path
        {
            get { return branchField.Properties.RootItem.Item; }
        }

        public bool IsRoot
        {
            get { return branchField.Properties.ParentBranch == null; }
        }

        public string ParentPath
        {
            get { return branchField.Properties.ParentBranch.Item; }
        }
    }
}
