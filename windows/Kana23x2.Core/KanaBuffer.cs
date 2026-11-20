namespace Kana23x2.Core;

/// <summary>
/// Local, uncommitted reading. Editing never sends Backspace to another application.
/// Changing board/script affects future keys, matching the mobile implementations.
/// </summary>
public sealed class KanaBuffer(string text = "")
{
    public string Text { get; private set; } = text;
    public bool IsEmpty => Text.Length == 0;
    public int Board { get; private set; }
    public bool Katakana { get; private set; }

    public bool AppendKey(char key)
    {
        if (!KanaLayout.TryGetKana(key, Board, Katakana, out var kana)) return false;
        Text += kana;
        return true;
    }

    public void AppendLongVowel() => Text += "ー";
    public void AppendLiteral(string value) => Text += value;
    public void ToggleBoard() => Board ^= 1;
    public void ToggleScript() => Katakana = !Katakana;
    public void ToggleDakuten() => Text = KanaTransforms.ToggleDakuten(Text);
    public void ToggleC() => Text = Board == 0
        ? KanaTransforms.ToggleSmall(Text) : KanaTransforms.ToggleHandakuten(Text);
    public void Backspace() => Text = KanaTransforms.RemoveLastElement(Text);
    public void Clear() => Text = "";
}
