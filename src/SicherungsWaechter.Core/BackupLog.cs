namespace SicherungsWaechter.Core;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Job, string Message);

public sealed class BackupLog
{
    private const int MaxEntries = 3000;
    private readonly object _lock = new();
    private readonly LinkedList<LogEntry> _entries = new();
    private readonly string? _directory;

    public event Action<LogEntry>? EntryAdded;

    public BackupLog(string? directory)
    {
        _directory = directory;
        if (_directory != null)
        {
            Directory.CreateDirectory(_directory);
            CleanupOldFiles();
        }
    }

    public string? LogDirectory => _directory;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock) return _entries.ToList();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    public void Write(LogLevel level, string job, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, job, message);
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveFirst();
            if (_directory != null)
            {
                try
                {
                    var file = Path.Combine(_directory, entry.Time.ToString("yyyy-MM-dd") + ".log");
                    File.AppendAllText(file, $"{entry.Time:HH:mm:ss} [{LevelText(level)}] {(job.Length > 0 ? job + ": " : "")}{message}{Environment.NewLine}");
                }
                catch { }
            }
        }
        EntryAdded?.Invoke(entry);
    }

    public static string LevelText(LogLevel l) => l switch
    {
        LogLevel.Success => "OK",
        LogLevel.Warning => "Warnung",
        LogLevel.Error => "Fehler",
        _ => "Info",
    };

    private void CleanupOldFiles()
    {
        try
        {
            foreach (var f in new DirectoryInfo(_directory!).GetFiles("*.log"))
                if (f.LastWriteTime < DateTime.Now.AddDays(-60)) f.Delete();
        }
        catch { }
    }
}
