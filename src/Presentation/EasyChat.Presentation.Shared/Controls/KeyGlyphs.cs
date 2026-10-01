using System;
using System.Collections.Generic;

namespace EasyChat.Presentation.Shared.Controls;

/// <summary>
/// How a platform writes modifier keys on screen.
/// </summary>
/// <remarks>
/// The convention is supplied by the host rather than discovered here, because the presentation
/// layer is not allowed to branch on the operating system and, more usefully, because naming
/// modifier keys is exactly the kind of thing a platform owns. A Windows user expects
/// "Ctrl + Shift + A"; a Mac user expects the symbols, unlabelled.
/// </remarks>
public sealed record KeyGlyphConvention(string Control, string Alt, string Shift, string Meta)
{
    /// <summary>Spelled-out names, as used on Windows.</summary>
    public static KeyGlyphConvention Names { get; } = new("Ctrl", "Alt", "Shift", "Win");

    /// <summary>The symbols macOS prints on its keys and in its menus.</summary>
    public static KeyGlyphConvention MacSymbols { get; } = new("⌃", "⌥", "⇧", "⌘");
}

/// <summary>
/// Turns the stored name of a key into what the user sees.
/// </summary>
/// <remarks>
/// Shortcuts are persisted with platform-neutral names, so this is the single place that decides how
/// one is written. The active convention is a process-wide setting, in the same spirit as the
/// current culture: the host chooses it once at start-up and every shortcut in the interface follows.
/// </remarks>
public static class KeyGlyphs
{
    private static readonly IReadOnlyDictionary<string, string> KeyNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OemMinus"] = "-",
            ["OemPlus"] = "+",
            ["OemPeriod"] = ".",
            ["OemComma"] = ",",
            ["OemQuestion"] = "?",
            ["OemOpenBrackets"] = "[",
            ["OemCloseBrackets"] = "]",
            ["OemQuotes"] = "\"",
            ["OemSemicolon"] = ";",
            ["OemTilde"] = "~",
            ["OemPipe"] = "|",
            ["Oem1"] = ";",
            ["Oem2"] = "/",
            ["Oem3"] = "`",
            ["Oem4"] = "[",
            ["Oem5"] = "\\",
            ["Oem6"] = "]",
            ["Oem7"] = "'",
            ["Return"] = "Enter",
            ["Next"] = "PgDn",
            ["Prior"] = "PgUp",
            ["Back"] = "Backspace",
            ["Capital"] = "Caps"
        };

    /// <summary>The convention every shortcut in this process is written with.</summary>
    public static KeyGlyphConvention Convention { get; set; } = KeyGlyphConvention.Names;

    public static string Format(string key) => Format(key, Convention);

    public static string Format(string key, KeyGlyphConvention convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var trimmed = key.Trim();

        // "Win" and "Windows" are how older settings spelled Meta, so they are read as the same
        // modifier rather than shown verbatim on a Mac.
        if (Equals(trimmed, "Meta") || Equals(trimmed, "Win") || Equals(trimmed, "Windows"))
            return convention.Meta;
        if (Equals(trimmed, "Ctrl") || Equals(trimmed, "Control"))
            return convention.Control;
        if (Equals(trimmed, "Alt") || Equals(trimmed, "Option"))
            return convention.Alt;
        if (Equals(trimmed, "Shift"))
            return convention.Shift;

        // Digit keys are stored as D0 to D9.
        if (trimmed.Length == 2
            && (trimmed[0] == 'D' || trimmed[0] == 'd')
            && char.IsDigit(trimmed[1]))
        {
            return trimmed[1].ToString();
        }

        return KeyNames.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    private static bool Equals(string value, string other) =>
        string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
}
