using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

public sealed class RestoreForm : Form
{
    private readonly BackupJob _job;
    private readonly JobLayout _layout;
    private readonly Segmented _kind = new("Ältere Versionen", "Gelöschte Dateien", "Aktuelle Sicherung");
    private readonly InputBox _search = new();
    private readonly DataGridView _grid = new();
    private readonly Label _count = Make.Label("", Ui.Small, muted: true);
    private readonly FlatButton _restore = new("An Originalort wiederherstellen", ButtonKind.Primary);
    private readonly FlatButton _restoreTo = new("Wiederherstellen nach …");
    private readonly FlatButton _show = new("Im Explorer zeigen");
    private readonly FlatButton _close = new("Schließen");
    private List<ArchiveEntry> _all = [];

    public RestoreForm(BackupJob job)
    {
        _job = job;
        _layout = JobLayout.For(job);
        Text = $"Wiederherstellen – {job.DisplayName}";
        Font = Ui.Base;
        Icon = Ui.AppIcon();
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(Ui.S(940), Ui.S(620));
        MinimumSize = new Size(Ui.S(760), Ui.S(460));

        var top = new Panel { Dock = DockStyle.Top, Height = Ui.S(64) };
        _search.Width = Ui.S(260);
        top.Controls.AddRange([_kind, _search]);
        top.Layout += (_, _) =>
        {
            _kind.Location = new Point(Ui.S(20), Ui.S(16));
            _search.Location = new Point(top.Width - Ui.S(20) - _search.Width, Ui.S(16) + (_kind.Height - _search.Height) / 2);
        };
        _search.Box.PlaceholderText = "Datei suchen …";

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(64) };
        bottom.Controls.AddRange([_count, _restore, _restoreTo, _show, _close]);
        bottom.Layout += (_, _) =>
        {
            int y = Ui.S(15);
            _count.Location = new Point(Ui.S(20), y + Ui.S(8));
            int x = bottom.Width - Ui.S(20);
            foreach (var b in new Control[] { _close, _show, _restoreTo, _restore })
            {
                x -= b.Width;
                b.Location = new Point(x, y);
                x -= Ui.S(8);
            }
        };

        var frame = new Card { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(2), Ui.S(4), Ui.S(2), Ui.S(4)) };
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(20), 0, Ui.S(20), 0) };
        outer.Controls.Add(frame);
        frame.Controls.Add(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoGenerateColumns = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = Ui.S(34);
        _grid.RowTemplate.Height = Ui.S(30);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "Datei", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "when", HeaderText = "Gesichert bis", Width = Ui.S(150) });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "stand", HeaderText = "Stand der Datei", Width = Ui.S(150) });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "Größe", Width = Ui.S(90), DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        Controls.Add(outer);
        Controls.Add(bottom);
        Controls.Add(top);

        _kind.SelectedIndexChanged += (_, _) => Reload();
        _search.Box.TextChanged += (_, _) => Fill();
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Show(); };
        _restore.Click += (_, _) => RestoreSelected(null);
        _restoreTo.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Wohin sollen die Dateien wiederhergestellt werden?", UseDescriptionForTitle = true };
            if (d.ShowDialog(this) == DialogResult.OK) RestoreSelected(d.SelectedPath);
        };
        _show.Click += (_, _) => Show();
        _close.Click += (_, _) => Close();
        CancelButton = _close;
        Reload();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Apply(this);
        Theme.ApplyTitleBar(this);
    }

    private void Reload()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            _all = _kind.SelectedIndex switch
            {
                0 => Archive.List(_layout.VersionRoot, ArchiveKind.Version).ToList(),
                1 => Archive.List(_layout.DeletedRoot, ArchiveKind.Deleted).ToList(),
                _ => CurrentFiles(),
            };
            _grid.Columns["when"]!.HeaderText = _kind.SelectedIndex switch { 0 => "Ersetzt am", 1 => "Gelöscht am", _ => "Gesichert" };
            _grid.Columns["when"]!.Visible = _kind.SelectedIndex != 2;
        }
        finally { Cursor = Cursors.Default; }
        Fill();
    }

    private List<ArchiveEntry> CurrentFiles()
    {
        if (_layout.IsFile)
        {
            var f = new FileInfo(Path.Combine(_layout.MirrorRoot, _layout.Label));
            return f.Exists ? [new ArchiveEntry(ArchiveKind.Version, _layout.Label, f.FullName, f.LastWriteTime, f.LastWriteTime, f.Length)] : [];
        }
        if (!Directory.Exists(_layout.MirrorRoot)) return [];
        return new DirectoryInfo(_layout.MirrorRoot)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => !f.Name.EndsWith(PathRules.TempSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(f => new ArchiveEntry(ArchiveKind.Version, Path.GetRelativePath(_layout.MirrorRoot, f.FullName), f.FullName, f.LastWriteTime, f.LastWriteTime, f.Length))
            .ToList();
    }

    private void Fill()
    {
        var q = _search.Text.Trim();
        var items = _all.Where(e => q.Length == 0 || e.RelativePath.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.ArchivedAt).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).Take(5000).ToList();
        _grid.Rows.Clear();
        foreach (var e in items)
        {
            int i = _grid.Rows.Add(e.RelativePath, e.ArchivedAt.ToString("dd.MM.yyyy HH:mm:ss"), e.LastWriteTime.ToString("dd.MM.yyyy HH:mm"), SyncStats.FormatBytes(e.Size));
            _grid.Rows[i].Tag = e;
        }
        _count.Text = _all.Count == 0
            ? (_kind.SelectedIndex switch { 0 => "Noch keine älteren Versionen vorhanden", 1 => "Keine gelöschten Dateien aufbewahrt", _ => "Noch nichts gesichert" })
            : $"{items.Count} von {_all.Count} Einträgen";
        _grid.ClearSelection();
        UpdateButtons();
    }

    private List<ArchiveEntry> Selected() => _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<ArchiveEntry>().ToList();

    private void UpdateButtons()
    {
        int n = Selected().Count;
        _restore.Enabled = _restoreTo.Enabled = n > 0;
        _show.Enabled = n == 1;
    }

    private new void Show()
    {
        var s = Selected().FirstOrDefault();
        if (s != null) Ui.OpenInExplorer(s.ArchivedPath, select: true);
    }

    private void RestoreSelected(string? folder)
    {
        var entries = Selected();
        if (entries.Count == 0) return;
        var dupes = entries.GroupBy(e => e.RelativePath, PathRules.Comparer).Where(g => g.Count() > 1).ToList();
        if (dupes.Count > 0)
        {
            MessageBox.Show(this, "Bitte pro Datei nur eine Version auswählen.", "Wiederherstellen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var targets = entries.Select(e => (Entry: e, Dest: folder == null ? Restorer.OriginalPath(_layout, e) : Path.Combine(folder, folder == null ? e.RelativePath : Path.GetFileName(e.RelativePath)))).ToList();
        if (folder != null && entries.Count > 1)
            targets = entries.Select(e => (Entry: e, Dest: Path.Combine(folder, _layout.IsFile ? Path.GetFileName(e.RelativePath) : e.RelativePath))).ToList();
        int existing = targets.Count(t => File.Exists(t.Dest));
        if (existing > 0)
        {
            var msg = existing == 1 && targets.Count == 1
                ? $"Die Datei gibt es dort schon:\n{targets[0].Dest}\n\nÜberschreiben?"
                : $"{existing} der {targets.Count} Dateien gibt es dort schon. Überschreiben?";
            if (folder == null && _job.KeepVersions)
                msg += "\n\nDie jetzige Fassung bleibt dabei als ältere Version in der Sicherung erhalten.";
            if (MessageBox.Show(this, msg, "Wiederherstellen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }
        int ok = 0;
        var errors = new List<string>();
        foreach (var (entry, dest) in targets)
        {
            try
            {
                Restorer.RestoreFile(entry.ArchivedPath, dest, overwrite: true);
                ok++;
            }
            catch (Exception ex) { errors.Add($"{entry.RelativePath}: {ex.Message}"); }
        }
        var text = $"{ok} Datei{(ok == 1 ? "" : "en")} wiederhergestellt.";
        if (errors.Count > 0) text += "\n\nFehler:\n" + string.Join("\n", errors.Take(10));
        MessageBox.Show(this, text, "Wiederherstellen", MessageBoxButtons.OK, errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        if (targets.Count == 1 && ok == 1) Ui.OpenInExplorer(targets[0].Dest, select: true);
        Reload();
    }
}
