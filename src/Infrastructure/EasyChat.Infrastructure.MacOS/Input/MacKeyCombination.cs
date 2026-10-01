using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>One synthetic keystroke: a virtual key code plus the modifiers held with it.</summary>
internal readonly record struct MacKeystroke(ushort VirtualKey, EventModifiers Modifiers);

/// <summary>
/// Parses the shortcut vocabulary shared with Windows ("Ctrl + Shift + A") into macOS virtual key
/// codes.
/// </summary>
/// <remarks>
/// The key codes are the ANSI positions from HIToolbox. They are layout positions, not characters:
/// macOS resolves them through the user's active keyboard layout, which is why a shortcut recorded
/// as "A" keeps working on AZERTY.
/// </remarks>
internal static class MacKeyCombination
{
    private static readonly IReadOnlyDictionary<string, ushort> Keys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0, ["S"] = 1, ["D"] = 2, ["F"] = 3, ["H"] = 4, ["G"] = 5, ["Z"] = 6,
            ["X"] = 7, ["C"] = 8, ["V"] = 9, ["B"] = 11, ["Q"] = 12, ["W"] = 13, ["E"] = 14,
            ["R"] = 15, ["Y"] = 16, ["T"] = 17, ["1"] = 18, ["2"] = 19, ["3"] = 20, ["4"] = 21,
            ["6"] = 22, ["5"] = 23, ["9"] = 25, ["7"] = 26, ["8"] = 28, ["0"] = 29,
            ["O"] = 31, ["U"] = 32, ["I"] = 34, ["P"] = 35, ["L"] = 37, ["J"] = 38, ["K"] = 40,
            ["N"] = 45, ["M"] = 46,
            ["Equal"] = 24, ["Minus"] = 27, ["RightBracket"] = 30, ["LeftBracket"] = 33,
            ["Quote"] = 39, ["Semicolon"] = 41, ["Backslash"] = 42, ["Comma"] = 43,
            ["Slash"] = 44, ["Period"] = 47, ["Grave"] = 50,
            ["Return"] = 36, ["Enter"] = 36, ["Tab"] = 48, ["Space"] = 49,
            ["Back"] = 51, ["Backspace"] = 51, ["Escape"] = 53, ["Esc"] = 53,
            ["Delete"] = 117, ["ForwardDelete"] = 117,
            ["Home"] = 115, ["PageUp"] = 116, ["End"] = 119, ["PageDown"] = 121,
            ["Left"] = 123, ["Right"] = 124, ["Down"] = 125, ["Up"] = 126,
            ["F1"] = 122, ["F2"] = 120, ["F3"] = 99, ["F4"] = 118, ["F5"] = 96, ["F6"] = 97,
            ["F7"] = 98, ["F8"] = 100, ["F9"] = 101, ["F10"] = 109, ["F11"] = 103, ["F12"] = 111
        };

    /// <summary>
    /// The macOS convention for each semantic command. Delete maps to Backspace rather than forward
    /// delete: it removes the current selection in every macOS text control and exists on every Mac
    /// keyboard, whereas forward delete does not.
    /// </summary>
    internal static string ForCommand(StandardTextCommand command) => command switch
    {
        StandardTextCommand.SelectAll => "Meta + A",
        StandardTextCommand.Delete => "Backspace",
        StandardTextCommand.Copy => "Meta + C",
        StandardTextCommand.Paste => "Meta + V",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };

    /// <summary>
    /// Resolves a recorded gesture to a macOS keystroke. The key name travels as the same string on
    /// both platforms, so the shared table is the only place that has to know the macOS positions.
    /// </summary>
    internal static bool TryResolve(ShortcutGesture gesture, out MacKeystroke keystroke)
    {
        keystroke = default;
        if (gesture is null || !Keys.TryGetValue(gesture.Key, out var virtualKey))
            return false;

        var modifiers = EventModifiers.None;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Control))
            modifiers |= EventModifiers.Control;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Alt))
            modifiers |= EventModifiers.Option;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Shift))
            modifiers |= EventModifiers.Shift;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Meta))
            modifiers |= EventModifiers.Command;

        keystroke = new MacKeystroke(virtualKey, modifiers);
        return true;
    }

    /// <summary>Translates the CoreGraphics modifier mask into the Carbon one.</summary>
    internal static CarbonModifiers ToCarbon(EventModifiers modifiers)
    {
        var carbon = CarbonModifiers.None;
        if (modifiers.HasFlag(EventModifiers.Control))
            carbon |= CarbonModifiers.Control;
        if (modifiers.HasFlag(EventModifiers.Option))
            carbon |= CarbonModifiers.Option;
        if (modifiers.HasFlag(EventModifiers.Shift))
            carbon |= CarbonModifiers.Shift;
        if (modifiers.HasFlag(EventModifiers.Command))
            carbon |= CarbonModifiers.Command;
        return carbon;
    }

    internal static bool TryParse(string combination, out MacKeystroke keystroke)
    {
        keystroke = default;
        if (string.IsNullOrWhiteSpace(combination))
            return false;

        var modifiers = EventModifiers.None;
        ushort? virtualKey = null;
        foreach (var part in combination.Split(
                     ['+', ' '],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var modifier = ParseModifier(part);
            if (modifier != EventModifiers.None)
            {
                modifiers |= modifier;
                continue;
            }

            // More than one non-modifier means the combination is not a single keystroke.
            if (virtualKey is not null || !Keys.TryGetValue(part, out var key))
                return false;

            virtualKey = key;
        }

        if (virtualKey is null)
            return false;

        keystroke = new MacKeystroke(virtualKey.Value, modifiers);
        return true;
    }

    /// <summary>
    /// Maps the modifier names EasyChat persists. "Win" and "Windows" are the compatibility aliases
    /// for Meta in older settings, and Meta is Command on macOS.
    /// </summary>
    private static EventModifiers ParseModifier(string value) => value.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => EventModifiers.Control,
        "ALT" or "OPTION" => EventModifiers.Option,
        "SHIFT" => EventModifiers.Shift,
        "WIN" or "WINDOWS" or "META" or "CMD" or "COMMAND" => EventModifiers.Command,
        _ => EventModifiers.None
    };
}
