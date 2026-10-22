using SicherungsWaechter.Core;

namespace SicherungsWaechter.Tests;

public class SyncEngineTests
{
    private static BackupJob Job(TestDir d) => new() { Source = d.Source, Target = d.Target };

    [Fact]
    public void FullSync_CopiesFilesAndFolders_WithTimestamps()
    {
        using var d = new TestDir();
        var a = d.Write("a.txt", "hallo");
        d.Write(Path.Combine("Unter", "Tief", "b.docx"), "inhalt");
        Directory.CreateDirectory(Path.Combine(d.Source, "Leer"));

        var stats = new SyncEngine(Job(d)).FullSync();

        Assert.Equal(2, stats.Copied);
        Assert.Equal("hallo", File.ReadAllText(d.Mirror("a.txt")));
        Assert.Equal("inhalt", File.ReadAllText(d.Mirror(Path.Combine("Unter", "Tief", "b.docx"))));
        Assert.True(Directory.Exists(d.Mirror("Leer")));
        Assert.Equal(File.GetLastWriteTimeUtc(a), File.GetLastWriteTimeUtc(d.Mirror("a.txt")));
        Assert.Equal(0, new SyncEngine(Job(d)).FullSync().Copied);
    }

    [Fact]
    public void ChangedFile_OldVersionGoesToVersions()
    {
        using var d = new TestDir();
        d.Write("brief.txt", "v1");
        new SyncEngine(Job(d)).FullSync();
        d.Write("brief.txt", "version zwei");

        var now = new DateTime(2026, 10, 22, 11, 30, 0);
        var stats = new SyncEngine(Job(d), now: () => now).SyncPath(Path.Combine(d.Source, "brief.txt"));

        Assert.Equal(1, stats.Copied);
        Assert.Equal(1, stats.Versioned);
        Assert.Equal("version zwei", File.ReadAllText(d.Mirror("brief.txt")));
        var version = Path.Combine(d.Target, "_Versionen", "Dokumente", "brief (2026-10-22 11-30-00).txt");
        Assert.Equal("v1", File.ReadAllText(version));
        var entry = Assert.Single(Archive.List(Path.Combine(d.Target, "_Versionen", "Dokumente"), ArchiveKind.Version));
        Assert.Equal("brief.txt", entry.RelativePath);
    }

    [Fact]
    public void NoVersions_WhenDisabled()
    {
        using var d = new TestDir();
        d.Write("x.txt", "1");
        var job = Job(d);
        job.KeepVersions = false;
        new SyncEngine(job).FullSync();
        d.Write("x.txt", "22");
        new SyncEngine(job).FullSync();
        Assert.Equal("22", File.ReadAllText(d.Mirror("x.txt")));
        Assert.False(Directory.Exists(Path.Combine(d.Target, "_Versionen")));
    }

    [Fact]
    public void DeletedFileAndFolder_MovedToDeletedArchive()
    {
        using var d = new TestDir();
        var f = d.Write("weg.txt", "x");
        d.Write(Path.Combine("Ordner", "1.txt"), "1");
        d.Write(Path.Combine("Ordner", "Sub", "2.txt"), "2");
        new SyncEngine(Job(d)).FullSync();

        File.Delete(f);
        Directory.Delete(Path.Combine(d.Source, "Ordner"), true);
        var stats = new SyncEngine(Job(d)).FullSync();

        Assert.Equal(3, stats.Archived);
        Assert.False(File.Exists(d.Mirror("weg.txt")));
        Assert.False(Directory.Exists(d.Mirror("Ordner")));
        var deleted = Archive.List(Path.Combine(d.Target, "_Gelöscht", "Dokumente"), ArchiveKind.Deleted).Select(e => e.RelativePath).OrderBy(x => x).ToList();
        Assert.Equal(new[] { Path.Combine("Ordner", "1.txt"), Path.Combine("Ordner", "Sub", "2.txt"), "weg.txt" }, deleted);
    }

    [Fact]
    public void DeleteModes_DeleteTooAndKeep()
    {
        using var d = new TestDir();
        var f = d.Write("a.txt", "x");
        var job = Job(d);
        job.DeleteMode = DeleteMode.KeepInBackup;
        new SyncEngine(job).FullSync();
        File.Delete(f);
        new SyncEngine(job).FullSync();
        Assert.True(File.Exists(d.Mirror("a.txt")));

        job.DeleteMode = DeleteMode.DeleteToo;
        var stats = new SyncEngine(job).FullSync();
        Assert.Equal(1, stats.Deleted);
        Assert.False(File.Exists(d.Mirror("a.txt")));
        Assert.False(Directory.Exists(Path.Combine(d.Target, "_Gelöscht")));
    }

    [Fact]
    public void Rename_MovesInsideMirror()
    {
        using var d = new TestDir();
        d.Write(Path.Combine("Alt", "datei.txt"), "inhalt");
        new SyncEngine(Job(d)).FullSync();
        Directory.Move(Path.Combine(d.Source, "Alt"), Path.Combine(d.Source, "Neu"));

        var stats = new SyncEngine(Job(d)).Rename(Path.Combine(d.Source, "Alt"), Path.Combine(d.Source, "Neu"));

        Assert.Equal(1, stats.Renamed);
        Assert.Equal(0, stats.Copied);
        Assert.Equal(0, stats.Archived);
        Assert.True(File.Exists(d.Mirror(Path.Combine("Neu", "datei.txt"))));
        Assert.False(Directory.Exists(d.Mirror("Alt")));
    }

    [Fact]
    public void Excludes_AreNotCopiedNorDeleted()
    {
        using var d = new TestDir();
        d.Write("~$brief.docx", "lock");
        d.Write("temp.tmp", "t");
        d.Write(Path.Combine("node_modules", "x.js"), "x");
        d.Write("ok.txt", "ok");
        var job = Job(d);
        job.Excludes.Add("node_modules");
        Assert.Equal(1, new SyncEngine(job).FullSync().Copied);
        Assert.False(File.Exists(d.Mirror("temp.tmp")));
        Assert.False(Directory.Exists(d.Mirror("node_modules")));
    }

    [Fact]
    public void MissingSource_ThrowsAndDeletesNothing()
    {
        using var d = new TestDir();
        d.Write("a.txt", "x");
        new SyncEngine(Job(d)).FullSync();
        Directory.Move(d.Source, d.Source + "-weg");
        Assert.Throws<SourceUnavailableException>(() => new SyncEngine(Job(d)).FullSync());
        Assert.True(File.Exists(d.Mirror("a.txt")));
    }

    [Fact]
    public void SingleFileJob()
    {
        using var d = new TestDir();
        var f = d.Write("einzeln.xlsx", "1");
        d.Write("andere.txt", "nicht sichern");
        var job = new BackupJob { Source = f, SourceIsFile = true, Target = d.Target };
        new SyncEngine(job).FullSync();
        Assert.True(File.Exists(Path.Combine(d.Target, "einzeln.xlsx")));
        Assert.False(File.Exists(Path.Combine(d.Target, "andere.txt")));
        Assert.Null(new SyncEngine(job).Layout.ToRelative(Path.Combine(d.Source, "andere.txt")));

        d.Write("einzeln.xlsx", "22");
        new SyncEngine(job).SyncPath(f);
        Assert.Equal("22", File.ReadAllText(Path.Combine(d.Target, "einzeln.xlsx")));
        Assert.Single(Archive.List(Path.Combine(d.Target, "_Versionen"), ArchiveKind.Version));

        File.Delete(f);
        new SyncEngine(job).SyncPath(f);
        Assert.False(File.Exists(Path.Combine(d.Target, "einzeln.xlsx")));
        Assert.Single(Archive.List(Path.Combine(d.Target, "_Gelöscht"), ArchiveKind.Deleted));
    }

    [Fact]
    public void Retention_RemovesOldAndExcessVersions()
    {
        using var d = new TestDir();
        var root = Path.Combine(d.Target, "_Versionen", "Dokumente");
        var now = new DateTime(2026, 10, 22, 12, 0, 0);
        for (int i = 0; i < 5; i++)
        {
            var p = Archive.BuildPath(root, "a.txt", now.AddDays(-i));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, i.ToString());
        }
        var old = Archive.BuildPath(root, Path.Combine("s", "b.txt"), now.AddDays(-100));
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "alt");

        int removed = Archive.Cleanup(root, ArchiveKind.Version, 90, 3, now);

        Assert.Equal(3, removed);
        Assert.Equal(3, Archive.List(root, ArchiveKind.Version).Count());
        Assert.False(Directory.Exists(Path.Combine(root, "s")));
    }

    [Theory]
    [InlineData("brief (2026-10-22 11-30-00).txt", "brief.txt")]
    [InlineData("archiv.tar (2026-10-22 11-30-00 3).gz", "archiv.tar.gz")]
    [InlineData("Makefile (2026-10-22 11-30-00)", "Makefile")]
    [InlineData(" (2026-10-22 11-30-00).gitignore", ".gitignore")]
    public void ArchiveNames_RoundTrip(string archived, string original)
    {
        Assert.True(Archive.TryParse(archived, out var name, out var at));
        Assert.Equal(original, name);
        Assert.Equal(new DateTime(2026, 10, 22, 11, 30, 0), at);
    }

    [Theory]
    [InlineData("~$brief.docx", true)]
    [InlineData("Ordner/Thumbs.db", true)]
    [InlineData("a/node_modules/b/c.js", true)]
    [InlineData("Projekte/build/x.o", true)]
    [InlineData("Andere/build/x.o", false)]
    [InlineData("brief.docx", false)]
    [InlineData("x.docx.swtmp", true)]
    public void ExcludeMatcher_Works(string rel, bool excluded)
    {
        var m = new ExcludeMatcher(BackupJob.DefaultExcludes().Concat(["node_modules", "Projekte/build"]));
        Assert.Equal(excluded, m.IsExcluded(rel.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void Validator_RejectsTargetInsideSource()
    {
        using var d = new TestDir();
        var job = new BackupJob { Source = d.Source, Target = Path.Combine(d.Source, "Sicherung") };
        Assert.Contains(JobValidator.Validate(job, []), i => i.IsError);
        var ok = new BackupJob { Source = d.Source, Target = d.Target };
        Assert.DoesNotContain(JobValidator.Validate(ok, []), i => i.IsError);
        var clash = new BackupJob { Source = Path.Combine(d.Root, "Anders", "Dokumente"), Target = d.Target };
        Directory.CreateDirectory(clash.Source);
        Assert.Contains(JobValidator.Validate(clash, [ok]), i => i.IsError && i.Message.Contains("bereits"));
    }
}

public class RestoreTests
{
    [Fact]
    public void RestoreDeletedFile_ToOriginalLocation()
    {
        using var d = new TestDir();
        var f = d.Write(Path.Combine("Sub", "wichtig.txt"), "daten");
        var mtime = File.GetLastWriteTimeUtc(f);
        var job = new BackupJob { Source = d.Source, Target = d.Target };
        new SyncEngine(job).FullSync();
        File.Delete(f);
        var engine = new SyncEngine(job);
        engine.FullSync();
        var entry = Assert.Single(Archive.List(engine.Layout.DeletedRoot, ArchiveKind.Deleted));
        var dest = Restorer.OriginalPath(engine.Layout, entry);
        Assert.Equal(f, dest);
        Restorer.RestoreFile(entry.ArchivedPath, dest, overwrite: false);
        Assert.Equal("daten", File.ReadAllText(f));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(f));
        Assert.Throws<IOException>(() => Restorer.RestoreFile(entry.ArchivedPath, dest, overwrite: false));
    }
}
