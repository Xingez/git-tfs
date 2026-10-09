namespace GitTfs.Commands
{
    using GitTfs.Core.RestTfs;
    using Spectre.Console;
    using Spectre.Console.Rendering;

    internal static class SpectreCloneProgress
    {
        private const int VisibleChangesets = 16;
        private const int VisibleCompletedChangesets = VisibleChangesets / 2;

        public static int Run(Func<IChangesetProgressReporter, int> action, string workingDirectory = null)
        {
            var console = AnsiConsole.Console;
            using var metrics = new ConsoleMetrics();
            var folder = workingDirectory == null ? null : new WorkingDirectoryTree(workingDirectory);
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

            var progressBar = new ProgressBarColumn
            {
                CompletedStyle = new Style(Color.Blue),
                FinishedStyle = new Style(Color.Green),
                RemainingStyle = new Style(Color.Grey),
                IndeterminateStyle = new Style(Color.Cyan)
            };
            var display = console.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskStatusColumn(console.Profile.Capabilities.Unicode),
                    new TaskDescriptionColumn { Wrap = true },
                    progressBar,
                    new FilePercentageColumn(),
                    new SecondsColumn());
            display.RefreshRate = TimeSpan.FromMilliseconds(250);
            var finished = false;
            Reporter reporter = null;
            string lastPhase = null;
            display.RenderHook = (progress, _) =>
            {
                progressBar.Width = Math.Max(14, (folder != null && console.Profile.Width >= 80
                    ? console.Profile.Width - FolderWidth(console.Profile.Width) - 2
                    : console.Profile.Width) - 28);
                var phase = reporter?.Phase ?? "Scanning";
                var refresh = phase != lastPhase;
                lastPhase = phase;
                return Dashboard(console, metrics.RenderDisplay(live: !finished, refresh), progress, phase,
                    folder?.Render(finished, maxNodes: Math.Clamp(console.Profile.Height - 6, 1, 20),
                        maxDepth: Math.Max(1, ((console.Profile.Width >= 80 ? FolderWidth(console.Profile.Width) : console.Profile.Width) / 2 - 8) / 4)));
            };
            var exitCode = display.Start(context =>
                {
                    reporter = new Reporter(context, folder);
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

        internal static IRenderable Dashboard(IAnsiConsole console, IRenderable metrics, IRenderable progress,
            string phase = null, IRenderable folder = null)
        {
            var title = "Changesets" + (phase == null ? string.Empty : " · " + phase);
            var changesets = new Table().RoundedBorder().Title("[bold blue]" + title + "[/]")
                .Expand().HideHeaders().AddColumn("Progress").AddRow(progress);
            IRenderable dashboard = folder == null ? changesets
                : console.Profile.Width >= 80
                    ? new Grid().AddColumn(new GridColumn().Width(console.Profile.Width - FolderWidth(console.Profile.Width) - 2))
                        .AddColumn(new GridColumn().Width(FolderWidth(console.Profile.Width))).AddRow(changesets, folder)
                    : new Rows(changesets, folder);
            return new Rows(dashboard, metrics);
        }

        private static int FolderWidth(int width) => Math.Max(width / 3, 24);

        private sealed class TaskStatusColumn(bool unicode) : ProgressColumn
        {
            private readonly SpinnerColumn spinner = new(unicode ? Spinner.Known.Dots : Spinner.Known.Line)
            {
                Style = new Style(Color.Cyan), CompletedText = unicode ? "✓" : "v"
            };
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
                => task.StartTime == null ? new Text(" ") : task.IsFinished && task.Value < task.MaxValue
                    ? new Text(unicode ? "×" : "x", new Style(Color.Red))
                    : spinner.Render(options, task, deltaTime);
        }

        private sealed class SecondsColumn : ProgressColumn
        {
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
                => new Text($"{Math.Floor(task.ElapsedTime?.TotalSeconds ?? 0):0}s", new Style(Color.Grey));
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
            private readonly WorkingDirectoryTree folder;
            private readonly ProgressTask overall;
            private readonly Dictionary<int, ProgressTask> entries = new();
            private readonly Queue<ProgressTask> completedTasks = new();
            private readonly Queue<int> upcoming = new();
            private int completed;
            public string Phase { get; private set; } = "Scanning";

            public Reporter(ProgressContext context, WorkingDirectoryTree folder)
            {
                this.context = context;
                this.folder = folder;
                overall = context.AddTask("[cyan]Overall[/]", maxValue: 1);
                overall.IsIndeterminate = true;
            }

            public void CompleteScan(int found)
            {
                Phase = "Importing";
                overall.MaxValue = Math.Max(completed + found, 1);
                overall.IsIndeterminate = false;
                overall.Value = completed + found == 0 ? 1 : completed;
            }

            public void ReportResume(IReadOnlyList<int> completedChangesets)
            {
                completed = completedChangesets.Count;
                foreach (var id in completedChangesets.TakeLast(VisibleCompletedChangesets))
                {
                    var task = AddChangeset(id);
                    task.StartTask();
                    task.Value = task.MaxValue;
                    task.StopTask();
                    entries.Remove(id);
                    completedTasks.Enqueue(task);
                }
            }

            public void ReportScan(int page, int found, int cursor, RestChangesetReference latest = null)
            {
                if (latest == null) return;
                upcoming.Enqueue(latest.ChangesetId);
                FillUpcoming();
            }

            private ProgressTask AddChangeset(int changesetId)
            {
                var task = context.AddTask($"[bold blue]C{changesetId}[/]", autoStart: false, maxValue: 1);
                entries[changesetId] = task;
                return task;
            }

            private void FillUpcoming()
            {
                while (upcoming.Count > 0 && entries.Count < VisibleChangesets - VisibleCompletedChangesets)
                    AddChangeset(upcoming.Dequeue());
            }

            public void DescribeChangeset(int changesetId, string comment)
            {
                Phase = "Importing";
                if (!entries.TryGetValue(changesetId, out var task)) task = AddChangeset(changesetId);
                task.StartTask();
            }

            public void StartChangeset(int changesetId, int totalFiles)
            {
                Phase = "Importing";
                if (!entries.TryGetValue(changesetId, out var task)) task = AddChangeset(changesetId);
                task.MaxValue = Math.Max(totalFiles, 1);
                task.StartTask();
            }

            public void ReportFiles(int changesetId, int processedFiles, int totalFiles)
            {
                if (!entries.TryGetValue(changesetId, out var task)) return;
                task.Value = Math.Min(processedFiles, task.MaxValue * .99);
                UpdateOverall();
            }

            public void ReportDownloadedFile(string relativePath) => folder?.ReportDownloadedFile(relativePath);

            public void CompleteChangeset(int changesetId, string commitSha)
            {
                if (!entries.Remove(changesetId, out var task)) return;
                task.Value = task.MaxValue;
                task.StopTask();
                completedTasks.Enqueue(task);
                if (completedTasks.Count > VisibleCompletedChangesets)
                    completedTasks.Dequeue().HideWhenCompleted = true;
                completed++;
                UpdateOverall();
                FillUpcoming();
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
