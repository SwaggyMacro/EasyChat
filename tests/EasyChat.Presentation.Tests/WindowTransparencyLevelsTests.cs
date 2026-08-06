using Avalonia.Controls;
using EasyChat.Presentation.Foundation.Platform;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class WindowTransparencyLevelsTests
{
    [TestMethod]
    public void AcrylicPreference_DegradesThroughBlurAndTransparent()
    {
        var levels = WindowTransparencyLevels.ForPreference("AcrylicBlur");

        CollectionAssert.AreEqual(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.Transparent,
                WindowTransparencyLevel.None
            },
            levels.ToArray());
    }

    [TestMethod]
    public void BlurPreference_SkipsAcrylic()
    {
        var levels = WindowTransparencyLevels.ForPreference("Blur");

        Assert.AreEqual(WindowTransparencyLevel.Blur, levels[0]);
        Assert.IsFalse(levels.Contains(WindowTransparencyLevel.AcrylicBlur));
        Assert.IsTrue(levels.Contains(WindowTransparencyLevel.None));
    }

    [TestMethod]
    public void TransparentAndUnknown_FallBackToSolidNone()
    {
        var transparent = WindowTransparencyLevels.ForPreference("Transparent");
        var unknown = WindowTransparencyLevels.ForPreference("SomethingElse");

        CollectionAssert.AreEqual(
            new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None },
            transparent.ToArray());
        CollectionAssert.AreEqual(transparent.ToArray(), unknown.ToArray());
    }

    [TestMethod]
    public void NonePreference_IsOpaqueOnly()
    {
        var levels = WindowTransparencyLevels.ForPreference("None");
        CollectionAssert.AreEqual(new[] { WindowTransparencyLevel.None }, levels.ToArray());
    }

    [TestMethod]
    public void Preferences_ExposeSettingsChoicesIncludingNone()
    {
        CollectionAssert.Contains(WindowTransparencyLevels.Preferences.ToList(), "AcrylicBlur");
        CollectionAssert.Contains(WindowTransparencyLevels.Preferences.ToList(), "None");
    }
}
