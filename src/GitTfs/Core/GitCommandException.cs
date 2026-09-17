
namespace GitTfs.Core
{
    using global::System.Diagnostics;
    public class GitCommandException : Exception
    {
        public Process Process { get; }

        public GitCommandException(string message, Process process) : base(message)
        {
            Process = process;
        }
    }
}
