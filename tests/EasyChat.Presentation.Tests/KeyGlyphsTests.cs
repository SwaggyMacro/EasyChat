using EasyChat.Presentation.Shared.Controls;

namespace EasyChat.Presentation.Tests;

/// <summary>
/// Shortcuts are persisted with platform-neutral names, so how they are written on screen is the
/// host's decision. These assert the mapping itself rather than which platform selects it.
/// </summary>
[TestClass]
public sealed class KeyGlyphsTests
{
    [TestMethod]
    public void MacSymbolsReplaceTheSpelledOutModifiers()
    {
        var mac = KeyGlyphConvention.MacSymbols;

        Assert.AreEqual("⌘", KeyGlyphs.Format("Meta", mac));
        Assert.AreEqual("⌥", KeyGlyphs.Format("Alt", mac));
        Assert.AreEqual("⌃", KeyGlyphs.Format("Ctrl", mac));
        Assert.AreEqual("⇧", KeyGlyphs.Format("Shift", mac));
    }

    [TestMethod]
    public void WindowsKeepsTheNamesItAlwaysShowed()
    {
        var names = KeyGlyphConvention.Names;

        Assert.AreEqual("Win", KeyGlyphs.Format("Meta", names));
        Assert.AreEqual("Alt", KeyGlyphs.Format("Alt", names));
        Assert.AreEqual("Ctrl", KeyGlyphs.Format("Ctrl", names));
        Assert.AreEqual("Shift", KeyGlyphs.Format("Shift", names));
    }

    [TestMethod]
    public void ShortcutsSavedBeforeMetaExistedStillShowTheRightSymbol()
    {
        // Older settings spelled Meta as "Win" or "Windows"; on a Mac those must appear as the
        // command symbol, not as the word "Win".
        Assert.AreEqual("⌘", KeyGlyphs.Format("Win", KeyGlyphConvention.MacSymbols));
        Assert.AreEqual("⌘", KeyGlyphs.Format("Windows", KeyGlyphConvention.MacSymbols));
    }

    [TestMethod]
    public void ModifierNamesAreMatchedRegardlessOfSpellingOrCase()
    {
        var mac = KeyGlyphConvention.MacSymbols;

        Assert.AreEqual("⌃", KeyGlyphs.Format("Control", mac));
        Assert.AreEqual("⌃", KeyGlyphs.Format("ctrl", mac));
        Assert.AreEqual("⌥", KeyGlyphs.Format("Option", mac));
        Assert.AreEqual("⌘", KeyGlyphs.Format(" meta ", mac), "surrounding spaces are trimmed");
    }

    [TestMethod]
    public void OrdinaryKeysAreUnaffectedByTheConvention()
    {
        foreach (var convention in new[] { KeyGlyphConvention.Names, KeyGlyphConvention.MacSymbols })
        {
            Assert.AreEqual("A", KeyGlyphs.Format("A", convention));
            Assert.AreEqual("F5", KeyGlyphs.Format("F5", convention));
            Assert.AreEqual("5", KeyGlyphs.Format("D5", convention), "digits are stored as D0-D9");
            Assert.AreEqual("Enter", KeyGlyphs.Format("Return", convention));
            Assert.AreEqual("[", KeyGlyphs.Format("OemOpenBrackets", convention));
            Assert.AreEqual("Backspace", KeyGlyphs.Format("Back", convention));
        }
    }

    [TestMethod]
    public void AnEmptyKeyFormatsToNothing()
    {
        Assert.AreEqual(string.Empty, KeyGlyphs.Format(string.Empty));
        Assert.AreEqual(string.Empty, KeyGlyphs.Format("   "));
    }

    [TestMethod]
    public void TheDefaultConventionIsTheSpelledOutOneSoWindowsIsUnchanged() =>
        Assert.AreEqual(KeyGlyphConvention.Names, KeyGlyphs.Convention);
}
