using System.Collections.ObjectModel;
using System.Collections.Specialized;
using EasyChat.Contracts.Settings;

namespace EasyChat.Presentation.Features.Settings.State;

public sealed class LiveGeneralSettings : LiveSettingsSection
{
    private LanguageSettings _sourceLanguage;
    private LanguageSettings _targetLanguage;
    private string? _displayLanguage;
    private LanguageSettings? _nativeLanguage;
    private ClosingBehavior _closingBehavior;
    private string? _transEngine;
    private string? _usingAiModel;
    private string? _usingAiModelId;
    private string? _usingMachineTransId;
    private string? _usingMachineTrans;
    private ThemeMode _baseTheme;
    private string? _colorTheme;
    private string? _customThemePrimaryColor;
    private string? _customThemeAccentColor;
    private bool _titleBarVisible;
    private bool _fullScreen;
    private bool _homeOnboardingDismissed;

    public LiveGeneralSettings(GeneralSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.General, commit)
    {
        _sourceLanguage = value.SourceLanguage;
        _targetLanguage = value.TargetLanguage;
        _displayLanguage = value.DisplayLanguage;
        _nativeLanguage = value.NativeLanguage;
        _closingBehavior = value.ClosingBehavior;
        _transEngine = value.TranslationEngine;
        _usingAiModel = value.AiModel;
        _usingAiModelId = value.AiModelId;
        _usingMachineTransId = value.MachineTranslationId;
        _usingMachineTrans = value.MachineTranslation;
        _baseTheme = value.BaseTheme;
        _colorTheme = value.ColorTheme;
        _customThemePrimaryColor = value.CustomThemePrimaryColor;
        _customThemeAccentColor = value.CustomThemeAccentColor;
        _titleBarVisible = value.TitleBarVisible;
        _fullScreen = value.FullScreen;
        _homeOnboardingDismissed = value.HomeOnboardingDismissed;
    }

    public LanguageSettings SourceLanguage { get => _sourceLanguage; set => Set(ref _sourceLanguage, value); }
    public LanguageSettings TargetLanguage { get => _targetLanguage; set => Set(ref _targetLanguage, value); }
    public string? DisplayLanguage { get => _displayLanguage; set => Set(ref _displayLanguage, value); }
    public LanguageSettings? NativeLanguage { get => _nativeLanguage; set => Set(ref _nativeLanguage, value); }
    public ClosingBehavior ClosingBehavior { get => _closingBehavior; set => Set(ref _closingBehavior, value); }
    public string? TransEngine { get => _transEngine; set => Set(ref _transEngine, value); }
    public string? UsingAiModel { get => _usingAiModel; set => Set(ref _usingAiModel, value); }
    public string? UsingAiModelId { get => _usingAiModelId; set => Set(ref _usingAiModelId, value); }
    public string? UsingMachineTransId { get => _usingMachineTransId; set => Set(ref _usingMachineTransId, value); }
    public string? UsingMachineTrans { get => _usingMachineTrans; set => Set(ref _usingMachineTrans, value); }
    public ThemeMode BaseTheme { get => _baseTheme; set => Set(ref _baseTheme, value); }
    public string? ColorTheme { get => _colorTheme; set => Set(ref _colorTheme, value); }
    public string? CustomThemePrimaryColor { get => _customThemePrimaryColor; set => Set(ref _customThemePrimaryColor, value); }
    public string? CustomThemeAccentColor { get => _customThemeAccentColor; set => Set(ref _customThemeAccentColor, value); }
    public bool TitleBarVisible { get => _titleBarVisible; set => Set(ref _titleBarVisible, value); }
    public bool FullScreen { get => _fullScreen; set => Set(ref _fullScreen, value); }
    public bool HomeOnboardingDismissed
    {
        get => _homeOnboardingDismissed;
        set => Set(ref _homeOnboardingDismissed, value);
    }

    public GeneralSettings ToContract() => new(
        SourceLanguage, TargetLanguage, DisplayLanguage, NativeLanguage, ClosingBehavior,
        TransEngine, UsingAiModel, UsingAiModelId, UsingMachineTransId, UsingMachineTrans,
        BaseTheme, ColorTheme, CustomThemePrimaryColor, CustomThemeAccentColor,
        TitleBarVisible, FullScreen, HomeOnboardingDismissed);
}

public sealed class LiveProxySettings : LiveSettingsSection
{
    private string _proxyUrl;

    public LiveProxySettings(ProxySettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Proxy, commit) => _proxyUrl = value.ProxyUrl;

    public string ProxyUrl { get => _proxyUrl; set => Set(ref _proxyUrl, value); }
    public ProxySettings ToContract() => new(ProxyUrl);
}

public sealed class LiveOcrSettings : LiveSettingsSection
{
    private bool _useProxy;

    public LiveOcrSettings(OcrSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Ocr, commit) => _useProxy = value.UseProxy;

    public bool UseProxy { get => _useProxy; set => Set(ref _useProxy, value); }
    public OcrSettings ToContract() => new(UseProxy);
}

public sealed class LiveInputSettings : LiveSettingsSection
{
    private string _transparencyLevel;
    private string _backgroundColor;
    private string _fontColor;
    private int _keySendDelay;
    private InputDeliveryMode _deliveryMode;
    private bool _reverseTranslateLanguage;
    private string _typingSourceLanguage;
    private string _typingTargetLanguage;
    private bool _followGlobalLanguage;

    public LiveInputSettings(InputSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Input, commit)
    {
        _transparencyLevel = value.TransparencyLevel;
        _backgroundColor = value.BackgroundColor;
        _fontColor = value.FontColor;
        _keySendDelay = value.KeySendDelay;
        _deliveryMode = value.DeliveryMode;
        _reverseTranslateLanguage = value.ReverseTranslateLanguage;
        _typingSourceLanguage = value.TypingSourceLanguage;
        _typingTargetLanguage = value.TypingTargetLanguage;
        _followGlobalLanguage = value.FollowGlobalLanguage;
    }

    public string TransparencyLevel { get => _transparencyLevel; set => Set(ref _transparencyLevel, value); }
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public string FontColor { get => _fontColor; set => Set(ref _fontColor, value); }
    public int KeySendDelay { get => _keySendDelay; set => Set(ref _keySendDelay, value); }
    public InputDeliveryMode DeliveryMode { get => _deliveryMode; set => Set(ref _deliveryMode, value); }
    public bool ReverseTranslateLanguage { get => _reverseTranslateLanguage; set => Set(ref _reverseTranslateLanguage, value); }
    public string TypingSourceLanguage { get => _typingSourceLanguage; set => Set(ref _typingSourceLanguage, value); }
    public string TypingTargetLanguage { get => _typingTargetLanguage; set => Set(ref _typingTargetLanguage, value); }
    public bool FollowGlobalLanguage { get => _followGlobalLanguage; set => Set(ref _followGlobalLanguage, value); }

    public InputSettings ToContract() => new(
        TransparencyLevel, BackgroundColor, FontColor, KeySendDelay, DeliveryMode,
        ReverseTranslateLanguage, TypingSourceLanguage, TypingTargetLanguage, FollowGlobalLanguage);
}

public sealed class LiveResultSettings : LiveSettingsSection
{
    private int _autoCloseDelay;
    private double _fontSize;
    private bool _enableAutoReadDelay;
    private int _millisecondsPerCharacter;
    private string _transparencyLevel;
    private string _backgroundColor;
    private string _fontColor;
    private string _fontFamily;
    private string _windowBackgroundColor;
    private ResultWindowMode _screenshotResultMode;
    private ResultReadAloudMode _readAloudMode;

    public LiveResultSettings(ResultSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Result, commit)
    {
        _autoCloseDelay = value.AutoCloseDelay;
        _fontSize = value.FontSize;
        _enableAutoReadDelay = value.EnableAutoReadDelay;
        _millisecondsPerCharacter = value.MillisecondsPerCharacter;
        _transparencyLevel = value.TransparencyLevel;
        _backgroundColor = value.BackgroundColor;
        _fontColor = value.FontColor;
        _fontFamily = value.FontFamily;
        _windowBackgroundColor = value.WindowBackgroundColor;
        _screenshotResultMode = value.ScreenshotResultMode;
        _readAloudMode = value.ReadAloudMode;
    }

    public int AutoCloseDelay { get => _autoCloseDelay; set => Set(ref _autoCloseDelay, value); }
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public bool EnableAutoReadDelay { get => _enableAutoReadDelay; set => Set(ref _enableAutoReadDelay, value); }
    public int MsPerChar { get => _millisecondsPerCharacter; set => Set(ref _millisecondsPerCharacter, value); }
    public string TransparencyLevel { get => _transparencyLevel; set => Set(ref _transparencyLevel, value); }
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public string FontColor { get => _fontColor; set => Set(ref _fontColor, value); }
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public string WindowBackgroundColor { get => _windowBackgroundColor; set => Set(ref _windowBackgroundColor, value); }
    public ResultWindowMode ScreenshotResultMode { get => _screenshotResultMode; set => Set(ref _screenshotResultMode, value); }
    public ResultReadAloudMode ReadAloudMode { get => _readAloudMode; set => Set(ref _readAloudMode, value); }

    public ResultSettings ToContract() => new(
        AutoCloseDelay, FontSize, EnableAutoReadDelay, MsPerChar, TransparencyLevel,
        BackgroundColor, FontColor, FontFamily, WindowBackgroundColor,
        ScreenshotResultMode, ReadAloudMode);
}

public sealed class FixedAreaState : LiveSettingsSection
{
    private string _name;
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private bool _isEnabled;

    public FixedAreaState(FixedAreaSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Screenshot, commit)
    {
        Id = value.Id;
        _name = value.Name;
        _x = value.X;
        _y = value.Y;
        _width = value.Width;
        _height = value.Height;
        _isEnabled = value.IsEnabled;
    }

    public string Id { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public int X { get => _x; set => Set(ref _x, value); }
    public int Y { get => _y; set => Set(ref _y, value); }
    public int Width { get => _width; set => Set(ref _width, value); }
    public int Height { get => _height; set => Set(ref _height, value); }
    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    public string DisplayInfo => $"X:{X}, Y:{Y}, W:{Width}, H:{Height}";
    public FixedAreaSettings ToContract() => new(Id, Name, X, Y, Width, Height, IsEnabled);
}

public sealed class LiveScreenshotSettings : LiveSettingsSection
{
    private string? _mode;

    public LiveScreenshotSettings(ScreenshotSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Screenshot, commit)
    {
        _mode = value.Mode;
        FixedAreas = new ObservableCollection<FixedAreaState>(
            value.FixedAreas.Select(area => new FixedAreaState(area, commit)));
        FixedAreas.CollectionChanged += OnCollectionChanged;
    }

    public string? Mode { get => _mode; set => Set(ref _mode, value); }
    public ObservableCollection<FixedAreaState> FixedAreas { get; }
    public ScreenshotSettings ToContract() => new(Mode, FixedAreas.Select(area => area.ToContract()).ToArray());

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Commit();
}

public sealed class LiveSelectionTranslationSettings : LiveSettingsSection
{
    private bool _enabled;
    private string _provider;
    private string? _machineProvider;
    private string? _aiModelId;
    private string? _promptId;
    private SelectionTriggerMode _triggerMode;
    private bool _translationEnabled;
    private bool _correctionEnabled;
    private bool _polishEnabled;
    private bool _summaryEnabled;

    public LiveSelectionTranslationSettings(
        SelectionTranslationSettings value,
        Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.SelectionTranslation, commit)
    {
        _enabled = value.Enabled;
        _provider = value.Provider;
        _machineProvider = value.MachineProvider;
        _aiModelId = value.AiModelId;
        _promptId = value.PromptId;
        _triggerMode = value.TriggerMode;
        _translationEnabled = value.TranslationEnabled;
        _correctionEnabled = value.CorrectionEnabled;
        _polishEnabled = value.PolishEnabled;
        _summaryEnabled = value.SummaryEnabled;
    }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Provider { get => _provider; set => Set(ref _provider, value); }
    public string? MachineProvider { get => _machineProvider; set => Set(ref _machineProvider, value); }
    public string? AiModelId { get => _aiModelId; set => Set(ref _aiModelId, value); }
    public string? PromptId { get => _promptId; set => Set(ref _promptId, value); }
    public SelectionTriggerMode TriggerMode { get => _triggerMode; set => Set(ref _triggerMode, value); }
    public bool TranslationEnabled { get => _translationEnabled; set => Set(ref _translationEnabled, value); }
    public bool CorrectionEnabled { get => _correctionEnabled; set => Set(ref _correctionEnabled, value); }
    public bool PolishEnabled { get => _polishEnabled; set => Set(ref _polishEnabled, value); }
    public bool SummaryEnabled { get => _summaryEnabled; set => Set(ref _summaryEnabled, value); }

    public SelectionTranslationSettings ToContract() => new(
        Enabled, Provider, MachineProvider, AiModelId, PromptId, TriggerMode,
        TranslationEnabled, CorrectionEnabled, PolishEnabled, SummaryEnabled);
}
