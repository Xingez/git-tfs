
namespace GitTfs.Core
{
    using global::System.Diagnostics;
    using global::System.Text.RegularExpressions;
    using global::System.Text;
    using global::GitTfs.Util;
    public class GitHelpers : IGitHelpers
    {
        private readonly IServiceProvider servicesField;

        /// <summary>
        /// Starting with version 1.7.10, Git uses UTF-8.
        /// Use this encoding for Git input and output.
        /// </summary>
        private static readonly Encoding encodingField = new UTF8Encoding(false, true);

        public GitHelpers(IServiceProvider services)
        {
            servicesField = services;
        }

        /// <summary>
        /// Runs the given git command, and returns the contents of its STDOUT.
        /// </summary>
        public string Command(params string[] command)
        {
            string retVal = null;
            CommandOutputPipe(stdout => retVal = stdout.ReadToEnd(), command);
            return retVal;
        }

        /// <summary>
        /// Runs the given git command, and returns the first line of its STDOUT.
        /// </summary>
        public string CommandOneline(params string[] command)
        {
            string retVal = null;
            CommandOutputPipe(stdout => retVal = stdout.ReadLine(), command);
            return retVal;
        }

        /// <summary>
        /// Runs the given git command, and passes STDOUT through to the current process's STDOUT.
        /// </summary>
        public void CommandNoisy(params string[] command) => CommandOutputPipe(stdout => Trace.TraceInformation(stdout.ReadToEnd()), command);

        /// <summary>
        /// Runs the given git command, and redirects STDOUT to the provided action.
        /// </summary>
        public void CommandOutputPipe(Action<TextReader> handleOutput, params string[] command) => Time(command, () =>
                                                                                                                              {
                                                                                                                                  AssertValidCommand(command);
                                                                                                                                  var process = Start(command, RedirectStdout);
                                                                                                                                  handleOutput(process.StandardOutput);
                                                                                                                                  Close(process);
                                                                                                                              });

        /// <summary>
        /// Runs the given git command, and returns a reader for STDOUT. NOTE: The returned value MUST be disposed!
        /// </summary>
        public TextReader CommandOutputPipe(params string[] command)
        {
            AssertValidCommand(command);
            var process = Start(command, RedirectStdout);
            return new ProcessStdoutReader(this, process);
        }

        private class ProcessStdoutReader : TextReader
        {
            private readonly GitProcess processField;
            private readonly GitHelpers helperField;

            public ProcessStdoutReader(GitHelpers helper, GitProcess process)
            {
                helperField = helper;
                processField = process;
            }

            public override void Close() => helperField.Close(processField);

            protected override void Dispose(bool disposing)
            {
                if (disposing && processField != null)
                {
                    Close();
                }
                base.Dispose(disposing);
            }

            public override bool Equals(object obj) => processField.StandardOutput.Equals(obj);

            public override int GetHashCode() => processField.StandardOutput.GetHashCode();

            public override int Peek() => processField.StandardOutput.Peek();

            public override int Read() => processField.StandardOutput.Read();

            public override int Read(char[] buffer, int index, int count) => processField.StandardOutput.Read(buffer, index, count);

            public override int ReadBlock(char[] buffer, int index, int count) => processField.StandardOutput.ReadBlock(buffer, index, count);

            public override string ReadLine() => processField.StandardOutput.ReadLine();

            public override string ReadToEnd() => processField.StandardOutput.ReadToEnd();

            public override string ToString() => processField.StandardOutput.ToString();
        }

        public void CommandInputPipe(Action<TextWriter> action, params string[] command) => Time(command, () =>
                                                                                                                       {
                                                                                                                           AssertValidCommand(command);
                                                                                                                           var process = Start(command, RedirectStdin);
                                                                                                                           action(process.StandardInput.WithEncoding(encodingField));
                                                                                                                           Close(process);
                                                                                                                       });

        public void CommandInputOutputPipe(Action<TextWriter, TextReader> interact, params string[] command) => Time(command, () =>
                                                                                                                                           {
                                                                                                                                               AssertValidCommand(command);
                                                                                                                                               var process = Start(command, Ext.And<ProcessStartInfo>(RedirectStdin, RedirectStdout));
                                                                                                                                               interact(process.StandardInput.WithEncoding(encodingField), process.StandardOutput);
                                                                                                                                               Close(process);
                                                                                                                                           });

        private void Time(string[] command, Action action)
        {
            var start = DateTime.Now;
            try
            {
                action();
            }
            finally
            {
                var end = DateTime.Now;
                Trace.WriteLine($"[{end - start}] {string.Join(" ", command)}", "git command time");
            }
        }

        private void Close(GitProcess process)
        {
            // if caller doesn't read entire stdout to the EOF - it is possible that
            // child process will hang waiting until there will be free space in stdout
            // buffer to write the rest of the output.
            // See https://github.com/git-tfs/git-tfs/issues/121 for details.
            if (process.StartInfo.RedirectStandardOutput)
            {
                process.StandardOutput.BaseStream.CopyTo(Stream.Null);
                process.StandardOutput.Close();
            }

            var waitTimeout = TimeSpan.FromSeconds(10);
            var waitTimer = Stopwatch.StartNew();
            if (!process.WaitForExit((int)waitTimeout.TotalMilliseconds))
            {
                Trace.WriteLine("Git process wait timed out after " + waitTimer.Elapsed.ToString("c")
                                + " (timeout " + waitTimeout.ToString("c") + ").");
                throw new GitCommandException("Command did not terminate.", process);
            }
            Trace.WriteLine("Git process wait completed in " + waitTimer.Elapsed.ToString("c")
                            + " (timeout " + waitTimeout.ToString("c") + ").");
            if (process.ExitCode != 0)
                throw new GitCommandException($"Command exited with error code: {process.ExitCode}\n{process.StandardErrorString}", process);
        }

        private void RedirectStdout(ProcessStartInfo startInfo)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.StandardOutputEncoding = encodingField;
        }

        private void RedirectStderr(ProcessStartInfo startInfo)
        {
            startInfo.RedirectStandardError = true;
            startInfo.StandardErrorEncoding = encodingField;
        }

        private void RedirectStdin(ProcessStartInfo startInfo) => startInfo.RedirectStandardInput = true;// there is no StandardInputEncoding property, use extension method StreamWriter.WithEncoding instead

        private GitProcess Start(string[] command) => Start(command, x => { });

        protected virtual GitProcess Start(string[] command, Action<ProcessStartInfo> initialize)
        {
            var startInfo = new ProcessStartInfo();
            startInfo.FileName = "git";
            startInfo.SetArguments(command);
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.EnvironmentVariables["GIT_PAGER"] = "cat";
            RedirectStderr(startInfo);
            initialize(startInfo);
            Trace.WriteLine("Starting process: " + startInfo.FileName + " " + startInfo.Arguments, "git command");
            var process = new GitProcess(Process.Start(startInfo));
            process.ConsumeStandardError();
            return process;
        }

        /// <summary>
        /// WrapGitCommandErrors the actions, and if there are any git exceptions, rethrow a new exception with the given message.
        /// </summary>
        /// <param name="exceptionMessage">A friendlier message to wrap the GitCommandException with. {0} is replaced with the command line and {1} is replaced with the exit code.</param>
        /// <param name="action"></param>
        public void WrapGitCommandErrors(string exceptionMessage, Action action)
        {
            try
            {
                action();
            }
            catch (GitCommandException e)
            {
                throw new Exception(string.Format(exceptionMessage, e.Process.StartInfo.FileName + " " + e.Process.StartInfo.Arguments, e.Process.ExitCode), e);
            }
        }

        public IGitRepository MakeRepository(string dir) =>
            servicesField.CreateInstance<GitRepository>(dir, servicesField, servicesField.GetService<Globals>(), servicesField.GetRequiredService<RemoteConfigConverter>());

        private static readonly Regex ValidCommandName = new Regex("^[a-z0-9A-Z_-]+$");
        private static void AssertValidCommand(string[] command)
        {
            if (command.Length < 1 || !ValidCommandName.IsMatch(command[0]))
                throw new Exception("bad git command: " + (command.Length == 0 ? "" : command[0]));
        }

        protected class GitProcess
        {
            private readonly Process processField;

            public GitProcess(Process process)
            {
                processField = process;
            }

            public static implicit operator Process(GitProcess process)
            {
                return process.processField;
            }

            public string StandardErrorString { get; private set; }

            public void ConsumeStandardError()
            {
                StandardErrorString = "";
                processField.ErrorDataReceived += StdErrReceived;
                processField.BeginErrorReadLine();
            }

            private void StdErrReceived(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null && e.Data.Trim() != "")
                {
                    var data = e.Data;
                    Trace.WriteLine(data.TrimEnd(), "git stderr");
                    StandardErrorString += data;
                }
            }

            // Delegate a bunch of things to the Process.

            public ProcessStartInfo StartInfo => processField.StartInfo;
            public int ExitCode => processField.ExitCode;

            public StreamWriter StandardInput => processField.StandardInput;
            public StreamReader StandardOutput => processField.StandardOutput;

            public bool WaitForExit(int milliseconds) => processField.WaitForExit(milliseconds);
        }
    }
}
