using System.Drawing.Drawing2D;
using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

public sealed class JobCard : Card
{
    private readonly FlatButton _sync = new("Jetzt sichern", ButtonKind.Primary);
    private readonly FlatButton _open = new("Sicherung öffnen");
    private readonly FlatButton _restore = new("Wiederherstellen …");
    private readonly FlatButton _edit = new("Bearbeiten");
    private readonly FlatButton _toggle = new("Ausschalten");
    private readonly FlatButton _remove = new("Entfernen", ButtonKind.Danger);
    private string _freeText = "";
    private bool _lowSpace;

    public JobRunner Runner { get; private set; }

    public event Action<JobCard>? SyncClicked;
    public event Action<JobCard>? OpenClicked;
    public event Action<JobCard>? RestoreClicked;
    public event Action<JobCard>? EditClicked;
    public event Action<JobCard>? ToggleClicked;
    public event Action<JobCard>? RemoveClicked;

    public JobCard(JobRunner runner)
    {
        Runner = runner;
        Height = Ui.S(176);
        foreach (var b in new[] { _sync, _open, _restore, _edit, _toggle, _remove }) Controls.Add(b);
        _sync.Click += (_, _) => SyncClicked?.Invoke(this);
        _open.Click += (_, _) => OpenClicked?.Invoke(this);
        _restore.Click += (_, _) => RestoreClicked?.Invoke(this);
        _edit.Click += (_, _) => EditClicked?.Invoke(this);
        _toggle.Click += (_, _) => ToggleClicked?.Invoke(this);
        _remove.Click += (_, _) => RemoveClicked?.Invoke(this);
        _toggle.FitWidth(TextRenderer.MeasureText("Einschalten", Ui.Base).Width + Ui.S(28));
        RefreshInfo();
    }

    public void SetRunner(JobRunner runner)
    {
        Runner = runner;
        RefreshInfo();
    }

    public void RefreshInfo()
    {
        _toggle.Text = Runner.Job.Enabled ? "Ausschalten" : "Einschalten";
        _sync.Enabled = Runner.Job.Enabled && Runner.State is not JobState.Paused;
        _lowSpace = false;
        try
        {
            var root = PathRules.DriveRoot(Runner.Job.Target);
            var di = root != null ? new DriveInfo(root) : null;
            _freeText = di is { IsReady: true }
                ? $"{root!.TrimEnd('\\')} {(string.IsNullOrEmpty(di.VolumeLabel) ? "" : "„" + di.VolumeLabel + "“ ")}– {SyncStats.FormatBytes(di.AvailableFreeSpace)} frei von {SyncStats.FormatBytes(di.TotalSize)}"
                : "";
            if (di is { IsReady: true } && FileTools.IsLowSpace(di.AvailableFreeSpace, di.TotalSize))
            {
                _lowSpace = true;
                _freeText += " – wird knapp!";
            }
        }
        catch { _freeText = ""; }
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = Ui.S(18);
        int y = Height - pad - _sync.Height;
        int x = pad + Ui.S(6);
        foreach (var b in new[] { _sync, _open, _restore, _edit })
        {
            b.Location = new Point(x, y);
            x += b.Width + Ui.S(8);
        }
        int rx = Width - pad;
        foreach (var b in new[] { _remove, _toggle })
        {
            rx -= b.Width;
            b.Location = new Point(rx, y);
            rx -= Ui.S(8);
        }
    }

    protected override void PaintContent(Graphics g)
    {
        var p = Theme.P;
        var job = Runner.Job;
        int pad = Ui.S(18);
        int left = pad + Ui.S(6);
        var stateColor = job.Enabled ? Ui.StateColor(Runner.State) : p.Muted;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bar = new SolidBrush(stateColor))
        using (var path = Ui.Rounded(new Rectangle(Ui.S(1), Ui.S(12), Ui.S(4), Height - Ui.S(24)), Ui.S(2)))
            g.FillPath(bar, path);

        int y = pad - Ui.S(2);
        var statusText = job.Enabled ? Runner.StatusText : "Ausgeschaltet";
        var statusSize = TextRenderer.MeasureText(statusText, Ui.Base, Size.Empty, TextFormatFlags.NoPrefix);
        var pill = new Rectangle(Width - pad - statusSize.Width - Ui.S(30), y, statusSize.Width + Ui.S(30), Ui.S(26));
        using (var pb = new SolidBrush(Ui.Blend(p.Surface, stateColor, p.Dark ? 0.22f : 0.12f)))
        using (var path = Ui.Rounded(pill, Ui.S(13)))
            g.FillPath(pb, path);
        using (var dot = new SolidBrush(stateColor))
            g.FillEllipse(dot, pill.X + Ui.S(10), pill.Y + pill.Height / 2 - Ui.S(4), Ui.S(8), Ui.S(8));
        TextRenderer.DrawText(g, statusText, Ui.Base, new Rectangle(pill.X + Ui.S(22), pill.Y, pill.Width - Ui.S(24), pill.Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        var titleRect = new Rectangle(left, y, pill.X - left - Ui.S(10), Ui.S(26));
        TextRenderer.DrawText(g, job.DisplayName, Ui.CardTitle, titleRect, p.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        y += Ui.S(34);

        string mirror;
        try
        {
            var l = JobLayout.For(job);
            mirror = l.IsFile ? Path.Combine(l.MirrorRoot, l.Label) : l.MirrorRoot;
        }
        catch { mirror = job.Target; }

        int labelW = Ui.S(70);
        int lineH = Ui.S(21);
        void Line(string label, string value, Color color)
        {
            TextRenderer.DrawText(g, label, Ui.Base, new Rectangle(left, y, labelW, lineH), p.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, value, Ui.Base, new Rectangle(left + labelW, y, Width - left - labelW - pad, lineH), color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis);
            y += lineH;
        }
        Line(job.SourceIsFile ? "Datei" : "Ordner", job.Source, p.Text);
        Line("Ziel", mirror + (_freeText.Length > 0 ? "   ·   " + _freeText : ""), _lowSpace ? p.Warning : p.Text);
        var info = $"Letzte Sicherung {Ui.Relative(Runner.LastBackup)}";
        if (Runner.FilesSaved > 0) info += $"   ·   {Runner.FilesSaved} Datei{(Runner.FilesSaved == 1 ? "" : "en")} seit Programmstart gesichert";
        if (Runner.PendingCount > 0) info += $"   ·   {Runner.PendingCount} Änderung(en) in Warteschlange";
        Line("Status", info, p.Muted);
    }
}
