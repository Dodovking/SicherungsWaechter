using System.Text;
using System.Text.RegularExpressions;

namespace SicherungsWaechter.Core;

public static class PathRules
{
    public static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static readonly StringComparer Comparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public const string TempSuffix = ".swtmp";

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(full) ?? "";
        if (full.Length > root.Length)
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    public static bool IsSameOrInside(string path, string parent)
    {
        path = Normalize(path);
        parent = Normalize(parent);
        if (string.Equals(path, parent, Comparison)) return true;
        var withSep = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return path.StartsWith(withSep, Comparison);
    }

    public static string? DriveRoot(string path)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(path)); }
        catch { return null; }
    }
}

public sealed class ExcludeMatcher
{
    private readonly List<(Regex Rx, bool HasSeparator)> _rules = [];

    public ExcludeMatcher(IEnumerable<string> patterns)
    {
        foreach (var raw in patterns)
        {
            var p = raw.Trim().Replace('\\', '/').Trim('/');
            if (p.Length == 0 || p.StartsWith('#')) continue;
            _rules.Add((new Regex("^" + GlobToRegex(p) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), p.Contains('/')));
        }
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*') { sb.Append(".*"); i++; }
                else sb.Append("[^/]*");
            }
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.ToString();
    }

    public bool IsExcluded(string relativePath)
    {
        if (relativePath.Length == 0) return false;
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;
        if (segments[^1].EndsWith(PathRules.TempSuffix, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var (rx, hasSep) in _rules)
        {
            if (hasSep)
            {
                for (int n = 1; n <= segments.Length; n++)
                    if (rx.IsMatch(string.Join('/', segments, 0, n))) return true;
            }
            else
            {
                foreach (var s in segments)
                    if (rx.IsMatch(s)) return true;
            }
        }
        return false;
    }
}
