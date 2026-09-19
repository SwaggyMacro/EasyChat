using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Threading;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Navigation;
using Material.Icons;
using ReactiveUI;
using ShadUI;

namespace EasyChat.Presentation.Features.Shortcuts
{
    public static class ShortcutActionCatalog
    {
        public static IReadOnlyList<EasyChat.Presentation.Features.Shortcuts.ShortcutActionOption> All { get; } =
        [
            new("Screenshot", "Action_ScreenshotTranslate"),
            new("ScreenshotOcr", "Action_ScreenshotOcr"),
            new("InputTranslate", "Action_InputTranslate"),
            new("RealtimeInterpretation", "Action_RealtimeInterpretation"),
            new("QuickTranslate", "Action_QuickTranslate"),
            new("QuickCorrect", "Action_QuickCorrect"),
            new("SelectionTranslate", "Action_SelectionTranslate"),
            new("SwitchSourceLang", "Action_SwitchSourceLang", true, "Hint_TargetLangCode",
                ["zh", "en", "ja", "ko", "fr", "de", "es", "ru"]),
            new("SwitchTargetLang", "Action_SwitchTargetLang", true, "Hint_TargetLangCode",
                ["zh", "en", "ja", "ko", "fr", "de", "es", "ru"]),
            new("SwitchEngineSourceTarget", "Action_SwitchEngineSourceTarget", true, "Hint_SwitchConfig"),
            new("AutomaticKey", "Action_AutomaticKey")
        ];

        public static EasyChat.Presentation.Features.Shortcuts.ShortcutActionOption? Get(string actionType) =>
            All.FirstOrDefault(action => action.ActionType == actionType);

        public static string GetDisplayName(string actionType) => Get(actionType)?.DisplayName ?? actionType;
    }
}

namespace EasyChat.Presentation.Features.Shortcuts
{
    using EasyChat.Presentation.Features.Shortcuts;

    public sealed class ShortcutViewModel : NavigationPageViewModel
    {
        private static readonly string[] BasicTypes =
            ["Screenshot", "ScreenshotOcr", "InputTranslate", "RealtimeInterpretation", "SelectionTranslate", "QuickTranslate", "QuickCorrect"];
        private static readonly string[] TextAssistTypes = ["QuickTranslate", "QuickCorrect"];
        private static readonly string[] LanguageTypes = ["SwitchEngineSourceTarget"];
        private static readonly string[] AutomaticTypes = ["AutomaticKey"];
        private readonly SettingsSession _settings;
        private readonly DialogManager _dialogManager;
        private readonly TranslationLanguageOptions _languages;
        private readonly IGlobalKeyboardMonitor _keyboardMonitor;
        private ObservableCollection<ShortcutEntryState> _basicShortcuts = [];
        private ObservableCollection<ShortcutEntryState> _languageShortcuts = [];
        private ObservableCollection<ShortcutEntryState> _automaticShortcuts = [];
        private ObservableCollection<ShortcutEntryState> _activeShortcuts = [];
        private int _categoryTabIndex;

        public ShortcutViewModel(
            SettingsSession settings,
            DialogManager dialogManager,
            TranslationLanguageOptions languages,
            IGlobalKeyboardMonitor keyboardMonitor)
            : base(Resources.Shortcut, MaterialIconKind.Keyboard, 2)
        {
            _settings = settings;
            _dialogManager = dialogManager;
            _languages = languages;
            _keyboardMonitor = keyboardMonitor ?? throw new ArgumentNullException(nameof(keyboardMonitor));
            Refresh();
            settings.Shortcut.Entries.CollectionChanged += (_, _) => Refresh();
            AddEntryCommand = ReactiveCommand.Create(AddCurrentCategoryEntry);
            AddEntryInCategoryCommand = ReactiveCommand.Create<string>(AddEntry);
            EditEntryCommand = ReactiveCommand.Create<ShortcutEntryState>(EditEntry);
            RemoveEntryCommand = ReactiveCommand.Create<ShortcutEntryState>(RemoveEntry);
            SelectBasicCategoryCommand = ReactiveCommand.Create(() => { IsBasicCategory = true; });
            SelectLanguageCategoryCommand = ReactiveCommand.Create(() => { IsLanguageCategory = true; });
            SelectAutomaticCategoryCommand = ReactiveCommand.Create(() => { IsAutomaticCategory = true; });
        }

        public ObservableCollection<ShortcutEntryState> BasicShortcuts
        {
            get => _basicShortcuts;
            private set => this.RaiseAndSetIfChanged(ref _basicShortcuts, value);
        }
        public ObservableCollection<ShortcutEntryState> LanguageShortcuts
        {
            get => _languageShortcuts;
            private set => this.RaiseAndSetIfChanged(ref _languageShortcuts, value);
        }
        public ObservableCollection<ShortcutEntryState> AutomaticShortcuts
        {
            get => _automaticShortcuts;
            private set => this.RaiseAndSetIfChanged(ref _automaticShortcuts, value);
        }
        public ObservableCollection<ShortcutEntryState> ActiveShortcuts
        {
            get => _activeShortcuts;
            private set => this.RaiseAndSetIfChanged(ref _activeShortcuts, value);
        }

        public bool IsBasicCategory
        {
            get => _categoryTabIndex == 0;
            set
            {
                if (!value || _categoryTabIndex == 0)
                    return;
                SetCategory(0);
            }
        }

        public bool IsLanguageCategory
        {
            get => _categoryTabIndex == 1;
            set
            {
                if (value)
                    SetCategory(1);
            }
        }

        public bool IsAutomaticCategory
        {
            get => _categoryTabIndex == 2;
            set
            {
                if (value)
                    SetCategory(2);
            }
        }

        /// <summary>0 = basic, 1 = language, 2 = automatic shortcuts.</summary>
        public int CategoryTabIndex
        {
            get => _categoryTabIndex;
            set
            {
                if (value is < 0 or > 2)
                    return;
                SetCategory(value);
            }
        }

        public string ActiveCategoryTitle =>
            CategoryTabIndex switch
            {
                0 => Resources.BasicShortcuts,
                1 => Resources.LanguageShortcuts,
                _ => Resources.AutomaticShortcuts
            };
        public string AddButtonLabel =>
            CategoryTabIndex switch
            {
                0 => Resources.AddBasicShortcut,
                1 => Resources.AddLanguageShortcut,
                _ => Resources.AddAutomaticShortcut
            };
        public string EmptyStateMessage =>
            CategoryTabIndex switch
            {
                0 => Resources.HomeHealthShortcutTodo,
                1 => Resources.LanguageShortcutEmptyState,
                _ => Resources.AutomaticShortcutEmptyState
            };
        public int ActiveShortcutCount => ActiveShortcuts.Count;
        public bool HasActiveShortcuts => ActiveShortcuts.Count > 0;
        public bool HasNoActiveShortcuts => !HasActiveShortcuts;

        public ReactiveCommand<Unit, Unit> AddEntryCommand { get; }
        public ReactiveCommand<string, Unit> AddEntryInCategoryCommand { get; }
        public ReactiveCommand<ShortcutEntryState, Unit> EditEntryCommand { get; }
        public ReactiveCommand<ShortcutEntryState, Unit> RemoveEntryCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectBasicCategoryCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectLanguageCategoryCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectAutomaticCategoryCommand { get; }

        private void Refresh()
        {
            BasicShortcuts = new ObservableCollection<ShortcutEntryState>(
                _settings.Shortcut.Entries.Where(entry => BasicTypes.Contains(entry.ActionType)));
            LanguageShortcuts = new ObservableCollection<ShortcutEntryState>(
                _settings.Shortcut.Entries.Where(entry => LanguageTypes.Contains(entry.ActionType)));
            AutomaticShortcuts = new ObservableCollection<ShortcutEntryState>(
                _settings.Shortcut.Entries.Where(entry => AutomaticTypes.Contains(entry.ActionType)));
            SyncActiveList();
            this.RaisePropertyChanged(nameof(ActiveShortcutCount));
            this.RaisePropertyChanged(nameof(HasActiveShortcuts));
            this.RaisePropertyChanged(nameof(HasNoActiveShortcuts));
        }

        private void SyncActiveList() =>
            ActiveShortcuts = CategoryTabIndex switch
            {
                0 => BasicShortcuts,
                1 => LanguageShortcuts,
                _ => AutomaticShortcuts
            };

        private void SetCategory(int category)
        {
            if (_categoryTabIndex == category)
                return;
            _categoryTabIndex = category;
            this.RaisePropertyChanged(nameof(IsBasicCategory));
            this.RaisePropertyChanged(nameof(IsLanguageCategory));
            this.RaisePropertyChanged(nameof(IsAutomaticCategory));
            this.RaisePropertyChanged(nameof(CategoryTabIndex));
            this.RaisePropertyChanged(nameof(ActiveCategoryTitle));
            this.RaisePropertyChanged(nameof(AddButtonLabel));
            this.RaisePropertyChanged(nameof(EmptyStateMessage));
            SyncActiveList();
            this.RaisePropertyChanged(nameof(ActiveShortcutCount));
            this.RaisePropertyChanged(nameof(HasActiveShortcuts));
            this.RaisePropertyChanged(nameof(HasNoActiveShortcuts));
        }

        private void AddCurrentCategoryEntry() =>
            AddEntry(CategoryTabIndex switch
            {
                0 => "Basic",
                1 => "Language",
                _ => "Automatic"
            });

        private void AddEntry(string category)
        {
            var allowed = category switch
            {
                "TextAssist" => TextAssistTypes,
                "Basic" => BasicTypes,
                "Automatic" => AutomaticTypes,
                _ => LanguageTypes
            };
            ShowEditor(null, allowed, category switch
            {
                "TextAssist" => "QuickTranslate",
                "Basic" => "Screenshot",
                "Automatic" => "AutomaticKey",
                _ => "SwitchEngineSourceTarget"
            });
        }

        private void EditEntry(ShortcutEntryState entry)
        {
            var allowed = TextAssistTypes.Contains(entry.ActionType)
                ? TextAssistTypes
                : AutomaticTypes.Contains(entry.ActionType)
                    ? AutomaticTypes
                    : BasicTypes.Contains(entry.ActionType) ? BasicTypes : LanguageTypes;
            ShowEditor(entry, allowed, entry.ActionType);
        }

        private void ShowEditor(ShortcutEntryState? entry, IReadOnlyList<string> allowed, string defaultAction)
        {
            var viewModel = new ShortcutEditDialogViewModel(
                _dialogManager,
                _settings,
                _languages,
                _keyboardMonitor,
                allowed,
                entry,
                defaultAction)
            {
                OnClose = result =>
                {
                    if (result is null)
                        return;
                    var replacement = new ShortcutEntryState(result, _settings.FlushSection);
                    if (entry is null)
                        _settings.Shortcut.Entries.Add(replacement);
                    else
                        _settings.Shortcut.Entries[_settings.Shortcut.Entries.IndexOf(entry)] = replacement;
                }
            };
            _dialogManager.CreateDialog(viewModel).Show();
        }

        private void RemoveEntry(ShortcutEntryState entry) => _dialogManager
            .CreateDialog(Resources.ConfirmDeletion, Resources.AreYouSureDelete)
            .WithPrimaryButton(
                Resources.Delete,
                () => _settings.Shortcut.Entries.Remove(entry),
                DialogButtonStyle.Destructive)
            .WithCancelButton(Resources.Cancel)
            .Show();
    }
}

namespace EasyChat.Presentation.Features.Shortcuts
{
    using EasyChat.Presentation.Features.Shortcuts;

    public sealed class ShortcutEditDialogViewModel : ConventionViewModelBase
    {
        private readonly DialogManager _dialogManager;
        private readonly ShortcutEntryState? _existing;
        private readonly SettingsSession _settings;
        private readonly TranslationLanguageOptions _languageOptions;
        private readonly IGlobalKeyboardMonitor _keyboardMonitor;
        private IKeyboardMonitorRegistration? _keyboardMonitorRegistration;
        private ShortcutActionOption _selectedAction;
        private EngineOption? _selectedEngineOption;
        private LanguageSettings? _selectedSourceLang;
        private LanguageSettings? _selectedTargetLang;
        private string _parameter = string.Empty;
        private string _keyCombination = string.Empty;
        private string _recordingPreview = string.Empty;
        private bool _isRecording;
        private bool _isRecordingBeforeInputKey;
        private bool _isRecordingAfterInputKey;
        private string _inputTranslateBeforeKey = string.Empty;
        private string _inputTranslateAfterKey = string.Empty;
        private bool _replaceCurrentInput;
        private bool _readSelectedText;
        private bool _showSelectionToolbar;
        private TextAssistShortcutMode _textAssistMode;
        private AutomaticShortcutTrigger _automaticTrigger = AutomaticShortcutTrigger.RealtimeInterpretationPlayback;
        private AutomaticShortcutPressMode _automaticPressMode = AutomaticShortcutPressMode.Tap;
        private string _remark = string.Empty;

        public ShortcutEditDialogViewModel(
            DialogManager dialogManager,
            SettingsSession settings,
            TranslationLanguageOptions languageOptions,
            IGlobalKeyboardMonitor keyboardMonitor,
            IReadOnlyList<string> allowedActionTypes,
            ShortcutEntryState? existing = null,
            string? defaultAction = null)
        {
            _dialogManager = dialogManager;
            _settings = settings;
            _languageOptions = languageOptions;
            _keyboardMonitor = keyboardMonitor ?? throw new ArgumentNullException(nameof(keyboardMonitor));
            _existing = existing;
            AvailableActions = ShortcutActionCatalog.All
                .Where(action => allowedActionTypes.Contains(action.ActionType))
                .ToArray();
            _selectedAction = ShortcutActionCatalog.Get(existing?.ActionType ?? defaultAction ?? string.Empty)
                              ?? AvailableActions.First();
            AvailableEngineOptions =
            [
                .. new[] { "Baidu", "Tencent", "Google", "DeepL" }
                    .Select(provider => new EngineOption(provider, provider, true)),
                .. settings.AiModel.ConfiguredModels
                    .Select(model => new EngineOption(model.Name, model.Id, false))
            ];
            _selectedEngineOption = AvailableEngineOptions.FirstOrDefault(option => option.Id == "Baidu")
                                    ?? AvailableEngineOptions.FirstOrDefault();
            UpdateAvailableLanguages();
            _selectedSourceLang = AvailableLanguages.FirstOrDefault(language => language.Id == "auto")
                                  ?? AvailableLanguages.FirstOrDefault();
            _selectedTargetLang = AvailableLanguages.FirstOrDefault(language => language.Id == "zh-Hans")
                                  ?? AvailableLanguages.FirstOrDefault();
            Restore(existing);

            ToggleRecordingCommand = ReactiveCommand.Create(() =>
            {
                if (IsRecording)
                    StopRecording();
                else
                    BeginPrimaryRecording();
            });
            ToggleBeforeInputKeyRecordingCommand = ReactiveCommand.Create(() => StartInputKeyRecording(true));
            ToggleAfterInputKeyRecordingCommand = ReactiveCommand.Create(() => StartInputKeyRecording(false));
            ClearBeforeInputKeyCommand = ReactiveCommand.Create(() => { InputTranslateBeforeKey = string.Empty; StopRecording(); });
            ClearAfterInputKeyCommand = ReactiveCommand.Create(() => { InputTranslateAfterKey = string.Empty; StopRecording(); });
            var canSave = this.WhenAnyValue(
                viewModel => viewModel.KeyCombination,
                viewModel => viewModel.SelectedAction,
                viewModel => viewModel.Parameter,
                viewModel => viewModel.SelectedEngineOption,
                (key, action, parameter, engine) =>
                    !string.IsNullOrWhiteSpace(key) &&
                    (!action.RequiresParameter ||
                     (action.ActionType == "SwitchEngineSourceTarget"
                         ? engine is not null && SelectedSourceLang is not null && SelectedTargetLang is not null
                         : !string.IsNullOrWhiteSpace(parameter))));
            SaveCommand = ReactiveCommand.Create(Save, canSave);
            CancelCommand = ReactiveCommand.Create(Cancel);
            // Do not auto-start recording here: the view must be attached and focused first,
            // otherwise key events never reach the capture handlers under dialog chrome.
        }

        public sealed record EngineOption(string Name, string Id, bool IsMachine);

        public IReadOnlyList<ShortcutActionOption> AvailableActions { get; }
        public IReadOnlyList<EngineOption> AvailableEngineOptions { get; }
        public IReadOnlyList<LanguageSettings> AvailableLanguages { get; private set; } = [];
        public IReadOnlyList<string>? AvailableParameterOptions => SelectedAction.ParameterOptions;
        public IReadOnlyList<TextAssistShortcutMode> TextAssistModes { get; } = Enum.GetValues<TextAssistShortcutMode>();
        public IReadOnlyList<string> AutomaticTriggers { get; } =
            [Resources.AutomaticShortcutTriggerRealtimeInterpretationPlayback];
        public IReadOnlyList<string> AutomaticPressModes { get; } =
            [Resources.AutomaticShortcutPressModeTap, Resources.AutomaticShortcutPressModeHold];
        public string ButtonText => _existing is null ? Resources.Add : Resources.Save;
        public string DialogTitle => _existing is null ? Resources.Add : Resources.Edit;
        public MaterialIconKind Icon => _existing is null ? MaterialIconKind.Plus : MaterialIconKind.Pencil;
        public bool IsComplexSwitchAction => SelectedAction.ActionType == "SwitchEngineSourceTarget";
        public bool IsTextAssistAction => SelectedAction.ActionType is "QuickTranslate" or "QuickCorrect";
        public bool IsModeSelectableTextAssistAction => false;
        public bool IsSelectionTranslateAction => SelectedAction.ActionType == "SelectionTranslate";
        public bool IsInputTranslateAction => SelectedAction.ActionType == "InputTranslate";
        public bool IsAutomaticAction => SelectedAction.ActionType == "AutomaticKey";
        public string KeyCombinationLabel => IsAutomaticAction
            ? Resources.AutomaticShortcutKey
            : Resources.KeyCombination;
        public string Remark { get => _remark; set => this.RaiseAndSetIfChanged(ref _remark, value); }
        public string SelectionToolbarOptionText => Resources.SelectionTranslation;
        public string SelectionToolbarOptionTip => Resources.ResourceManager.GetString("SelectionShortcutToolbarTip", Resources.Culture)
                                                   ?? "Show the configured selection toolbar instead of translating immediately.";

        public ShortcutActionOption SelectedAction
        {
            get => _selectedAction;
            set
            {
                if (_selectedAction == value)
                    return;
                this.RaiseAndSetIfChanged(ref _selectedAction, value);
                RaiseActionProperties();
                if (_existing?.ActionType != value.ActionType)
                {
                    Parameter = string.Empty;
                    ReadSelectedText = false;
                    ShowSelectionToolbar = false;
                    ReplaceCurrentInput = false;
                    AutomaticTrigger = AutomaticShortcutTrigger.RealtimeInterpretationPlayback;
                    AutomaticPressMode = AutomaticShortcutPressMode.Tap;
                }
            }
        }
        public EngineOption? SelectedEngineOption
        {
            get => _selectedEngineOption;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedEngineOption, value);
                UpdateAvailableLanguages();
            }
        }
        public LanguageSettings? SelectedSourceLang { get => _selectedSourceLang; set => this.RaiseAndSetIfChanged(ref _selectedSourceLang, value); }
        public LanguageSettings? SelectedTargetLang { get => _selectedTargetLang; set => this.RaiseAndSetIfChanged(ref _selectedTargetLang, value); }
        public string Parameter { get => _parameter; set => this.RaiseAndSetIfChanged(ref _parameter, value); }
        public string KeyCombination
        {
            get => _keyCombination;
            set
            {
                this.RaiseAndSetIfChanged(ref _keyCombination, value);
                this.RaisePropertyChanged(nameof(DisplayedKeyCombination));
            }
        }
        public string RecordingPreview
        {
            get => _recordingPreview;
            private set
            {
                this.RaiseAndSetIfChanged(ref _recordingPreview, value);
                this.RaisePropertyChanged(nameof(DisplayedKeyCombination));
                this.RaisePropertyChanged(nameof(DisplayedBeforeInputKey));
                this.RaisePropertyChanged(nameof(DisplayedAfterInputKey));
                this.RaisePropertyChanged(nameof(IsBeforeInputRecordingPromptVisible));
                this.RaisePropertyChanged(nameof(IsAfterInputRecordingPromptVisible));
            }
        }
        public string DisplayedKeyCombination => IsRecording ? RecordingPreview : KeyCombination;
        public string DisplayedBeforeInputKey => IsRecordingBeforeInputKey ? RecordingPreview : InputTranslateBeforeKey;
        public string DisplayedAfterInputKey => IsRecordingAfterInputKey ? RecordingPreview : InputTranslateAfterKey;
        public bool IsBeforeInputRecordingPromptVisible =>
            IsRecordingBeforeInputKey && string.IsNullOrEmpty(RecordingPreview);
        public bool IsAfterInputRecordingPromptVisible =>
            IsRecordingAfterInputKey && string.IsNullOrEmpty(RecordingPreview);
        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRecording, value);
                this.RaisePropertyChanged(nameof(DisplayedKeyCombination));
                this.RaisePropertyChanged(nameof(IsNotRecording));
            }
        }
        public bool IsNotRecording => !IsRecording;
        public bool IsRecordingBeforeInputKey
        {
            get => _isRecordingBeforeInputKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRecordingBeforeInputKey, value);
                this.RaisePropertyChanged(nameof(IsNotRecordingBeforeInputKey));
                this.RaisePropertyChanged(nameof(DisplayedBeforeInputKey));
                this.RaisePropertyChanged(nameof(IsBeforeInputRecordingPromptVisible));
            }
        }
        public bool IsNotRecordingBeforeInputKey => !IsRecordingBeforeInputKey;
        public bool IsRecordingAfterInputKey
        {
            get => _isRecordingAfterInputKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRecordingAfterInputKey, value);
                this.RaisePropertyChanged(nameof(IsNotRecordingAfterInputKey));
                this.RaisePropertyChanged(nameof(DisplayedAfterInputKey));
                this.RaisePropertyChanged(nameof(IsAfterInputRecordingPromptVisible));
            }
        }
        public bool IsNotRecordingAfterInputKey => !IsRecordingAfterInputKey;
        public string InputTranslateBeforeKey
        {
            get => _inputTranslateBeforeKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _inputTranslateBeforeKey, value);
                this.RaisePropertyChanged(nameof(DisplayedBeforeInputKey));
            }
        }
        public string InputTranslateAfterKey
        {
            get => _inputTranslateAfterKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _inputTranslateAfterKey, value);
                this.RaisePropertyChanged(nameof(DisplayedAfterInputKey));
            }
        }
        public bool ReplaceCurrentInput { get => _replaceCurrentInput; set => this.RaiseAndSetIfChanged(ref _replaceCurrentInput, value); }
        public bool ReadSelectedText { get => _readSelectedText; set => this.RaiseAndSetIfChanged(ref _readSelectedText, value); }
        public bool ShowSelectionToolbar { get => _showSelectionToolbar; set => this.RaiseAndSetIfChanged(ref _showSelectionToolbar, value); }
        public AutomaticShortcutTrigger AutomaticTrigger
        {
            get => _automaticTrigger;
            set => this.RaiseAndSetIfChanged(ref _automaticTrigger, value);
        }
        public AutomaticShortcutPressMode AutomaticPressMode
        {
            get => _automaticPressMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _automaticPressMode, value);
                this.RaisePropertyChanged(nameof(SelectedAutomaticPressModeOption));
            }
        }
        public string SelectedAutomaticTriggerOption
        {
            get => Resources.AutomaticShortcutTriggerRealtimeInterpretationPlayback;
            set => AutomaticTrigger = AutomaticShortcutTrigger.RealtimeInterpretationPlayback;
        }
        public string SelectedAutomaticPressModeOption
        {
            get => AutomaticPressMode == AutomaticShortcutPressMode.Hold
                ? Resources.AutomaticShortcutPressModeHold
                : Resources.AutomaticShortcutPressModeTap;
            set => AutomaticPressMode = string.Equals(
                value,
                Resources.AutomaticShortcutPressModeHold,
                StringComparison.CurrentCulture)
                ? AutomaticShortcutPressMode.Hold
                : AutomaticShortcutPressMode.Tap;
        }
        public TextAssistShortcutMode TextAssistMode
        {
            get => _textAssistMode;
            set { this.RaiseAndSetIfChanged(ref _textAssistMode, value); this.RaisePropertyChanged(nameof(IsReadSelectionLocked)); }
        }
        public bool IsReadSelectionLocked => IsModeSelectableTextAssistAction && TextAssistMode == TextAssistShortcutMode.Simple;
        public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleBeforeInputKeyRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleAfterInputKeyRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearBeforeInputKeyCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearAfterInputKeyCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public Action<ShortcutEntrySettings?>? OnClose { get; init; }

        public void PreviewRecordedKeyCombination(string combination) => RecordingPreview = combination;

        public void BeginPrimaryRecording()
        {
            RecordingPreview = string.Empty;
            IsRecordingBeforeInputKey = false;
            IsRecordingAfterInputKey = false;
            IsRecording = true;
            StartKeyboardMonitor();
        }

        public void SetRecordedKeyCombination(string combination)
        {
            if (IsRecordingBeforeInputKey)
                InputTranslateBeforeKey = combination;
            else if (IsRecordingAfterInputKey)
                InputTranslateAfterKey = combination;
            else
                KeyCombination = combination;
            StopRecording();
        }

        public void StopRecording()
        {
            StopKeyboardMonitor();
            IsRecording = false;
            IsRecordingBeforeInputKey = false;
            IsRecordingAfterInputKey = false;
            RecordingPreview = string.Empty;
        }

        private void StartInputKeyRecording(bool before)
        {
            StopKeyboardMonitor();
            RecordingPreview = string.Empty;
            IsRecording = false;
            IsRecordingBeforeInputKey = before;
            IsRecordingAfterInputKey = !before;
            StartKeyboardMonitor();
        }

        private void StartKeyboardMonitor()
        {
            StopKeyboardMonitor();
            try
            {
                _keyboardMonitorRegistration = _keyboardMonitor.Start(OnGlobalKeyboardEvent);
            }
            catch
            {
                // The window-level Avalonia handlers remain available as a local fallback.
                _keyboardMonitorRegistration = null;
            }
        }

        private void StopKeyboardMonitor() =>
            Interlocked.Exchange(ref _keyboardMonitorRegistration, null)?.Dispose();

        private void OnGlobalKeyboardEvent(GlobalKeyboardEvent keyboardEvent)
        {
            if (!keyboardEvent.IsPressed || IsModifierKey(keyboardEvent.Key))
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (!IsRecording && !IsRecordingBeforeInputKey && !IsRecordingAfterInputKey)
                    return;

                if (keyboardEvent.Key is "Escape")
                {
                    StopRecording();
                    return;
                }

                if (keyboardEvent.Key is "Tab")
                {
                    StopRecording();
                    return;
                }

                var parts = new List<string>();
                if (keyboardEvent.Modifiers.HasFlag(ShortcutModifiers.Control))
                    parts.Add("Ctrl");
                if (keyboardEvent.Modifiers.HasFlag(ShortcutModifiers.Alt))
                    parts.Add("Alt");
                if (keyboardEvent.Modifiers.HasFlag(ShortcutModifiers.Shift))
                    parts.Add("Shift");
                if (keyboardEvent.Modifiers.HasFlag(ShortcutModifiers.Meta))
                    parts.Add("Win");
                parts.Add(keyboardEvent.Key);
                SetRecordedKeyCombination(string.Join(" + ", parts));
            });
        }

        private static bool IsModifierKey(string key) => key is
            "Ctrl" or "Control" or "Alt" or "Shift" or "LWin" or "RWin";

        private void Restore(ShortcutEntryState? entry)
        {
            if (entry is null)
                return;
            KeyCombination = entry.KeyCombination;
            Remark = entry.Remark ?? string.Empty;
            Parameter = entry.Parameter?.Value ?? string.Empty;
            ReadSelectedText = IsTextAssistAction && (entry.Parameter?.ReadSelectedText ?? true);
            InputTranslateBeforeKey = entry.Parameter?.InputTranslateBeforeKey ?? string.Empty;
            InputTranslateAfterKey = entry.Parameter?.InputTranslateAfterKey ?? string.Empty;
            ReplaceCurrentInput = entry.Parameter?.ReplaceCurrentInput ?? false;
            ShowSelectionToolbar = entry.Parameter?.ShowSelectionToolbar ?? false;
            TextAssistMode = entry.Parameter?.TextAssistMode ?? TextAssistShortcutMode.Simple;
            AutomaticTrigger = entry.Parameter?.AutomaticTrigger
                ?? AutomaticShortcutTrigger.RealtimeInterpretationPlayback;
            AutomaticPressMode = entry.Parameter?.AutomaticPressMode
                ?? AutomaticShortcutPressMode.Tap;
            if (!IsComplexSwitchAction || entry.Parameter is null)
                return;
            SelectedEngineOption = AvailableEngineOptions.FirstOrDefault(option => option.Id == entry.Parameter.EngineId)
                                   ?? AvailableEngineOptions.FirstOrDefault(option => option.Name == entry.Parameter.Engine)
                                   ?? SelectedEngineOption;
            SelectedSourceLang = AvailableLanguages.FirstOrDefault(language => language.Id == entry.Parameter.Source?.Id)
                                 ?? SelectedSourceLang;
            SelectedTargetLang = AvailableLanguages.FirstOrDefault(language => language.Id == entry.Parameter.Target?.Id)
                                 ?? SelectedTargetLang;
        }

        private void UpdateAvailableLanguages()
        {
            var all = _languageOptions.All.ToList();
            if (SelectedEngineOption is { IsMachine: true } engine)
            {
                all = all.Where(language => language.Id == "auto" ||
                    language.ProviderCodes.TryGetValue(engine.Id, out var code) && !string.IsNullOrWhiteSpace(code)).ToList();
            }
            AvailableLanguages = all;
            this.RaisePropertyChanged(nameof(AvailableLanguages));
            SelectedSourceLang = all.FirstOrDefault(language => language.Id == SelectedSourceLang?.Id)
                                 ?? all.FirstOrDefault(language => language.Id == "auto")
                                 ?? all.FirstOrDefault();
            SelectedTargetLang = all.FirstOrDefault(language => language.Id == SelectedTargetLang?.Id)
                                 ?? all.FirstOrDefault(language => language.Id == "zh-Hans")
                                 ?? all.FirstOrDefault();
        }

        private void RaiseActionProperties()
        {
            this.RaisePropertyChanged(nameof(IsComplexSwitchAction));
            this.RaisePropertyChanged(nameof(IsTextAssistAction));
            this.RaisePropertyChanged(nameof(IsModeSelectableTextAssistAction));
            this.RaisePropertyChanged(nameof(IsSelectionTranslateAction));
            this.RaisePropertyChanged(nameof(IsInputTranslateAction));
            this.RaisePropertyChanged(nameof(IsAutomaticAction));
            this.RaisePropertyChanged(nameof(KeyCombinationLabel));
            this.RaisePropertyChanged(nameof(AvailableParameterOptions));
        }

        private void Save()
        {
            StopKeyboardMonitor();
            var parameter = IsComplexSwitchAction
                ? new ShortcutParameterSettings(
                    SelectedEngineOption?.Name ?? string.Empty,
                    SelectedEngineOption?.Id,
                    SelectedSourceLang,
                    SelectedTargetLang,
                    null, null, null, null, null, null, null)
                : new ShortcutParameterSettings(
                    string.Empty, null, null, null,
                    string.IsNullOrWhiteSpace(Parameter) ? null : Parameter,
                    IsTextAssistAction ? ReadSelectedText : null,
                    IsInputTranslateAction ? NullIfEmpty(InputTranslateBeforeKey) : null,
                    IsInputTranslateAction ? NullIfEmpty(InputTranslateAfterKey) : null,
                    IsInputTranslateAction ? ReplaceCurrentInput : null,
                    IsModeSelectableTextAssistAction ? TextAssistMode : null,
                    IsSelectionTranslateAction ? ShowSelectionToolbar : null,
                    IsAutomaticAction ? AutomaticTrigger : null,
                    IsAutomaticAction ? AutomaticPressMode : null);
            OnClose?.Invoke(new ShortcutEntrySettings(
                SelectedAction.ActionType,
                SelectedAction.RequiresParameter || IsTextAssistAction || IsInputTranslateAction || IsSelectionTranslateAction || IsAutomaticAction
                    ? parameter
                    : null,
                KeyCombination,
                _existing?.IsEnabled ?? true,
                NullIfEmpty(Remark)?.Trim()));
            _dialogManager.Close(this);
        }

        private void Cancel()
        {
            StopKeyboardMonitor();
            OnClose?.Invoke(null);
            _dialogManager.Close(this);
        }

        private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public sealed record ShortcutActionOption(
        string ActionType,
        string ResourceKey,
        bool RequiresParameter = false,
        string? ParameterHintKey = null,
        IReadOnlyList<string>? ParameterOptions = null)
    {
        public string DisplayName => Resources.ResourceManager.GetString(ResourceKey, Resources.Culture) ?? ResourceKey;
        public string? ParameterHint => string.IsNullOrWhiteSpace(ParameterHintKey)
            ? null
            : Resources.ResourceManager.GetString(ParameterHintKey, Resources.Culture) ?? ParameterHintKey;
    }
}
