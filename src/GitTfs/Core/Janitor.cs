
namespace GitTfs.Core
{
    using GitTfs.Util;

    using System.Diagnostics;
    [SingletonService]
    public class Janitor : IDisposable
    {
        private readonly Queue<Action> actionsField = new Queue<Action>();

        public void CleanThisUpWhenWeClose(Action action) => actionsField.Enqueue(action);

        public void Dispose()
        {
            while (actionsField.Count > 0)
            {
                try
                {
                    actionsField.Dequeue()();
                }
                catch (Exception e)
                {
                    Trace.WriteLine("Janitor tried to clean something up, and it failed: " + e);
                }
            }
        }
    }
}
