using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains(Updater.SelfTestArg, StringComparer.OrdinalIgnoreCase))
        {
            _ = typeof(BackupService).Assembly.GetName();
            using (new Label()) { }
            Environment.Exit(Updater.SelfTestExitCode);
        }
        bool afterUpdate = Updater.WaitForPreviousInstance(args);
        bool minimized = args.Contains("--minimiert", StringComparer.OrdinalIgnoreCase);
        string? dataDir = ArgValue(args, "--daten");
        string? design = ArgValue(args, "--design");

        var instanceKey = "SicherungsWaechter_" + (dataDir == null ? "standard" : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant())))[..12]);
        using var mutex = new Mutex(true, @"Local\" + instanceKey, out bool firstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + instanceKey + "_Zeigen");
        if (!firstInstance)
        {
            showEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        LowImpact.OnBattery = () => SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        var store = new SettingsStore(dataDir);
        bool firstRun = !File.Exists(store.SettingsFile);
        using var service = new BackupService(store);
        Updater.CleanupLeftovers();
        if (afterUpdate) service.Log.Write(LogLevel.Info, "", $"Aktualisiert auf Version {Updater.CurrentVersion.ToString(3)}");

        Application.ThreadException += (_, e) =>
        {
            service.Log.Write(LogLevel.Error, "", "Programmfehler: " + e.Exception);
            MessageBox.Show("Ein unerwarteter Fehler ist aufgetreten:\n\n" + e.Exception.Message, "SicherungsWächter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            service.Log.Write(LogLevel.Error, "", "Schwerer Programmfehler: " + e.ExceptionObject);

        Theme.Set(design?.ToLowerInvariant() switch
        {
            "hell" => ThemeChoice.Hell,
            "dunkel" => ThemeChoice.Dunkel,
            _ => service.Settings.Theme,
        });

        if (firstRun)
        {
            service.Save();
            Autostart.Set(service.Settings.StartWithWindows, dataDir);
        }
        else if (service.Settings.StartWithWindows)
        {
            Autostart.Set(true, dataDir);
        }

        using var form = new MainForm(service, dataDir, Updater.SourceUrl(args)) { StartArgs = args };
        var waiter = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                try { form.BeginInvoke(form.ShowWindow); } catch { return; }
            }
        }) { IsBackground = true };
        waiter.Start();

        service.Start();
        if (!minimized) form.Show();
        if (afterUpdate) form.ShowUpdatedHint();
        Application.Run();
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
