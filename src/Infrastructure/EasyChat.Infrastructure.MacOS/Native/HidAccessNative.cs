using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

internal enum HidAccessType : uint
{
    Granted = 0,
    Denied = 1,
    Unknown = 2
}

/// <summary>
/// Input Monitoring (TCC) access for listen-only event taps.
/// </summary>
internal static partial class HidAccessNative
{
    private const string LibraryPath = "/System/Library/Frameworks/IOKit.framework/IOKit";

    /// <summary>Value of <c>kIOHIDRequestTypeListenEvent</c>.</summary>
    private const uint ListenEventRequest = 1;

    [LibraryImport(LibraryPath)]
    private static partial uint IOHIDCheckAccess(uint requestType);

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool IOHIDRequestAccess(uint requestType);

    internal static HidAccessType CheckListenAccess() =>
        (HidAccessType)IOHIDCheckAccess(ListenEventRequest);

    internal static bool RequestListenAccess() => IOHIDRequestAccess(ListenEventRequest);
}
