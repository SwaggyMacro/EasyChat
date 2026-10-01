using Avalonia.Controls;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Presentation.Foundation.Platform;

namespace EasyChat.Desktop.MacOS;

/// <summary>
/// Bridges Avalonia windows onto AppKit. Avalonia's macOS platform handle is the backing
/// <c>NSView</c>; resolving its <c>NSWindow</c> and every native call stay inside the macOS
/// infrastructure assembly.
/// </summary>
internal sealed class AvaloniaMacWindowBehavior(MacOwnedWindowBehavior appKit) : IPlatformWindowBehavior
{
    public ValueTask ConfigureNoActivateAsync(
        Window window,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        appKit.ConfigureNoActivate(GetHandle(window));
        return ValueTask.CompletedTask;
    }

    public ValueTask BringToFrontWithoutActivatingAsync(
        Window window,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        appKit.BringToFrontWithoutActivating(GetHandle(window));
        return ValueTask.CompletedTask;
    }

    // macOS needs no counterpart to the Windows IME restore: an EasyChat floating window is raised
    // with orderFrontRegardless and never becomes key, so the foreground application keeps its
    // text-input context throughout.

    public ValueTask SetClickThroughAsync(
        Window window,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        appKit.SetClickThrough(GetHandle(window), enabled);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySetExcludedFromCaptureAsync(
        Window window,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(appKit.TrySetExcludedFromCapture(GetHandle(window), enabled));
    }

    private static nint GetHandle(Window window) =>
        window.TryGetPlatformHandle()?.Handle
        ?? throw new InvalidOperationException("The Avalonia window does not have a native handle yet.");
}
