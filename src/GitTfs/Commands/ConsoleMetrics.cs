namespace GitTfs.Commands
{
    using System.Diagnostics.Metrics;
    using System.Globalization;
    using GitTfs.Core;
    using Spectre.Console;
    using Spectre.Console.Rendering;

    internal sealed class ConsoleMetrics : IDisposable
    {
        private readonly MeterListener listener;
        private readonly TimeProvider clock;
        private readonly object gate = new();
        private Table liveTable;
        private long lastLiveRefresh;
        private int? lastStatus;
        private double? rateRemaining, rateLimit, suggestedDelay, serverDelay;
        private string delaySource;
        private double? waitSeconds;
        private long waitStarted;
        private bool waitThrottled;
        private int retryAttempt, maxAttempts;
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
                if (instrument.Name == "gittfs.responses") { ReadResponse(tags); return; }
                if (instrument.Name == "gittfs.requests.waiting") { ReadWait(value, tags); return; }
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

        private static object Tag(ReadOnlySpan<KeyValuePair<string, object>> tags, string name)
        {
            foreach (var tag in tags)
                if (tag.Key == name) return tag.Value;
            return null;
        }

        private static double? Number(object value)
            => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number) && number >= 0 ? number : null;

        private void ReadResponse(ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            lock (gate)
            {
                lastStatus = Tag(tags, "status") as int?;
                rateRemaining = Number(Tag(tags, "remaining")) ?? rateRemaining;
                rateLimit = Number(Tag(tags, "limit")) ?? rateLimit;
                serverDelay = Number(Tag(tags, "server_delay")) ?? serverDelay;
                if (Tag(tags, "delay_source") is string source)
                {
                    delaySource = source;
                    suggestedDelay = Number(Tag(tags, "delay_seconds"));
                }
            }
        }

        private void ReadWait(long value, ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            lock (gate)
            {
                waitSeconds = value > 0 ? Number(Tag(tags, "seconds")) : null;
                waitStarted = clock.GetTimestamp();
                waitThrottled = Tag(tags, "throttled") is true;
                retryAttempt = Tag(tags, "attempt") as int? ?? 0;
                maxAttempts = Tag(tags, "max_attempts") as int? ?? 0;
            }
        }

        internal ApiStatus ApiSnapshot()
        {
            lock (gate)
                return new ApiStatus(lastStatus, waitSeconds.HasValue, waitThrottled,
                    waitSeconds.HasValue ? Math.Max(0, waitSeconds.Value - clock.GetElapsedTime(waitStarted).TotalSeconds) : 0,
                    retryAttempt, maxAttempts, rateRemaining, rateLimit, delaySource, suggestedDelay, serverDelay);
        }

        internal sealed record ApiStatus(int? StatusCode, bool Waiting, bool Throttled, double RemainingSeconds,
            int RetryAttempt, int MaxAttempts, double? RateRemaining, double? RateLimit,
            string DelaySource, double? SuggestedDelaySeconds, double? ServerDelaySeconds);

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
                rows.Add(("Retries", counters.GetValueOrDefault("gittfs.requests.retries"), null));
                rows.Add(("Throttles", counters.GetValueOrDefault("gittfs.responses.throttled"), null));
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
            var api = ApiSnapshot();
            var table = new Table().RoundedBorder()
                .AddColumn("Metric").AddColumn(new TableColumn("Count").RightAligned())
                .AddColumn(new TableColumn("Avg ms").RightAligned());
            foreach (var row in Snapshot())
            {
                table.AddRow(row.Name, row.Count.ToString("N0", CultureInfo.InvariantCulture),
                    row.AverageMilliseconds?.ToString("N1", CultureInfo.InvariantCulture) ?? "—");
                if (row.Name == "Throttles")
                {
                    table.AddRow("Rate limit (TSTU)", Format(api.RateLimit), "—");
                    table.AddRow("Rate remaining (TSTU)", Format(api.RateRemaining), "—");
                }
            }
            return table;
        }

        public IRenderable RenderDisplay(bool live, bool refresh = false)
        {
            if (refresh) lock (gate) liveTable = null;
            var api = ApiSnapshot();
            var state = api.Waiting
                ? (api.Throttled ? "API throttled" : "API retry")
                    + (api.RetryAttempt > 0 ? $" · attempt {api.RetryAttempt}/{api.MaxAttempts}" : string.Empty)
                    + (api.RemainingSeconds > 0 ? $" · {Math.Ceiling(api.RemainingSeconds):0}s" : " · resuming")
                : api.StatusCode >= 400 ? $"API error · HTTP {api.StatusCode}" : "API ready";
            var style = api.Waiting ? "yellow" : api.StatusCode >= 400 ? "red" : "green";
            var rows = new List<IRenderable>
            {
                live ? RenderLive() : Render(),
                new Markup($"[{style}]{state}[/]")
            };
            if (api.SuggestedDelaySeconds.HasValue)
                rows.Add(new Text(api.DelaySource switch
                {
                    "X-MS-Retry-After-MS" => $"Last X-MS-Retry-After-MS: {Format(api.SuggestedDelaySeconds * 1000)}ms",
                    "X-RateLimit-Reset" => $"Last reset backoff: {Format(api.SuggestedDelaySeconds)}s",
                    _ => $"Last {api.DelaySource}: {Format(api.SuggestedDelaySeconds)}s"
                }));
            if (api.ServerDelaySeconds.HasValue && api.DelaySource != "X-RateLimit-Delay")
                rows.Add(new Text($"Server delay: {Format(api.ServerDelaySeconds)}s"));
            return new Rows(rows);
        }

        private static string Format(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—";

        public void Dispose() => listener.Dispose();
    }
}
