using System.Xml.Linq;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class SettingViewLayoutTests
{
    [TestMethod]
    public void SettingsLayout_IsNotWrappedInAnOuterScrollViewer()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "src",
            "Presentation",
            "EasyChat.Presentation",
            "Features",
            "Settings",
            "Views",
            "SettingView.axaml");
        var document = XDocument.Load(path);
        var settingsLayout = document.Descendants()
            .Single(element => element.Name.LocalName == "SettingsLayout");

        Assert.IsFalse(settingsLayout.Ancestors().Any(element => element.Name.LocalName == "ScrollViewer"));
        Assert.AreEqual("240", settingsLayout.Attribute("StackSummaryWidth")?.Value);
    }

    [TestMethod]
    public void GeneralSettings_AsrModelsUseCollapsibleListBindings()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "src",
            "Presentation",
            "EasyChat.Presentation",
            "Features",
            "Settings",
            "Views",
            "GeneralSettingsView.axaml");
        var document = XDocument.Load(path);

        var modelList = document.Descendants()
            .Single(element => element.Name.LocalName == "ItemsControl"
                               && element.Attribute("ItemsSource")?.Value == "{Binding VisibleAsrModels}");
        var toggle = document.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                               && element.Attribute("Command")?.Value == "{Binding ToggleAsrModelListCommand}");

        Assert.AreEqual("{Binding HasAsrModels}", modelList.Attribute("IsVisible")?.Value);
        Assert.AreEqual(
            "{Binding IsAsrModelListToggleVisible}",
            toggle.Attribute("IsVisible")?.Value);
        Assert.AreEqual(
            "{Binding AsrModelListToggleText}",
            toggle.Attribute("ToolTip.Tip")?.Value);
    }

    [TestMethod]
    public void GeneralSettings_CompactOcrLanguagesOpenDetailsDialog()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "src",
            "Presentation",
            "EasyChat.Presentation",
            "Features",
            "Settings",
            "Views",
            "GeneralSettingsView.axaml");
        var document = XDocument.Load(path);

        var detailsButton = document.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                               && element.Attribute("Command")?.Value?.Contains(
                                   "ShowOcrModelLanguagesCommand",
                                   StringComparison.Ordinal) == true);

        Assert.AreEqual(
            "{Binding IsSupportedLanguageListCompact}",
            detailsButton.Parent?.Attribute("IsVisible")?.Value);
        Assert.AreEqual("Horizontal", detailsButton.Parent?.Attribute("Orientation")?.Value);
        Assert.AreEqual("28", detailsButton.Attribute("Width")?.Value);
        Assert.AreEqual("28", detailsButton.Attribute("Height")?.Value);
        Assert.AreEqual("Transparent", detailsButton.Attribute("Background")?.Value);
        Assert.AreEqual("0", detailsButton.Attribute("BorderThickness")?.Value);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EasyChat.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
