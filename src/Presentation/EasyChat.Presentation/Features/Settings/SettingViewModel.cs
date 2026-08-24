using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Reactive;
using Avalonia.Threading;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Shell;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Features.Speech;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.Translation;
using Material.Icons;
using ReactiveUI;
using ShadUI;
using ToastNotification = ShadUI.Notification;

namespace EasyChat.Presentation.Features.Settings;

public sealed record NetworkProxyModeOption(NetworkProxyMode Mode, string DisplayName);

public sealed class SettingViewModel : NavigationPageViewModel
{
    private static readonly Uri AsrModelDownloadsUri = new(
        "https://github.com/SwaggyMacro/MicroASR/releases/tag/models-v1");
    private readonly SettingsSession _settings;
    private readonly IApplicationDataUseCases _applicationData;
    private readonly IOcrModelUseCases _ocr;
    private readonly IImageTranslationModelUseCases _imageTranslationModels;
    private readonly ITranslationUseCases _translation;
    private readonly ITranslationLanguageCatalog _languages;
    private readonly ISpeechRecognitionModelCatalog _speechModels;
    private readonly ISpeechRecognitionModelDownloadUseCases _speechModelDownloads;
    private readonly ISpeechRecognitionModelInstaller _speechModelInstaller;
    private readonly ISpeechRecognitionModelRemover _speechModelRemover;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly ISettingsDialogCoordinator _dialogs;
    private readonly ToastManager _toasts;
    private readonly IApplicationRestartService? _restartService;
    private readonly IApplicationAutoStartService _autoStartService;
    private readonly ITsfInputTranslationUseCases? _tsf;
    private readonly Dictionary<OcrModelDownloadItemViewModel, CancellationTokenSource> _downloads = [];
    private readonly Dictionary<ImageTranslationModelDownloadItemViewModel, CancellationTokenSource> _imageTranslationDownloads = [];
    private readonly Dictionary<SpeechRecognitionModelDownloadItemViewModel, CancellationTokenSource> _asrDownloads = [];
    private bool _isOcrModelListExpanded;
    private bool _isAsrModelListExpanded;
    private bool _isTestingBaidu;
    private bool _isTestingTencent;
    private bool _isTestingGoogle;
    private bool _isTestingDeepL;
    private ObservableCollection<ModelCardItem> _modelCardsWithAddButton = [];
    private ObservableCollection<string> _availableFonts = [];
    private ObservableCollection<SpeechRecognitionModel> _asrModels = [];
    private bool _isImportingAsrModel;
    private bool _isChangingDataLocation;
    private string _searchText = string.Empty;
    private bool _isSearchOpen;
    private SettingsPaneId _activePane = SettingsPaneId.General;
    private SettingsNavItem? _selectedNavItem;
    private bool _isAutoStartEnabled;

    public SettingViewModel(
        SettingsSession settings,
        IApplicationDataUseCases applicationData,
        IOcrModelUseCases ocr,
        IImageTranslationModelUseCases imageTranslationModels,
        ITtsUseCases tts,
        ITranslationUseCases translation,
        ITranslationLanguageCatalog languages,
        ISpeechRecognitionModelCatalog speechModels,
        ISpeechRecognitionModelDownloadUseCases speechModelDownloads,
        ISpeechRecognitionModelInstaller speechModelInstaller,
        ISpeechRecognitionModelRemover speechModelRemover,
        IExternalUriLauncher uriLauncher,
        ISettingsDialogCoordinator dialogs,
        ToastManager toasts,
        IApplicationAutoStartService autoStartService,
        IApplicationRestartService? restartService = null,
        ITsfInputTranslationUseCases? tsf = null)
        : base(Resources.Settings, MaterialIconKind.Settings, 1)
    {
        _settings = settings;
        _applicationData = applicationData;
        _ocr = ocr;
        _imageTranslationModels = imageTranslationModels ?? throw new ArgumentNullException(nameof(imageTranslationModels));
        _translation = translation;
        _languages = languages;
        _speechModels = speechModels;
        _speechModelDownloads = speechModelDownloads;
        _speechModelInstaller = speechModelInstaller;
        _speechModelRemover = speechModelRemover;
        _uriLauncher = uriLauncher;
        _dialogs = dialogs;
        _toasts = toasts;
        _restartService = restartService;
        _tsf = tsf;
        _autoStartService = autoStartService ?? throw new ArgumentNullException(nameof(autoStartService));
        _isAutoStartEnabled = GetAutoStartEnabled();

        DisplayLanguages = BuildDisplayLanguages();
        NativeLanguages = BuildLanguages(includeAuto: false);
        OcrModelItems = new ObservableCollection<OcrModelDownloadItemViewModel>(
            _ocr.ModelPackages.Select(package => new OcrModelDownloadItemViewModel(
                package,
                GetOcrModelDisplayName(package.Id),
                GetOcrModelDescription(package.Id),
                string.Format(
                    Resources.OcrSupportedLanguages,
                    string.Join(", ", package.SupportedLanguages.Select(GetOcrLanguageDisplayName))),
                 _ocr.IsModelDownloaded(package))));
        ImageTranslationModelItems = new ObservableCollection<ImageTranslationModelDownloadItemViewModel>(
            _imageTranslationModels.ModelPackages.Select(package =>
                new ImageTranslationModelDownloadItemViewModel(
                    package,
                    GetImageTranslationModelDisplayName(package),
                    GetImageTranslationModelDescription(package),
                    _imageTranslationModels.IsModelDownloaded(package))));
        foreach (var item in ImageTranslationModelItems)
            item.PropertyChanged += (_, _) => RaiseImageTranslationModelStateChanged();
        AsrModelItems = new ObservableCollection<SpeechRecognitionModelDownloadItemViewModel>(
            _speechModelDownloads.ModelPackages.Select(package =>
                new SpeechRecognitionModelDownloadItemViewModel(package, isDownloaded: false)));

        RefreshModelCards();
        AiModelConf.ConfiguredModels.CollectionChanged += OnModelsChanged;

        TtsProviders = tts.GetProviders().Select(provider => provider.Id).ToList();
        AddModelCommand = ReactiveCommand.Create(() => _dialogs.EditAiModel(null));
        EditModelCommand = ReactiveCommand.Create<CustomAiModelState>(_dialogs.EditAiModel);
        DeleteModelCommand = ReactiveCommand.Create<CustomAiModelState>(_dialogs.DeleteAiModel);
        EditModelKeysCommand = ReactiveCommand.Create<CustomAiModelState>(_dialogs.EditAiModelKeys);
        EditBaiduKeysCommand = ReactiveCommand.Create(_dialogs.EditBaiduKeys);
        EditTencentKeysCommand = ReactiveCommand.Create(_dialogs.EditTencentKeys);
        EditGoogleKeysCommand = ReactiveCommand.Create(_dialogs.EditGoogleKeys);
        EditDeepLKeysCommand = ReactiveCommand.Create(_dialogs.EditDeepLKeys);
        ManageFixedAreasCommand = ReactiveCommand.Create(_dialogs.ManageFixedAreas);
        ConfigureTtsCommand = ReactiveCommand.Create(_dialogs.ConfigureTts);
        ApplySubtitleAppearancePresetCommand = ReactiveCommand.Create<SubtitleAppearancePreset>(ApplySubtitleAppearancePreset);
        OpenAsrModelDownloadsCommand = ReactiveCommand.Create(OpenAsrModelDownloads);
        DownloadAsrModelCommand = ReactiveCommand.Create<SpeechRecognitionModelDownloadItemViewModel>(StartDownloadAsrModel);
        CancelAsrModelCommand = ReactiveCommand.Create<SpeechRecognitionModelDownloadItemViewModel>(CancelAsrModel);
        DeleteAsrModelCommand = ReactiveCommand.Create<SpeechRecognitionModel>(ConfirmDeleteAsrModel);
        ManageSelectionAppListCommand = ReactiveCommand.Create(_dialogs.ManageSelectionApps);
        RetryTsfRegistrationCommand = ReactiveCommand.CreateFromTask(RetryTsfRegistrationAsync);
        OpenWindowsInputSettingsCommand = ReactiveCommand.Create(OpenWindowsInputSettings);

        TestAiModelConnectionCommand = ReactiveCommand.CreateFromTask<CustomAiModelState>(TestAiModelConnectionAsync);
        TestBaiduConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("Baidu"));
        TestTencentConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("Tencent"));
        TestGoogleConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("Google"));
        TestDeepLConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("DeepL"));
        ToggleAiModelProxyCommand = ReactiveCommand.Create<CustomAiModelState>(ToggleAiModelProxy);
        ToggleBaiduProxyCommand = ReactiveCommand.Create<LiveBaiduSettings>(ToggleBaiduProxy);
        ToggleTencentProxyCommand = ReactiveCommand.Create<LiveTencentSettings>(ToggleTencentProxy);
        ToggleGoogleProxyCommand = ReactiveCommand.Create<LiveGoogleSettings>(ToggleGoogleProxy);
        ToggleDeepLProxyCommand = ReactiveCommand.Create<LiveDeepLSettings>(ToggleDeepLProxy);

        DownloadOcrModelCommand = ReactiveCommand.Create<OcrModelDownloadItemViewModel>(StartDownloadOcrModel);
        CancelOcrModelCommand = ReactiveCommand.Create<OcrModelDownloadItemViewModel>(CancelOcrModel);
        DeleteOcrModelCommand = ReactiveCommand.Create<OcrModelDownloadItemViewModel>(DeleteOcrModel);
        DownloadImageTranslationModelCommand = ReactiveCommand.Create<ImageTranslationModelDownloadItemViewModel>(
            StartDownloadImageTranslationModel);
        CancelImageTranslationModelCommand = ReactiveCommand.Create<ImageTranslationModelDownloadItemViewModel>(
            CancelImageTranslationModel);
        DeleteImageTranslationModelCommand = ReactiveCommand.Create<ImageTranslationModelDownloadItemViewModel>(
            DeleteImageTranslationModel);
        ShowOcrModelLanguagesCommand = ReactiveCommand.Create<OcrModelDownloadItemViewModel>(item =>
            _dialogs.ShowInformation(item.DisplayName, item.SupportedLanguages));
        ToggleOcrModelListCommand = ReactiveCommand.Create(() =>
        {
            IsOcrModelListExpanded = !IsOcrModelListExpanded;
        });
        ToggleAsrModelListCommand = ReactiveCommand.Create(() =>
        {
            IsAsrModelListExpanded = !IsAsrModelListExpanded;
        });

        NavItems =
        [
            new(SettingsPaneId.General, Resources.General, MaterialIconKind.Cog, SettingsSearch.GeneralSearchFields),
            new(SettingsPaneId.Translation, Resources.Translation, MaterialIconKind.Translate, SettingsSearch.TranslationFields),
            new(SettingsPaneId.Selection, Resources.SelectionToolbarSettings, MaterialIconKind.CursorDefault, SettingsSearch.SelectionFields),
            new(SettingsPaneId.Speech, Resources.Speech, MaterialIconKind.VolumeHigh, SettingsSearch.SpeechFields),
            new(SettingsPaneId.Screenshot, Resources.Screenshot, MaterialIconKind.Monitor, SettingsSearch.ScreenshotFields),
            new(SettingsPaneId.Result, Resources.ResultSettings, MaterialIconKind.DockWindow, SettingsSearch.ResultFields),
            new(SettingsPaneId.Input, Resources.InputSettings, MaterialIconKind.Keyboard, SettingsSearch.InputFields)
        ];
        _selectedNavItem = NavItems[0];
        _selectedNavItem.IsSelected = true;
        SelectPaneCommand = ReactiveCommand.Create<SettingsPaneId>(OpenPane);
        OpenSearchCommand = ReactiveCommand.Create(() => { IsSearchOpen = true; });
        CloseSearchCommand = ReactiveCommand.Create(CloseSearch);

        Dispatcher.UIThread.Post(LoadAvailableFonts);
        Dispatcher.UIThread.Post(() => _ = LoadAsrModelsAsync());
    }

    /// <summary>Browse mode shows one pane; search mode shows all matches.</summary>
    public bool IsBrowseMode => string.IsNullOrWhiteSpace(SearchText);
    public bool IsSearchMode => !IsBrowseMode;
    /// <summary>Expanded search field on the title row (icon-only when collapsed).</summary>
    public bool IsSearchOpen
    {
        get => _isSearchOpen || !string.IsNullOrWhiteSpace(_searchText);
        private set
        {
            if (_isSearchOpen == value)
                return;
            this.RaiseAndSetIfChanged(ref _isSearchOpen, value);
            this.RaisePropertyChanged(nameof(IsSearchCollapsed));
        }
    }
    public bool IsSearchCollapsed => !IsSearchOpen;
    public IReadOnlyList<SettingsNavItem> NavItems { get; }
    public ReactiveCommand<SettingsPaneId, Unit> SelectPaneCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseSearchCommand { get; }

    public SettingsNavItem? SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (value is null || ReferenceEquals(_selectedNavItem, value))
                return;
            SetSelectedNav(value);
            OpenPane(value.Id);
        }
    }

    public SettingsPaneId ActivePane
    {
        get => _activePane;
        private set
        {
            if (_activePane == value)
                return;
            this.RaiseAndSetIfChanged(ref _activePane, value);
            RaiseSectionVisibility();
            var match = NavItems.FirstOrDefault(item => item.Id == value);
            if (match is not null)
                SetSelectedNav(match);
        }
    }

    public void OpenPane(SettingsPaneId pane) => ActivePane = pane;

    private void SetSelectedNav(SettingsNavItem next)
    {
        if (ReferenceEquals(_selectedNavItem, next))
        {
            next.IsSelected = true;
            return;
        }

        if (_selectedNavItem is not null)
            _selectedNavItem.IsSelected = false;
        _selectedNavItem = next;
        next.IsSelected = true;
        this.RaisePropertyChanged(nameof(SelectedNavItem));
    }

    /// <summary>Deep-link entry from shell navigation context.</summary>
    public void OpenPane(EasyChat.Presentation.Features.Shell.SettingsPane pane) =>
        OpenPane(pane switch
        {
            EasyChat.Presentation.Features.Shell.SettingsPane.Translation => SettingsPaneId.Translation,
            EasyChat.Presentation.Features.Shell.SettingsPane.Selection => SettingsPaneId.Selection,
            EasyChat.Presentation.Features.Shell.SettingsPane.Speech => SettingsPaneId.Speech,
            EasyChat.Presentation.Features.Shell.SettingsPane.Screenshot => SettingsPaneId.Screenshot,
            EasyChat.Presentation.Features.Shell.SettingsPane.Result => SettingsPaneId.Result,
            EasyChat.Presentation.Features.Shell.SettingsPane.Input => SettingsPaneId.Input,
            _ => SettingsPaneId.General
        });

    public List<string> DeepLModelTypes { get; } = ["quality_optimized", "prefer_quality_optimized", "latency_optimized"];
    public List<LanguageSettings> DisplayLanguages { get; }
    public List<LanguageSettings> NativeLanguages { get; }
    public List<ClosingBehavior> ClosingBehaviors { get; } = Enum.GetValues<ClosingBehavior>().ToList();
    public List<NetworkProxyModeOption> NetworkProxyModes { get; } =
    [
        new(NetworkProxyMode.System, Resources.SystemProxy),
        new(NetworkProxyMode.None, Resources.NoProxy),
        new(NetworkProxyMode.Custom, Resources.CustomProxy)
    ];
    public List<string> ScreenshotModes { get; } = ["Precise", "Quick"];
    public List<ImageTextEraseMode> ImageTextEraseModes { get; } = Enum.GetValues<ImageTextEraseMode>().ToList();
    public List<OcrRecognitionMode> OcrRecognitionModes { get; } = Enum.GetValues<OcrRecognitionMode>().ToList();
    public List<string> MachineTransProviders { get; } = ["Baidu", "Tencent", "Google", "DeepL"];
    public List<string> TranslationEngineTypes { get; } = [Resources.AIEngine, Resources.MachineTranslation];
    public IEnumerable<TranslationConfigurationOption> SelectionTranslationEngineOptions { get; } =
    [
        TranslationConfigurationOption.FollowGlobal(Resources.TextAssistFollowGlobal),
        new(TranslationEngineNames.AiModel, Resources.AIEngine, false, MaterialIconKind.Robot),
        new(TranslationEngineNames.MachineTrans, Resources.MachineTranslation, false, MaterialIconKind.Translate)
    ];
    public List<SelectionTriggerModeOption> SelectionTriggerModes { get; } =
    [
        new(SelectionTriggerMode.DoubleClick, Resources.SelectionTriggerModeDoubleClick),
        new(SelectionTriggerMode.DragSelection, Resources.SelectionTriggerModeDragSelection),
        new(SelectionTriggerMode.All, Resources.SelectionTriggerModeAll)
    ];
    public List<SelectionFilterModeOption> SelectionFilterModes { get; } =
    [
        new(SelectionFilterMode.Disabled, Resources.SelectionFilterDisabled),
        new(SelectionFilterMode.Blacklist, Resources.SelectionFilterBlacklist),
        new(SelectionFilterMode.Whitelist, Resources.SelectionFilterWhitelist)
    ];
    public IReadOnlyList<string> TransparencyLevels { get; } =
        EasyChat.Presentation.Foundation.Platform.WindowTransparencyLevels.Preferences;
    public List<InputDeliveryMode> InputDeliveryModes { get; } = Enum.GetValues<InputDeliveryMode>().ToList();
    public List<InputTranslationMode> InputTranslationModes { get; } = Enum.GetValues<InputTranslationMode>().ToList();
    public string TsfStatusText => FormatTsfStatus(_tsf?.Status);
    public string RetryTsfRegistrationText => IsChineseUi() ? "重试 TSF 注册" : "Retry TSF registration";
    public string OpenWindowsInputSettingsText => IsChineseUi() ? "打开 Windows 输入法设置" : "Open Windows input settings";
    public List<ResultWindowMode> ResultWindowModes { get; } = Enum.GetValues<ResultWindowMode>().ToList();
    public List<ResultReadAloudMode> ResultReadAloudModes { get; } = Enum.GetValues<ResultReadAloudMode>().ToList();
    public List<string> TtsProviders { get; }

    public LiveGeneralSettings GeneralConf => _settings.General;
    public LiveAiModelSettings AiModelConf => _settings.AiModel;
    public ObservableCollection<CustomAiModelState> ConfiguredModels => AiModelConf.ConfiguredModels;
    public LiveMachineTranslationSettings MachineTransConf => _settings.MachineTranslation;
    public LiveProxySettings NetworkProxyConf => _settings.Proxy;
    [Obsolete("Use NetworkProxyConf.")]
    public LiveProxySettings ProxyConf => _settings.Proxy;
    public LiveOcrSettings OcrConf => _settings.Ocr;
    public LiveResultSettings ResultConf => _settings.Result;
    public LiveInputSettings InputConf => _settings.Input;
    public LiveScreenshotSettings ScreenshotConf => _settings.Screenshot;
    public LiveSelectionTranslationSettings SelectionTranslationConf => _settings.SelectionTranslation;
    public ObservableCollection<PromptEntryState> PromptEntries => _settings.Prompts.Entries;
    public LiveTtsSettings TtsConf => _settings.Tts;
    public LiveSpeechRecognitionSettings SpeechRecognitionConf => _settings.SpeechRecognition;
    public IReadOnlyList<SubtitleAppearancePreset> SubtitleAppearancePresets { get; } =
        EasyChat.Presentation.Features.Speech.SubtitleAppearancePresets.All;
    public IReadOnlyList<KeyValuePair<FloatingDisplayMode, string>> SubtitleDisplayModeOptions { get; } =
    [
        new(FloatingDisplayMode.Segmented, Resources.Speech_DisplayMode_Segmented),
        new(FloatingDisplayMode.AutoScroll, Resources.Speech_DisplayMode_AutoScroll)
    ];
    public IReadOnlyList<KeyValuePair<SubtitleSource, string>> MainSubtitleSourceOptions { get; } =
    [
        new(SubtitleSource.Original, Resources.Subtitle_Source_Original),
        new(SubtitleSource.Translated, Resources.Subtitle_Source_Translated)
    ];
    public IReadOnlyList<KeyValuePair<SubtitleSource, string>> SecondarySubtitleSourceOptions { get; } =
    [
        new(SubtitleSource.None, Resources.Subtitle_Source_None),
        new(SubtitleSource.Original, Resources.Subtitle_Source_Original),
        new(SubtitleSource.Translated, Resources.Subtitle_Source_Translated)
    ];
    public IReadOnlyList<string> SubtitleWindowOrientationOptions { get; } = ["Horizontal", "Vertical"];
    public ObservableCollection<OcrModelDownloadItemViewModel> OcrModelItems { get; }
    public ObservableCollection<ImageTranslationModelDownloadItemViewModel> ImageTranslationModelItems { get; }
    public ObservableCollection<SpeechRecognitionModelDownloadItemViewModel> AsrModelItems { get; }
    public ObservableCollection<ModelCardItem> ModelCardsWithAddButton
    {
        get => _modelCardsWithAddButton;
        private set => this.RaiseAndSetIfChanged(ref _modelCardsWithAddButton, value);
    }
    public ObservableCollection<string> AvailableFonts
    {
        get => _availableFonts;
        private set => this.RaiseAndSetIfChanged(ref _availableFonts, value);
    }
    public ObservableCollection<SpeechRecognitionModel> AsrModels
    {
        get => _asrModels;
        private set
        {
            this.RaiseAndSetIfChanged(ref _asrModels, value);
            this.RaisePropertyChanged(nameof(VisibleAsrModels));
            this.RaisePropertyChanged(nameof(HasAsrModels));
            this.RaisePropertyChanged(nameof(HasNoAsrModels));
            this.RaisePropertyChanged(nameof(HasImportedAsrModels));
        }
    }
    public IEnumerable<SpeechRecognitionModel> VisibleAsrModels =>
        AsrModels.Where(model => !AsrModelItems.Any(item =>
            string.Equals(item.Id, model.Id, StringComparison.OrdinalIgnoreCase)));
    public bool HasAsrModels => AsrModels.Count > 0;
    public bool HasNoAsrModels => !HasAsrModels;
    public bool HasImportedAsrModels => VisibleAsrModels.Any();
    public IEnumerable<SpeechRecognitionModelDownloadItemViewModel> VisibleAsrModelItems =>
        IsAsrModelListExpanded ? AsrModelItems : AsrModelItems.Take(3);
    public bool IsAsrModelListExpanded
    {
        get => _isAsrModelListExpanded;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isAsrModelListExpanded, value);
            this.RaisePropertyChanged(nameof(VisibleAsrModelItems));
            this.RaisePropertyChanged(nameof(AsrModelListToggleIcon));
            this.RaisePropertyChanged(nameof(AsrModelListToggleText));
        }
    }
    public MaterialIconKind AsrModelListToggleIcon => IsAsrModelListExpanded
        ? MaterialIconKind.ExpandLess
        : MaterialIconKind.ExpandMore;
    public bool IsAsrModelListToggleVisible => AsrModelItems.Count > 3;
    public string AsrModelListToggleText => IsAsrModelListExpanded
        ? Resources.ShowLessAsrModels
        : Resources.ShowMoreAsrModels;
    public bool IsImportingAsrModel
    {
        get => _isImportingAsrModel;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isImportingAsrModel, value);
            this.RaisePropertyChanged(nameof(CanImportAsrModel));
            this.RaisePropertyChanged(nameof(CanDownloadAsrModel));
            this.RaisePropertyChanged(nameof(CanChangeDataLocation));
        }
    }
    public bool IsDownloadingAsrModel => _asrDownloads.Count > 0;
    public bool CanImportAsrModel => !IsImportingAsrModel && !IsDownloadingAsrModel;
    public bool CanDownloadAsrModel => !IsImportingAsrModel;
    public string ApplicationDataRoot => _applicationData.Current.RootDirectory;
    public bool IsChangingDataLocation
    {
        get => _isChangingDataLocation;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isChangingDataLocation, value);
            this.RaisePropertyChanged(nameof(CanChangeDataLocation));
        }
    }
    public bool CanChangeDataLocation =>
        !IsChangingDataLocation && !IsImportingAsrModel && !IsDownloadingAsrModel
        && _downloads.Count == 0 && _imageTranslationDownloads.Count == 0;
    public List<string> AiProviders => ConfiguredModels.Select(model => model.Name).ToList();

    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_searchText, next, StringComparison.Ordinal))
                return;
            this.RaiseAndSetIfChanged(ref _searchText, next);
            if (!string.IsNullOrWhiteSpace(next) && !_isSearchOpen)
            {
                _isSearchOpen = true;
                this.RaisePropertyChanged(nameof(IsSearchOpen));
                this.RaisePropertyChanged(nameof(IsSearchCollapsed));
            }

            this.RaisePropertyChanged(nameof(IsBrowseMode));
            this.RaisePropertyChanged(nameof(IsSearchMode));
            this.RaisePropertyChanged(nameof(IsSearchOpen));
            this.RaisePropertyChanged(nameof(IsSearchCollapsed));
            RaiseSectionVisibility();
        }
    }

    /// <summary>Collapse title-row search (also used by Escape in the view).</summary>
    public void CollapseSearch() => CloseSearch();

    private void CloseSearch()
    {
        SearchText = string.Empty;
        _isSearchOpen = false;
        this.RaisePropertyChanged(nameof(IsSearchOpen));
        this.RaisePropertyChanged(nameof(IsSearchCollapsed));
    }

    public bool ShowGeneralSection => IsSectionVisible(SettingsPaneId.General, Resources.General, SettingsSearch.GeneralSearchFields);
    // Translation providers/models are managed as a dedicated page and are not part
    // of the field-level settings search. Keep the page available in browse mode.
    public bool ShowTranslationSection => IsBrowseMode && ActivePane == SettingsPaneId.Translation;
    public bool ShowSelectionSection => IsSectionVisible(SettingsPaneId.Selection, Resources.SelectionToolbarSettings, SettingsSearch.SelectionFields);
    public bool ShowSpeechSection => IsSectionVisible(SettingsPaneId.Speech, Resources.Speech, SettingsSearch.SpeechFields);
    public bool ShowScreenshotSection => IsSectionVisible(SettingsPaneId.Screenshot, Resources.Screenshot, SettingsSearch.ScreenshotFields);
    public bool ShowResultSection => IsSectionVisible(SettingsPaneId.Result, Resources.ResultSettings, SettingsSearch.ResultFields);
    public bool ShowInputSection => IsSectionVisible(SettingsPaneId.Input, Resources.InputSettings, SettingsSearch.InputFields);
    public bool HasSearchResults =>
        ShowGeneralSection || ShowTranslationSection || ShowSelectionSection || ShowSpeechSection
        || ShowScreenshotSection || ShowResultSection || ShowInputSection;
    public bool ShowNoSearchResults => IsSearchMode && !HasSearchResults;

    private bool IsSectionVisible(SettingsPaneId pane, string header, string fieldKeywords)
    {
        if (IsBrowseMode)
            return ActivePane == pane;
        return SettingsSearch.MatchesAny(SearchText, header, fieldKeywords);
    }

    private void RaiseSectionVisibility()
    {
        this.RaisePropertyChanged(nameof(ShowGeneralSection));
        this.RaisePropertyChanged(nameof(ShowTranslationSection));
        this.RaisePropertyChanged(nameof(ShowSelectionSection));
        this.RaisePropertyChanged(nameof(ShowSpeechSection));
        this.RaisePropertyChanged(nameof(ShowScreenshotSection));
        this.RaisePropertyChanged(nameof(ShowResultSection));
        this.RaisePropertyChanged(nameof(ShowInputSection));
        this.RaisePropertyChanged(nameof(HasSearchResults));
        this.RaisePropertyChanged(nameof(ShowNoSearchResults));
    }

    public LanguageSettings SelectedDisplayLanguage
    {
        get => DisplayLanguages.FirstOrDefault(language => language.EnglishName == GeneralConf.DisplayLanguage)
               ?? DisplayLanguages[0];
        set
        {
            if (value.EnglishName == GeneralConf.DisplayLanguage)
                return;
            GeneralConf.DisplayLanguage = value.EnglishName;
            var culture = value.Id == "zh-Hans" ? new CultureInfo("zh-CN") : new CultureInfo("en-US");
            Resources.Culture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            this.RaisePropertyChanged();
            ShowToast(
                Resources.LanguageChanged,
                Resources.RestartToTakeEffect,
                ToastNotification.Success,
                Resources.Restart,
                RestartApplication);
        }
    }

    public LanguageSettings SelectedNativeLanguage
    {
        get => NativeLanguages.FirstOrDefault(language => language.Id == GeneralConf.NativeLanguage?.Id)
               ?? NativeLanguages.First();
        set
        {
            if (value.Id == GeneralConf.NativeLanguage?.Id)
                return;
            GeneralConf.NativeLanguage = value;
            this.RaisePropertyChanged();
        }
    }

    public ClosingBehavior SelectedClosingBehavior
    {
        get => GeneralConf.ClosingBehavior;
        set
        {
            GeneralConf.ClosingBehavior = value;
            this.RaisePropertyChanged();
        }
    }

    public bool IsAutoStartEnabled
    {
        get => _isAutoStartEnabled;
        set
        {
            if (_isAutoStartEnabled == value)
                return;

            var result = _autoStartService.SetEnabled(value);
            if (result.IsFailure)
            {
                ShowToast(Resources.AutoStart, result.Error.Message, ToastNotification.Error);
                return;
            }

            this.RaiseAndSetIfChanged(ref _isAutoStartEnabled, value);
        }
    }

    public string SelectedScreenshotMode
    {
        get => ScreenshotConf.Mode ?? "Precise";
        set
        {
            ScreenshotConf.Mode = value;
            this.RaisePropertyChanged();
        }
    }

    public ImageTextEraseMode SelectedImageTextEraseMode
    {
        get => ScreenshotConf.ImageTextEraseMode;
        set
        {
            if (ScreenshotConf.ImageTextEraseMode == value)
                return;
            ScreenshotConf.ImageTextEraseMode = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsPreciseImageTextEraseMode));
            this.RaisePropertyChanged(nameof(IsPreciseImageEraseModelRequired));
        }
    }

    public bool IsPreciseImageTextEraseMode => SelectedImageTextEraseMode == ImageTextEraseMode.Precise;

    public bool IsPreciseImageEraseModelRequired =>
        IsPreciseImageTextEraseMode && ImageTranslationModelItems.Any(item => !item.IsDownloaded);

    public string ImageTextEraseModeLabel =>
        Resources.ResourceManager.GetString("ImageTextEraseMode", Resources.Culture) ?? "Background erase mode";

    public string ImageTranslationSettingsLabel =>
        Resources.ResourceManager.GetString("ImageTranslationSettings", Resources.Culture) ?? "Image translation";

    public string ImageTranslationModelsLabel =>
        Resources.ResourceManager.GetString("ImageTranslationModels", Resources.Culture) ?? "Image translation models";

    public string ImageTranslationModelsDescription =>
        Resources.ResourceManager.GetString("ImageTranslationModelsDescription", Resources.Culture)
        ?? "AOT-GAN is used only to erase the background when replacing text during image translation.";

    public string ImageTranslationModelRequiredMessage =>
        Resources.ResourceManager.GetString("ImageTranslationModelRequired", Resources.Culture)
        ?? "Precise background removal for image translation text replacement requires the AOT-GAN model. Download it below or switch to normal mode.";

    public OcrRecognitionMode SelectedOcrRecognitionMode
    {
        get => ScreenshotConf.OcrMode;
        set
        {
            ScreenshotConf.OcrMode = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsIdleReleaseOcrMode));
        }
    }

    public bool IsIdleReleaseOcrMode =>
        SelectedOcrRecognitionMode == OcrRecognitionMode.IdleRelease;

    public SelectionTriggerMode SelectedSelectionTriggerMode
    {
        get => SelectionTranslationConf.TriggerMode;
        set
        {
            SelectionTranslationConf.TriggerMode = value;
            this.RaisePropertyChanged();
        }
    }

    public SelectionFilterMode SelectedSelectionFilterMode
    {
        get => SelectionTranslationConf.FilterMode;
        set
        {
            SelectionTranslationConf.FilterMode = value;
            this.RaisePropertyChanged();
        }
    }

    public string SelectedSelectionTranslationEngine
    {
        get => EffectiveSelectionTranslationEngine == TranslationEngineNames.AiModel
            ? Resources.AIEngine
            : Resources.MachineTranslation;
        set
        {
            if (SelectionTranslationConf.Provider == TranslationConfigurationOption.FollowGlobalId)
                return;
            SelectionTranslationConf.Provider = value == Resources.AIEngine
                ? TranslationEngineNames.AiModel
                : TranslationEngineNames.MachineTrans;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsAiTranslationSelected));
            this.RaisePropertyChanged(nameof(IsMachineTranslationSelected));
        }
    }

    public TranslationConfigurationOption SelectedSelectionTranslationEngineOption
    {
        get => SelectionTranslationConf.Provider == TranslationConfigurationOption.FollowGlobalId
            ? SelectionTranslationEngineOptions.First(option => option.IsGlobal)
            : SelectionTranslationEngineOptions.First(option => option.Id == SelectionTranslationConf.Provider);
        set
        {
            if (value.IsGlobal)
            {
                SelectionTranslationConf.Provider = TranslationConfigurationOption.FollowGlobalId;
                this.RaisePropertyChanged(nameof(SelectedSelectionTranslationEngine));
                this.RaisePropertyChanged(nameof(SelectedSelectionTranslationEngineOption));
                this.RaisePropertyChanged(nameof(IsAiTranslationSelected));
                this.RaisePropertyChanged(nameof(IsMachineTranslationSelected));
                return;
            }

            SelectionTranslationConf.Provider = value.Id;
            this.RaisePropertyChanged(nameof(SelectedSelectionTranslationEngine));
            this.RaisePropertyChanged(nameof(IsAiTranslationSelected));
            this.RaisePropertyChanged(nameof(IsMachineTranslationSelected));
        }
    }

    public bool IsAiTranslationSelected =>
        string.Equals(EffectiveSelectionTranslationEngine, TranslationEngineNames.AiModel, StringComparison.OrdinalIgnoreCase);
    public bool IsMachineTranslationSelected => !IsAiTranslationSelected;

    public string? SelectedSelectionTranslationAiModelId
    {
        get => SelectionTranslationConf.AiModelId == TranslationConfigurationOption.FollowGlobalId
            ? GeneralConf.UsingAiModelId
            : SelectionTranslationConf.AiModelId;
        set
        {
            SelectionTranslationConf.AiModelId = value;
        }
    }

    public IEnumerable<TranslationConfigurationOption> SelectionTranslationAiModelOptions =>
        new[] { TranslationConfigurationOption.FollowGlobal(Resources.TextAssistFollowGlobal) }
            .Concat(ConfiguredModels.Select(model =>
                new TranslationConfigurationOption(model.Id, model.Name, false, MaterialIconKind.Robot)
                {
                    ImageValue = model.ModelType
                }));

    public TranslationConfigurationOption SelectedSelectionTranslationAiModelOption
    {
        get => SelectionTranslationConf.AiModelId == TranslationConfigurationOption.FollowGlobalId
            ? SelectionTranslationAiModelOptions.First(option => option.IsGlobal)
            : SelectionTranslationAiModelOptions.FirstOrDefault(option => option.Id == SelectionTranslationConf.AiModelId)
              ?? SelectionTranslationAiModelOptions.First(option => option.IsGlobal);
        set
        {
            if (value.IsGlobal)
            {
                SelectionTranslationConf.AiModelId = TranslationConfigurationOption.FollowGlobalId;
                this.RaisePropertyChanged(nameof(SelectedSelectionTranslationAiModelId));
                return;
            }

            SelectionTranslationConf.AiModelId = value.Id;
            this.RaisePropertyChanged(nameof(SelectedSelectionTranslationAiModelId));
        }
    }

    public string? SelectedSelectionTranslationPromptId
    {
        get => SelectionTranslationConf.PromptId == TranslationConfigurationOption.FollowGlobalId
            ? _settings.Prompts.SelectedPromptId
            : SelectionTranslationConf.PromptId;
        set
        {
            SelectionTranslationConf.PromptId = value;
        }
    }

    public IEnumerable<TranslationConfigurationOption> SelectionTranslationPromptOptions =>
        new[] { TranslationConfigurationOption.FollowGlobal(Resources.TextAssistFollowGlobal) }
            .Concat(PromptEntries.Select(prompt =>
                new TranslationConfigurationOption(prompt.Id, prompt.Name, false, MaterialIconKind.TextBox)));

    public TranslationConfigurationOption SelectedSelectionTranslationPromptOption
    {
        get => SelectionTranslationConf.PromptId == TranslationConfigurationOption.FollowGlobalId
            ? SelectionTranslationPromptOptions.First(option => option.IsGlobal)
            : SelectionTranslationPromptOptions.FirstOrDefault(option => option.Id == SelectionTranslationConf.PromptId)
              ?? SelectionTranslationPromptOptions.First(option => option.IsGlobal);
        set
        {
            if (value.IsGlobal)
            {
                SelectionTranslationConf.PromptId = TranslationConfigurationOption.FollowGlobalId;
                this.RaisePropertyChanged(nameof(SelectedSelectionTranslationPromptId));
                return;
            }

            SelectionTranslationConf.PromptId = value.Id;
            this.RaisePropertyChanged(nameof(SelectedSelectionTranslationPromptId));
        }
    }

    public string SelectedMachineTranslationProvider
    {
        get => SelectionTranslationConf.MachineProvider == TranslationConfigurationOption.FollowGlobalId
            ? GeneralConf.UsingMachineTransId ?? GeneralConf.UsingMachineTrans ?? "Baidu"
            : SelectionTranslationConf.MachineProvider ?? "Baidu";
        set
        {
            SelectionTranslationConf.MachineProvider = value;
            this.RaisePropertyChanged();
        }
    }

    public IEnumerable<TranslationConfigurationOption> SelectionTranslationMachineProviderOptions =>
        new[] { TranslationConfigurationOption.FollowGlobal(Resources.TextAssistFollowGlobal) }
            .Concat(MachineTransProviders.Select(provider =>
                new TranslationConfigurationOption(provider, provider, false, MaterialIconKind.Translate)
                {
                    ImageValue = provider
                }));

    public TranslationConfigurationOption SelectedMachineTranslationOption
    {
        get => SelectionTranslationConf.MachineProvider == TranslationConfigurationOption.FollowGlobalId
            ? SelectionTranslationMachineProviderOptions.First(option => option.IsGlobal)
            : SelectionTranslationMachineProviderOptions.FirstOrDefault(option => option.Id == SelectionTranslationConf.MachineProvider)
              ?? SelectionTranslationMachineProviderOptions.First(option => option.IsGlobal);
        set
        {
            if (value.IsGlobal)
            {
                SelectionTranslationConf.MachineProvider = TranslationConfigurationOption.FollowGlobalId;
                this.RaisePropertyChanged(nameof(SelectedMachineTranslationProvider));
                return;
            }

            SelectionTranslationConf.MachineProvider = value.Id;
            this.RaisePropertyChanged(nameof(SelectedMachineTranslationProvider));
        }
    }

    private string EffectiveSelectionTranslationEngine =>
        SelectionTranslationConf.Provider == TranslationConfigurationOption.FollowGlobalId
            ? GeneralConf.TransEngine ?? TranslationEngineNames.AiModel
            : SelectionTranslationConf.Provider;

    public string SelectedTtsProvider
    {
        get => TtsConf.Provider;
        set
        {
            TtsConf.Provider = value;
            this.RaisePropertyChanged();
        }
    }

    public IEnumerable<OcrModelDownloadItemViewModel> VisibleOcrModelItems =>
        IsOcrModelListExpanded ? OcrModelItems : OcrModelItems.Take(3);
    public bool IsOcrModelListExpanded
    {
        get => _isOcrModelListExpanded;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isOcrModelListExpanded, value);
            this.RaisePropertyChanged(nameof(VisibleOcrModelItems));
            this.RaisePropertyChanged(nameof(OcrModelListToggleIcon));
            this.RaisePropertyChanged(nameof(OcrModelListToggleText));
        }
    }
    public MaterialIconKind OcrModelListToggleIcon => IsOcrModelListExpanded
        ? MaterialIconKind.ExpandLess
        : MaterialIconKind.ExpandMore;
    public bool IsOcrModelListToggleVisible => OcrModelItems.Count > 3;
    public string OcrModelListToggleText => IsOcrModelListExpanded
        ? Resources.ShowLessOcrModels
        : Resources.ShowMoreOcrModels;

    public bool IsTestingBaidu { get => _isTestingBaidu; private set => this.RaiseAndSetIfChanged(ref _isTestingBaidu, value); }
    public bool IsTestingTencent { get => _isTestingTencent; private set => this.RaiseAndSetIfChanged(ref _isTestingTencent, value); }
    public bool IsTestingGoogle { get => _isTestingGoogle; private set => this.RaiseAndSetIfChanged(ref _isTestingGoogle, value); }
    public bool IsTestingDeepL { get => _isTestingDeepL; private set => this.RaiseAndSetIfChanged(ref _isTestingDeepL, value); }

    public ReactiveCommand<Unit, Unit> ManageFixedAreasCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfigureTtsCommand { get; }
    public ReactiveCommand<SubtitleAppearancePreset, Unit> ApplySubtitleAppearancePresetCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenAsrModelDownloadsCommand { get; }
    public ReactiveCommand<SpeechRecognitionModelDownloadItemViewModel, Unit> DownloadAsrModelCommand { get; }
    public ReactiveCommand<SpeechRecognitionModelDownloadItemViewModel, Unit> CancelAsrModelCommand { get; }
    public ReactiveCommand<SpeechRecognitionModel, Unit> DeleteAsrModelCommand { get; }
    public ReactiveCommand<Unit, Unit> ManageSelectionAppListCommand { get; }
    public ReactiveCommand<Unit, Unit> RetryTsfRegistrationCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenWindowsInputSettingsCommand { get; }
    public ReactiveCommand<OcrModelDownloadItemViewModel, Unit> DownloadOcrModelCommand { get; }
    public ReactiveCommand<OcrModelDownloadItemViewModel, Unit> CancelOcrModelCommand { get; }
    public ReactiveCommand<OcrModelDownloadItemViewModel, Unit> DeleteOcrModelCommand { get; }
    public ReactiveCommand<OcrModelDownloadItemViewModel, Unit> ShowOcrModelLanguagesCommand { get; }
    public ReactiveCommand<ImageTranslationModelDownloadItemViewModel, Unit> DownloadImageTranslationModelCommand { get; }
    public ReactiveCommand<ImageTranslationModelDownloadItemViewModel, Unit> CancelImageTranslationModelCommand { get; }
    public ReactiveCommand<ImageTranslationModelDownloadItemViewModel, Unit> DeleteImageTranslationModelCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOcrModelListCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleAsrModelListCommand { get; }
    public ReactiveCommand<Unit, Unit> AddModelCommand { get; }
    public ReactiveCommand<CustomAiModelState, Unit> EditModelCommand { get; }
    public ReactiveCommand<CustomAiModelState, Unit> DeleteModelCommand { get; }
    public ReactiveCommand<CustomAiModelState, Unit> EditModelKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditBaiduKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditTencentKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditGoogleKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditDeepLKeysCommand { get; }
    public ReactiveCommand<CustomAiModelState, Unit> TestAiModelConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestBaiduConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestTencentConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestGoogleConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestDeepLConnectionCommand { get; }
    public ReactiveCommand<CustomAiModelState, Unit> ToggleAiModelProxyCommand { get; }
    public ReactiveCommand<LiveBaiduSettings, Unit> ToggleBaiduProxyCommand { get; }
    public ReactiveCommand<LiveTencentSettings, Unit> ToggleTencentProxyCommand { get; }
    public ReactiveCommand<LiveGoogleSettings, Unit> ToggleGoogleProxyCommand { get; }
    public ReactiveCommand<LiveDeepLSettings, Unit> ToggleDeepLProxyCommand { get; }

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshModelCards();
        this.RaisePropertyChanged(nameof(AiProviders));
        this.RaisePropertyChanged(nameof(SelectionTranslationAiModelOptions));
        this.RaisePropertyChanged(nameof(SelectedSelectionTranslationAiModelOption));
    }

    private void RefreshModelCards()
    {
        var cards = ConfiguredModels.Select(model => new ModelCardItem(model)).ToList();
        cards.Add(new ModelCardItem(null));
        ModelCardsWithAddButton = new ObservableCollection<ModelCardItem>(cards);
    }

    private void ApplySubtitleAppearancePreset(SubtitleAppearancePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        SpeechRecognitionConf.PrimaryFontSize = preset.PrimaryFontSize;
        SpeechRecognitionConf.PrimaryFontColor = preset.PrimaryFontColor;
        SpeechRecognitionConf.SecondaryFontSize = preset.SecondaryFontSize;
        SpeechRecognitionConf.SecondaryFontColor = preset.SecondaryFontColor;
        SpeechRecognitionConf.BackgroundColor = preset.BackgroundColor;
        SpeechRecognitionConf.SubtitleBackgroundColor = preset.SubtitleBackgroundColor;
        SpeechRecognitionConf.WindowOpacity = preset.WindowOpacity;
    }

    private void LoadAvailableFonts()
    {
        AvailableFonts = new ObservableCollection<string>(
            Avalonia.Media.FontManager.Current.SystemFonts
                .Select(font => font.Name)
                .Order(StringComparer.CurrentCulture));
    }

    public async Task ImportAsrModelsAsync(
        IReadOnlyList<string> sourcePaths,
        SpeechRecognitionModelImportSourceKind sourceKind)
    {
        if (IsImportingAsrModel)
            return;

        IsImportingAsrModel = true;
        try
        {
            var result = await _speechModelInstaller.ImportAsync(
                new SpeechRecognitionModelImportRequest(sourcePaths, sourceKind));
            await RefreshAsrModelsAsync();

            var messages = new List<string>();
            if (result.ImportedModels.Count > 0)
                messages.Add(string.Format(
                    Resources.AsrModelsImported,
                    string.Join(", ", result.ImportedModels.Select(model => model.Id))));
            if (result.SkippedModels.Count > 0)
                messages.Add(string.Format(
                    Resources.AsrModelsSkipped,
                    string.Join(", ", result.SkippedModels.Select(model => model.Id))));

            ShowToast(
                Resources.AsrModels,
                string.Join(Environment.NewLine, messages),
                result.SkippedModels.Count > 0
                    ? ToastNotification.Info
                    : ToastNotification.Success);
        }
        catch (Exception exception)
        {
            ShowToast(Resources.AsrModelImportFailed, exception.Message, ToastNotification.Error);
        }
        finally
        {
            IsImportingAsrModel = false;
        }
    }

    private void ConfirmDeleteAsrModel(SpeechRecognitionModel model) =>
        _dialogs.ConfirmDeleteAsrModel(model, () => _ = DeleteAsrModelAsync(model));

    public async Task ChangeApplicationDataLocationAsync(string rootDirectory)
    {
        if (!CanChangeDataLocation)
        {
            ShowToast(Resources.ApplicationData, Resources.ApplicationDataMoveBusy, ToastNotification.Info);
            return;
        }

        IsChangingDataLocation = true;
        try
        {
            var result = await _applicationData.ChangeLocationAsync(rootDirectory);
            if (result.IsFailure)
            {
                ShowToast(Resources.ApplicationDataMoveFailed, result.Error.Message, ToastNotification.Error);
                return;
            }

            this.RaisePropertyChanged(nameof(ApplicationDataRoot));
            await RefreshAsrModelsAsync();
            ShowToast(
                Resources.ApplicationData,
                string.Format(Resources.ApplicationDataMoved, result.Value.RootDirectory),
                ToastNotification.Success);
        }
        catch (Exception exception)
        {
            ShowToast(Resources.ApplicationDataMoveFailed, exception.Message, ToastNotification.Error);
        }
        finally
        {
            IsChangingDataLocation = false;
        }
    }

    private async Task DeleteAsrModelAsync(SpeechRecognitionModel model)
    {
        if (IsImportingAsrModel)
            return;

        IsImportingAsrModel = true;
        try
        {
            if (await _speechModelRemover.DeleteAsync(model.Id))
            {
                await RefreshAsrModelsAsync();
                ShowToast(
                    Resources.AsrModels,
                    string.Format(Resources.AsrModelDeleted, model.Id),
                    ToastNotification.Success);
            }
        }
        catch (Exception exception)
        {
            ShowToast(Resources.AsrModelDeleteFailed, exception.Message, ToastNotification.Error);
        }
        finally
        {
            IsImportingAsrModel = false;
        }
    }

    private async Task RefreshAsrModelsAsync()
    {
        var models = await _speechModels.GetModelsAsync();
        AsrModels = new ObservableCollection<SpeechRecognitionModel>(models);
        var installedIds = models
            .Select(model => model.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in AsrModelItems)
            item.SyncDownloaded(installedIds.Contains(item.Id));
    }

    private async Task LoadAsrModelsAsync()
    {
        try
        {
            await RefreshAsrModelsAsync();
        }
        catch (Exception exception)
        {
            ShowToast(Resources.AsrModels, exception.Message, ToastNotification.Error);
        }
    }

    private void OpenAsrModelDownloads()
    {
        var result = _uriLauncher.Open(AsrModelDownloadsUri);
        if (result.IsFailure)
            ShowToast(Resources.AsrModels, result.Error.Message, ToastNotification.Error);
    }

    private void StartDownloadAsrModel(SpeechRecognitionModelDownloadItemViewModel item) =>
        _ = DownloadAsrModelAsync(item);

    private async Task DownloadAsrModelAsync(SpeechRecognitionModelDownloadItemViewModel item)
    {
        if (!CanDownloadAsrModel || item.IsDownloading || item.IsDownloaded || _asrDownloads.ContainsKey(item))
            return;

        var cancellation = new CancellationTokenSource();
        _asrDownloads.Add(item, cancellation);
        RaiseAsrDownloadStateChanged();
        item.StartDownload();
        try
        {
            await _speechModelDownloads.DownloadModelAsync(
                item.Package,
                new Progress<double>(item.SetProgress),
                cancellation.Token);
            await RefreshAsrModelsAsync();
            item.CompleteDownload();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            item.CancelDownload();
        }
        catch (Exception exception)
        {
            item.FailDownload(GetImageTranslationModelDownloadError(exception));
        }
        finally
        {
            _asrDownloads.Remove(item);
            RaiseAsrDownloadStateChanged();
            cancellation.Dispose();
        }
    }

    private void CancelAsrModel(SpeechRecognitionModelDownloadItemViewModel item)
    {
        if (_asrDownloads.TryGetValue(item, out var cancellation))
            cancellation.Cancel();
    }

    private void RaiseAsrDownloadStateChanged()
    {
        this.RaisePropertyChanged(nameof(IsDownloadingAsrModel));
        this.RaisePropertyChanged(nameof(CanImportAsrModel));
        this.RaisePropertyChanged(nameof(CanDownloadAsrModel));
        this.RaisePropertyChanged(nameof(CanChangeDataLocation));
    }

    private void StartDownloadOcrModel(OcrModelDownloadItemViewModel item) => _ = DownloadOcrModelAsync(item);

    private async Task DownloadOcrModelAsync(OcrModelDownloadItemViewModel item)
    {
        if (item.IsDownloading || item.IsDownloaded || _downloads.ContainsKey(item))
            return;

        var cancellation = new CancellationTokenSource();
        _downloads.Add(item, cancellation);
        this.RaisePropertyChanged(nameof(CanChangeDataLocation));
        item.StartDownload();
        try
        {
            await _ocr.DownloadModelAsync(item.Package, new Progress<double>(item.SetProgress), cancellation.Token);
            item.CompleteDownload();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            item.CancelDownload();
        }
        catch (Exception exception)
        {
            item.FailDownload(exception.Message);
        }
        finally
        {
            _downloads.Remove(item);
            this.RaisePropertyChanged(nameof(CanChangeDataLocation));
            cancellation.Dispose();
        }
    }

    private void CancelOcrModel(OcrModelDownloadItemViewModel item)
    {
        if (_downloads.TryGetValue(item, out var cancellation))
            cancellation.Cancel();
    }

    private void DeleteOcrModel(OcrModelDownloadItemViewModel item)
    {
        try
        {
            _ocr.DeleteModel(item.Package);
            item.MarkDeleted();
        }
        catch (Exception exception)
        {
            item.FailDownload(exception.Message);
        }
    }

    private void StartDownloadImageTranslationModel(ImageTranslationModelDownloadItemViewModel item) =>
        _ = DownloadImageTranslationModelAsync(item);

    private async Task DownloadImageTranslationModelAsync(ImageTranslationModelDownloadItemViewModel item)
    {
        if (item.IsDownloading || item.IsDownloaded || _imageTranslationDownloads.ContainsKey(item))
            return;

        var cancellation = new CancellationTokenSource();
        _imageTranslationDownloads.Add(item, cancellation);
        RaiseImageTranslationModelStateChanged();
        item.StartDownload();
        try
        {
            await _imageTranslationModels.DownloadModelAsync(
                item.Package,
                new Progress<double>(item.SetProgress),
                cancellation.Token);
            if (!_imageTranslationModels.IsModelDownloaded(item.Package))
                throw new InvalidDataException("The image translation model failed integrity validation after download.");
            item.CompleteDownload();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            item.CancelDownload();
        }
        catch (Exception exception)
        {
            item.FailDownload(string.Format(
                GetImageTranslationModelResource(
                    "ImageTranslationModelDownloadError",
                    "Image translation model download failed: {0}"),
                exception.Message));
        }
        finally
        {
            _imageTranslationDownloads.Remove(item);
            RaiseImageTranslationModelStateChanged();
            cancellation.Dispose();
        }
    }

    private void CancelImageTranslationModel(ImageTranslationModelDownloadItemViewModel item)
    {
        if (_imageTranslationDownloads.TryGetValue(item, out var cancellation))
            cancellation.Cancel();
    }

    private void DeleteImageTranslationModel(ImageTranslationModelDownloadItemViewModel item)
    {
        try
        {
            _imageTranslationModels.DeleteModel(item.Package);
            item.MarkDeleted();
        }
        catch (Exception exception)
        {
            item.FailDownload(exception.Message);
        }
    }

    private void RaiseImageTranslationModelStateChanged()
    {
        this.RaisePropertyChanged(nameof(IsPreciseImageEraseModelRequired));
        this.RaisePropertyChanged(nameof(CanChangeDataLocation));
    }

    private static string GetImageTranslationModelDisplayName(ImageTranslationModelPackage package) =>
        string.Equals(package.Id, "aotgan-onnx", StringComparison.Ordinal)
            ? GetImageTranslationModelResource("ImageTranslationModelAotGanName", "AOT-GAN")
            : package.DisplayName;

    private static string GetImageTranslationModelDescription(ImageTranslationModelPackage package) =>
        string.Equals(package.Id, "aotgan-onnx", StringComparison.Ordinal)
            ? GetImageTranslationModelResource(
                "ImageTranslationModelAotGanDescription",
                "Background removal model for text replacement during image translation (61 MB).")
            : package.Description;

    private static string GetImageTranslationModelResource(string key, string fallback) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;

    private static string GetImageTranslationModelDownloadError(Exception exception) => exception switch
    {
        HttpRequestException => GetImageTranslationModelResource(
            "ImageTranslationModelNetworkError",
            "The image translation model could not be downloaded. Check your network and proxy settings."),
        TimeoutException => GetImageTranslationModelResource(
            "ImageTranslationModelNetworkError",
            "The image translation model could not be downloaded. Check your network and proxy settings."),
        IOException => GetImageTranslationModelResource(
            "ImageTranslationModelStorageError",
            "The image translation model could not be installed. Close any program using the model file and retry."),
        InvalidDataException => GetImageTranslationModelResource(
            "ImageTranslationModelIntegrityError",
            "The downloaded image translation model failed integrity verification. Please retry."),
        _ => GetImageTranslationModelResource(
            "ImageTranslationModelUnknownError",
            "The image translation model download failed. Please retry.")
    };

    private string GetOcrLanguageDisplayName(OcrLanguage language)
    {
        var translationLanguage = _languages.All.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, language.Id, StringComparison.Ordinal));
        return translationLanguage is null
            ? language.DisplayName
            : LanguageDisplayNames.ForUi(
                translationLanguage.NativeName,
                translationLanguage.EnglishName);
    }

    private static string GetOcrModelDisplayName(string packageId) => packageId switch
    {
        "universal-v6-small" => Resources.OcrUniversalModel,
        "korean-v4" => Resources.OcrKoreanV4Model,
        "arabic-v4" => Resources.OcrArabicV4Model,
        "devanagari-v4" => Resources.OcrDevanagariV4Model,
        "tamil-v4" => Resources.OcrTamilV4Model,
        "telugu-v4" => Resources.OcrTeluguV4Model,
        "kannada-v4" => Resources.OcrKannadaV4Model,
        "cyrillic-v3" => Resources.OcrCyrillicV3Model,
        _ => packageId
    };

    private static string GetOcrModelDescription(string packageId) =>
        packageId == "universal-v6-small"
            ? Resources.OcrUniversalModelDescription
            : string.Empty;

    private Task TestAiModelConnectionAsync(CustomAiModelState model) => TestConnectionAsync(
        model.Name,
        new TranslationProviderSelection(TranslationEngineNames.AiModel, AiModelId: model.Id),
        testing => model.IsTesting = testing);

    private Task TestMachineConnectionAsync(string provider)
    {
        Action<bool> state = provider switch
        {
            "Baidu" => value => IsTestingBaidu = value,
            "Tencent" => value => IsTestingTencent = value,
            "Google" => value => IsTestingGoogle = value,
            _ => value => IsTestingDeepL = value
        };
        return TestConnectionAsync(
            provider,
            new TranslationProviderSelection(TranslationEngineNames.MachineTrans, MachineProviderName: provider),
            state);
    }

    private async Task TestConnectionAsync(
        string providerName,
        TranslationProviderSelection provider,
        Action<bool> setTesting)
    {
        setTesting(true);
        try
        {
            var result = await _translation.TranslateAsync(new TranslationRequest(
                "Hello",
                _languages.Get("en"),
                _languages.Get("zh-Hans"),
                Provider: provider));
            if (result.IsSuccess)
                ShowToast(providerName, Resources.ConnectionSuccess, ToastNotification.Success);
            else
                ShowToast(Resources.ConnectionFailed, $"{providerName}: {result.Error.Message}", ToastNotification.Error);
        }
        catch (Exception exception)
        {
            ShowToast(Resources.ConnectionFailed, $"{providerName}: {exception.Message}", ToastNotification.Error);
        }
        finally
        {
            setTesting(false);
        }
    }

    private void ToggleAiModelProxy(CustomAiModelState model) => ValidateProviderProxy(
        () => model.UseProxy,
        value => model.UseProxy = value);

    private void ToggleBaiduProxy(LiveBaiduSettings provider) => ValidateProviderProxy(
        () => provider.UseProxy,
        value => provider.UseProxy = value);

    private void ToggleTencentProxy(LiveTencentSettings provider) => ValidateProviderProxy(
        () => provider.UseProxy,
        value => provider.UseProxy = value);

    private void ToggleGoogleProxy(LiveGoogleSettings provider) => ValidateProviderProxy(
        () => provider.UseProxy,
        value => provider.UseProxy = value);

    private void ToggleDeepLProxy(LiveDeepLSettings provider) => ValidateProviderProxy(
        () => provider.UseProxy,
        value => provider.UseProxy = value);

    private void ValidateProviderProxy(Func<bool> isEnabled, Action<bool> setEnabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!isEnabled() || HasConfiguredNetworkProxy())
                return;

            setEnabled(false);
            ShowToast(Resources.NetworkProxy, Resources.NetworkProxyRequired, ToastNotification.Warning);
        }, DispatcherPriority.Background);
    }

    private bool HasConfiguredNetworkProxy() => NetworkProxyConf.Mode switch
    {
        NetworkProxyMode.System => true,
        NetworkProxyMode.Custom => Uri.TryCreate(NetworkProxyConf.ProxyUrl, UriKind.Absolute, out _),
        _ => false
    };

    private List<LanguageSettings> BuildDisplayLanguages() => BuildLanguages(includeAuto: false)
        .Where(language => language.Id is "en" or "zh-Hans")
        .ToList();

    private List<LanguageSettings> BuildLanguages(bool includeAuto)
    {
        var existing = new[] { GeneralConf.SourceLanguage, GeneralConf.TargetLanguage, GeneralConf.NativeLanguage }
            .Where(language => language is not null)
            .Cast<LanguageSettings>();
        return existing.Concat(_languages.All.Select(ToSettingsLanguage))
            .Where(language => includeAuto || language.Id != "auto")
            .DistinctBy(language => language.Id)
            .OrderBy(language => language.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static LanguageSettings ToSettingsLanguage(TranslationLanguage language)
    {
        var localized = language.NativeName ?? language.EnglishName;
        var display = language.NativeName is { Length: > 0 } && language.NativeName != language.EnglishName
            ? $"{language.NativeName} ({language.EnglishName})"
            : language.EnglishName;
        return new LanguageSettings(
            language.Id,
            localized,
            language.EnglishName,
            language.Icon ?? "unknown.png",
            localized,
            display,
            language.ProviderCodes ?? new Dictionary<string, string>());
    }

    private void ShowToast(
        string title,
        string content,
        ToastNotification severity,
        string? actionLabel = null,
        Action? action = null)
    {
        var toast = _toasts.CreateToast(title).WithContent(content);
        if (!string.IsNullOrWhiteSpace(actionLabel) && action is not null)
            toast.WithAction(actionLabel, action);
        switch (severity)
        {
            case ToastNotification.Success:
                toast.ShowSuccess();
                break;
            case ToastNotification.Warning:
                toast.ShowWarning();
                break;
            case ToastNotification.Error:
                toast.ShowError();
                break;
            default:
                toast.ShowInfo();
                break;
        }
    }

    private async Task RetryTsfRegistrationAsync()
    {
        if (_tsf is null)
            return;
        var result = await _tsf.StartAsync().ConfigureAwait(true);
        this.RaisePropertyChanged(nameof(TsfStatusText));
        ShowToast(
            IsChineseUi() ? "TSF 状态" : "TSF status",
            result.IsSuccess ? TsfStatusText : result.Error.Message,
            result.IsSuccess ? ToastNotification.Success : ToastNotification.Warning);
    }

    private void OpenWindowsInputSettings()
    {
        var result = _uriLauncher.Open(new Uri("ms-settings:regionlanguage"));
        if (result.IsFailure)
            ShowToast(OpenWindowsInputSettingsText, result.Error.Message, ToastNotification.Warning);
    }

    private static bool IsChineseUi() =>
        string.Equals(Resources.Culture?.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);

    private static string FormatTsfStatus(TextServicesFrameworkStatus? status)
    {
        if (status is null)
            return IsChineseUi() ? "TSF 不可用（非 Windows 主机）" : "TSF unavailable on this host";
        var label = status.State switch
        {
            TextServicesFrameworkState.Available => IsChineseUi() ? "已连接" : "Connected",
            TextServicesFrameworkState.RegistrationFailed => IsChineseUi() ? "注册失败" : "Registration failed",
            TextServicesFrameworkState.PipeUnavailable => IsChineseUi() ? "Pipe 不可用" : "Pipe unavailable",
            TextServicesFrameworkState.NotActive => IsChineseUi() ? "已注册，尚未激活" : "Registered, not active",
            _ => IsChineseUi() ? "不支持" : "Unsupported"
        };
        return string.IsNullOrWhiteSpace(status.Message) ? label : $"{label}: {status.Message}";
    }

    private void RestartApplication()
    {
        _toasts.DismissAll();
        _restartService?.Restart();
    }

    private bool GetAutoStartEnabled()
    {
        var result = _autoStartService.GetEnabled();
        if (result.IsSuccess)
            return result.Value;

        ShowToast(Resources.AutoStart, result.Error.Message, ToastNotification.Error);
        return false;
    }
}

public enum SettingsPaneId
{
    General,
    Translation,
    Selection,
    Speech,
    Screenshot,
    Result,
    Input
}

public sealed class SettingsNavItem(
    SettingsPaneId id,
    string title,
    MaterialIconKind icon,
    string searchFields) : ReactiveObject
{
    private bool _isSelected;

    public SettingsPaneId Id { get; } = id;
    public string Title { get; } = title;
    public MaterialIconKind Icon { get; } = icon;
    public string SearchFields { get; } = searchFields;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public sealed class ModelCardItem(CustomAiModelState? model)
{
    public CustomAiModelState? Model { get; } = model;
    public bool IsAddButton => Model is null;
    public bool IsModelCard => Model is not null;
    public string Name => Model?.Name ?? string.Empty;
    public AiModelType ModelType => Model?.ModelType ?? AiModelType.Custom;
    public string ApiUrl => Model?.ApiUrl ?? string.Empty;
    public string ModelName => Model?.Model ?? string.Empty;
}

public sealed record SelectionTriggerModeOption(SelectionTriggerMode Value, string DisplayName);

public sealed record SelectionFilterModeOption(SelectionFilterMode Value, string DisplayName);

public interface ISettingsDialogCoordinator
{
    void EditAiModel(CustomAiModelState? model);
    void DeleteAiModel(CustomAiModelState model);
    void EditAiModelKeys(CustomAiModelState model);
    void EditBaiduKeys();
    void EditTencentKeys();
    void EditGoogleKeys();
    void EditDeepLKeys();
    void ManageFixedAreas();
    void ConfigureTts();
    void ShowInformation(string title, string message);
    void ConfirmDeleteAsrModel(SpeechRecognitionModel model, Action onConfirmed);
    void ManageSelectionApps();
}
