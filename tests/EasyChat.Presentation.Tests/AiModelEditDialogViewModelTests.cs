using EasyChat.Contracts.AiModels;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Features.Settings.Translation;
using EasyChat.Presentation.Foundation.UiHost;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class AiModelEditDialogViewModelTests
{
    [TestMethod]
    public async Task ApiKeyChange_SilentlyFetchesModelsAfterDebounce()
    {
        var catalog = new RecordingCatalog(new InvalidOperationException("Unavailable"));
        var viewModel = CreateViewModel(catalog);

        viewModel.ApiKey = "updated-key";
        await WaitForAsync(() => catalog.CallCount > 0);

        Assert.AreEqual(string.Empty, viewModel.FetchModelsError);
        Assert.IsFalse(viewModel.IsFetchingModels);
    }

    [TestMethod]
    public async Task UnknownModel_RequiresConfirmationBeforeSaving()
    {
        CustomAiModelSettings? saved = null;
        var viewModel = CreateViewModel(new RecordingCatalog([]), result => saved = result);

        await WaitForAsync(() => ((System.Windows.Input.ICommand)viewModel.SaveCommand).CanExecute(null));
        ((System.Windows.Input.ICommand)viewModel.SaveCommand).Execute(null);
        await WaitForAsync(() => viewModel.IsModelConfirmationRequired);

        Assert.IsTrue(viewModel.IsModelConfirmationRequired);
        Assert.IsNull(saved);

        await WaitForAsync(() => ((System.Windows.Input.ICommand)viewModel.ConfirmSaveCommand).CanExecute(null));
        ((System.Windows.Input.ICommand)viewModel.ConfirmSaveCommand).Execute(null);
        await WaitForAsync(() => saved is not null);

        Assert.IsNotNull(saved);
        Assert.AreEqual(viewModel.Model, saved.Model);
    }

    private static AiModelEditDialogViewModel CreateViewModel(
        IAiModelCatalogTransport catalog,
        Action<CustomAiModelSettings?>? onClose = null) =>
        new(new NullDialogSession(), catalog)
        {
            OnClose = onClose
        };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(25, timeout.Token);
    }

    private sealed class NullDialogSession : IUiDialogSession
    {
        public void Dismiss()
        {
        }
    }

    private sealed class RecordingCatalog : IAiModelCatalogTransport
    {
        private readonly IReadOnlyList<string>? _models;
        private readonly Exception? _exception;
        private int _callCount;

        public RecordingCatalog(IReadOnlyList<string> models) => _models = models;

        public RecordingCatalog(Exception exception) => _exception = exception;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<IReadOnlyList<string>> FetchModelsAsync(
            AiModelCatalogRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return _exception is null
                ? Task.FromResult(_models ?? (IReadOnlyList<string>)[])
                : Task.FromException<IReadOnlyList<string>>(_exception);
        }
    }
}
