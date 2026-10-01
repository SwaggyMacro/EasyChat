using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Screen Recording (TCC) access. The preflight call is silent; the request call prompts once per
/// installation and macOS keeps reporting the old answer until the app is relaunched.
/// </summary>
internal static partial class ScreenCaptureAccessNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGRequestScreenCaptureAccess();

    internal static bool HasAccess() => CGPreflightScreenCaptureAccess();

    internal static bool RequestAccess() => CGRequestScreenCaptureAccess();
}
