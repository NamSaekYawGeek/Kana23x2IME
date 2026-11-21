namespace Kana23x2.Desktop;

internal sealed class GuideForm : Form
{
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 40 };
    private bool exiting;

    public GuideForm(Action toggle)
    {
        Text = "Kana23x2 · Windows";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 660);
        ClientSize = new Size(860, 710);
        Padding = new Padding(20);
        Font = new Font("Microsoft YaHei UI", 10);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        foreach (int height in new[] { 40, 125, 215, 54, 30 })
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(status, 0, 0);
        content.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "1. 安装日语 Microsoft IME，使用罗马字输入、全角假名和默认微软快捷键配置。\n" +
                   "2. 点击输入框，按 Kana23x2 布局打字：每次按键立即进入目标输入框。\n" +
                   "3. 空格转换／展开系统候选，方向键选词、Enter 确认，均由微软 IME 处理。\n" +
                   "4. 可在下方试用 X/C/Shift，也可在托盘关闭尾字变形。\n" +
                   "Ctrl + Alt + K 暂停／启用。系统候选内容与排序沿用微软 IME 的设置和学习结果。"
        }, 0, 1);

        var layout = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, TabStop = false,
            WordWrap = false, ScrollBars = ScrollBars.Horizontal,
            BorderStyle = BorderStyle.None, BackColor = SystemColors.Control,
            Font = new Font("Yu Gothic UI", 12),
            Text = "键位     Q W E R T Y U I O P   /   A S D F G H J K L   /   V B N M\r\n" +
                   "第一板   あ い う え お か き く け こ  /  さ し す せ そ た ち つ て  /  と や ゆ よ\r\n" +
                   "第二板   な に ぬ ね の は ひ ふ へ ほ  /  ま み む め も ら り る れ  /  ろ わ を ん\r\n\r\n" +
                   "Z：换板     X：浊音     C：第一板小假名／第二板半浊音\r\n" +
                   "CapsLock：原键放行，单独松开切换平／片假名     -：长音\r\n" +
                   "单独 Shift：尾字换另一板基础假名（まま ↔ まさ），当前板号不变\r\n" +
                   "连续编辑时，退格后 3 秒内可用 X/C/Shift 修改前一个假名。"
        };
        content.Controls.Add(layout, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var toggleButton = new Button { Text = "暂停／启用", AutoSize = true };
        toggleButton.Click += (_, _) => toggle();
        var hideButton = new Button { Text = "隐藏到托盘", AutoSize = true };
        hideButton.Click += (_, _) => Hide();
        actions.Controls.Add(toggleButton);
        actions.Controls.Add(hideButton);
        content.Controls.Add(actions, 0, 3);
        content.Controls.Add(new Label { Text = "可在这里试输，也可以切换到记事本等应用：", Dock = DockStyle.Fill }, 0, 4);
        content.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Yu Gothic UI", 16)
        }, 0, 5);
        // No AcceptButton: Enter remains an IME/editor key.
        Controls.Add(content);
    }

    public void SetStatus(string message) => status.Text = message;
    public void Exit() { exiting = true; Close(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
