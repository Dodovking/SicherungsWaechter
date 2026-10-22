using System.Drawing.Drawing2D;
using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

/// <summary>Checks GitHub, downloads and verifies the new EXE. DialogResult.OK means: ready to swap and restart.</summary>
public sealed class UpdateForm : DialogBase
{
    private readonly UpdateChecker _checker;
    private UpdateInfo? _info;
    private readonly Label _heading;
    private readonly Label _versions;
    private readonly ProgressLine _progress = new();
    private readonly Label _status;
    private readonly Label _notesHeading;
    private readonly Label _notes;
    private readonly CancellationTokenSource _cts = new();
    private bool _busy;

    public UpdateInfo? Info => _info;

    public UpdateForm(UpdateChecker checker, UpdateInfo? known) : base("Update", "Jetzt aktualisieren", 560, 430)
    {
        _checker = checker;
        _info = known;
        _heading = AddHeading("Suche nach Updates …");
        _versions = AddHint($"Installiert: Version {Updater.CurrentVersion.ToString(3)}");
        _progress.Size = new Size(ContentWidth, Ui.S(8));
        Place(_progress, _progress.Height, 6);
        _progress.Visible = false;
        _status = AddHint("Beim Update wird SicherungsWächter kurz geschlossen, die Programmdatei ausgetauscht und sofort neu gestartet. Deine Aufträge, Einstellungen und Sicherungen bleiben unverändert.");
        _notesHeading = AddHeading("Was ist neu");
        _notes = AddHint("");
        _notesHeading.Visible = false;
        FinishLayout();
        OkButton.Enabled = false;
        OkButton.Click += async (_, _) => await InstallAsync();
        AcceptButton = OkButton;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_info == null) await CheckAsync();
        else ShowInfo();
    }

    private async Task CheckAsync()
    {
        try
        {
            _info = await _checker.GetLatestAsync(_cts.Token);
        }
        catch (UpdateException ex)
        {
            Finish("Update-Suche fehlgeschlagen", ex.Message);
            return;
        }
        catch (OperationCanceledException) { return; }
        if (IsDisposed) return;
        ShowInfo();
    }

    private void ShowInfo()
    {
        if (_info == null || !_info.IsNewerThan(Updater.CurrentVersion))
        {
            Finish("Du hast die neueste Version", _info == null
                ? "Auf GitHub ist noch keine Version veröffentlicht."
                : $"Version {Updater.CurrentVersion.ToString(3)} ist aktuell.");
            return;
        }
        _heading.Text = $"Version {_info.Version.ToString(3)} ist verfügbar";
        _versions.Text = $"Installiert: Version {Updater.CurrentVersion.ToString(3)}   →   Neu: Version {_info.Version.ToString(3)}"
                         + (_info.ExeSize > 0 ? $"   ({_info.ExeSize / 1024.0 / 1024.0:0} MB)" : "");
        if (_info.Notes.Length > 0)
        {
            _notesHeading.Visible = true;
            _notes.Text = _info.Notes.Length > 2000 ? _info.Notes[..2000] + " …" : _info.Notes;
        }
        OkButton.Enabled = true;
        OkButton.Focus();
    }

    private void Finish(string heading, string text)
    {
        _heading.Text = heading;
        _status.Text = text;
        _progress.Visible = false;
        OkButton.Visible = false;
        CancelBtn.Text = "Schließen";
        CancelBtn.FitWidth();
        Footer.PerformLayout();
    }

    private async Task InstallAsync()
    {
        if (_info == null || _busy) return;
        var problem = Updater.CheckWritable();
        if (problem != null)
        {
            MessageBox.Show(this, problem, "Update nicht möglich", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _busy = true;
        OkButton.Enabled = false;
        _progress.Visible = true;
        _status.Text = "Lade die neue Version herunter …";
        var target = Updater.NewExePath;
        try
        {
            var progress = new Progress<(long done, long total)>(p =>
            {
                if (IsDisposed) return;
                _progress.Value = p.total > 0 ? (float)p.done / p.total : 0;
                _status.Text = p.total > 0
                    ? $"Lade die neue Version herunter … {p.done / 1048576.0:0} von {p.total / 1048576.0:0} MB"
                    : $"Lade die neue Version herunter … {p.done / 1048576.0:0} MB";
            });
            await _checker.DownloadAsync(_info, target, progress, _cts.Token);
            _status.Text = "Prüfe die neue Version …";
            _progress.Value = 1;
            await Task.Run(() => Updater.VerifyStarts(target), _cts.Token);
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            TryDelete(target);
            _busy = false;
            _progress.Visible = false;
            _status.Text = ex.Message;
            OkButton.Enabled = true;
            return;
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            return;
        }
        _status.Text = "Starte neu …";
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void TryDelete(string f)
    {
        try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) _cts.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}

public sealed class ProgressLine : Control, IThemed
{
    private float _value;

    public ProgressLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
    }

    public float Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int r = Height / 2;
        using (var track = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), r))
        using (var b = new SolidBrush(Theme.P.Border))
            g.FillPath(b, track);
        int w = (int)((Width - 1) * _value);
        if (w < Height) return;
        using var bar = Ui.Rounded(new Rectangle(0, 0, w, Height - 1), r);
        using var ab = new SolidBrush(Theme.P.Accent);
        g.FillPath(ab, bar);
    }
}
