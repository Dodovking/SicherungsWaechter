using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

public sealed class JobEditorForm : DialogBase
{
    private readonly BackupJob _job;
    private readonly IReadOnlyList<BackupJob> _others;
    private readonly InputBox _source = new();
    private readonly InputBox _target = new();
    private readonly InputBox _name = new();
    private readonly Label _preview;
    private readonly ToggleSwitch _subfolders = new("Unterordner mitsichern");
    private readonly ToggleSwitch _versions = new("Vorherige Fassung geänderter Dateien aufbewahren (in „_Versionen“)");
    private readonly NumberBox _maxVersions = new(0, 9999);
    private readonly NumberBox _versionDays = new(0, 36500);
    private readonly Segmented _deleteMode = new("In „_Gelöscht“ aufbewahren", "Auch in der Sicherung löschen", "In der Sicherung behalten");
    private readonly NumberBox _deletedDays = new(0, 36500);
    private readonly InputBox _excludes = new(multiline: true);
    private readonly NumberBox _delay = new(1, 600);
    private readonly Label _issues;
    private bool _sourceIsFile;

    public BackupJob Result => _job;

    public JobEditorForm(BackupJob? job, IReadOnlyList<BackupJob> allJobs, string? presetSource)
        : base(job == null ? "Neuer Sicherungsauftrag" : "Sicherungsauftrag bearbeiten", "Speichern", 720, 780)
    {
        _job = job?.Clone() ?? new BackupJob();
        _others = allJobs.Where(j => j.Id != _job.Id).ToList();
        if (presetSource != null)
        {
            _job.Source = presetSource;
            _job.SourceIsFile = File.Exists(presetSource);
            var lastTarget = _others.LastOrDefault()?.Target;
            if (lastTarget != null && string.IsNullOrEmpty(_job.Target)) _job.Target = lastTarget;
        }
        _sourceIsFile = _job.SourceIsFile;

        AddHeading("Was soll gesichert werden?");
        var pickFolder = new FlatButton("Ordner …");
        var pickFile = new FlatButton("Datei …");
        AddRow(_source, pickFolder, pickFile);
        AddHint("Ein ganzer Ordner (mit allem darin) oder eine einzelne Datei. Für mehrere Ordner einfach mehrere Aufträge anlegen.");

        AddHeading("Wohin? – am besten auf eine andere Festplatte");
        var pickTarget = new FlatButton("Ordner …");
        AddRow(_target, pickTarget);
        _preview = AddHint("");

        AddHeading("Name (optional)");
        AddRow(_name);

        AddHeading("Optionen");
        Place(_subfolders, _subfolders.Height, 4);
        Place(_versions, _versions.Height, 4);
        AddLine("Höchstens", _maxVersions, "Versionen je Datei, löschen nach", _versionDays, "Tagen  (0 = unbegrenzt)");

        AddHeading("Wenn eine Datei gelöscht wird …");
        _deleteMode.Width = ContentWidth;
        Place(_deleteMode, _deleteMode.Height);
        AddLine("Gelöschte Dateien aufbewahren für", _deletedDays, "Tage  (0 = für immer)");

        AddHeading("Nicht sichern (eine Regel pro Zeile)");
        _excludes.Width = ContentWidth;
        Place(_excludes, _excludes.Height);
        AddHint("Beispiele: *.tmp  ·  ~$*  ·  node_modules  ·  Fotos/Unscharf. * steht für beliebige Zeichen.");

        AddHeading("Wartezeit");
        AddLine("Nach einer Änderung", _delay, "Sekunden warten, bevor gesichert wird (damit große Dateien fertig geschrieben sind)");

        _issues = Make.Label("", Ui.Base, width: ContentWidth);
        _issues.Tag = "keepcolor";
        Place(_issues, Ui.S(40));
        FinishLayout();

        _source.Text = _job.Source;
        _target.Text = _job.Target;
        _name.Text = _job.Name;
        _subfolders.Checked = _job.IncludeSubfolders;
        _versions.Checked = _job.KeepVersions;
        _maxVersions.Value = _job.MaxVersionsPerFile;
        _versionDays.Value = _job.VersionRetentionDays;
        _deleteMode.SelectedIndex = (int)_job.DeleteMode;
        _deletedDays.Value = _job.DeletedRetentionDays;
        _excludes.Text = string.Join(Environment.NewLine, _job.Excludes);
        _delay.Value = _job.DelaySeconds;

        pickFolder.Click += (_, _) => PickSourceFolder();
        pickFile.Click += (_, _) => PickSourceFile();
        pickTarget.Click += (_, _) => PickTarget();
        _source.Box.TextChanged += (_, _) =>
        {
            var s = _source.Text.Trim();
            if (File.Exists(s)) _sourceIsFile = true;
            else if (Directory.Exists(s)) _sourceIsFile = false;
            UpdatePreview();
        };
        _target.Box.TextChanged += (_, _) => UpdatePreview();
        _versions.CheckedChanged += (_, _) => UpdateEnabled();
        _deleteMode.SelectedIndexChanged += (_, _) => UpdateEnabled();
        OkButton.Click += (_, _) => Save();
        UpdatePreview();
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        _maxVersions.Enabled = _versionDays.Enabled = _versions.Checked;
        _deletedDays.Enabled = _deleteMode.SelectedIndex == 0;
        _subfolders.Enabled = !_sourceIsFile;
    }

    private BackupJob Collect()
    {
        var j = _job.Clone();
        j.Source = _source.Text.Trim().Trim('"');
        j.SourceIsFile = _sourceIsFile;
        j.Target = _target.Text.Trim().Trim('"');
        j.Name = _name.Text.Trim();
        j.IncludeSubfolders = _subfolders.Checked;
        j.KeepVersions = _versions.Checked;
        j.MaxVersionsPerFile = _maxVersions.Value;
        j.VersionRetentionDays = _versionDays.Value;
        j.DeleteMode = (DeleteMode)_deleteMode.SelectedIndex;
        j.DeletedRetentionDays = _deletedDays.Value;
        j.Excludes = _excludes.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        j.DelaySeconds = _delay.Value;
        return j;
    }

    private void UpdatePreview()
    {
        UpdateEnabled();
        var j = Collect();
        if (string.IsNullOrWhiteSpace(j.Source) || string.IsNullOrWhiteSpace(j.Target))
        {
            _preview.Text = "Die Sicherung landet in einem Unterordner des Ziels, der wie die Quelle heißt.";
            ShowIssues([]);
            return;
        }
        try
        {
            var l = JobLayout.For(j);
            var where = l.IsFile ? Path.Combine(l.MirrorRoot, l.Label) : l.MirrorRoot;
            _preview.Text = $"Die Sicherung landet in: {where}";
        }
        catch { _preview.Text = ""; }
        ShowIssues(JobValidator.Validate(j, _others));
    }

    private void ShowIssues(List<ValidationIssue> issues)
    {
        var first = issues.FirstOrDefault(i => i.IsError) ?? issues.FirstOrDefault();
        _issues.Text = first == null ? "" : (first.IsError ? "✖ " : "⚠ ") + first.Message;
        _issues.ForeColor = first?.IsError == true ? Theme.P.Danger : Theme.P.Warning;
        OkButton.Enabled = !issues.Any(i => i.IsError);
    }

    private void Save()
    {
        var j = Collect();
        var issues = JobValidator.Validate(j, _others);
        if (issues.Any(i => i.IsError))
        {
            ShowIssues(issues);
            return;
        }
        _job.Source = j.Source;
        foreach (var prop in typeof(BackupJob).GetProperties().Where(p => p.CanWrite))
            prop.SetValue(_job, prop.GetValue(j));
        DialogResult = DialogResult.OK;
        Close();
    }

    private void PickSourceFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Ordner auswählen, der gesichert werden soll", UseDescriptionForTitle = true };
        if (Directory.Exists(_source.Text)) d.InitialDirectory = _source.Text;
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _sourceIsFile = false;
        _source.Text = d.SelectedPath;
    }

    private void PickSourceFile()
    {
        using var d = new OpenFileDialog { Title = "Datei auswählen, die gesichert werden soll", CheckFileExists = true };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _sourceIsFile = true;
        _source.Text = d.FileName;
    }

    private void PickTarget()
    {
        using var d = new FolderBrowserDialog { Description = "Zielordner auf der Sicherungsfestplatte auswählen", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(_target.Text)) d.InitialDirectory = _target.Text;
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _target.Text = d.SelectedPath;
    }
}
