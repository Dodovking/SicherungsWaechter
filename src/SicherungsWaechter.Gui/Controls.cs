using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SicherungsWaechter.Gui;

public enum ButtonKind
{
    Primary,
    Secondary,
    Danger,
    Ghost,
}

public sealed class FlatButton : Button, IThemed
{
    private bool _hover;
    private bool _down;

    [DefaultValue(ButtonKind.Secondary)]
    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        Text = text;
        Kind = kind;
        Font = Ui.Base;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        UseMnemonic = false;
        Height = Ui.S(34);
        FitWidth();
    }

    public void FitWidth(int min = 0) =>
        Width = Math.Max(min, TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Ui.S(28));

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill, text, border;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = _hover ? p.AccentHover : p.Accent; text = p.AccentText; border = fill; break;
            case ButtonKind.Danger:
                fill = _hover ? Ui.Blend(p.Surface, p.Danger, 0.15f) : p.Surface; text = p.Danger; border = Ui.Blend(p.Border, p.Danger, 0.5f); break;
            case ButtonKind.Ghost:
                fill = _hover ? p.SurfaceHover : Ui.EffectiveBack(this); text = p.Text; border = fill; break;
            default:
                fill = _hover ? p.SurfaceHover : p.Surface; text = p.Text; border = p.Border; break;
        }
        if (_down) fill = Ui.Blend(fill, p.Text, 0.08f);
        if (!Enabled)
        {
            fill = Ui.Blend(fill, Ui.EffectiveBack(this), 0.5f);
            text = Ui.Blend(text, Ui.EffectiveBack(this), 0.55f);
        }
        using (var path = Ui.Rounded(r, Ui.S(7)))
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }
        if (Focused && ShowFocusCues)
        {
            using var path = Ui.Rounded(Rectangle.Inflate(r, -2, -2), Ui.S(6));
            using var pen = new Pen(Kind == ButtonKind.Primary ? p.AccentText : p.Accent) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, r, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

public sealed class ToggleSwitch : CheckBox, IThemed
{
    public ToggleSwitch(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Text = text;
        Font = Ui.Base;
        AutoSize = false;
        Cursor = Cursors.Hand;
        UseMnemonic = false;
        Height = Ui.S(30);
        Width = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Ui.S(64);
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        var back = Ui.EffectiveBack(this);
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int h = Ui.S(20), w = Ui.S(38);
        var track = new Rectangle(1, (Height - h) / 2, w, h);
        var trackColor = Checked ? p.Accent : Ui.Blend(p.Border, p.Muted, 0.35f);
        if (!Enabled) trackColor = Ui.Blend(trackColor, back, 0.5f);
        using (var path = Ui.Rounded(track, h / 2))
        using (var b = new SolidBrush(trackColor))
            g.FillPath(b, path);
        int k = h - Ui.S(6);
        int kx = Checked ? track.Right - k - Ui.S(3) : track.X + Ui.S(3);
        using (var kb = new SolidBrush(Color.White))
            g.FillEllipse(kb, kx, track.Y + Ui.S(3), k, k);
        var textRect = new Rectangle(track.Right + Ui.S(10), 0, Width - track.Right - Ui.S(10), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? p.Text : p.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(p.Accent) { DashStyle = DashStyle.Dot };
            using var path = Ui.Rounded(Rectangle.Inflate(track, 2, 2), h / 2 + 2);
            g.DrawPath(pen, path);
        }
    }
}

public sealed class Segmented : Control, IThemed
{
    private string[] _items;
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectedIndexChanged;

    public Segmented(params string[] items)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        _items = items;
        Font = Ui.Base;
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Ui.S(34);
        Width = items.Sum(i => TextRenderer.MeasureText(i, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Ui.S(30)) + Ui.S(8);
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, _items.Length - 1);
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Rectangle Segment(int i)
    {
        int inner = Width - Ui.S(8);
        var widths = _items.Select(t => TextRenderer.MeasureText(t, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Ui.S(30)).ToArray();
        float scale = (float)inner / widths.Sum();
        int x = Ui.S(4);
        for (int j = 0; j < i; j++) x += (int)(widths[j] * scale);
        int w = i == _items.Length - 1 ? Width - Ui.S(4) - x : (int)(widths[i] * scale);
        return new Rectangle(x, Ui.S(4), w, Height - Ui.S(8));
    }

    private int HitTest(Point pt)
    {
        for (int i = 0; i < _items.Length; i++) if (Segment(i).Contains(pt)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { var h = HitTest(e.Location); if (h != _hover) { _hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var h = HitTest(e.Location);
        if (h >= 0) SelectedIndex = h;
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        if (e.KeyCode == Keys.Right) SelectedIndex++;
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(8)))
        {
            using var b = new SolidBrush(p.Surface);
            g.FillPath(b, path);
            using var pen = new Pen(p.Border);
            g.DrawPath(pen, path);
        }
        for (int i = 0; i < _items.Length; i++)
        {
            var r = Segment(i);
            Color text = p.Text;
            if (i == _selected)
            {
                using var path = Ui.Rounded(r, Ui.S(6));
                using var b = new SolidBrush(p.Accent);
                g.FillPath(b, path);
                text = p.AccentText;
            }
            else if (i == _hover)
            {
                using var path = Ui.Rounded(r, Ui.S(6));
                using var b = new SolidBrush(p.SurfaceHover);
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, _items[i], Font, r, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (i == _selected && Focused && ShowFocusCues)
            {
                using var pen = new Pen(p.AccentText) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(pen, Rectangle.Inflate(r, -3, -3));
            }
        }
    }
}

/// <summary>TextBox with a themed rounded border.</summary>
public class InputBox : Panel, IThemed
{
    public TextBox Box { get; }

    public InputBox(bool multiline = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Ui.Base,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            AcceptsReturn = multiline,
        };
        Padding = new Padding(Ui.S(8), Ui.S(7), Ui.S(4), Ui.S(6));
        Box.Dock = DockStyle.Fill;
        Controls.Add(Box);
        Height = multiline ? Ui.S(110) : Box.PreferredHeight + Ui.S(14);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Tag = "input";
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => Box.Text;
        set => Box.Text = value;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        Box.BackColor = p.Input;
        Box.ForeColor = p.Text;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(6));
        using var b = new SolidBrush(p.Input);
        g.FillPath(b, path);
        using var pen = new Pen(Box.Focused ? p.Accent : p.Border, Box.Focused ? 1.6f : 1f);
        g.DrawPath(pen, path);
    }
}

public sealed class NumberBox : InputBox
{
    private readonly int _min;
    private readonly int _max;

    public NumberBox(int min, int max) : base(false)
    {
        _min = min;
        _max = max;
        Width = Ui.S(70);
        Box.TextAlign = HorizontalAlignment.Right;
        Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        Box.Leave += (_, _) => Value = Value;
    }

    public int Value
    {
        get => int.TryParse(Box.Text, out var v) ? Math.Clamp(v, _min, _max) : _min;
        set => Box.Text = Math.Clamp(value, _min, _max).ToString();
    }
}

/// <summary>Rounded surface card used as a container.</summary>
public class Card : Panel, IThemed
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Tag = "surface";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        BackColor = p.Surface;
        var g = e.Graphics;
        g.Clear(Parent != null ? Ui.EffectiveBack(this) : p.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10));
        using var b = new SolidBrush(p.Surface);
        g.FillPath(b, path);
        using var pen = new Pen(p.Border);
        g.DrawPath(pen, path);
        PaintContent(g);
    }

    protected virtual void PaintContent(Graphics g) { }
}

public static class Make
{
    public static Label Label(string text, Font? font = null, bool muted = false, int width = 0)
    {
        var l = new Label
        {
            Text = text,
            Font = font ?? Ui.Base,
            AutoSize = width == 0,
            UseMnemonic = false,
            Tag = muted ? "muted" : null,
            ForeColor = muted ? Theme.P.Muted : Theme.P.Text,
            BackColor = Color.Transparent,
        };
        if (width > 0)
        {
            l.Width = width;
            l.MaximumSize = new Size(width, 0);
            l.AutoSize = true;
        }
        return l;
    }
}
