using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Contracts.Updates;
using EasyChat.Presentation.Features.Settings;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Settings.Theme;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.UiHost;
using EasyChat.Presentation.Shared.Controls;
using Material.Icons;
using ReactiveUI;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Models;
using SukiUI.Toasts;

namespace EasyChat.Presentation.Features.Shell
{
    public sealed class PageNavigation
    {
        public event Action<Type, object?>? NavigationRequested;

        public void NavigateTo<TPage>(object? context = null)
            where TPage : NavigationPageViewModel =>
            NavigationRequested?.Invoke(typeof(TPage), context);
    }

    /// <summary>Optional payload when opening Settings from Home / badges.</summary>
    public enum SettingsPane
    {
        General,
        Translation,
        Selection,
        Tts,
        Screenshot,
        Result,
        Input
    }

    public sealed class MainWindowViewModel : ViewModelBase
    {
        private readonly SukiTheme _theme;
        private readonly SettingsSession _settings;
        private readonly IExternalUriLauncher _uriLauncher;
        private readonly IUiToastHost _toasts;
        private readonly IUiDialogHost _dialogs;
        private NavigationPageViewModel? _activePage;
        private ThemeMode _baseThemeMode;
        private bool _isApplyingBaseTheme;
        private bool _isFullScreen;
        private bool _titleBarVisible;

        public MainWindowViewModel(
            IEnumerable<NavigationPageViewModel> pages,
            PageNavigation navigation,
            SettingsSession settings,
            IExternalUriLauncher uriLauncher,
            ISukiToastManager toastManager,
            ISukiDialogManager dialogManager,
            IUiToastHost toasts,
            IUiDialogHost dialogs)
        {
            _settings = settings;
            _uriLauncher = uriLauncher;
            _toasts = toasts;
            _dialogs = dialogs;
            // Bound to SukiToastHost / SukiDialogHost in MainWindow.axaml only.
            ToastManager = toastManager;
            DialogManager = dialogManager;
            Pages = new ObservableCollection<NavigationPageViewModel>(
                pages.OrderBy(page => page.Index).ThenBy(page => page.DisplayName));
            _activePage = Pages.FirstOrDefault();

            _theme = SukiTheme.GetInstance();
            Themes = _theme.ColorThemes;
            _baseThemeMode = settings.General.BaseTheme;
            _isFullScreen = settings.General.FullScreen;
            _titleBarVisible = !_isFullScreen;
            // Keep settings in sync without forcing a second chrome write on startup.
            if (settings.General.TitleBarVisible != _titleBarVisible)
                settings.General.TitleBarVisible = _titleBarVisible;
            ApplyBaseTheme(_baseThemeMode);
            RestoreColorTheme();

            CycleBaseThemeCommand = ReactiveCommand.Create(CycleBaseTheme);
            // Color picks only — never wire base light/dark through ChangeColorTheme (double paint).
            ChangeThemeCommand = ReactiveCommand.Create<SukiColorTheme>(ApplyColorTheme);
            CreateCustomThemeCommand = ReactiveCommand.Create(CreateCustomTheme);
            ToggleFullScreenCommand = ReactiveCommand.Create(ToggleFullScreen);
            OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);

            navigation.NavigationRequested += NavigateTo;
            // Do not subscribe OnBaseThemeChanged → ChangeColorTheme.
            // Suki's own ChangeBaseTheme rebinds color after variant flip and is the classic flash.
            // Avalonia ThemeDictionaries (Ec*) already swap on RequestedThemeVariant alone.
            _theme.OnColorThemeChanged += OnColorThemeChanged;
        }

        public event EventHandler<bool>? FullScreenChanged;

        public ObservableCollection<NavigationPageViewModel> Pages { get; }
        public IReadOnlyList<SukiColorTheme> Themes { get; }
        public ISukiDialogManager DialogManager { get; }
        public ISukiToastManager ToastManager { get; }

        public NavigationPageViewModel? ActivePage
        {
            get => _activePage;
            set => this.RaiseAndSetIfChanged(ref _activePage, value);
        }

        public bool TitleBarVisible
        {
            get => _titleBarVisible;
            set
            {
                if (_titleBarVisible == value)
                    return;
                // Paint first — LiveGeneralSettings.Set flushes disk synchronously.
                this.RaiseAndSetIfChanged(ref _titleBarVisible, value);
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (_settings.General.TitleBarVisible != value)
                            _settings.General.TitleBarVisible = value;
                    },
                    DispatcherPriority.Background);
            }
        }

        public bool IsFullScreen
        {
            get => _isFullScreen;
            private set => this.RaiseAndSetIfChanged(ref _isFullScreen, value);
        }

        public ThemeMode BaseThemeMode
        {
            get => _baseThemeMode;
            private set
            {
                if (_baseThemeMode == value)
                    return;
                this.RaiseAndSetIfChanged(ref _baseThemeMode, value);
                this.RaisePropertyChanged(nameof(ThemeToggleIcon));
                this.RaisePropertyChanged(nameof(CurrentThemeModeName));
            }
        }

        public MaterialIconKind ThemeToggleIcon => BaseThemeMode switch
        {
            ThemeMode.Light => MaterialIconKind.WeatherSunny,
            ThemeMode.Dark => MaterialIconKind.WeatherNight,
            _ => MaterialIconKind.ThemeLightDark
        };
        public string CurrentThemeModeName => BaseThemeMode switch
        {
            ThemeMode.Light => Resources.LightMode,
            ThemeMode.Dark => Resources.DarkMode,
            _ => Resources.FollowSystemMode
        };

        public ReactiveCommand<Unit, Unit> CycleBaseThemeCommand { get; }
        public ReactiveCommand<SukiColorTheme, Unit> ChangeThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateCustomThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleFullScreenCommand { get; }
        public ReactiveCommand<string, Unit> OpenUrlCommand { get; }

        private void RestoreColorTheme()
        {
            if (string.IsNullOrWhiteSpace(_settings.General.ColorTheme))
                return;
            var saved = Themes.FirstOrDefault(theme => string.Equals(
                theme.DisplayName,
                _settings.General.ColorTheme,
                StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                _theme.ChangeColorTheme(saved);
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.General.CustomThemePrimaryColor) ||
                string.IsNullOrWhiteSpace(_settings.General.CustomThemeAccentColor))
            {
                return;
            }

            try
            {
                var custom = new SukiColorTheme(
                    _settings.General.ColorTheme,
                    Color.Parse(_settings.General.CustomThemePrimaryColor),
                    Color.Parse(_settings.General.CustomThemeAccentColor));
                _theme.AddColorTheme(custom);
                _theme.ChangeColorTheme(custom);
            }
            catch (FormatException)
            {
                // Manually edited invalid colors fall back to SukiUI's active theme.
            }
        }

        private void NavigateTo(Type pageType, object? context)
        {
            var page = Pages.FirstOrDefault(candidate => candidate.GetType() == pageType);
            if (page is null)
                return;
            ActivePage = page;
            if (page is SettingViewModel settings && context is SettingsPane pane)
                settings.OpenPane(pane);
        }


        private void CycleBaseTheme() => ChangeBaseTheme(BaseThemeMode switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System
        });

        private void ChangeBaseTheme(ThemeMode mode)
        {
            if (BaseThemeMode == mode)
                return;

            // Paint first, persist after the frame — FlushSection rebuilds the whole bundle + disk.
            BaseThemeMode = mode;
            ApplyBaseTheme(mode);
            var modeToSave = mode;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (BaseThemeMode != modeToSave
                        || _settings.General.BaseTheme == modeToSave)
                        return;
                    _settings.General.BaseTheme = modeToSave;
                },
                DispatcherPriority.ApplicationIdle);
        }

        private void ApplyBaseTheme(ThemeMode mode)
        {
            var application = Application.Current
                ?? throw new InvalidOperationException("Avalonia application is not initialized.");

            var target = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

            // Single paint: only RequestedThemeVariant.
            // Never call SukiTheme.ChangeBaseTheme / ChangeColorTheme here — both re-walk
            // primary/accent opacities (SetColorWithOpacities) and produce a second full-tree
            // invalidation that reads as 闪屏.
            if (Equals(application.RequestedThemeVariant, target))
                return;

            _isApplyingBaseTheme = true;
            application.RequestedThemeVariant = target;
            // Hold the guard past ActualThemeVariantChanged + layout so nothing re-enters color rebind.
            Dispatcher.UIThread.Post(
                () => _isApplyingBaseTheme = false,
                DispatcherPriority.Loaded);
        }

        private void ApplyColorTheme(SukiColorTheme theme)
        {
            if (ReferenceEquals(_theme.ActiveColorTheme, theme))
                return;
            _theme.ChangeColorTheme(theme);
        }

        private void OnColorThemeChanged(SukiColorTheme theme)
        {
            if (_isApplyingBaseTheme)
                return;
            _settings.General.ColorTheme = theme.DisplayName;
            // Color picks are deliberate; keep feedback. Base light/dark cycles stay silent.
            _toasts.Show(
                Resources.ColorChangedTitle,
                $"{Resources.ColorChangedContent} {theme.DisplayName}.");
        }

        private void CreateCustomTheme() => _dialogs.ShowContent(new UiContentDialogOptions
        {
            CreateContent = session => new CustomThemeDialogViewModel(_theme, session, _settings.General)
        });

        private void ToggleFullScreen()
        {
            var next = !IsFullScreen;
            // Order: local state → window state event → deferred settings flush.
            // Sync settings flush + badge refresh on this path caused hitch/twitch.
            IsFullScreen = next;
            TitleBarVisible = !next;
            FullScreenChanged?.Invoke(this, next);
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_settings.General.FullScreen != next)
                        _settings.General.FullScreen = next;
                },
                DispatcherPriority.Background);
        }

        private void OpenUrl(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                _uriLauncher.Open(uri);
        }
    }
}

namespace EasyChat.Presentation.Features.Shell
{
    public sealed class HomeHealthItem
    {
        public HomeHealthItem(
            string title,
            string description,
            bool isDone,
            MaterialIconKind icon,
            string actionText,
            ReactiveCommand<Unit, Unit> actionCommand)
        {
            Title = title;
            Description = description;
            IsDone = isDone;
            Icon = icon;
            ActionText = actionText;
            ActionCommand = actionCommand;
        }

        public string Title { get; }
        public string Description { get; }
        public bool IsDone { get; }
        public bool NeedsAction => !IsDone;
        public MaterialIconKind Icon { get; }
        public string ActionText { get; }
        public ReactiveCommand<Unit, Unit> ActionCommand { get; }
        public EcStatusKind StatusKind => IsDone ? EcStatusKind.Success : EcStatusKind.Warning;
        public string StatusText => IsDone ? Resources.HomeStatusReady : Resources.HomeStatusNeedsSetup;
    }

    public sealed class HomeQuickAction(
        string title,
        string description,
        MaterialIconKind icon,
        ReactiveCommand<Unit, Unit> command)
    {
        public string Title { get; } = title;
        public string Description { get; } = description;
        public MaterialIconKind Icon { get; } = icon;
        public ReactiveCommand<Unit, Unit> Command { get; } = command;
    }

    public sealed class HomeViewModel : NavigationPageViewModel
    {
        private readonly IApplicationUpdateService _updates;
        private readonly PageNavigation _navigation;
        private readonly SettingsSession _settings;
        private string _latestVersion = "-";
        private IReadOnlyList<HomeHealthItem> _healthItems = [];

        public HomeViewModel(
            SettingsSession settings,
            TranslationLanguageOptions languages,
            IApplicationUpdateService updates,
            PageNavigation navigation)
            : base(Resources.Home, MaterialIconKind.Home)
        {
            _updates = updates;
            _navigation = navigation;
            _settings = settings;
            GeneralConfig = settings.General;
            ConfiguredModels = settings.AiModel.ConfiguredModels;
            AvailableLanguages = languages.All;
            GeneralConfig.PropertyChanged += OnGeneralPropertyChanged;
            ConfiguredModels.CollectionChanged += (_, _) => RaiseDashboardProperties();
            settings.Shortcut.Entries.CollectionChanged += (_, _) => RaiseDashboardProperties();
            NavigateToSettingsCommand = ReactiveCommand.Create(() =>
                _navigation.NavigateTo<SettingViewModel>(SettingsPane.Translation));
            NavigateToShortcutsCommand = ReactiveCommand.Create(() =>
                _navigation.NavigateTo<EasyChat.Presentation.Features.Shortcuts.ShortcutViewModel>());
            NavigateToSpeechCommand = ReactiveCommand.Create(() =>
                _navigation.NavigateTo<EasyChat.Presentation.Features.Speech.SpeechRecognitionViewModel>());
            NavigateToTextAssistCommand = ReactiveCommand.Create(() =>
                _navigation.NavigateTo<EasyChat.Presentation.Features.TextAssist.TextAssistViewModel>());
            NavigateToPromptsCommand = ReactiveCommand.Create(() =>
                _navigation.NavigateTo<EasyChat.Presentation.Features.Settings.Prompts.PromptViewModel>());
            OpenEngineSettingsCommand = ReactiveCommand.Create(() =>
                _navigation.NavigateTo<SettingViewModel>(SettingsPane.Translation));
            NavigateToAboutCommand = ReactiveCommand.Create(() =>
                _navigation.NavigateTo<AboutViewModel>());
            SwapLanguagesCommand = ReactiveCommand.Create(SwapLanguages);
            DismissOnboardingCommand = ReactiveCommand.Create(() =>
            {
                GeneralConfig.HomeOnboardingDismissed = true;
                this.RaisePropertyChanged(nameof(ShowOnboarding));
            });
            QuickActions =
            [
                new HomeQuickAction(
                    Resources.Page_SpeechRecognition,
                    Resources.HomeQuickSpeechHint,
                    MaterialIconKind.Microphone,
                    NavigateToSpeechCommand),
                new HomeQuickAction(
                    Resources.TextAssist,
                    Resources.HomeQuickTextAssistHint,
                    MaterialIconKind.Translate,
                    NavigateToTextAssistCommand),
                new HomeQuickAction(
                    Resources.Shortcut,
                    Resources.HomeQuickShortcutHint,
                    MaterialIconKind.Keyboard,
                    NavigateToShortcutsCommand),
                new HomeQuickAction(
                    Resources.Prompts,
                    Resources.HomeQuickPromptHint,
                    MaterialIconKind.TextBox,
                    NavigateToPromptsCommand)
            ];
            RefreshHealthItems();
            _ = CheckForUpdateAsync();
        }

        public LiveGeneralSettings GeneralConfig { get; }
        public ObservableCollection<CustomAiModelState> ConfiguredModels { get; }
        public IReadOnlyList<string> MachineTransProviders { get; } = ["Baidu", "Tencent", "Google", "DeepL"];
        public IReadOnlyList<LanguageSettings> AvailableLanguages { get; }
        public LanguageSettings SelectedSourceLanguage
        {
            get => ResolveLanguage(GeneralConfig.SourceLanguage.Id);
            set
            {
                if (value is not null && value.Id != GeneralConfig.SourceLanguage.Id)
                    GeneralConfig.SourceLanguage = value;
            }
        }

        public LanguageSettings SelectedTargetLanguage
        {
            get => ResolveLanguage(GeneralConfig.TargetLanguage.Id);
            set
            {
                if (value is not null && value.Id != GeneralConfig.TargetLanguage.Id)
                    GeneralConfig.TargetLanguage = value;
            }
        }

        public string CurrentVersion => _updates.CurrentVersion;
        public string LatestVersion { get => _latestVersion; private set => this.RaiseAndSetIfChanged(ref _latestVersion, value); }

        public int ConfiguredModelCount => ConfiguredModels.Count;
        public int ShortcutCount => _settings.Shortcut.Entries.Count;
        public bool IsUsingAiEngine =>
            string.Equals(GeneralConfig.TransEngine, "AiModel", StringComparison.OrdinalIgnoreCase);
        public bool IsEngineReady => IsUsingAiEngine
            ? ConfiguredModels.Count > 0 && !string.IsNullOrWhiteSpace(GeneralConfig.UsingAiModelId)
            : !string.IsNullOrWhiteSpace(GeneralConfig.UsingMachineTrans);
        public bool NeedsConfiguration => !IsEngineReady;
        public string EngineStatusText => IsEngineReady ? Resources.HomeStatusReady : Resources.HomeStatusNeedsSetup;
        public EcStatusKind EngineStatusKind => IsEngineReady ? EcStatusKind.Success : EcStatusKind.Warning;
        public string EngineSummaryText => IsUsingAiEngine
            ? (ConfiguredModels.FirstOrDefault(model => model.Id == GeneralConfig.UsingAiModelId)?.Name
               ?? Resources.NotSet)
            : (GeneralConfig.UsingMachineTrans ?? Resources.NotSet);
        public string EngineKindText => IsUsingAiEngine ? Resources.AIEngine : Resources.MachineTranslation;
        public string CapabilitySummaryText =>
            string.Format(Resources.HomeCapabilitySummary, ConfiguredModelCount, ShortcutCount);
        public string SourceLanguageDisplay => DisplayLanguage(SelectedSourceLanguage);
        public string TargetLanguageDisplay => DisplayLanguage(SelectedTargetLanguage);
        public string LanguagePairDisplay => $"{SourceLanguageDisplay}  →  {TargetLanguageDisplay}";

        public bool HasIncompleteHealth => HealthItems.Any(item => !item.IsDone);
        public bool ShowOnboarding => !GeneralConfig.HomeOnboardingDismissed && HasIncompleteHealth;
        public IReadOnlyList<HomeHealthItem> HealthItems
        {
            get => _healthItems;
            private set => this.RaiseAndSetIfChanged(ref _healthItems, value);
        }

        public ReactiveCommand<Unit, Unit> NavigateToSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> NavigateToShortcutsCommand { get; }
        public ReactiveCommand<Unit, Unit> NavigateToSpeechCommand { get; }
        public ReactiveCommand<Unit, Unit> NavigateToTextAssistCommand { get; }
        public ReactiveCommand<Unit, Unit> NavigateToPromptsCommand { get; }
        public ReactiveCommand<Unit, Unit> NavigateToAboutCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenEngineSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> SwapLanguagesCommand { get; }
        public ReactiveCommand<Unit, Unit> DismissOnboardingCommand { get; }
        public IReadOnlyList<HomeQuickAction> QuickActions { get; }

        private void OnGeneralPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(LiveGeneralSettings.SourceLanguage))
            {
                this.RaisePropertyChanged(nameof(SelectedSourceLanguage));
                this.RaisePropertyChanged(nameof(SourceLanguageDisplay));
                this.RaisePropertyChanged(nameof(LanguagePairDisplay));
            }
            else if (args.PropertyName == nameof(LiveGeneralSettings.TargetLanguage))
            {
                this.RaisePropertyChanged(nameof(SelectedTargetLanguage));
                this.RaisePropertyChanged(nameof(TargetLanguageDisplay));
                this.RaisePropertyChanged(nameof(LanguagePairDisplay));
            }
            else if (args.PropertyName is nameof(LiveGeneralSettings.TransEngine)
                     or nameof(LiveGeneralSettings.UsingAiModelId)
                     or nameof(LiveGeneralSettings.UsingMachineTrans))
                RaiseDashboardProperties();
            else if (args.PropertyName == nameof(LiveGeneralSettings.HomeOnboardingDismissed))
                this.RaisePropertyChanged(nameof(ShowOnboarding));
        }

        private void RaiseDashboardProperties()
        {
            this.RaisePropertyChanged(nameof(ConfiguredModelCount));
            this.RaisePropertyChanged(nameof(ShortcutCount));
            this.RaisePropertyChanged(nameof(IsUsingAiEngine));
            this.RaisePropertyChanged(nameof(IsEngineReady));
            this.RaisePropertyChanged(nameof(NeedsConfiguration));
            this.RaisePropertyChanged(nameof(EngineStatusText));
            this.RaisePropertyChanged(nameof(EngineStatusKind));
            this.RaisePropertyChanged(nameof(EngineSummaryText));
            this.RaisePropertyChanged(nameof(EngineKindText));
            this.RaisePropertyChanged(nameof(CapabilitySummaryText));
            this.RaisePropertyChanged(nameof(SourceLanguageDisplay));
            this.RaisePropertyChanged(nameof(TargetLanguageDisplay));
            this.RaisePropertyChanged(nameof(LanguagePairDisplay));
            RefreshHealthItems();
            this.RaisePropertyChanged(nameof(HasIncompleteHealth));
            this.RaisePropertyChanged(nameof(ShowOnboarding));
        }

        private static string DisplayLanguage(LanguageSettings language) =>
            LanguageDisplayNames.ForUi(language.ChineseName, language.EnglishName);

        private void RefreshHealthItems()
        {
            HealthItems =
            [
                new HomeHealthItem(
                    Resources.HomeHealthEngineTitle,
                    IsEngineReady ? Resources.HomeHealthEngineDone : Resources.HomeHealthEngineTodo,
                    IsEngineReady,
                    MaterialIconKind.Robot,
                    Resources.HomeHealthActionOpenSettings,
                    NavigateToSettingsCommand),
                new HomeHealthItem(
                    Resources.HomeHealthShortcutTitle,
                    ShortcutCount > 0
                        ? string.Format(Resources.HomeHealthShortcutDone, ShortcutCount)
                        : Resources.HomeHealthShortcutTodo,
                    ShortcutCount > 0,
                    MaterialIconKind.Keyboard,
                    Resources.HomeHealthActionOpenShortcuts,
                    NavigateToShortcutsCommand)
            ];
        }

        private void SwapLanguages()
        {
            var source = GeneralConfig.SourceLanguage;
            GeneralConfig.SourceLanguage = GeneralConfig.TargetLanguage;
            GeneralConfig.TargetLanguage = source;
            this.RaisePropertyChanged(nameof(SelectedSourceLanguage));
            this.RaisePropertyChanged(nameof(SelectedTargetLanguage));
            this.RaisePropertyChanged(nameof(SourceLanguageDisplay));
            this.RaisePropertyChanged(nameof(TargetLanguageDisplay));
            this.RaisePropertyChanged(nameof(LanguagePairDisplay));
        }

        private LanguageSettings ResolveLanguage(string id) =>
            AvailableLanguages.FirstOrDefault(language => language.Id == id)
            ?? AvailableLanguages[0];

        private async Task CheckForUpdateAsync()
        {
            var result = await _updates.CheckAsync();
            LatestVersion = result.IsSuccess ? result.Value.LatestVersion : "Error";
        }
    }

    public sealed class AboutViewModel : NavigationPageViewModel
    {
        private readonly IExternalUriLauncher _uriLauncher;

        public AboutViewModel(
            IApplicationUpdateService updates,
            IExternalUriLauncher uriLauncher)
            : base(Resources.About, MaterialIconKind.InformationOutline, 10)
        {
            ArgumentNullException.ThrowIfNull(updates);
            _uriLauncher = uriLauncher ?? throw new ArgumentNullException(nameof(uriLauncher));
            Version = updates.CurrentVersion;
            OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);
        }

        public string Version { get; }
        public ReactiveCommand<string, Unit> OpenUrlCommand { get; }

        private void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return;
            _uriLauncher.Open(uri);
        }
    }
}

namespace EasyChat.Presentation.Features.Shell
{
    public sealed class CloseBehaviorDialogViewModel : ConventionViewModelBase
    {
        private readonly IUiDialogSession _dialog;
        private readonly LiveGeneralSettings _settings;
        private readonly Action _ensureTrayVisible;
        private readonly Action _minimize;
        private readonly Action _exit;
        private bool _isRemember;

        public CloseBehaviorDialogViewModel(
            IUiDialogSession dialog,
            LiveGeneralSettings settings,
            Action ensureTrayVisible,
            Action minimize,
            Action exit)
        {
            _dialog = dialog;
            _settings = settings;
            _ensureTrayVisible = ensureTrayVisible
                ?? throw new ArgumentNullException(nameof(ensureTrayVisible));
            _minimize = minimize;
            _exit = exit;
            MinimizeCommand = ReactiveCommand.Create(Minimize);
            ExitAppCommand = ReactiveCommand.Create(Exit);
            // Close was already cancelled on the window; cancel only dismisses the prompt.
            CancelCommand = ReactiveCommand.Create(() => _dialog.Dismiss());
        }

        public bool IsRemember { get => _isRemember; set => this.RaiseAndSetIfChanged(ref _isRemember, value); }
        public ReactiveCommand<Unit, Unit> MinimizeCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitAppCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        private void Minimize()
        {
            if (IsRemember)
                _settings.ClosingBehavior = ClosingBehavior.MinimizeToTray;
            _ensureTrayVisible();
            _minimize();
            _dialog.Dismiss();
        }

        private void Exit()
        {
            if (IsRemember)
                _settings.ClosingBehavior = ClosingBehavior.ExitApp;
            _exit();
            _dialog.Dismiss();
        }
    }
}
