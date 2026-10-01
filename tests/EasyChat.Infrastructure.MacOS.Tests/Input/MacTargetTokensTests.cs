using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacTargetTokensTests
{
    [TestMethod]
    public void ATokenRoundTripsBackToTheTargetItNames()
    {
        var token = MacTargetTokens.FromTarget(new MacTarget(4321, 77));

        Assert.IsTrue(MacTargetTokens.TryDecode(token, out var decoded));
        Assert.AreEqual(4321, decoded.ProcessIdentifier);
        Assert.AreEqual(77, decoded.WindowNumber);
        Assert.IsFalse(decoded.IsApplicationWide);
    }

    [TestMethod]
    public void ATargetWithoutAWindowIsApplicationWide()
    {
        Assert.IsTrue(MacTargetTokens.TryDecode(
            MacTargetTokens.FromTarget(new MacTarget(11, 0)),
            out var decoded));

        Assert.IsTrue(decoded.IsApplicationWide);
    }

    [TestMethod]
    public void ATargetWithoutAProcessHasNoToken() =>
        Assert.IsTrue(MacTargetTokens.FromTarget(new MacTarget(0, 0)).IsEmpty);

    [TestMethod]
    public void AnEmptyTokenDoesNotDecode() =>
        Assert.IsFalse(MacTargetTokens.TryDecode(ExternalTargetToken.None, out _));

    [TestMethod]
    public void ATokenFromAnotherPlatformDoesNotDecode() =>
        Assert.IsFalse(MacTargetTokens.TryDecode(new ExternalTargetToken("win32:1A2B"), out _));

    [TestMethod]
    public void ATokenFromAnotherSessionDoesNotDecode()
    {
        // A token that survived a restart, or was persisted against the rules, must not resolve to
        // whichever process happens to hold that id now.
        var token = MacTargetTokens.FromTarget(new MacTarget(4321, 0));
        var segments = token.Value.Split(':');
        var foreign = new ExternalTargetToken(
            $"{segments[0]}:000000000000:{segments[2]}:{segments[3]}");

        Assert.IsFalse(MacTargetTokens.TryDecode(foreign, out _));
    }

    [TestMethod]
    public void AMalformedTokenDoesNotDecode()
    {
        var session = MacTargetTokens.FromTarget(new MacTarget(1, 0)).Value.Split(':')[1];

        Assert.IsFalse(MacTargetTokens.TryDecode(new ExternalTargetToken("mac:"), out _));
        Assert.IsFalse(MacTargetTokens.TryDecode(new ExternalTargetToken($"mac:{session}:1"), out _));
        Assert.IsFalse(MacTargetTokens.TryDecode(
            new ExternalTargetToken($"mac:{session}:not-a-pid:0"),
            out _));
        Assert.IsFalse(MacTargetTokens.TryDecode(
            new ExternalTargetToken($"mac:{session}:-3:0"),
            out _));
    }
}
