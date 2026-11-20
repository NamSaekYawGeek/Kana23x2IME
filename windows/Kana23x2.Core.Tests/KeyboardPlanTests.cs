using Kana23x2.Core;
using Kana23x2.Desktop;

/// <summary>Exercises the production event planner without invoking Windows APIs.</summary>
public static class KeyboardPlanTests
{
    public static void OrdinaryPlansNeverSwitchScript()
    {
        foreach (bool nativeKatakana in new[] { false, true })
        {
            // Script metadata, even alternating or contrary to the native mode,
            // must not manufacture an IME mode key for ordinary mapped input.
            RomanRun[] runs = [new(!nativeKatakana, "a"), new(nativeKatakana, "ni")];
            var batch = Plan(runs, nativeKatakana, null, false);
            Expect(batch, Down(0x41), Up(0x41), Down(0x4E), Up(0x4E), Down(0x49), Up(0x49));
        }
    }

    public static void BoardRoundTripFollowsNativeScript()
    {
        foreach (bool nativeKatakana in new[] { false, true })
        {
            var session = new DirectInputSession();
            var emitted = new List<NativeMethods.Input>();
            string[] expectedKana = nativeKatakana ? ["ア", "ナ", "ア"] : ["あ", "な", "あ"];
            for (int index = 0; index < expectedKana.Length; ++index)
            {
                if (index != 0) session.ToggleBoard(); // Physical Q, Z, Q, Z, Q.
                Require(session.TryGetKana('Q', nativeKatakana, out string kana), "Q should map.");
                Require(kana == expectedKana[index], "Board/script mapping differs from native mode.");
                Require(session.RequestedKatakana is null, "Z must not create a script request.");
                Require(Romanization.TryEncode(kana, out var runs, out _), "Mapped Q must encode.");
                emitted.AddRange(Plan(runs, nativeKatakana, session.RequestedKatakana, false));
                session.Remember(kana);
            }
            Expect(emitted.ToArray(),
                Down(0x41), Up(0x41),
                Down(0x4E), Up(0x4E), Down(0x41), Up(0x41),
                Down(0x41), Up(0x41));
        }
    }

    public static void ExplicitScriptRequestPrecedesKana()
    {
        foreach (bool nativeKatakana in new[] { false, true })
        {
            var session = new DirectInputSession();
            session.ToggleScript(nativeKatakana);
            Require(session.RequestedKatakana == !nativeKatakana, "Shift must request the other script.");
            Require(session.TryGetKana('Q', nativeKatakana, out string kana), "Q should map.");
            Require(Romanization.TryEncode(kana, out var runs, out _), "Q should encode.");
            var batch = Plan(runs, nativeKatakana, session.RequestedKatakana, false);
            if (nativeKatakana)
                Expect(batch, Down(0x16), Up(0x16), Down(0x41), Up(0x41));
            else
                Expect(batch, Down(0xA0), Down(0x16), Up(0x16), Up(0xA0), Down(0x41), Up(0x41));

            // Consuming the successful one-shot request leaves subsequent kana
            // following the actual observed native mode, with no repeated chord.
            session.Remember(kana);
            Require(session.RequestedKatakana is null, "Successful send should consume the request.");
            Expect(Plan(runs, !nativeKatakana, session.RequestedKatakana, false), Down(0x41), Up(0x41));

            // A request already matching native mode needs no event at all.
            Expect(Plan(runs, nativeKatakana, nativeKatakana, false), Down(0x41), Up(0x41));
        }

        // Multiple runs still get only one explicit request, released before text.
        RomanRun[] mixed = [new(false, "a"), new(true, "i"), new(false, "u")];
        Expect(Plan(mixed, false, true, false),
            Down(0xA0), Down(0x16), Up(0x16), Up(0xA0),
            Down(0x41), Up(0x41), Down(0x49), Up(0x49), Down(0x55), Up(0x55));
    }

    public static void ReplacementNeverSwitchesScript()
    {
        foreach (bool nativeKatakana in new[] { false, true })
        {
            var session = new DirectInputSession();
            session.Remember(nativeKatakana ? "カ" : "か");
            Require(session.TryTransform('X', out string voiced, out _), "X should voice ka.");
            Require(Romanization.TryEncode(voiced, out var runs, out _), "Voiced ka should encode.");
            Expect(Plan(runs, nativeKatakana, null, true),
                Down(0x08), Up(0x08), Down(0x47), Up(0x47), Down(0x41), Up(0x41));

            session.Remember(nativeKatakana ? "ハ" : "は");
            session.ToggleBoard();
            Require(session.TryTransform('C', out string semi, out _), "Second-board C should produce pa.");
            Require(Romanization.TryEncode(semi, out runs, out _), "Pa should encode.");
            Expect(Plan(runs, nativeKatakana, null, true),
                Down(0x08), Up(0x08), Down(0x50), Up(0x50), Down(0x41), Up(0x41));

            // Even conflicting metadata/requests must not switch modes in a replacement.
            Expect(Plan([new RomanRun(!nativeKatakana, "ga")], nativeKatakana, !nativeKatakana, true),
                Down(0x08), Up(0x08), Down(0x47), Up(0x47), Down(0x41), Up(0x41));
        }
    }

    public static void InvalidPlanNeverReturnsDeletionOrPartialInput()
    {
        foreach (bool replacement in new[] { false, true })
        {
            Reject([new(false, "a漢")], replacement, ResolveAscii);
            Reject([new(false, "a?")], replacement, ResolveAscii);
            // Fail after a valid prefix, including a pending explicit script request.
            Reject([new(false, "ag")], replacement, c => c == 'g' ? (short)-1 : ResolveAscii(c));
            Reject([new(false, "ag")], replacement, c => c == 'g' ? (short)0x0247 : ResolveAscii(c));
            Reject([new(false, "ag")], replacement, c => c == 'g' ? (short)0x0447 : ResolveAscii(c));
        }
        Reject([], true, ResolveAscii);
        Reject([new(false, "")], true, ResolveAscii);
        Expect(Plan([], false, true, false)); // Empty input must not change IME mode either.
    }

    private static NativeMethods.Input[] Plan(IReadOnlyList<RomanRun> runs, bool currentKatakana,
        bool? requestedKatakana, bool replacement)
    {
        Require(KeyboardBridge.TryBuildInputPlan(runs, currentKatakana, requestedKatakana, replacement,
            ResolveAscii, out var batch, out var error), $"Expected valid event plan: {error}");
        Require(error is null, "Successful plan returned an error.");
        return batch;
    }

    private static void Reject(IReadOnlyList<RomanRun> runs, bool replacement, Func<char, short> resolver)
    {
        Require(!KeyboardBridge.TryBuildInputPlan(runs, false, true, replacement, resolver,
            out var batch, out var error), "Invalid plan was accepted.");
        Require(batch.Length == 0, "Invalid plan leaked a prefix, possibly a Backspace or mode key.");
        Require(!string.IsNullOrEmpty(error), "Rejected plan should explain the failure.");
    }

    private static short ResolveAscii(char value) => value switch
    {
        >= 'a' and <= 'z' => (short)char.ToUpperInvariant(value),
        >= '0' and <= '9' => (short)value,
        '-' => 0xBD,
        ',' => 0xBC,
        '.' => 0xBE,
        '\'' => 0xDE,
        _ => -1
    };

    private static (ushort Key, bool Up) Down(ushort key) => (key, false);
    private static (ushort Key, bool Up) Up(ushort key) => (key, true);

    private static void Expect(NativeMethods.Input[] batch, params (ushort Key, bool Up)[] expected)
    {
        Require(batch.Length == expected.Length, $"Expected {expected.Length} events, got {batch.Length}.");
        for (int index = 0; index < expected.Length; ++index)
        {
            var actual = batch[index];
            var key = actual.Data.Keyboard;
            Require(actual.Type == 1, "Only keyboard input is expected.");
            Require(key.VirtualKey == expected[index].Key,
                $"Event {index}: expected VK {expected[index].Key:X2}, got {key.VirtualKey:X2}.");
            Require(key.Flags == (expected[index].Up ? NativeMethods.KeyeventfKeyup : 0),
                $"Event {index}: wrong key edge or unexpected Unicode/scan-code flags.");
            Require(key.ScanCode == 0, "Planner must emit virtual-key events.");
            Require(key.ExtraInfo == NativeMethods.InjectedInputTag, "Events must bypass our own hook.");
            Require(key.VirtualKey != 0x14, "Caps Lock must never be used as an implicit script switch.");
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
