namespace PowerProbe.Core;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <param name="Source">Short area name, e.g. "WMI" or "Battery report".</param>
public sealed record LogEntry(LogLevel Level, string Source, string Message);

/// <summary>
/// Collects what happened while gathering data: skipped sections, fallbacks and
/// WMI/battery report discrepancies. Collectors write here instead of throwing.
/// </summary>
public sealed class CollectionLog
{
    private readonly List<LogEntry> _entries = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public void Info(string source, string message) => Add(LogLevel.Info, source, message);

    public void Warning(string source, string message) => Add(LogLevel.Warning, source, message);

    public void Error(string source, string message) => Add(LogLevel.Error, source, message);

    private void Add(LogLevel level, string source, string message)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(level, source, message));
        }
    }
}
