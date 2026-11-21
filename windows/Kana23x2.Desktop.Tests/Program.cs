using System.Globalization;
using Kana23x2.Core;
using Kana23x2.Desktop;

namespace Kana23x2.Desktop.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        (string Name, Action<Fixture> Run)[] tests =
        [
            ("solo Shift waits for release and replaces only the last kana", SoloShift),
            ("a fast following key flushes the swap exactly once", FastFollowingKey),
            ("Shift plus a letter stays native", ShiftChord),
            ("native Backspace repeats preserve earlier X/C history", BackspaceHistory),
            ("small kana still toggles after native Backspace", SmallKanaAfterBackspace),
            ("Caps changes mode immediately and candidate keys stay native", CapsAndCandidates),
            ("focus change cancels a queued swap", FocusCancelsSwap),
            ("external input cancels a queued swap", ExternalCancelsSwap),
            ("a shortcut cancels a queued swap", ShortcutCancelsSwap)
        ];
        int failed = 0;
        foreach (var test in tests)
        {
            try
            {
                using var fixture = new Fixture();
                test.Run(fixture);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception error)
            {
                ++failed;
                Console.Error.WriteLine($"FAIL {test.Name}\n{error}");
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} Windows controller regressions passed.");
        Console.WriteLine("Real WinForms controller + simulated native bridge; no real Japanese IME validation.");
        return failed == 0 ? 0 : 1;
    }

    private static void SoloShift(Fixture f)
    {
        f.TypeMama();
        f.Bridge.Down(0xA0);
        Check.Equal("まま", f.Bridge.Text);
        Check.Equal(0, f.Bridge.ReplacementAttempts);
        f.Bridge.Up(0xA0);
        Check.Equal("まま", f.Bridge.Text, "Shift-up must queue work, not inject during the hook.");
        Check.Equal(0, f.Bridge.ReplacementAttempts);
        Fixture.Pump();
        Check.Equal("まさ", f.Bridge.Text);
        Check.Equal(1, f.Bridge.ReplaceCalls);
        Check.Equal(0, f.Bridge.UnsafeModifierAttempts);
        Fixture.Pump();
        Check.Equal(1, f.Bridge.ReplaceCalls, "The queued callback must not run twice.");
        f.Tap(0xA0);
        Fixture.Pump();
        Check.Equal("まま", f.Bridge.Text, "A second solo Shift must swap the same tail back.");
        Check.Equal(0, f.Bridge.ModeCommands.Count, "Shift must not change the native kana script.");
        f.Tap('A');
        Check.Equal("ままま", f.Bridge.Text, "Tail swapping must not change the active board.");
    }

    private static void FastFollowingKey(Fixture f)
    {
        f.TypeMama();
        f.Tap(0xA0);
        Check.Equal(0, f.Bridge.ReplaceCalls);
        f.Tap('B'); // Second board B is わ; do not pump the pending callback first.
        Check.Equal("まさわ", f.Bridge.Text);
        Check.Equal(1, f.Bridge.ReplaceCalls);
        Check.Equal("replace:sa", f.Bridge.Operations[^2]);
        Check.Equal("append:wa", f.Bridge.Operations[^1]);
        Fixture.Pump();
        Check.Equal("まさわ", f.Bridge.Text);
        Check.Equal(1, f.Bridge.ReplaceCalls);
    }

    private static void ShiftChord(Fixture f)
    {
        f.TypeMama();
        int appends = f.Bridge.AppendCalls;
        f.Bridge.Down(0xA0);
        Check.False(f.Bridge.Down('Q'), "Shift+Q must not be captured.");
        f.Bridge.Up('Q');
        f.Bridge.Up(0xA0);
        Fixture.Pump();
        Check.Equal(appends, f.Bridge.AppendCalls);
        Check.Equal(0, f.Bridge.ReplacementAttempts);
        Check.Equal(0, f.Bridge.ModeCommands.Count);
        Check.Equal(1, f.Bridge.Passed.Count(e => e.VirtualKey == 'Q' && e.IsDown));
        Check.Equal(1, f.Bridge.Passed.Count(e => e.VirtualKey == 'Q' && e.IsUp));
        Check.Equal(1, f.Bridge.Passed.Count(e => e.VirtualKey == 0xA0 && e.IsDown));
        Check.Equal(1, f.Bridge.Passed.Count(e => e.VirtualKey == 0xA0 && e.IsUp));
    }

    private static void BackspaceHistory(Fixture f)
    {
        f.Tap('Z');
        f.Tap('Y');
        f.Tap('A');
        f.Tap('A');
        Check.Equal("はまま", f.Bridge.Text);
        Check.False(f.Bridge.Down(0x08));
        Check.Equal("はま", f.Bridge.Text);
        Check.False(f.Bridge.Down(0x08)); // A held, originally passed Backspace repeat.
        f.Bridge.Up(0x08);
        Check.Equal("は", f.Bridge.Text);
        Check.Equal(2, f.Bridge.NativeBackspaces);
        Check.Equal(0, f.Bridge.InjectedBackspaces, "Physical deletion must not inject another Backspace.");
        f.Tap('X');
        Check.Equal("ば", f.Bridge.Text, "X must address the previous surviving kana.");
        f.Tap('C');
        Check.Equal("ぱ", f.Bridge.Text);
        f.Tap('A');
        f.Tap(0x08);
        f.Tap('X');
        Check.Equal("ば", f.Bridge.Text, "Replacement must update history rather than append an entry.");
        Check.Equal(3, f.Bridge.NativeBackspaces);
        Check.Equal(3, f.Bridge.InjectedBackspaces);
        Check.Equal(3, f.Bridge.ReplaceCalls);
    }

    private static void CapsAndCandidates(Fixture f)
    {
        Check.True(f.Bridge.Down(0x14));
        Check.Equal(1, f.Bridge.ModeCommands.Count, "Caps must switch now, without a subsequent letter.");
        Check.True(f.Bridge.ModeCommands[0]);
        Check.Equal("", f.Bridge.Text);
        Check.Equal(0, f.Bridge.AppendCalls);
        Check.True(f.Bridge.Down(0x14)); // Autorepeat is swallowed, not toggled again.
        f.Bridge.Up(0x14);
        Fixture.Pump();
        Check.Equal(1, f.Bridge.ModeCommands.Count);
        Check.Equal(0, f.Bridge.Passed.Count(e => e.VirtualKey == 0x14));
        f.Tap('Q');
        Check.Equal("ア", f.Bridge.Text);
        Check.Equal(1, f.Bridge.ModeCommands.Count, "Ordinary kana must not send a script command.");
        Check.False(f.Bridge.Down(0x20));
        f.Bridge.Up(0x20);
        Check.False(f.Bridge.Down(0x0D));
        f.Bridge.Up(0x0D);
        Check.Equal(1, f.Bridge.Passed.Count(e => e.VirtualKey == 0x20 && e.IsDown));
        Check.Equal(1, f.Bridge.Passed.Count(e => e.VirtualKey == 0x0D && e.IsDown));
        f.Tap(0x14);
        Check.Equal(2, f.Bridge.ModeCommands.Count);
        Check.False(f.Bridge.ModeCommands[1]);
        Check.Equal("ア", f.Bridge.Text, "A script command must not rewrite preceding text.");
        f.Tap('Q');
        Check.Equal("アあ", f.Bridge.Text);
    }

    private static void SmallKanaAfterBackspace(Fixture f)
    {
        f.Tap('Q');
        f.Tap('Q');
        Check.Equal("ああ", f.Bridge.Text);
        f.Tap(0x08);
        Check.Equal("あ", f.Bridge.Text);
        Check.Equal(1, f.Bridge.NativeBackspaces);
        Check.Equal(0, f.Bridge.InjectedBackspaces);
        f.Tap('C');
        Check.Equal("ぁ", f.Bridge.Text);
        f.Tap('C');
        Check.Equal("あ", f.Bridge.Text);
        Check.Equal(2, f.Bridge.ReplaceCalls);
        Check.Equal(2, f.Bridge.InjectedBackspaces);
        Check.Equal(1, f.Bridge.NativeBackspaces);
    }

    private static void FocusCancelsSwap(Fixture f)
    {
        f.TypeMama();
        f.Tap(0xA0);
        f.Bridge.ChangeFocus(); // No event: test the controller's immediate target recheck.
        Fixture.Pump();
        Check.Equal("まま", f.Bridge.Text);
        Check.Equal(0, f.Bridge.ReplacementAttempts);
    }

    private static void ExternalCancelsSwap(Fixture f)
    {
        f.TypeMama();
        f.Tap(0xA0);
        f.Bridge.NotifyExternalInput();
        Fixture.Pump();
        Check.Equal("まま", f.Bridge.Text);
        Check.Equal(0, f.Bridge.ReplacementAttempts);
        f.Tap('X');
        Check.Equal(0, f.Bridge.ReplacementAttempts, "External input must also invalidate retained history.");
    }

    private static void ShortcutCancelsSwap(Fixture f)
    {
        f.TypeMama();
        f.Tap(0xA0);
        f.Bridge.Down(0xA2);
        f.Tap('Q');
        f.Bridge.Up(0xA2);
        Fixture.Pump();
        Check.Equal(0, f.Bridge.ReplacementAttempts);
        Check.Equal(2, f.Bridge.AppendCalls);
        Check.Equal(1, f.Bridge.Passed.Count(e => e.VirtualKey == 'Q' && e.IsDown));
    }
}

internal sealed class Fixture : IDisposable
{
    internal FakeKeyboardBridge Bridge { get; } = new();
    private readonly TrayApplication controller;

    internal Fixture()
    {
        controller = new TrayApplication(Bridge);
        Pump();
    }

    internal void Tap(int key) { Bridge.Down(key); Bridge.Up(key); }
    internal void TypeMama()
    {
        Tap('Z'); Tap('A'); Tap('A');
        Check.Equal("まま", Bridge.Text);
    }

    // Dispatch actual WinForms BeginInvoke messages. No controller methods are
    // invoked reflectively, and no waiting for a real Japanese IME is involved.
    internal static void Pump() { Application.DoEvents(); Application.DoEvents(); }
    public void Dispose() { controller.Dispose(); Pump(); }
}

/// <summary>
/// Models only the native actions used by these controller tests. This is not an
/// IME implementation. Real keyboard hooks, conversion, and app compatibility
/// remain outside this harness and require separate Windows interaction checks.
/// </summary>
internal sealed class FakeKeyboardBridge : IKeyboardBridge
{
    private static readonly Dictionary<string, (string Hiragana, string Katakana)> Kana = new()
    {
        ["ma"] = ("ま", "マ"), ["sa"] = ("さ", "サ"), ["wa"] = ("わ", "ワ"),
        ["ha"] = ("は", "ハ"), ["ba"] = ("ば", "バ"), ["pa"] = ("ぱ", "パ"),
        ["a"] = ("あ", "ア"), ["xa"] = ("ぁ", "ァ"),
        ["ka"] = ("か", "カ"), ["ga"] = ("が", "ガ")
    };
    private readonly HashSet<int> physicalDown = [];
    private readonly HashSet<int> captured = [];
    private WindowContext current = new WindowContext(0x111, 0x112, 0x0411, 42).WithImeStatus(true, 0x9);
    private bool disposed;

    public event Func<KeyboardEvent, bool>? Key;
    // These cases drive keyboard and external-input events only.
    public event Action? MouseActivity { add { } remove { } }
    public event Action? ExternalInput;
    public event Action<Exception>? CallbackFailed { add { } remove { } }
    public bool LastSendWasPartial => false;
    internal string Text { get; private set; } = "";
    internal List<string> Operations { get; } = [];
    internal List<bool> ModeCommands { get; } = [];
    internal List<KeyboardEvent> Passed { get; } = [];
    internal int AppendCalls { get; private set; }
    internal int ReplaceCalls { get; private set; }
    internal int ReplacementAttempts { get; private set; }
    internal int NativeBackspaces { get; private set; }
    internal int InjectedBackspaces { get; private set; }
    internal int UnsafeModifierAttempts { get; private set; }

    public WindowContext ReadForegroundContext() => current;
    public bool CurrentTargetMatches(WindowContext expected) =>
        current.Window == expected.Window && current.Focus == expected.Focus
        && current.Layout == expected.Layout && current.ThreadId == expected.ThreadId;

    public bool TrySendReading(WindowContext expected, IReadOnlyList<RomanRun> runs, out string? error)
    {
        if (!Guard(expected, out error)) return false;
        string keys = string.Concat(runs.Select(run => run.Keys));
        Text += Decode(keys);
        ++AppendCalls;
        Operations.Add($"append:{keys}");
        return true;
    }

    public bool TryReplaceLastKana(WindowContext expected, IReadOnlyList<RomanRun> runs, out string? error)
    {
        ++ReplacementAttempts;
        if (!Guard(expected, out error)) return false;
        string keys = string.Concat(runs.Select(run => run.Keys));
        DeleteNativeTail();
        Text += Decode(keys);
        ++InjectedBackspaces;
        ++ReplaceCalls;
        Operations.Add($"replace:{keys}");
        return true;
    }

    public bool TrySetKanaScript(WindowContext expected, bool katakana, out string? error)
    {
        if (!Guard(expected, out error)) return false;
        current = current.WithImeStatus(true, katakana ? (nuint)0xB : 0x9);
        ModeCommands.Add(katakana);
        Operations.Add($"script:{katakana}");
        return true;
    }

    private bool Guard(WindowContext expected, out string? error)
    {
        error = null;
        if (disposed || !expected.IsInputReady || !CurrentTargetMatches(expected))
        { error = "Simulated target unavailable."; return false; }
        if (physicalDown.Any(key => key is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C))
        { ++UnsafeModifierAttempts; error = "Physical modifier has not been released."; return false; }
        return true;
    }

    internal bool Down(int key)
    {
        bool repeat = !physicalDown.Add(key);
        var ev = MakeEvent(key, true, repeat);
        bool owned = captured.Contains(key);
        if (repeat && !owned && key != 0x08)
        {
            NotifyExternalInput();
            Pass(ev);
            return false;
        }
        bool handled = Key?.Invoke(ev) ?? false;
        if (repeat && !owned) handled = false; // Backspace observer cannot steal ownership.
        if (handled || owned) { captured.Add(key); return true; }
        Pass(ev);
        return false;
    }

    internal void Up(int key)
    {
        var ev = MakeEvent(key, false, false);
        // The hardware modifier is still down during this callback, exactly the
        // ordering the delayed Shift replacement must respect.
        Key?.Invoke(ev);
        if (!captured.Remove(key)) Pass(ev);
        physicalDown.Remove(key);
    }

    private KeyboardEvent MakeEvent(int key, bool down, bool repeat)
    {
        bool Held(int vk) => vk == key ? down : physicalDown.Contains(vk);
        return new KeyboardEvent(key, down, repeat,
            Held(0xA2) || Held(0xA3), Held(0xA4) || Held(0xA5),
            Held(0x5B) || Held(0x5C), Held(0xA0) || Held(0xA1));
    }

    private void Pass(KeyboardEvent key)
    {
        Passed.Add(key);
        if (key.IsDown && key.VirtualKey == 0x08)
        { ++NativeBackspaces; DeleteNativeTail(); }
    }

    private string Decode(string keys)
    {
        if (!Kana.TryGetValue(keys, out var kana))
            throw new InvalidOperationException($"Unexpected test reading: {keys}");
        return current.KatakanaMode ? kana.Katakana : kana.Hiragana;
    }

    private void DeleteNativeTail()
    {
        var starts = StringInfo.ParseCombiningCharacters(Text);
        if (starts.Length != 0) Text = Text[..starts[^1]];
    }

    internal void ChangeFocus() => current =
        new WindowContext(current.Window + 1, current.Focus + 1, current.Layout, current.ThreadId + 1)
            .WithImeStatus(true, current.KatakanaMode ? (nuint)0xB : 0x9);
    internal void NotifyExternalInput() => ExternalInput?.Invoke();
    public void Dispose() => disposed = true;
}

internal static class Check
{
    internal static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected [{expected}], got [{actual}].");
    }
    internal static void True(bool value, string? message = null) => Equal(true, value, message);
    internal static void False(bool value, string? message = null) => Equal(false, value, message);
}
