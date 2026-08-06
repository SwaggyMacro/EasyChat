using EasyChat.Presentation.Foundation.Platform;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class EcFontFamiliesTests
{
    [TestMethod]
    public void Resolve_Empty_UsesUiStack()
    {
        var family = EcFontFamilies.Resolve(null);
        Assert.AreEqual(EcFontFamilies.Ui, family);
        // Avalonia reports the primary face only.
        StringAssert.Contains(EcFontFamilies.UiStack, "Inter");
        StringAssert.Contains(EcFontFamilies.UiStack, "Microsoft YaHei");
        StringAssert.Contains(EcFontFamilies.UiStack, "PingFang SC");
    }

    [TestMethod]
    public void Resolve_PreferredFace_BuildsWithoutThrowing()
    {
        var family = EcFontFamilies.Resolve("Segoe UI");
        Assert.IsNotNull(family);
        // Primary name is the preferred face; full stack is in the constructor input.
        Assert.AreEqual("Segoe UI", family.Name);
        Assert.AreNotEqual(EcFontFamilies.Ui, family);
    }

    [TestMethod]
    public void Resolve_ExistingStack_PreservesAsIs()
    {
        var stack = "Comic Sans MS, Inter, sans-serif";
        var family = EcFontFamilies.Resolve(stack);
        Assert.IsNotNull(family);
        Assert.AreEqual("Comic Sans MS", family.Name);
    }

    [TestMethod]
    public void MonoStack_IncludesCrossPlatformFaces()
    {
        StringAssert.Contains(EcFontFamilies.MonoStack, "Cascadia Mono");
        StringAssert.Contains(EcFontFamilies.MonoStack, "Menlo");
        StringAssert.Contains(EcFontFamilies.MonoStack, "monospace");
    }
}
