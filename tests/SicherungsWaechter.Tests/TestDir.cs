namespace SicherungsWaechter.Tests;

public sealed class TestDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "swtest-" + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "Quelle", "Dokumente");
    public string Target => Path.Combine(Root, "Ziel");

    public TestDir()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Target);
    }

    public string Write(string rel, string content, DateTime? mtime = null)
    {
        var p = Path.Combine(Source, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        File.SetLastWriteTimeUtc(p, mtime ?? DateTime.UtcNow.AddSeconds(-Random.Shared.Next(10, 1000)));
        return p;
    }

    public string Mirror(string rel) => Path.Combine(Target, "Dokumente", rel);

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}
