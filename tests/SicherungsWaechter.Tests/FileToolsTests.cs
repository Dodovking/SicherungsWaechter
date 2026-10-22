using SicherungsWaechter.Core;

namespace SicherungsWaechter.Tests;

public class FileToolsTests
{
    private static string Write(string dir, string rel, string content)
    {
        var p = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void FindsDuplicatesByContentNotName()
    {
        using var d = new TestDir();
        Write(d.Source, "a.txt", "gleicher Inhalt");
        Write(d.Source, "sub/b.txt", "gleicher Inhalt");
        Write(d.Source, "c.txt", "anderer Inhalt");
        Write(d.Source, "d.txt", "gleicherInhalt!");
        var groups = FileTools.FindDuplicates(d.Source, 1, gentle: false);
        var g = Assert.Single(groups);
        Assert.Equal(2, g.Files.Count);
        Assert.Equal(g.Size, g.Wasted);
    }

    [Fact]
    public void LargeDuplicatesUseFullHash()
    {
        using var d = new TestDir();
        var data = new byte[200_000];
        File.WriteAllBytes(Path.Combine(d.Source, "x.bin"), data);
        File.WriteAllBytes(Path.Combine(d.Source, "y.bin"), data);
        data[150_000] = 1;
        File.WriteAllBytes(Path.Combine(d.Source, "z.bin"), data);
        var g = Assert.Single(FileTools.FindDuplicates(d.Source, 1, gentle: false));
        Assert.Equal(["x.bin", "y.bin"], g.Files.Select(f => Path.GetFileName(f.Path)).Order());
    }

    [Fact]
    public void LargestReturnsTopN()
    {
        using var d = new TestDir();
        for (int i = 1; i <= 10; i++) Write(d.Source, $"f{i}.txt", new string('x', i * 10));
        var r = FileTools.FindLargest(d.Source, 3, gentle: false);
        Assert.Equal(10, r.TotalFiles);
        Assert.Equal(550, r.TotalBytes);
        Assert.Equal([100L, 90L, 80L], r.Files.Select(f => f.Size));
    }

    [Fact]
    public void ComparesFolders()
    {
        using var d = new TestDir();
        Write(d.Source, "gleich.txt", "1");
        Write(d.Target, "gleich.txt", "1");
        File.SetLastWriteTimeUtc(Path.Combine(d.Target, "gleich.txt"), File.GetLastWriteTimeUtc(Path.Combine(d.Source, "gleich.txt")));
        Write(d.Source, "anders.txt", "1");
        Write(d.Target, "anders.txt", "22");
        Write(d.Source, "nurA/x.txt", "a");
        Write(d.Target, "nurB.txt", "b");
        var r = FileTools.CompareFolders(d.Source, d.Target, compareContent: true, gentle: false).ToDictionary(e => e.RelativePath.Replace('\\', '/'), e => e.Status);
        Assert.Equal(CompareStatus.Same, r["gleich.txt"]);
        Assert.Equal(CompareStatus.Different, r["anders.txt"]);
        Assert.Equal(CompareStatus.OnlyLeft, r["nurA/x.txt"]);
        Assert.Equal(CompareStatus.OnlyRight, r["nurB.txt"]);
    }

    [Fact]
    public void HashesLikeSha256()
    {
        using var d = new TestDir();
        var p = Write(d.Source, "abc.txt", "abc");
        var h = FileTools.HashFile(p, gentle: true);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", h);
        Assert.True(FileTools.HashMatches(h, "BA7816BF 8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
        Assert.False(FileTools.HashMatches(h, ""));
    }

    [Fact]
    public void LowSpaceThreshold()
    {
        Assert.True(FileTools.IsLowSpace(2L << 30, 1000L << 30));
        Assert.False(FileTools.IsLowSpace(100L << 30, 1000L << 30));
        Assert.True(FileTools.IsLowSpace(500L << 20, 8L << 30));
        Assert.False(FileTools.IsLowSpace(3L << 30, 8L << 30));
    }
}
