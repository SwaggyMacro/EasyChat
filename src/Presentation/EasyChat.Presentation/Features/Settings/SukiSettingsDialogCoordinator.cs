using EasyChat.Contracts.AiModels;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Settings.Translation;
using EasyChat.Presentation.Features.Speech;
using EasyChat.Presentation.Foundation.UiHost;

namespace EasyChat.Presentation.Features.Settings;

public sealed class SukiSettingsDialogCoordinator(
    IUiDialogHost dialogs,
    IUiToastHost toasts,
    SettingsSession settings,
    IAiModelCatalogTransport modelCatalog,
    ITtsUseCases tts,
    IScreenRegionPicker regionPicker) : ISettingsDialogCoordinator
{
    private readonly IUiDialogHost _dialogs = dialogs;
    private readonly IUiToastHost _toasts = toasts;
    private readonly SettingsSession _settings = settings;
    private readonly IAiModelCatalogTransport _modelCatalog = modelCatalog;
    private readonly ITtsUseCases _tts = tts;
    private readonly IScreenRegionPicker _regionPicker = regionPicker;

    public void EditAiModel(CustomAiModelState? model) => _dialogs.ShowContent(new UiContentDialogOptions
    {
        Title = model is null ? Resources.AddModel : Resources.EditModel,
        CreateContent = session => new AiModelEditDialogViewModel(session, _modelCatalog, model)
        {
            OnClose = result => SaveModel(model, result)
        }
    });

    public void DeleteAiModel(CustomAiModelState model) => _dialogs.ShowMessage(new UiMessageDialogOptions
    {
        Title = Resources.ConfirmDeletion,
        Message = Resources.ConfirmDeleteModel,
        Severity = UiMessageSeverity.Warning,
        PrimaryText = Resources.Delete,
        PrimaryIsDanger = true,
        OnPrimary = () => _settings.AiModel.ConfiguredModels.Remove(model),
        SecondaryText = Resources.Cancel
    });

    public void ConfirmDeleteAsrModel(SpeechRecognitionModel model, Action onConfirmed) =>
        _dialogs.ShowMessage(new UiMessageDialogOptions
        {
            Title = Resources.ConfirmDeletion,
            Message = string.Format(Resources.ConfirmDeleteAsrModel, model.Id),
            Severity = UiMessageSeverity.Warning,
            PrimaryText = Resources.Delete,
            PrimaryIsDanger = true,
            OnPrimary = onConfirmed,
            SecondaryText = Resources.Cancel
        });

    public void ShowInformation(string title, string content) => _dialogs.CreateDialog()
        .WithTitle(title)
        .WithContent(content)
        .WithActionButton(Resources.Close, _ => { }, true, string.Empty)
        .TryShow();

    public void EditAiModelKeys(CustomAiModelState model) => ShowStringKeys(
        $"{model.Name} API Keys",
        model.ApiKeys,
        values => Replace(model.ApiKeys, values));

    public void EditBaiduKeys()
    {
        var items = _settings.MachineTranslation.Baidu.Items.Select(item =>
            (KeyItemViewModelBase)new BaiduKeyItemViewModel
            {
                AppId = item.AppId,
                AppKey = item.AppKey
            });
        ShowKeyEditor(Resources.Baidu, KeyListType.Baidu, items, edited =>
        {
            _settings.MachineTranslation.Baidu.Items.Clear();
            foreach (var item in edited.OfType<BaiduKeyItemViewModel>())
            {
                if (!string.IsNullOrWhiteSpace(item.AppId) || !string.IsNullOrWhiteSpace(item.AppKey))
                {
                    _settings.MachineTranslation.Baidu.Items.Add(new BaiduCredentialState(
                        new BaiduCredentialSettings(item.AppId, item.AppKey),
                        _settings.FlushSection));
                }
            }
        });
    }

    public void EditTencentKeys()
    {
        var items = _settings.MachineTranslation.Tencent.Items.Select(item =>
            (KeyItemViewModelBase)new TencentKeyItemViewModel
            {
                SecretId = item.SecretId,
                SecretKey = item.SecretKey
            });
        ShowKeyEditor(Resources.Tencent, KeyListType.Tencent, items, edited =>
        {
            _settings.MachineTranslation.Tencent.Items.Clear();
            foreach (var item in edited.OfType<TencentKeyItemViewModel>())
            {
                if (!string.IsNullOrWhiteSpace(item.SecretId) || !string.IsNullOrWhiteSpace(item.SecretKey))
                {
                    _settings.MachineTranslation.Tencent.Items.Add(new TencentCredentialState(
                        new TencentCredentialSettings(item.SecretId, item.SecretKey),
                        _settings.FlushSection));
                }
            }
        });
    }

    public void EditGoogleKeys() => ShowStringKeys(
        Resources.Google,
        _settings.MachineTranslation.Google.ApiKeys,
        values => Replace(_settings.MachineTranslation.Google.ApiKeys, values));

    public void EditDeepLKeys() => ShowStringKeys(
        Resources.DeepL,
        _settings.MachineTranslation.DeepL.ApiKeys,
        values => Replace(_settings.MachineTranslation.DeepL.ApiKeys, values));

    public void ManageFixedAreas() => _dialogs.ShowContent(new UiContentDialogOptions
    {
        Title = Resources.FixedAreas,
        CreateContent = session => new FixedAreaEditDialogViewModel(
            _dialogs, session, _settings, _regionPicker)
    });

    public void ConfigureTts() => _dialogs.ShowContent(new UiContentDialogOptions
    {
        Title = Resources.Tts_Configuration,
        CreateContent = session => new TtsVoiceSettingsDialogViewModel(
            _dialogs, session, _toasts, _tts, _settings.Tts)
    });

    private void SaveModel(CustomAiModelState? existing, CustomAiModelSettings? value)
    {
        if (value is null)
            return;
        if (existing is null)
        {
            _settings.AiModel.ConfiguredModels.Add(new CustomAiModelState(value, _settings.FlushSection));
            return;
        }

        existing.Name = value.Name;
        existing.ModelType = value.ModelType;
        existing.ApiUrl = value.ApiUrl;
        existing.Model = value.Model;
        existing.UseProxy = value.UseProxy;
        existing.EnableThinking = value.EnableThinking;
        Replace(existing.ApiKeys, value.ApiKeys);
    }

    private void ShowStringKeys(
        string title,
        IEnumerable<string> values,
        Action<IReadOnlyList<string>> save)
    {
        var items = values.Select(value =>
            (KeyItemViewModelBase)new StringKeyItemViewModel { Value = value });
        ShowKeyEditor(title, KeyListType.String, items, edited => save(
            edited.OfType<StringKeyItemViewModel>()
                .Select(item => item.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray()));
    }

    private void ShowKeyEditor(
        string title,
        KeyListType type,
        IEnumerable<KeyItemViewModelBase> items,
        Action<IReadOnlyList<KeyItemViewModelBase>> save) => _dialogs.ShowContent(new UiContentDialogOptions
    {
        Title = title,
        CreateContent = session => new KeyListEditorViewModel(session, title, type, items)
        {
            OnSave = save
        }
    });

    private static void Replace<T>(ICollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }
}
