namespace SicherungsWaechter.Core;

public sealed class SourceUnavailableException(string message) : Exception(message);
public sealed class TargetUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class TargetFullException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class FileBusyException(string message, Exception? inner = null) : IOException(message, inner);

public sealed class SyncStats
{
    public int Copied;
    public int Versioned;
    public int Deleted;
    public int Archived;
    public int Renamed;
    public int Errors;
    public long BytesCopied;

    public bool AnyChange => Copied + Deleted + Archived + Renamed > 0;

    public void Add(SyncStats o)
    {
        Copied += o.Copied; Versioned += o.Versioned; Deleted += o.Deleted; Archived += o.Archived;
        Renamed += o.Renamed; Errors += o.Errors; BytesCopied += o.BytesCopied;
    }

    public string Summary()
    {
        var parts = new List<string>();
        if (Copied > 0) parts.Add($"{Copied} Datei{(Copied == 1 ? "" : "en")} gesichert ({FormatBytes(BytesCopied)})");
        if (Renamed > 0) parts.Add($"{Renamed} umbenannt/verschoben");
        if (Archived > 0) parts.Add($"{Archived} gelöschte nach „{JobLayout.DeletedFolder}“");
        if (Deleted > 0) parts.Add($"{Deleted} gelöscht");
        if (Errors > 0) parts.Add($"{Errors} Fehler");
        return parts.Count == 0 ? "Keine Änderungen" : string.Join(", ", parts);
    }

    public static string FormatBytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{b} B" : $"{v:0.#} {u[i]}";
    }
}

/// <summary>Mirrors one job's source into its target, keeping replaced and deleted files in the archive folders.</summary>
public sealed class SyncEngine
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorNotReady = unchecked((int)0x80070015);
    private const int ErrorDeviceRemoved = unchecked((int)0x800701B1);
    private const int ErrorBadNetPath = unchecked((int)0x80070035);
    private const int ErrorNetNameDeleted = unchecked((int)0x80070040);

    private readonly BackupJob _job;
    private readonly ExcludeMatcher _excludes;
    private readonly Action<LogLevel, string> _log;
    private readonly Func<DateTime> _now;

    public JobLayout Layout { get; }
    public List<string> BusyFiles { get; } = [];
    public long MaxBytesPerSecond { get; set; }
    public bool PauseWhileScanning { get; set; }
    private int _scanned;

    public SyncEngine(BackupJob job, Action<LogLevel, string>? log = null, Func<DateTime>? now = null)
    {
        _job = job;
        Layout = JobLayout.For(job);
        _excludes = new ExcludeMatcher(job.Excludes);
        _log = log ?? ((_, _) => { });
        _now = now ?? (() => DateTime.Now);
    }

    public bool IsExcluded(string rel) => _excludes.IsExcluded(rel);

    public void EnsureAvailable()
    {
        if (!Layout.SourceAvailable)
            throw new SourceUnavailableException($"Quelle nicht erreichbar: {Layout.SourceDir}");
        if (!Layout.TargetDriveAvailable)
            throw new TargetUnavailableException($"Ziellaufwerk nicht angeschlossen: {PathRules.DriveRoot(Layout.TargetPath)}");
        try { Directory.CreateDirectory(Layout.MirrorRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TargetUnavailableException($"Zielordner kann nicht angelegt werden: {Layout.MirrorRoot} ({ex.Message})", ex);
        }
    }

    public SyncStats FullSync(CancellationToken ct = default)
    {
        var stats = new SyncStats();
        BusyFiles.Clear();
        EnsureAvailable();
        if (Layout.IsFile) Guard(stats, Layout.Label, () => SyncFile(Layout.Label, stats, ct));
        else SyncTree("", stats, ct);
        CleanupArchives();
        return stats;
    }

    public void CleanupArchives()
    {
        var now = _now();
        int v = Archive.Cleanup(Layout.VersionRoot, ArchiveKind.Version, _job.VersionRetentionDays, _job.MaxVersionsPerFile, now);
        int d = Archive.Cleanup(Layout.DeletedRoot, ArchiveKind.Deleted, _job.DeletedRetentionDays, 0, now);
        if (v + d > 0) _log(LogLevel.Info, $"Aufgeräumt: {v} alte Versionen, {d} gelöschte Dateien nach Ablauf der Aufbewahrungszeit entfernt");
    }

    /// <summary>Synchronises a single changed source path (file or directory, existing or deleted).</summary>
    public SyncStats SyncPath(string fullSourcePath, CancellationToken ct = default)
    {
        var stats = new SyncStats();
        var rel = Layout.ToRelative(fullSourcePath);
        if (rel == null) return stats;
        EnsureAvailable();
        if (rel.Length == 0)
        {
            SyncTree("", stats, ct);
            return stats;
        }
        if (_excludes.IsExcluded(rel)) return stats;
        if (!_job.IncludeSubfolders && !Layout.IsFile && rel.IndexOfAny(['\\', '/']) >= 0) return stats;

        var src = Layout.SourceOf(rel);
        if (File.Exists(src)) Guard(stats, rel, () => SyncFile(rel, stats, ct));
        else if (Directory.Exists(src))
        {
            if (!Layout.IsFile && _job.IncludeSubfolders) SyncTree(rel, stats, ct);
        }
        else if (Directory.Exists(Path.GetDirectoryName(src)))
        {
            Guard(stats, rel, () => HandleDeleted(rel, stats));
        }
        return stats;
    }

    public SyncStats Rename(string oldFullPath, string newFullPath, CancellationToken ct = default)
    {
        var stats = new SyncStats();
        var oldRel = Layout.ToRelative(oldFullPath);
        var newRel = Layout.ToRelative(newFullPath);
        EnsureAvailable();
        if (oldRel is { Length: > 0 } && newRel is { Length: > 0 } && !Layout.IsFile
            && !_excludes.IsExcluded(oldRel) && !_excludes.IsExcluded(newRel)
            && (_job.IncludeSubfolders || (oldRel.IndexOfAny(['\\', '/']) < 0 && newRel.IndexOfAny(['\\', '/']) < 0)))
        {
            var mOld = Layout.MirrorOf(oldRel);
            var mNew = Layout.MirrorOf(newRel);
            var sNew = Layout.SourceOf(newRel);
            bool caseOnly = string.Equals(oldRel, newRel, StringComparison.OrdinalIgnoreCase) && !string.Equals(oldRel, newRel, StringComparison.Ordinal);
            bool mirrorHasOld = File.Exists(mOld) || Directory.Exists(mOld);
            bool mirrorHasNew = File.Exists(mNew) || Directory.Exists(mNew);
            bool sourceHasOld = File.Exists(Layout.SourceOf(oldRel)) || Directory.Exists(Layout.SourceOf(oldRel));
            if (mirrorHasOld && (caseOnly || (!mirrorHasNew && !sourceHasOld)) && (File.Exists(sNew) || Directory.Exists(sNew)))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(mNew)!);
                    if (File.Exists(mOld)) File.Move(mOld, mNew);
                    else Directory.Move(mOld, mNew);
                    stats.Renamed++;
                    _log(LogLevel.Info, $"Umbenannt: {oldRel} → {newRel}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log(LogLevel.Warning, $"Umbenennen in der Sicherung nicht möglich ({ex.Message}), Datei wird neu gesichert");
                }
            }
        }
        stats.Add(SyncPath(oldFullPath, ct));
        stats.Add(SyncPath(newFullPath, ct));
        return stats;
    }

    private void SyncTree(string relDir, SyncStats stats, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var srcDir = Layout.SourceOf(relDir);
        var mirDir = Layout.MirrorOf(relDir);
        if (!Directory.Exists(srcDir))
        {
            if (relDir.Length > 0) Guard(stats, relDir, () => HandleDeleted(relDir, stats));
            return;
        }
        if (relDir.Length > 0 && File.Exists(mirDir)) Guard(stats, relDir, () => HandleDeleted(relDir, stats));
        CreateMirrorDirectory(mirDir);

        var seenFiles = new HashSet<string>(PathRules.Comparer);
        var seenDirs = new HashSet<string>(PathRules.Comparer);
        FileInfo[] files;
        DirectoryInfo[] dirs;
        try
        {
            var di = new DirectoryInfo(srcDir);
            files = di.GetFiles();
            dirs = _job.IncludeSubfolders ? di.GetDirectories() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stats.Errors++;
            _log(LogLevel.Warning, $"Ordner kann nicht gelesen werden, wird übersprungen: {srcDir} ({ex.Message})");
            return;
        }

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            ScanBreather(ct);
            seenFiles.Add(f.Name);
            var rel = Combine(relDir, f.Name);
            if (_excludes.IsExcluded(rel)) continue;
            Guard(stats, rel, () => SyncFile(rel, stats, ct));
        }
        foreach (var d in dirs)
        {
            seenDirs.Add(d.Name);
            var rel = Combine(relDir, d.Name);
            if (_excludes.IsExcluded(rel)) continue;
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            SyncTree(rel, stats, ct);
        }

        if (!Directory.Exists(mirDir)) return;
        foreach (var f in new DirectoryInfo(mirDir).GetFiles())
        {
            ct.ThrowIfCancellationRequested();
            if (f.Name.EndsWith(PathRules.TempSuffix, StringComparison.OrdinalIgnoreCase))
            {
                try { f.Delete(); } catch { }
                continue;
            }
            if (seenFiles.Contains(f.Name)) continue;
            var rel = Combine(relDir, f.Name);
            if (_excludes.IsExcluded(rel)) continue;
            Guard(stats, rel, () => HandleDeleted(rel, stats));
        }
        if (_job.IncludeSubfolders)
        {
            foreach (var d in new DirectoryInfo(mirDir).GetDirectories())
            {
                if (seenDirs.Contains(d.Name)) continue;
                var rel = Combine(relDir, d.Name);
                if (_excludes.IsExcluded(rel)) continue;
                Guard(stats, rel, () => HandleDeleted(rel, stats));
            }
        }
    }

    private void SyncFile(string rel, SyncStats stats, CancellationToken ct)
    {
        var src = new FileInfo(Layout.SourceOf(rel));
        if (!src.Exists)
        {
            if (Directory.Exists(src.FullName)) return;
            if (Directory.Exists(src.DirectoryName)) HandleDeleted(rel, stats);
            return;
        }
        var dstPath = Layout.MirrorOf(rel);
        if (Directory.Exists(dstPath))
            HandleDeleted(rel, stats);
        var dst = new FileInfo(dstPath);
        if (dst.Exists && IsSame(src, dst)) return;
        CopyAtomic(src, dst, rel, stats, ct);
    }

    private void CopyStream(Stream input, Stream output, CancellationToken ct)
    {
        var buffer = new byte[1 << 20];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long done = 0;
        int n;
        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            output.Write(buffer, 0, n);
            done += n;
            if (MaxBytesPerSecond <= 0) continue;
            var ahead = TimeSpan.FromSeconds((double)done / MaxBytesPerSecond) - clock.Elapsed;
            if (ahead > TimeSpan.Zero) ct.WaitHandle.WaitOne(ahead);
        }
        ct.ThrowIfCancellationRequested();
    }

    private void ScanBreather(CancellationToken ct)
    {
        if (PauseWhileScanning && ++_scanned % LowImpact.ScanBatch == 0) ct.WaitHandle.WaitOne(LowImpact.ScanPause);
    }

    public static bool IsSame(FileInfo a, FileInfo b) =>
        a.Length == b.Length && Math.Abs((a.LastWriteTimeUtc - b.LastWriteTimeUtc).TotalSeconds) <= 2;

    private void CopyAtomic(FileInfo src, FileInfo dst, string rel, SyncStats stats, CancellationToken ct)
    {
        CreateMirrorDirectory(dst.DirectoryName!);
        var tmp = dst.FullName + PathRules.TempSuffix;
        long length = src.Length;
        var mtime = src.LastWriteTimeUtc;
        try
        {
            using (var input = new FileStream(src.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan))
            using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                CopyStream(input, output, ct);
                output.Flush(true);
            }
            File.SetLastWriteTimeUtc(tmp, mtime);
            src.Refresh();
            if (!src.Exists || src.Length != length || src.LastWriteTimeUtc != mtime)
                throw new FileBusyException("Datei wurde während des Kopierens verändert");

            bool replaced = false;
            if (dst.Exists)
            {
                if (_job.KeepVersions)
                {
                    var versionPath = Archive.BuildPath(Layout.VersionRoot, rel, _now());
                    Directory.CreateDirectory(Path.GetDirectoryName(versionPath)!);
                    File.Move(dst.FullName, versionPath);
                    stats.Versioned++;
                }
                replaced = true;
            }
            File.Move(tmp, dst.FullName, true);
            stats.Copied++;
            stats.BytesCopied += length;
            _log(LogLevel.Success, (replaced ? "Aktualisiert: " : "Neu gesichert: ") + rel);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    private void HandleDeleted(string rel, SyncStats stats)
    {
        var mirror = Layout.MirrorOf(rel);
        if (File.Exists(mirror)) DeleteMirrorFile(rel, stats);
        else if (Directory.Exists(mirror))
        {
            if (_job.DeleteMode == DeleteMode.KeepInBackup) return;
            foreach (var f in Directory.EnumerateFiles(mirror, "*", SearchOption.AllDirectories).ToList())
            {
                var fileRel = Path.GetRelativePath(Layout.MirrorRoot, f);
                if (f.EndsWith(PathRules.TempSuffix, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(f); } catch { } continue; }
                Guard(stats, fileRel, () => DeleteMirrorFile(fileRel, stats));
            }
            Archive.RemoveEmptyDirectories(mirror, keepRoot: false);
        }
    }

    private void DeleteMirrorFile(string rel, SyncStats stats)
    {
        var mirror = Layout.MirrorOf(rel);
        switch (_job.DeleteMode)
        {
            case DeleteMode.KeepInBackup:
                return;
            case DeleteMode.DeleteToo:
                File.Delete(mirror);
                stats.Deleted++;
                _log(LogLevel.Info, "Gelöscht (auch in der Sicherung): " + rel);
                break;
            default:
                var archived = Archive.BuildPath(Layout.DeletedRoot, rel, _now());
                Directory.CreateDirectory(Path.GetDirectoryName(archived)!);
                File.Move(mirror, archived);
                stats.Archived++;
                _log(LogLevel.Info, $"Gelöscht, aufbewahrt in „{JobLayout.DeletedFolder}“: " + rel);
                break;
        }
    }

    private void CreateMirrorDirectory(string dir)
    {
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Classify(ex);
            throw;
        }
    }

    private void Guard(SyncStats stats, string rel, Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Classify(ex);
            if (ex is FileBusyException || ex.HResult is ErrorSharingViolation or ErrorLockViolation)
            {
                BusyFiles.Add(Layout.SourceOf(rel));
                _log(LogLevel.Warning, $"Gerade in Benutzung, wird später erneut versucht: {rel}");
                return;
            }
            stats.Errors++;
            _log(LogLevel.Error, $"{rel}: {ex.Message}");
        }
    }

    private void Classify(Exception ex)
    {
        if (ex.HResult is ErrorDiskFull or ErrorHandleDiskFull)
            throw new TargetFullException($"Die Sicherungsfestplatte ist voll ({PathRules.DriveRoot(Layout.TargetPath)})", ex);
        if (ex.HResult is ErrorNotReady or ErrorDeviceRemoved or ErrorBadNetPath or ErrorNetNameDeleted || !Layout.TargetDriveAvailable)
            throw new TargetUnavailableException($"Ziellaufwerk nicht mehr erreichbar: {PathRules.DriveRoot(Layout.TargetPath)}", ex);
        if (!Layout.SourceAvailable)
            throw new SourceUnavailableException($"Quelle nicht mehr erreichbar: {Layout.SourceDir}");
    }

    private static string Combine(string a, string b) => a.Length == 0 ? b : Path.Combine(a, b);
}
