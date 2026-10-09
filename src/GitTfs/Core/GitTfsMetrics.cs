namespace GitTfs.Core
{
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Globalization;

    internal static class GitTfsMetrics
    {
        public const string MeterName = "GitTfs";

        private static readonly Meter meterField = new(MeterName, "1.0.0");
        private static readonly Counter<long> requestCountField = meterField.CreateCounter<long>(
            "gittfs.requests", "request", "HTTP attempts, including retries.");
        private static readonly Histogram<double> requestDurationField = meterField.CreateHistogram<double>(
            "gittfs.request.duration", "ms", "HTTP attempt duration, excluding retry backoff.");
        private static readonly Counter<long> responsesField = meterField.CreateCounter<long>("gittfs.responses");
        private static readonly Counter<long> retriesField = meterField.CreateCounter<long>("gittfs.requests.retries");
        private static readonly Counter<long> throttlesField = meterField.CreateCounter<long>("gittfs.responses.throttled");
        private static readonly UpDownCounter<long> waitingField = meterField.CreateUpDownCounter<long>("gittfs.requests.waiting");

        public static bool RecordResponse(AzureDevOpsRateLimit rateLimit)
        {
            var delay = rateLimit.GetServerDelay(DateTimeOffset.UtcNow, out var source);
            var tags = new TagList { { "status", rateLimit.StatusCode } };
            if (rateLimit.Remaining != null) tags.Add("remaining", rateLimit.Remaining);
            if (rateLimit.Limit != null) tags.Add("limit", rateLimit.Limit);
            if (source != null && (source != "X-RateLimit-Reset" || rateLimit.IsThrottled))
            {
                tags.Add("delay_source", source);
                tags.Add("delay_seconds", delay.Value.TotalSeconds);
            }
            if (rateLimit.Delay != null) tags.Add("server_delay", rateLimit.Delay);
            responsesField.Add(1, tags);
            var throttled = rateLimit.StatusCode == 429 || rateLimit.RetryAfter != null
                || rateLimit.MsRetryAfter != null || rateLimit.XAfter != null
                || double.TryParse(rateLimit.Delay, NumberStyles.Float, CultureInfo.InvariantCulture, out var serverDelay) && serverDelay > 0;
            if (throttled) throttlesField.Add(1);
            return throttled;
        }

        public static IDisposable MeasureWait(TimeSpan delay, string source, bool throttled,
            int retryAttempt = 0, int maxAttempts = 0)
        {
            if (retryAttempt > 0) retriesField.Add(1);
            return new WaitMeasurement(new TagList
            {
                { "seconds", Math.Max(0, delay.TotalSeconds) }, { "source", source },
                { "throttled", throttled }, { "attempt", retryAttempt }, { "max_attempts", maxAttempts }
            });
        }

        private sealed class WaitMeasurement : IDisposable
        {
            private readonly TagList tags;
            private int completed;
            public WaitMeasurement(TagList tags) { this.tags = tags; waitingField.Add(1, tags); }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref completed, 1) == 0) waitingField.Add(-1, tags);
            }
        }

        public static RequestMeasurement MeasureRequest(string operation)
        {
            requestCountField.Add(1, new KeyValuePair<string, object>("operation", operation));
            return new RequestMeasurement(operation);
        }

        public sealed class RequestMeasurement(string operation) : IDisposable
        {
            private readonly long started = Stopwatch.GetTimestamp();
            private int completed;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                requestDurationField.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    new KeyValuePair<string, object>("operation", operation));
            }
        }
        private static readonly Counter<long> importedChangesetsField = meterField.CreateCounter<long>(
            "gittfs.changesets.imported", "changeset", "Changesets committed to the Git repository.");
        private static readonly Counter<long> skippedChangesetsField = meterField.CreateCounter<long>(
            "gittfs.changesets.skipped", "changeset", "Changesets with no relevant repository changes.");
        private static readonly Histogram<double> changesetDurationField = meterField.CreateHistogram<double>(
            "gittfs.changeset.duration", "s", "Time spent importing a TFVC changeset.");
        private static readonly Counter<long> processedFilesField = meterField.CreateCounter<long>(
            "gittfs.files.processed", "file", "Changed files applied to the Git tree.");
        private static readonly Counter<long> downloadedFilesField = meterField.CreateCounter<long>(
            "gittfs.files.downloaded", "file", "Files downloaded from TFVC.");
        private static readonly Counter<long> reusedFilesField = meterField.CreateCounter<long>(
            "gittfs.files.reused", "file", "Files reused after their local hash matched TFVC.");
        private static readonly Counter<long> deletedFilesField = meterField.CreateCounter<long>(
            "gittfs.files.deleted", "file", "Files removed from the Git tree.");
        private static readonly Counter<long> downloadedBytesField = meterField.CreateCounter<long>(
            "gittfs.file.bytes.downloaded", "By", "File content bytes downloaded from TFVC.");

        public static ChangesetImportMeasurement MeasureChangesetImport()
            => new(Stopwatch.GetTimestamp());

        public static void RecordChangesetSkipped()
            => skippedChangesetsField.Add(1);

        public static void RecordChangesetImported(int filesProcessed, int filesDownloaded,
            int filesReused, int filesDeleted, long bytesDownloaded)
        {
            importedChangesetsField.Add(1);
            processedFilesField.Add(filesProcessed);
            downloadedFilesField.Add(filesDownloaded);
            reusedFilesField.Add(filesReused);
            deletedFilesField.Add(filesDeleted);
            downloadedBytesField.Add(bytesDownloaded);
        }

        public readonly struct ChangesetImportMeasurement : IDisposable
        {
            private readonly long startTimestampField;

            internal ChangesetImportMeasurement(long startTimestamp)
            {
                startTimestampField = startTimestamp;
            }

            public void Dispose()
                => changesetDurationField.Record(Stopwatch.GetElapsedTime(startTimestampField).TotalSeconds);
        }
    }
}
