using System;
using System.Collections.Generic;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Shortcuts;

/// <summary>
/// Utility class for parsing key combination strings into Key and KeyModifiers.
/// </summary>
public static class KeyCombinationParser
{
    /// <summary>
    /// Maps Avalonia's OEM key aliases to our custom Key enum names.
    /// Avalonia reports keys like "Oem3" but our enum uses "OemTilde".
    /// </summary>
    private static readonly Dictionary<string, string> KeyNameTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Oem1", "OemSemicolon" },    // ; :
        { "Oem2", "OemQuestion" },      // / ?
        { "Oem3", "OemTilde" },         // ` ~ (backtick)
        { "Oem4", "OemOpenBrackets" },  // [ {
        { "Oem5", "OemPipe" },          // \ |
        { "Oem6", "OemCloseBrackets" }, // ] }
        { "Oem7", "OemQuotes" },        // ' "
    };
    
    /// <summary>
    /// Parse a key combination string like "Ctrl+Alt+A" into modifiers and key.
    /// </summary>
    /// <param name="combination">The key combination string (e.g., "Ctrl+Shift+F1")</param>
    /// <returns>A tuple of (modifiers, key) if parsing succeeds, null otherwise.</returns>
    public static (KeyModifiers modifiers, Key key)? Parse(string combination)
    {
        if (string.IsNullOrWhiteSpace(combination)) return null;

        var parts = combination.Split(new[] { "+", " " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        KeyModifiers modifiers = KeyModifiers.None;
        Key key = (Key)0;
        bool keyFound = false;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Shift;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Windows;
            else
            {
                // Try to translate Avalonia's key name to our custom enum name
                var keyName = KeyNameTranslations.TryGetValue(part, out var translated) ? translated : part;
                
                if (Enum.TryParse<Key>(keyName, true, out var k))
                {
                    key = k;
                    keyFound = true;
                }
            }
        }

        if (!keyFound) return null;

        modifiers |= KeyModifiers.NoRepeat;
        return (modifiers, key);
    }
}
