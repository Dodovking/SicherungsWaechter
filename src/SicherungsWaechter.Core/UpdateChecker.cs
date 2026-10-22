using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SicherungsWaechter.Core;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Title,
    string Notes,
    string PageUrl,
    DateTime? Published,
    string ExeUrl,
    long ExeSize,
    string? ExeSha256,
    string? Sha256Url)
{
    public bool IsNewerThan(Version current) => UpdateChecker.Normalize(Version) > UpdateChecker.Normalize(current);
}

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reads the latest GitHub release and downloads its EXE with SHA-256 verification.</summary>
public sealed class UpdateChecker
{
    public const string Repository = "Dodovking/SicherungsWaechter";
    public const string ExeAssetName = "SicherungsWaechter.exe";
    public const string HashAssetName = ExeAssetName + ".sha256";

    private readonly HttpClient _http;
    private readonly string _releaseUrl;

    public UpdateChecker(HttpClient? http = null, string? releaseUrl = null)
    {
        _http = http ?? CreateClient();
        _releaseUrl = releaseUrl ?? $"https://api.github.com/repos/{Repository}/releases/latest";
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SicherungsWaechter", "1"));
        return http;
    }

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var m = Regex.Match(tag.Trim(), @"^[vV]?(\d+(?:\.\d+){1,3})");
        return m.Success && Version.TryParse(m.Groups[1].Value, out var v) ? Normalize(v) : null;
    }

    /// <summary>Returns null when no release has been published yet.</summary>
    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _releaseUrl);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new UpdateException("Keine Verbindung zu GitHub. Bist du mit dem Internet verbunden?", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException("GitHub antwortet nicht. Bitte später noch einmal versuchen.", e);
        }
        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new UpdateException("GitHub hat gerade zu viele Anfragen von dir bekommen. Bitte in einer Stunde noch einmal versuchen.");
            if (!resp.IsSuccessStatusCode)
                throw new UpdateException($"GitHub hat mit Fehler {(int)resp.StatusCode} geantwortet.");
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json);
        }
    }

    public static UpdateInfo Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { throw new UpdateException("Die Update-Informationen von GitHub sind unlesbar.", e); }
        using (doc)
        {
            var root = doc.RootElement;
            string tag = Str(root, "tag_name") ?? "";
            var version = ParseVersion(tag) ?? throw new UpdateException($"Unbekannte Versionsnummer „{tag}“.");
            string? exeUrl = null, hashUrl = null, digest = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    if (string.Equals(name, ExeAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        exeUrl = url;
                        size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var l) ? l : 0;
                        var d = Str(a, "digest");
                        if (d != null && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) digest = d[7..].ToLowerInvariant();
                    }
                    else if (string.Equals(name, HashAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        hashUrl = url;
                    }
                }
            }
            if (exeUrl == null)
                throw new UpdateException($"Version {version.ToString(3)} ist auf GitHub, enthält aber noch keine Programmdatei.");
            DateTime? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && p.TryGetDateTime(out var dt) ? dt : null;
            return new UpdateInfo(version, tag, Str(root, "name") is { Length: > 0 } n ? n : tag, (Str(root, "body") ?? "").Trim(),
                Str(root, "html_url") ?? $"https://github.com/{Repository}/releases", published, exeUrl, size, digest, hashUrl);
        }
    }

    /// <summary>Downloads the new EXE to <paramref name="destination"/>; throws if the checksum does not match.</summary>
    public async Task DownloadAsync(UpdateInfo info, string destination, IProgress<(long done, long total)>? progress = null, CancellationToken ct = default)
    {
        var expected = info.ExeSha256 ?? await GetHashFileAsync(info, ct).ConfigureAwait(false)
            ?? throw new UpdateException("Für dieses Update fehlt die Prüfsumme. Aus Sicherheitsgründen wird es nicht installiert.");
        var part = destination + ".part";
        try
        {
            using var resp = await _http.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new UpdateException($"Download fehlgeschlagen (Fehler {(int)resp.StatusCode}).");
            long total = resp.Content.Headers.ContentLength ?? info.ExeSize;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var buf = new byte[1 << 16];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    hash.AppendData(buf, 0, n);
                    done += n;
                    progress?.Report((done, total));
                }
                await dst.FlushAsync(ct).ConfigureAwait(false);
                if (info.ExeSize > 0 && done != info.ExeSize)
                    throw new UpdateException("Der Download ist unvollständig. Bitte noch einmal versuchen.");
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (actual != expected)
                throw new UpdateException("Die heruntergeladene Datei ist beschädigt (Prüfsumme stimmt nicht). Das Update wurde nicht installiert.");
            File.Move(part, destination, true);
        }
        catch (HttpRequestException e)
        {
            throw new UpdateException("Die Verbindung ist beim Herunterladen abgebrochen. Bitte noch einmal versuchen.", e);
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
        }
    }

    private async Task<string?> GetHashFileAsync(UpdateInfo info, CancellationToken ct)
    {
        if (info.Sha256Url == null) return null;
        try
        {
            var text = await _http.GetStringAsync(info.Sha256Url, ct).ConfigureAwait(false);
            var m = Regex.Match(text, @"\b[0-9a-fA-F]{64}\b");
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }
        catch (HttpRequestException e)
        {
            throw new UpdateException("Die Prüfsumme des Updates konnte nicht geladen werden.", e);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
