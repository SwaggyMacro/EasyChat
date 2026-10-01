using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Layout of <c>CGRect</c>, whose members are <c>CGFloat</c> (double on 64-bit).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CoreGraphicsRect
{
    internal double X;
    internal double Y;
    internal double Width;
    internal double Height;
}

/// <summary>Layout of <c>CGPoint</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CoreGraphicsPoint
{
    internal double X;
    internal double Y;
}

/// <summary>
/// Display enumeration and geometry.
/// </summary>
/// <remarks>
/// <c>CGDisplayBounds</c> already uses the global display space, whose origin is the top-left of the
/// main display with y growing downwards. That is the orientation the contract wants, so no flip is
/// needed here — the bottom-left origin belongs to AppKit's <c>NSScreen</c>, which this adapter does
/// not use. What <c>CGDisplayBounds</c> does report is points, not pixels.
/// </remarks>
internal static partial class DisplayNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const int MaximumDisplays = 32;

    [LibraryImport(LibraryPath)]
    private static partial int CGGetActiveDisplayList(
        uint maximumDisplays,
        [Out] uint[] displays,
        out uint count);

    [LibraryImport(LibraryPath)]
    internal static partial uint CGMainDisplayID();

    [LibraryImport(LibraryPath)]
    internal static partial CoreGraphicsRect CGDisplayBounds(uint display);

    [LibraryImport(LibraryPath)]
    private static partial IntPtr CGDisplayCopyDisplayMode(uint display);

    [LibraryImport(LibraryPath)]
    private static partial void CGDisplayModeRelease(IntPtr mode);

    [LibraryImport(LibraryPath)]
    private static partial nint CGDisplayModeGetWidth(IntPtr mode);

    [LibraryImport(LibraryPath)]
    private static partial nint CGDisplayModeGetPixelWidth(IntPtr mode);

    [LibraryImport(LibraryPath)]
    private static partial nint CGDisplayModeGetHeight(IntPtr mode);

    [LibraryImport(LibraryPath)]
    private static partial nint CGDisplayModeGetPixelHeight(IntPtr mode);

    [LibraryImport(LibraryPath)]
    internal static partial uint CGDisplayVendorNumber(uint display);

    [LibraryImport(LibraryPath)]
    internal static partial uint CGDisplayModelNumber(uint display);

    [LibraryImport(LibraryPath)]
    internal static partial uint CGDisplaySerialNumber(uint display);

    [LibraryImport(LibraryPath)]
    internal static partial uint CGDisplayUnitNumber(uint display);

    [LibraryImport(LibraryPath)]
    internal static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport(LibraryPath)]
    internal static partial CoreGraphicsPoint CGEventGetLocation(IntPtr handle);

    internal static uint[] ActiveDisplays()
    {
        var displays = new uint[MaximumDisplays];
        return CGGetActiveDisplayList(MaximumDisplays, displays, out var count) != 0
            ? []
            : displays[..(int)count];
    }

    /// <summary>
    /// The display's backing scale: pixels per point on each axis. A display running at a scaled
    /// resolution still reports the ratio of its mode's pixel size to its point size, which is what
    /// decides how many real pixels a captured region contains.
    /// </summary>
    internal static (double X, double Y) BackingScale(uint display)
    {
        var mode = CGDisplayCopyDisplayMode(display);
        if (mode == IntPtr.Zero)
            return (1d, 1d);

        try
        {
            var points = (CGDisplayModeGetWidth(mode), CGDisplayModeGetHeight(mode));
            var pixels = (CGDisplayModeGetPixelWidth(mode), CGDisplayModeGetPixelHeight(mode));
            return (
                points.Item1 > 0 ? (double)pixels.Item1 / points.Item1 : 1d,
                points.Item2 > 0 ? (double)pixels.Item2 / points.Item2 : 1d);
        }
        finally
        {
            CGDisplayModeRelease(mode);
        }
    }
}
