using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Infrastructure.MacOS.Native;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacOwnedWindowBehaviorTests
{
    private static MacOwnedWindowBehavior CreateBehavior() =>
        new(NullLogger<MacOwnedWindowBehavior>.Instance);

    [TestMethod]
    public void AMissingViewHandleIsRejectedRatherThanSentToAppKit()
    {
        var behavior = CreateBehavior();

        Assert.ThrowsExactly<ArgumentException>(() => behavior.ConfigureNoActivate(0));
        Assert.ThrowsExactly<ArgumentException>(() => behavior.BringToFrontWithoutActivating(0));
        Assert.ThrowsExactly<ArgumentException>(() => behavior.SetClickThrough(0, true));
    }

    [TestMethod]
    public void CaptureExclusionReportsFailureInsteadOfThrowingOnAMissingWindow() =>
        Assert.IsFalse(CreateBehavior().TrySetExcludedFromCapture(0, true));

    [TestMethod]
    public void AppKitWindowSelectorsResolveOnAMacOsHost()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("AppKit can only be verified on macOS 26 or later.");
            return;
        }


        // AppKit has to be resident before the Objective-C runtime knows its classes, and a
        // missing view handle short-circuits before that load, so the load is forced here.
        AppKitNative.EnsureLoaded();

        var nsWindow = ObjectiveCNative.GetClass("NSWindow");
        Assert.AreNotEqual(IntPtr.Zero, nsWindow);
        Assert.AreEqual(IntPtr.Zero, AppKitNative.GetWindow(0));
        foreach (var selector in new[]
                 {
                     "setLevel:",
                     "setCollectionBehavior:",
                     "setHidesOnDeactivate:",
                     "setIgnoresMouseEvents:",
                     "orderFrontRegardless",
                     "setSharingType:"
                 })
        {
            Assert.IsTrue(
                ObjectiveCNative.Responds(nsWindow, selector) || InstancesRespond(nsWindow, selector),
                selector);
        }
    }

    private static bool InstancesRespond(IntPtr type, string selector) =>
        ObjectiveCNative.SendReturningBool(
            type,
            ObjectiveCNative.GetSelector("instancesRespondToSelector:"),
            ObjectiveCNative.GetSelector(selector));
}
