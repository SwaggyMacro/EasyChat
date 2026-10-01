using System.Runtime.InteropServices;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Native;

/// <summary>
/// Exercises the native privacy bridge against the real system. Every call here is a silent check,
/// so no test in this class can raise a system prompt. On a non-macOS host the tests report
/// Inconclusive rather than pretending the bridge was verified.
/// </summary>
[TestClass]
public sealed class MacSystemPrivacyGatewayTests
{
    [TestMethod]
    public void EveryPermissionResolvesAgainstARealPrivacyEntryPoint()
    {
        if (!RunningOnSupportedHost(out var reason))
        {
            Assert.Inconclusive(reason);
            return;
        }

        var gateway = new MacSystemPrivacyGateway();

        foreach (var permission in Enum.GetValues<PlatformPermission>())
        {
            var state = gateway.Check(permission);
            Assert.AreNotEqual(
                MacPrivacyState.Unavailable,
                state,
                $"{permission} has no macOS privacy entry point.");
        }
    }

    [TestMethod]
    public void TheGatewayRefusesToAnswerOnAnUnsupportedHost()
    {
        if (RunningOnSupportedHost(out _))
        {
            Assert.Inconclusive("The host runs a supported macOS version.");
            return;
        }

        Assert.ThrowsExactly<PlatformNotSupportedException>(() =>
            new MacSystemPrivacyGateway().Check(PlatformPermission.Accessibility));
    }

    [TestMethod]
    public void TheMicrophoneCheckBindsToARealObjectiveCClassAndSelector()
    {
        if (!RunningOnSupportedHost(out var reason))
        {
            Assert.Inconclusive(reason);
            return;
        }

        // objc_msgSend on a nil receiver answers zero, which is also NotDetermined, so the binding
        // has to be asserted separately from the state it reports.
        Assert.AreNotEqual(IntPtr.Zero, ObjectiveCNative.GetClass("AVCaptureDevice"));
        Assert.AreNotEqual(
            IntPtr.Zero,
            ObjectiveCNative.GetSelector("authorizationStatusForMediaType:"));
        Assert.AreNotEqual(
            IntPtr.Zero,
            ObjectiveCNative.GetSelector("requestAccessForMediaType:completionHandler:"));
    }

    [TestMethod]
    public void TheMicrophoneCompletionBlockIsAWellFormedGlobalBlock()
    {
        if (!RunningOnSupportedHost(out var reason))
        {
            Assert.Inconclusive(reason);
            return;
        }

        var block = ObjectiveCBlockFactory.CreateGlobalBlock(nint.MaxValue);

        Assert.AreNotEqual(IntPtr.Zero, block);
        Assert.AreNotEqual(IntPtr.Zero, Marshal.ReadIntPtr(block), "isa");
        Assert.AreEqual(ObjectiveCBlock.GlobalFlag, Marshal.ReadInt32(block, IntPtr.Size), "flags");
        Assert.AreNotEqual(IntPtr.Zero, Marshal.ReadIntPtr(block, (IntPtr.Size * 2) + IntPtr.Size), "descriptor");
    }

    private static bool RunningOnSupportedHost(out string reason)
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            reason = string.Empty;
            return true;
        }

        reason = "The macOS privacy bridge can only be verified on macOS 26 or later.";
        return false;
    }
}
