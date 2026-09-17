
namespace GitTfs.Core.BranchVisitors
{
    using global::GitTfs.Core.TfsInterop;
    public class BranchTreeContainsPathVisitor : IBranchTreeVisitor
    {
        private readonly string searchPathField;
        private readonly bool searchExactPathField;

        public BranchTreeContainsPathVisitor(string searchPath, bool searchExactPath)
        {
            searchPathField = searchPath;
            searchExactPathField = searchExactPath;
        }

        public bool Found { get; private set; }

        public void Visit(BranchTree childBranch, int level)
        {
            if (Found == false
                && ((searchExactPathField && searchPathField.ToLower() == childBranch.Path.ToLower())
                || (!searchExactPathField && searchPathField.ToLower().IndexOf(childBranch.Path.ToLower()) == 0)))
            {
                Found = true;
            }
        }
    }
}