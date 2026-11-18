using SicherungsWaechter.Core;

namespace SicherungsWaechter.Tests;

public class JobValidatorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NestedMirror_IsRejected_InEitherOrder(bool nestedIsFile, bool reverse)
    {
        using var d = new TestDir();
        var outer = new BackupJob { Source = d.Source, Target = d.Target };
        var nested = OtherJob(d, Path.Combine(JobLayout.For(outer).MirrorRoot, "Nested"), nestedIsFile);

        var issues = reverse ? JobValidator.Validate(outer, [nested]) : JobValidator.Validate(nested, [outer]);

        Assert.Contains(issues, i => i.IsError && i.Message.Contains("bereits"));
    }

    [Theory]
    [InlineData("Ziel")]
    [InlineData("Unrelated")]
    [InlineData("Ziel/Dokumente-extra")]
    public void NonOverlappingMirrors_AreAllowed(string target)
    {
        using var d = new TestDir();
        var first = new BackupJob { Source = d.Source, Target = d.Target };
        var other = OtherJob(d, Path.Combine(d.Root, target));

        Assert.DoesNotContain(JobValidator.Validate(other, [first]), i => i.IsError);
        Assert.DoesNotContain(JobValidator.Validate(first, [other]), i => i.IsError);
        Assert.DoesNotContain(JobValidator.Validate(first, []), i => i.IsError);
        Assert.DoesNotContain(JobValidator.Validate(first, [first]), i => i.IsError);
    }

    [Fact]
    public void FileJobs_WithDifferentLabels_CanShareTarget()
    {
        using var d = new TestDir();
        var first = new BackupJob { Source = d.Write("first.txt", "first"), SourceIsFile = true, Target = d.Target };
        var other = OtherJob(d, d.Target, true);

        Assert.DoesNotContain(JobValidator.Validate(first, [other]), i => i.IsError);
        Assert.DoesNotContain(JobValidator.Validate(other, [first]), i => i.IsError);
        new SyncEngine(first).FullSync();
        new SyncEngine(other).FullSync();
        new SyncEngine(first).FullSync();
        Assert.Equal("other", File.ReadAllText(JobLayout.For(other).MirrorOf("other.txt")));
    }

    [Theory]
    [InlineData(DeleteMode.MoveToArchive)]
    [InlineData(DeleteMode.DeleteToo)]
    public void UnvalidatedNestedJob_BackupIsRemovedByOuterFullSync(DeleteMode mode)
    {
        using var d = new TestDir();
        var outer = new BackupJob { Source = d.Source, Target = d.Target, DeleteMode = mode };
        var nested = OtherJob(d, Path.Combine(JobLayout.For(outer).MirrorRoot, "Nested"));
        new SyncEngine(nested).FullSync();
        var backup = JobLayout.For(nested).MirrorOf("backup.txt");
        Assert.True(File.Exists(backup));

        var stats = new SyncEngine(outer).FullSync();

        Assert.False(File.Exists(backup));
        if (mode == DeleteMode.DeleteToo) Assert.Equal(1, stats.Deleted);
        else
        {
            Assert.Equal(1, stats.Archived);
            var entry = Assert.Single(Archive.List(JobLayout.For(outer).DeletedRoot, ArchiveKind.Deleted));
            Assert.Equal("other", File.ReadAllText(entry.ArchivedPath));
        }
    }

    private static BackupJob OtherJob(TestDir d, string target, bool isFile = false)
    {
        var source = Path.Combine(d.Root, "OtherSource");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, isFile ? "other.txt" : "backup.txt");
        File.WriteAllText(file, "other");
        return new BackupJob { Source = isFile ? file : source, SourceIsFile = isFile, Target = target };
    }
}
