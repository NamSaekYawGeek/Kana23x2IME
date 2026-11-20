namespace Kana23x2.Desktop;

public sealed record WindowContext(nint Window, nint Focus, nint Layout, uint ThreadId)
{
    public static WindowContext Unknown { get; } = new(0, 0, 0, 0);
    public bool IsJapanese => ((long)Layout & 0xFFFF) == 0x0411;
    public bool IsKnown { get; private init; }
    public bool ImeOpen { get; private init; }
    public bool NativeMode { get; private init; }
    public bool FullWidthMode { get; private init; }
    public bool KatakanaMode { get; init; }
    public bool IsInputReady => IsKnown && IsJapanese && ImeOpen && NativeMode && FullWidthMode;

    // Decode the conversion mode of the focused editor. The physical keyboard's
    // romaji setting is a setup prerequisite. An absent IME_CMODE_ROMAN bit
    // in this snapshot is insufficient to conclude that setup is wrong.
    public WindowContext WithImeStatus(bool open, nuint conversion) => this with
    {
        IsKnown = true,
        ImeOpen = open,
        NativeMode = (conversion & 0x0001) != 0,    // IME_CMODE_NATIVE
        KatakanaMode = (conversion & 0x0002) != 0,  // IME_CMODE_KATAKANA
        FullWidthMode = (conversion & 0x0008) != 0  // IME_CMODE_FULLSHAPE
    };
}
