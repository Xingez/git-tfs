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
                var reporter = new StaticReporter(console);
                var result = action(reporter);
                console.MarkupLine("[green]Import complete[/] · {0} changesets imported", reporter.Imported);
                console.Write(metrics.Render());
                return result;
            }

            Reporter liveReporter = null;
            var display = console.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(true)
                .Columns(
                    new SpinnerColumn(console.Profile.Capabilities.Unicode ? Spinner.Known.Dots : Spinner.Known.Line)
                    {
                        Style = new Style(Color.Cyan), CompletedText = "✓"
                    },
                    new TaskDescriptionColumn { Wrap = true },
                    new ProgressBarColumn
                    {
                        Width = 20,
                        CompletedStyle = new Style(Color.Blue),
                        FinishedStyle = new Style(Color.Green),
                        RemainingStyle = new Style(Color.Grey),
                        IndeterminateStyle = new Style(Color.Cyan)
                    },
                    new FilePercentageColumn(),
                    new ElapsedTimeColumn());
            display.RenderHook = (progress, _) => new Rows(progress, metrics.Render());
            var exitCode = display.Start(context =>
                {
                    liveReporter = new Reporter(context);
                    try { return action(liveReporter); }
                    finally { liveReporter.Stop(); }
                });
            console.MarkupLine("[green]Import complete[/] · {0} changesets imported", liveReporter.Imported);
            console.Write(metrics.Render());
            return exitCode;
        }

        private static string Label(string text)
        {
            var clean = new string((text ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
            return Markup.Escape(clean.Length > 72 ? clean[..69] + "..." : clean);
        }

        private sealed class FilePercentageColumn : ProgressColumn
        {
            public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
                => new Text(task.IsIndeterminate ? "..." : $"{task.Percentage:0}%");
        }

        private sealed class StaticReporter(IAnsiConsole console) : IChangesetProgressReporter
        {
            private int describedId;
            private string comment;
            public int Imported { get; private set; }

            public void ReportScan(int page, int found, int cursor, RestChangesetReference latest = null)
            {
                if (latest == null)
                    console.MarkupLine("[cyan]Scanning TFVC[/] · page {0} · {1} found · after C{2}", page, found, cursor);
                else
                    console.MarkupLine("[cyan]Found C{0}[/] · {1}", latest.ChangesetId, Label(latest.Comment));
            }

            public void CompleteScan(int found)
                => console.MarkupLine("[green]TFVC changeset scan complete[/] · {0} found", found);

            public void DescribeChangeset(int changesetId, string comment)
            {
                describedId = changesetId;
                this.comment = Label(comment);
            }

            public void ReportActivity(string description)
                => console.MarkupLine("[grey]{0}[/]", Label(description));

            public void StartChangeset(int changesetId, int totalFiles)
            {
                console.MarkupLine("[yellow]Importing C{0}[/] · {1} · {2} files", changesetId,
                    describedId == changesetId ? comment : string.Empty, totalFiles);
            }

            public void ReportFiles(int changesetId, int processedFiles, int totalFiles) { }

            public void CompleteChangeset(int changesetId, string commitSha)
            {
                console.MarkupLine("[green]C{0} complete[/] · {1} · 100%", changesetId,
                    Label(commitSha?[..Math.Min(7, commitSha.Length)]));
                Imported++;
            }
        }

        private sealed class Reporter : IChangesetProgressReporter
        {
            private readonly ProgressContext context;
            private readonly ProgressTask scan;
            private readonly ProgressTask activity;
            private readonly Dictionary<int, (ProgressTask Task, string Name)> entries = new();
            private int describedId;
            private string comment;
            private string latestName = string.Empty;
            public int Imported { get; private set; }

            public Reporter(ProgressContext context)
            {
                this.context = context;
                scan = context.AddTask("[cyan]Connecting to TFVC[/]", maxValue: 1);
                scan.IsIndeterminate = true;
                activity = context.AddTask("[yellow]Waiting for TFVC[/]", maxValue: 1);
                activity.IsIndeterminate = true;
            }

            public void ReportScan(int page, int found, int cursor, RestChangesetReference latest = null)
            {
                if (latest != null)
                    latestName = $" · C{latest.ChangesetId} {Label(latest.Comment)}";
                scan.Description = $"[cyan]Scan[/] · page {page} · [bold]{found} found[/]"
                    + latestName;
                if (latest == null)
                    ReportActivity($"Waiting for TFVC history page {page} after C{cursor}");
            }

            public void CompleteScan(int found)
            {
                scan.Description = $"[green]TFVC changeset scan complete[/] · {found} found";
                scan.IsIndeterminate = false;
                scan.Value = scan.MaxValue;
                scan.StopTask();
            }

            public void DescribeChangeset(int changesetId, string description)
            {
                describedId = changesetId;
                comment = Label(description);
            }

            public void ReportActivity(string description)
                => activity.Description = "[yellow]" + Label(description) + "[/]";

            public void StartChangeset(int changesetId, int totalFiles)
            {
                var name = $"[bold blue]C{changesetId}[/]"
                    + (describedId == changesetId && !string.IsNullOrEmpty(comment) ? " · " + comment : string.Empty);
                var task = context.AddTask(name + $" · 0/{totalFiles} files", maxValue: Math.Max(totalFiles, 1));
                entries[changesetId] = (task, name);
                ReportActivity($"Importing C{changesetId} · {Imported} committed so far");
            }

            public void ReportFiles(int changesetId, int processedFiles, int totalFiles)
            {
                if (!entries.TryGetValue(changesetId, out var entry)) return;
                entry.Task.Value = Math.Min(processedFiles, entry.Task.MaxValue);
                entry.Task.Description = entry.Name + $" · {processedFiles}/{totalFiles} files";
            }

            public void CompleteChangeset(int changesetId, string commitSha)
            {
                if (!entries.Remove(changesetId, out var entry)) return;
                entry.Task.Value = entry.Task.MaxValue;
                entry.Task.StopTask();
                Imported++;
                ReportActivity($"Committed C{changesetId} · {Imported} imported");
            }

            public void Stop()
            {
                scan.StopTask();
                activity.StopTask();
                foreach (var entry in entries.Values) entry.Task.StopTask();
            }
        }
    }
}
