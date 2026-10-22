using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

public sealed class SettingsForm : DialogBase
{
    private readonly ToggleSwitch _autostart = new("Mit Windows starten (läuft dann unsichtbar neben der Uhr)");
    private readonly ToggleSwitch _notify = new("Hinweis anzeigen, wenn es ein Problem gibt (z. B. Festplatte fehlt)");
    private readonly Segmented _theme = new("Wie Windows", "Hell", "Dunkel");
    private readonly NumberBox _interval = new(5, 1440);

    public bool StartWithWindows => _autostart.Checked;
    public bool ShowNotifications => _notify.Checked;
    public int FullScanMinutes => _interval.Value;
    public ThemeChoice ThemeChoice => (ThemeChoice)_theme.SelectedIndex;

    public SettingsForm(AppSettings settings, string dataDir) : base("Einstellungen", "Speichern", 640, 470)
    {
        AddHeading("Allgemein");
        Place(_autostart, _autostart.Height, 4);
        Place(_notify, _notify.Height, 4);

        AddHeading("Darstellung");
        Place(_theme, _theme.Height);

        AddHeading("Sicherheitsnetz");
        AddLine("Zusätzlich alle", _interval, "Minuten alle Dateien komplett prüfen");
        AddHint("Änderungen werden sofort erkannt. Die Komplettprüfung findet auch Änderungen, die passiert sind, während das Programm nicht lief.");

        AddHeading("Über");
        var version = typeof(SettingsForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        AddHint($"SicherungsWächter {version} · Einstellungen und Protokoll: {dataDir}");
        var open = new FlatButton("Einstellungsordner öffnen");
        open.Click += (_, _) => Ui.OpenInExplorer(dataDir);
        Place(open, open.Height);
        FinishLayout();

        _autostart.Checked = settings.StartWithWindows;
        _notify.Checked = settings.ShowNotifications;
        _theme.SelectedIndex = (int)settings.Theme;
        _interval.Value = settings.FullScanIntervalMinutes;
        OkButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
    }
}
