namespace EasyChat.Contracts.Settings;

public sealed record SettingsBundle(
    GeneralSettings General,
    AiModelSettings AiModel,
    MachineTranslationSettings MachineTranslation,
    ProxySettings Proxy,
    ShortcutSettings Shortcut,
    PromptSettings Prompts,
    ResultSettings Result,
    InputSettings Input,
    ScreenshotSettings Screenshot,
    SpeechRecognitionSettings SpeechRecognition,
    SelectionTranslationSettings SelectionTranslation,
    TtsSettings Tts,
    TextAssistSettings TextAssist,
    OcrSettings Ocr);

public enum ClosingBehavior
{
    Ask = 0,
    ExitApp = 1,
    MinimizeToTray = 2
}

public enum AiModelType
{
    OpenAi = 0,
    Gemini = 1,
    Claude = 2,
    DeepSeek = 3,
    Custom = 4
}

public enum TextAssistShortcutMode
{
    Simple = 0,
    Complex = 1
}

public enum ResultWindowMode
{
    Classic = 0,
    Dictionary = 1
}

public enum ResultReadAloudMode
{
    None = 0,
    Source = 1,
    Target = 2,
    Both = 3
}

public enum InputDeliveryMode
{
    Type = 0,
    Paste = 1,
    Message = 2
}

public enum FloatingDisplayMode
{
    Segmented = 0,
    AutoScroll = 1
}

public enum SubtitleSource
{
    None = 0,
    Original = 1,
    Translated = 2
}

public enum SelectionTriggerMode
{
    DoubleClick = 0,
    DragSelection = 1,
    All = 2
}

public sealed record LanguageSettings(
    string Id,
    string ChineseName,
    string EnglishName,
    string Icon,
    string LocalizedName,
    string DisplayName,
    IReadOnlyDictionary<string, string> ProviderCodes);

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

public sealed record GeneralSettings(
    LanguageSettings SourceLanguage,
    LanguageSettings TargetLanguage,
    string? DisplayLanguage,
    LanguageSettings? NativeLanguage,
    ClosingBehavior ClosingBehavior,
    string? TranslationEngine,
    string? AiModel,
    string? AiModelId,
    string? MachineTranslationId,
    string? MachineTranslation,
    ThemeMode BaseTheme,
    string? ColorTheme,
    string? CustomThemePrimaryColor,
    string? CustomThemeAccentColor,
    bool TitleBarVisible,
    bool FullScreen,
    bool HomeOnboardingDismissed = false);

public sealed record AiModelSettings(
    IReadOnlyList<CustomAiModelSettings> ConfiguredModels);

public sealed record CustomAiModelSettings(
    string Id,
    string Name,
    AiModelType ModelType,
    IReadOnlyList<string> ApiKeys,
    string ApiUrl,
    string Model,
    bool UseProxy,
    bool EnableThinking);

public sealed record MachineTranslationSettings(
    BaiduTranslationSettings Baidu,
    TencentTranslationSettings Tencent,
    GoogleTranslationSettings Google,
    DeepLTranslationSettings DeepL);

public sealed record BaiduTranslationSettings(
    bool UseProxy,
    string Id,
    IReadOnlyList<BaiduCredentialSettings> Items);

public sealed record BaiduCredentialSettings(string AppId, string AppKey);

public sealed record TencentTranslationSettings(
    bool UseProxy,
    string Id,
    IReadOnlyList<TencentCredentialSettings> Items);

public sealed record TencentCredentialSettings(string SecretId, string SecretKey);

public sealed record GoogleTranslationSettings(
    bool UseProxy,
    string Id,
    string Model,
    IReadOnlyList<string> ApiKeys);

public sealed record DeepLTranslationSettings(
    bool UseProxy,
    string Id,
    string ModelType,
    IReadOnlyList<string> ApiKeys);

public sealed record ProxySettings(string ProxyUrl);

public sealed record ShortcutSettings(IReadOnlyList<ShortcutEntrySettings> Entries);

public sealed record ShortcutEntrySettings(
    string ActionType,
    ShortcutParameterSettings? Parameter,
    string KeyCombination,
    bool IsEnabled,
    string? Remark = null);

public sealed record ShortcutParameterSettings(
    string Engine,
    string? EngineId,
    LanguageSettings? Source,
    LanguageSettings? Target,
    string? Value,
    bool? ReadSelectedText,
    string? InputTranslateBeforeKey,
    string? InputTranslateAfterKey,
    bool? ReplaceCurrentInput,
    TextAssistShortcutMode? TextAssistMode,
    bool? ShowSelectionToolbar);

public sealed record PromptSettings(
    string SelectedPromptId,
    IReadOnlyList<PromptEntrySettings> Entries);

public sealed record PromptEntrySettings(
    string Id,
    string Name,
    string Content,
    bool IsDefault);

public sealed record ResultSettings(
    int AutoCloseDelay,
    double FontSize,
    bool EnableAutoReadDelay,
    int MillisecondsPerCharacter,
    string TransparencyLevel,
    string BackgroundColor,
    string FontColor,
    string FontFamily,
    string WindowBackgroundColor,
    ResultWindowMode ScreenshotResultMode,
    ResultReadAloudMode ReadAloudMode);

public sealed record InputSettings(
    string TransparencyLevel,
    string BackgroundColor,
    string FontColor,
    int KeySendDelay,
    InputDeliveryMode DeliveryMode,
    bool ReverseTranslateLanguage,
    string TypingSourceLanguage,
    string TypingTargetLanguage,
    bool FollowGlobalLanguage);

public sealed record ScreenshotSettings(
    string? Mode,
    IReadOnlyList<FixedAreaSettings> FixedAreas);

public sealed record FixedAreaSettings(
    string Id,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsEnabled);

public sealed record SpeechRecognitionSettings(
    string RecognitionLanguage,
    bool IsTranslationEnabled,
    bool IsRealTimePreviewEnabled,
    string TargetLanguage,
    string EngineId,
    int EngineType,
    int MaxSentencesPerLine,
    FloatingDisplayMode FloatingDisplayMode,
    int MaxFloatingHistory,
    int AutoClearInterval,
    SubtitleSource MainSubtitleSource,
    double PrimaryFontSize,
    string PrimaryFontFamily,
    string PrimaryFontColor,
    SubtitleSource SecondarySubtitleSource,
    double SecondaryFontSize,
    string SecondaryFontFamily,
    string SecondaryFontColor,
    string BackgroundColor,
    string SubtitleBackgroundColor,
    double WindowOpacity,
    bool IsFloatingWindowLocked,
    string FloatingWindowOrientation,
    double WindowX,
    double WindowY,
    double WindowWidth,
    double WindowHeight);

public sealed record SelectionTranslationSettings(
    bool Enabled,
    string Provider,
    string? MachineProvider,
    string? AiModelId,
    string? PromptId,
    SelectionTriggerMode TriggerMode,
    bool TranslationEnabled,
    bool CorrectionEnabled,
    bool PolishEnabled,
    bool SummaryEnabled);

public sealed record TtsSettings(
    string Provider,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ProviderVoicePreferences);

public sealed record TextAssistSettings(
    bool FollowGlobal,
    string SourceLanguageId,
    string TargetLanguageId,
    string Provider,
    string? AiModelId,
    string? TranslationPromptId,
    string? CorrectionPromptId,
    string? PolishPromptId,
    string? SummaryPromptId,
    bool DetailedExplanation,
    bool TranslationConfigurationExpanded,
    bool CorrectionConfigurationExpanded,
    string MachineProvider);

public sealed record OcrSettings(bool UseProxy);
