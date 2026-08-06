using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Reactive;
using Avalonia.Threading;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.UiHost;
using Material.Icons;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Settings;

public sealed class SettingViewModel : NavigationPageViewModel
{
    private static readonly Uri AsrModelDownloadsUri = new(
        "https://github.com/SwaggyMacro/MicroASR/releases/tag/models-v1");
    private readonly SettingsSession _settings;
    private readonly IOcrModelUseCases _ocr;
    private readonly ITranslationUseCases _translation;
    private readonly ITranslationLanguageCatalog _languages;
    private readonly ISpeechRecognitionModelCatalog _speechModels;
    private readonly ISpeechRecognitionModelInstaller _speechModelInstaller;
    private readonly ISpeechRecognitionModelRemover _speechModelRemover;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly ISettingsDialogCoordinator _dialogs;
    private readonly IUiToastHost _toasts;
    private readonly Dictionary<OcrModelDownloadItemViewModel, CancellationTokenSource> _downloads = [];
    private bool _isOcrModelListExpanded;
    private bool _isTestingBaidu;
    private bool _isTestingTencent;
    private bool _isTestingGoogle;
    private bool _isTestingDeepL;
    private ObservableCollection<ModelCardItem> _modelCardsWithAddButton = [];
    private ObservableCollection<string> _availableFonts = [];
    private ObservableCollection<SpeechRecognitionModel> _asrModels = [];
    private bool _isImportingAsrModel;
    private string _searchText = string.Empty;
    private bool _isSearchOpen;
    private SettingsPaneId _activePane = SettingsPaneId.General;
    private SettingsNavItem? _selectedNavItem;

    public SettingViewModel(
        SettingsSession settings,
        IOcrModelUseCases ocr,
        ITtsUseCases tts,
        ITranslationUseCases translation,
        ITranslationLanguageCatalog languages,
        ISpeechRecognitionModelCatalog speechModels,
        ISpeechRecognitionModelInstaller speechModelInstaller,
        ISpeechRecognitionModelRemover speechModelRemover,
        IExternalUriLauncher uriLauncher,
        ISettingsDialogCoordinator dialogs,
        IUiToastHost toasts)
        : base(Resources.Settings, MaterialIconKind.Settings, 1)
    {
        _settings = settings;
        _ocr = ocr;
        _translation = translation;
        _languages = languages;
        _speechModels = speechModels;
        _speechModelInstaller = speechModelInstaller;
        _speechModelRemover = speechModelRemover;
        _uriLauncher = uriLauncher;
        _dialogs = dialogs;
        _toasts = toasts;

        DisplayLanguages = BuildDisplayLanguages();
        NativeLanguages = BuildLanguages(includeAuto: false);
        OcrModelItems = new ObservableCollection<OcrModelDownloadItemViewModel>(
            _ocr.SupportedLanguages.Select(language => new OcrModelDownloadItemViewModel(
                language,
                _ocr.IsModelDownloaded(language),
                _ocr.CanDeleteModels)));

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
        OpenAsrModelDownloadsCommand = ReactiveCommand.Create(OpenAsrModelDownloads);
        DeleteAsrModelCommand = ReactiveCommand.Create<SpeechRecognitionModel>(ConfirmDeleteAsrModel);

        TestAiModelConnectionCommand = ReactiveCommand.CreateFromTask<CustomAiModelState>(TestAiModelConnectionAsync);
        TestBaiduConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("Baidu"));
        TestTencentConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("Tencent"));
        TestGoogleConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("Google"));
        TestDeepLConnectionCommand = ReactiveCommand.CreateFromTask(() => TestMachineConnectionAsync("DeepL"));

        DownloadOcrModelCommand = ReactiveCommand.Create<OcrModelDownloadItemViewModel>(StartDownloadOcrModel);
        CancelOcrModelCommand = ReactiveCommand.Create<OcrModelDownloadItemViewModel>(CancelOcrModel);
        DeleteOcrModelCommand = ReactiveCommand.Create<OcrModelDownloadItemViewModel>(DeleteOcrModel);
        ToggleOcrModelListCommand = ReactiveCommand.Create(() =>
        {
            IsOcrModelListExpanded = !IsOcrModelListExpanded;
        });

        NavItems =
        [
            new(SettingsPaneId.General, Resources.General, MaterialIconKind.Cog, SettingsSearch.GeneralFields),
            new(SettingsPaneId.Translation, Resources.Translation, MaterialIconKind.Translate, SettingsSearch.TranslationFields),
            new(SettingsPaneId.Selection, Resources.SelectionToolbarSettings, MaterialIconKind.CursorDefault, SettingsSearch.SelectionFields),
            new(SettingsPaneId.Tts, Resources.Tts, MaterialIconKind.VolumeHigh, SettingsSearch.TtsFields),
            new(SettingsPaneId.Screenshot, Resources.ScreenshotMode, MaterialIconKind.Monitor, SettingsSearch.ScreenshotFields),
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
            EasyChat.Presentation.Features.Shell.SettingsPane.Tts => SettingsPaneId.Tts,
            EasyChat.Presentation.Features.Shell.SettingsPane.Screenshot => SettingsPaneId.Screenshot,
            EasyChat.Presentation.Features.Shell.SettingsPane.Result => SettingsPaneId.Result,
            EasyChat.Presentation.Features.Shell.SettingsPane.Input => SettingsPaneId.Input,
            _ => SettingsPaneId.General
        });

    public List<string> DeepLModelTypes { get; } = ["quality_optimized", "prefer_quality_optimized", "latency_optimized"];
    public List<LanguageSettings> DisplayLanguages { get; }
    public List<LanguageSettings> NativeLanguages { get; }
    public List<ClosingBehavior> ClosingBehaviors { get; } = Enum.GetValues<ClosingBehavior>().ToList();
    public List<string> ScreenshotModes { get; } = ["Precise", "Quick"];
    public List<string> MachineTransProviders { get; } = ["Baidu", "Tencent", "Google", "DeepL"];
    public List<string> TranslationEngineTypes { get; } = [Resources.AIEngine, Resources.MachineTranslation];
    public List<SelectionTriggerModeOption> SelectionTriggerModes { get; } =
    [
        new(SelectionTriggerMode.DoubleClick, Resources.SelectionTriggerModeDoubleClick),
        new(SelectionTriggerMode.DragSelection, Resources.SelectionTriggerModeDragSelection),
        new(SelectionTriggerMode.All, Resources.SelectionTriggerModeAll)
    ];
    public IReadOnlyList<string> TransparencyLevels { get; } =
        EasyChat.Presentation.Foundation.Platform.WindowTransparencyLevels.Preferences;
    public List<InputDeliveryMode> InputDeliveryModes { get; } = Enum.GetValues<InputDeliveryMode>().ToList();
    public List<ResultWindowMode> ResultWindowModes { get; } = Enum.GetValues<ResultWindowMode>().ToList();
    public List<ResultReadAloudMode> ResultReadAloudModes { get; } = Enum.GetValues<ResultReadAloudMode>().ToList();
    public List<string> TtsProviders { get; }

    public LiveGeneralSettings GeneralConf => _settings.General;
    public LiveAiModelSettings AiModelConf => _settings.AiModel;
    public ObservableCollection<CustomAiModelState> ConfiguredModels => AiModelConf.ConfiguredModels;
    public LiveMachineTranslationSettings MachineTransConf => _settings.MachineTranslation;
    public LiveProxySettings ProxyConf => _settings.Proxy;
    public LiveOcrSettings OcrConf => _settings.Ocr;
    public LiveResultSettings ResultConf => _settings.Result;
    public LiveInputSettings InputConf => _settings.Input;
    public LiveScreenshotSettings ScreenshotConf => _settings.Screenshot;
    public LiveSelectionTranslationSettings SelectionTranslationConf => _settings.SelectionTranslation;
    public ObservableCollection<PromptEntryState> PromptEntries => _settings.Prompts.Entries;
    public LiveTtsSettings TtsConf => _settings.Tts;
    public ObservableCollection<OcrModelDownloadItemViewModel> OcrModelItems { get; }
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
            this.RaisePropertyChanged(nameof(HasAsrModels));
            this.RaisePropertyChanged(nameof(HasNoAsrModels));
        }
    }
    public bool HasAsrModels => AsrModels.Count > 0;
    public bool HasNoAsrModels => !HasAsrModels;
    public bool IsImportingAsrModel
    {
        get => _isImportingAsrModel;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isImportingAsrModel, value);
            this.RaisePropertyChanged(nameof(CanImportAsrModel));
        }
    }
    public bool CanImportAsrModel => !IsImportingAsrModel;
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

    public bool ShowGeneralSection => IsSectionVisible(SettingsPaneId.General, Resources.General, SettingsSearch.GeneralFields);
    public bool ShowTranslationSection => IsSectionVisible(SettingsPaneId.Translation, Resources.Translation, SettingsSearch.TranslationFields);
    public bool ShowSelectionSection => IsSectionVisible(SettingsPaneId.Selection, Resources.SelectionToolbarSettings, SettingsSearch.SelectionFields);
    public bool ShowTtsSection => IsSectionVisible(SettingsPaneId.Tts, Resources.Tts, SettingsSearch.TtsFields);
    public bool ShowScreenshotSection => IsSectionVisible(SettingsPaneId.Screenshot, Resources.ScreenshotMode, SettingsSearch.ScreenshotFields);
    public bool ShowResultSection => IsSectionVisible(SettingsPaneId.Result, Resources.ResultSettings, SettingsSearch.ResultFields);
    public bool ShowInputSection => IsSectionVisible(SettingsPaneId.Input, Resources.InputSettings, SettingsSearch.InputFields);
    public bool HasSearchResults =>
        ShowGeneralSection || ShowTranslationSection || ShowSelectionSection || ShowTtsSection
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
        this.RaisePropertyChanged(nameof(ShowTtsSection));
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
            ShowToast(Resources.LanguageChanged, Resources.RestartToTakeEffect, UiMessageSeverity.Success);
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

    public string SelectedScreenshotMode
    {
        get => ScreenshotConf.Mode ?? "Precise";
        set
        {
            ScreenshotConf.Mode = value;
            this.RaisePropertyChanged();
        }
    }

    public SelectionTriggerMode SelectedSelectionTriggerMode
    {
        get => SelectionTranslationConf.TriggerMode;
        set
        {
            SelectionTranslationConf.TriggerMode = value;
            this.RaisePropertyChanged();
        }
    }

    public string SelectedSelectionTranslationEngine
    {
        get => SelectionTranslationConf.Provider == TranslationEngineNames.AiModel
            ? Resources.AIEngine
            : Resources.MachineTranslation;
        set
        {
            SelectionTranslationConf.Provider = value == Resources.AIEngine
                ? TranslationEngineNames.AiModel
                : TranslationEngineNames.MachineTrans;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsAiTranslationSelected));
            this.RaisePropertyChanged(nameof(IsMachineTranslationSelected));
        }
    }

    public bool IsAiTranslationSelected => SelectionTranslationConf.Provider == TranslationEngineNames.AiModel;
    public bool IsMachineTranslationSelected => !IsAiTranslationSelected;

    public string SelectedMachineTranslationProvider
    {
        get => SelectionTranslationConf.MachineProvider ?? "Baidu";
        set
        {
            SelectionTranslationConf.MachineProvider = value;
            this.RaisePropertyChanged();
        }
    }

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
    public ReactiveCommand<Unit, Unit> OpenAsrModelDownloadsCommand { get; }
    public ReactiveCommand<SpeechRecognitionModel, Unit> DeleteAsrModelCommand { get; }
    public ReactiveCommand<OcrModelDownloadItemViewModel, Unit> DownloadOcrModelCommand { get; }
    public ReactiveCommand<OcrModelDownloadItemViewModel, Unit> CancelOcrModelCommand { get; }
    public ReactiveCommand<OcrModelDownloadItemViewModel, Unit> DeleteOcrModelCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleOcrModelListCommand { get; }
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

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshModelCards();
        this.RaisePropertyChanged(nameof(AiProviders));
    }

    private void RefreshModelCards()
    {
        var cards = ConfiguredModels.Select(model => new ModelCardItem(model)).ToList();
        cards.Add(new ModelCardItem(null));
        ModelCardsWithAddButton = new ObservableCollection<ModelCardItem>(cards);
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
                    ? UiMessageSeverity.Information
                    : UiMessageSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowToast(Resources.AsrModelImportFailed, exception.Message, UiMessageSeverity.Error);
        }
        finally
        {
            IsImportingAsrModel = false;
        }
    }

    private void ConfirmDeleteAsrModel(SpeechRecognitionModel model) =>
        _dialogs.ConfirmDeleteAsrModel(model, () => _ = DeleteAsrModelAsync(model));

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
                    UiMessageSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            ShowToast(Resources.AsrModelDeleteFailed, exception.Message, UiMessageSeverity.Error);
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
    }

    private async Task LoadAsrModelsAsync()
    {
        try
        {
            await RefreshAsrModelsAsync();
        }
        catch (Exception exception)
        {
            ShowToast(Resources.AsrModels, exception.Message, UiMessageSeverity.Error);
        }
    }

    private void OpenAsrModelDownloads()
    {
        var result = _uriLauncher.Open(AsrModelDownloadsUri);
        if (result.IsFailure)
            ShowToast(Resources.AsrModels, result.Error.Message, UiMessageSeverity.Error);
    }

    private void StartDownloadOcrModel(OcrModelDownloadItemViewModel item) => _ = DownloadOcrModelAsync(item);

    private async Task DownloadOcrModelAsync(OcrModelDownloadItemViewModel item)
    {
        if (item.IsDownloading || item.IsDownloaded || _downloads.ContainsKey(item))
            return;

        var cancellation = new CancellationTokenSource();
        _downloads.Add(item, cancellation);
        item.StartDownload();
        try
        {
            await _ocr.DownloadModelAsync(item.Language, new Progress<double>(item.SetProgress), cancellation.Token);
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
            _ocr.DeleteModel(item.Language);
            item.MarkDeleted();
        }
        catch (Exception exception)
        {
            item.FailDownload(exception.Message);
        }
    }

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
                ShowToast(providerName, Resources.ConnectionSuccess, UiMessageSeverity.Success);
            else
                ShowToast(Resources.ConnectionFailed, $"{providerName}: {result.Error.Message}", UiMessageSeverity.Error);
        }
        catch (Exception exception)
        {
            ShowToast(Resources.ConnectionFailed, $"{providerName}: {exception.Message}", UiMessageSeverity.Error);
        }
        finally
        {
            setTesting(false);
        }
    }

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

    private void ShowToast(string title, string content, UiMessageSeverity severity) =>
        _toasts.Show(title, content, severity);
}

public enum SettingsPaneId
{
    General,
    Translation,
    Selection,
    Tts,
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
    void ConfirmDeleteAsrModel(SpeechRecognitionModel model, Action onConfirmed);
}
