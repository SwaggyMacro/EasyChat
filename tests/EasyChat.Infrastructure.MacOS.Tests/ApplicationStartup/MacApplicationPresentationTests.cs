using EasyChat.Infrastructure.MacOS.ApplicationStartup;

namespace EasyChat.Infrastructure.MacOS.Tests.ApplicationStartup;

[TestClass]
public sealed class MacApplicationPresentationTests
{
    [TestMethod]
    public void AProcessCanTakeItselfOutOfTheDock()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("The activation policy can only be verified on macOS 26 or later.");
            return;
        }

        // The screenshot worker is the same executable as the application, so without this it would
        // show up as a second EasyChat in the Dock and the application switcher every time the user
        // takes a screenshot.
        Assert.IsTrue(
            MacApplicationPresentation.TryHideFromDock(),
            "macOS refused to narrow the activation policy.");
    }
}
