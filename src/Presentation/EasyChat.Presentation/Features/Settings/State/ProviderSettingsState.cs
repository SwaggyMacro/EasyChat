using System.Collections.ObjectModel;
using System.Collections.Specialized;
using EasyChat.Contracts.Settings;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Settings.State;

public sealed class LiveAiModelSettings : LiveSettingsSection
{
    private readonly Func<SettingsSection, EasyChat.Shared.Results.Result> _commit;

    public LiveAiModelSettings(AiModelSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.AiModel, commit)
    {
        _commit = commit;
        ConfiguredModels = new ObservableCollection<CustomAiModelState>(
            value.ConfiguredModels.Select(model => new CustomAiModelState(model, commit)));
        ConfiguredModels.CollectionChanged += OnCollectionChanged;
    }

    public ObservableCollection<CustomAiModelState> ConfiguredModels { get; }
    public AiModelSettings ToContract() => new(ConfiguredModels.Select(model => model.ToContract()).ToArray());

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => _commit(SettingsSection.AiModel);
}

public sealed class CustomAiModelState : LiveSettingsSection
{
    private string _name;
    private AiModelType _modelType;
    private string _apiUrl;
    private string _model;
    private bool _useProxy;
    private bool _enableThinking;
    private bool _isTesting;

    public CustomAiModelState(
        CustomAiModelSettings value,
        Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.AiModel, commit)
    {
        Id = value.Id;
        _name = value.Name;
        _modelType = value.ModelType;
        _apiUrl = value.ApiUrl;
        _model = value.Model;
        _useProxy = value.UseProxy;
        _enableThinking = value.EnableThinking;
        ApiKeys = new ObservableCollection<string>(value.ApiKeys);
        ApiKeys.CollectionChanged += (_, _) => Commit();
    }

    public string Id { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public AiModelType ModelType { get => _modelType; set => Set(ref _modelType, value); }
    public ObservableCollection<string> ApiKeys { get; }
    public string ApiKey
    {
        get => ApiKeys.FirstOrDefault() ?? string.Empty;
        set
        {
            if (ApiKeys.Count == 0)
                ApiKeys.Add(value);
            else if (ApiKeys[0] != value)
                ApiKeys[0] = value;
            this.RaisePropertyChanged();
        }
    }
    public string ApiUrl { get => _apiUrl; set => Set(ref _apiUrl, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public bool UseProxy { get => _useProxy; set => Set(ref _useProxy, value); }
    public bool EnableThinking { get => _enableThinking; set => Set(ref _enableThinking, value); }
    public bool IsTesting { get => _isTesting; set => this.RaiseAndSetIfChanged(ref _isTesting, value); }

    public CustomAiModelSettings ToContract() => new(
        Id, Name, ModelType, ApiKeys.ToArray(), ApiUrl, Model, UseProxy, EnableThinking);
}

public sealed class LiveMachineTranslationSettings
{
    public LiveMachineTranslationSettings(
        MachineTranslationSettings value,
        Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
    {
        Baidu = new LiveBaiduSettings(value.Baidu, commit);
        Tencent = new LiveTencentSettings(value.Tencent, commit);
        Google = new LiveGoogleSettings(value.Google, commit);
        DeepL = new LiveDeepLSettings(value.DeepL, commit);
    }

    public LiveBaiduSettings Baidu { get; }
    public LiveTencentSettings Tencent { get; }
    public LiveGoogleSettings Google { get; }
    public LiveDeepLSettings DeepL { get; }

    public MachineTranslationSettings ToContract() => new(
        Baidu.ToContract(), Tencent.ToContract(), Google.ToContract(), DeepL.ToContract());
}

public sealed class BaiduCredentialState : LiveSettingsSection
{
    private string _appId;
    private string _appKey;

    public BaiduCredentialState(
        BaiduCredentialSettings value,
        Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.MachineTranslation, commit)
    {
        _appId = value.AppId;
        _appKey = value.AppKey;
    }

    public string AppId { get => _appId; set => Set(ref _appId, value); }
    public string AppKey { get => _appKey; set => Set(ref _appKey, value); }
    public BaiduCredentialSettings ToContract() => new(AppId, AppKey);
}

public sealed class TencentCredentialState : LiveSettingsSection
{
    private string _secretId;
    private string _secretKey;

    public TencentCredentialState(
        TencentCredentialSettings value,
        Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.MachineTranslation, commit)
    {
        _secretId = value.SecretId;
        _secretKey = value.SecretKey;
    }

    public string SecretId { get => _secretId; set => Set(ref _secretId, value); }
    public string SecretKey { get => _secretKey; set => Set(ref _secretKey, value); }
    public TencentCredentialSettings ToContract() => new(SecretId, SecretKey);
}

public sealed class LiveBaiduSettings : LiveSettingsSection
{
    private bool _useProxy;
    private string _id;

    public LiveBaiduSettings(BaiduTranslationSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.MachineTranslation, commit)
    {
        _useProxy = value.UseProxy;
        _id = value.Id;
        Items = new ObservableCollection<BaiduCredentialState>(
            value.Items.Select(item => new BaiduCredentialState(item, commit)));
        Items.CollectionChanged += (_, _) => Commit();
    }

    public bool UseProxy { get => _useProxy; set => Set(ref _useProxy, value); }
    public string Id { get => _id; set => Set(ref _id, value); }
    public ObservableCollection<BaiduCredentialState> Items { get; }
    public BaiduTranslationSettings ToContract() => new(UseProxy, Id, Items.Select(item => item.ToContract()).ToArray());
}

public sealed class LiveTencentSettings : LiveSettingsSection
{
    private bool _useProxy;
    private string _id;

    public LiveTencentSettings(TencentTranslationSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.MachineTranslation, commit)
    {
        _useProxy = value.UseProxy;
        _id = value.Id;
        Items = new ObservableCollection<TencentCredentialState>(
            value.Items.Select(item => new TencentCredentialState(item, commit)));
        Items.CollectionChanged += (_, _) => Commit();
    }

    public bool UseProxy { get => _useProxy; set => Set(ref _useProxy, value); }
    public string Id { get => _id; set => Set(ref _id, value); }
    public ObservableCollection<TencentCredentialState> Items { get; }
    public TencentTranslationSettings ToContract() => new(UseProxy, Id, Items.Select(item => item.ToContract()).ToArray());
}

public sealed class LiveGoogleSettings : LiveSettingsSection
{
    private bool _useProxy;
    private string _id;
    private string _model;

    public LiveGoogleSettings(GoogleTranslationSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.MachineTranslation, commit)
    {
        _useProxy = value.UseProxy;
        _id = value.Id;
        _model = value.Model;
        ApiKeys = new ObservableCollection<string>(value.ApiKeys);
        ApiKeys.CollectionChanged += (_, _) => Commit();
    }

    public bool UseProxy { get => _useProxy; set => Set(ref _useProxy, value); }
    public string Id { get => _id; set => Set(ref _id, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public ObservableCollection<string> ApiKeys { get; }
    public GoogleTranslationSettings ToContract() => new(UseProxy, Id, Model, ApiKeys.ToArray());
}

public sealed class LiveDeepLSettings : LiveSettingsSection
{
    private bool _useProxy;
    private string _id;
    private string _modelType;

    public LiveDeepLSettings(DeepLTranslationSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.MachineTranslation, commit)
    {
        _useProxy = value.UseProxy;
        _id = value.Id;
        _modelType = value.ModelType;
        ApiKeys = new ObservableCollection<string>(value.ApiKeys);
        ApiKeys.CollectionChanged += (_, _) => Commit();
    }

    public bool UseProxy { get => _useProxy; set => Set(ref _useProxy, value); }
    public string Id { get => _id; set => Set(ref _id, value); }
    public string ModelType { get => _modelType; set => Set(ref _modelType, value); }
    public ObservableCollection<string> ApiKeys { get; }
    public DeepLTranslationSettings ToContract() => new(UseProxy, Id, ModelType, ApiKeys.ToArray());
}

public sealed class LiveTtsSettings : LiveSettingsSection
{
    private string _provider;

    public LiveTtsSettings(TtsSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Tts, commit)
    {
        _provider = value.Provider;
        ProviderVoicePreferences = value.ProviderVoicePreferences.ToDictionary(
            provider => provider.Key,
            provider => NormalizeVoicePreferences(provider.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    public string Provider { get => _provider; set => Set(ref _provider, value); }
    public Dictionary<string, Dictionary<string, string>> ProviderVoicePreferences { get; }

    public string? GetVoiceForLanguage(string provider, string languageId) =>
        ProviderVoicePreferences.TryGetValue(provider, out var voices)
        && (voices.TryGetValue(languageId, out var voice)
            || voices.TryGetValue(GetPrimaryLanguage(languageId), out voice))
            ? voice
            : null;

    public void SetVoiceForLanguage(string provider, string languageId, string voiceId)
    {
        if (!ProviderVoicePreferences.TryGetValue(provider, out var voices))
            ProviderVoicePreferences[provider] = voices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        voices[NormalizeLanguageId(languageId)] = voiceId;
        Commit();
    }

    public void RemoveVoiceForLanguage(string provider, string languageId)
    {
        if (ProviderVoicePreferences.TryGetValue(provider, out var voices)
            && (voices.Remove(languageId) || voices.Remove(GetPrimaryLanguage(languageId))))
            Commit();
    }

    public TtsSettings ToContract() => new(
        Provider,
        ProviderVoicePreferences.ToDictionary(
            provider => provider.Key,
            provider => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(provider.Value),
            StringComparer.OrdinalIgnoreCase));

    private static Dictionary<string, string> NormalizeVoicePreferences(
        IReadOnlyDictionary<string, string> preferences) =>
        preferences
            .Where(preference => !string.IsNullOrWhiteSpace(preference.Value))
            .GroupBy(preference => NormalizeLanguageId(preference.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

    private static string NormalizeLanguageId(string languageId) =>
        languageId.Trim();

    private static string GetPrimaryLanguage(string languageId) =>
        languageId.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
}
