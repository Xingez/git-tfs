namespace GitTfs
{
    using global::System.Diagnostics;
    using global::Microsoft.Extensions.Logging;

    /// <summary>
    /// Keeps legacy Trace-based command messages on the Microsoft logging pipeline.
    /// </summary>
    public sealed class MicrosoftLoggingTraceListener : TraceListener
    {
        private readonly ILogger loggerField;

        public MicrosoftLoggingTraceListener(ILogger logger)
        {
            loggerField = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override void Write(string message) => Log(LogLevel.Information, 0, message);

        public override void WriteLine(string message) => Log(LogLevel.Information, 0, message);

        public override void TraceEvent(
            TraceEventCache eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string message) => Log(ToLogLevel(eventType), id, message, source);

        public override void TraceEvent(
            TraceEventCache eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string format,
            params object[] args)
        {
            var message = args == null || args.Length == 0
                ? format
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args);
            Log(ToLogLevel(eventType), id, message, source);
        }

        private void Log(LogLevel level, int id, string message, string category = null)
        {
            loggerField.Log(level, new EventId(id, category), null, message ?? string.Empty,
                Array.Empty<object>());
        }

        private static LogLevel ToLogLevel(TraceEventType eventType) => eventType switch
        {
            TraceEventType.Critical => LogLevel.Critical,
            TraceEventType.Error => LogLevel.Error,
            TraceEventType.Warning => LogLevel.Warning,
            TraceEventType.Verbose => LogLevel.Debug,
            _ => LogLevel.Information,
        };
    }
}
