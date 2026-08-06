using EasyChat.Presentation.Features.Settings;
using EasyChat.Presentation.Lang;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class SettingsSearchFilterTests
{
    [TestMethod]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.IsTrue(SettingsSearch.Matches("", "font size"));
        Assert.IsTrue(SettingsSearch.Matches("   ", SettingsSearch.ResultFields));
        Assert.IsTrue(SettingsSearch.MatchesAny(null, Resources.General, SettingsSearch.GeneralFields));
    }

    [TestMethod]
    public void Query_MatchesSectionHeader()
    {
        Assert.IsTrue(SettingsSearch.MatchesAny("trans", "Translation", SettingsSearch.TranslationFields));
        Assert.IsTrue(SettingsSearch.MatchesAny("翻译", "翻译", SettingsSearch.TranslationFields));
        Assert.IsFalse(SettingsSearch.MatchesAny("xyz", "Translation", SettingsSearch.TranslationFields));
    }

    [TestMethod]
    public void Query_MatchesFieldKeywords_CaseInsensitive()
    {
        Assert.IsTrue(SettingsSearch.Matches("OCR", SettingsSearch.GeneralFields));
        Assert.IsTrue(SettingsSearch.Matches("划词", SettingsSearch.SelectionFields));
        Assert.IsTrue(SettingsSearch.Matches("font", SettingsSearch.ResultFields));
        Assert.IsTrue(SettingsSearch.Matches("透明", SettingsSearch.InputFields));
    }

    [TestMethod]
    public void FieldConverter_HidesNonMatchingFields()
    {
        var converter = SettingsFieldVisibleConverter.Instance;
        Assert.AreEqual(true, converter.Convert("", typeof(bool), "font size 字体", null));
        Assert.AreEqual(true, converter.Convert("font", typeof(bool), "font size 字体", null));
        Assert.AreEqual(false, converter.Convert("proxy", typeof(bool), "font size 字体", null));
    }
}
