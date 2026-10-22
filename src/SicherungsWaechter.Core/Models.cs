using System.Text.Json;
using System.Text.Json.Serialization;

namespace SicherungsWaechter.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeleteMode
{
    MoveToArchive,
    DeleteToo,
    KeepInBackup,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeChoice
{
    System,
    Hell,
    Dunkel,
}

public sealed class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public bool SourceIsFile { get; set; }
    public string Target { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;
    public List<string> Excludes { get; set; } = DefaultExcludes();
    public bool KeepVersions { get; set; } = true;
    public int MaxVersionsPerFile { get; set; } = 10;
    public int VersionRetentionDays { get; set; } = 90;
    public DeleteMode DeleteMode { get; set; } = DeleteMode.MoveToArchive;
    public int DeletedRetentionDays { get; set; } = 90;
    public int DelaySeconds { get; set; } = 3;

    public static List<string> DefaultExcludes() =>
        ["~$*", "*.tmp", "~*.tmp", ".~lock.*#", "Thumbs.db", "desktop.ini", "$RECYCLE.BIN", "System Volume Information"];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? JobLayout.SourceLabel(Source, SourceIsFile) : Name;

    public BackupJob Clone() =>
        JsonSerializer.Deserialize<BackupJob>(JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
}

public sealed class AppSettings
{
    public List<BackupJob> Jobs { get; set; } = [];
    public bool StartWithWindows { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public int FullScanIntervalMinutes { get; set; } = 60;
    public bool Paused { get; set; }
    public bool TrayHintShown { get; set; }
    public bool CheckForUpdates { get; set; } = true;
}
