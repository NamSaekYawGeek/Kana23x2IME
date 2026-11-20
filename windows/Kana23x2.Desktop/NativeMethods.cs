using System.Runtime.InteropServices;

namespace Kana23x2.Desktop;

internal static class NativeMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WhMouseLl = 14;
    internal const uint WmKeyDown = 0x0100;
    internal const uint WmKeyUp = 0x0101;
    internal const uint WmSysKeyDown = 0x0104;
    internal const uint WmSysKeyUp = 0x0105;
    internal const uint WmImeControl = 0x0283;
    internal const uint ImcGetConversionMode = 0x0001;
    internal const uint ImcGetOpenStatus = 0x0005;
    internal const uint LlkhfExtended = 0x01;
    internal const uint LlkhfInjected = 0x10;
    internal const uint LlmhfInjected = 0x01;
    internal const uint KeyeventfKeyup = 0x0002;
    internal static readonly nuint InjectedInputTag = 0x4B323358;

    internal delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHookData
    {
        internal Point Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint Active;
        internal nint Focus;
        internal nint Capture;
        internal nint MenuOwner;
        internal nint MoveSize;
        internal nint Caret;
        internal Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Data;

        internal static Input Key(ushort key, bool up = false) => new()
        {
            Type = 1,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = up ? KeyeventfKeyup : 0,
                    ExtraInfo = InjectedInputTag
                }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
        [FieldOffset(0)] internal HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        internal uint Message;
        internal ushort ParamLow;
        internal ushort ParamHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookExW(int idHook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    internal static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern short VkKeyScanExW(char character, nint layout);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, [In] Input[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SendMessageTimeoutW(nint window, uint message, nuint wParam,
        nint lParam, uint flags, uint timeout, out nuint result);

    [DllImport("imm32.dll")]
    internal static extern nint ImmGetDefaultIMEWnd(nint window);
}
