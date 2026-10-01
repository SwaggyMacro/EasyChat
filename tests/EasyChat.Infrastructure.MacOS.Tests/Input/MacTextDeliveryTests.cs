using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

/// <summary>
/// Covers the paths that reach no other application. Nothing here posts a synthetic keystroke or
/// selects text anywhere: a test that did would type into whatever the developer had in front of
/// them.
/// </summary>
[TestClass]
public sealed class MacTextDeliveryTests
{
    private static MacTextDelivery CreateDelivery() =>
        new(
            new UnusedClipboardSnapshots(),
            new UnusedClipboardText(),
            NullLogger<MacTextDelivery>.Instance);

    /// <summary>
    /// Skips when the test host holds Accessibility approval, because the accessibility paths would
    /// then act on the developer's frontmost application instead of failing harmlessly.
    /// </summary>
    private static bool SkipWhenAccessibilityWouldReachAnotherApp()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Text delivery can only be verified on macOS 26 or later.");
            return true;
        }

        if (AccessibilityNative.IsProcessTrusted())
        {
            Assert.Inconclusive(
                "The test host is trusted for Accessibility, so this path would reach the frontmost application.");
            return true;
        }

        return false;
    }

    [TestMethod]
    public async Task AnUnknownKeyCombinationIsRejectedBeforeAnythingIsPosted()
    {
        var sent = await CreateDelivery().SendKeyCombinationAsync("Ctrl + NotAKey");

        Assert.IsTrue(sent.IsFailure);
        Assert.AreEqual("text-delivery.key-combination-invalid", sent.Error.Code);
    }

    [TestMethod]
    public async Task AnUnsupportedModeIsRejected()
    {
        var delivered = await CreateDelivery().DeliverAsync(new TextDeliveryRequest(
            "text",
            ExternalTargetToken.None,
            (TextDeliveryMode)99,
            TimeSpan.Zero));

        Assert.IsTrue(delivered.IsFailure);
        Assert.AreEqual("text-delivery.mode-unsupported", delivered.Error.Code);
    }

    [TestMethod]
    public async Task MessageModeReportsAMissingFocusedElementInsteadOfClaimingSuccess()
    {
        if (SkipWhenAccessibilityWouldReachAnotherApp())
            return;

        var delivered = await CreateDelivery().DeliverAsync(new TextDeliveryRequest(
            "translated",
            ExternalTargetToken.None,
            TextDeliveryMode.Message,
            TimeSpan.Zero));

        Assert.IsTrue(delivered.IsFailure);
        Assert.AreEqual("text-delivery.no-focused-element", delivered.Error.Code);
    }

    [TestMethod]
    public async Task NoFocusedTextElementIsReportedAsNoFocusedControlRatherThanAnError()
    {
        if (SkipWhenAccessibilityWouldReachAnotherApp())
            return;

        var selection = await new MacTextSelection().SelectAllAsync();

        Assert.IsTrue(selection.IsSuccess, selection.Error.Message);
        Assert.IsFalse(selection.Value.HasFocusedControl);
    }

    private sealed class UnusedClipboardSnapshots : IClipboardSnapshots
    {
        public ValueTask<Result<IClipboardChangeToken>> GetChangeTokenAsync(
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public ValueTask<Result<bool>> IsChangeTokenCurrentAsync(
            IClipboardChangeToken changeToken,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public ValueTask<Result<IClipboardSnapshot>> CaptureAsync(
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public ValueTask<Result> RestoreAsync(
            IClipboardSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public ValueTask<Result> RestoreIfUnchangedAsync(
            IClipboardSnapshot snapshot,
            IClipboardChangeToken expectedChangeToken,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class UnusedClipboardText : IClipboardText
    {
        public ValueTask<Result<string?>> ReadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public ValueTask<Result> WriteAsync(
            string text,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
