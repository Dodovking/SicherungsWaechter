using System.Text.Encodings.Web;
using System.Text.Json;

namespace SicherungsWaechter.Core;

public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string DataDirectory { get; }
    public string SettingsFile => Path.Combine(DataDirectory, "einstellungen.json");
    public string LogDirectory => Path.Combine(DataDirectory, "Protokoll");

    public SettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SicherungsWaechter");
        Directory.CreateDirectory(DataDirectory);
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            var broken = SettingsFile + ".defekt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Copy(SettingsFile, broken, true); } catch { }
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var tmp = SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, SettingsFile, true);
    }
}
