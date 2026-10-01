using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Reads and writes the text element that currently holds the keyboard focus, through the
/// accessibility API. The caller owns releasing the element it is handed.
/// </summary>
internal static class MacAccessibilityText
{
    /// <summary>
    /// Copies the focused element, or zero when there is none or Accessibility is not approved.
    /// </summary>
    internal static IntPtr CopyFocusedElement()
    {
        if (!AccessibilityNative.IsProcessTrusted())
            return IntPtr.Zero;

        var systemWide = AccessibilityNative.AXUIElementCreateSystemWide();
        if (systemWide == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            return AccessibilityNative.CopyAttribute(
                systemWide,
                AccessibilityNative.FocusedElementAttribute);
        }
        finally
        {
            CoreFoundationNative.CFRelease(systemWide);
        }
    }

    /// <summary>
    /// Answers whether the element is a password field. EasyChat must neither read from nor write
    /// into one: macOS marks these so assistive software leaves them alone, and working around that
    /// would defeat a system protection rather than adapt to it.
    /// </summary>
    internal static bool IsSecureField(IntPtr element)
    {
        var role = AccessibilityNative.CopyAttribute(element, AccessibilityNative.RoleAttribute);
        if (role == IntPtr.Zero)
            return false;

        try
        {
            return string.Equals(
                FoundationNative.ReadString(role),
                AccessibilityNative.SecureTextFieldRole,
                StringComparison.Ordinal);
        }
        finally
        {
            CoreFoundationNative.CFRelease(role);
        }
    }

    /// <summary>
    /// Reads the focused element's selected text, or null when there is none. A password field
    /// answers null: macOS marks those so assistive software leaves them alone, and reading one
    /// anyway would defeat a system protection rather than adapt to it.
    /// </summary>
    internal static string? ReadSelectedText(IntPtr element)
    {
        if (element == IntPtr.Zero || IsSecureField(element))
            return null;

        var value = AccessibilityNative.CopyAttribute(
            element,
            AccessibilityNative.SelectedTextAttribute);
        if (value == IntPtr.Zero)
            return null;

        try
        {
            var text = FoundationNative.ReadString(value);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        finally
        {
            CoreFoundationNative.CFRelease(value);
        }
    }

    /// <summary>Reads the number of characters the focused text element holds.</summary>
    internal static bool TryReadLength(IntPtr element, out int length)
    {
        length = 0;
        var value = AccessibilityNative.CopyAttribute(
            element,
            AccessibilityNative.CharacterCountAttribute);
        if (value == IntPtr.Zero)
            return false;

        try
        {
            if (!CoreFoundationNative.TryReadInt(value, out var count) || count < 0)
                return false;

            length = (int)count;
            return true;
        }
        finally
        {
            CoreFoundationNative.CFRelease(value);
        }
    }
}
