using Kana23x2.Desktop;
using Kana23x2.Core;

var tests = new (string Name, Action Run)[]
{
    ("Full-width kana input stays ready across ROMAN flag changes", KanaModeReadiness),
    ("Unavailable IME states still pass keys to the editor", InvalidImeStates),
    ("All 46 mobile layout positions and both scripts", Layout),
    ("Unknown keys and boards do not map", InvalidKeys),
    ("Board and script change only subsequent input", BufferModes),
    ("Dakuten and handakuten are reversible and interchangeable", Voicing),
    ("C follows the active board", BoardC),
    ("Small kana pairs include mobile extended katakana", SmallKana),
    ("Combining marks stay with their base", Combining),
    ("Backspace removes complete graphemes", GraphemeDeletion),
    ("Script normalization preserves marks and supplementary scalars", ScriptNormalization),
    ("Every base layout kana can be encoded", BaseEncoding),
    ("Common modified kana and independent n encodings", ModifiedEncoding),
    ("Mixed scripts preserve order and neutral characters", MixedEncoding),
    ("Literal digits preserve pending buffer and script", Digits),
    ("Unsupported input never returns a partial injection", Unsupported),
    ("Empty buffer operations are safe", Empty),
    ("Direct mapping plans never claim successful delivery", DirectPlanning),
    ("Direct transforms require a remembered tail and successful delivery", DirectTransforms),
    ("Direct boundaries prevent edits of previous text", DirectBoundaries),
    ("Direct board and script transitions have distinct tail rules", DirectModes),
    ("Direct ordinary input and Z follow the native script without requesting changes", DirectNativeMode),
    ("Direct Shift requests are explicit and consumed only after successful delivery", DirectScriptRequest),
    ("Direct Shift requests cancel on second tap or explicit invalidation", DirectScriptCancellation),
    ("Ordinary input sends only roman keys in both native scripts", KeyboardPlanTests.OrdinaryPlansNeverSwitchScript),
    ("Q/Z/Q/Z/Q queues only the expected reading", KeyboardPlanTests.BoardRoundTripFollowsNativeScript),
    ("Only a requested script change precedes kana", KeyboardPlanTests.ExplicitScriptRequestPrecedesKana),
    ("Postfix replacement preserves the native script", KeyboardPlanTests.ReplacementNeverSwitchesScript),
    ("Failed keyboard plans cannot delete text or deliver partial input", KeyboardPlanTests.InvalidPlanNeverReturnsDeletionOrPartialInput),
    ("Direct unsupported transforms leave the remembered tail unchanged", DirectUnsupported)
};
var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { ++failures; Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected <{expected}>, got <{actual}>.");
}
static void True(bool value) => Equal(true, value);
static void False(bool value) => Equal(false, value);

static void Layout()
{
    const string keys = "QWERTYUIOPASDFGHJKLVBNM";
    string[] expected = ["あいうえおかきくけこさしすせそたちつてとやゆよ", "なにぬねのはひふへほまみむめもらりるれろわをん"];
    string[] katakana = ["アイウエオカキクケコサシスセソタチツテトヤユヨ", "ナニヌネノハヒフヘホマミムメモラリルレロワヲン"];
    Equal(keys, KanaLayout.Keys);
    for (var board = 0; board < 2; ++board)
    {
        Equal(23, KanaLayout.Boards[board].Length);
        for (var i = 0; i < keys.Length; ++i)
        {
            True(KanaLayout.TryGetKana(keys[i], board, false, out var kana));
            Equal(expected[board][i].ToString(), kana);
            True(KanaLayout.TryGetKana(char.ToLowerInvariant(keys[i]), board, true, out kana));
            Equal(katakana[board][i].ToString(), kana);
        }
    }
}
static void InvalidKeys()
{
    foreach (var key in "ZXC123 ;") False(KanaLayout.TryGetKana(key, 0, false, out _));
    False(KanaLayout.TryGetKana('Q', -1, false, out _));
    False(KanaLayout.TryGetKana('Q', 2, false, out _));
    var buffer = new KanaBuffer("あ");
    False(buffer.AppendKey('X'));
    Equal("あ", buffer.Text);
}
static void BufferModes()
{
    var buffer = new KanaBuffer();
    buffer.AppendKey('Q'); buffer.ToggleScript(); buffer.AppendKey('Q');
    buffer.ToggleBoard(); buffer.AppendKey('Q'); buffer.ToggleScript(); buffer.AppendKey('Q');
    Equal("あアナな", buffer.Text);
    buffer.Clear();
    Equal(1, buffer.Board); False(buffer.Katakana); True(buffer.IsEmpty);
    buffer.AppendLongVowel(); Equal("ー", buffer.Text);
}
static void Voicing()
{
    foreach (var (baseKana, voiced, semi) in new[] { ("は", "ば", "ぱ"), ("ヒ", "ビ", "ピ") })
    {
        Equal(voiced, KanaTransforms.ToggleDakuten(baseKana));
        Equal(baseKana, KanaTransforms.ToggleDakuten(voiced));
        Equal(semi, KanaTransforms.ToggleHandakuten(baseKana));
        Equal(baseKana, KanaTransforms.ToggleHandakuten(semi));
        Equal(voiced, KanaTransforms.ToggleDakuten(semi));
        Equal(semi, KanaTransforms.ToggleHandakuten(voiced));
    }
    Equal("にほんが", KanaTransforms.ToggleDakuten("にほんか"));
    Equal("ゔ", KanaTransforms.ToggleDakuten("う"));
}
static void BoardC()
{
    var buffer = new KanaBuffer("つ");
    buffer.ToggleC(); Equal("っ", buffer.Text); buffer.ToggleC(); Equal("つ", buffer.Text);
    buffer.Clear(); buffer.ToggleBoard(); buffer.AppendKey('Y');
    buffer.ToggleC(); Equal("ぱ", buffer.Text); buffer.ToggleDakuten(); Equal("ば", buffer.Text);
    buffer.ToggleC(); Equal("ぱ", buffer.Text);
}
static void SmallKana()
{
    Equal("ぁ", KanaTransforms.ToggleSmall("あ"));
    Equal("あ", KanaTransforms.ToggleSmall("ぁ"));
    Equal("ㇰ", KanaTransforms.ToggleSmall("ク"));
    Equal("ク", KanaTransforms.ToggleSmall("ㇰ"));
    Equal("ゕ", KanaTransforms.ToggleSmall("か"));
    Equal("し", KanaTransforms.ToggleSmall("し"));
}
static void Combining()
{
    Equal("か", KanaTransforms.ToggleDakuten("か\u3099"));
    Equal("あ\u3099", KanaTransforms.ToggleDakuten("あ"));
    Equal("ぁ\u3099", KanaTransforms.ToggleSmall("あ\u3099"));
    Equal("あ\u3099", KanaTransforms.ToggleSmall("ぁ\u3099"));
    Equal("\u3099", KanaTransforms.ToggleDakuten("\u3099"));
}
static void GraphemeDeletion()
{
    foreach (var tail in new[] { "か\u3099", "𠮷", "👨‍👩‍👧‍👦", "🇯🇵" })
    {
        var buffer = new KanaBuffer("あ" + tail);
        buffer.Backspace(); Equal("あ", buffer.Text);
    }
}
static void ScriptNormalization()
{
    Equal("わ\u3099", KanaTransforms.NormalizeReading("ヷ"));
    Equal("が", KanaTransforms.NormalizeReading("カ\u3099"));
    Equal("𠮷カ", KanaTransforms.ToKatakana("𠮷か"));
}
static string Encoded(string text)
{
    True(Romanization.TryEncode(text, out var runs, out var unsupported));
    Equal("", unsupported);
    return string.Concat(runs.Select(run => run.Keys));
}
static void BaseEncoding()
{
    string[] expected = ["aiueokakikukekosashisusesotachitsutetoyayuyo", "naninunenohahifuhehomamimumemorarirurerowawonn"];
    for (var i = 0; i < 2; ++i)
    {
        Equal(expected[i], Encoded(KanaLayout.Boards[i]));
        Equal(expected[i], Encoded(KanaTransforms.ToKatakana(KanaLayout.Boards[i])));
    }
}
static void ModifiedEncoding()
{
    Equal("gajididupavu", Encoded("がじぢづぱゔ"));
    Equal("xaxixuxexoxtsuxyaxyuxyoxwa", Encoded("ぁぃぅぇぉっゃゅょゎ"));
    Equal("kixya", Encoded("きゃ"));
    Equal("nna", Encoded("んあ"));
    Equal("nnya", Encoded("んや"));
    Equal("nnnn", Encoded("んん"));
    Equal("ga", Encoded("か\u3099"));
}
static void MixedEncoding()
{
    True(Romanization.TryEncode("あコーヒー、い。", out var runs, out _));
    Equal(3, runs.Count);
    Equal(new RomanRun(false, "a"), runs[0]);
    Equal(new RomanRun(true, "ko-hi-,"), runs[1]);
    Equal(new RomanRun(false, "i."), runs[2]);
}
static void Digits()
{
    var buffer = new KanaBuffer();
    buffer.ToggleBoard(); buffer.ToggleScript(); buffer.AppendKey('Q');
    buffer.AppendLiteral("123、。");
    Equal("ナ123、。", buffer.Text);
    True(Romanization.TryEncode(buffer.Text, out var runs, out _));
    Equal(1, runs.Count); Equal(new RomanRun(true, "na123,."), runs[0]);
    buffer.Clear(); Equal(1, buffer.Board); True(buffer.Katakana);
    Equal("0123456789", Encoded("0123456789"));
}
static void Unsupported()
{
    foreach (var element in new[] { "漢", "ㇰ", "ゕ", "ヵ", "あ\u3099", "ヷ", "a", "😀" })
    {
        False(Romanization.TryEncode("かな" + element + "あと", out var runs, out var unsupported));
        Equal(0, runs.Count); True(unsupported.Length > 0);
    }
}
static void Empty()
{
    var buffer = new KanaBuffer();
    buffer.Backspace(); buffer.ToggleC(); buffer.ToggleDakuten(); buffer.Clear();
    Equal("", buffer.Text);
    True(Romanization.TryEncode("", out var runs, out var unsupported));
    Equal(0, runs.Count); Equal("", unsupported);
}
static void DirectPlanning()
{
    var session = new DirectInputSession();
    True(session.TryGetKana('Y', false, out var kana)); Equal("か", kana);
    Equal("", session.Tail); // A failed send must not authorize a later edit.
    False(session.TryTransform('X', out var replacement, out var unsupported));
    Equal("", replacement); Equal("", unsupported);
    False(session.TryGetKana('Z', false, out _)); Equal("", session.Tail);
    session.Remember(kana); Equal("か", session.Tail);
    True(session.TryGetKana('Q', false, out _)); Equal("か", session.Tail);
}
static void DirectTransforms()
{
    var session = new DirectInputSession();
    foreach (var key in "XC")
    {
        False(session.TryTransform(key, out var missing, out _)); Equal("", missing);
    }
    session.Remember("か");
    True(session.TryTransform('x', out var replacement, out var unsupported));
    Equal("が", replacement); Equal("", unsupported); Equal("か", session.Tail);
    // If native injection failed, repeated planning must still target the old kana.
    True(session.TryTransform('X', out replacement, out _)); Equal("が", replacement);
    session.Remember(replacement);
    True(session.TryTransform('X', out replacement, out _)); Equal("か", replacement);
    session.Remember(replacement);
    False(session.TryTransform('Q', out replacement, out _)); Equal("", replacement);
    session.Remember("し"); False(session.TryTransform('C', out _, out _));
}
static void DirectBoundaries()
{
    var session = new DirectInputSession();
    session.Remember("は"); session.ClearTail();
    False(session.TryTransform('X', out _, out _)); Equal("", session.Tail);
    session.Remember("か\u3099"); // Combining sequences still count as one tail.
    True(session.TryTransform('X', out var replacement, out _)); Equal("か", replacement);
    var rejected = false;
    try { session.Remember("かき"); }
    catch (ArgumentException) { rejected = true; }
    True(rejected); Equal("か\u3099", session.Tail);
    session.Remember(""); Equal("", session.Tail);
}
static void DirectModes()
{
    var session = new DirectInputSession();
    session.Remember("は"); session.ToggleBoard();
    Equal(1, session.Board); Equal("は", session.Tail);
    True(session.TryTransform('C', out var replacement, out _)); Equal("ぱ", replacement);
    True(session.TryGetKana('Y', false, out var mapped)); Equal("は", mapped);
    session.ToggleScript(false); True(session.KatakanaFor(false)); Equal("", session.Tail);
    True(session.TryGetKana('Y', false, out mapped)); Equal("ハ", mapped);
    False(session.TryTransform('C', out _, out _));
    session.ClearTail(); Equal(1, session.Board); Equal<bool?>(true, session.RequestedKatakana);
}
static void DirectNativeMode()
{
    foreach (var nativeKatakana in new[] { false, true })
    {
        var session = new DirectInputSession();
        Equal<bool?>(null, session.RequestedKatakana);
        Equal(nativeKatakana, session.KatakanaFor(nativeKatakana));
        session.Remember(nativeKatakana ? "カ" : "か");
        var previousTail = session.Tail;

        // Z, Q, Z, Q must change the board, never manufacture a mode change.
        session.ToggleBoard(); Equal(1, session.Board); Equal(previousTail, session.Tail);
        True(session.TryGetKana('Q', nativeKatakana, out var secondBoard));
        Equal(nativeKatakana ? "ナ" : "な", secondBoard);
        Equal<bool?>(null, session.RequestedKatakana);
        session.Remember(secondBoard);
        session.ToggleBoard(); Equal(0, session.Board); Equal(secondBoard, session.Tail);
        True(session.TryGetKana('Q', nativeKatakana, out var firstBoard));
        Equal(nativeKatakana ? "ア" : "あ", firstBoard);
        Equal<bool?>(null, session.RequestedKatakana);

        // With no explicit request, each new native observation is authoritative.
        True(session.TryGetKana('Q', !nativeKatakana, out var changedNative));
        Equal(nativeKatakana ? "あ" : "ア", changedNative);
        Equal<bool?>(null, session.RequestedKatakana);
    }
}
static void DirectScriptRequest()
{
    foreach (var nativeKatakana in new[] { false, true })
    {
        var session = new DirectInputSession();
        session.Remember(nativeKatakana ? "カ" : "か");
        session.ToggleScript(nativeKatakana);
        Equal<bool?>(!nativeKatakana, session.RequestedKatakana);
        Equal("", session.Tail);
        True(session.TryGetKana('Q', nativeKatakana, out var kana));
        Equal(nativeKatakana ? "あ" : "ア", kana);
        Equal<bool?>(!nativeKatakana, session.RequestedKatakana);
        False(session.TryGetKana('Z', nativeKatakana, out _));
        Equal<bool?>(!nativeKatakana, session.RequestedKatakana);
        session.ToggleBoard(); Equal(1, session.Board);
        Equal<bool?>(!nativeKatakana, session.RequestedKatakana);

        // Planning or failed delivery does not consume the pending Shift intent.
        True(session.TryGetKana('Q', nativeKatakana, out kana));
        Equal(nativeKatakana ? "な" : "ナ", kana);
        session.Remember(kana);
        Equal(kana, session.Tail); Equal<bool?>(null, session.RequestedKatakana);
        Equal(!nativeKatakana, session.KatakanaFor(!nativeKatakana));
        Equal(nativeKatakana, session.KatakanaFor(nativeKatakana));
    }
}
static void DirectScriptCancellation()
{
    foreach (var nativeKatakana in new[] { false, true })
    {
        var session = new DirectInputSession();
        session.ToggleBoard();
        session.ToggleScript(nativeKatakana);
        session.ToggleScript(nativeKatakana);
        Equal<bool?>(null, session.RequestedKatakana);
        Equal(nativeKatakana, session.KatakanaFor(nativeKatakana));
        session.ToggleScript(nativeKatakana);
        session.ClearScriptRequest(); // Caller observed a focus/control boundary.
        Equal<bool?>(null, session.RequestedKatakana); Equal(1, session.Board);
        True(session.TryGetKana('Q', nativeKatakana, out var kana));
        Equal(nativeKatakana ? "ナ" : "な", kana);
        session.Remember(kana);
        session.ClearScriptRequest(); Equal(kana, session.Tail); Equal(1, session.Board);
    }
}
static void DirectUnsupported()
{
    var session = new DirectInputSession();
    session.Remember("あ");
    False(session.TryTransform('X', out var replacement, out var unsupported));
    Equal("", replacement); Equal("あ\u3099", unsupported); Equal("あ", session.Tail);
    session.Remember("か");
    False(session.TryTransform('C', out replacement, out unsupported));
    Equal("", replacement); Equal("ゕ", unsupported); Equal("か", session.Tail);
    session.Remember("ク");
    False(session.TryTransform('C', out replacement, out unsupported));
    Equal("", replacement); Equal("ㇰ", unsupported); Equal("ク", session.Tail);
}


static void KanaModeReadiness()
{
    var target = new WindowContext(1, 2, 0x0411, 3);
    // Simulate polling the same full-width editor before and after switching boards.
    // A change in 0x10 alone must neither disable mapping nor look like a focus/mode change.
    foreach (uint fullWidthKana in new uint[] { 0x09, 0x0B })
    {
        var advertised = target.WithImeStatus(true, fullWidthKana | 0x10);
        var unadvertised = target.WithImeStatus(true, fullWidthKana);
        True(advertised.IsInputReady);
        True(unadvertised.IsInputReady);
        Equal(advertised, unadvertised);
        Equal(fullWidthKana == 0x0B, unadvertised.KatakanaMode);
    }
}

static void InvalidImeStates()
{
    var japanese = new WindowContext(1, 2, 0x0411, 3);
    False(japanese.IsInputReady);
    False(japanese.WithImeStatus(false, 0x09).IsInputReady);
    False(japanese.WithImeStatus(true, 0x08).IsInputReady); // Full-width alphanumeric.
    False(japanese.WithImeStatus(true, 0x03).IsInputReady); // Half-width katakana.
    False(new WindowContext(1, 2, 0x0409, 3).WithImeStatus(true, 0x09).IsInputReady);
}
