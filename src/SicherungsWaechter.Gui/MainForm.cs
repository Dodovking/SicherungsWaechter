using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

public sealed class MainForm : Form
{
    private readonly BackupService _service;
    private readonly string? _dataDirArg;
    private readonly Panel _header = new() { Dock = DockStyle.Top };
    private readonly Label _title = Make.Label("SicherungsWächter", Ui.Title);
    private readonly Label _subtitle = Make.Label("", Ui.Base, muted: true);
    private readonly FlatButton _new = new("+ Neuer Auftrag", ButtonKind.Primary);
    private readonly FlatButton _syncAll = new("Jetzt alles sichern");
    private readonly FlatButton _pause = new("Pausieren");
    private readonly FlatButton _settings = new("Einstellungen", ButtonKind.Ghost);
    private readonly FlatButton _update = new("Updates", ButtonKind.Ghost);
    private readonly ToolStripMenuItem _trayUpdate = new("Nach Updates suchen …");
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly UpdateChecker _updateChecker;
    private UpdateInfo? _available;
    private readonly Panel _banner = new() { Dock = DockStyle.Top, Visible = false };
    private readonly Label _bannerText = Make.Label("", Ui.Bold);
    private readonly FlatButton _bannerInstall = new("Jetzt aktualisieren", ButtonKind.Primary);
    private readonly FlatButton _bannerLater = new("Später", ButtonKind.Ghost);
    private Version? _dismissedVersion;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private bool _checkingUpdates;
    private bool _updating;
    private readonly Panel _tabBar = new() { Dock = DockStyle.Top };
    private readonly Segmented _tabs = new("Sicherungsaufträge", "Protokoll");
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly CardList _jobsPage = new();
    private readonly Panel _empty = new();
    private readonly Panel _logPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly LogList _logList = new();
    private readonly Segmented _logFilter = new("Alle Meldungen", "Nur Probleme");
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _trayPause = new("Überwachung pausieren");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly Icon _icon = Ui.AppIcon();
    private Icon? _trayIcon;
    private string _trayState = "";
    private bool _exiting;

    public string[] StartArgs { get; init; } = [];

    public MainForm(BackupService service, string? dataDirArg, string? updateSource = null)
    {
        _service = service;
        _updateChecker = new UpdateChecker(releaseUrl: updateSource);
        _dataDirArg = dataDirArg;
        Text = "SicherungsWächter";
        Icon = _icon;
        Font = Ui.Base;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Ui.S(1000), Ui.S(700));
        MinimumSize = new Size(Ui.S(960), Ui.S(560));
        AllowDrop = true;

        BuildHeader();
        BuildLogPage();
        BuildEmptyState();
        _content.Controls.Add(_jobsPage);
        _content.Controls.Add(_empty);
        _content.Controls.Add(_logPage);
        _content.Padding = new Padding(Ui.S(24), Ui.S(4), Ui.S(24), Ui.S(20));
        Controls.Add(_content);
        Controls.Add(_tabBar);
        Controls.Add(_banner);
        Controls.Add(_header);
        BuildBanner();

        BuildTray();

        _tabs.SelectedIndexChanged += (_, _) => ShowPage();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _timer.Tick += (_, _) => RefreshAll();
        _service.JobsChanged += () => UiInvoke(RebuildCards);
        _service.RunnerChanged += r => UiInvoke(() => UpdateCard(r));
        _service.Problem += (r, s, t) => UiInvoke(() => Notify(r, s, t));
        _service.Log.EntryAdded += e => UiInvoke(() => _logList.Add(e, _logFilter.SelectedIndex == 1));

        Theme.Changed += ApplyTheme;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color)
                UiInvoke(Theme.Refresh);
        };

        _ = Handle;
        ApplyTheme();
        RebuildCards();
        _timer.Start();
        _updateTimer.Interval = 15_000;
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = 60 * 60 * 1000;
            if (_service.Settings.CheckForUpdates) await CheckUpdatesQuietly();
        };
        _updateTimer.Start();
    }

    private void UiInvoke(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(a);
        else a();
    }

    private void BuildHeader()
    {
        _header.Height = Ui.S(84);
        _header.Padding = new Padding(Ui.S(24), 0, Ui.S(24), 0);
        _header.Controls.AddRange([_title, _subtitle, _new, _syncAll, _pause, _update, _settings]);
        _header.Layout += (_, _) =>
        {
            _title.Location = new Point(Ui.S(24), Ui.S(16));
            _subtitle.Location = new Point(Ui.S(26), _title.Bottom + Ui.S(1));
            int x = _header.Width - Ui.S(24);
            foreach (var b in new Control[] { _settings, _update, _pause, _syncAll, _new })
            {
                x -= b.Width;
                b.Location = new Point(x, (_header.Height - b.Height) / 2);
                x -= Ui.S(8);
            }
        };
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.P.Border);
            e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };
        _pause.FitWidth(TextRenderer.MeasureText("Fortsetzen", Ui.Base).Width + Ui.S(28));
        _new.Click += (_, _) => EditJob(null);
        _syncAll.Click += (_, _) => { _service.SyncAllNow(); _subtitle.Text = "Komplettprüfung aller Aufträge gestartet …"; };
        _pause.Click += (_, _) => TogglePause();
        _settings.Click += (_, _) => OpenSettings();
        _update.Click += (_, _) => OpenUpdate();

        _tabBar.Height = Ui.S(62);
        _tabBar.Controls.Add(_tabs);
        _tabs.Location = new Point(Ui.S(24), Ui.S(16));
    }

    private void BuildBanner()
    {
        _banner.Height = Ui.S(56);
        _banner.Controls.AddRange([_bannerText, _bannerInstall, _bannerLater]);
        _banner.Layout += (_, _) =>
        {
            _bannerLater.Location = new Point(_banner.Width - Ui.S(24) - _bannerLater.Width, (_banner.Height - _bannerLater.Height) / 2);
            _bannerInstall.Location = new Point(_bannerLater.Left - Ui.S(8) - _bannerInstall.Width, _bannerLater.Top);
            _bannerText.Location = new Point(Ui.S(26), (_banner.Height - _bannerText.PreferredHeight) / 2);
        };
        _banner.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.P.Border);
            e.Graphics.DrawLine(pen, 0, _banner.Height - 1, _banner.Width, _banner.Height - 1);
            using var bar = new SolidBrush(Theme.P.Accent);
            e.Graphics.FillRectangle(bar, 0, 0, Ui.S(4), _banner.Height);
        };
        _bannerInstall.Click += (_, _) => OpenUpdate();
        _bannerLater.Click += (_, _) => { _dismissedVersion = _available?.Version; _banner.Visible = false; };
    }

    private void BuildEmptyState()
    {
        _empty.Dock = DockStyle.Fill;
        var box = new Card { Size = new Size(Ui.S(560), Ui.S(250)) };
        var t = Make.Label("Noch keine Sicherung eingerichtet", Ui.CardTitle);
        var d = Make.Label("Wähle einen Ordner oder eine Datei aus und eine andere Festplatte als Ziel. " +
                           "Ab dann wird jede neue, geänderte, umbenannte oder gelöschte Datei automatisch gesichert.",
            Ui.Base, muted: true, width: Ui.S(500));
        var b = new FlatButton("+ Ersten Auftrag anlegen", ButtonKind.Primary);
        var h = Make.Label("Tipp: Du kannst Ordner auch einfach in dieses Fenster ziehen.", Ui.Small, muted: true);
        box.Controls.AddRange([t, d, b, h]);
        t.Location = new Point(Ui.S(30), Ui.S(30));
        d.Location = new Point(Ui.S(30), Ui.S(70));
        b.Location = new Point(Ui.S(30), Ui.S(150));
        h.Location = new Point(Ui.S(30), Ui.S(205));
        b.Click += (_, _) => EditJob(null);
        _empty.Controls.Add(box);
        _empty.Resize += (_, _) => box.Location = new Point(Math.Max(0, (_empty.Width - box.Width) / 2), Math.Max(0, (_empty.Height - box.Height) / 3));
    }

    private void BuildLogPage()
    {
        var bottom = new Panel { Dock = DockStyle.Top, Height = Ui.S(48) };
        var openFolder = new FlatButton("Protokollordner öffnen");
        var clear = new FlatButton("Anzeige leeren", ButtonKind.Ghost);
        bottom.Controls.AddRange([_logFilter, openFolder, clear]);
        bottom.Layout += (_, _) =>
        {
            _logFilter.Location = new Point(0, 0);
            clear.Location = new Point(bottom.Width - clear.Width, 0);
            openFolder.Location = new Point(clear.Left - Ui.S(8) - openFolder.Width, 0);
        };
        openFolder.Click += (_, _) => Ui.OpenInExplorer(_service.Log.LogDirectory ?? _service.DataDirectory);
        clear.Click += (_, _) => { _service.Log.Clear(); _logList.Reload([], false); };
        _logFilter.SelectedIndexChanged += (_, _) => _logList.Reload(_service.Log.Snapshot(), _logFilter.SelectedIndex == 1);
        var frame = new Card { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(2), Ui.S(6), Ui.S(2), Ui.S(6)) };
        _logList.Dock = DockStyle.Fill;
        frame.Controls.Add(_logList);
        _logPage.Controls.Add(frame);
        _logPage.Controls.Add(bottom);
        _logList.Reload(_service.Log.Snapshot(), false);
    }

    private void BuildTray()
    {
        var open = new ToolStripMenuItem("Öffnen", null, (_, _) => ShowWindow()) { Font = new Font(Ui.Base, FontStyle.Bold) };
        var sync = new ToolStripMenuItem("Jetzt alles sichern", null, (_, _) => _service.SyncAllNow());
        _trayPause.Click += (_, _) => TogglePause();
        var exit = new ToolStripMenuItem("Beenden", null, (_, _) => ExitApp());
        _trayUpdate.Click += (_, _) => { ShowWindow(); OpenUpdate(); };
        _trayMenu.Items.AddRange([open, sync, _trayPause, _trayUpdate, new ToolStripSeparator(), exit]);
        _trayMenu.Font = Ui.Base;
        _tray.ContextMenuStrip = _trayMenu;
        _tray.Icon = _icon;
        _tray.Text = "SicherungsWächter";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => ShowWindow();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
    }

    public void ShowWindow()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        RefreshAll();
        if (_service.Settings.CheckForUpdates && DateTime.Now - _lastUpdateCheck > TimeSpan.FromMinutes(10))
            _ = CheckUpdatesQuietly();
    }

    private void ExitApp()
    {
        var working = _service.Runners.Any(r => r.State == JobState.Working);
        if (MessageBox.Show(this.Visible ? this : null,
                (working ? "Gerade läuft eine Sicherung. " : "") +
                "Wenn du SicherungsWächter beendest, werden Änderungen nicht mehr automatisch gesichert.\n\nTrotzdem beenden?",
                "SicherungsWächter beenden", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _exiting = true;
        _tray.Visible = false;
        Close();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_service.Settings.TrayHintShown)
            {
                _service.Settings.TrayHintShown = true;
                _service.Save();
                _tray.ShowBalloonTip(5000, "SicherungsWächter läuft weiter",
                    "Die Überwachung läuft im Hintergrund. Über das Symbol neben der Uhr öffnest du das Fenster wieder.", ToolTipIcon.Info);
            }
            return;
        }
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    private void ApplyTheme()
    {
        Theme.Apply(this);
        _header.BackColor = Theme.P.Surface;
        _tabBar.BackColor = Theme.P.Back;
        _banner.BackColor = Ui.Blend(Theme.P.Back, Theme.P.Accent, Theme.P.Dark ? 0.22f : 0.10f);
        _bannerText.ForeColor = Theme.P.Text;
        _content.BackColor = Theme.P.Back;
        _title.ForeColor = Theme.P.Text;
        _subtitle.ForeColor = Theme.P.Muted;
        StyleMenu(_trayMenu);
        foreach (Control c in _jobsPage.Controls) c.Invalidate();
        Invalidate(true);
    }

    public static void StyleMenu(ToolStrip menu)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };
        menu.BackColor = Theme.P.Surface;
        menu.ForeColor = Theme.P.Text;
        foreach (ToolStripItem i in menu.Items) i.ForeColor = Theme.P.Text;
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.P.Surface;
        public override Color ImageMarginGradientBegin => Theme.P.Surface;
        public override Color ImageMarginGradientMiddle => Theme.P.Surface;
        public override Color ImageMarginGradientEnd => Theme.P.Surface;
        public override Color MenuBorder => Theme.P.Border;
        public override Color MenuItemBorder => Theme.P.Selection;
        public override Color MenuItemSelected => Theme.P.Selection;
        public override Color SeparatorDark => Theme.P.Border;
        public override Color SeparatorLight => Theme.P.Surface;
    }

    private void ShowPage()
    {
        bool log = _tabs.SelectedIndex == 1;
        _logPage.Visible = log;
        bool hasJobs = _service.Runners.Count > 0;
        _jobsPage.Visible = !log && hasJobs;
        _empty.Visible = !log && !hasJobs;
        if (log) _logList.ScrollToEnd();
    }

    private void RebuildCards()
    {
        var runners = _service.Runners;
        var existing = _jobsPage.Controls.OfType<JobCard>().ToList();
        _jobsPage.SuspendLayout();
        foreach (var card in existing)
        {
            if (!runners.Contains(card.Runner))
            {
                var replacement = runners.FirstOrDefault(r => r.Job.Id == card.Runner.Job.Id);
                if (replacement != null) card.SetRunner(replacement);
                else { _jobsPage.Controls.Remove(card); card.Dispose(); }
            }
        }
        var cards = _jobsPage.Controls.OfType<JobCard>().ToList();
        for (int i = 0; i < runners.Count; i++)
        {
            var card = cards.FirstOrDefault(c => c.Runner == runners[i]);
            if (card == null)
            {
                card = new JobCard(runners[i]);
                card.SyncClicked += c => { c.Runner.RequestFullSync(); };
                card.OpenClicked += c => OpenBackup(c.Runner.Job);
                card.RestoreClicked += c => new RestoreForm(c.Runner.Job).ShowDialog(this);
                card.EditClicked += c => EditJob(c.Runner.Job);
                card.ToggleClicked += c => ToggleJob(c.Runner.Job);
                card.RemoveClicked += c => RemoveJob(c.Runner.Job);
                _jobsPage.Controls.Add(card);
            }
            _jobsPage.Controls.SetChildIndex(card, i);
        }
        _jobsPage.ResumeLayout(true);
        ShowPage();
        RefreshAll();
    }

    private void UpdateCard(JobRunner r)
    {
        foreach (var c in _jobsPage.Controls.OfType<JobCard>())
            if (c.Runner == r) c.RefreshInfo();
        RefreshSummary();
    }

    private void RefreshAll()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
            foreach (var c in _jobsPage.Controls.OfType<JobCard>()) c.RefreshInfo();
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var runners = _service.Runners.Where(r => r.Job.Enabled).ToList();
        string text;
        string state;
        if (_service.Paused) { text = "Überwachung pausiert – Änderungen werden gerade nicht gesichert"; state = "pause"; }
        else if (runners.Count == 0) { text = _service.Runners.Count == 0 ? "Lege einen Auftrag an, um mit der Sicherung zu beginnen" : "Alle Aufträge sind ausgeschaltet"; state = "pause"; }
        else
        {
            int problems = runners.Count(r => r.State is JobState.Error or JobState.TargetMissing or JobState.SourceMissing);
            int working = runners.Count(r => r.State is JobState.Working or JobState.Starting);
            if (problems > 0) { text = $"{problems} von {runners.Count} Aufträgen brauchen Aufmerksamkeit"; state = "warn"; }
            else if (working > 0) { text = "Sicherung läuft …"; state = "ok"; }
            else { text = $"Alles gesichert · {runners.Count} Auftrag{(runners.Count == 1 ? "" : "e")} wird überwacht"; state = "ok"; }
        }
        _subtitle.Text = text;
        _pause.Text = _service.Paused ? "Fortsetzen" : "Pausieren";
        _pause.Kind = _service.Paused ? ButtonKind.Primary : ButtonKind.Secondary;
        _trayPause.Text = _service.Paused ? "Überwachung fortsetzen" : "Überwachung pausieren";
        var tip = "SicherungsWächter – " + text;
        _tray.Text = tip.Length > 63 ? tip[..62] + "…" : tip;
        if (state != _trayState)
        {
            _trayState = state;
            var old = _trayIcon;
            _trayIcon = state == "ok" ? null : OverlayIcon(state == "warn" ? Color.FromArgb(245, 158, 11) : Color.FromArgb(148, 163, 184));
            _tray.Icon = _trayIcon ?? _icon;
            old?.Dispose();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private Icon OverlayIcon(Color dot)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var src = new Icon(_icon, size, size);
            g.DrawIcon(src, new Rectangle(0, 0, size, size));
            int d = size / 2;
            using var b = new SolidBrush(dot);
            using var pen = new Pen(Color.White, Math.Max(1, size / 16f));
            g.FillEllipse(b, size - d - 1, size - d - 1, d, d);
            g.DrawEllipse(pen, size - d - 1, size - d - 1, d, d);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    private void Notify(JobRunner r, JobState s, string text)
    {
        UpdateCard(r);
        if (!_service.Settings.ShowNotifications) return;
        var icon = s == JobState.Error ? ToolTipIcon.Error : ToolTipIcon.Warning;
        var msg = s switch
        {
            JobState.TargetMissing => "Die Sicherungsfestplatte ist nicht angeschlossen. Änderungen werden nachgeholt, sobald sie wieder da ist.",
            JobState.SourceMissing => "Der zu sichernde Ordner ist nicht erreichbar.",
            _ => text,
        };
        _tray.ShowBalloonTip(6000, r.Job.DisplayName, msg, icon);
    }

    private void TogglePause() => UiInvoke(() => { _service.SetPaused(!_service.Paused); RefreshAll(); });

    private void OpenBackup(BackupJob job)
    {
        try
        {
            var l = JobLayout.For(job);
            if (l.IsFile && File.Exists(Path.Combine(l.MirrorRoot, l.Label))) Ui.OpenInExplorer(Path.Combine(l.MirrorRoot, l.Label), select: true);
            else Ui.OpenInExplorer(l.MirrorRoot);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "SicherungsWächter"); }
    }

    private void EditJob(BackupJob? job, string? presetSource = null)
    {
        using var f = new JobEditorForm(job, _service.Settings.Jobs, presetSource);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _service.AddOrUpdateJob(f.Result);
        _tabs.SelectedIndex = 0;
    }

    private void ToggleJob(BackupJob job)
    {
        var copy = job.Clone();
        copy.Enabled = !copy.Enabled;
        _service.AddOrUpdateJob(copy);
    }

    private void RemoveJob(BackupJob job)
    {
        if (MessageBox.Show(this,
                $"Auftrag „{job.DisplayName}“ entfernen?\n\nDie bereits gesicherten Dateien bleiben auf der Sicherungsfestplatte erhalten, es wird nur nicht mehr weiter gesichert.",
                "Auftrag entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            _service.RemoveJob(job.Id);
    }

    private async Task CheckUpdatesQuietly()
    {
        if (_checkingUpdates || _updating) return;
        _checkingUpdates = true;
        _lastUpdateCheck = DateTime.Now;
        UpdateInfo? info;
        try { info = await _updateChecker.GetLatestAsync(); }
        catch (Exception e) when (e is UpdateException or OperationCanceledException) { return; }
        finally { _checkingUpdates = false; }
        if (info == null || !info.IsNewerThan(Updater.CurrentVersion) || IsDisposed) return;
        bool fresh = _available?.Version != info.Version;
        SetAvailable(info);
        if (fresh && _service.Settings.ShowNotifications)
            _tray.ShowBalloonTip(8000, $"Update {info.Version.ToString(3)} verfügbar",
                "Klicke oben im Fenster auf „Update“, um es zu installieren. Das dauert nur ein paar Sekunden.", ToolTipIcon.Info);
    }

    private void SetAvailable(UpdateInfo? info)
    {
        _available = info != null && info.IsNewerThan(Updater.CurrentVersion) ? info : null;
        _update.Text = _available == null ? "Updates" : $"Update {_available.Version.ToString(3)}";
        _update.Kind = _available == null ? ButtonKind.Ghost : ButtonKind.Primary;
        _update.FitWidth();
        _update.Invalidate();
        if (_available != null)
            _bannerText.Text = $"Neue Version {_available.Version.ToString(3)} verfügbar  ·  Installiert: {Updater.CurrentVersion.ToString(3)}";
        _banner.Visible = _available != null && _available.Version != _dismissedVersion;
        _banner.PerformLayout();
        _trayUpdate.Text = _available == null ? "Nach Updates suchen …" : $"Update {_available.Version.ToString(3)} installieren …";
        _header.PerformLayout();
    }

    private void OpenUpdate()
    {
        if (_updating) return;
        using var f = new UpdateForm(_updateChecker, _available);
        var result = f.ShowDialog(this);
        SetAvailable(f.Info);
        if (result == DialogResult.OK && f.Info != null) PerformUpdate(f.Info);
    }

    private void PerformUpdate(UpdateInfo info)
    {
        _updating = true;
        _service.Log.Write(LogLevel.Info, "", $"Update auf Version {info.Version.ToString(3)} – Programm startet neu");
        try
        {
            Updater.Swap();
            Updater.StartUpdated(StartArgs);
        }
        catch (Exception e)
        {
            try { Updater.Rollback(); } catch { }
            _updating = false;
            _service.Log.Write(LogLevel.Error, "", "Update fehlgeschlagen: " + e.Message);
            MessageBox.Show(this, "Das Update konnte nicht installiert werden:\n\n" + e.Message + "\n\nDie bisherige Version läuft unverändert weiter.",
                "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _exiting = true;
        _tray.Visible = false;
        Close();
        Application.Exit();
    }

    public void ShowUpdatedHint() =>
        _tray.ShowBalloonTip(6000, "SicherungsWächter wurde aktualisiert",
            $"Jetzt läuft Version {Updater.CurrentVersion.ToString(3)}. Deine Sicherungen laufen weiter wie bisher.", ToolTipIcon.Info);

    private void OpenSettings()
    {
        using var f = new SettingsForm(_service.Settings, _service.DataDirectory);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _service.Settings.StartWithWindows = f.StartWithWindows;
        _service.Settings.ShowNotifications = f.ShowNotifications;
        _service.Settings.FullScanIntervalMinutes = f.FullScanMinutes;
        _service.Settings.Theme = f.ThemeChoice;
        _service.Settings.CheckForUpdates = f.CheckForUpdates;
        _service.Settings.SaveResources = f.SaveResources;
        _service.Save();
        Autostart.Set(f.StartWithWindows, _dataDirArg);
        Theme.Set(f.ThemeChoice);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        BeginInvoke(() =>
        {
            foreach (var p in paths.Take(5))
                EditJob(null, p);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _updateTimer.Dispose();
            _trayIcon?.Dispose();
            Theme.Changed -= ApplyTheme;
        }
        base.Dispose(disposing);
    }
}

/// <summary>Vertical list of job cards that fill the available width.</summary>
public sealed class CardList : Panel
{
    public CardList()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int y = AutoScrollPosition.Y;
        int w = ClientSize.Width - Ui.S(2);
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            c.SetBounds(0, y, w, c.Height);
            y += c.Height + Ui.S(14);
        }
        AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y);
        base.OnLayout(levent);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        PerformLayout();
    }
}

public sealed class LogList : ListBox
{
    public LogList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Ui.S(26);
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Font = Ui.Base;
        HorizontalScrollbar = false;
        SelectionMode = SelectionMode.One;
    }

    public void Reload(IEnumerable<LogEntry> entries, bool onlyProblems)
    {
        BeginUpdate();
        Items.Clear();
        foreach (var e in entries)
            if (!onlyProblems || e.Level is LogLevel.Warning or LogLevel.Error) Items.Add(e);
        EndUpdate();
        ScrollToEnd();
    }

    public void Add(LogEntry e, bool onlyProblems)
    {
        if (onlyProblems && e.Level is not (LogLevel.Warning or LogLevel.Error)) return;
        bool atEnd = Items.Count == 0 || TopIndex + ClientSize.Height / ItemHeight >= Items.Count - 1;
        Items.Add(e);
        while (Items.Count > 3000) Items.RemoveAt(0);
        if (atEnd) ScrollToEnd();
    }

    public void ScrollToEnd()
    {
        if (Items.Count > 0) TopIndex = Math.Max(0, Items.Count - Math.Max(1, ClientSize.Height / ItemHeight));
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(selected ? p.Selection : p.Surface))
            g.FillRectangle(b, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not LogEntry entry) return;
        var color = entry.Level switch
        {
            LogLevel.Success => p.Success,
            LogLevel.Warning => p.Warning,
            LogLevel.Error => p.Danger,
            _ => p.Muted,
        };
        int x = e.Bounds.X + Ui.S(12);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, entry.Time.ToString(entry.Time.Date == DateTime.Today ? "HH:mm:ss" : "dd.MM. HH:mm"), Font,
            new Rectangle(x, e.Bounds.Y, Ui.S(90), e.Bounds.Height), p.Muted, flags);
        x += Ui.S(92);
        using (var dot = new SolidBrush(color))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(dot, x, e.Bounds.Y + e.Bounds.Height / 2 - Ui.S(4), Ui.S(8), Ui.S(8));
        }
        x += Ui.S(18);
        if (entry.Job.Length > 0)
        {
            var w = Math.Min(Ui.S(200), TextRenderer.MeasureText(entry.Job, Ui.Bold).Width + Ui.S(4));
            TextRenderer.DrawText(g, entry.Job, Ui.Bold, new Rectangle(x, e.Bounds.Y, w, e.Bounds.Height), p.Text, flags | TextFormatFlags.EndEllipsis);
            x += w + Ui.S(8);
        }
        TextRenderer.DrawText(g, entry.Message, Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x - Ui.S(8), e.Bounds.Height),
            entry.Level is LogLevel.Error or LogLevel.Warning ? color : p.Text, flags | TextFormatFlags.EndEllipsis);
    }
}
