using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacKeyCombinationTests
{
    /// <summary>Value of <c>kVK_ANSI_A</c>.</summary>
    private const ushort AKey = 0;

    /// <summary>Value of <c>kVK_ANSI_C</c>.</summary>
    private const ushort CKey = 8;

    /// <summary>Value of <c>kVK_ANSI_V</c>.</summary>
    private const ushort VKey = 9;

    /// <summary>Value of <c>kVK_Delete</c>, which is Backspace on a Mac keyboard.</summary>
    private const ushort BackspaceKey = 51;

    [TestMethod]
    public void SelectAllBecomesCommandA()
    {
        Assert.IsTrue(MacKeyCombination.TryParse(
            MacKeyCombination.ForCommand(StandardTextCommand.SelectAll),
            out var keystroke));

        Assert.AreEqual(AKey, keystroke.VirtualKey);
        Assert.AreEqual(EventModifiers.Command, keystroke.Modifiers);
    }

    [TestMethod]
    public void CopyAndPasteBecomeCommandCAndCommandV()
    {
        Assert.IsTrue(MacKeyCombination.TryParse(
            MacKeyCombination.ForCommand(StandardTextCommand.Copy),
            out var copy));
        Assert.IsTrue(MacKeyCombination.TryParse(
            MacKeyCombination.ForCommand(StandardTextCommand.Paste),
            out var paste));

        Assert.AreEqual(CKey, copy.VirtualKey);
        Assert.AreEqual(EventModifiers.Command, copy.Modifiers);
        Assert.AreEqual(VKey, paste.VirtualKey);
        Assert.AreEqual(EventModifiers.Command, paste.Modifiers);
    }

    [TestMethod]
    public void DeleteBecomesBackspaceWithNoModifier()
    {
        // Backspace, not forward delete: it clears the selection in every macOS text control and
        // exists on every Mac keyboard.
        Assert.IsTrue(MacKeyCombination.TryParse(
            MacKeyCombination.ForCommand(StandardTextCommand.Delete),
            out var keystroke));

        Assert.AreEqual(BackspaceKey, keystroke.VirtualKey);
        Assert.AreEqual(EventModifiers.None, keystroke.Modifiers);
    }

    [TestMethod]
    public void EverySemanticCommandHasAMacOsKeystroke()
    {
        foreach (var command in Enum.GetValues<StandardTextCommand>())
        {
            Assert.IsTrue(
                MacKeyCombination.TryParse(MacKeyCombination.ForCommand(command), out _),
                command.ToString());
        }
    }

    [TestMethod]
    public void TheWindowsModifierVocabularyIsUnderstood()
    {
        Assert.IsTrue(MacKeyCombination.TryParse("Ctrl + Alt + Shift + T", out var keystroke));

        Assert.AreEqual(
            EventModifiers.Control | EventModifiers.Option | EventModifiers.Shift,
            keystroke.Modifiers);
    }

    [TestMethod]
    public void MetaAndItsPersistedWindowsAliasesBothMeanCommand()
    {
        foreach (var name in new[] { "Meta", "Win", "Windows", "Cmd", "Command" })
        {
            Assert.IsTrue(MacKeyCombination.TryParse($"{name} + A", out var keystroke), name);
            Assert.AreEqual(EventModifiers.Command, keystroke.Modifiers, name);
        }
    }

    [TestMethod]
    public void ACombinationThatIsNotASingleKeystrokeIsRejected()
    {
        Assert.IsFalse(MacKeyCombination.TryParse("A + B", out _), "two keys");
        Assert.IsFalse(MacKeyCombination.TryParse("Ctrl", out _), "modifier only");
        Assert.IsFalse(MacKeyCombination.TryParse("Ctrl + NotAKey", out _), "unknown key");
        Assert.IsFalse(MacKeyCombination.TryParse("   ", out _), "blank");
    }
}
