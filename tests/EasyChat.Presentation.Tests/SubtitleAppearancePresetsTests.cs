using EasyChat.Presentation.Features.Speech;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class SubtitleAppearancePresetsTests
{
    [TestMethod]
    public void Catalog_HasFiveDistinctPresets()
    {
        Assert.AreEqual(5, SubtitleAppearancePresets.All.Count);
        var ids = SubtitleAppearancePresets.All.Select(preset => preset.Id).ToHashSet(StringComparer.Ordinal);
        Assert.AreEqual(5, ids.Count);
        Assert.IsTrue(ids.Contains(SubtitleAppearancePresets.ClassicDarkId));
        Assert.IsTrue(ids.Contains(SubtitleAppearancePresets.NeonId));
    }

    [TestMethod]
    public void Find_IsCaseInsensitive()
    {
        var preset = SubtitleAppearancePresets.Find("HIGH-CONTRAST");
        Assert.IsNotNull(preset);
        Assert.AreEqual(SubtitleAppearancePresets.HighContrastId, preset.Id);
        Assert.IsTrue(preset.PrimaryFontSize >= 18);
        Assert.IsFalse(string.IsNullOrWhiteSpace(preset.PrimaryFontColor));
    }

    [TestMethod]
    public void Find_Unknown_ReturnsNull()
    {
        Assert.IsNull(SubtitleAppearancePresets.Find(null));
        Assert.IsNull(SubtitleAppearancePresets.Find("missing"));
    }

    [TestMethod]
    public void EachPreset_HasValidOpacityAndColors()
    {
        foreach (var preset in SubtitleAppearancePresets.All)
        {
            Assert.IsTrue(preset.WindowOpacity is > 0 and <= 1, preset.Id);
            Assert.IsTrue(preset.PrimaryFontColor.StartsWith('#'), preset.Id);
            Assert.IsTrue(preset.SecondaryFontColor.StartsWith('#'), preset.Id);
            Assert.IsTrue(preset.BackgroundColor.StartsWith('#'), preset.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(preset.DisplayName), preset.Id);
        }
    }
}
