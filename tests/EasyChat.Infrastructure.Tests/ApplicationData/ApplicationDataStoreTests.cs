using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Settings;
using EasyChat.Infrastructure.ApplicationData;
using EasyChat.Infrastructure.Settings.Persistence;

namespace EasyChat.Infrastructure.Tests.ApplicationData;

[TestClass]
public sealed class ApplicationDataStoreTests
{
    [TestMethod]
    public void Constructor_MigratesAllLegacyConfigurationAndModelsWithoutOverwriting()
    {
        using var workspace = new TestWorkspace();
        var defaultRoot = workspace.Directory("data");
        var application = workspace.Directory("application");
        var legacyOcr = workspace.Directory("legacy-ocr");
        Write(Path.Combine(defaultRoot, "Configuration", "General.json"), "current");
        Write(Path.Combine(application, "Configuration", "General.json"), "legacy");
        Write(Path.Combine(application, "Configuration", "Nested", "AllSettings.json"), "settings");
        Write(Path.Combine(application, "Models", "en-US", "model.int8.onnx"), "asr");
        Write(Path.Combine(legacyOcr, "english", "inference.pdiparams"), "ocr");

        var store = new ApplicationDataStore(
            defaultRoot,
            application,
            workspace.Path("location.json"),
            legacyOcr);

        Assert.AreEqual("current", File.ReadAllText(Path.Combine(
            store.ConfigurationDirectory,
            "General.json")));
        Assert.AreEqual("settings", File.ReadAllText(Path.Combine(
            store.ConfigurationDirectory,
            "Nested",
            "AllSettings.json")));
        Assert.AreEqual("asr", File.ReadAllText(Path.Combine(
            store.SpeechModelsDirectory,
            "en-US",
            "model.int8.onnx")));
        Assert.AreEqual("ocr", File.ReadAllText(Path.Combine(
            store.OcrModelsDirectory,
            "english",
            "inference.pdiparams")));
    }

    [TestMethod]
    public async Task ChangeLocationAsync_MigratesEveryDataAreaThenPersistsAndPublishesTheLocation()
    {
        using var workspace = new TestWorkspace();
        var defaultRoot = workspace.Directory("data");
        var application = workspace.Directory("application");
        var legacyOcr = workspace.Directory("legacy-ocr");
        var locationFile = workspace.Path("bootstrap", "location.json");
        var store = new ApplicationDataStore(defaultRoot, application, locationFile, legacyOcr);
        Write(Path.Combine(store.ConfigurationDirectory, "General.json"), "settings");
        Write(Path.Combine(store.SpeechModelsDirectory, "zh-CN", "model.onnx"), "asr");
        Write(Path.Combine(store.OcrModelsDirectory, "chinese", "model.nb"), "ocr");
        var target = workspace.Directory("custom-data");
        ApplicationDataLocationChangedEventArgs? changed = null;
        store.LocationChanged += (_, args) => changed = args;

        var result = await store.ChangeLocationAsync(target);

        Assert.IsTrue(result.IsSuccess, result.Error.Message);
        Assert.AreEqual(Path.GetFullPath(target), result.Value.RootDirectory);
        Assert.IsFalse(result.Value.IsDefault);
        Assert.IsNotNull(changed);
        Assert.AreEqual(Path.GetFullPath(defaultRoot), changed.Previous.RootDirectory);
        Assert.AreEqual(Path.GetFullPath(target), changed.Current.RootDirectory);
        Assert.AreEqual("settings", File.ReadAllText(Path.Combine(
            target,
            "Configuration",
            "General.json")));
        Assert.AreEqual("asr", File.ReadAllText(Path.Combine(
            target,
            "Models",
            "ASR",
            "zh-CN",
            "model.onnx")));
        Assert.AreEqual("ocr", File.ReadAllText(Path.Combine(
            target,
            "Models",
            "OCR",
            "chinese",
            "model.nb")));
        Assert.IsTrue(File.Exists(Path.Combine(defaultRoot, "Configuration", "General.json")));

        var reloaded = new ApplicationDataStore(defaultRoot, application, locationFile, legacyOcr);
        Assert.AreEqual(Path.GetFullPath(target), reloaded.Current.RootDirectory);
    }

    [TestMethod]
    public async Task ChangeLocationAsync_RejectsNonEmptyTargetAndKeepsCurrentLocation()
    {
        using var workspace = new TestWorkspace();
        var defaultRoot = workspace.Directory("data");
        var target = workspace.Directory("occupied");
        Write(Path.Combine(target, "unrelated.txt"), "keep");
        var store = new ApplicationDataStore(
            defaultRoot,
            workspace.Directory("application"),
            workspace.Path("location.json"),
            workspace.Directory("legacy-ocr"));

        var result = await store.ChangeLocationAsync(target);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("application-data.location-not-empty", result.Error.Code);
        Assert.AreEqual(Path.GetFullPath(defaultRoot), store.Current.RootDirectory);
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "unrelated.txt")));
    }

    [TestMethod]
    public async Task SettingsGateway_WritesOnlyToTheActiveConfigurationDirectoryAfterMigration()
    {
        using var workspace = new TestWorkspace();
        var defaultRoot = workspace.Directory("data");
        var store = new ApplicationDataStore(
            defaultRoot,
            workspace.Directory("application"),
            workspace.Path("location.json"),
            workspace.Directory("legacy-ocr"));
        var gateway = new JsonSettingsPersistenceGateway(() => store.ConfigurationDirectory);
        var initial = await gateway.ReadAllAsync();
        Assert.IsTrue(initial.IsSuccess, initial.Error.Message);
        var oldProxyPath = Path.Combine(store.ConfigurationDirectory, "Proxy.json");
        var oldProxy = File.ReadAllText(oldProxyPath);
        var target = workspace.Directory("custom-data");
        var move = await store.ChangeLocationAsync(target);
        Assert.IsTrue(move.IsSuccess, move.Error.Message);

        var changed = initial.Value with
        {
            Proxy = new ProxySettings("http://127.0.0.1:7890")
        };
        var write = await gateway.WriteAsync(SettingsSection.Proxy, changed);

        Assert.IsTrue(write.IsSuccess, write.Error.Message);
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(target, "Configuration", "Proxy.json")),
            "http://127.0.0.1:7890");
        Assert.AreEqual(oldProxy, File.ReadAllText(oldProxyPath));
    }

    private static void Write(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "EasyChat.ApplicationData.Tests",
            Guid.NewGuid().ToString("N"));

        public TestWorkspace() => System.IO.Directory.CreateDirectory(_root);

        public string Path(params string[] parts) =>
            System.IO.Path.Combine([_root, .. parts]);

        public string Directory(params string[] parts)
        {
            var path = Path(parts);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_root))
                System.IO.Directory.Delete(_root, recursive: true);
        }
    }
}
