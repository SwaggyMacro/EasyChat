using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Foundation.Translation;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Settings.State;

public sealed class LiveSpeechRecognitionSettings : LiveSettingsSection
{
    private string _recognitionLanguage = string.Empty;
    private bool _isTranslationEnabled;
    private bool _isTranslatedSpeechEnabled;
    private bool _isRealTimePreviewEnabled;
    private string _targetLanguage = string.Empty;
    private string _engineId = string.Empty;
    private int _engineType;
    private string? _promptId;
    private IReadOnlyList<string> _translatedSpeechOutputDeviceNames = [];
    private IReadOnlyList<string> _audioOutputDeviceNames = [];
    private SpeechTranslationConfiguration _audioTranslationConfiguration;
    private SpeechTranslationConfiguration _realtimeInterpretationConfiguration;
    private SpeechTranslationMode _activeMode;
    private int _maxSentencesPerLine;
    private FloatingDisplayMode _floatingDisplayMode;
    private int _maxFloatingHistory;
    private int _autoClearInterval;
    private SubtitleSource _mainSubtitleSource;
    private double _primaryFontSize;
    private string _primaryFontFamily;
    private string _primaryFontColor;
    private SubtitleSource _secondarySubtitleSource;
    private double _secondaryFontSize;
    private string _secondaryFontFamily;
    private string _secondaryFontColor;
    private string _backgroundColor;
    private string _subtitleBackgroundColor;
    private double _windowOpacity;
    private bool _isFloatingWindowLocked;
    private string _floatingWindowOrientation;
    private double _windowX;
    private double _windowY;
    private double _windowWidth;
    private double _windowHeight;

    public LiveSpeechRecognitionSettings(
        SpeechRecognitionSettings value,
        Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.SpeechRecognition, commit)
    {
        _audioTranslationConfiguration = value.AudioTranslationConfiguration
            ?? ToModeConfiguration(value);
        _realtimeInterpretationConfiguration = value.RealtimeInterpretationConfiguration
            ?? _audioTranslationConfiguration with
            {
                IsTranslationEnabled = true,
                IsTranslatedSpeechEnabled = true
            };
        _activeMode = SpeechTranslationMode.AudioTranslation;
        ApplyModeConfiguration(_audioTranslationConfiguration, raiseChanges: false);
        _maxSentencesPerLine = value.MaxSentencesPerLine;
        _floatingDisplayMode = value.FloatingDisplayMode;
        _maxFloatingHistory = value.MaxFloatingHistory;
        _autoClearInterval = value.AutoClearInterval;
        _mainSubtitleSource = value.MainSubtitleSource;
        _primaryFontSize = value.PrimaryFontSize;
        _primaryFontFamily = value.PrimaryFontFamily;
        _primaryFontColor = value.PrimaryFontColor;
        _secondarySubtitleSource = value.SecondarySubtitleSource;
        _secondaryFontSize = value.SecondaryFontSize;
        _secondaryFontFamily = value.SecondaryFontFamily;
        _secondaryFontColor = value.SecondaryFontColor;
        _backgroundColor = value.BackgroundColor;
        _subtitleBackgroundColor = value.SubtitleBackgroundColor;
        _windowOpacity = value.WindowOpacity;
        _isFloatingWindowLocked = value.IsFloatingWindowLocked;
        _floatingWindowOrientation = value.FloatingWindowOrientation;
        _windowX = value.WindowX;
        _windowY = value.WindowY;
        _windowWidth = value.WindowWidth;
        _windowHeight = value.WindowHeight;
    }

    public string RecognitionLanguage { get => _recognitionLanguage; set => Set(ref _recognitionLanguage, value); }
    public bool IsTranslationEnabled { get => _isTranslationEnabled; set => Set(ref _isTranslationEnabled, value); }
    public bool IsTranslatedSpeechEnabled { get => _isTranslatedSpeechEnabled; set => Set(ref _isTranslatedSpeechEnabled, value); }
    public bool IsRealTimePreviewEnabled { get => _isRealTimePreviewEnabled; set => Set(ref _isRealTimePreviewEnabled, value); }
    public string TargetLanguage { get => _targetLanguage; set => Set(ref _targetLanguage, value); }
    public string EngineId { get => _engineId; set => Set(ref _engineId, value); }
    public int EngineType { get => _engineType; set => Set(ref _engineType, value); }
    public string? PromptId { get => _promptId; set => Set(ref _promptId, value); }
    public IReadOnlyList<string> TranslatedSpeechOutputDeviceNames
    {
        get => _translatedSpeechOutputDeviceNames;
        set => SetOutputDeviceNames(ref _translatedSpeechOutputDeviceNames, value);
    }

    public IReadOnlyList<string> AudioOutputDeviceNames
    {
        get => _audioOutputDeviceNames;
        set => SetOutputDeviceNames(ref _audioOutputDeviceNames, value);
    }
    public SpeechTranslationMode ActiveMode => _activeMode;
    public int MaxSentencesPerLine { get => _maxSentencesPerLine; set => Set(ref _maxSentencesPerLine, value); }
    public FloatingDisplayMode FloatingDisplayMode { get => _floatingDisplayMode; set => Set(ref _floatingDisplayMode, value); }
    public int MaxFloatingHistory { get => _maxFloatingHistory; set => Set(ref _maxFloatingHistory, value); }
    public int AutoClearInterval { get => _autoClearInterval; set => Set(ref _autoClearInterval, value); }
    public SubtitleSource MainSubtitleSource { get => _mainSubtitleSource; set => Set(ref _mainSubtitleSource, value); }
    public double PrimaryFontSize { get => _primaryFontSize; set => Set(ref _primaryFontSize, value); }
    public string PrimaryFontFamily { get => _primaryFontFamily; set => Set(ref _primaryFontFamily, value); }
    public string PrimaryFontColor { get => _primaryFontColor; set => Set(ref _primaryFontColor, value); }
    public SubtitleSource SecondarySubtitleSource { get => _secondarySubtitleSource; set => Set(ref _secondarySubtitleSource, value); }
    public double SecondaryFontSize { get => _secondaryFontSize; set => Set(ref _secondaryFontSize, value); }
    public string SecondaryFontFamily { get => _secondaryFontFamily; set => Set(ref _secondaryFontFamily, value); }
    public string SecondaryFontColor { get => _secondaryFontColor; set => Set(ref _secondaryFontColor, value); }
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public string SubtitleBackgroundColor { get => _subtitleBackgroundColor; set => Set(ref _subtitleBackgroundColor, value); }
    public double WindowOpacity { get => _windowOpacity; set => Set(ref _windowOpacity, value); }
    public bool IsFloatingWindowLocked { get => _isFloatingWindowLocked; set => Set(ref _isFloatingWindowLocked, value); }
    public string FloatingWindowOrientation { get => _floatingWindowOrientation; set => Set(ref _floatingWindowOrientation, value); }
    public double WindowX { get => _windowX; set => Set(ref _windowX, value); }
    public double WindowY { get => _windowY; set => Set(ref _windowY, value); }
    public double WindowWidth { get => _windowWidth; set => Set(ref _windowWidth, value); }
    public double WindowHeight { get => _windowHeight; set => Set(ref _windowHeight, value); }

    public void SetActiveMode(SpeechTranslationMode mode)
    {
        if (_activeMode == mode)
            return;

        StoreActiveModeConfiguration();
        _activeMode = mode;
        ApplyModeConfiguration(GetModeConfiguration(mode), raiseChanges: true);
        Commit();
    }

    public SpeechRecognitionSettings ToContract() => ToContractForMode(_activeMode);

    public SpeechRecognitionSettings ToContractForMode(SpeechTranslationMode mode)
    {
        StoreActiveModeConfiguration();
        var active = GetModeConfiguration(mode);
        return new SpeechRecognitionSettings(
            active.RecognitionLanguage,
            active.IsTranslationEnabled,
            active.IsRealTimePreviewEnabled,
            active.TargetLanguage,
            active.EngineId,
            active.EngineType,
            MaxSentencesPerLine,
            FloatingDisplayMode,
            MaxFloatingHistory,
            AutoClearInterval,
            MainSubtitleSource,
            PrimaryFontSize,
            PrimaryFontFamily,
            PrimaryFontColor,
            SecondarySubtitleSource,
            SecondaryFontSize,
            SecondaryFontFamily,
            SecondaryFontColor,
            BackgroundColor,
            SubtitleBackgroundColor,
            WindowOpacity,
            IsFloatingWindowLocked,
            FloatingWindowOrientation,
            WindowX,
            WindowY,
            WindowWidth,
            WindowHeight,
            active.PromptId,
            active.IsTranslatedSpeechEnabled,
            GetModeConfiguration(SpeechTranslationMode.AudioTranslation),
            GetModeConfiguration(SpeechTranslationMode.RealtimeInterpretation));
    }

    private SpeechTranslationConfiguration GetModeConfiguration(SpeechTranslationMode mode) =>
        mode == SpeechTranslationMode.AudioTranslation
            ? _audioTranslationConfiguration
            : _realtimeInterpretationConfiguration;

    private void StoreActiveModeConfiguration()
    {
        var current = new SpeechTranslationConfiguration(
            _recognitionLanguage,
            _isTranslationEnabled,
            _isTranslatedSpeechEnabled,
            _isRealTimePreviewEnabled,
            _targetLanguage,
            _engineId,
            _engineType,
            _promptId,
            _translatedSpeechOutputDeviceNames,
            _audioOutputDeviceNames);
        if (_activeMode == SpeechTranslationMode.AudioTranslation)
            _audioTranslationConfiguration = current;
        else
            _realtimeInterpretationConfiguration = current;
    }

    private void ApplyModeConfiguration(SpeechTranslationConfiguration configuration, bool raiseChanges)
    {
        _recognitionLanguage = configuration.RecognitionLanguage;
        _isTranslationEnabled = configuration.IsTranslationEnabled;
        _isTranslatedSpeechEnabled = configuration.IsTranslatedSpeechEnabled;
        _isRealTimePreviewEnabled = configuration.IsRealTimePreviewEnabled;
        _targetLanguage = configuration.TargetLanguage;
        _engineId = configuration.EngineId;
        _engineType = configuration.EngineType;
        _promptId = configuration.PromptId;
        _translatedSpeechOutputDeviceNames = configuration.TranslatedSpeechOutputDeviceNames?.ToArray() ?? [];
        _audioOutputDeviceNames = configuration.AudioOutputDeviceNames?.ToArray() ?? [];
        if (!raiseChanges)
            return;

        foreach (var property in new[]
                 {
                     nameof(RecognitionLanguage), nameof(IsTranslationEnabled),
                     nameof(IsTranslatedSpeechEnabled), nameof(IsRealTimePreviewEnabled),
                     nameof(TargetLanguage), nameof(EngineId), nameof(EngineType), nameof(PromptId),
                     nameof(TranslatedSpeechOutputDeviceNames), nameof(AudioOutputDeviceNames)
                 })
            this.RaisePropertyChanged(property);
    }

    private bool SetOutputDeviceNames(ref IReadOnlyList<string> field, IReadOnlyList<string>? value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        var next = value is null
            ? Array.Empty<string>()
            : value
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        if (field.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
            return false;

        field = next;
        this.RaisePropertyChanged(propertyName);
        Commit();
        return true;
    }

    private static SpeechTranslationConfiguration ToModeConfiguration(SpeechRecognitionSettings source) => new(
        source.RecognitionLanguage,
        source.IsTranslationEnabled,
        source.IsTranslatedSpeechEnabled,
        source.IsRealTimePreviewEnabled,
        source.TargetLanguage,
        source.EngineId,
        source.EngineType,
        source.PromptId);
}

public sealed class LiveTextAssistSettings : LiveSettingsSection
{
    private string _sourceLanguageId;
    private string _targetLanguageId;
    private string _provider;
    private string? _aiModelId;
    private string? _translationPromptId;
    private string? _correctionPromptId;
    private string? _polishPromptId;
    private string? _summaryPromptId;
    private bool _detailedExplanation;
    private bool _translationConfigurationExpanded;
    private bool _correctionConfigurationExpanded;
    private string _machineProvider;

    public LiveTextAssistSettings(TextAssistSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.TextAssist, commit)
    {
        _sourceLanguageId = value.SourceLanguageId;
        _targetLanguageId = value.TargetLanguageId;
        _provider = value.Provider;
        _aiModelId = value.AiModelId;
        _translationPromptId = value.TranslationPromptId;
        _correctionPromptId = value.CorrectionPromptId;
        _polishPromptId = value.PolishPromptId;
        _summaryPromptId = value.SummaryPromptId;
        _detailedExplanation = value.DetailedExplanation;
        _translationConfigurationExpanded = value.TranslationConfigurationExpanded;
        _correctionConfigurationExpanded = value.CorrectionConfigurationExpanded;
        _machineProvider = value.MachineProvider;
    }

    public string SourceLanguageId { get => _sourceLanguageId; set => Set(ref _sourceLanguageId, value); }
    public string TargetLanguageId { get => _targetLanguageId; set => Set(ref _targetLanguageId, value); }
    public string Provider { get => _provider; set => Set(ref _provider, value); }
    public string? AiModelId { get => _aiModelId; set => Set(ref _aiModelId, value); }
    public string? TranslationPromptId { get => _translationPromptId; set => Set(ref _translationPromptId, value); }
    public string? CorrectionPromptId { get => _correctionPromptId; set => Set(ref _correctionPromptId, value); }
    public string? PolishPromptId { get => _polishPromptId; set => Set(ref _polishPromptId, value); }
    public string? SummaryPromptId { get => _summaryPromptId; set => Set(ref _summaryPromptId, value); }
    public bool DetailedExplanation { get => _detailedExplanation; set => Set(ref _detailedExplanation, value); }
    public bool TranslationConfigurationExpanded { get => _translationConfigurationExpanded; set => Set(ref _translationConfigurationExpanded, value); }
    public bool CorrectionConfigurationExpanded { get => _correctionConfigurationExpanded; set => Set(ref _correctionConfigurationExpanded, value); }
    public string MachineProvider { get => _machineProvider; set => Set(ref _machineProvider, value); }

    public TextAssistSettings ToContract() => new(
        SourceLanguageId, TargetLanguageId, Provider, AiModelId,
        TranslationPromptId, CorrectionPromptId, PolishPromptId, SummaryPromptId,
        DetailedExplanation, TranslationConfigurationExpanded, CorrectionConfigurationExpanded,
        MachineProvider);
}
