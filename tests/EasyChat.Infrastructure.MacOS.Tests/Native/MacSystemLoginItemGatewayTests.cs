using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Native;

/// <summary>
/// Exercises the <c>SMAppService</c> bridge against the real system. Only the read path is called,
/// so no test here registers or removes a login item on the developer's machine.
/// </summary>
[TestClass]
public sealed class MacSystemLoginItemGatewayTests
{
    [TestMethod]
    public void ReadingTheLoginItemStateReachesARealSmAppServiceInstance()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("SMAppService can only be verified on macOS 26 or later.");
            return;
        }

        // The test host is not an .app bundle, so macOS is expected to answer NotFound; the point is
        // that the message send reaches a real implementation instead of a nil receiver. Reading
        // also forces the framework load that registers the class with the Objective-C runtime,
        // which is why the class lookup is asserted afterwards rather than before.
        var state = new MacSystemLoginItemGateway().Read();

        Assert.IsTrue(Enum.IsDefined(state), $"SMAppService answered an unknown status: {state}.");
        Assert.AreNotEqual(IntPtr.Zero, ObjectiveCNative.GetClass("SMAppService"));
    }
}
