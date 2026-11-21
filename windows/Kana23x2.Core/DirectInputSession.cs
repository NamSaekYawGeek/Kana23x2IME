using System.Globalization;

namespace Kana23x2.Core;

/// <summary>
/// Layout board and a bounded history of the current locally emitted segment.
/// History is bookkeeping, not proof of composition in another process. The
/// caller clears it at focus, commit and other editing boundaries, and only
/// records accepted emissions/deletions. Native editing remains app-dependent.
/// </summary>
public sealed class DirectInputSession
{
    public const int HistoryCapacity = 256;
    private sealed record Entry(string Kana, char? SourceKey, int Board, bool Katakana);
    private readonly List<Entry> history = [];

    public int Board { get; private set; }
    public int HistoryCount => history.Count;
    public string Tail => history.Count == 0 ? "" : history[^1].Kana;

    /// <summary>Plans a mapped key without assuming that it was delivered.</summary>
    public bool TryGetKana(char key, bool nativeKatakana, out string kana) =>
        KanaLayout.TryGetKana(key, Board, nativeKatakana, out kana);

    /// <summary>
    /// Append a successfully queued new kana. SourceKey is its originating mapped
    /// physical letter; a long mark or other literal has no board-swap provenance.
    /// </summary>
    public void Remember(string kana, char? sourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(kana);
        if (kana.Length == 0) { ClearTail(); return; }
        ValidateKana(kana);
        if (sourceKey.HasValue)
        {
            sourceKey = char.ToUpperInvariant(sourceKey.Value);
            if (!KanaLayout.Keys.Contains(sourceKey.Value))
                throw new ArgumentException("Source key must be one of the mapped kana letters.", nameof(sourceKey));
        }
        if (history.Count == HistoryCapacity) history.RemoveAt(0);
        history.Add(new Entry(kana, sourceKey, Board, kana[0] is >= '\u30A1' and <= '\u30FA'));
    }

    /// <summary>
    /// Update an accepted X/C or board-swap replacement without appending a new
    /// character. A successful board swap flips this entry's provenance only;
    /// the active board and all preceding entries are unchanged.
    /// </summary>
    public bool ReplaceTail(string kana, bool swapBoard = false)
    {
        ValidateKana(kana);
        if (history.Count == 0) return false;
        var previous = history[^1];
        history[^1] = previous with { Kana = kana, Board = swapBoard ? previous.Board ^ 1 : previous.Board };
        return true;
    }

    /// <summary>Record one accepted native Backspace; never resurrect an evicted entry.</summary>
    public bool Backspace()
    {
        if (history.Count == 0) return false;
        history.RemoveAt(history.Count - 1);
        return true;
    }

    /// <summary>
    /// Plan the opposite board's base kana for the remembered physical letter.
    /// This deliberately drops X/C modifications: e.g. Y/は → ば → Shift/か,
    /// followed by Shift/は. The original script is retained through each swap.
    /// </summary>
    public bool TryPlanBoardSwap(out string replacement)
    {
        replacement = "";
        if (history.Count == 0 || history[^1].SourceKey is not char sourceKey) return false;
        var previous = history[^1];
        return KanaLayout.TryGetKana(sourceKey, previous.Board ^ 1, previous.Katakana, out replacement);
    }

    /// <summary>Forget the entire editable local segment at an input boundary.</summary>
    public void ClearTail() => history.Clear();

    // Z selects the board for subsequent input and C without rewriting provenance.
    public void ToggleBoard() => Board ^= 1;

    private static void ValidateKana(string kana)
    {
        ArgumentNullException.ThrowIfNull(kana);
        if (StringInfo.ParseCombiningCharacters(kana).Length != 1)
            throw new ArgumentException("Exactly one kana grapheme is required.", nameof(kana));
    }

    /// <summary>
    /// Plans X/C against the remembered kana. Neither success nor failure changes
    /// Tail. The caller must revalidate its target, timeout and input-event
    /// boundaries before injecting. Those checks do not establish composition
    /// state. Call ReplaceTail only when the replacement was successfully queued.
    /// </summary>
    public bool TryTransform(char key, out string replacement, out string unsupported)
    {
        replacement = "";
        unsupported = "";
        if (Tail.Length == 0) return false;

        var transformed = char.ToUpperInvariant(key) switch
        {
            'X' => KanaTransforms.ToggleDakuten(Tail),
            'C' when Board == 0 => KanaTransforms.ToggleSmall(Tail),
            'C' => KanaTransforms.ToggleHandakuten(Tail),
            _ => Tail
        };
        if (transformed == Tail) return false;
        if (!Romanization.TryEncode(transformed, out _, out unsupported)) return false;
        replacement = transformed;
        return true;
    }
}
