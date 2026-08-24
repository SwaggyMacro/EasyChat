using EasyChat.Contracts.Settings;
using EasyChat.Contracts.ImageTranslation;
using Newtonsoft.Json;

namespace EasyChat.Infrastructure.Settings.Persistence;

internal enum TextAssistShortcutModeDto
{
    Simple = 0,
    Complex = 1
}

internal enum ResultWindowModeDto
{
    Classic = 0,
    Dictionary = 1
}

internal enum ResultReadAloudModeDto
{
    None = 0,
    Source = 1,
    Target = 2,
    Both = 3
}

internal enum InputDeliveryModeDto
{
    Type = 0,
    Paste = 1,
    Message = 2
}

internal enum InputTranslationModeDto
{
    NormalWindow = 0,
    Tsf = 1
}

internal enum OcrRecognitionModeDto
{
    Fast = 0,
    Normal = 1,
    IdleRelease = 2
}

internal enum ImageTextEraseModeDto
{
    Fast = 0,
    Precise = 1
}

internal enum FloatingDisplayModeDto
{
    Segmented = 0,
    AutoScroll = 1
}

internal enum SubtitleSourceDto
{
    None = 0,
    Original = 1,
    Translated = 2
}

internal enum SelectionTriggerModeDto
{
    DoubleClick = 0,
    DragSelection = 1,
    All = 2
}

internal enum SelectionFilterModeDto
{
    Disabled = 0,
    Blacklist = 1,
    Whitelist = 2
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ShortcutSettingsDto
{
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<ShortcutEntrySettingsDto> Entries { get; set; } = [];
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ShortcutEntrySettingsDto
{
    [JsonProperty]
    public string ActionType { get; set; } = "Screenshot";

    [JsonProperty]
    public ShortcutParameterSettingsDto? Parameter { get; set; }

    [JsonProperty]
    public string KeyCombination { get; set; } = string.Empty;

    [JsonProperty]
    public bool IsEnabled { get; set; } = true;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Remark { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ShortcutParameterSettingsDto
{
    [JsonProperty]
    public string Engine { get; set; } = string.Empty;

    [JsonProperty]
    public string? EngineId { get; set; }

    [JsonProperty]
    public LanguageSettingsDto? Source { get; set; }

    [JsonProperty]
    public LanguageSettingsDto? Target { get; set; }

    [JsonProperty]
    public string? Value { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? ReadSelectedText { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? InputTranslateBeforeKey { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? InputTranslateAfterKey { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? ReplaceCurrentInput { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public TextAssistShortcutModeDto? TextAssistMode { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? ShowSelectionToolbar { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class PromptSettingsDto
{
    [JsonProperty]
    public string SelectedPromptId { get; set; } = string.Empty;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<PromptEntrySettingsDto> Entries { get; set; } = [];
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class PromptEntrySettingsDto
{
    [JsonProperty]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty]
    public string Name { get; set; } = string.Empty;

    [JsonProperty]
    public string Content { get; set; } = string.Empty;

    [JsonProperty]
    public bool IsDefault { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ResultSettingsDto
{
    [JsonProperty]
    public int AutoCloseDelay { get; set; } = 5000;

    [JsonProperty]
    public double FontSize { get; set; } = 18;

    [JsonProperty]
    public bool EnableAutoReadDelay { get; set; }

    [JsonProperty]
    public int MsPerChar { get; set; } = 50;

    [JsonProperty]
    public string TransparencyLevel { get; set; } = "Transparent";

    [JsonProperty]
    public string BackgroundColor { get; set; } = "#00000000";

    [JsonProperty]
    public string FontColor { get; set; } = "#FFFFFFFF";

    [JsonProperty]
    public string FontFamily { get; set; } = string.Empty;

    [JsonProperty]
    public string WindowBackgroundColor { get; set; } = "#CC000000";

    [JsonProperty]
    public ResultWindowModeDto ScreenshotResultMode { get; set; } = ResultWindowModeDto.Dictionary;

    [JsonProperty]
    public ResultReadAloudModeDto ReadAloudMode { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class InputSettingsDto
{
    [JsonProperty]
    public string TransparencyLevel { get; set; } = "Transparent";

    [JsonProperty]
    public string BackgroundColor { get; set; } = "#CC000000";

    [JsonProperty]
    public string FontColor { get; set; } = "#FFFFFFFF";

    [JsonProperty]
    public int KeySendDelay { get; set; } = 10;

    [JsonProperty]
    public InputDeliveryModeDto DeliveryMode { get; set; } = InputDeliveryModeDto.Paste;

    [JsonProperty]
    public bool ReverseTranslateLanguage { get; set; } = true;

    [JsonProperty]
    public string TypingSourceLanguage { get; set; } = "auto";

    [JsonProperty]
    public string TypingTargetLanguage { get; set; } = "en";

    [JsonProperty]
    public bool FollowGlobalLanguage { get; set; } = true;

    [JsonProperty]
    public InputTranslationModeDto TranslationMode { get; set; } = InputTranslationModeDto.NormalWindow;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ScreenshotSettingsDto
{
    private string? _mode = "Precise";

    [JsonProperty]
    public string? Mode
    {
        get => _mode ?? "Precise";
        set => _mode = value ?? "Quick";
    }

    [JsonProperty]
    public List<FixedAreaSettingsDto> FixedAreas { get; set; } = [];

    [JsonProperty]
    public OcrRecognitionModeDto OcrMode { get; set; } = OcrRecognitionModeDto.Normal;

    [JsonProperty]
    public int OcrIdleTimeoutSeconds { get; set; } = ScreenshotSettings.DefaultOcrIdleTimeoutSeconds;

    [JsonProperty]
    public bool ClosePreviousOcrWindow { get; set; }

    [JsonProperty]
    public ImageTextEraseModeDto ImageTextEraseMode { get; set; } = ImageTextEraseModeDto.Fast;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class FixedAreaSettingsDto
{
    [JsonProperty]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty]
    public string Name { get; set; } = string.Empty;

    [JsonProperty]
    public int X { get; set; }

    [JsonProperty]
    public int Y { get; set; }

    [JsonProperty]
    public int Width { get; set; }

    [JsonProperty]
    public int Height { get; set; }

    [JsonProperty]
    public bool IsEnabled { get; set; } = true;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class SpeechRecognitionSettingsDto
{
    [JsonProperty]
    public string RecognitionLanguage { get; set; } = string.Empty;

    [JsonProperty]
    public bool IsTranslationEnabled { get; set; }

    [JsonProperty]
    public bool IsTranslatedSpeechEnabled { get; set; }

    [JsonProperty]
    public bool IsRealTimePreviewEnabled { get; set; } = true;

    [JsonProperty]
    public string TargetLanguage { get; set; } = string.Empty;

    [JsonProperty]
    public string EngineId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public int EngineType { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? PromptId { get; set; }

    [JsonProperty]
    public bool? FollowGlobalTranslationConfiguration { get; set; }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public SpeechTranslationConfigurationDto? AudioTranslationConfiguration { get; set; }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public SpeechTranslationConfigurationDto? RealtimeInterpretationConfiguration { get; set; }

    [JsonProperty]
    public int MaxSentencesPerLine { get; set; } = 1;

    [JsonProperty]
    public FloatingDisplayModeDto FloatingDisplayMode { get; set; }

    [JsonProperty]
    public int MaxFloatingHistory { get; set; } = 4;

    [JsonProperty]
    public int AutoClearInterval { get; set; }

    [JsonProperty]
    public SubtitleSourceDto MainSubtitleSource { get; set; } = SubtitleSourceDto.Original;

    [JsonProperty]
    public double FontSize { get; set; } = 20;

    [JsonProperty]
    public string FontFamily { get; set; } = "Microsoft YaHei UI";

    [JsonProperty]
    public string FontColor { get; set; } = "#FFFFFFFF";

    [JsonProperty]
    public SubtitleSourceDto SecondarySubtitleSource { get; set; } = SubtitleSourceDto.Translated;

    [JsonProperty]
    public double SecondaryFontSize { get; set; } = 16;

    [JsonProperty]
    public string SecondaryFontFamily { get; set; } = "Microsoft YaHei UI";

    [JsonProperty]
    public string SecondaryFontColor { get; set; } = "#FFCCCCCC";

    [JsonProperty]
    public string BackgroundColor { get; set; } = "#99000000";

    [JsonProperty]
    public string SubtitleBackgroundColor { get; set; } = "#00000000";

    [JsonProperty]
    public double WindowOpacity { get; set; } = 0.8;

    [JsonProperty]
    public bool IsFloatingWindowLocked { get; set; }

    [JsonProperty]
    public string FloatingWindowOrientation { get; set; } = "Horizontal";

    [JsonProperty]
    public double WindowX { get; set; } = -1;

    [JsonProperty]
    public double WindowY { get; set; } = -1;

    [JsonProperty]
    public double WindowWidth { get; set; } = -1;

    [JsonProperty]
    public double WindowHeight { get; set; } = -1;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class SelectionTranslationSettingsDto
{
    [JsonProperty]
    public bool Enabled { get; set; }

    [JsonProperty]
    public string Provider { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? MachineProvider { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? AiModelId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? PromptId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public bool? FollowGlobalTranslationConfiguration { get; set; }

    [JsonProperty]
    public SelectionTriggerModeDto TriggerMode { get; set; } = SelectionTriggerModeDto.All;

    [JsonProperty]
    public bool TranslationEnabled { get; set; } = true;

    [JsonProperty]
    public bool CorrectionEnabled { get; set; } = true;

    [JsonProperty]
    public bool PolishEnabled { get; set; } = true;

    [JsonProperty]
    public bool SummaryEnabled { get; set; } = true;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? ExplanationEnabled { get; set; } = true;

    [JsonProperty]
    public SelectionFilterModeDto FilterMode { get; set; }

    /// <summary>Persisted list entries with stable display metadata (name, description, icon).</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<SelectionAppEntryDto> AppEntries { get; set; } = [];

    /// <summary>Identifier-only list written alongside <see cref="AppEntries"/> so older
    /// builds can still read the list without the richer metadata.</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> AppList { get; set; } = [];
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class SelectionAppEntryDto
{
    [JsonProperty]
    public string Identifier { get; set; } = string.Empty;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? DisplayName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public byte[]? IconPng { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class TtsSettingsDto
{
    [JsonProperty]
    public string Provider { get; set; } = "EdgeTTS";

    [JsonProperty]
    public Dictionary<string, Dictionary<string, string>> ProviderVoicePreferences { get; set; } = new();
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class TextAssistSettingsDto
{
    // Compatibility aggregate flag; current defaults are represented by the option IDs below.
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? FollowGlobal { get; set; }

    [JsonProperty]
    public string SourceLanguageId { get; set; } = "auto";

    [JsonProperty]
    public string TargetLanguageId { get; set; } = "zh-Hans";

    [JsonProperty]
    public string Provider { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? AiModelId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? TranslationPromptId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? CorrectionPromptId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? PolishPromptId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public string? SummaryPromptId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public bool DetailedExplanation { get; set; }

    [JsonProperty]
    public bool TranslationConfigurationExpanded { get; set; } = true;

    [JsonProperty]
    public bool CorrectionConfigurationExpanded { get; set; } = true;

    [JsonProperty]
    public string MachineProvider { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class SpeechTranslationConfigurationDto
{
    [JsonProperty]
    public string RecognitionLanguage { get; set; } = string.Empty;

    [JsonProperty]
    public bool IsTranslationEnabled { get; set; }

    [JsonProperty]
    public bool IsTranslatedSpeechEnabled { get; set; }

    [JsonProperty]
    public bool IsRealTimePreviewEnabled { get; set; } = true;

    [JsonProperty]
    public string TargetLanguage { get; set; } = string.Empty;

    [JsonProperty]
    public string EngineId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;

    [JsonProperty]
    public int EngineType { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? PromptId { get; set; } = TranslationConfigurationOptionIds.FollowGlobal;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class OcrSettingsDto
{
    [JsonProperty]
    public bool UseProxy { get; set; }
}
