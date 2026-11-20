using System.Globalization;

namespace Kana23x2.Core;

/// <summary>
/// Layout mode plus the most recent successfully sent kana. Tail is only a
/// bookkeeping hint, not proof of an active composition in another process.
/// The caller applies target, time and input-event boundaries before attempting
/// the explicitly experimental, application-dependent replacement operation.
/// </summary>
public sealed class DirectInputSession
{
    public int Board { get; private set; }
    /// <summary>An explicit one-shot Shift request; ordinary input follows the native IME.</summary>
    public bool? RequestedKatakana { get; private set; }
    public string Tail { get; private set; } = "";

    public bool KatakanaFor(bool nativeKatakana) => RequestedKatakana ?? nativeKatakana;

    /// <summary>Plans a mapped key without assuming that it was delivered.</summary>
    public bool TryGetKana(char key, bool nativeKatakana, out string kana) =>
        KanaLayout.TryGetKana(key, Board, KatakanaFor(nativeKatakana), out kana);

    /// <summary>Called only after successful injection, never merely after planning.</summary>
    public void Remember(string tail)
    {
        ArgumentNullException.ThrowIfNull(tail);
        if (StringInfo.ParseCombiningCharacters(tail).Length > 1)
            throw new ArgumentException("Only a single trailing grapheme may be remembered.", nameof(tail));
        Tail = tail;
        ClearScriptRequest();
    }

    public void ClearTail() => Tail = "";

    // Z can select which C operation applies to the preceding kana.
    public void ToggleBoard() => Board ^= 1;

    // Shift requests a change relative to the actual IME, not a persistent local
    // mode. A second tap returning to the native mode cancels the request.
    public void ToggleScript(bool nativeKatakana)
    {
        var requested = !KatakanaFor(nativeKatakana);
        RequestedKatakana = requested == nativeKatakana ? null : requested;
        ClearTail();
    }

    /// <summary>Forget an unsent mode request on focus/control invalidation.</summary>
    public void ClearScriptRequest() => RequestedKatakana = null;

    /// <summary>
    /// Plans X/C against the remembered kana. Neither success nor failure changes
    /// Tail. The caller must revalidate its target, timeout and input-event
    /// boundaries before injecting. Those checks do not establish composition
    /// state. Call Remember only when the replacement was successfully queued.
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
