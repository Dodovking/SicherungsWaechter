using System.Diagnostics;
using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

/// <summary>Swaps the running EXE for a downloaded one and restarts – no reinstall, settings stay untouched.</summary>
internal static class Updater
{
    public const string AfterUpdateArg = "--nach-update";
    public const string SelfTestArg = "--selbsttest";
    public const string SourceArg = "--update-quelle";
    public const int SelfTestExitCode = 57;

    public static Version CurrentVersion => UpdateChecker.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0, 0));

    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
    private static string Dir => Path.GetDirectoryName(ExePath)!;
    private static string BaseName => Path.GetFileNameWithoutExtension(ExePath);
    public static string NewExePath => Path.Combine(Dir, BaseName + ".neu.exe");
    private static string OldExePath => ExePath + ".alt";

    /// <summary>Null if the program folder is writable, otherwise a message for the user.</summary>
    public static string? CheckWritable()
    {
        try
        {
            var probe = Path.Combine(Dir, ".schreibtest-" + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return $"SicherungsWächter darf seinen eigenen Ordner nicht ändern:\n{Dir}\n\nLege die EXE z. B. in „Dokumente“ oder auf den Desktop und starte sie von dort, dann klappt das Update.";
        }
    }

    /// <summary>Starts the downloaded EXE in self-test mode to make sure it actually runs before replacing anything.</summary>
    public static void VerifyStarts(string exe)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, SelfTestArg) { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new UpdateException("Die neue Version ließ sich nicht starten.");
        if (!p.WaitForExit(90_000))
        {
            try { p.Kill(); } catch { }
            throw new UpdateException("Die neue Version reagiert nicht. Das Update wurde nicht installiert.");
        }
        if (p.ExitCode != SelfTestExitCode)
            throw new UpdateException($"Die neue Version startet nicht richtig (Code {p.ExitCode}). Das Update wurde nicht installiert.");
    }

    private static string _target = "";
    private static bool _relocated;
    private static string? _movedOld;

    /// <summary>
    /// Renames the running EXE (Windows allows that) and moves the new one into its place.
    /// If the running file was deleted meanwhile, installs into %LOCALAPPDATA%\SicherungsWaechter instead.
    /// </summary>
    public static void Swap()
    {
        _target = ExePath;
        _relocated = false;
        if (!File.Exists(_target))
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SicherungsWaechter");
            Directory.CreateDirectory(dir);
            _target = Path.Combine(dir, "SicherungsWaechter.exe");
            _relocated = true;
        }
        string? old = null;
        if (File.Exists(_target))
        {
            old = _target + ".alt";
            try { if (File.Exists(old)) File.Delete(old); }
            catch { old = _target + "." + DateTime.Now.Ticks + ".alt"; }
            File.Move(_target, old);
        }
        try
        {
            File.Move(NewExePath, _target);
        }
        catch
        {
            if (old != null) File.Move(old, _target);
            throw;
        }
        _movedOld = old;
    }

    public static void Rollback()
    {
        if (_movedOld == null || !File.Exists(_movedOld)) return;
        try { if (File.Exists(_target)) File.Move(_target, NewExePath, true); } catch { }
        File.Move(_movedOld, _target, true);
        _movedOld = null;
    }

    public static void StartUpdated(IEnumerable<string> originalArgs)
    {
        var psi = new ProcessStartInfo(_target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(_target)! };
        foreach (var a in StripArgs(originalArgs.ToArray())) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(AfterUpdateArg);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi);
        if (_relocated) CreateShortcuts(_target);
    }

    private static void CreateShortcuts(string exe)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            {
                dynamic lnk = shell.CreateShortcut(Path.Combine(folder, "SicherungsWächter.lnk"));
                lnk.TargetPath = exe;
                lnk.WorkingDirectory = Path.GetDirectoryName(exe);
                lnk.IconLocation = exe + ",0";
                lnk.Description = "Automatische Sicherung auf eine andere Festplatte";
                lnk.Save();
            }
        }
        catch { }
    }

    private static IEnumerable<string> StripArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], AfterUpdateArg, StringComparison.OrdinalIgnoreCase)) { i++; continue; }
            if (string.Equals(args[i], "--minimiert", StringComparison.OrdinalIgnoreCase)) continue;
            yield return args[i];
        }
    }

    /// <summary>When started by an update: waits until the old process has exited. Returns true if this is a post-update start.</summary>
    public static bool WaitForPreviousInstance(string[] args)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, AfterUpdateArg, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.WaitForExit(60_000);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        return true;
    }

    /// <summary>Removes the old EXE and leftovers of an aborted download.</summary>
    public static void CleanupLeftovers()
    {
        new Thread(() =>
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                bool left = false;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(Dir, Path.GetFileName(ExePath) + "*.alt")
                                 .Concat(Directory.EnumerateFiles(Dir, BaseName + ".neu.exe*")))
                    {
                        try { File.Delete(f); } catch { left = true; }
                    }
                }
                catch { return; }
                if (!left) return;
                Thread.Sleep(1000);
            }
        }) { IsBackground = true }.Start();
    }

    public static string? SourceUrl(string[] args)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, SourceArg, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
