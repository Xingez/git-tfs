
namespace GitTfs.Core
{
    using GitTfs.Core.TfsInterop;
    public interface IBranchTreeVisitor
    {
        void Visit(BranchTree childBranch, int level);
    }
}