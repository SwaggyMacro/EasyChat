using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Reads whether a key is physically down, so selection capture can avoid injecting keystrokes
/// while the user is still holding their own.
/// </summary>
/// <remarks>
/// The modifier queries answer true for either side of the keyboard, matching the contract's
/// side-agnostic Control, Alt and Shift, while Command keeps the left and right distinction the
/// contract asks for.
/// </remarks>
public sealed class MacKeyboardState : IKeyboardState
{
    /// <summary>Virtual key codes from HIToolbox, by physical position.</summary>
    private const ushort LeftShift = 56;
    private const ushort RightShift = 60;
    private const ushort LeftControl = 59;
    private const ushort RightControl = 62;
    private const ushort LeftOption = 58;
    private const ushort RightOption = 61;
    private const ushort LeftCommand = 55;
    private const ushort RightCommand = 54;
    private const ushort CKey = 8;
    private const ushort EscapeKey = 53;

    public bool IsPressed(KeyboardKey key)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        return key switch
        {
            KeyboardKey.Control => IsDown(LeftControl) || IsDown(RightControl),
            KeyboardKey.Alt => IsDown(LeftOption) || IsDown(RightOption),
            KeyboardKey.Shift => IsDown(LeftShift) || IsDown(RightShift),
            KeyboardKey.LeftMeta => IsDown(LeftCommand),
            KeyboardKey.RightMeta => IsDown(RightCommand),
            KeyboardKey.C => IsDown(CKey),
            KeyboardKey.Escape => IsDown(EscapeKey),
            _ => false
        };
    }

    private static bool IsDown(ushort virtualKey) =>
        CoreGraphicsEventNative.IsKeyDown(virtualKey);
}
