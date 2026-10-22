using System.Security.Cryptography;

namespace SicherungsWaechter.Core;

public sealed record FileEntry(string Path, long Size, DateTime LastWrite);

public sealed record DuplicateGroup(long Size, string Hash, List<FileEntry> Files)
{
    public long Wasted => Size * (Files.Count - 1);
}

public enum CompareStatus { OnlyLeft, OnlyRight, Different, Same }

public sealed record CompareEntry(string RelativePath, CompareStatus Status, FileEntry? Left, FileEntry? Right);

public sealed record LargestResult(List<FileEntry> Files, int TotalFiles, long TotalBytes);

/// <summary>Small stand-alone helpers: duplicates, largest files, folder comparison, checksums.</summary>
public static class FileTools
{
    private const int QuickHashBytes = 64 * 1024;

    private static EnumerationOptions Options => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    private static FileEntry Entry(FileInfo f) => new(f.FullName, f.Length, f.LastWriteTime);

    private static IEnumerable<FileInfo> Walk(string root, bool gentle, IProgress<string>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Ordner nicht gefunden: {root}");
        int n = 0;
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Options))
        {
            ct.ThrowIfCancellationRequested();
            if (f.Name.EndsWith(PathRules.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            n++;
            if (gentle && n % LowImpact.ScanBatch == 0) ct.WaitHandle.WaitOne(LowImpact.ScanPause);
            if (n % 500 == 0) progress?.Report($"{n:N0} Dateien durchsucht …");
            yield return f;
        }
    }

    public static List<DuplicateGroup> FindDuplicates(string root, long minSize, bool gentle, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var _ = LowImpact.Enter(gentle);
        var bySize = new Dictionary<long, List<FileInfo>>();
        foreach (var f in Walk(root, gentle, progress, ct))
        {
            if (f.Length < Math.Max(1, minSize)) continue;
            if (!bySize.TryGetValue(f.Length, out var list)) bySize[f.Length] = list = [];
            list.Add(f);
        }
        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        int total = candidates.Sum(c => c.Value.Count), done = 0;
        var result = new List<DuplicateGroup>();
        foreach (var (size, files) in candidates)
        {
            var quick = files.GroupBy(f => { progress?.Report($"Vergleiche Inhalte … {++done:N0} von {total:N0}"); return TryHash(f.FullName, QuickHashBytes, gentle, ct); })
                .Where(g => g.Key != null && g.Count() > 1);
            foreach (var q in quick)
            {
                var full = size <= QuickHashBytes
                    ? [q]
                    : q.GroupBy(f => TryHash(f.FullName, long.MaxValue, gentle, ct)).Where(g => g.Key != null && g.Count() > 1);
                foreach (var g in full)
                    result.Add(new DuplicateGroup(size, g.Key!, g.Select(Entry).OrderBy(e => e.LastWrite).ToList()));
            }
        }
        return result.OrderByDescending(g => g.Wasted).ToList();
    }

    public static LargestResult FindLargest(string root, int count, bool gentle, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var _ = LowImpact.Enter(gentle);
        var heap = new PriorityQueue<FileEntry, long>();
        int files = 0;
        long bytes = 0;
        foreach (var f in Walk(root, gentle, progress, ct))
        {
            files++;
            bytes += f.Length;
            heap.Enqueue(Entry(f), f.Length);
            if (heap.Count > count) heap.Dequeue();
        }
        var list = new List<FileEntry>();
        while (heap.Count > 0) list.Add(heap.Dequeue());
        list.Reverse();
        return new LargestResult(list, files, bytes);
    }

    public static List<CompareEntry> CompareFolders(string left, string right, bool compareContent, bool gentle, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var _ = LowImpact.Enter(gentle);
        Dictionary<string, FileInfo> Index(string root) =>
            Walk(root, gentle, progress, ct).ToDictionary(f => Path.GetRelativePath(root, f.FullName), f => f, PathRules.Comparer);
        var a = Index(left);
        var b = Index(right);
        var result = new List<CompareEntry>();
        int done = 0;
        foreach (var (rel, fa) in a)
        {
            ct.ThrowIfCancellationRequested();
            if (!b.TryGetValue(rel, out var fb))
            {
                result.Add(new CompareEntry(rel, CompareStatus.OnlyLeft, Entry(fa), null));
                continue;
            }
            bool same = fa.Length == fb.Length;
            if (same && compareContent)
            {
                progress?.Report($"Vergleiche Inhalte … {++done:N0}");
                var ha = TryHash(fa.FullName, long.MaxValue, gentle, ct);
                same = ha != null && ha == TryHash(fb.FullName, long.MaxValue, gentle, ct);
            }
            else if (same) same = SyncEngine.IsSame(fa, fb);
            result.Add(new CompareEntry(rel, same ? CompareStatus.Same : CompareStatus.Different, Entry(fa), Entry(fb)));
        }
        foreach (var (rel, fb) in b)
            if (!a.ContainsKey(rel)) result.Add(new CompareEntry(rel, CompareStatus.OnlyRight, null, Entry(fb)));
        return result.OrderBy(e => e.Status).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string HashFile(string path, bool gentle, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var _ = LowImpact.Enter(gentle);
        return Hash(path, long.MaxValue, gentle, progress, ct);
    }

    public static bool HashMatches(string actual, string expected)
    {
        var e = new string(expected.Where(Uri.IsHexDigit).ToArray());
        return e.Length > 0 && string.Equals(actual, e, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLowSpace(long free, long total) => free < Math.Min(5L << 30, Math.Max(1L << 30, total / 20));

    private static string? TryHash(string path, long limit, bool gentle, CancellationToken ct)
    {
        try { return Hash(path, limit, gentle, null, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string Hash(string path, long limit, bool gentle, IProgress<string>? progress, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        var buffer = new byte[1 << 20];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long done = 0, length = fs.Length;
        int n;
        while (done < limit && (n = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - done))) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, n);
            done += n;
            if (progress != null && length > 0) progress.Report($"Berechne … {done * 100 / length} %");
            if (!gentle) continue;
            var ahead = TimeSpan.FromSeconds((double)done / LowImpact.CopyBytesPerSecond) - clock.Elapsed;
            if (ahead > TimeSpan.Zero) ct.WaitHandle.WaitOne(ahead);
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
