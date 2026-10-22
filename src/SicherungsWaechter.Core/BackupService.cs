namespace SicherungsWaechter.Core;

public sealed class BackupService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly List<JobRunner> _runners = [];
    private readonly object _lock = new();

    public AppSettings Settings { get; }
    public BackupLog Log { get; }
    public string DataDirectory => _store.DataDirectory;

    public event Action? JobsChanged;
    public event Action<JobRunner>? RunnerChanged;
    public event Action<JobRunner, JobState, string>? Problem;

    public BackupService(SettingsStore store)
    {
        _store = store;
        Settings = store.Load();
        Log = new BackupLog(store.LogDirectory);
    }

    public IReadOnlyList<JobRunner> Runners
    {
        get { lock (_lock) return _runners.ToList(); }
    }

    public bool Paused => Settings.Paused;

    public void Start()
    {
        Log.Write(LogLevel.Info, "", Settings.Paused ? "SicherungsWächter gestartet (pausiert)" : "SicherungsWächter gestartet");
        lock (_lock)
            foreach (var job in Settings.Jobs) _runners.Add(CreateRunner(job));
        JobsChanged?.Invoke();
    }

    private JobRunner CreateRunner(BackupJob job)
    {
        var r = new JobRunner(job, Log, () => TimeSpan.FromMinutes(Math.Max(5, Settings.FullScanIntervalMinutes)), Settings.Paused, () => Settings.SaveResources);
        r.Changed += x => RunnerChanged?.Invoke(x);
        r.Problem += (x, s, t) => Problem?.Invoke(x, s, t);
        r.Start();
        return r;
    }

    public void Save() => _store.Save(Settings);

    public void AddOrUpdateJob(BackupJob job)
    {
        JobRunner? old;
        lock (_lock)
        {
            var idx = Settings.Jobs.FindIndex(j => j.Id == job.Id);
            if (idx >= 0) Settings.Jobs[idx] = job;
            else Settings.Jobs.Add(job);
            old = _runners.FirstOrDefault(r => r.Job.Id == job.Id);
            if (old != null) _runners.Remove(old);
        }
        old?.Dispose();
        var runner = CreateRunner(job);
        lock (_lock)
        {
            var idx = Settings.Jobs.FindIndex(j => j.Id == job.Id);
            _runners.Insert(Math.Min(idx, _runners.Count), runner);
        }
        Save();
        Log.Write(LogLevel.Info, job.DisplayName, old == null ? "Auftrag angelegt" : "Auftrag geändert");
        JobsChanged?.Invoke();
    }

    public void RemoveJob(Guid id)
    {
        JobRunner? runner;
        lock (_lock)
        {
            Settings.Jobs.RemoveAll(j => j.Id == id);
            runner = _runners.FirstOrDefault(r => r.Job.Id == id);
            if (runner != null) _runners.Remove(runner);
        }
        runner?.Dispose();
        Save();
        if (runner != null) Log.Write(LogLevel.Info, runner.Job.DisplayName, "Auftrag entfernt (bereits gesicherte Dateien bleiben auf der Festplatte)");
        JobsChanged?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        Settings.Paused = paused;
        Save();
        foreach (var r in Runners) r.SetPaused(paused);
        Log.Write(LogLevel.Info, "", paused ? "Überwachung pausiert" : "Überwachung fortgesetzt");
    }

    public void SyncAllNow()
    {
        foreach (var r in Runners) r.RequestFullSync();
    }

    public void Dispose()
    {
        foreach (var r in Runners) r.Dispose();
    }
}
