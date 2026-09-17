
namespace GitTfs.Util
{
    using global::System.Reflection;

    using global::GitTfs.Commands;
    using global::GitTfs.Core;
    public class GitTfsCommandRunner
    {
        private readonly IHelpHelper helpField;

        public GitTfsCommandRunner(IHelpHelper help)
        {
            helpField = help;
        }

        public int Run(GitTfsCommand command, IList<string> args)
        {
            try
            {
                var runMethods = command.GetType().GetMethods().Where(m => m.Name == "Run" && m.ReturnType == typeof(int)).Select(m => new { Method = m, Parameters = m.GetParameters() });
                var splitRunMethods = runMethods.Where(m => m.Parameters.All(p => p.ParameterType == typeof(string)));
                var exactMatchingMethod = splitRunMethods.SingleOrDefault(m => m.Parameters.Length == args.Count);
                if (exactMatchingMethod != null)
                    return (int)exactMatchingMethod.Method.Invoke(command, args.ToArray());
                var defaultRunMethod = runMethods.FirstOrDefault(m => m.Parameters.Length == 1 && m.Parameters[0].ParameterType.IsAssignableFrom(args.GetType()));
                if (defaultRunMethod != null)
                    return (int)defaultRunMethod.Method.Invoke(command, new object[] { args });
                return helpField.ShowHelpForInvalidArguments(command);
            }
            catch (TargetInvocationException e)
            {
                if (e.InnerException is GitTfsException)
                    throw e.InnerException;
                throw;
            }
        }
    }
}
