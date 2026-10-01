using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Capture;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Reads the pointer in the contract's unified physical pixel space.
/// </summary>
/// <remarks>
/// macOS answers the location in points, so the display list is re-read on each call rather than
/// cached: a display can be attached, detached or rescaled between two readings, and a stale scale
/// would place the pointer on the wrong part of the desktop.
/// </remarks>
public sealed class MacPointerPosition : IPointerPosition
{
    public PhysicalScreenPoint GetCurrent()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            return default;

        var handle = DisplayNative.CGEventCreate(IntPtr.Zero);
        if (handle == IntPtr.Zero)
            return default;

        try
        {
            return MacDisplayGeometry.ToPhysicalPoint(
                DisplayNative.CGEventGetLocation(handle),
                MacDisplayGeometry.GetDisplays());
        }
        finally
        {
            CoreFoundationNative.CFRelease(handle);
        }
    }
}
