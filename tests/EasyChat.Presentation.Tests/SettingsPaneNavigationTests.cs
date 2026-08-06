using EasyChat.Presentation.Features.Settings;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class SettingsPaneNavigationTests
{
    [TestMethod]
    public void BrowseMode_ShowsOnlyActivePane()
    {
        Assert.IsTrue(IsVisible(browse: true, active: SettingsPaneId.Translation, pane: SettingsPaneId.Translation, query: ""));
        Assert.IsFalse(IsVisible(browse: true, active: SettingsPaneId.Translation, pane: SettingsPaneId.General, query: ""));
    }

    [TestMethod]
    public void SearchMode_IgnoresActivePane_UsesKeywords()
    {
        Assert.IsTrue(IsVisible(
            browse: false,
            active: SettingsPaneId.General,
            pane: SettingsPaneId.Result,
            query: "font",
            header: "Result",
            fields: SettingsSearch.ResultFields));
        Assert.IsFalse(IsVisible(
            browse: false,
            active: SettingsPaneId.Result,
            pane: SettingsPaneId.General,
            query: "font",
            header: "General",
            fields: SettingsSearch.GeneralFields));
    }

    private static bool IsVisible(
        bool browse,
        SettingsPaneId active,
        SettingsPaneId pane,
        string query,
        string header = "Section",
        string fields = "keywords")
    {
        if (browse)
            return active == pane;
        return SettingsSearch.MatchesAny(query, header, fields);
    }
}
