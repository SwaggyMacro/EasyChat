using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Values of <c>CGEventFlags</c> for the modifier keys EasyChat sends.</summary>
[Flags]
internal enum EventModifiers : ulong
{
    None = 0,
    Shift = 0x00020000,
    Control = 0x00040000,
    Option = 0x00080000,
    Command = 0x00100000
}

/// <summary>
/// Synthetic keyboard events. Posting these needs Accessibility approval; without it macOS silently
/// drops the events, so callers must check the capability first rather than treat a successful post
/// as a delivered keystroke.
/// </summary>
internal static partial class CoreGraphicsEventNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <summary>Value of <c>kCGHIDEventTap</c>, the lowest tap, so the event reaches every app.</summary>
    private const int HidEventTap = 0;

    /// <summary>Value of <c>kCGEventSourceStateHIDSystemState</c>.</summary>
    private const int HidSystemState = 1;

    [LibraryImport(LibraryPath)]
    private static partial IntPtr CGEventSourceCreate(int stateId);

    [LibraryImport(LibraryPath)]
    private static partial IntPtr CGEventCreateKeyboardEvent(
        IntPtr source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.U1)] bool keyDown);

    [LibraryImport(LibraryPath)]
    private static partial void CGEventSetFlags(IntPtr handle, ulong flags);

    [LibraryImport(LibraryPath)]
    private static partial void CGEventKeyboardSetUnicodeString(
        IntPtr handle,
        nint length,
        ref ushort text);

    [LibraryImport(LibraryPath)]
    private static partial void CGEventPost(int tap, IntPtr handle);

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGEventSourceKeyState(int stateId, ushort virtualKey);

    /// <summary>
    /// Whether a key is physically down right now. This reads the combined session state rather than
    /// an event stream, so it needs no event tap.
    /// </summary>
    internal static bool IsKeyDown(ushort virtualKey) =>
        CGEventSourceKeyState(CombinedSessionState, virtualKey);

    /// <summary>Value of <c>kCGEventSourceStateCombinedSessionState</c>.</summary>
    private const int CombinedSessionState = 0;

    /// <summary>Presses and releases <paramref name="virtualKey"/> with the given modifiers held.</summary>
    internal static void SendKey(ushort virtualKey, EventModifiers modifiers)
    {
        var source = CGEventSourceCreate(HidSystemState);
        try
        {
            Post(source, virtualKey, keyDown: true, modifiers);
            Post(source, virtualKey, keyDown: false, modifiers);
        }
        finally
        {
            if (source != IntPtr.Zero)
                CoreFoundationNative.CFRelease(source);
        }
    }

    /// <summary>
    /// Types one character by attaching its UTF-16 units to a keystroke, which is how macOS delivers
    /// text that no physical key produces on the current layout.
    /// </summary>
    internal static void SendText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return;

        var source = CGEventSourceCreate(HidSystemState);
        try
        {
            Span<ushort> units = MemoryMarshal.Cast<char, ushort>(text.ToArray().AsSpan());
            PostText(source, units, keyDown: true);
            PostText(source, units, keyDown: false);
        }
        finally
        {
            if (source != IntPtr.Zero)
                CoreFoundationNative.CFRelease(source);
        }
    }

    private static void Post(IntPtr source, ushort virtualKey, bool keyDown, EventModifiers modifiers)
    {
        var handle = CGEventCreateKeyboardEvent(source, virtualKey, keyDown);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("CoreGraphics refused to create a keyboard event.");

        try
        {
            if (modifiers != EventModifiers.None)
                CGEventSetFlags(handle, (ulong)modifiers);
            CGEventPost(HidEventTap, handle);
        }
        finally
        {
            CoreFoundationNative.CFRelease(handle);
        }
    }

    private static void PostText(IntPtr source, Span<ushort> units, bool keyDown)
    {
        var handle = CGEventCreateKeyboardEvent(source, 0, keyDown);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("CoreGraphics refused to create a keyboard event.");

        try
        {
            CGEventKeyboardSetUnicodeString(handle, units.Length, ref units[0]);
            CGEventPost(HidEventTap, handle);
        }
        finally
        {
            CoreFoundationNative.CFRelease(handle);
        }
    }
}
