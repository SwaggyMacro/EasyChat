using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>One on-screen window, as much as macOS will say without Screen Recording approval.</summary>
internal readonly record struct WindowListEntry(
    long WindowNumber,
    int OwnerProcessIdentifier,
    CoreGraphicsRect Bounds,
    long Layer);

/// <summary>
/// Enumerates on-screen windows front to back.
/// </summary>
/// <remarks>
/// Window numbers, owner process ids, bounds and layers come back without any privacy approval; only
/// window titles and contents are gated behind Screen Recording, and this adapter asks for neither.
/// </remarks>
internal static partial class WindowListNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <summary>
    /// <c>kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements</c>: visible
    /// windows only, without the desktop picture and its icons.
    /// </summary>
    private const uint OnScreenWindows = 1 | 16;

    /// <summary>Value of <c>kCGNullWindowID</c>.</summary>
    private const uint NullWindow = 0;

    private static readonly Lazy<IntPtr> NumberKey = Key("kCGWindowNumber");
    private static readonly Lazy<IntPtr> OwnerKey = Key("kCGWindowOwnerPID");
    private static readonly Lazy<IntPtr> BoundsKey = Key("kCGWindowBounds");
    private static readonly Lazy<IntPtr> LayerKey = Key("kCGWindowLayer");

    [LibraryImport(LibraryPath)]
    private static partial IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGRectMakeWithDictionaryRepresentation(
        IntPtr dictionary,
        out CoreGraphicsRect rect);

    /// <summary>On-screen windows ordered front to back, so the first match is the topmost.</summary>
    internal static IReadOnlyList<WindowListEntry> OnScreen()
    {
        var array = CGWindowListCopyWindowInfo(OnScreenWindows, NullWindow);
        if (array == IntPtr.Zero)
            return [];

        try
        {
            var count = CoreFoundationNative.CFArrayGetCount(array);
            var entries = new List<WindowListEntry>((int)count);
            for (nint index = 0; index < count; index++)
            {
                var window = CoreFoundationNative.CFArrayGetValueAtIndex(array, index);
                if (window == IntPtr.Zero)
                    continue;

                if (!TryReadNumber(window, NumberKey.Value, out var number)
                    || !TryReadNumber(window, OwnerKey.Value, out var owner))
                {
                    continue;
                }

                TryReadNumber(window, LayerKey.Value, out var layer);
                var boundsDictionary = CoreFoundationNative.CFDictionaryGetValue(
                    window,
                    BoundsKey.Value);
                if (boundsDictionary == IntPtr.Zero
                    || !CGRectMakeWithDictionaryRepresentation(boundsDictionary, out var bounds))
                {
                    continue;
                }

                entries.Add(new WindowListEntry(number, (int)owner, bounds, layer));
            }

            return entries;
        }
        finally
        {
            CoreFoundationNative.CFRelease(array);
        }
    }

    private static bool TryReadNumber(IntPtr window, IntPtr key, out nint value) =>
        CoreFoundationNative.TryReadInt(
            CoreFoundationNative.CFDictionaryGetValue(window, key),
            out value);

    private static Lazy<IntPtr> Key(string name) => new(
        () => CoreFoundationNative.CreateString(name),
        LazyThreadSafetyMode.ExecutionAndPublication);
}
