
namespace GitTfs.Test
{
    public class BaseTest
    {
        /// <summary>
        /// Set this variable to `true` to display trace logs
        /// This value is false by default because verbose trace output makes automated build logs difficult to read.
        /// </summary>
        public const bool DebugTests = false;

        public static bool DisplayTrace => System.Diagnostics.Debugger.IsAttached || DebugTests;
        static BaseTest()
        {
            Globals.DisableGarbageCollect = true;
            if (!DisplayTrace)
            {
                System.Diagnostics.Trace.Listeners.Clear();
            }
        }
    }
}
