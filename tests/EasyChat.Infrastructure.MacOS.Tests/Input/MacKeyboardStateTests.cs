using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacKeyboardStateTests
{
    [TestMethod]
    public void EveryContractKeyAnswersWithoutFailing()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Key state can only be verified on macOS 26 or later.");
            return;
        }

        var state = new MacKeyboardState();

        // Nothing is held while the suite runs, so every key reads as up. The value matters less
        // than the fact that each one reaches a real CoreGraphics query instead of throwing.
        foreach (var key in Enum.GetValues<KeyboardKey>())
            Assert.IsFalse(state.IsPressed(key), key.ToString());
    }

    [TestMethod]
    public void AKeyOutsideTheContractIsNotReportedAsPressed()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Key state can only be verified on macOS 26 or later.");
            return;
        }

        Assert.IsFalse(new MacKeyboardState().IsPressed((KeyboardKey)99));
    }
}
