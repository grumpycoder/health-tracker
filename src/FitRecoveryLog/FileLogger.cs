using Microsoft.Extensions.Logging;

namespace FitRecoveryLog;

/// <summary>
/// Dev-only logger that appends Error/Critical log entries (including Blazor's
/// swallowed component render exceptions) to a file we can inspect.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    public FileLoggerProvider(string path) => _path = path;
    public ILogger CreateLogger(string categoryName) => new FileLogger(_path, categoryName);
    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly string _path;
        private readonly string _category;
        public FileLogger(string path, string category) { _path = path; _category = category; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"[{DateTime.Now:HH:mm:ss}] {logLevel} {_category}: " +
                       $"{formatter(state, exception)}\n{exception}\n\n";
            try { File.AppendAllText(_path, line); } catch { /* best-effort */ }
        }
    }
}
