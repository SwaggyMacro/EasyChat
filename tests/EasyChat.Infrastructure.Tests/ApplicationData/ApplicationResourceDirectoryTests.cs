using EasyChat.Infrastructure.ApplicationData;

namespace EasyChat.Infrastructure.Tests.ApplicationData;

[TestClass]
public sealed class ApplicationResourceDirectoryTests
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "easychat-resource-directory-tests",
        Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Resolve_InsideAnApplicationBundle_UsesContentsResources()
    {
        var executable = Directory.CreateDirectory(Path.Combine(_root, "EasyChat.app", "Contents", "MacOS")).FullName;
        var resources = Directory.CreateDirectory(Path.Combine(_root, "EasyChat.app", "Contents", "Resources")).FullName;

        Assert.AreEqual(resources, ApplicationResourceDirectory.Resolve(executable + Path.DirectorySeparatorChar));
    }

    [TestMethod]
    public void Resolve_OutsideABundle_UsesTheExecutableDirectory()
    {
        var executable = Directory.CreateDirectory(Path.Combine(_root, "publish")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "Resources"));

        Assert.AreEqual(executable, ApplicationResourceDirectory.Resolve(executable));
    }

    [TestMethod]
    public void Resolve_BundleLayoutWithoutResources_UsesTheExecutableDirectory()
    {
        var executable = Directory.CreateDirectory(Path.Combine(_root, "Contents", "MacOS")).FullName;

        Assert.AreEqual(executable, ApplicationResourceDirectory.Resolve(executable));
    }
}
