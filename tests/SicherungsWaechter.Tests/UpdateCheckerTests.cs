using System.Net;
using System.Security.Cryptography;
using System.Text;
using SicherungsWaechter.Core;

namespace SicherungsWaechter.Tests;

public class UpdateCheckerTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(new string('x', 200_000));
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static string ReleaseJson(string tag, string? digest, bool withHashFile = false, bool withExe = true) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "SicherungsWächter {{tag}}",
          "body": "Neu: Update-Knopf",
          "html_url": "https://github.com/x/y/releases/tag/{{tag}}",
          "published_at": "2026-10-22T10:00:00Z",
          "assets": [
            {{(withExe ? $$"""{ "name": "SicherungsWaechter.exe", "size": {{Payload.Length}}, "browser_download_url": "https://dl/exe"{{(digest == null ? "" : $", \"digest\": \"sha256:{digest}\"")}} }""" : """{ "name": "other.zip", "browser_download_url": "https://dl/zip" }""")}}
            {{(withHashFile ? """, { "name": "SicherungsWaechter.exe.sha256", "browser_download_url": "https://dl/hash" }""" : "")}}
          ]
        }
        """;

    private static HttpResponseMessage Ok(byte[] b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };
    private static HttpResponseMessage Ok(string s) => Ok(Encoding.UTF8.GetBytes(s));

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("V2.0.1-beta", "2.0.1")]
    [InlineData("v1.0.0.7", "1.0.0")]
    public void ParseVersion_Variants(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Fact]
    public void ParseVersion_Garbage_ReturnsNull() => Assert.Null(UpdateChecker.ParseVersion("neueste"));

    [Fact]
    public void Parse_ReadsReleaseAndComparesVersions()
    {
        var info = UpdateChecker.Parse(ReleaseJson("v1.1.0", Hash(Payload)));
        Assert.Equal(new Version(1, 1, 0), info.Version);
        Assert.Equal("https://dl/exe", info.ExeUrl);
        Assert.Equal(Payload.Length, info.ExeSize);
        Assert.Equal(Hash(Payload), info.ExeSha256);
        Assert.Equal("Neu: Update-Knopf", info.Notes);
        Assert.True(info.IsNewerThan(new Version(1, 0, 0, 0)));
        Assert.False(info.IsNewerThan(new Version(1, 1, 0, 0)));
        Assert.False(info.IsNewerThan(new Version(1, 2, 0)));
    }

    [Fact]
    public void Parse_WithoutExe_Throws() =>
        Assert.Throws<UpdateException>(() => UpdateChecker.Parse(ReleaseJson("v1.1.0", null, withExe: false)));

    [Fact]
    public async Task GetLatest_NoRelease_ReturnsNull()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.Null(await checker.GetLatestAsync());
    }

    [Fact]
    public async Task GetLatest_RateLimited_ThrowsFriendlyError()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var e = await Assert.ThrowsAsync<UpdateException>(() => checker.GetLatestAsync());
        Assert.Contains("zu viele Anfragen", e.Message);
    }

    [Fact]
    public async Task Download_WithDigest_WritesVerifiedFile()
    {
        using var dir = new TestDir();
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath switch
        {
            "/repos/Dodovking/SicherungsWaechter/releases/latest" => Ok(ReleaseJson("v1.1.0", Hash(Payload))),
            "/exe" => Ok(Payload),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var checker = new UpdateChecker(new HttpClient(handler));
        var info = (await checker.GetLatestAsync())!;
        var dest = Path.Combine(dir.Root, "neu.exe");
        long last = 0;
        await checker.DownloadAsync(info, dest, new SyncProgress(p => last = p.done));
        Assert.Equal(Payload, File.ReadAllBytes(dest));
        Assert.Equal(Payload.Length, last);
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task Download_UsesHashFileWhenNoDigest()
    {
        using var dir = new TestDir();
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath switch
        {
            "/exe" => Ok(Payload),
            "/hash" => Ok(Hash(Payload) + "  SicherungsWaechter.exe\n"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var info = UpdateChecker.Parse(ReleaseJson("v1.1.0", null, withHashFile: true));
        var dest = Path.Combine(dir.Root, "neu.exe");
        await new UpdateChecker(new HttpClient(handler)).DownloadAsync(info, dest);
        Assert.Equal(Payload, File.ReadAllBytes(dest));
    }

    [Fact]
    public async Task Download_WrongHash_ThrowsAndLeavesNothing()
    {
        using var dir = new TestDir();
        var info = UpdateChecker.Parse(ReleaseJson("v1.1.0", new string('0', 64)));
        var dest = Path.Combine(dir.Root, "neu.exe");
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => Ok(Payload))));
        var e = await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(info, dest));
        Assert.Contains("Prüfsumme", e.Message);
        Assert.Empty(Directory.GetFiles(dir.Root));
    }

    [Fact]
    public async Task Download_WithoutAnyHash_Refuses()
    {
        using var dir = new TestDir();
        var info = UpdateChecker.Parse(ReleaseJson("v1.1.0", null));
        var handler = new FakeHandler(_ => Ok(Payload));
        var e = await Assert.ThrowsAsync<UpdateException>(() => new UpdateChecker(new HttpClient(handler)).DownloadAsync(info, Path.Combine(dir.Root, "neu.exe")));
        Assert.Contains("Prüfsumme", e.Message);
        Assert.Empty(handler.Requests);
    }

    private sealed class SyncProgress(Action<(long done, long total)> a) : IProgress<(long done, long total)>
    {
        public void Report((long done, long total) value) => a(value);
    }
}
