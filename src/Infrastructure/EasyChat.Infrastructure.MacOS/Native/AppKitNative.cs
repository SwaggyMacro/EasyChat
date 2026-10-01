using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Values of <c>NSWindowLevel</c> that EasyChat maps onto.</summary>
internal static class WindowLevel
{
    internal const nint Normal = 0;
    internal const nint Floating = 3;
    internal const nint StatusBar = 25;
}

/// <summary>Values of <c>NSWindowCollectionBehavior</c>.</summary>
[Flags]
internal enum WindowCollectionBehavior : long
{
    Default = 0,
    CanJoinAllSpaces = 1 << 0,
    Transient = 1 << 3,
    Stationary = 1 << 4,
    IgnoresCycle = 1 << 6,
    FullScreenAuxiliary = 1 << 8
}

/// <summary>
/// AppKit window operations on a raw <c>NSView</c> handle. The handle never leaves this assembly and
/// no AppKit type is projected into managed code; every call is a single Objective-C message.
/// </summary>
internal static class AppKitNative
{
    private const string LibraryPath = "/System/Library/Frameworks/AppKit.framework/AppKit";

    /// <summary>Value of <c>NSWindowSharingNone</c>.</summary>
    private const nint SharingNone = 0;

    /// <summary>Value of <c>NSWindowSharingReadOnly</c>, the AppKit default.</summary>
    private const nint SharingReadOnly = 1;

    private static readonly Lazy<bool> Loaded = new(
        () => NativeLibrary.Load(LibraryPath) != IntPtr.Zero,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Resolves the <c>NSWindow</c> that hosts a toolkit platform handle. The host passes the
    /// backing <c>NSView</c>, and a view that is not in a window yet answers nil.
    /// </summary>
    internal static IntPtr GetWindow(nint view)
    {
        if (view == 0)
            return IntPtr.Zero;

        EnsureLoaded();
        return ObjectiveCNative.SendReturningHandle(view, ObjectiveCNative.GetSelector("window"));
    }

    internal static void SetLevel(IntPtr window, nint level) =>
        ObjectiveCNative.Send(window, ObjectiveCNative.GetSelector("setLevel:"), level);

    internal static void SetCollectionBehavior(IntPtr window, WindowCollectionBehavior behavior) =>
        ObjectiveCNative.Send(
            window,
            ObjectiveCNative.GetSelector("setCollectionBehavior:"),
            (nint)behavior);

    internal static void SetHidesOnDeactivate(IntPtr window, bool hides) =>
        ObjectiveCNative.Send(window, ObjectiveCNative.GetSelector("setHidesOnDeactivate:"), hides);

    internal static void SetIgnoresMouseEvents(IntPtr window, bool ignores) =>
        ObjectiveCNative.Send(
            window,
            ObjectiveCNative.GetSelector("setIgnoresMouseEvents:"),
            ignores);

    /// <summary>
    /// Raises the window above its level without making it key and without activating EasyChat, so
    /// the foreground application keeps its text-input context.
    /// </summary>
    internal static void OrderFrontRegardless(IntPtr window) =>
        ObjectiveCNative.Send(window, ObjectiveCNative.GetSelector("orderFrontRegardless"));

    /// <summary>
    /// Hides the window from screen capture through <c>NSWindow.sharingType</c>. Answers
    /// <see langword="false"/> when AppKit no longer exposes the property, so callers fall back to a
    /// visual workaround instead of assuming the window is excluded.
    /// </summary>
    internal static bool TrySetExcludedFromCapture(IntPtr window, bool excluded)
    {
        if (!ObjectiveCNative.Responds(window, "setSharingType:"))
            return false;

        ObjectiveCNative.Send(
            window,
            ObjectiveCNative.GetSelector("setSharingType:"),
            excluded ? SharingNone : SharingReadOnly);
        return true;
    }

    /// <summary>Loads AppKit so the Objective-C runtime knows its classes.</summary>
    internal static void EnsureLoaded()
    {
        if (!Loaded.Value)
            throw new PlatformNotSupportedException("AppKit could not be loaded.");
    }
}
