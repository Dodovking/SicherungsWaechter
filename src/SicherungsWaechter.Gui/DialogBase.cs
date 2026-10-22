namespace SicherungsWaechter.Gui;

/// <summary>Dialog with a scrollable body that stacks rows top-to-bottom and a footer with buttons.</summary>
public class DialogBase : Form
{
    protected readonly Panel Body = new() { Dock = DockStyle.Fill, AutoScroll = true };
    protected readonly Panel Footer = new() { Dock = DockStyle.Bottom };
    protected readonly FlatButton OkButton;
    protected readonly FlatButton CancelBtn = new("Abbrechen");
    protected int Y;
    protected int Left0 => Ui.S(28);
    protected int ContentWidth => ClientSize.Width - Ui.S(56) - SystemInformation.VerticalScrollBarWidth;

    public DialogBase(string title, string okText, int width, int height)
    {
        Text = title;
        Font = Ui.Base;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = Ui.AppIcon();
        ClientSize = new Size(Ui.S(width), Ui.S(height));
        OkButton = new FlatButton(okText, ButtonKind.Primary);
        Footer.Height = Ui.S(68);
        Footer.Controls.AddRange([OkButton, CancelBtn]);
        Footer.Layout += (_, _) =>
        {
            OkButton.Location = new Point(Footer.Width - Ui.S(28) - OkButton.Width, Ui.S(17));
            CancelBtn.Location = new Point(OkButton.Left - Ui.S(8) - CancelBtn.Width, Ui.S(17));
        };
        Footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.P.Border);
            e.Graphics.DrawLine(pen, 0, 0, Footer.Width, 0);
        };
        CancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = CancelBtn;
        Controls.Add(Body);
        Controls.Add(Footer);
        Y = Ui.S(22);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Apply(this);
        Footer.BackColor = Theme.P.Surface;
        Theme.ApplyTitleBar(this);
    }

    protected Label AddHeading(string text)
    {
        if (Y > Ui.S(30)) Y += Ui.S(10);
        var l = Make.Label(text, Ui.Section);
        Place(l, l.PreferredHeight);
        Y += Ui.S(2);
        return l;
    }

    protected Label AddHint(string text)
    {
        var l = Make.Label(text, Ui.Small, muted: true, width: ContentWidth);
        Place(l, l.PreferredHeight);
        return l;
    }

    protected T Place<T>(T c, int height, int gap = 8) where T : Control
    {
        c.Location = new Point(Left0, Y);
        Body.Controls.Add(c);
        Y += height + Ui.S(gap);
        return c;
    }

    /// <summary>Puts several controls on one row, left to right; the first InputBox stretches.</summary>
    protected void AddRow(params Control[] controls)
    {
        int fixedW = controls.Where(c => c is not InputBox || c is NumberBox).Sum(c => c.Width) + Ui.S(8) * (controls.Length - 1);
        int x = Left0;
        int h = controls.Max(c => c.Height);
        bool stretched = false;
        foreach (var c in controls)
        {
            if (c is InputBox and not NumberBox && !stretched)
            {
                c.Width = ContentWidth - fixedW;
                stretched = true;
            }
            c.Location = new Point(x, Y + (h - c.Height) / 2);
            Body.Controls.Add(c);
            x += c.Width + Ui.S(8);
        }
        Y += h + Ui.S(8);
    }

    protected void AddLine(params object[] parts)
    {
        var controls = parts.Select(p => p is string s ? Make.Label(s) : (Control)p).ToArray();
        int x = Left0;
        int h = controls.Max(c => c is Label l ? l.PreferredHeight : c.Height);
        foreach (var c in controls)
        {
            int ch = c is Label l ? l.PreferredHeight : c.Height;
            c.Location = new Point(x, Y + (h - ch) / 2);
            Body.Controls.Add(c);
            x += (c is Label lb ? lb.PreferredWidth : c.Width) + Ui.S(6);
        }
        Y += h + Ui.S(8);
    }

    protected void FinishLayout()
    {
        var spacer = new Panel { Location = new Point(0, Y), Size = new Size(1, Ui.S(10)) };
        Body.Controls.Add(spacer);
    }
}
