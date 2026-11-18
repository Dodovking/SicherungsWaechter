using SicherungsWaechter.Core;

namespace SicherungsWaechter.Tests;

public class SharedTargetArchiveTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0);

    [Theory]
    [InlineData(ArchiveKind.Version, false)]
    [InlineData(ArchiveKind.Version, true)]
    [InlineData(ArchiveKind.Deleted, false)]
    [InlineData(ArchiveKind.Deleted, true)]
    public void FullSync_TrimsOnlyTheSingleFileJobsOwnArchive(ArchiveKind kind, bool sharedWithFolder)
    {
        using var d = new TestDir();
        var (job, other, otherRelative) = SharedJobs(d, sharedWithFolder);
        var current = AddEntry(job, kind, "brief.txt", 0);
        var recent = AddEntry(job, kind, "brief.txt", 1);
        var expired = AddEntry(job, kind, "brief.txt", 2);
        var otherEntries = new[] { 0, 1, 2 }.Select(days => AddEntry(other, kind, otherRelative, days)).ToList();

        new SyncEngine(job, now: () => Now).FullSync();

        Assert.All(otherEntries, path => Assert.True(File.Exists(path), $"Other job lost {path}"));
        Assert.True(File.Exists(current));
        Assert.Equal(kind == ArchiveKind.Deleted, File.Exists(recent));
        Assert.False(File.Exists(expired));
    }

    [Theory]
    [InlineData(ArchiveKind.Version, false)]
    [InlineData(ArchiveKind.Version, true)]
    [InlineData(ArchiveKind.Deleted, false)]
    [InlineData(ArchiveKind.Deleted, true)]
    public void ArchiveListing_ReturnsOnlyTheJobsOwnEntries(ArchiveKind kind, bool sharedWithFolder)
    {
        using var d = new TestDir();
        var (job, other, otherRelative) = SharedJobs(d, sharedWithFolder);
        var ownPath = AddEntry(job, kind, "brief.txt", 0);
        var otherPath = AddEntry(other, kind, otherRelative, 0);

        var layout = JobLayout.For(job);
        var ownEntry = Assert.Single(Archive.List(layout, kind));
        Assert.Equal(ownPath, ownEntry.ArchivedPath);
        Assert.Equal(job.Source, Restorer.OriginalPath(layout, ownEntry));
        var otherEntry = Assert.Single(Archive.List(JobLayout.For(other), kind));
        Assert.Equal(otherPath, otherEntry.ArchivedPath);
        Assert.Equal(otherRelative, otherEntry.RelativePath);
    }

    private static (BackupJob job, BackupJob other, string otherRelative) SharedJobs(TestDir d, bool sharedWithFolder)
    {
        var job = new BackupJob
        {
            Source = d.Write("brief.txt", "current"),
            SourceIsFile = true,
            Target = d.Target,
            MaxVersionsPerFile = 1,
            DeletedRetentionDays = 1,
        };
        var other = new BackupJob
        {
            Source = sharedWithFolder ? d.Source : d.Write("anderer-brief.txt", "other"),
            SourceIsFile = !sharedWithFolder,
            Target = d.Target,
        };
        Assert.DoesNotContain(JobValidator.Validate(job, [other]), issue => issue.IsError);
        Assert.DoesNotContain(JobValidator.Validate(other, [job]), issue => issue.IsError);
        return (job, other, sharedWithFolder ? "brief.txt" : "anderer-brief.txt");
    }

    private static string AddEntry(BackupJob job, ArchiveKind kind, string relativePath, int daysAgo)
    {
        var layout = JobLayout.For(job);
        var root = kind == ArchiveKind.Version ? layout.VersionRoot : layout.DeletedRoot;
        var path = Archive.BuildPath(root, relativePath, Now.AddDays(-daysAgo));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);
        return path;
    }
}
