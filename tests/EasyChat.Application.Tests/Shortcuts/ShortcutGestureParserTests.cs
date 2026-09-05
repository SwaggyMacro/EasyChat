using EasyChat.Application.Shortcuts;
using EasyChat.Contracts.Platform;

namespace EasyChat.Application.Tests.Shortcuts;

[TestClass]
public sealed class ShortcutGestureParserTests
{
    [TestMethod]
    [DataRow("Win + K")]
    [DataRow("Windows + K")]
    [DataRow("Meta + K")]
    public void Parse_MetaAliasesUseThePortableModifier(string value)
    {
        var parsed = ShortcutGestureParser.Parse(value);

        Assert.IsTrue(parsed.IsSuccess);
        Assert.AreEqual(
            new ShortcutGesture("K", ShortcutModifiers.Meta),
            parsed.Value);
    }
}
