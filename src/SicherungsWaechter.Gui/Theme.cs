using System.Runtime.InteropServices;
using Microsoft.Win32;
using SicherungsWaechter.Core;

namespace SicherungsWaechter.Gui;

public sealed record Palette(
    bool Dark,
    Color Back,
    Color Surface,
    Color SurfaceHover,
    Color Border,
    Color Text,
    Color Muted,
    Color Accent,
    Color AccentHover,
    Color AccentText,
    Color Success,
    Color Warning,
    Color Danger,
    Color Input,
    Color Selection);

public static class Theme
{
    public static readonly Palette Light = new(false,
        Back: Color.FromArgb(243, 244, 246), Surface: Color.White, SurfaceHover: Color.FromArgb(243, 244, 246),
        Border: Color.FromArgb(220, 223, 228), Text: Color.FromArgb(17, 24, 39), Muted: Color.FromArgb(107, 114, 128),
        Accent: Color.FromArgb(37, 99, 235), AccentHover: Color.FromArgb(29, 78, 216), AccentText: Color.White,
        Success: Color.FromArgb(22, 163, 74), Warning: Color.FromArgb(217, 119, 6), Danger: Color.FromArgb(220, 38, 38),
        Input: Color.White, Selection: Color.FromArgb(219, 234, 254));

    public static readonly Palette DarkPalette = new(true,
        Back: Color.FromArgb(17, 19, 24), Surface: Color.FromArgb(27, 30, 37), SurfaceHover: Color.FromArgb(38, 42, 52),
        Border: Color.FromArgb(48, 53, 66), Text: Color.FromArgb(229, 231, 235), Muted: Color.FromArgb(156, 163, 175),
        Accent: Color.FromArgb(59, 130, 246), AccentHover: Color.FromArgb(96, 155, 250), AccentText: Color.White,
        Success: Color.FromArgb(74, 222, 128), Warning: Color.FromArgb(251, 191, 36), Danger: Color.FromArgb(248, 113, 113),
        Input: Color.FromArgb(22, 25, 31), Selection: Color.FromArgb(30, 58, 110));

    public static Palette P { get; private set; } = Light;
    public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;
    public static event Action? Changed;

    public static void Set(ThemeChoice choice)
    {
        Choice = choice;
        var next = choice switch
        {
            ThemeChoice.Dunkel => DarkPalette,
            ThemeChoice.Hell => Light,
            _ => SystemPrefersDark() ? DarkPalette : Light,
        };
        if (next == P) return;
        P = next;
        Changed?.Invoke();
    }

    public static void Refresh() => Set(Choice);

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Colours standard controls; custom controls paint themselves from <see cref="P"/>.</summary>
    public static void Apply(Control root)
    {
        ApplyOne(root);
        foreach (Control c in root.Controls) Apply(c);
    }

    private static void ApplyOne(Control c)
    {
        switch (c)
        {
            case Form f:
                f.BackColor = P.Back;
                f.ForeColor = P.Text;
                if (f.IsHandleCreated) ApplyTitleBar(f);
                break;
            case TextBox t:
                t.BackColor = P.Input;
                t.ForeColor = P.Text;
                DarkScroll(t);
                break;
            case ListBox l:
                l.BackColor = P.Surface;
                l.ForeColor = P.Text;
                DarkScroll(l);
                break;
            case DataGridView g:
                StyleGrid(g);
                break;
            case IThemed:
                c.Invalidate();
                break;
            case Label l:
                if (l.Tag is not "keepcolor") l.ForeColor = l.Tag is "muted" ? P.Muted : P.Text;
                l.BackColor = Color.Transparent;
                break;
            case ScrollableControl s:
                if (s.Tag is "surface") s.BackColor = P.Surface;
                else if (s.Tag is "input") s.BackColor = P.Input;
                else if (s.Parent != null) s.BackColor = s.Parent.BackColor;
                if (s.AutoScroll) DarkScroll(s);
                break;
        }
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = P.Surface;
        g.GridColor = P.Border;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = P.Surface;
        g.ColumnHeadersDefaultCellStyle.ForeColor = P.Muted;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = P.Surface;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = P.Muted;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.DefaultCellStyle.BackColor = P.Surface;
        g.DefaultCellStyle.ForeColor = P.Text;
        g.DefaultCellStyle.SelectionBackColor = P.Selection;
        g.DefaultCellStyle.SelectionForeColor = P.Text;
        g.AlternatingRowsDefaultCellStyle.BackColor = P.Surface;
        g.RowHeadersVisible = false;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        DarkScroll(g);
        foreach (Control sb in g.Controls) DarkScroll(sb);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static bool _appModeSet;

    public static void ApplyTitleBar(Form f)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            int dark = P.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(f.Handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(f.Handle, 19, ref dark, sizeof(int));
            if (f.Visible)
            {
                bool active = Form.ActiveForm == f;
                SendMessage(f.Handle, 0x0086, IntPtr.Zero, IntPtr.Zero);
                SendMessage(f.Handle, 0x0086, active ? 1 : 0, IntPtr.Zero);
            }
        }
        catch { }
    }

    public static void DarkScroll(Control c)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            if (!_appModeSet)
            {
                _appModeSet = true;
                try { SetPreferredAppMode(1); } catch { }
            }
            void Do() => SetWindowTheme(c.Handle, P.Dark ? "DarkMode_Explorer" : "Explorer", null);
            if (c.IsHandleCreated) Do();
            else c.HandleCreated += (_, _) => Do();
        }
        catch { }
    }
}

public interface IThemed;

public static class Ui
{
    public static readonly float Scale = GetScale();

    private static float GetScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch { return 1f; }
    }

    public static int S(int v) => (int)Math.Round(v * Scale);

    public static Font Font(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public static readonly Font Base = Font(9.75f);
    public static readonly Font Bold = Font(9.75f, FontStyle.Bold);
    public static readonly Font Small = Font(8.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 11.5f);
    public static readonly Font Section = new("Segoe UI Semibold", 10.5f);

    public static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color StateColor(JobState s) => s switch
    {
        JobState.Idle => Theme.P.Success,
        JobState.Working or JobState.Starting => Theme.P.Accent,
        JobState.TargetMissing or JobState.SourceMissing => Theme.P.Warning,
        JobState.Error => Theme.P.Danger,
        _ => Theme.P.Muted,
    };

    public static Color EffectiveBack(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Theme.P.Back;
    }

    public static string Relative(DateTime? t)
    {
        if (t == null) return "noch nie";
        var d = DateTime.Now - t.Value;
        if (d.TotalSeconds < 60) return "gerade eben";
        if (d.TotalMinutes < 60) return $"vor {(int)d.TotalMinutes} Min.";
        if (t.Value.Date == DateTime.Today) return "heute " + t.Value.ToString("HH:mm");
        if (t.Value.Date == DateTime.Today.AddDays(-1)) return "gestern " + t.Value.ToString("HH:mm");
        return t.Value.ToString("dd.MM.yyyy HH:mm");
    }

    public static void OpenInExplorer(string path, bool select = false)
    {
        try
        {
            if (select && File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            else
                MessageBox.Show($"Der Ordner existiert (noch) nicht:\n{path}", "SicherungsWächter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SicherungsWächter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static Icon AppIcon()
    {
        try
        {
            using var s = typeof(Ui).Assembly.GetManifestResourceStream("app.ico");
            if (s != null) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }
}
