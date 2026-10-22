using Microsoft.Win32;

namespace SicherungsWaechter.Gui;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SicherungsWaechter";

    public static string Command(string? dataDir)
    {
        var cmd = $"\"{Environment.ProcessPath}\" --minimiert";
        if (dataDir != null) cmd += $" --daten \"{dataDir}\"";
        return cmd;
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void Set(bool enabled, string? dataDir)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command(dataDir));
            else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
        }
        catch { }
    }
}
