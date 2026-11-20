using System.ComponentModel;
using System.Runtime.InteropServices;
using Kana23x2.Core;

namespace Kana23x2.Desktop;

public readonly record struct KeyboardEvent(
    int VirtualKey, bool IsDown, bool IsRepeat, bool Ctrl, bool Alt, bool Win, bool Shift)
{
    public bool IsUp => !IsDown;
}

/// <summary>
/// UI-thread keyboard bridge. This object needs a running Windows message loop.
/// It sends ordinary key events to the IME and never reads another process's HIMC.
/// </summary>
public sealed class KeyboardBridge : IDisposable
{
    private readonly NativeMethods.HookProc _keyboardCallback;
    private readonly NativeMethods.HookProc _mouseCallback;
    private readonly HashSet<int> _down = [];
    private readonly HashSet<int> _suppressed = [];
    private nint _keyboardHook;
    private nint _mouseHook;
    private int _currentUndeliveredDown = -1;
    private bool _disposed;

    public event Func<KeyboardEvent, bool>? Key;
    public event Action? MouseActivity;
    public event Action? ExternalInput;
    public event Action<Exception>? CallbackFailed;
    public bool LastSendWasPartial { get; private set; }

    public KeyboardBridge()
    {
        _keyboardCallback = OnKeyboard;
        _mouseCallback = OnMouse;
        var module = NativeMethods.GetModuleHandleW(null);
        _keyboardHook = NativeMethods.SetWindowsHookExW(NativeMethods.WhKeyboardLl, _keyboardCallback, module, 0);
        if (_keyboardHook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装按键映射钩子。");

        _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WhMouseLl, _mouseCallback, module, 0);
        if (_mouseHook == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "无法安装焦点保护所需的鼠标钩子。");
        }
    }

    /// <summary>Poll from a timer, never from a low-level hook callback.</summary>
    public WindowContext ReadForegroundContext()
    {
        var target = ReadTarget();
        if (target.Window == 0 || target.Focus == 0 || !target.IsJapanese)
            return target;

        var ime = NativeMethods.ImmGetDefaultIMEWnd(target.Focus);
        if (ime == 0 || !QueryIme(ime, NativeMethods.ImcGetOpenStatus, out var open)
            || !QueryIme(ime, NativeMethods.ImcGetConversionMode, out var conversion))
            return target;

        return target.WithImeStatus(open != 0, conversion);
    }

    /// <summary>Fast recheck that does not send a message to the target application.</summary>
    public bool CurrentTargetMatches(WindowContext expected)
    {
        var current = ReadTarget();
        return current.Window != 0 && current.Focus != 0
            && expected.Window == current.Window && expected.Focus == current.Focus
            && expected.Layout == current.Layout && expected.ThreadId == current.ThreadId;
    }

    /// <summary>
    /// Atomically queues romaji and an optional, explicit one-shot script request.
    /// Ordinary input follows the native IME mode; RomanRun.Katakana is not a command.
    /// A successful result means queued, not that an application has processed it.
    /// A partial result must never be retried automatically.
    /// </summary>
    public bool TrySendReading(WindowContext expected, IReadOnlyList<RomanRun> runs,
        bool? requestedKatakana, out string? error) =>
        TryInject(expected, runs, requestedKatakana, replaceLastKana: false, out error);

    /// <summary>
    /// Sends one native Backspace followed by replacement romaji in a single batch.
    /// The caller must restrict this to its own tracked tail, invalidate that tail
    /// at every input boundary, and treat application/IME deletion behavior as
    /// compatibility-dependent. This is not proof of native composition ownership.
    /// </summary>
    public bool TryReplaceLastKana(WindowContext expected, IReadOnlyList<RomanRun> replacement,
        out string? error) =>
        TryInject(expected, replacement, null, replaceLastKana: true, out error);

    private bool TryInject(WindowContext expected, IReadOnlyList<RomanRun> runs,
        bool? requestedKatakana, bool replaceLastKana, out string? error)
    {
        LastSendWasPartial = false;
        error = null;
        if (_disposed || !expected.IsInputReady || !CurrentTargetMatches(expected))
        {
            error = "输入目标或输入法状态已改变；未发送文字。";
            return false;
        }
        if (ModifiersPhysicallyDown())
        {
            error = "请先松开 Shift、Ctrl、Alt 和 Windows 键；未发送文字。";
            return false;
        }
        if (!TryBuildInputPlan(runs, expected.KatakanaMode, requestedKatakana, replaceLastKana,
                character => NativeMethods.VkKeyScanExW(character, expected.Layout), out var batch, out error))
            return false;
        if (batch.Length == 0)
            return true;

        // Recheck immediately before SendInput: preparing a long reading may take time.
        if (ModifiersPhysicallyDown() || !CurrentTargetMatches(expected))
        {
            error = "输入目标或按键状态已改变；未发送文字。";
            return false;
        }

        var sent = NativeMethods.SendInput((uint)batch.Length, batch, Marshal.SizeOf<NativeMethods.Input>());
        if (sent == batch.Length)
            return true;

        LastSendWasPartial = sent != 0;
        var released = sent == 0 || ReleasePartialDowns(batch, sent);
        error = sent == 0
            ? "Windows 未接受输入事件。请检查目标应用权限与输入法状态。"
            : $"Windows 仅接受 {sent}/{batch.Length} 个输入事件；请检查目标文字，不要重复发送。";
        if (!released)
            error += "部分按键释放也未送达，请松开并重新按一次相关按键。";
        return false;
    }

    /// <summary>
    /// Builds the actual production event batch without querying or mutating Windows.
    /// Tests supply a character resolver; production supplies VkKeyScanExW for the
    /// target layout. A failed plan always returns an empty batch.
    /// </summary>
    internal static bool TryBuildInputPlan(IReadOnlyList<RomanRun> runs, bool currentKatakana,
        bool? requestedKatakana, bool replaceLastKana, Func<char, short> resolveKey,
        out NativeMethods.Input[] batch, out string? error)
    {
        batch = [];
        error = null;
        if (!runs.Any(run => !string.IsNullOrEmpty(run.Keys)))
        {
            if (!replaceLastKana) return true;
            error = "替换读音为空；未修改文字。";
            return false;
        }

        var inputs = new List<NativeMethods.Input>();
        if (replaceLastKana)
        {
            // X/C never switches modes, even if a supplied run describes another
            // script. Validate the complete plan before delivering the Backspace.
            AddKey(inputs, 0x08);
        }
        else if (requestedKatakana is { } requested && requested != currentKatakana)
        {
            AddScriptRequest(inputs, requested);
        }

        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Keys)) continue;
            // The single explicit request above is the only script operation.
            // Neither this run nor a subsequent run can implicitly change modes.
            foreach (var character in run.Keys)
            {
                if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '\'' or '.' or ',')
                    || !TryAddCharacter(inputs, resolveKey(character)))
                {
                    error = "当前键盘布局无法发送所需罗马字；未发送文字。";
                    return false;
                }
            }
        }
        batch = inputs.ToArray();
        return true;
    }

    private bool ReleasePartialDowns(NativeMethods.Input[] batch, uint sent)
    {
        // SendInput inserts the batch in sequence. Balance only the inserted prefix;
        // never resend a text keydown or retry the reading after a partial result.
        var outstanding = new List<ushort>();
        for (var i = 0; i < Math.Min(sent, (uint)batch.Length); ++i)
        {
            var keyboard = batch[i].Data.Keyboard;
            if ((keyboard.Flags & NativeMethods.KeyeventfKeyup) != 0)
                outstanding.Remove(keyboard.VirtualKey);
            else if (!outstanding.Contains(keyboard.VirtualKey))
                outstanding.Add(keyboard.VirtualKey);
        }

        var releases = new List<NativeMethods.Input>();
        for (var i = outstanding.Count - 1; i >= 0; --i)
        {
            var key = outstanding[i];
            // _down tracks hardware events only. GetAsyncKeyState at this point
            // could reflect our injected down, so it cannot distinguish ownership.
            // A physical down swallowed by this bridge was never sent to the app.
            // The current hook down has not been delivered yet either; this matters
            // when immediate mapping injects the same letter as the physical key.
            if (!_down.Contains(key) || _suppressed.Contains(key) || key == _currentUndeliveredDown)
                releases.Add(NativeMethods.Input.Key(key, true));
        }
        if (releases.Count == 0)
            return true;
        var cleanup = releases.ToArray();
        return NativeMethods.SendInput((uint)cleanup.Length, cleanup,
            Marshal.SizeOf<NativeMethods.Input>()) == cleanup.Length;
    }

    private static WindowContext ReadTarget()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0)
            return WindowContext.Unknown;
        var threadId = NativeMethods.GetWindowThreadProcessId(window, out _);
        if (threadId == 0)
            return WindowContext.Unknown;
        var layout = NativeMethods.GetKeyboardLayout(threadId);
        var info = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        var focus = NativeMethods.GetGUIThreadInfo(threadId, ref info) ? info.Focus : 0;
        return new WindowContext(window, focus, layout, threadId);
    }

    private static bool QueryIme(nint ime, uint command, out nuint result) =>
        NativeMethods.SendMessageTimeoutW(ime, NativeMethods.WmImeControl, command, 0,
            0x0001 | 0x0002, 30, out result) != 0; // SMTO_BLOCK | SMTO_ABORTIFHUNG

    private bool ModifiersPhysicallyDown() =>
        new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }
            .Any(key => _down.Contains(key) || NativeMethods.GetAsyncKeyState(key) < 0);

    private static void AddScriptRequest(List<NativeMethods.Input> inputs, bool katakana)
    {
        // Dedicated IME On has explicit Japanese IME semantics across composition
        // states: IME On -> Hiragana; Shift+IME On -> full-width Katakana.
        // Avoid Caps Lock/Eisu and release synthetic Shift before any roman letters.
        if (katakana) inputs.Add(NativeMethods.Input.Key(NativeMethods.VkLShift));
        AddKey(inputs, NativeMethods.VkImeOn);
        if (katakana) inputs.Add(NativeMethods.Input.Key(NativeMethods.VkLShift, true));
    }

    private static bool TryAddCharacter(List<NativeMethods.Input> inputs, short mapping)
    {
        if (mapping == -1)
            return false;
        var modifiers = (mapping >> 8) & 0xFF;
        // Never manufacture Ctrl/Alt shortcuts to type a character.
        if ((modifiers & ~1) != 0)
            return false;
        if ((modifiers & 1) != 0)
            inputs.Add(NativeMethods.Input.Key(0xA0));
        AddKey(inputs, (ushort)(mapping & 0xFF));
        if ((modifiers & 1) != 0)
            inputs.Add(NativeMethods.Input.Key(0xA0, true));
        return true;
    }

    private static void AddKey(List<NativeMethods.Input> inputs, ushort key)
    {
        inputs.Add(NativeMethods.Input.Key(key));
        inputs.Add(NativeMethods.Input.Key(key, true));
    }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
        if ((data.Flags & NativeMethods.LlkhfInjected) != 0)
        {
            // Our own events must not recurse through the mapper. Other injected
            // input can commit or move an IME composition and invalidates its tail.
            if (data.ExtraInfo != NativeMethods.InjectedInputTag)
                NotifyExternalInput();
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }
        var message = (uint)wParam;
        if (message is not (NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown
            or NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp))
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);

        var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var key = NormalizeModifier((int)data.VirtualKey, data.ScanCode, data.Flags);
        // Async state is sampled before this physical event is applied. It also
        // recognizes a held key whose initial down predates installing the hook.
        var repeat = isDown && (_down.Contains(key) || NativeMethods.GetAsyncKeyState(key) < 0);
        if (isDown)
            _down.Add(key);
        else
            _down.Remove(key);
        var alreadySuppressed = _suppressed.Contains(key);
        // Ownership belongs to the first down until the matching up. In particular,
        // enabling mapping while a previously delivered key is held must not steal
        // its repeats/up (or let the controller append those repeats to its buffer).
        if (isDown && repeat && !alreadySuppressed)
        {
            // A native repeat may still commit/edit newly injected text (e.g. a
            // held Enter). Invalidate the tracked tail without offering capture.
            NotifyExternalInput();
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }
        var handled = false;
        var previousUndeliveredDown = _currentUndeliveredDown;
        _currentUndeliveredDown = isDown ? key : -1;
        try
        {
            var ev = new KeyboardEvent(key, isDown, repeat,
                Down(0xA2, key, isDown) || Down(0xA3, key, isDown),
                Down(0xA4, key, isDown) || Down(0xA5, key, isDown),
                Down(0x5B, key, isDown) || Down(0x5C, key, isDown),
                Down(0xA0, key, isDown) || Down(0xA1, key, isDown));
            if (Key is { } handlers)
                foreach (Func<KeyboardEvent, bool> handler in handlers.GetInvocationList())
                    handled |= handler(ev);
        }
        catch (Exception exception)
        {
            ReportCallbackFailure(exception);
        }
        finally
        {
            _currentUndeliveredDown = previousUndeliveredDown;
        }

        if (isDown && (handled || alreadySuppressed))
        {
            _suppressed.Add(key);
            return 1;
        }
        if (!isDown)
        {
            _suppressed.Remove(key);
            // Never swallow an up whose down was delivered to the application.
            if (alreadySuppressed)
                return 1;
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private bool Down(int key, int currentKey, bool currentDown) =>
        key == currentKey ? currentDown : _down.Contains(key) || NativeMethods.GetAsyncKeyState(key) < 0;

    private static int NormalizeModifier(int key, uint scan, uint flags) => key switch
    {
        0x10 => scan == 0x36 ? 0xA1 : 0xA0,
        0x11 => (flags & NativeMethods.LlkhfExtended) != 0 ? 0xA3 : 0xA2,
        0x12 => (flags & NativeMethods.LlkhfExtended) != 0 ? 0xA5 : 0xA4,
        _ => key
    };

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (uint)wParam is 0x0201 or 0x0204 or 0x0207 or 0x020B or 0x020A or 0x020E)
        {
            // This application never synthesizes mouse input. Both hardware and
            // injected clicks/wheels can select a candidate or change the caret.
            try { MouseActivity?.Invoke(); }
            catch (Exception exception) { ReportCallbackFailure(exception); }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void ReportCallbackFailure(Exception exception)
    {
        // An exception crossing the unmanaged hook boundary would terminate the host.
        try { CallbackFailed?.Invoke(exception); }
        catch { }
    }

    private void NotifyExternalInput()
    {
        try { ExternalInput?.Invoke(); }
        catch (Exception exception) { ReportCallbackFailure(exception); }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_keyboardHook != 0)
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != 0)
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = 0;
        GC.KeepAlive(_keyboardCallback);
        GC.KeepAlive(_mouseCallback);
    }
}
