using System.Globalization;
using System.Text.RegularExpressions;

namespace SicherungsWaechter.Core;

public enum ArchiveKind
{
    Version,
    Deleted,
}

public sealed record ArchiveEntry(
    ArchiveKind Kind,
    string RelativePath,
    string ArchivedPath,
    DateTime ArchivedAt,
    DateTime LastWriteTime,
    long Size);

/// <summary>Old versions and deleted files: "name (yyyy-MM-dd HH-mm-ss).ext" next to the original relative path.</summary>
public static class Archive
{
    private const string TimeFormat = "yyyy-MM-dd HH-mm-ss";
    private static readonly Regex NameRx = new(
        @"^(?<base>.*) \((?<ts>\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2})(?: (?<n>\d+))?\)(?<ext>\.[^.]*)?$",
        RegexOptions.CultureInvariant);

    public static string BuildPath(string root, string rel, DateTime at)
    {
        var dir = Path.GetDirectoryName(rel) ?? "";
        var file = Path.GetFileName(rel);
        var ext = Path.GetExtension(file);
        var stem = file[..^ext.Length];
        var ts = at.ToString(TimeFormat, CultureInfo.InvariantCulture);
        var folder = dir.Length == 0 ? root : Path.Combine(root, dir);
        var candidate = Path.Combine(folder, $"{stem} ({ts}){ext}");
        for (int n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
            candidate = Path.Combine(folder, $"{stem} ({ts} {n}){ext}");
        return candidate;
    }

    public static bool TryParse(string fileName, out string originalName, out DateTime archivedAt)
    {
        originalName = "";
        archivedAt = default;
        var m = NameRx.Match(fileName);
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups["ts"].Value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out archivedAt))
            return false;
        originalName = m.Groups["base"].Value + m.Groups["ext"].Value;
        return originalName.Length > 0;
    }

    public static IEnumerable<ArchiveEntry> List(string root, ArchiveKind kind, string? onlyRelative = null)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
        }
        catch { yield break; }
        foreach (var f in files)
        {
            if (!TryParse(f.Name, out var original, out var at)) continue;
            var relDir = Path.GetRelativePath(root, f.DirectoryName!);
            var rel = relDir == "." ? original : Path.Combine(relDir, original);
            if (onlyRelative != null && !string.Equals(rel, onlyRelative, PathRules.Comparison)) continue;
            yield return new ArchiveEntry(kind, rel, f.FullName, at, f.LastWriteTime, f.Length);
        }
    }

    /// <summary>Deletes entries older than the retention and beyond the per-file limit. Returns number of deleted files.</summary>
    public static int Cleanup(string root, ArchiveKind kind, int retentionDays, int maxPerFile, DateTime now)
    {
        if (!Directory.Exists(root)) return 0;
        int removed = 0;
        foreach (var group in List(root, kind).GroupBy(e => e.RelativePath, PathRules.Comparer))
        {
            int index = 0;
            foreach (var e in group.OrderByDescending(e => e.ArchivedAt))
            {
                index++;
                bool tooOld = retentionDays > 0 && e.ArchivedAt < now.AddDays(-retentionDays);
                bool tooMany = maxPerFile > 0 && index > maxPerFile;
                if (!tooOld && !tooMany) continue;
                try { File.Delete(e.ArchivedPath); removed++; } catch { }
            }
        }
        if (removed > 0) RemoveEmptyDirectories(root, keepRoot: true);
        return removed;
    }

    public static void RemoveEmptyDirectories(string dir, bool keepRoot)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            foreach (var sub in Directory.GetDirectories(dir)) RemoveEmptyDirectories(sub, false);
            if (!keepRoot && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch { }
    }
}

public static class Restorer
{
    /// <summary>Copies a backed-up file back to <paramref name="destination"/>, keeping its original timestamp.</summary>
    public static void RestoreFile(string backupFile, string destination, bool overwrite)
    {
        if (File.Exists(destination) && !overwrite)
            throw new IOException($"Die Datei existiert bereits: {destination}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tmp = destination + PathRules.TempSuffix;
        var mtime = File.GetLastWriteTimeUtc(backupFile);
        try
        {
            File.Copy(backupFile, tmp, true);
            File.SetLastWriteTimeUtc(tmp, mtime);
            File.Move(tmp, destination, true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    public static string OriginalPath(JobLayout layout, ArchiveEntry entry) => layout.SourceOf(entry.RelativePath);
}
