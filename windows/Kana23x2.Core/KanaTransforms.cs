using System.Globalization;
using System.Text;

namespace Kana23x2.Core;

/// <summary>Grapheme-aware port of ios/Sources/KanaCore/KanaTransforms.swift.</summary>
public static class KanaTransforms
{
    private static readonly IReadOnlyDictionary<string, string> SmallPairs = CreateSmallPairs();
    private static readonly IReadOnlyDictionary<string, string> LargePairs =
        SmallPairs.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static Dictionary<string, string> CreateSmallPairs()
    {
        const string large = "あいうえおつやゆよわかけアイウエオツヤユヨワカケクシストヌハヒフヘホムラリルレロ";
        const string small = "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶㇰㇱㇲㇳㇴㇵㇶㇷㇸㇹㇺㇻㇼㇽㇾㇿ";
        return large.Zip(small).ToDictionary(pair => pair.First.ToString(), pair => pair.Second.ToString());
    }

    public static string ToKatakana(string text) => MapScript(text, 0x3041, 0x3096, 0x60);
    public static string ToHiragana(string text) => MapScript(text, 0x30A1, 0x30F6, -0x60);
    public static string NormalizeReading(string text) =>
        ToHiragana(text.Normalize(NormalizationForm.FormD)).Normalize(NormalizationForm.FormC);

    private static string MapScript(string text, int first, int last, int offset)
    {
        var result = new StringBuilder();
        foreach (var scalar in text.EnumerateRunes())
            result.Append(scalar.Value >= first && scalar.Value <= last
                ? new Rune(scalar.Value + offset).ToString() : scalar.ToString());
        return result.ToString();
    }

    public static string ToggleDakuten(string text) => ToggleMark(text, 0x3099);
    public static string ToggleHandakuten(string text) => ToggleMark(text, 0x309A);

    private static string ToggleMark(string text, int mark)
    {
        if (text.Length == 0) return text;
        var start = LastElementStart(text);
        var scalars = text[start..].Normalize(NormalizationForm.FormD).EnumerateRunes().ToArray();
        if (scalars.Length == 0 || IsMark(scalars[0].Value) ||
            !(scalars.Length == 1 || scalars.Length == 2 && IsMark(scalars[1].Value))) return text;
        var currentMark = scalars.Length == 2 ? scalars[1].Value : 0;
        var replacement = scalars[0].ToString() + (currentMark == mark ? "" : new Rune(mark).ToString());
        return text[..start] + replacement.Normalize(NormalizationForm.FormC);
    }

    public static string ToggleSmall(string text)
    {
        if (text.Length == 0) return text;
        var start = LastElementStart(text);
        var tail = text[start..];
        if (TrySmallPair(tail, out var replacement)) return text[..start] + replacement;
        var scalars = tail.EnumerateRunes().ToArray();
        if (scalars.Length == 2 && IsMark(scalars[1].Value) &&
            TrySmallPair(scalars[0].ToString(), out replacement))
            return text[..start] + replacement + scalars[1];
        return text;
    }

    public static string RemoveLastElement(string text) =>
        text.Length == 0 ? text : text[..LastElementStart(text)];

    private static int LastElementStart(string text) => StringInfo.ParseCombiningCharacters(text)[^1];
    private static bool IsMark(int scalar) => scalar is 0x3099 or 0x309A;
    private static bool TrySmallPair(string value, out string replacement) =>
        SmallPairs.TryGetValue(value, out replacement!) || LargePairs.TryGetValue(value, out replacement!);
}
