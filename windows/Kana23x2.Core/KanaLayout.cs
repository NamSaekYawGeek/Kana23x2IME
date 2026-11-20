namespace Kana23x2.Core;

/// <summary>The same 23 physical letter positions and two boards as Android/iOS.</summary>
public static class KanaLayout
{
    public const string Keys = "QWERTYUIOPASDFGHJKLVBNM";
    public static IReadOnlyList<string> Boards { get; } = Array.AsReadOnly(new[]
    {
        "あいうえおかきくけこさしすせそたちつてとやゆよ",
        "なにぬねのはひふへほまみむめもらりるれろわをん"
    });

    public static bool TryGetKana(char key, int board, bool katakana, out string kana)
    {
        var index = Keys.IndexOf(char.ToUpperInvariant(key));
        if (index < 0 || board < 0 || board >= Boards.Count)
        {
            kana = "";
            return false;
        }
        kana = Boards[board][index].ToString();
        if (katakana) kana = KanaTransforms.ToKatakana(kana);
        return true;
    }
}
