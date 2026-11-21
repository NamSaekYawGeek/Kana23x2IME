using Kana23x2.Core;

namespace Kana23x2.Desktop;

/// <summary>The native input boundary used by the Windows controller.</summary>
public interface IKeyboardBridge : IDisposable
{
    event Func<KeyboardEvent, bool>? Key;
    event Action? MouseActivity;
    event Action? ExternalInput;
    event Action<Exception>? CallbackFailed;
    bool LastSendWasPartial { get; }
    WindowContext ReadForegroundContext();
    bool CurrentTargetMatches(WindowContext expected);
    bool TrySendReading(WindowContext expected, IReadOnlyList<RomanRun> runs, out string? error);
    bool TryReplaceLastKana(WindowContext expected, IReadOnlyList<RomanRun> replacement, out string? error);
    bool TrySetKanaScript(WindowContext expected, bool katakana, out string? error);
}
