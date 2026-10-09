namespace GitTfs.Commands
{
    using GitTfs.Core.RestTfs;
    using Spectre.Console;
    using Spectre.Console.Rendering;

    internal static class SpectreCloneProgress
    {
        public static int Run(Func<IChangesetProgressReporter, int> action)
        {
            var console = AnsiConsole.Console;
            using var metrics = new ConsoleMetrics();
            // A real ANSI terminal can render live progress even when CI detection disabled interaction.
            if (console.Profile.Out.IsTerminal && console.Profile.Capabilities.Ansi)
                console.Profile.Capabilities.Interactive = true;
            if (!console.Profile.Capabilities.Interactive)
            {
                var staticReporter = new StaticReporter(console);
                var result = action(staticReporter);
                console.Write(metrics.RenderDisplay(live: false));
                return result;
            }

            var display = console.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskStatusColumn(console.Profile.Capabilities.Unicode),
                    new TaskDescriptionColumn { Wrap = true },
                    new ProgressBarColumn
                    {
                        Width = 14,
                        CompletedStyle = new Style(Color.Blue),
                        FinishedStyle = new Style(Color.Green),
                        RemainingStyle = new Style(Color.Grey),
                        IndeterminateStyle = new Style(Color.Cyan)
                    },
                    new FilePercentageColumn());
            display.RefreshRate = TimeSpan.FromMilliseconds(250);
            var finished = false;
            Reporter reporter = null;
            string lastPhase = null;
            display.RenderHook = (progress, _) =>
            {
                var phase = reporter?.Phase ?? "Scanning";
                var refresh = phase != lastPhase;
                lastPhase = phase;
                return Dashboard(console, metrics.RenderDisplay(live: !finished, refresh), progress, phase);
            };
            var exitCode = display.Start(context =>
                {
                    reporter = new Reporter(context);
                    var succeeded = false;
                    try
                    {
                        var result = action(reporter);
                        succeeded = result == 0;
                        return result;
                    }
                    finally { reporter.Stop(succeeded); finished = true; }
                });
            return exitCode;
        }

        internal static IRenderable Dashboard(IAnsiConsole console, IRenderable metrics, IRenderable progress, string phase = null)
        {
            var title = "Changesets" + (phase == null ? string.Empty : " · " + phase);
            var changesets = new Table().RoundedBorder().Title("[bold blue]" + title + "[/]")
                .HideHeaders().AddColumn("Progress").AddRow(progress);
            if (console.Profile.Width < 80)
                return new Rows(metrics, changesets);
            return new Grid().AddColumn().AddColumn().AddRow(metrics, changesets);
        }

        private sealed class TaskStatusColumn(bool unicode) : ProgressColumn
        {
            private readonly SpinnerColumn spinner = new(unicode ? Spinner.Known.Dots : Spinner.Known.Line)
            {
                Style = new Style(Color.Cyan), CompletedText = unicode ? "✓" : "v"
            };
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
                => task.IsFinished && task.Value < task.MaxValue
                    ? new Text(unicode ? "×" : "x", new Style(Color.Red))
                    : spinner.Render(options, task, deltaTime);
        }

        private sealed class FilePercentageColumn : ProgressColumn
        {
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
                => new Text(task.IsIndeterminate ? "..." : $"{task.Percentage:0}%");
        }

        private sealed class StaticReporter(IAnsiConsole console) : IChangesetProgressReporter
        {
            private readonly Dictionary<int, int> percentages = new();

            public void ReportScan(int page, int found, int cursor, RestChangesetReference latest = null)
            {
                if (page == 1 && latest == null)
                    console.MarkupLine("[cyan]Scanning TFVC[/]");
            }

            public void StartChangeset(int changesetId, int totalFiles)
            {
                percentages[changesetId] = 0;
                console.MarkupLine("[blue]C{0}[/] · 0%", changesetId);
            }

            public void ReportFiles(int changesetId, int processedFiles, int totalFiles)
            {
                var percentage = totalFiles > 0 ? (int)Math.Clamp(100.0 * processedFiles / totalFiles, 0, 99) : 0;
                if (percentages.TryGetValue(changesetId, out var previous) && previous == percentage) return;
                percentages[changesetId] = percentage;
                console.MarkupLine("[blue]C{0}[/] · {1}%", changesetId, percentage);
            }

            public void CompleteChangeset(int changesetId, string commitSha)
            {
                percentages.Remove(changesetId);
                console.MarkupLine("[green]C{0}[/] · 100%", changesetId);
            }

            public void SkipChangeset(int changesetId) => CompleteChangeset(changesetId, null);
        }

        private sealed class Reporter : IChangesetProgressReporter
        {
            private readonly ProgressContext context;
            private readonly ProgressTask overall;
            private readonly Dictionary<int, ProgressTask> entries = new();
            private readonly Queue<ProgressTask> recent = new();
            private int completed;
            public string Phase { get; private set; } = "Scanning";

            public Reporter(ProgressContext context)
            {
                this.context = context;
                overall = context.AddTask("[cyan]Overall[/]", maxValue: 1);
                overall.IsIndeterminate = true;
            }

            public void CompleteScan(int found)
            {
                Phase = "Importing";
                overall.MaxValue = Math.Max(found, 1);
                overall.IsIndeterminate = false;
                overall.Value = found == 0 ? 1 : completed;
            }

            public void StartChangeset(int changesetId, int totalFiles)
            {
                Phase = "Importing";
                var task = context.AddTask($"[bold blue]C{changesetId}[/]", maxValue: Math.Max(totalFiles, 1));
                entries[changesetId] = task;
                recent.Enqueue(task);
                if (recent.Count > 10)
                    recent.Dequeue().HideWhenCompleted = true;
            }

            public void ReportFiles(int changesetId, int processedFiles, int totalFiles)
            {
                if (!entries.TryGetValue(changesetId, out var task)) return;
                task.Value = Math.Min(processedFiles, task.MaxValue * .99);
                UpdateOverall();
            }

            public void CompleteChangeset(int changesetId, string commitSha)
            {
                if (!entries.Remove(changesetId, out var task)) return;
                task.Value = task.MaxValue;
                task.StopTask();
                completed++;
                UpdateOverall();
            }

            public void SkipChangeset(int changesetId)
            {
                StartChangeset(changesetId, 1);
                CompleteChangeset(changesetId, null);
            }

            private void UpdateOverall()
            {
                if (!overall.IsIndeterminate)
                    overall.Value = Math.Min(overall.MaxValue,
                        completed + entries.Values.Sum(task => task.Percentage / 100));
            }

            public void ReportActivity(string description)
            {
                if (description.StartsWith("Verifying", StringComparison.Ordinal)) Phase = "Verifying";
                else if (description.StartsWith("Writing Git", StringComparison.Ordinal)) Phase = "Writing files";
                else if (description.StartsWith("Running Git", StringComparison.Ordinal)) Phase = "Git cleanup";
                else if (description.StartsWith("Merging and pushing", StringComparison.Ordinal)) Phase = "Syncing";
            }

            public void Stop(bool succeeded)
            {
                Phase = succeeded ? "Complete" : "Failed";
                if (overall.IsIndeterminate)
                {
                    overall.MaxValue = Math.Max(completed + entries.Count, 1);
                    overall.IsIndeterminate = false;
                    UpdateOverall();
                }
                overall.StopTask();
                foreach (var task in entries.Values) task.StopTask();
            }
        }
    }
}
