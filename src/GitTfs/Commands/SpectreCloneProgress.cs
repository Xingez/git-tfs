namespace GitTfs.Commands
{
    using System;
    using System.Collections.Generic;
    using GitTfs.Core.RestTfs;
    using Spectre.Console;

    internal static class SpectreCloneProgress
    {
        public static int Run(Func<IChangesetProgressReporter, int> action)
        {
            return AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn
                    {
                        CompletedStyle = new Style(Color.Blue),
                        FinishedStyle = new Style(Color.Green),
                        RemainingStyle = new Style(Color.Grey),
                    },
                    new PercentageColumn())
                .Start(context =>
                {
                    var reporter = new Reporter(context);
                    try
                    {
                        return action(reporter);
                    }
                    finally
                    {
                        reporter.CompleteScan();
                    }
                });
        }

        private sealed class Reporter : IChangesetProgressReporter
        {
            private readonly ProgressContext contextField;
            private readonly Dictionary<int, ProgressEntry> entriesField = new();
            private readonly ProgressTask scanTaskField;

            public Reporter(ProgressContext context)
            {
                contextField = context;
                scanTaskField = context.AddTask("Scanning TFVC changesets", maxValue: 1);
                scanTaskField.IsIndeterminate = true;
            }

            public void CompleteScan()
            {
                scanTaskField.IsIndeterminate = false;
                scanTaskField.Value = scanTaskField.MaxValue;
                scanTaskField.Description = "TFVC changeset scan complete";
            }

            public void StartChangeset(int changesetId, int totalFiles)
            {
                var maximum = Math.Max(totalFiles, 1);
                var task = contextField.AddTask("C" + changesetId, maxValue: maximum);
                entriesField[changesetId] = new ProgressEntry(task, maximum);
                task.Description = totalFiles == 0
                    ? "C" + changesetId + " (commit)"
                    : "C" + changesetId + " (0/" + totalFiles + " files)";
            }

            public void ReportFiles(int changesetId, int processedFiles, int totalFiles)
            {
                if (!entriesField.TryGetValue(changesetId, out var entry))
                    return;

                entry.Task.Value = Math.Min(processedFiles, entry.Maximum);
                entry.Task.Description = "C" + changesetId + " (" + processedFiles + "/" + totalFiles + " files)";
            }

            public void CompleteChangeset(int changesetId, string commitSha)
            {
                if (!entriesField.TryGetValue(changesetId, out var entry))
                    return;

                entry.Task.Value = entry.Maximum;
                var shortSha = string.IsNullOrWhiteSpace(commitSha)
                    ? string.Empty
                    : " " + commitSha.Substring(0, Math.Min(7, commitSha.Length));
                entry.Task.Description = "C" + changesetId + shortSha;
            }

            private sealed class ProgressEntry
            {
                public ProgressEntry(ProgressTask task, double maximum)
                {
                    Task = task;
                    Maximum = maximum;
                }

                public ProgressTask Task { get; }
                public double Maximum { get; }
            }
        }
    }
}
