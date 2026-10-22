namespace SicherungsWaechter.Core;

public enum JobState
{
    Starting,
    Idle,
    Working,
    Paused,
    Disabled,
    TargetMissing,
    SourceMissing,
    Error,
}

/// <summary>Watches one job's source and keeps its backup up to date on a background loop.</summary>
public sealed class JobRunner : IDisposable
{
    private enum OpKind { Change, Rename }

    private sealed record Op(OpKind Kind, string Path, string? OldPath, int Attempt);

    private const int MaxBusyAttempts = 20;
    private static readonly TimeSpan MaxBatchDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(15);

    private readonly BackupLog _log;
    private readonly Func<TimeSpan> _fullScanInterval;
    private readonly Func<bool> _saveResources;
    private readonly object _lock = new();
    private readonly List<Op> _pending = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private FileSystemWatcher? _watcher;
    private bool _watcherBroken;
    private DateTime _firstEvent;
    private DateTime _lastEvent;
    private DateTime _retryAfter = DateTime.MinValue;
    private bool _fullSyncRequested = true;
    private DateTime _nextFullSync = DateTime.MinValue;
    private Task? _loop;
    private bool _paused;

    public BackupJob Job { get; }
    public JobState State { get; private set; } = JobState.Starting;
    public string StatusText { get; private set; } = "Wird gestartet …";
    public DateTime? LastBackup { get; private set; }
    public DateTime? LastFullSync { get; private set; }
    public string LastResult { get; private set; } = "";
    public int FilesSaved { get; private set; }
    public int PendingCount { get { lock (_lock) return _pending.Count; } }

    public event Action<JobRunner>? Changed;
    public event Action<JobRunner, JobState, string>? Problem;

    public JobRunner(BackupJob job, BackupLog log, Func<TimeSpan> fullScanInterval, bool paused, Func<bool>? saveResources = null)
    {
        Job = job;
        _log = log;
        _fullScanInterval = fullScanInterval;
        _saveResources = saveResources ?? (() => false);
        _paused = paused;
    }

    public void Start()
    {
        if (!Job.Enabled)
        {
            SetState(JobState.Disabled, "Ausgeschaltet");
            return;
        }
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (!paused) _fullSyncRequested = true;
        _signal.Release();
    }

    public void RequestFullSync()
    {
        _fullSyncRequested = true;
        _signal.Release();
    }

    private void Log(LogLevel level, string message) => _log.Write(level, Job.DisplayName, message);

    private void SetState(JobState state, string text)
    {
        var old = State;
        State = state;
        StatusText = text;
        if (old != state && state is JobState.TargetMissing or JobState.SourceMissing or JobState.Error)
            Problem?.Invoke(this, state, text);
        Changed?.Invoke(this);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Tick(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "Unerwarteter Fehler: " + ex.Message);
                SetState(JobState.Error, "Fehler: " + ex.Message);
                _fullSyncRequested = true;
                await DelayAsync(TimeSpan.FromSeconds(30), ct);
            }
            await DelayAsync(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await _signal.WaitAsync(delay, ct); }
        catch (OperationCanceledException) { }
    }

    private void Tick(CancellationToken ct)
    {
        if (_paused)
        {
            StopWatcher();
            lock (_lock) _pending.Clear();
            if (State != JobState.Paused) SetState(JobState.Paused, "Pausiert");
            return;
        }

        SyncEngine engine;
        bool gentle = _saveResources();
        try { engine = new SyncEngine(Job, Log) { MaxBytesPerSecond = gentle ? LowImpact.CopyBytesPerSecond : 0, PauseWhileScanning = gentle }; }
        catch (Exception ex)
        {
            SetState(JobState.Error, "Ungültiger Pfad: " + ex.Message);
            return;
        }

        if (!engine.Layout.SourceAvailable)
        {
            StopWatcher();
            _fullSyncRequested = true;
            if (State != JobState.SourceMissing)
            {
                Log(LogLevel.Warning, "Quelle nicht erreichbar: " + engine.Layout.SourceDir);
                SetState(JobState.SourceMissing, "Quelle nicht erreichbar");
            }
            return;
        }

        EnsureWatcher(engine.Layout);

        if (!engine.Layout.TargetDriveAvailable)
        {
            _fullSyncRequested = true;
            if (State != JobState.TargetMissing)
            {
                Log(LogLevel.Warning, $"Sicherungsfestplatte {PathRules.DriveRoot(engine.Layout.TargetPath)} nicht angeschlossen – Änderungen werden nachgeholt, sobald sie wieder da ist");
                SetState(JobState.TargetMissing, "Sicherungsfestplatte nicht angeschlossen");
            }
            return;
        }

        var now = DateTime.Now;
        if (State is JobState.TargetMissing or JobState.Error && now < _retryAfter) return;

        if (_fullSyncRequested || now >= _nextFullSync)
        {
            _fullSyncRequested = false;
            lock (_lock) _pending.Clear();
            Run(engine, ct, full: true, () => engine.FullSync(ct));
            return;
        }

        List<Op> batch;
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                if (State is not JobState.Idle) SetIdle();
                return;
            }
            var delay = TimeSpan.FromSeconds(Math.Max(1, Job.DelaySeconds));
            if (now - _lastEvent < delay && now - _firstEvent < MaxBatchDelay) return;
            batch = Dedupe(_pending);
            _pending.Clear();
        }
        Run(engine, ct, full: false, () =>
        {
            var total = new SyncStats();
            var busy = new List<Op>();
            foreach (var op in batch)
            {
                ct.ThrowIfCancellationRequested();
                engine.BusyFiles.Clear();
                total.Add(op.Kind == OpKind.Rename ? engine.Rename(op.OldPath!, op.Path, ct) : engine.SyncPath(op.Path, ct));
                if (engine.BusyFiles.Count > 0) busy.AddRange(engine.BusyFiles.Select(p => op with { Kind = OpKind.Change, Path = p, OldPath = null }));
            }
            engine.BusyFiles.Clear();
            Requeue(busy);
            return total;
        });
    }

    private TimeSpan NextScanInterval()
    {
        var interval = _fullScanInterval();
        if (_saveResources() && LowImpact.OnBattery()) interval *= LowImpact.BatteryScanFactor;
        return interval;
    }

    private void Run(SyncEngine engine, CancellationToken ct, bool full, Func<SyncStats> work)
    {
        using var _ = LowImpact.Enter(_saveResources());
        SetState(JobState.Working, full ? "Prüft alle Dateien …" : "Sichert Änderungen …");
        try
        {
            var stats = work();
            if (full)
            {
                Requeue(engine.BusyFiles.Select(p => new Op(OpKind.Change, p, null, 0)));
                LastFullSync = DateTime.Now;
                _nextFullSync = DateTime.Now + NextScanInterval();
                Log(stats.Errors > 0 ? LogLevel.Warning : LogLevel.Info, "Komplettprüfung abgeschlossen: " + stats.Summary());
            }
            else if (stats.AnyChange || stats.Errors > 0)
            {
                Log(stats.Errors > 0 ? LogLevel.Warning : LogLevel.Info, stats.Summary());
            }
            FilesSaved += stats.Copied;
            if (stats.AnyChange || full) LastBackup = DateTime.Now;
            if (stats.AnyChange || full) LastResult = stats.Summary();
            SetIdle(stats.Errors > 0 ? $"Gesichert, {stats.Errors} Fehler (siehe Protokoll)" : null);
        }
        catch (OperationCanceledException) { throw; }
        catch (TargetUnavailableException ex)
        {
            _fullSyncRequested = true;
            _retryAfter = DateTime.Now.AddSeconds(10);
            Log(LogLevel.Warning, ex.Message);
            SetState(JobState.TargetMissing, "Sicherungsfestplatte nicht erreichbar");
        }
        catch (SourceUnavailableException ex)
        {
            _fullSyncRequested = true;
            Log(LogLevel.Warning, ex.Message);
            SetState(JobState.SourceMissing, "Quelle nicht erreichbar");
        }
        catch (TargetFullException ex)
        {
            _fullSyncRequested = true;
            _retryAfter = DateTime.Now.AddMinutes(5);
            Log(LogLevel.Error, ex.Message + " – bitte Platz schaffen oder alte Versionen löschen");
            SetState(JobState.Error, "Sicherungsfestplatte voll");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _fullSyncRequested = true;
            _retryAfter = DateTime.Now.AddSeconds(30);
            Log(LogLevel.Error, ex.Message);
            SetState(JobState.Error, "Fehler: " + ex.Message);
        }
    }

    private void SetIdle(string? text = null)
    {
        int pending = PendingCount;
        SetState(JobState.Idle, text ?? (pending > 0 ? $"{pending} Änderung(en) warten" : "Überwacht – alles gesichert"));
    }

    private void Requeue(IEnumerable<Op> ops)
    {
        lock (_lock)
        {
            foreach (var op in ops)
            {
                if (op.Attempt + 1 >= MaxBusyAttempts)
                {
                    Log(LogLevel.Warning, $"Datei ist dauerhaft in Benutzung und wird bei der nächsten Komplettprüfung erneut versucht: {op.Path}");
                    continue;
                }
                _pending.Add(op with { Attempt = op.Attempt + 1 });
                var at = DateTime.Now + BusyRetryDelay - TimeSpan.FromSeconds(Math.Max(1, Job.DelaySeconds));
                if (_lastEvent < at) _lastEvent = at;
                if (_firstEvent == default || _pending.Count == 1) _firstEvent = DateTime.Now;
            }
        }
    }

    private static List<Op> Dedupe(List<Op> ops)
    {
        var lastIndex = new Dictionary<string, int>(PathRules.Comparer);
        for (int i = 0; i < ops.Count; i++)
            if (ops[i].Kind == OpKind.Change) lastIndex[ops[i].Path] = i;
        var result = new List<Op>();
        for (int i = 0; i < ops.Count; i++)
            if (ops[i].Kind == OpKind.Rename || lastIndex[ops[i].Path] == i) result.Add(ops[i]);
        return result;
    }

    private void Enqueue(Op op)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            if (_pending.Count == 0) _firstEvent = now;
            _lastEvent = now;
            _pending.Add(op);
            if (_pending.Count > 200_000)
            {
                _pending.Clear();
                _fullSyncRequested = true;
            }
        }
    }

    private void EnsureWatcher(JobLayout layout)
    {
        if (_watcher != null && !_watcherBroken) return;
        StopWatcher();
        try
        {
            var w = new FileSystemWatcher(layout.SourceDir)
            {
                IncludeSubdirectories = !layout.IsFile && Job.IncludeSubfolders,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            if (layout.IsFile) w.Filter = Path.GetFileName(layout.SourcePath);
            w.Created += (_, e) => Enqueue(new Op(OpKind.Change, e.FullPath, null, 0));
            w.Deleted += (_, e) => Enqueue(new Op(OpKind.Change, e.FullPath, null, 0));
            w.Changed += (_, e) =>
            {
                if (Directory.Exists(e.FullPath)) return;
                Enqueue(new Op(OpKind.Change, e.FullPath, null, 0));
            };
            w.Renamed += (_, e) => Enqueue(new Op(OpKind.Rename, e.FullPath, e.OldFullPath, 0));
            w.Error += (_, e) =>
            {
                _watcherBroken = true;
                _fullSyncRequested = true;
                Log(LogLevel.Warning, "Überwachung unterbrochen (" + e.GetException().Message + ") – es folgt eine Komplettprüfung");
                _signal.Release();
            };
            w.EnableRaisingEvents = true;
            _watcher = w;
            _watcherBroken = false;
        }
        catch (Exception ex)
        {
            _watcher = null;
            Log(LogLevel.Warning, "Echtzeit-Überwachung konnte nicht gestartet werden: " + ex.Message);
        }
    }

    private void StopWatcher()
    {
        if (_watcher == null) return;
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        catch { }
        _watcher = null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _signal.Release();
        try { _loop?.Wait(TimeSpan.FromSeconds(10)); } catch { }
        StopWatcher();
        _cts.Dispose();
    }
}
