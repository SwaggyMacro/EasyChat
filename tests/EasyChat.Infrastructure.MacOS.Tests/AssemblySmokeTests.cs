namespace EasyChat.Infrastructure.MacOS.Tests;

[TestClass]
public sealed class AssemblySmokeTests
{
    [TestMethod]
    public void MacOSAssembly_IsLoadable() =>
        Assert.IsNotNull(typeof(Infrastructure.MacOS.AssemblyMarker).Assembly);
}
