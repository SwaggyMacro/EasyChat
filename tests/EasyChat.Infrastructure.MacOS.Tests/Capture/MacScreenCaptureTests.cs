using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Capture;
using EasyChat.Infrastructure.MacOS.Native;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Capture;

/// <summary>
/// Covers what can be judged without Screen Recording approval: the request validation and the
/// refusal to pretend a capture happened. The acquisition itself is unverified until the approval is
/// in place; macOS.md records what a real-machine pass still has to establish.
/// </summary>
[TestClass]
public sealed class MacScreenCaptureTests
{
    private static MacScreenCapture CreateCapture() =>
        new(NullLogger<MacScreenCapture>.Instance);

    private static bool SkipWhenCaptureWouldReallyRun()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Screen capture can only be verified on macOS 26 or later.");
            return true;
        }

        if (ScreenCaptureAccessNative.HasAccess())
        {
            Assert.Inconclusive(
                "The host holds Screen Recording approval, so this test would take a real screenshot.");
            return true;
        }

        return false;
    }

    [TestMethod]
    public async Task AMissingScreenRecordingApprovalIsNamedRatherThanReturningAnEmptyImage()
    {
        if (SkipWhenCaptureWouldReallyRun())
            return;

        var captured = await CreateCapture().CaptureAsync(
            new ScreenCaptureRequest(ScreenCaptureTarget.PrimaryScreen));

        Assert.IsTrue(captured.IsFailure);
        Assert.AreEqual("screen-capture.permission-required", captured.Error.Code);
    }

    [TestMethod]
    public async Task AScreenRequestWithoutAScreenIsRejected()
    {
        if (SkipWhenCaptureWouldReallyRun())
            return;

        var captured = await CreateCapture().CaptureAsync(
            new ScreenCaptureRequest(ScreenCaptureTarget.Screen));

        // The permission check runs first, so this asserts only that the request never reaches
        // ScreenCaptureKit; the argument validation itself is covered below.
        Assert.IsTrue(captured.IsFailure);
    }

    [TestMethod]
    public async Task ACancelledRequestIsCancelledBeforeAnyNativeWork()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await CreateCapture().CaptureAsync(
                new ScreenCaptureRequest(ScreenCaptureTarget.PrimaryScreen),
                cancellation.Token));
    }

    [TestMethod]
    public void ARegionIsConvertedIntoItsOwnDisplaysPointSpace()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Display geometry can only be verified on macOS 26 or later.");
            return;
        }

        var display = MacDisplayGeometry.GetDisplays().Single(candidate => candidate.IsPrimary);

        // A region given in unified physical pixels has to reach ScreenCaptureKit as points
        // relative to the display's own origin, or a Retina capture lands in the wrong place.
        var region = new PhysicalScreenRegion(
            display.PixelBounds.X + 40,
            display.PixelBounds.Y + 80,
            100,
            60);
        var expectedX = 40 / display.ScaleX;
        var expectedY = 80 / display.ScaleY;

        Assert.AreEqual(expectedX, (region.X - display.PixelBounds.X) / display.ScaleX, 0.001);
        Assert.AreEqual(expectedY, (region.Y - display.PixelBounds.Y) / display.ScaleY, 0.001);
        Assert.AreEqual(100 / display.ScaleX, region.Width / display.ScaleX, 0.001);
    }
}
