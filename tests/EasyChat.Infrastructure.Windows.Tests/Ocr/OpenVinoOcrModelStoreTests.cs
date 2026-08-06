using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Ocr;
using EasyChat.Infrastructure.Windows.Ocr;
using Sdcb.OpenVINO.PaddleOCR.Models.Online;

namespace EasyChat.Infrastructure.Windows.Tests.Ocr;

[TestClass]
public sealed class OpenVinoOcrModelStoreTests
{
    private string? _workspace;

    [TestInitialize]
    public void Initialize()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"easychat-openvino-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_workspace is not null && Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [TestMethod]
    public void DeleteModel_PreservesComponentsUsedByAnotherInstalledPackage()
    {
        var paths = new FakeApplicationDataPaths(Path.Combine(Workspace, "models"));
        using var backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
        var arabic = GetSpec(OpenVinoOcrModelCatalog.ArabicV4Id);
        var devanagari = GetSpec(OpenVinoOcrModelCatalog.DevanagariV4Id);
        WriteCompleteModel(arabic);
        WriteCompleteModel(devanagari);
        var sharedRoots = GetRoots(arabic)
            .Intersect(GetRoots(devanagari), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.IsNotEmpty(sharedRoots);

        backend.DeleteModel(arabic);

        Assert.IsFalse(backend.IsModelAvailable(arabic));
        Assert.IsTrue(backend.IsModelAvailable(devanagari));
        Assert.IsTrue(sharedRoots.All(Directory.Exists));
    }

    [TestMethod]
    public void V5Directory_IsNeverConsideredAvailableOrDeleted()
    {
        var paths = new FakeApplicationDataPaths(Path.Combine(Workspace, "models"));
        using var backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
        var v5Root = Path.Combine(paths.OcrModelsDirectory, "ch_PP-OCRv5_rec_infer");
        Directory.CreateDirectory(v5Root);
        File.WriteAllBytes(Path.Combine(v5Root, "inference.json"), [1]);
        File.WriteAllBytes(Path.Combine(v5Root, "inference.pdiparams"), [1]);
        var universal = GetSpec(OpenVinoOcrModelCatalog.UniversalV6SmallId);

        Assert.IsFalse(backend.IsModelAvailable(universal));
        backend.DeleteModel(universal);

        Assert.IsTrue(File.Exists(Path.Combine(v5Root, "inference.json")));
        Assert.IsTrue(File.Exists(Path.Combine(v5Root, "inference.pdiparams")));
    }

    [TestMethod]
    public async Task DownloadModelAsync_HonorsCancellationBeforeNetworkAccess()
    {
        var paths = new FakeApplicationDataPaths(Path.Combine(Workspace, "models"));
        using var backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => backend.DownloadModelAsync(
            GetSpec(OpenVinoOcrModelCatalog.KoreanV4Id),
            new OcrModelDownloadOptions(null, false),
            progress: null,
            cancellation.Token));
    }

    [TestMethod]
    public async Task DownloadComponentAsync_ReplacesIncompleteDirectoryBeforeRetry()
    {
        var paths = new FakeApplicationDataPaths(Path.Combine(Workspace, "models"));
        using var backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
        var root = Path.Combine(paths.OcrModelsDirectory, "retry-model");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "inference.pdiparams"), [1]);
        var package = new OpenVinoOcrModelPackageSpec(
            new OcrModelPackage("retry-package", [OcrLanguages.English]),
            () => null!,
            OpenVinoOcrModelFormat.Paddle);
        var downloadCalls = 0;

        await backend.DownloadComponentAsync(
            package,
            root,
            requireYaml: false,
            _ =>
            {
                downloadCalls++;
                Assert.IsFalse(Directory.Exists(root));
                Directory.CreateDirectory(root);
                File.WriteAllBytes(Path.Combine(root, "inference.pdmodel"), [1]);
                File.WriteAllBytes(Path.Combine(root, "inference.pdiparams"), [1]);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual(1, downloadCalls);
        Assert.IsTrue(OpenVinoWindowsOcrBackend.IsPaddleModelComplete(root));
    }

    [TestMethod]
    public async Task DownloadComponentAsync_RepairsNestedPaddleModelFromFailedCache()
    {
        var paths = new FakeApplicationDataPaths(Path.Combine(Workspace, "models"));
        using var backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
        var root = Path.Combine(paths.OcrModelsDirectory, "ka_PP-OCRv4_rec");
        WriteNestedPaddleModel(root);
        var package = CreatePaddlePackage("kannada-v4");
        var downloadCalls = 0;

        await backend.DownloadComponentAsync(
            package,
            root,
            requireYaml: false,
            _ =>
            {
                downloadCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual(0, downloadCalls);
        Assert.IsTrue(OpenVinoWindowsOcrBackend.IsPaddleModelComplete(root));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "kannada_PP-OCRv4_rec_infer")));
    }

    [TestMethod]
    public async Task DownloadComponentAsync_RepairsNestedPaddleModelAfterUpstreamFailure()
    {
        var paths = new FakeApplicationDataPaths(Path.Combine(Workspace, "models"));
        using var backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
        var root = Path.Combine(paths.OcrModelsDirectory, "ka_PP-OCRv4_rec");
        var package = CreatePaddlePackage("kannada-v4");

        await backend.DownloadComponentAsync(
            package,
            root,
            requireYaml: false,
            _ =>
            {
                WriteNestedPaddleModel(root);
                throw new Exception($"inference.pdiparams not found in {root}, model error?");
            },
            CancellationToken.None);

        Assert.IsTrue(OpenVinoWindowsOcrBackend.IsPaddleModelComplete(root));
        Assert.IsFalse(File.Exists(Path.Combine(root, "ka_PP-OCRv4_rec_infer.tar")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "._kannada_PP-OCRv4_rec_infer")));
    }

    [TestMethod]
    public void LocationChange_RepointsTheOnlineModelDirectory()
    {
        var paths = new FakeApplicationDataPaths(Path.Combine(Workspace, "models-a"));
        using var backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
        var second = Path.Combine(Workspace, "models-\u6A21\u578B-\U0001F680");

        paths.ChangeOcrDirectory(second);

        var root = GetSpec(OpenVinoOcrModelCatalog.UniversalV6SmallId)
            .CreateOnlineModel()
            .RecModel
            .RootDirectory;
        Assert.StartsWith(Path.GetFullPath(second), Path.GetFullPath(root));
    }

    private string Workspace => _workspace
        ?? throw new InvalidOperationException("Test workspace is not initialized.");

    private static OpenVinoOcrModelPackageSpec GetSpec(string id) =>
        OpenVinoOcrModelCatalog.Specs.Single(spec => spec.Package.Id == id);

    private static OpenVinoOcrModelPackageSpec CreatePaddlePackage(string id) => new(
        new OcrModelPackage(id, [OcrLanguages.Kannada]),
        () => null!,
        OpenVinoOcrModelFormat.Paddle);

    private static void WriteNestedPaddleModel(string root)
    {
        var nested = Path.Combine(root, "kannada_PP-OCRv4_rec_infer");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "inference.pdmodel"), [1]);
        File.WriteAllBytes(Path.Combine(nested, "inference.pdiparams"), [1]);
        File.WriteAllBytes(Path.Combine(root, "ka_PP-OCRv4_rec_infer.tar"), [1]);
        File.WriteAllBytes(Path.Combine(root, "._kannada_PP-OCRv4_rec_infer"), [1]);
    }

    private static void WriteCompleteModel(OpenVinoOcrModelPackageSpec spec)
    {
        foreach (var root in GetRoots(spec))
        {
            Directory.CreateDirectory(root);
            if (spec.Format == OpenVinoOcrModelFormat.OnnxV6)
            {
                File.WriteAllBytes(Path.Combine(root, "inference.onnx"), [1]);
            }
            else
            {
                File.WriteAllBytes(Path.Combine(root, "inference.pdmodel"), [1]);
                File.WriteAllBytes(Path.Combine(root, "inference.pdiparams"), [1]);
            }
        }

        if (spec.Format == OpenVinoOcrModelFormat.OnnxV6)
        {
            File.WriteAllText(
                Path.Combine(spec.CreateOnlineModel().RecModel.RootDirectory, "inference.yml"),
                "model: v6");
        }
    }

    private static string[] GetRoots(OpenVinoOcrModelPackageSpec spec)
    {
        var model = spec.CreateOnlineModel();
        return model.ClsModel is null
            ? [model.DetModel.RootDirectory, model.RecModel.RootDirectory]
            : [model.DetModel.RootDirectory, model.ClsModel.RootDirectory, model.RecModel.RootDirectory];
    }

    private sealed class FakeApplicationDataPaths(string ocrModelsDirectory) : IApplicationDataPaths
    {
        private string _ocrModelsDirectory = Path.GetFullPath(ocrModelsDirectory);

        public event EventHandler<ApplicationDataLocationChangedEventArgs>? LocationChanged;

        public ApplicationDataLocation Current =>
            new(Path.GetDirectoryName(_ocrModelsDirectory)!, IsDefault: false);

        public string ConfigurationDirectory => Path.Combine(Current.RootDirectory, "Configuration");
        public string SpeechModelsDirectory => Path.Combine(Current.RootDirectory, "Models", "ASR");
        public string OcrModelsDirectory => _ocrModelsDirectory;

        public void ChangeOcrDirectory(string directory)
        {
            var previous = Current;
            _ocrModelsDirectory = Path.GetFullPath(directory);
            LocationChanged?.Invoke(
                this,
                new ApplicationDataLocationChangedEventArgs(previous, Current));
        }
    }
}
