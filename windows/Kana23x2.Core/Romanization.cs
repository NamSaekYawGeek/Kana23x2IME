using System.Globalization;
using System.Text;

namespace Kana23x2.Core;

public sealed record RomanRun(bool Katakana, string Keys);

/// <summary>
/// Emits ordinary roman keys for the Microsoft Japanese IME's romaji mode.
/// The caller must send keyboard events, not Unicode text or clipboard contents.
/// </summary>
public static class Romanization
{
    private static readonly IReadOnlyDictionary<string, string> Readings = CreateReadings();

    private static Dictionary<string, string> CreateReadings()
    {
        var result = new Dictionary<string, string>();
        Add("あいうえお", "a i u e o");
        Add("かきくけこ", "ka ki ku ke ko");
        Add("さしすせそ", "sa shi su se so");
        Add("たちつてと", "ta chi tsu te to");
        Add("なにぬねの", "na ni nu ne no");
        Add("はひふへほ", "ha hi fu he ho");
        Add("まみむめも", "ma mi mu me mo");
        Add("やゆよ", "ya yu yo");
        Add("らりるれろ", "ra ri ru re ro");
        Add("わをん", "wa wo nn");
        Add("がぎぐげご", "ga gi gu ge go");
        Add("ざじずぜぞ", "za ji zu ze zo");
        Add("だぢづでど", "da di du de do");
        Add("ばびぶべぼ", "ba bi bu be bo");
        Add("ぱぴぷぺぽ", "pa pi pu pe po");
        Add("ゔ", "vu");
        Add("ぁぃぅぇぉっゃゅょゎ", "xa xi xu xe xo xtsu xya xyu xyo xwa");
        // ゕ/ゖ, extended small katakana and nonstandard voiced combinations are
        // deliberately unsupported: direct Unicode would bypass IME composition.
        return result;

        void Add(string kana, string keys)
        {
            var words = keys.Split(' ');
            if (kana.Length != words.Length) throw new InvalidOperationException("Invalid roman table.");
            for (var i = 0; i < kana.Length; ++i) result.Add(kana[i].ToString(), words[i]);
        }
    }

    public static bool TryEncode(string text, out IReadOnlyList<RomanRun> runs, out string unsupported)
    {
        var result = new List<RomanRun>();
        var current = new StringBuilder();
        var katakana = false;
        var enumerator = StringInfo.GetTextElementEnumerator(text.Normalize(NormalizationForm.FormC));
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var digit = element.Length == 1 && element[0] is >= '0' and <= '9';
            var neutral = digit || element is "ー" or "。" or "、";
            var elementKatakana = neutral ? katakana : IsKatakana(element);
            var key = neutral ? element : KanaTransforms.ToHiragana(element);
            string? roman = key switch
            {
                "ー" => "-",
                "。" => ".",
                "、" => ",",
                _ => digit ? element : Readings.GetValueOrDefault(key)
            };
            if (roman is null)
            {
                runs = Array.Empty<RomanRun>();
                unsupported = element;
                return false;
            }
            if (current.Length > 0 && elementKatakana != katakana)
            {
                result.Add(new RomanRun(katakana, current.ToString()));
                current.Clear();
            }
            katakana = elementKatakana;
            current.Append(roman);
        }
        if (current.Length > 0) result.Add(new RomanRun(katakana, current.ToString()));
        runs = result.AsReadOnly();
        unsupported = "";
        return true;
    }

    private static bool IsKatakana(string element) => element.EnumerateRunes().First().Value is >= 0x30A1 and <= 0x30FA;
}
