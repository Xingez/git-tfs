
namespace GitTfs
{
    using global::System.Diagnostics;
    using global::System.Globalization;
    using global::Serilog;
    using global::Serilog.Events;
    /// <summary>
    /// Keeps the existing Trace-based logging calls backed by Serilog.
    /// </summary>
    public sealed class SerilogTraceListener : TraceListener
    {
        public override void Write(string message) => Write(LogEventLevel.Information, message);

        public override void WriteLine(string message) => Write(LogEventLevel.Information, message);

        public override void TraceEvent(
            TraceEventCache eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string message) => Write(ToLogEventLevel(eventType), message);

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
                : string.Format(CultureInfo.CurrentCulture, format, args);
            Write(ToLogEventLevel(eventType), message);
        }

        private static void Write(LogEventLevel level, string message) =>
            Log.Logger.Write(level, "{Message:l}", message ?? string.Empty);

        private static LogEventLevel ToLogEventLevel(TraceEventType eventType) => eventType switch
        {
            TraceEventType.Critical => LogEventLevel.Fatal,
            TraceEventType.Error => LogEventLevel.Error,
            TraceEventType.Warning => LogEventLevel.Warning,
            TraceEventType.Verbose => LogEventLevel.Verbose,
            _ => LogEventLevel.Information,
        };
    }
}
