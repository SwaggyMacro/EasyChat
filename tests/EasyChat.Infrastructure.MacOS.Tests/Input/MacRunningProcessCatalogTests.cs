using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacRunningProcessCatalogTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("Application discovery can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public async Task InteractiveApplicationsAreListedWithAStableIdentityAndAnIcon()
    {
        if (Skip())
            return;

        var applications = await new MacRunningProcessCatalog().GetRunningProcessesAsync();

        Assert.IsNotEmpty(applications);
        foreach (var application in applications)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(application.Identifier),
                "Every listed application needs a persistable identity.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(application.Name));
            if (!application.IconPng.IsEmpty)
            {
                Assert.IsTrue(
                    application.IconPng.Span[..PngSignature.Length].SequenceEqual(PngSignature),
                    $"{application.Identifier} produced an icon that is not a PNG.");
            }
        }
    }

    [TestMethod]
    public async Task TheFinderIsListedByItsBundleIdentifier()
    {
        if (Skip())
            return;

        var applications = await new MacRunningProcessCatalog().GetRunningProcessesAsync();

        // The Finder is the one application guaranteed to be running with a regular activation
        // policy on any macOS session, which makes it a stable anchor for this assertion.
        Assert.IsTrue(
            applications.Any(application => application.Identifier == "com.apple.finder"),
            "The Finder should always appear in the catalog.");
    }

    [TestMethod]
    public async Task TheCatalogNeverListsEasyChatItself()
    {
        if (Skip())
            return;

        var applications = await new MacRunningProcessCatalog().GetRunningProcessesAsync();
        var identities = applications.Select(application => application.Identifier).ToArray();

        Assert.AreEqual(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());
        var self = await new MacWindowFocus().GetForegroundTargetAsync();
        if (self.IsFailure)
            return;

        Assert.IsTrue(MacTargetTokens.TryDecode(self.Value, out _));
    }

    [TestMethod]
    public async Task AnEmptyTargetIsRejected()
    {
        var identity = await new MacRunningProcessCatalog()
            .ResolveProcessIdentifierAsync(ExternalTargetToken.None);

        Assert.IsTrue(identity.IsFailure);
        Assert.AreEqual("running-process.empty-target", identity.Error.Code);
    }

    [TestMethod]
    public async Task ATokenFromAnotherPlatformIsRejected()
    {
        var identity = await new MacRunningProcessCatalog()
            .ResolveProcessIdentifierAsync(new ExternalTargetToken("win32:1A2B"));

        Assert.IsTrue(identity.IsFailure);
        Assert.AreEqual("running-process.invalid-target", identity.Error.Code);
    }

    [TestMethod]
    public async Task AnExitedTargetIsReportedAsUnavailable()
    {
        if (Skip())
            return;

        var gone = MacTargetTokens.FromTarget(new MacTarget(int.MaxValue - 1, 0));

        var identity = await new MacRunningProcessCatalog().ResolveProcessIdentifierAsync(gone);

        Assert.IsTrue(identity.IsFailure);
        Assert.AreEqual("running-process.unavailable", identity.Error.Code);
    }
}
