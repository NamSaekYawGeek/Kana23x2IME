namespace Kana23x2.Desktop;

/// <summary>Brief mode/error feedback. Never takes focus from the target editor.</summary>
internal sealed class StatusOverlay : Form
{
    private readonly Label state = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly Label reading = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Font readingFont = new("Microsoft YaHei UI", 12);

    public StatusOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(600, 96);
        Padding = new Padding(16, 10, 16, 10);
        BackColor = Color.FromArgb(27, 33, 43);
        ForeColor = Color.White;
        Font = SystemFonts.MessageBoxFont;
        state.ForeColor = Color.FromArgb(143, 207, 220);
        reading.Font = readingFont;
        Controls.Add(reading);
        Controls.Add(state);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var value = base.CreateParams;
            value.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; // noactivate, toolwindow, transparent
            return value;
        }
    }

    public void Display(nint target, string title, string text)
    {
        state.Text = title;
        reading.Text = text;
        var area = Screen.FromHandle(target).WorkingArea;
        Width = Math.Min(600, area.Width - 24);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 24);
        if (!Visible) Show();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) readingFont.Dispose();
    }
}
