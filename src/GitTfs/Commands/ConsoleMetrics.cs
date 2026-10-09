namespace GitTfs.Commands
{
    using System.Diagnostics.Metrics;
    using System.Globalization;
    using GitTfs.Core;
    using Spectre.Console;

    internal sealed class ConsoleMetrics : IDisposable
    {
        private readonly MeterListener listener;
        private readonly TimeProvider clock;
        private readonly object gate = new();
        private Table liveTable;
        private long lastLiveRefresh;
        private readonly Dictionary<string, long> counters = new();
        private readonly Dictionary<string, (int Samples, double Milliseconds)> timings = new();
        private static readonly string[] RequestRows =
            ["History requests", "Changeset requests", "File downloads", "Metadata requests"];

        public ConsoleMetrics(TimeProvider clock = null)
        {
            this.clock = clock ?? TimeProvider.System;
            listener = new MeterListener
            {
                InstrumentPublished = (instrument, receiver) =>
                {
                    if (instrument.Meter.Name == GitTfsMetrics.MeterName)
                        receiver.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var name = instrument.Name == "gittfs.requests" ? Operation(tags) : instrument.Name;
                lock (gate) counters[name] = counters.GetValueOrDefault(name) + value;
            });
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                if (instrument.Name != "gittfs.request.duration" && instrument.Name != "gittfs.changeset.duration")
                    return;
                var name = instrument.Name == "gittfs.request.duration" ? Operation(tags) : "Changesets timed";
                var milliseconds = instrument.Unit == "s" ? value * 1000 : value;
                lock (gate)
                {
                    var previous = timings.GetValueOrDefault(name);
                    timings[name] = (previous.Samples + 1, previous.Milliseconds + milliseconds);
                }
            });
            listener.Start();
        }

        private static string Operation(ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            foreach (var tag in tags)
                if (tag.Key == "operation") return tag.Value?.ToString() ?? "Metadata requests";
            return "Metadata requests";
        }

        internal IReadOnlyList<(string Name, long Count, double? AverageMilliseconds)> Snapshot()
        {
            lock (gate)
            {
                var rows = RequestRows.Select(name =>
                {
                    var timing = timings.GetValueOrDefault(name);
                    return (name, counters.GetValueOrDefault(name), timing.Samples == 0
                        ? (double?)null : timing.Milliseconds / timing.Samples);
                }).ToList();
                var imports = timings.GetValueOrDefault("Changesets timed");
                rows.Add(("Changesets timed", imports.Samples, imports.Samples == 0
                    ? null : imports.Milliseconds / imports.Samples));
                foreach (var (label, counter) in new[] {
                    ("Changesets imported", "gittfs.changesets.imported"),
                    ("Changesets skipped", "gittfs.changesets.skipped"),
                    ("Files processed", "gittfs.files.processed"),
                    ("Files downloaded", "gittfs.files.downloaded"),
                    ("Files reused", "gittfs.files.reused"),
                    ("Files deleted", "gittfs.files.deleted") })
                    rows.Add((label, counters.GetValueOrDefault(counter), null));
                return rows;
            }
        }

        public Table RenderLive()
        {
            lock (gate)
            {
                var now = clock.GetTimestamp();
                if (liveTable == null || clock.GetElapsedTime(lastLiveRefresh, now) >= TimeSpan.FromSeconds(1))
                {
                    liveTable = Render();
                    lastLiveRefresh = now;
                }
                return liveTable;
            }
        }

        public Table Render()
        {
            var table = new Table().RoundedBorder().Title("[bold cyan]Live metrics[/]")
                .AddColumn("Metric").AddColumn(new TableColumn("Count").RightAligned())
                .AddColumn(new TableColumn("Avg ms").RightAligned());
            foreach (var row in Snapshot())
                table.AddRow(row.Name, row.Count.ToString("N0", CultureInfo.InvariantCulture),
                    row.AverageMilliseconds?.ToString("N1", CultureInfo.InvariantCulture) ?? "—");
            return table;
        }

        public void Dispose() => listener.Dispose();
    }
}
