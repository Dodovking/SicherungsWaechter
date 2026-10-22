using SicherungsWaechter.Core;

namespace SicherungsWaechter.Tests;

public class JobRunnerTests
{
    private static bool WaitFor(Func<bool> cond, int seconds = 20)
    {
        var until = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < until)
        {
            if (cond()) return true;
            Thread.Sleep(200);
        }
        return cond();
    }

    [Fact]
    public void Watcher_BacksUpNewChangedRenamedAndDeletedFiles()
    {
        using var d = new TestDir();
        d.Write("start.txt", "s");
        var log = new BackupLog(null);
        using var runner = new JobRunner(new BackupJob { Name = "Test", Source = d.Source, Target = d.Target, DelaySeconds = 1 }, log, () => TimeSpan.FromHours(1), false);
        runner.Start();
        Assert.True(WaitFor(() => File.Exists(d.Mirror("start.txt")) && runner.State == JobState.Idle), "Erstsicherung");

        File.WriteAllText(Path.Combine(d.Source, "neu.txt"), "neu");
        Assert.True(WaitFor(() => File.Exists(d.Mirror("neu.txt"))), "neue Datei");

        File.WriteAllText(Path.Combine(d.Source, "neu.txt"), "geändert!");
        Assert.True(WaitFor(() => File.ReadAllText(d.Mirror("neu.txt")) == "geändert!"), "Änderung");

        Directory.CreateDirectory(Path.Combine(d.Source, "Ordner"));
        File.WriteAllText(Path.Combine(d.Source, "Ordner", "drin.txt"), "x");
        Assert.True(WaitFor(() => File.Exists(d.Mirror(Path.Combine("Ordner", "drin.txt")))), "neuer Ordner");

        Directory.Move(Path.Combine(d.Source, "Ordner"), Path.Combine(d.Source, "Umbenannt"));
        Assert.True(WaitFor(() => File.Exists(d.Mirror(Path.Combine("Umbenannt", "drin.txt"))) && !Directory.Exists(d.Mirror("Ordner"))), "umbenannt");

        File.Delete(Path.Combine(d.Source, "start.txt"));
        Assert.True(WaitFor(() => !File.Exists(d.Mirror("start.txt"))), "gelöscht");
        Assert.Single(Archive.List(Path.Combine(d.Target, "_Gelöscht", "Dokumente"), ArchiveKind.Deleted));
    }
}
