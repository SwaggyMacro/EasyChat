using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Layout of <c>CFRange</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CoreFoundationRange
{
    internal nint Location;
    internal nint Length;
}

/// <summary>
/// Accessibility (TCC) trust checks. <see cref="IsProcessTrusted"/> never prompts;
/// <see cref="PromptForTrust"/> is the only entry point allowed to surface a system dialog.
/// </summary>
internal static partial class AccessibilityNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    /// <summary>Documented literal value of <c>kAXTrustedCheckOptionPrompt</c>.</summary>
    private const string TrustedCheckOptionPrompt = "AXTrustedCheckOptionPrompt";

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrustedWithOptions(IntPtr options);

    /// <summary>Value of <c>kAXFocusedApplicationAttribute</c>.</summary>
    internal const string FocusedApplicationAttribute = "AXFocusedApplication";

    /// <summary>Value of <c>kAXFocusedUIElementAttribute</c>.</summary>
    internal const string FocusedElementAttribute = "AXFocusedUIElement";

    /// <summary>Value of <c>kAXErrorSuccess</c>.</summary>
    internal const int Success = 0;

    [LibraryImport(LibraryPath)]
    internal static partial IntPtr AXUIElementCreateSystemWide();

    [LibraryImport(LibraryPath)]
    internal static partial IntPtr AXUIElementCreateApplication(int processIdentifier);

    [LibraryImport(LibraryPath)]
    internal static partial int AXUIElementCopyAttributeValue(
        IntPtr element,
        IntPtr attribute,
        out IntPtr value);

    [LibraryImport(LibraryPath)]
    internal static partial int AXUIElementGetPid(IntPtr element, out int processIdentifier);

    /// <summary>Value of <c>kAXSelectedTextRangeAttribute</c>.</summary>
    internal const string SelectedTextRangeAttribute = "AXSelectedTextRange";

    /// <summary>Value of <c>kAXSelectedTextAttribute</c>.</summary>
    internal const string SelectedTextAttribute = "AXSelectedText";

    /// <summary>Value of <c>kAXNumberOfCharactersAttribute</c>.</summary>
    internal const string CharacterCountAttribute = "AXNumberOfCharacters";

    /// <summary>Value of <c>kAXRoleAttribute</c>.</summary>
    internal const string RoleAttribute = "AXRole";

    /// <summary>Value of <c>kAXSecureTextFieldRole</c>: a password field.</summary>
    internal const string SecureTextFieldRole = "AXSecureTextField";

    /// <summary>Value of <c>kAXValueTypeCFRange</c>.</summary>
    private const int CfRangeValueType = 4;

    [LibraryImport(LibraryPath)]
    internal static partial int AXUIElementSetAttributeValue(
        IntPtr element,
        IntPtr attribute,
        IntPtr value);

    [LibraryImport(LibraryPath)]
    private static partial IntPtr AXValueCreate(int type, ref CoreFoundationRange value);

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXValueGetValue(
        IntPtr value,
        int type,
        out CoreFoundationRange range);

    internal static bool IsProcessTrusted() => AXIsProcessTrusted();

    /// <summary>Writes an attribute; the caller still owns <paramref name="value"/>.</summary>
    internal static bool SetAttribute(IntPtr element, string attribute, IntPtr value)
    {
        var name = CoreFoundationNative.CreateString(attribute);
        try
        {
            return AXUIElementSetAttributeValue(element, name, value) == Success;
        }
        finally
        {
            CoreFoundationNative.CFRelease(name);
        }
    }

    internal static bool TryReadRange(IntPtr element, string attribute, out CoreFoundationRange range)
    {
        range = default;
        var value = CopyAttribute(element, attribute);
        if (value == IntPtr.Zero)
            return false;

        try
        {
            return AXValueGetValue(value, CfRangeValueType, out range);
        }
        finally
        {
            CoreFoundationNative.CFRelease(value);
        }
    }

    /// <summary>Creates an <c>AXValue</c> holding a range; the caller must release it.</summary>
    internal static IntPtr CreateRange(CoreFoundationRange range) =>
        AXValueCreate(CfRangeValueType, ref range);

    /// <summary>
    /// Reads an attribute of an accessibility element. The caller owns the returned CoreFoundation
    /// object and must release it.
    /// </summary>
    internal static IntPtr CopyAttribute(IntPtr element, string attribute)
    {
        if (element == IntPtr.Zero)
            return IntPtr.Zero;

        var name = CoreFoundationNative.CreateString(attribute);
        try
        {
            return AXUIElementCopyAttributeValue(element, name, out var value) == Success
                ? value
                : IntPtr.Zero;
        }
        finally
        {
            CoreFoundationNative.CFRelease(name);
        }
    }

    /// <summary>
    /// Asks macOS to show the "open System Settings" prompt and reports the trust state observed at
    /// that moment. macOS answers <see langword="false"/> while the user is still deciding, so the
    /// caller must re-check rather than treat the prompt itself as consent.
    /// </summary>
    internal static bool PromptForTrust()
    {
        var key = CoreFoundationNative.CreateString(TrustedCheckOptionPrompt);
        if (key == IntPtr.Zero)
            return AXIsProcessTrusted();

        var options = IntPtr.Zero;
        try
        {
            options = CoreFoundationNative.CreateDictionary(
                [key],
                [CoreFoundationNative.BooleanTrue]);
            return options == IntPtr.Zero
                ? AXIsProcessTrusted()
                : AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            if (options != IntPtr.Zero)
                CoreFoundationNative.CFRelease(options);
            CoreFoundationNative.CFRelease(key);
        }
    }
}
