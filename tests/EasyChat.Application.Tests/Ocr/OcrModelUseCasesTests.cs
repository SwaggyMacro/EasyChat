using EasyChat.Application.Ocr;
using EasyChat.Application.Tests.Settings;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Settings;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.Ocr;

[TestClass]
public sealed class OcrModelUseCasesTests
{
    [TestMethod]
    public async Task DownloadModelAsync_AppliesCurrentProxyPolicy()
    {
        var bundle = SettingsTestData.CreateBundle() with
        {
            Proxy = new ProxySettings("http://127.0.0.1:7890"),
            Ocr = new OcrSettings(true)
        };
        var store = new FakeOcrModelStore();
        var useCases = new OcrModelUseCases(store, new FakeSettingsUseCases(bundle));
        var package = store.ModelPackages[0];

        await useCases.DownloadModelAsync(package);

        Assert.AreEqual(package.Id, store.DownloadedPackage?.Id);
        Assert.AreEqual("http://127.0.0.1:7890", store.Options?.ProxyUrl);
        Assert.IsTrue(store.Options?.UseProxy);
    }

    private sealed class FakeOcrModelStore : IOcrModelStore
    {
        public IReadOnlyList<OcrModelPackage> ModelPackages { get; } =
        [
            new("test-package", [OcrLanguages.English])
        ];
        public OcrModelPackage? DownloadedPackage { get; private set; }
        public OcrModelDownloadOptions? Options { get; private set; }

        public bool IsModelDownloaded(OcrModelPackage package) => false;

        public Task DownloadModelAsync(
            OcrModelPackage package,
            OcrModelDownloadOptions options,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadedPackage = package;
            Options = options;
            return Task.CompletedTask;
        }

        public void DeleteModel(OcrModelPackage package)
        {
        }
    }

    private sealed class FakeSettingsUseCases(SettingsBundle current) : ISettingsUseCases
    {
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed
        {
            add { }
            remove { }
        }
        public bool IsInitialized => true;
        public SettingsBundle Current { get; } = current;

        public ValueTask<Result<SettingsBundle>> InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<SettingsBundle>.Success(Current));

        public Result Update(SettingsSection section, SettingsBundle settings) => Result.Success();
        public ValueTask<Result> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
