using EasyChat.Infrastructure.Windows.Input;

namespace EasyChat.Infrastructure.Windows.Tests.Input;

[TestClass]
public sealed class WindowsKeyCombinationTests
{
    [TestMethod]
    public void ParsePressAndRelease_PreserveModifierOrderAndKeyShape()
    {
        var press = WindowsKeyCombination.ParsePress("Ctrl + Oem3");
        var release = WindowsKeyCombination.ParseRelease("Ctrl + Oem3");

        Assert.HasCount(2, press);
        Assert.AreEqual((ushort)0x11, press[0].VirtualKey);
        Assert.AreEqual((ushort)0xC0, press[1].VirtualKey);
        Assert.AreEqual(
            WindowsKeyboardInputEvent.KeyUp,
            release[0].Flags & WindowsKeyboardInputEvent.KeyUp);
        Assert.AreEqual((ushort)0xC0, release[0].VirtualKey);
        Assert.AreEqual((ushort)0x11, release[1].VirtualKey);
    }

    [TestMethod]
    public void ParsePress_SupportsRecordedDigitAndNumpadNames()
    {
        var digit = WindowsKeyCombination.ParsePress("D2");
        var numpad = WindowsKeyCombination.ParsePress("NumPad1");

        Assert.AreEqual((ushort)'2', digit.Single().VirtualKey);
        Assert.AreEqual((ushort)0x61, numpad.Single().VirtualKey);
    }
}
