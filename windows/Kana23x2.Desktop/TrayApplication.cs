using Kana23x2.Core;

namespace Kana23x2.Desktop;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly KeyboardBridge bridge;
    private readonly GuideForm guide;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem toggleItem;
    private readonly ToolStripMenuItem transformItem;
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 50 };
    private readonly DirectInputSession session = new();
    private WindowContext? context;
    private bool enabled = true;
    private int shiftTap;
    private DateTime feedbackUntil;
    private string? feedbackMessage;
    private DateTime tailUntil;
    private bool refreshQueued;

    public TrayApplication()
    {
        guide = new GuideForm(ToggleEnabled);
        toggleItem = new ToolStripMenuItem("暂停映射", null, (_, _) => ToggleEnabled());
        transformItem = new ToolStripMenuItem("X/C 尾字变形（实验性）") { CheckOnClick = true, Checked = true };
        transformItem.CheckedChanged += (_, _) => ClearTail();
        var menu = new ContextMenuStrip();
        menu.Items.Add("布局与试输", null, (_, _) => { guide.Show(); guide.Activate(); });
        menu.Items.Add(toggleItem);
        menu.Items.Add(transformItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray = new NotifyIcon
        {
            Icon = SystemIcons.Application, Text = "Kana23x2", Visible = true, ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => { guide.Show(); guide.Activate(); };
        bridge = new KeyboardBridge();
        bridge.Key += OnKey;
        bridge.MouseActivity += InvalidateContext;
        bridge.ExternalInput += InvalidateContext;
        bridge.CallbackFailed += PauseOnError;
        poll.Tick += (_, _) => RefreshContext();
        RefreshContext();
        poll.Start();
        guide.Show();
    }

    private bool Ready => enabled && context is { IsInputReady: true };

    private void InvalidateContext()
    {
        ClearInputState();
        shiftTap = 0;
        context = null;
        QueueContextRefresh();
    }

    private void QueueContextRefresh()
    {
        // Query outside the hook as soon as the event is delivered, rather than
        // waiting for the periodic poll after a focus/mode transition.
        if (refreshQueued || !guide.IsHandleCreated || guide.IsDisposed) return;
        refreshQueued = true;
        guide.BeginInvoke((Action)(() =>
        {
            refreshQueued = false;
            if (!guide.IsDisposed) RefreshContext();
        }));
    }

    private void RefreshContext()
    {
        var next = bridge.ReadForegroundContext();
        if (context is not null && context != next)
        {
            ClearInputState();
            shiftTap = 0;
        }
        context = next;
        string status = !enabled ? "已暂停 · Ctrl + Alt + K 启用" :
            !next.IsJapanese ? "等待切换到日语 Microsoft IME" :
            !next.IsKnown ? "无法确认当前 IME 状态，暂不映射按键" :
            !next.ImeOpen || !next.NativeMode ? "请切换到日语假名「あ」输入模式" :
            !next.FullWidthMode ? "请使用全角平假名或全角片假名模式" :
            $"即时映射已启用 · 第 {session.Board + 1} 板 · {(session.KatakanaFor(next.KatakanaMode) ? "片假名" : "平假名")}";
        if (DateTime.UtcNow >= feedbackUntil) feedbackMessage = null;
        guide.SetStatus(feedbackMessage ?? status);
        tray.Text = feedbackMessage is null ? $"Kana23x2 · {status}" : "Kana23x2 · 请查看布局与试输窗口中的提示";
        if (DateTime.UtcNow >= tailUntil) session.ClearTail();
    }

    private void ToggleEnabled()
    {
        enabled = !enabled;
        ClearInputState();
        shiftTap = 0;
        toggleItem.Text = enabled ? "暂停映射" : "启用映射";
        // Also runs in the low-level hook: polling stays on the timer.
        guide.SetStatus(enabled ? "已启用，正在检查输入法状态…" : "已暂停 · Ctrl + Alt + K 启用");
    }

    private void PauseOnError(Exception error)
    {
        enabled = false;
        ClearInputState();
        toggleItem.Text = "启用映射";
        Feedback("映射已暂停", error.Message);
    }

    private bool OnKey(KeyboardEvent key)
    {
        try { return RouteKey(key); }
        catch (Exception error) { PauseOnError(error); return false; }
    }

    private bool RouteKey(KeyboardEvent key)
    {
        int vk = key.VirtualKey;
        if (key.IsDown && vk == 0x4B && key.Ctrl && key.Alt && !key.Win && !key.Shift)
        {
            if (!key.IsRepeat) ToggleEnabled();
            return true;
        }
        if (!Ready || context is null || !bridge.CurrentTargetMatches(context))
        {
            ClearInputState();
            shiftTap = 0;
            QueueContextRefresh();
            return false;
        }
        if (vk is 0x10 or 0xA0 or 0xA1)
        {
            if (key.IsDown)
            {
                if (!key.IsRepeat)
                    shiftTap = shiftTap == 0 && !key.Ctrl && !key.Alt && !key.Win ? vk : 0;
            }
            else
            {
                if (shiftTap == vk && !key.Ctrl && !key.Alt && !key.Win)
                {
                    session.ToggleScript(context.KatakanaMode);
                }
                shiftTap = 0;
            }
            return false; // Never swallow a release whose press went to the system.
        }
        if (!key.IsDown) return false;
        shiftTap = 0;
        if (key.Ctrl || key.Alt || key.Win || key.Shift)
        {
            InvalidateContext(); // Native mode shortcuts can retain the same HWND/HKL.
            return false;
        }
        switch (vk)
        {
            case 0x5A:
                // Both physical Z edges belong to the mapper. Keep the focused IME untouched.
                if (!key.IsRepeat) session.ToggleBoard();
                return true;
            case 0x58:
            case 0x43:
                if (!key.IsRepeat) Transform((char)vk);
                return true;
            case 0xBD:
            case 0x6D:
                return SendKana("ー", false);
        }
        if (vk is >= 0x41 and <= 0x5A && session.TryGetKana((char)vk, context.KatakanaMode, out string kana))
            return SendKana(kana, false);

        // Space/Enter/Esc/Backspace/arrows/digits/punctuation all belong to the IME.
        // Leave subsequent composition in the hands of the focused input method.
        ClearInputState();
        if (vk is 0x14 or 0x15 or 0x19 or 0x1C or 0x1D) InvalidateContext();
        return false;
    }

    private void Transform(char key)
    {
        if (!transformItem.Checked)
        {
            Feedback("X/C 尾字变形已关闭", "可在托盘菜单中启用实验性变形。");
            return;
        }
        if (DateTime.UtcNow >= tailUntil) session.ClearTail();
        if (session.Tail.Length == 0) return;
        if (!session.TryTransform(key, out string replacement, out string unsupported))
        {
            if (unsupported.Length != 0)
                Feedback("未应用此变形", $"微软罗马字接口暂不支持：{unsupported}");
            return;
        }
        SendKana(replacement, true);
    }

    private bool SendKana(string kana, bool replace)
    {
        if (context is null) return false;
        if (!Romanization.TryEncode(kana, out var runs, out string unsupported))
        {
            ClearTail();
            Feedback("未发送", $"无法编码：{unsupported}");
            return true;
        }
        var requestedScript = session.RequestedKatakana;
        string? error;
        bool sent = replace
            ? bridge.TryReplaceLastKana(context, runs, out error)
            : bridge.TrySendReading(context, runs, requestedScript, out error);
        if (!sent)
        {
            ClearInputState();
            if (bridge.LastSendWasPartial)
            {
                enabled = false;
                toggleItem.Text = "启用映射";
                Feedback("输入未完整送达 · 已暂停", error ?? "请检查目标文字，不要重复发送。");
            }
            else Feedback("未发送", error ?? "请检查目标窗口与输入法状态。");
            return true;
        }
        // Track only the latest successfully queued kana. This is a compatibility
        // heuristic, NOT proof of an uncommitted composition in another process.
        session.Remember(kana);
        tailUntil = DateTime.UtcNow.AddSeconds(3);
        if (!replace && requestedScript.HasValue)
            context = context with { KatakanaMode = requestedScript.Value };
        return true;
    }

    private void Feedback(string title, string message)
    {
        // The guide and tray display the message on their next refresh without creating a window.
        feedbackMessage = $"{title} · {message}";
        feedbackUntil = DateTime.UtcNow.AddSeconds(4);
    }

    private void ClearInputState()
    {
        ClearTail();
        session.ClearScriptRequest();
    }

    private void ClearTail()
    {
        session.ClearTail();
        tailUntil = DateTime.MinValue;
    }

    protected override void ExitThreadCore()
    {
        poll.Stop();
        bridge.Dispose();
        tray.Visible = false;
        guide.Exit();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            poll.Dispose();
            bridge.Dispose();
            tray.Dispose();
            guide.Dispose();
        }
        base.Dispose(disposing);
    }
}
