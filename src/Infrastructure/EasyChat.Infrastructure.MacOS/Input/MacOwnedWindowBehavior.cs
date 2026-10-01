using EasyChat.Infrastructure.MacOS.Native;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// AppKit behaviour for EasyChat's floating windows — subtitles, the selection toolbar and result
/// popups. It takes the raw toolkit platform handle, which is an <c>NSView</c> on macOS, and never
/// lets an AppKit type escape this assembly.
/// </summary>
/// <remarks>
/// AppKit has no way to stop a plain <c>NSWindow</c> from becoming key; only an <c>NSPanel</c>
/// created with the non-activating style can do that, and the toolkit does not create one. The
/// guarantee therefore comes from how the window is raised: <c>orderFrontRegardless</c> shows and
/// raises it without activating EasyChat, so the foreground application keeps key status and its
/// text-input context. Callers must not pair this with an activating show.
/// </remarks>
public sealed class MacOwnedWindowBehavior(ILogger<MacOwnedWindowBehavior> logger)
{
    /// <summary>
    /// Floating windows stay above ordinary windows, follow the user across Spaces, survive a
    /// full-screen application and stay out of the window-cycling order.
    /// </summary>
    private const WindowCollectionBehavior FloatingBehavior =
        WindowCollectionBehavior.CanJoinAllSpaces
        | WindowCollectionBehavior.Stationary
        | WindowCollectionBehavior.IgnoresCycle
        | WindowCollectionBehavior.FullScreenAuxiliary;

    public void ConfigureNoActivate(nint view)
    {
        var window = ResolveWindow(view);
        AppKitNative.SetLevel(window, WindowLevel.Floating);
        AppKitNative.SetCollectionBehavior(window, FloatingBehavior);

        // AppKit hides ordinary windows when the owning application is deactivated, which would
        // make subtitles disappear the moment the user returns to the application being watched.
        AppKitNative.SetHidesOnDeactivate(window, false);
        logger.LogDebug("Configured an AppKit floating window that does not take key status.");
    }

    public void BringToFrontWithoutActivating(nint view) =>
        AppKitNative.OrderFrontRegardless(ResolveWindow(view));

    public void SetClickThrough(nint view, bool enabled) =>
        AppKitNative.SetIgnoresMouseEvents(ResolveWindow(view), enabled);

    public bool TrySetExcludedFromCapture(nint view, bool enabled)
    {
        var window = AppKitNative.GetWindow(view);
        if (window == IntPtr.Zero)
            return false;

        var excluded = AppKitNative.TrySetExcludedFromCapture(window, enabled);
        if (!excluded)
            logger.LogDebug("AppKit does not expose window capture exclusion on this system.");
        return excluded;
    }

    private static IntPtr ResolveWindow(nint view)
    {
        if (view == 0)
            throw new ArgumentException("A native view handle is required.", nameof(view));

        var window = AppKitNative.GetWindow(view);
        return window != IntPtr.Zero
            ? window
            : throw new InvalidOperationException(
                "The view is not attached to an AppKit window yet.");
    }
}
