using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

/// <summary>Shared frame for the small tools: input rows on top, result grid, status and buttons at the bottom.</summary>
public abstract class ToolWindow : Form
{
    private readonly Panel _top = new() { Dock = DockStyle.Top };
    private readonly Panel _bottom = new() { Dock = DockStyle.Bottom, Height = Ui.S(64) };
    private readonly List<(Label Label, InputBox Box, FlatButton Browse)> _rows = [];
    private readonly List<Control> _options = [];
    private readonly List<Control> _actions = [];
    private readonly string _startText;
    private CancellationTokenSource? _cts;

    protected readonly DataGridView Grid = new();
    protected readonly Label Status = Make.Label("", Ui.Small, muted: true);
    protected readonly FlatButton StartButton;
    protected bool Gentle { get; }
    protected bool Busy => _cts != null;

    protected ToolWindow(string title, string startText, bool gentle)
    {
        Gentle = gentle;
        _startText = startText;
        StartButton = new FlatButton(startText, ButtonKind.Primary);
        StartButton.FitWidth(TextRenderer.MeasureText("Abbrechen", Ui.Base).Width + Ui.S(28));
        Text = title + " – SicherungsWächter";
        Font = Ui.Base;
        Icon = Ui.AppIcon();
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Ui.S(940), Ui.S(620));
        MinimumSize = new Size(Ui.S(760), Ui.S(460));
        AllowDrop = true;

        _top.Controls.Add(StartButton);
        _top.Layout += (_, _) => LayoutTop();
        var close = new FlatButton("Schließen");
        close.Click += (_, _) => Close();
        _bottom.Controls.AddRange([Status, close]);
        _actions.Add(close);
        _bottom.Layout += (_, _) =>
        {
            int y = Ui.S(15);
            int x = _bottom.Width - Ui.S(20);
            foreach (var b in _actions)
            {
                x -= b.Width;
                b.Location = new Point(x, y);
                x -= Ui.S(8);
            }
            Status.Location = new Point(Ui.S(20), y + Ui.S(8));
            Status.MaximumSize = new Size(Math.Max(Ui.S(100), x - Ui.S(28)), 0);
        };

        var frame = new Card { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(2), Ui.S(4), Ui.S(2), Ui.S(4)) };
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(20), 0, Ui.S(20), 0) };
        outer.Controls.Add(frame);
        frame.Controls.Add(Grid);
        Grid.Dock = DockStyle.Fill;
        Grid.ReadOnly = true;
        Grid.AllowUserToAddRows = false;
        Grid.AllowUserToDeleteRows = false;
        Grid.AllowUserToResizeRows = false;
        Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Grid.MultiSelect = true;
        Grid.AutoGenerateColumns = false;
        Grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        Grid.ColumnHeadersHeight = Ui.S(34);
        Grid.RowTemplate.Height = Ui.S(30);
        Grid.SelectionChanged += (_, _) => UpdateButtons();

        Controls.Add(outer);
        Controls.Add(_bottom);
        Controls.Add(_top);
        StartButton.Click += async (_, _) => { if (_cts != null) _cts.Cancel(); else await StartAsync(); };
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
            for (int i = 0; i < _rows.Count && i < paths.Length; i++) _rows[i].Box.Text = paths[i];
            if (paths.Length == 1 && _rows.Count > 1)
                (_rows.FirstOrDefault(r => r.Box.Text.Length == 0).Box ?? _rows[0].Box).Text = paths[0];
        };
    }

    protected InputBox PathRow(string label, bool file)
    {
        var l = Make.Label(label, Ui.Base, muted: true);
        var box = new InputBox();
        box.Box.PlaceholderText = file ? "Datei wählen oder hierher ziehen …" : "Ordner wählen oder hierher ziehen …";
        var browse = new FlatButton("Durchsuchen …");
        browse.Click += (_, _) =>
        {
            if (file)
            {
                using var d = new OpenFileDialog { Title = label };
                if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.FileName;
            }
            else
            {
                using var d = new FolderBrowserDialog { Description = label, UseDescriptionForTitle = true };
                if (Directory.Exists(box.Text)) d.InitialDirectory = box.Text;
                if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            }
        };
        _top.Controls.AddRange([l, box, browse]);
        _rows.Add((l, box, browse));
        return box;
    }

    protected T Option<T>(T c) where T : Control
    {
        _top.Controls.Add(c);
        _options.Add(c);
        return c;
    }

    protected FlatButton Action(string text, Action onClick, ButtonKind kind = ButtonKind.Secondary)
    {
        var b = new FlatButton(text, kind) { Enabled = false };
        b.Click += (_, _) => onClick();
        _bottom.Controls.Add(b);
        _actions.Insert(_actions.Count - 1, b);
        return b;
    }

    protected void Column(string name, string header, int width = 0, bool right = false)
    {
        var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, SortMode = DataGridViewColumnSortMode.NotSortable };
        if (width == 0) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
        else c.Width = Ui.S(width);
        if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        Grid.Columns.Add(c);
    }

    private void LayoutTop()
    {
        int left = Ui.S(20), labelW = Ui.S(120), y = Ui.S(16);
        foreach (var (l, box, browse) in _rows)
        {
            browse.Location = new Point(_top.Width - Ui.S(20) - browse.Width, y + (box.Height - browse.Height) / 2);
            box.Location = new Point(left + labelW, y);
            box.Width = browse.Left - Ui.S(8) - box.Left;
            l.Location = new Point(left, y + (box.Height - l.PreferredHeight) / 2);
            y += Math.Max(box.Height, browse.Height) + Ui.S(10);
        }
        int rowH = Math.Max(StartButton.Height, _options.Count == 0 ? 0 : _options.Max(o => o.Height));
        int x = left + labelW;
        foreach (var o in _options)
        {
            o.Location = new Point(x, y + (rowH - o.Height) / 2);
            x += o.Width + Ui.S(10);
        }
        StartButton.Location = new Point(_top.Width - Ui.S(20) - StartButton.Width, y + (rowH - StartButton.Height) / 2);
        int h = y + rowH + Ui.S(16);
        if (_top.Height != h) _top.Height = h;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Apply(this);
        Theme.ApplyTitleBar(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }

    protected async Task RunAsync<T>(Func<IProgress<string>, CancellationToken, T> work, Action<T> show, bool clear = true)
    {
        _cts = new CancellationTokenSource();
        StartButton.Text = "Abbrechen";
        StartButton.Kind = ButtonKind.Secondary;
        if (clear) Grid.Rows.Clear();
        UpdateButtons();
        Status.Text = "Läuft …";
        var progress = new Progress<string>(s => { if (_cts != null) Status.Text = s; });
        var token = _cts.Token;
        try
        {
            var result = await Task.Run(() => work(progress, token), token);
            _cts.Dispose();
            _cts = null;
            if (!IsDisposed) show(result);
        }
        catch (OperationCanceledException) { if (!IsDisposed) Status.Text = "Abgebrochen"; }
        catch (Exception ex) { if (!IsDisposed) Status.Text = "Fehler: " + ex.Message; }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (!IsDisposed)
            {
                StartButton.Text = _startText;
                StartButton.Kind = ButtonKind.Primary;
                StartButton.Invalidate();
                UpdateButtons();
            }
        }
    }

    protected abstract Task StartAsync();

    protected virtual void UpdateButtons() { }

    protected List<DataGridViewRow> SelectedRows() => Grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).ToList();

    protected bool CheckFolder(string path)
    {
        if (Directory.Exists(path)) return true;
        Status.Text = path.Length == 0 ? "Bitte zuerst einen Ordner wählen." : "Ordner nicht gefunden: " + path;
        return false;
    }

    protected static string When(DateTime t) => t.ToString("dd.MM.yyyy HH:mm");

    protected int RecycleFiles(IReadOnlyCollection<string> paths)
    {
        int ok = 0;
        var errors = new List<string>();
        foreach (var p in paths)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                ok++;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(p)}: {ex.Message}"); }
        }
        if (errors.Count > 0)
            MessageBox.Show(this, "Nicht alles konnte verschoben werden:\n\n" + string.Join("\n", errors.Take(10)), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return ok;
    }
}

public sealed class DuplicatesForm : ToolWindow
{
    private readonly InputBox _folder;
    private readonly NumberBox _min = new(0, 10_000_000) { Value = 100 };
    private readonly FlatButton _mark;
    private readonly FlatButton _show;
    private readonly FlatButton _recycle;

    public DuplicatesForm(bool gentle) : base("Doppelte Dateien finden", "Suchen", gentle)
    {
        _folder = PathRow("Ordner", file: false);
        Option(Make.Label("Nur Dateien ab", Ui.Base, muted: true));
        Option(_min);
        Option(Make.Label("KB", Ui.Base, muted: true));
        Column("group", "Gruppe", 70, right: true);
        Column("file", "Datei");
        Column("size", "Größe", 100, right: true);
        Column("when", "Geändert", 140);
        _mark = Action("Duplikate markieren", MarkDuplicates);
        _show = Action("Im Explorer zeigen", () => Ui.OpenInExplorer(SelectedEntries()[0].Path, select: true));
        _recycle = Action("In den Papierkorb", Recycle, ButtonKind.Danger);
        Status.Text = "Findet Dateien mit genau gleichem Inhalt – egal wie sie heißen. Es wird nichts ohne Rückfrage gelöscht.";
    }

    protected override Task StartAsync()
    {
        var root = _folder.Text.Trim();
        if (!CheckFolder(root)) return Task.CompletedTask;
        long min = _min.Value * 1024L;
        return RunAsync((p, ct) => FileTools.FindDuplicates(root, min, Gentle, p, ct), Show);
    }

    private void Show(List<DuplicateGroup> groups)
    {
        Grid.SuspendLayout();
        int n = 0;
        foreach (var g in groups.Take(2000))
        {
            n++;
            foreach (var f in g.Files)
            {
                int i = Grid.Rows.Add(n, f.Path, SyncStats.FormatBytes(f.Size), When(f.LastWrite));
                Grid.Rows[i].Tag = (n, f);
                if (n % 2 == 0) Grid.Rows[i].DefaultCellStyle.BackColor = Ui.Blend(Theme.P.Surface, Theme.P.Accent, Theme.P.Dark ? 0.08f : 0.05f);
            }
        }
        Grid.ResumeLayout();
        Grid.ClearSelection();
        long wasted = groups.Sum(g => g.Wasted);
        Status.Text = groups.Count == 0
            ? "Keine doppelten Dateien gefunden."
            : $"{groups.Count:N0} Gruppen mit gleichem Inhalt · {SyncStats.FormatBytes(wasted)} könnten frei werden" + (groups.Count > 2000 ? " · die größten 2000 Gruppen werden angezeigt" : "");
        UpdateButtons();
    }

    private List<FileEntry> SelectedEntries() => SelectedRows().Select(r => r.Tag).OfType<(int, FileEntry)>().Select(t => t.Item2).ToList();

    private void MarkDuplicates()
    {
        Grid.ClearSelection();
        var seen = new HashSet<int>();
        foreach (DataGridViewRow r in Grid.Rows)
            if (r.Tag is (int g, FileEntry) && !seen.Add(g)) r.Selected = true;
    }

    private void Recycle()
    {
        var rows = SelectedRows().Where(r => r.Tag is (int, FileEntry)).ToList();
        if (rows.Count == 0) return;
        var groups = Grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is (int, FileEntry)).GroupBy(r => (((int, FileEntry))r.Tag!).Item1);
        if (groups.Any(g => g.All(r => r.Selected)))
        {
            MessageBox.Show(this, "Von jeder Gruppe muss mindestens eine Datei übrig bleiben. Bitte nicht alle Dateien einer Gruppe auswählen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var paths = rows.Select(r => (((int, FileEntry))r.Tag!).Item2.Path).ToList();
        if (MessageBox.Show(this, $"{paths.Count} Datei(en) in den Papierkorb verschieben?\n\nDu kannst sie von dort jederzeit zurückholen.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        int ok = RecycleFiles(paths);
        foreach (var r in rows.Where(r => !File.Exists((((int, FileEntry))r.Tag!).Item2.Path))) Grid.Rows.Remove(r);
        Status.Text = $"{ok} Datei(en) in den Papierkorb verschoben.";
    }

    protected override void UpdateButtons()
    {
        if (_mark == null) return;
        int n = Busy ? 0 : SelectedEntries().Count;
        _mark.Enabled = !Busy && Grid.Rows.Count > 0;
        _show.Enabled = n == 1;
        _recycle.Enabled = n > 0;
    }
}

public sealed class LargestForm : ToolWindow
{
    private readonly InputBox _folder;
    private readonly NumberBox _count = new(10, 1000) { Value = 100 };
    private readonly FlatButton _show;
    private readonly FlatButton _recycle;

    public LargestForm(bool gentle) : base("Größte Dateien finden", "Suchen", gentle)
    {
        _folder = PathRow("Ordner / Laufwerk", file: false);
        Option(Make.Label("Die größten", Ui.Base, muted: true));
        Option(_count);
        Option(Make.Label("Dateien anzeigen", Ui.Base, muted: true));
        Column("size", "Größe", 100, right: true);
        Column("file", "Datei");
        Column("when", "Geändert", 140);
        _show = Action("Im Explorer zeigen", () => Ui.OpenInExplorer(Selected()[0], select: true));
        _recycle = Action("In den Papierkorb", Recycle, ButtonKind.Danger);
        Status.Text = "Zeigt, welche Dateien am meisten Platz belegen – z. B. bevor die Festplatte voll ist.";
    }

    protected override Task StartAsync()
    {
        var root = _folder.Text.Trim();
        if (!CheckFolder(root)) return Task.CompletedTask;
        int count = _count.Value;
        return RunAsync((p, ct) => FileTools.FindLargest(root, count, Gentle, p, ct), r =>
        {
            foreach (var f in r.Files)
            {
                int i = Grid.Rows.Add(SyncStats.FormatBytes(f.Size), f.Path, When(f.LastWrite));
                Grid.Rows[i].Tag = f.Path;
            }
            Grid.ClearSelection();
            Status.Text = $"{r.TotalFiles:N0} Dateien, zusammen {SyncStats.FormatBytes(r.TotalBytes)} · die {r.Files.Count} größten belegen {SyncStats.FormatBytes(r.Files.Sum(f => f.Size))}";
        });
    }

    private List<string> Selected() => SelectedRows().Select(r => r.Tag).OfType<string>().ToList();

    private void Recycle()
    {
        var rows = SelectedRows().Where(r => r.Tag is string).ToList();
        if (rows.Count == 0) return;
        if (MessageBox.Show(this, $"{rows.Count} Datei(en) in den Papierkorb verschieben?\n\nDu kannst sie von dort jederzeit zurückholen.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        int ok = RecycleFiles(rows.Select(r => (string)r.Tag!).ToList());
        foreach (var r in rows.Where(r => !File.Exists((string)r.Tag!))) Grid.Rows.Remove(r);
        Status.Text = $"{ok} Datei(en) in den Papierkorb verschoben.";
    }

    protected override void UpdateButtons()
    {
        if (_show == null) return;
        int n = Busy ? 0 : Selected().Count;
        _show.Enabled = n == 1;
        _recycle.Enabled = n > 0;
    }
}

public sealed class CompareForm : ToolWindow
{
    private readonly InputBox _left;
    private readonly InputBox _right;
    private readonly ToggleSwitch _content = new("Inhalt genau vergleichen (langsamer)");
    private readonly ToggleSwitch _hideSame = new("Gleiche ausblenden") { Checked = true };
    private readonly FlatButton _showLeft;
    private readonly FlatButton _showRight;
    private List<CompareEntry> _result = [];

    public CompareForm(bool gentle) : base("Ordner vergleichen", "Vergleichen", gentle)
    {
        _left = PathRow("Ordner A", file: false);
        _right = PathRow("Ordner B", file: false);
        Option(_content);
        Option(_hideSame);
        Column("file", "Datei");
        Column("state", "Ergebnis", 150);
        Column("a", "In A", 170);
        Column("b", "In B", 170);
        _showLeft = Action("In A zeigen", () => ShowSelected(true));
        _showRight = Action("In B zeigen", () => ShowSelected(false));
        _hideSame.CheckedChanged += (_, _) => Fill();
        Status.Text = "Prüft z. B., ob eine Kopie oder Sicherung vollständig ist: was fehlt, was anders ist, was gleich ist.";
    }

    protected override Task StartAsync()
    {
        var a = _left.Text.Trim();
        var b = _right.Text.Trim();
        if (!CheckFolder(a) || !CheckFolder(b)) return Task.CompletedTask;
        bool content = _content.Checked;
        return RunAsync((p, ct) => FileTools.CompareFolders(a, b, content, Gentle, p, ct), r => { _result = r; Fill(); if (r.Count == 0) Status.Text = "Beide Ordner sind leer."; });
    }

    private void Fill()
    {
        Grid.Rows.Clear();
        var p = Theme.P;
        foreach (var e in _result.Where(e => !_hideSame.Checked || e.Status != CompareStatus.Same).Take(20_000))
        {
            var (text, color) = e.Status switch
            {
                CompareStatus.OnlyLeft => ("Fehlt in B", p.Warning),
                CompareStatus.OnlyRight => ("Fehlt in A", p.Warning),
                CompareStatus.Different => ("Unterschiedlich", p.Danger),
                _ => ("Gleich", p.Success),
            };
            static string Info(FileEntry? f) => f == null ? "–" : $"{SyncStats.FormatBytes(f.Size)} · {When(f.LastWrite)}";
            int i = Grid.Rows.Add(e.RelativePath, text, Info(e.Left), Info(e.Right));
            Grid.Rows[i].Cells["state"].Style.ForeColor = color;
            Grid.Rows[i].Tag = e;
        }
        Grid.ClearSelection();
        if (_result.Count > 0)
        {
            int Count(CompareStatus s) => _result.Count(e => e.Status == s);
            Status.Text = Count(CompareStatus.OnlyLeft) + Count(CompareStatus.OnlyRight) + Count(CompareStatus.Different) == 0 && _result.Count > 0
                ? $"Beide Ordner sind gleich ({Count(CompareStatus.Same):N0} Dateien)."
                : $"{Count(CompareStatus.OnlyLeft):N0} fehlen in B · {Count(CompareStatus.OnlyRight):N0} fehlen in A · {Count(CompareStatus.Different):N0} unterschiedlich · {Count(CompareStatus.Same):N0} gleich";
        }
        UpdateButtons();
    }

    private void ShowSelected(bool left)
    {
        if (SelectedRows().FirstOrDefault()?.Tag is not CompareEntry e) return;
        var f = left ? e.Left : e.Right;
        if (f != null) Ui.OpenInExplorer(f.Path, select: true);
        else Ui.OpenInExplorer(Path.GetDirectoryName(Path.Combine(left ? _left.Text.Trim() : _right.Text.Trim(), e.RelativePath))!);
    }

    protected override void UpdateButtons()
    {
        if (_showLeft == null) return;
        bool one = !Busy && SelectedRows().Count == 1;
        _showLeft.Enabled = _showRight.Enabled = one;
    }
}

public sealed class HashForm : ToolWindow
{
    private readonly InputBox _file;
    private readonly InputBox _expected = new() { Width = Ui.S(460) };
    private readonly FlatButton _copy;

    public HashForm(bool gentle) : base("Prüfsumme berechnen", "Berechnen", gentle)
    {
        _file = PathRow("Datei", file: true);
        _expected.Box.PlaceholderText = "Erwartete Prüfsumme zum Vergleichen (optional)";
        Option(_expected);
        Column("file", "Datei");
        Column("size", "Größe", 90, right: true);
        Column("hash", "SHA-256", 470);
        Column("result", "Vergleich", 120);
        Grid.MultiSelect = false;
        _copy = Action("Prüfsumme kopieren", () => { if (SelectedRows().FirstOrDefault()?.Tag is string h) Clipboard.SetText(h); Status.Text = "Prüfsumme in die Zwischenablage kopiert."; });
        Status.Text = "Gleiche Prüfsumme = Dateien sind Bit für Bit identisch. Praktisch, um Kopien oder Downloads zu prüfen.";
    }

    protected override Task StartAsync()
    {
        var path = _file.Text.Trim();
        if (!File.Exists(path)) { Status.Text = path.Length == 0 ? "Bitte zuerst eine Datei wählen." : "Datei nicht gefunden: " + path; return Task.CompletedTask; }
        var expected = _expected.Text.Trim();
        return RunAsync((p, ct) => FileTools.HashFile(path, Gentle, p, ct), hash =>
        {
            string result = expected.Length == 0 ? "" : FileTools.HashMatches(hash, expected) ? "✔ stimmt überein" : "✖ stimmt NICHT";
            int i = Grid.Rows.Add(path, SyncStats.FormatBytes(new FileInfo(path).Length), hash, result);
            Grid.Rows[i].Tag = hash;
            if (result.Length > 0) Grid.Rows[i].Cells["result"].Style.ForeColor = result.StartsWith('✔') ? Theme.P.Success : Theme.P.Danger;
            Grid.ClearSelection();
            Grid.Rows[i].Selected = true;
            Status.Text = result.Length == 0 ? "Fertig. Wähle eine weitere Datei, um Prüfsummen zu vergleichen." : $"Fertig – {result.TrimStart('✔', '✖', ' ')}.";
        }, clear: false);
    }

    protected override void UpdateButtons()
    {
        if (_copy == null) return;
        _copy.Enabled = !Busy && SelectedRows().Count == 1;
    }
}
