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
            var batch = Plan(runs, false);
            Expect(batch, Down(0x41), Up(0x41), Down(0x4E), Up(0x4E), Down(0x49), Up(0x49));
        }

        // A layout may require Shift for punctuation. Its release must precede
        // the next roman letter, without leaking a modifier into the IME input.
        Require(KeyboardBridge.TryBuildInputPlan([new(false, "'a")], false,
            c => c == '\'' ? (short)0x0137 : ResolveAscii(c), out var shifted, out var error),
            $"Shifted punctuation should encode: {error}");
        Expect(shifted, Down(0xA0), Down(0x37), Up(0x37), Up(0xA0), Down(0x41), Up(0x41));
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
                Require(Romanization.TryEncode(kana, out var runs, out _), "Mapped Q must encode.");
                emitted.AddRange(Plan(runs, false));
                session.Remember(kana, 'Q');
            }
            Expect(emitted.ToArray(),
                Down(0x41), Up(0x41),
                Down(0x4E), Up(0x4E), Down(0x41), Up(0x41),
                Down(0x41), Up(0x41));
        }
    }

    public static void CapsScriptCommandIsStandalone()
    {
        foreach (bool requestedKatakana in new[] { false, true })
        {
            // Caps uses an immediate, separate native mode operation. It neither
            // waits for a kana nor modifies a later character's event plan.
            var command = KeyboardBridge.BuildScriptPlan(requestedKatakana);
            if (requestedKatakana)
                Expect(command, Down(0xA0), Down(0x16), Up(0x16), Up(0xA0));
            else
                Expect(command, Down(0x16), Up(0x16));

            // Once Windows reports that script, subsequent Q simply follows it.
            var session = new DirectInputSession();
            Require(session.TryGetKana('Q', requestedKatakana, out string kana), "Q should map.");
            Require(kana == (requestedKatakana ? "ア" : "あ"), "Q should follow observed native script.");
            Require(Romanization.TryEncode(kana, out var runs, out _), "Q should encode.");
            Expect(Plan(runs, false), Down(0x41), Up(0x41));
            session.Remember(kana, 'Q');
        }
    }

    public static void ReplacementNeverSwitchesScript()
    {
        foreach (bool nativeKatakana in new[] { false, true })
        {
            var session = new DirectInputSession();
            session.Remember(nativeKatakana ? "カ" : "か", 'Y');
            Require(session.TryTransform('X', out string voiced, out _), "X should voice ka.");
            Require(Romanization.TryEncode(voiced, out var runs, out _), "Voiced ka should encode.");
            Expect(Plan(runs, true),
                Down(0x08), Up(0x08), Down(0x47), Up(0x47), Down(0x41), Up(0x41));
            Require(session.ReplaceTail(voiced), "Accepted X must update, not append, the tail.");
            Require(session.HistoryCount == 1, "X replacement must not grow history.");

            // Solo Shift changes this physical Y to the opposite board's base
            // kana, retaining native script. It is a replacement, not a mode key.
            Require(session.TryPlanBoardSwap(out string swapped), "Y must have board provenance.");
            Require(swapped == (nativeKatakana ? "ハ" : "は"), "Shift should swap voiced ka to base ha.");
            Require(Romanization.TryEncode(swapped, out runs, out _), "Board counterpart should encode.");
            Expect(Plan(runs, true),
                Down(0x08), Up(0x08), Down(0x48), Up(0x48), Down(0x41), Up(0x41));
            Require(session.ReplaceTail(swapped, swapBoard: true), "Accepted Shift must update provenance.");
            Require(session.HistoryCount == 1 && session.Board == 0, "Replacement must not grow history or switch active board.");
            Require(session.TryPlanBoardSwap(out string back), "Second Shift should restore the base counterpart.");
            Require(back == (nativeKatakana ? "カ" : "か"), "Second Shift should return base ka.");

            session.ToggleBoard();
            Require(session.TryTransform('C', out string semi, out _), "Second-board C should produce pa.");
            Require(Romanization.TryEncode(semi, out runs, out _), "Pa should encode.");
            Expect(Plan(runs, true),
                Down(0x08), Up(0x08), Down(0x50), Up(0x50), Down(0x41), Up(0x41));
            Require(session.ReplaceTail(semi), "Accepted C must update the tail.");
            Require(session.HistoryCount == 1, "C replacement must not grow history.");

            // Even conflicting metadata must not switch modes in a replacement.
            Expect(Plan([new RomanRun(!nativeKatakana, "ga")], true),
                Down(0x08), Up(0x08), Down(0x47), Up(0x47), Down(0x41), Up(0x41));
        }
    }

    public static void InvalidPlanNeverReturnsDeletionOrPartialInput()
    {
        foreach (bool replacement in new[] { false, true })
        {
            Reject([new(false, "a漢")], replacement, ResolveAscii);
            Reject([new(false, "a?")], replacement, ResolveAscii);
            // Fail after a valid prefix; even replacement Backspace is withheld.
            Reject([new(false, "ag")], replacement, c => c == 'g' ? (short)-1 : ResolveAscii(c));
            Reject([new(false, "ag")], replacement, c => c == 'g' ? (short)0x0247 : ResolveAscii(c));
            Reject([new(false, "ag")], replacement, c => c == 'g' ? (short)0x0447 : ResolveAscii(c));
        }
        Reject([], true, ResolveAscii);
        Reject([new(false, "")], true, ResolveAscii);
        Expect(Plan([], false)); // Empty reading cannot send a standalone mode command.
    }

    private static NativeMethods.Input[] Plan(IReadOnlyList<RomanRun> runs, bool replacement)
    {
        Require(KeyboardBridge.TryBuildInputPlan(runs, replacement,
            ResolveAscii, out var batch, out var error), $"Expected valid event plan: {error}");
        Require(error is null, "Successful plan returned an error.");
        return batch;
    }

    private static void Reject(IReadOnlyList<RomanRun> runs, bool replacement, Func<char, short> resolver)
    {
        Require(!KeyboardBridge.TryBuildInputPlan(runs, replacement, resolver,
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
