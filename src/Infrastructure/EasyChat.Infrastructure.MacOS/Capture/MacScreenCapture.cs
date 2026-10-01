using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Capture;

/// <summary>
/// Captures the screen through ScreenCaptureKit.
/// </summary>
/// <remarks>
/// <para>
/// The deprecated <c>CGDisplayCreateImage</c> path is not used even as a fallback: it still works on
/// macOS 26 but Apple has replaced it, and keeping a second acquisition path alive would mean two
/// sets of pixel behaviour to reason about for the one feature where pixel fidelity decides OCR
/// quality.
/// </para>
/// <para>
/// Captures are serialised. ScreenCaptureKit answers through completion blocks fulfilled by
/// process-wide handlers, and more importantly EasyChat only ever captures for one visible
/// operation at a time, so a queue is honest about the real usage rather than a limitation.
/// </para>
/// </remarks>
public sealed class MacScreenCapture(ILogger<MacScreenCapture> logger) : IScreenCapture
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<Result<ImageFrame>> CaptureAsync(
        ScreenCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            return Result<ImageFrame>.Failure(new Error(
                "screen-capture.unsupported",
                "Screen capture requires macOS 26 or later."));
        }

        // Preflight rather than discovering it from an opaque ScreenCaptureKit error, so the user is
        // told which approval is missing instead of being shown a capture that silently did nothing.
        if (!ScreenCaptureAccessNative.HasAccess())
        {
            return Result<ImageFrame>.Failure(new Error(
                "screen-capture.permission-required",
                "Screen Recording access has not been granted to EasyChat."));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CaptureCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Screen capture failed.");
            return Result<ImageFrame>.Failure(
                new Error("screen-capture.failed", exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<Result<ImageFrame>> CaptureCoreAsync(
        ScreenCaptureRequest request,
        CancellationToken cancellationToken)
    {
        var displays = MacDisplayGeometry.GetDisplays();
        if (displays.Count == 0)
        {
            return Result<ImageFrame>.Failure(new Error(
                "screen-capture.no-display",
                "macOS reported no active display."));
        }

        var resolved = Resolve(request, displays);
        if (resolved.IsFailure)
            return Result<ImageFrame>.Failure(resolved.Error);
        var (display, region) = resolved.Value;

        ScreenCaptureKitNative.EnsureAvailable();
        var content = await ScreenCaptureKitNative.GetShareableContentAsync()
            .WaitAsync(CaptureTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (content.Content == IntPtr.Zero)
        {
            return Result<ImageFrame>.Failure(new Error(
                "screen-capture.content-unavailable",
                content.Error ?? "macOS did not report any capturable content."));
        }

        IntPtr filter = IntPtr.Zero;
        IntPtr configuration = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        try
        {
            using var pool = AutoreleasePool.Push();
            var target = FindDisplay(content.Content, display.DisplayId);
            if (target == IntPtr.Zero)
            {
                return Result<ImageFrame>.Failure(new Error(
                    "screen-capture.display-unavailable",
                    "The target display is no longer capturable; it may have been disconnected."));
            }

            filter = ScreenCaptureKitNative.CreateFilter(
                target,
                CollectOwnWindows(content.Content));
            configuration = ScreenCaptureKitNative.CreateConfiguration(
                region.PixelWidth,
                region.PixelHeight,
                region.SourceRect);
            if (filter == IntPtr.Zero || configuration == IntPtr.Zero)
            {
                return Result<ImageFrame>.Failure(new Error(
                    "screen-capture.failed",
                    "The capture filter or configuration could not be built."));
            }

            var captured = await ScreenCaptureKitNative
                .CaptureImageAsync(filter, configuration)
                .WaitAsync(CaptureTimeout, cancellationToken)
                .ConfigureAwait(false);
            image = captured.Image;
            if (image == IntPtr.Zero)
            {
                return Result<ImageFrame>.Failure(new Error(
                    "screen-capture.failed",
                    captured.Error ?? "ScreenCaptureKit returned no image."));
            }

            var pixels = ImageEncodingNative.ReadBgra32(
                image,
                out var width,
                out var height,
                out var stride);
            return Result<ImageFrame>.Success(new ImageFrame(
                width,
                height,
                stride,
                ScreenDescriptor.LogicalDpi * display.ScaleX,
                ScreenDescriptor.LogicalDpi * display.ScaleY,
                pixels));
        }
        finally
        {
            if (image != IntPtr.Zero)
                ImageEncodingNative.CGImageRelease(image);
            if (configuration != IntPtr.Zero)
                ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("release"));
            if (filter != IntPtr.Zero)
                ObjectiveCNative.Send(filter, ObjectiveCNative.GetSelector("release"));
            CoreFoundationNative.CFRelease(content.Content);
        }
    }

    /// <summary>
    /// Works out which display to capture and what part of it, converting the request's unified
    /// physical pixels into the display-local point rectangle ScreenCaptureKit expects.
    /// </summary>
    private static Result<(MacDisplay Display, CaptureRegion Region)> Resolve(
        ScreenCaptureRequest request,
        IReadOnlyList<MacDisplay> displays)
    {
        switch (request.Target)
        {
            case ScreenCaptureTarget.PrimaryScreen:
                var primary = displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0];
                return (primary, WholeDisplay(primary));

            case ScreenCaptureTarget.Screen:
                if (request.Screen is not { } id)
                {
                    return Result<(MacDisplay, CaptureRegion)>.Failure(new Error(
                        "screen-capture.screen-missing",
                        "A screen capture request must name a screen."));
                }

                var named = displays.FirstOrDefault(display => display.Id == id);
                return named is null
                    ? Result<(MacDisplay, CaptureRegion)>.Failure(new Error(
                        "screen-capture.display-unavailable",
                        $"No active display matches '{id.Value}'."))
                    : (named, WholeDisplay(named));

            case ScreenCaptureTarget.Region:
                if (request.Region is not { } region || region.IsEmpty)
                {
                    return Result<(MacDisplay, CaptureRegion)>.Failure(new Error(
                        "screen-capture.region-invalid",
                        "A region capture request must name a non-empty region."));
                }

                var containing = displays.FirstOrDefault(display =>
                    region.X >= display.PixelBounds.X
                    && region.Y >= display.PixelBounds.Y
                    && region.X + region.Width <= display.PixelBounds.X + display.PixelBounds.Width
                    && region.Y + region.Height <= display.PixelBounds.Y + display.PixelBounds.Height);
                if (containing is null)
                {
                    // A region straddling two displays has no single point space to express it in,
                    // so this fails rather than silently capturing part of it.
                    return Result<(MacDisplay, CaptureRegion)>.Failure(new Error(
                        "screen-capture.region-invalid",
                        "The region is not contained by a single display."));
                }

                return (containing, new CaptureRegion(
                    region.Width,
                    region.Height,
                    new CoreGraphicsRect
                    {
                        X = (region.X - containing.PixelBounds.X) / containing.ScaleX,
                        Y = (region.Y - containing.PixelBounds.Y) / containing.ScaleY,
                        Width = region.Width / containing.ScaleX,
                        Height = region.Height / containing.ScaleY
                    }));

            default:
                return Result<(MacDisplay, CaptureRegion)>.Failure(new Error(
                    "screen-capture.target-unsupported",
                    $"Unsupported capture target: {request.Target}."));
        }
    }

    private static CaptureRegion WholeDisplay(MacDisplay display) => new(
        display.PixelBounds.Width,
        display.PixelBounds.Height,
        new CoreGraphicsRect
        {
            X = 0,
            Y = 0,
            Width = display.PointBounds.Width,
            Height = display.PointBounds.Height
        });

    private static IntPtr FindDisplay(IntPtr content, uint displayId)
    {
        var displays = ScreenCaptureKitNative.Displays(content);
        for (nint index = 0; index < FoundationNative.CountOf(displays); index++)
        {
            var display = FoundationNative.ItemAt(displays, index);
            if (ScreenCaptureKitNative.DisplayId(display) == displayId)
                return display;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// EasyChat's own windows, so the capture excludes the selection overlay and any floating
    /// subtitle that happens to be over the area the user is capturing.
    /// </summary>
    private static IntPtr CollectOwnWindows(IntPtr content)
    {
        var excluded = FoundationNative.CreateMutableArray();
        var windows = ScreenCaptureKitNative.Windows(content);
        var self = Environment.ProcessId;
        for (nint index = 0; index < FoundationNative.CountOf(windows); index++)
        {
            var window = FoundationNative.ItemAt(windows, index);
            if (ScreenCaptureKitNative.OwningProcessIdentifier(window) == self)
                FoundationNative.Add(excluded, window);
        }

        return excluded;
    }

    private readonly record struct CaptureRegion(
        int PixelWidth,
        int PixelHeight,
        CoreGraphicsRect SourceRect);
}
