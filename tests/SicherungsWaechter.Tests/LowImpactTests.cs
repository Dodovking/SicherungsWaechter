using System.Diagnostics;
using SicherungsWaechter.Core;

namespace SicherungsWaechter.Tests;

public class LowImpactTests
{
    [Fact]
    public void ThrottledCopyRespectsSpeedLimit()
    {
        using var d = new TestDir();
        var data = new byte[3 * 1024 * 1024];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(Path.Combine(d.Source, "gross.bin"), data);
        var engine = new SyncEngine(new BackupJob { Source = d.Source, Target = d.Target }) { MaxBytesPerSecond = 2 * 1024 * 1024 };
        var sw = Stopwatch.StartNew();
        var stats = engine.FullSync();
        sw.Stop();
        Assert.Equal(1, stats.Copied);
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1.2), $"zu schnell: {sw.Elapsed}");
        var copy = Directory.GetFiles(d.Target, "gross.bin", SearchOption.AllDirectories).Single();
        Assert.Equal(data, File.ReadAllBytes(copy));
    }

    [Fact]
    public void ScanPausesStillSyncEverything()
    {
        using var d = new TestDir();
        for (int i = 0; i < 450; i++) File.WriteAllText(Path.Combine(d.Source, $"f{i}.txt"), i.ToString());
        var stats = new SyncEngine(new BackupJob { Source = d.Source, Target = d.Target }) { PauseWhileScanning = true }.FullSync();
        Assert.Equal(450, stats.Copied);
    }

    [Fact]
    public void EnterIsHarmlessOffWindows()
    {
        using (LowImpact.Enter(true)) { }
        using (LowImpact.Enter(false)) { }
    }
}
