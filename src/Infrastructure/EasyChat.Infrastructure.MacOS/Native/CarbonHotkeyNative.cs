using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Values of the Carbon modifier mask used by <c>RegisterEventHotKey</c>.</summary>
[Flags]
internal enum CarbonModifiers : uint
{
    None = 0,
    Command = 0x0100,
    Shift = 0x0200,
    Option = 0x0800,
    Control = 0x1000
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventHotKeyId
{
    internal uint Signature;
    internal uint Id;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventTypeSpec
{
    internal uint EventClass;
    internal uint EventKind;
}

/// <summary>
/// Carbon hot keys.
/// </summary>
/// <remarks>
/// Carbon is chosen over a <c>CGEventTap</c> because <c>RegisterEventHotKey</c> needs no privacy
/// approval at all, while a tap needs Input Monitoring before it sees a single key. It also
/// registers by virtual key code, which is a physical key position, so a shortcut keeps working
/// after the user switches input source. Events are delivered on the application event target, which
/// means the main thread's run loop has to be pumping — true inside the app, not inside a test host.
/// </remarks>
internal static partial class CarbonHotkeyNative
{
    private const string LibraryPath = "/System/Library/Frameworks/Carbon.framework/Carbon";

    /// <summary>Four-character code <c>keyb</c>, the value of <c>kEventClassKeyboard</c>.</summary>
    internal const uint KeyboardEventClass = 0x6B657962;

    /// <summary>Value of <c>kEventHotKeyPressed</c>.</summary>
    internal const uint HotKeyPressed = 5;

    /// <summary>Value of <c>kEventHotKeyReleased</c>.</summary>
    internal const uint HotKeyReleased = 6;

    /// <summary>Four-character code <c>EZCH</c>, EasyChat's hot key signature.</summary>
    internal const uint Signature = 0x455A4348;

    /// <summary>Four-character code <c>----</c>, the value of <c>kEventParamDirectObject</c>.</summary>
    private const uint DirectObjectParameter = 0x2D2D2D2D;

    /// <summary>Four-character code <c>hkid</c>, the value of <c>typeEventHotKeyID</c>.</summary>
    private const uint HotKeyIdType = 0x686B6964;

    /// <summary>Value of <c>eventHotKeyExistsErr</c>.</summary>
    internal const int HotKeyExistsError = -9878;

    [LibraryImport(LibraryPath)]
    internal static partial IntPtr GetApplicationEventTarget();

    [LibraryImport(LibraryPath)]
    internal static partial int RegisterEventHotKey(
        uint keyCode,
        uint modifiers,
        EventHotKeyId hotKeyId,
        IntPtr target,
        uint options,
        out IntPtr hotKey);

    [LibraryImport(LibraryPath)]
    internal static partial int UnregisterEventHotKey(IntPtr hotKey);

    [LibraryImport(LibraryPath)]
    internal static partial int InstallEventHandler(
        IntPtr target,
        IntPtr handler,
        nint eventTypeCount,
        [In] EventTypeSpec[] eventTypes,
        IntPtr userData,
        out IntPtr handlerRef);

    [LibraryImport(LibraryPath)]
    internal static partial uint GetEventKind(IntPtr handle);

    [LibraryImport(LibraryPath)]
    private static partial int GetEventParameter(
        IntPtr handle,
        uint name,
        uint desiredType,
        IntPtr actualType,
        nint bufferSize,
        IntPtr actualSize,
        out EventHotKeyId data);

    internal static unsafe bool TryReadHotKeyId(IntPtr handle, out EventHotKeyId hotKeyId) =>
        GetEventParameter(
            handle,
            DirectObjectParameter,
            HotKeyIdType,
            IntPtr.Zero,
            sizeof(EventHotKeyId),
            IntPtr.Zero,
            out hotKeyId) == 0;
}
