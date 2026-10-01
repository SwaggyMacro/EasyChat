using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Values of <c>CGEventType</c> that the pointer tap listens for.</summary>
internal static class EventTapType
{
    internal const uint LeftMouseDown = 1;
    internal const uint LeftMouseUp = 2;

    /// <summary>
    /// Values of <c>kCGEventTapDisabledByTimeout</c> and <c>kCGEventTapDisabledByUserInput</c>.
    /// macOS delivers these regardless of the requested mask, and the tap stays dead until it is
    /// explicitly re-enabled.
    /// </summary>
    internal const uint DisabledByTimeout = 0xFFFFFFFE;

    internal const uint DisabledByUserInput = 0xFFFFFFFF;
}

/// <summary>
/// A listen-only session event tap. Creating one needs Accessibility or Input Monitoring approval;
/// without it <c>CGEventTapCreate</c> answers null, which the caller reports rather than treating as
/// a running monitor.
/// </summary>
internal static partial class EventTapNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <summary>Value of <c>kCGSessionEventTap</c>.</summary>
    private const uint SessionEventTap = 1;

    /// <summary>Value of <c>kCGHeadInsertEventTap</c>.</summary>
    private const uint HeadInsert = 0;

    /// <summary>
    /// Value of <c>kCGEventTapOptionListenOnly</c>: EasyChat observes clicks and never alters or
    /// swallows them, so a failure here can never make the user's mouse stop working.
    /// </summary>
    private const uint ListenOnly = 1;

    /// <summary>Value of <c>kCGMouseEventClickState</c>: the click count macOS itself accumulated.</summary>
    private const uint ClickStateField = 1;

    [LibraryImport(LibraryPath)]
    private static partial IntPtr CGEventTapCreate(
        uint tap,
        uint place,
        uint options,
        ulong eventsOfInterest,
        IntPtr callback,
        IntPtr userInfo);

    [LibraryImport(LibraryPath)]
    internal static partial void CGEventTapEnable(
        IntPtr tap,
        [MarshalAs(UnmanagedType.U1)] bool enable);

    [LibraryImport(LibraryPath)]
    internal static partial long CGEventGetIntegerValueField(IntPtr handle, uint field);

    internal static IntPtr CreateMouseTap(IntPtr callback, IntPtr userInfo) =>
        CGEventTapCreate(
            SessionEventTap,
            HeadInsert,
            ListenOnly,
            (1UL << (int)EventTapType.LeftMouseDown) | (1UL << (int)EventTapType.LeftMouseUp),
            callback,
            userInfo);

    /// <summary>How many clicks macOS counted in this sequence; 2 or more is a double click.</summary>
    internal static long ClickCount(IntPtr handle) =>
        CGEventGetIntegerValueField(handle, ClickStateField);
}
