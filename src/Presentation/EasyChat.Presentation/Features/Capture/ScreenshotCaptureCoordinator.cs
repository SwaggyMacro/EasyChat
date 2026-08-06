using EasyChat.Contracts.Capture;
using EasyChat.Contracts.Platform;

namespace EasyChat.Presentation.Features.Capture;

public interface IScreenshotCaptureSession
{
    ValueTask WarmUpAsync(CancellationToken cancellationToken = default);

    Task<ScreenshotSelection?> CaptureAsync(
        bool precise,
        CaptureOverlayAction defaultAction,
        CaptureToolbarMode toolbarMode,
        CancellationToken cancellationToken = default);
}

public sealed class ScreenshotCaptureCoordinator(
    IPlatformAccessUseCases platformAccess,
    IScreenshotCaptureSession session)
{
    private readonly IPlatformAccessUseCases _platformAccess = platformAccess;
    private readonly IScreenshotCaptureSession _session = session;

    public async Task<ScreenshotSelection?> CaptureAsync(
        string? mode,
        CaptureOverlayAction defaultAction = CaptureOverlayAction.Translation,
        CaptureToolbarMode toolbarMode = CaptureToolbarMode.Full,
        CancellationToken cancellationToken = default)
    {
        var access = await _platformAccess.EnsureAvailableAsync(
            PlatformCapability.ScreenCapture,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
            throw new InvalidOperationException(access.Error.Message);

        return await _session.CaptureAsync(
            precise: !string.Equals(mode, "Quick", StringComparison.OrdinalIgnoreCase),
            defaultAction,
            toolbarMode,
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class InProcessScreenshotCaptureSession(CaptureOverlayCoordinator overlays)
    : IScreenshotCaptureSession
{
    private readonly CaptureOverlayCoordinator _overlays = overlays;

    public ValueTask WarmUpAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public async Task<ScreenshotSelection?> CaptureAsync(
        bool precise,
        CaptureOverlayAction defaultAction,
        CaptureToolbarMode toolbarMode,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _overlays.SelectAsync(
            precise,
            regionOnly: false,
            defaultAction,
            toolbarMode,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (outcome is null)
            return null;
        if (outcome.Image is null)
            throw new InvalidOperationException("Screenshot selection did not produce an image.");

        using (outcome.Image)
        {
            return new ScreenshotSelection(
                ImageTranslation.AvaloniaImageFrames.ToImageFrame(outcome.Image),
                outcome.Action,
                outcome.CompletionPoint);
        }
    }
}
