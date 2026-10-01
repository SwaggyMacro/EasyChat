using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacWindowFocusTests
{
    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("Window focus can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public async Task TheForegroundTargetNamesARealRunningApplication()
    {
        if (Skip())
            return;

        var target = await new MacWindowFocus().GetForegroundTargetAsync();

        Assert.IsTrue(target.IsSuccess, target.Error.Message);
        Assert.IsTrue(MacTargetTokens.TryDecode(target.Value, out var decoded));

        var identity = await new MacRunningProcessCatalog()
            .ResolveProcessIdentifierAsync(target.Value);
        Assert.IsTrue(identity.IsSuccess, identity.Error.Message);
        Assert.IsFalse(string.IsNullOrWhiteSpace(identity.Value));
        Assert.IsGreaterThan(0, decoded.ProcessIdentifier);
    }

    [TestMethod]
    public async Task AForeignTokenIsRejectedBeforeAnythingIsActivated()
    {
        var focused = await new MacWindowFocus()
            .EnsureFocusedAsync(new ExternalTargetToken("win32:1A2B"));

        Assert.IsTrue(focused.IsFailure);
        Assert.AreEqual("window.target-invalid", focused.Error.Code);
    }

    [TestMethod]
    public async Task AnExitedTargetFailsInsteadOfActivatingSomethingElse()
    {
        if (Skip())
            return;

        // A process id that no running application owns stands in for a target that has quit.
        var gone = MacTargetTokens.FromTarget(new MacTarget(int.MaxValue - 1, 0));

        var focused = await new MacWindowFocus().EnsureFocusedAsync(gone);

        Assert.IsTrue(focused.IsFailure);
        Assert.AreEqual("window.target-exited", focused.Error.Code);
    }

    [TestMethod]
    public async Task ConfiguringAnotherApplicationsWindowIsRefused()
    {
        var configured = await new MacWindowFocus()
            .ConfigureNoActivateAsync(MacTargetTokens.FromTarget(new MacTarget(1, 0)));

        Assert.IsTrue(configured.IsFailure);
        Assert.AreEqual("window.no-activate-unsupported", configured.Error.Code);
    }

    [TestMethod]
    public async Task TheFocusedTargetEitherResolvesOrNamesTheMissingApproval()
    {
        if (Skip())
            return;

        var focused = await new MacWindowFocus().GetFocusedTargetAsync();

        if (focused.IsSuccess)
        {
            Assert.IsTrue(MacTargetTokens.TryDecode(focused.Value, out _));
            return;
        }

        // Without Accessibility approval this must say so rather than fall back to the frontmost
        // application, which is exactly the case where the two answers differ.
        Assert.AreEqual("window.focus-permission-required", focused.Error.Code);
    }
}
