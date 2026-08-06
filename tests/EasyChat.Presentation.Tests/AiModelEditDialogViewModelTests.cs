using System.Reflection;
using EasyChat.Contracts.AiModels;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Settings.Translation;
using SukiUI.Dialogs;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class AiModelEditDialogViewModelTests
{
    [TestMethod]
    public void NewModel_DoesNotUseHardCodedModelDefaults()
    {
        var viewModel = CreateViewModel(new RecordingCatalog([]));

        Assert.AreEqual(string.Empty, viewModel.Model);

        foreach (var modelType in Enum.GetValues<AiModelType>())
        {
            viewModel.SelectedModelType = modelType;
            Assert.AreEqual(string.Empty, viewModel.Model, modelType.ToString());
        }
    }

    [TestMethod]
    public async Task ModelTypeChange_SelectsModelReturnedByFetch()
    {
        var existing = new CustomAiModelState(
            new CustomAiModelSettings(
                "model-id",
                "Existing",
                AiModelType.OpenAi,
                ["api-key"],
                "https://api.openai.com/v1",
                "existing-model",
                false,
                false),
            _ => EasyChat.Shared.Results.Result.Success());
        var viewModel = CreateViewModel(new RecordingCatalog(["fetched-model"]), existing: existing);

        viewModel.SelectedModelType = AiModelType.DeepSeek;
        Assert.AreEqual(string.Empty, viewModel.Model);

        ((System.Windows.Input.ICommand)viewModel.FetchModelsCommand).Execute(null);
        await WaitForAsync(() => viewModel.Model == "fetched-model");

        Assert.AreEqual("fetched-model", viewModel.Model);
    }

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
        viewModel.Model = "manually-entered-model";

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
        Action<CustomAiModelSettings?>? onClose = null,
        CustomAiModelState? existing = null) =>
        new(DispatchProxy.Create<ISukiDialog, NullDialogProxy>(), catalog, existing)
        {
            OnClose = onClose
        };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(25, timeout.Token);
    }

    public class NullDialogProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || targetMethod.ReturnType == typeof(void))
                return null;
            return targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
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
