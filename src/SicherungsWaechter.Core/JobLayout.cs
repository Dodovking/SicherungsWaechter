namespace SicherungsWaechter.Core;

/// <summary>Where a job's source lives and where its mirror, versions and deleted files go.</summary>
public sealed class JobLayout
{
    public const string VersionsFolder = "_Versionen";
    public const string DeletedFolder = "_Gelöscht";

    public required bool IsFile { get; init; }
    public required string SourcePath { get; init; }
    public required string SourceDir { get; init; }
    public required string TargetPath { get; init; }
    public required string MirrorRoot { get; init; }
    public required string VersionRoot { get; init; }
    public required string DeletedRoot { get; init; }
    public required string Label { get; init; }

    public static string SourceLabel(string source, bool isFile)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";
        string full;
        try { full = PathRules.Normalize(source); } catch { return source; }
        var name = Path.GetFileName(full);
        if (!string.IsNullOrEmpty(name)) return name;
        var root = (Path.GetPathRoot(full) ?? full).TrimEnd('\\', '/', ':');
        return isFile ? root : "Laufwerk " + root;
    }

    public static JobLayout For(BackupJob job)
    {
        var src = PathRules.Normalize(job.Source);
        var tgt = PathRules.Normalize(job.Target);
        var label = SourceLabel(src, job.SourceIsFile);
        if (job.SourceIsFile)
        {
            return new JobLayout
            {
                IsFile = true,
                SourcePath = src,
                SourceDir = Path.GetDirectoryName(src) ?? src,
                TargetPath = tgt,
                MirrorRoot = tgt,
                VersionRoot = Path.Combine(tgt, VersionsFolder),
                DeletedRoot = Path.Combine(tgt, DeletedFolder),
                Label = label,
            };
        }
        return new JobLayout
        {
            IsFile = false,
            SourcePath = src,
            SourceDir = src,
            TargetPath = tgt,
            MirrorRoot = Path.Combine(tgt, label),
            VersionRoot = Path.Combine(tgt, VersionsFolder, label),
            DeletedRoot = Path.Combine(tgt, DeletedFolder, label),
            Label = label,
        };
    }

    /// <summary>Relative path of a source path inside this job, or null if it does not belong to it.</summary>
    public string? ToRelative(string fullSourcePath)
    {
        string full;
        try { full = PathRules.Normalize(fullSourcePath); } catch { return null; }
        if (IsFile)
            return string.Equals(full, SourcePath, PathRules.Comparison) ? Path.GetFileName(SourcePath) : null;
        if (string.Equals(full, SourceDir, PathRules.Comparison)) return "";
        if (!PathRules.IsSameOrInside(full, SourceDir)) return null;
        return Path.GetRelativePath(SourceDir, full);
    }

    public string SourceOf(string rel) => rel.Length == 0 ? SourceDir : Path.Combine(SourceDir, rel);
    public string MirrorOf(string rel) => rel.Length == 0 ? MirrorRoot : Path.Combine(MirrorRoot, rel);

    public bool SourceAvailable => Directory.Exists(SourceDir);

    public bool TargetDriveAvailable
    {
        get
        {
            var root = PathRules.DriveRoot(TargetPath);
            return root != null && Directory.Exists(root);
        }
    }
}
