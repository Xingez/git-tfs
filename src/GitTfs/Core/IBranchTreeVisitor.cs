
namespace GitTfs.Core
{
    using global::GitTfs.Core.TfsInterop;
    public interface IBranchTreeVisitor
    {
        void Visit(BranchTree childBranch, int level);
    }
}