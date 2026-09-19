using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reactive;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Speech;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.Translation;
using Material.Icons;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Speech;

public sealed record SpeechEngineOption(string Name, string Id, bool IsMachine, bool IsGlobal = false)
{
    public object? ImageValue { get; init; }

    public MaterialIconKind Icon => IsGlobal
        ? MaterialIconKind.LinkVariant
        : IsMachine ? MaterialIconKind.Translate : MaterialIconKind.Robot;
}

public sealed class SpeechAudioSourceItem : ReactiveObject, IDisposable
{
    private bool _isSelected;

    public SpeechAudioSourceItem(AudioCaptureSourceDescriptor source, bool isSelected)
    {
        Token = source.Token;
        Kind = source.Kind;
        Name = source.Name;
        DisplayName = source.Kind == AudioCaptureSourceKind.SystemOutput
            ? Resources.Speech_AllSystemAudio
            : source.DisplayName;
        Title = source.Description ?? string.Empty;
        Category = source.Kind == AudioCaptureSourceKind.Microphone
            ? source.IsVirtualCable
                ? Resources.Speech_VirtualMicrophone
                : Resources.Speech_PhysicalMicrophone
            : string.Empty;
        _isSelected = isSelected;
        if (!source.IconPng.IsEmpty)
        {
            using var stream = new MemoryStream(source.IconPng.ToArray());
            AppIcon = new Bitmap(stream);
        }
    }

    public AudioCaptureSourceToken Token { get; }
    public AudioCaptureSourceKind Kind { get; }
    public string Name { get; }
    public string Title { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);
    public Bitmap? AppIcon { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public void Dispose() => AppIcon?.Dispose();
}

public sealed class SpeechPlaybackOutputItem : ReactiveObject
{
    private bool _isSelected;

    public SpeechPlaybackOutputItem(string noOperationLabel)
    {
        IsNoOperation = true;
        DisplayName = noOperationLabel;
    }

    public SpeechPlaybackOutputItem(AudioPlaybackDeviceDescriptor device, bool isSelected)
    {
        Device = device;
        DisplayName = string.IsNullOrWhiteSpace(device.DisplayName)
            ? device.Name
            : device.DisplayName;
        _isSelected = isSelected;
    }

    public AudioPlaybackDeviceDescriptor? Device { get; }
    public string? DeviceName => Device?.Name;
    public string DisplayName { get; }
    public string Description => Device?.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool IsNoOperation { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public sealed class SpeechSubtitleItemViewModel : ReactiveObject
{
    private string _originalText = string.Empty;
    private string _translatedText = string.Empty;
    private string _displayTranslatedText = string.Empty;
    private bool _isTranslating;
    private double _opacity = 1;

    public SpeechSubtitleItemViewModel(SpeechSubtitleLine subtitle)
    {
        Id = subtitle.Id;
        Timestamp = subtitle.Timestamp;
        Origin = subtitle.Origin;
        Update(subtitle);
    }

    public long Id { get; }
    public TimeSpan Timestamp { get; }
    public SpeechSubtitleOrigin Origin { get; }
    public string OriginLabel => Origin == SpeechSubtitleOrigin.RealtimeInterpretation
        ? Resources.Speech_SubtitleOriginInterpretation
        : Resources.Speech_SubtitleOriginAudio;
    public double Opacity { get => _opacity; private set => this.RaiseAndSetIfChanged(ref _opacity, value); }
    public string OriginalText { get => _originalText; private set => this.RaiseAndSetIfChanged(ref _originalText, value); }
    public string TranslatedText { get => _translatedText; private set => this.RaiseAndSetIfChanged(ref _translatedText, value); }
    public bool IsTranslating
    {
        get => _isTranslating;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isTranslating, value);
            this.RaisePropertyChanged(nameof(DisplayTranslatedText));
        }
    }
    public string DisplayTranslatedText
    {
        get => string.IsNullOrEmpty(_displayTranslatedText)
               && IsTranslating
               && !string.IsNullOrWhiteSpace(OriginalText)
               && OriginalText is not ("..." or "\u2026")
            ? Resources.Speech_Translating
            : _displayTranslatedText;
        private set => this.RaiseAndSetIfChanged(ref _displayTranslatedText, value);
    }

    public void Update(SpeechSubtitleLine subtitle)
    {
        OriginalText = subtitle.OriginalText;
        TranslatedText = subtitle.TranslatedText;
        DisplayTranslatedText = subtitle.DisplayTranslatedText;
        IsTranslating = subtitle.IsTranslating;
    }

    internal void BeginFadeOut() => Opacity = 0;

    internal void StopLoading() => IsTranslating = false;
}

public enum SpeechSessionMode
{
    AudioTranslation = 0,
    RealtimeInterpretation = 1
}

internal sealed class SpeechModeSnapshot
{
    public HashSet<AudioCaptureSourceReference> Sources { get; } = [];
}

internal sealed class SpeechSubtitleProjection
{
    private readonly HashSet<long> _removedFloatingSubtitleIds = [];
    private readonly HashSet<long> _retractedSubtitleIds = [];

    public ObservableCollection<SpeechSubtitleItemViewModel> SubtitleItems { get; } = [];
    public ObservableCollection<SpeechSubtitleItemViewModel> FloatingSubtitles { get; } = [];

    public SpeechSubtitleItemViewModel? Update(SpeechSubtitleLine subtitle)
    {
        if (string.IsNullOrEmpty(subtitle.OriginalText))
        {
            _retractedSubtitleIds.Add(subtitle.Id);
            var retracted = SubtitleItems.FirstOrDefault(line => line.Id == subtitle.Id)
                            ?? FloatingSubtitles.FirstOrDefault(line => line.Id == subtitle.Id);
            if (retracted is not null)
                SubtitleItems.Remove(retracted);
            return retracted;
        }

        if (_retractedSubtitleIds.Contains(subtitle.Id))
            return null;

        var item = SubtitleItems.FirstOrDefault(line => line.Id == subtitle.Id);
        if (item is null)
        {
            item = new SpeechSubtitleItemViewModel(subtitle);
            InsertOrdered(SubtitleItems, item);
        }
        else
        {
            item.Update(subtitle);
        }

        if (!_removedFloatingSubtitleIds.Contains(subtitle.Id)
            && !FloatingSubtitles.Contains(item))
        {
            InsertOrdered(FloatingSubtitles, item);
        }

        return item;
    }

    public SpeechSubtitleItemViewModel? BeginFloatingRemoval(long subtitleId)
    {
        if (!_removedFloatingSubtitleIds.Add(subtitleId))
            return null;

        var item = FloatingSubtitles.FirstOrDefault(line => line.Id == subtitleId);
        item?.BeginFadeOut();
        return item;
    }

    public void CompleteFloatingRemoval(SpeechSubtitleItemViewModel item)
    {
        if (_removedFloatingSubtitleIds.Contains(item.Id))
            FloatingSubtitles.Remove(item);
    }

    public void Clear()
    {
        SubtitleItems.Clear();
        FloatingSubtitles.Clear();
    }

    public void StopLoading()
    {
        foreach (var item in SubtitleItems)
            item.StopLoading();
    }

    private static void InsertOrdered(
        ObservableCollection<SpeechSubtitleItemViewModel> items,
        SpeechSubtitleItemViewModel item)
    {
        var index = 0;
        while (index < items.Count && Compare(items[index], item) <= 0)
            index++;
        items.Insert(index, item);
    }

    private static int Compare(
        SpeechSubtitleItemViewModel left,
        SpeechSubtitleItemViewModel right)
    {
        var timestampComparison = left.Timestamp.CompareTo(right.Timestamp);
        return timestampComparison != 0
            ? timestampComparison
            : left.Id.CompareTo(right.Id);
    }
}

public sealed class SpeechRecognitionViewModel : NavigationPageViewModel, IDisposable
{
    private static readonly TimeSpan FloatingSubtitleFadeDuration = TimeSpan.FromMilliseconds(200);

    private readonly SettingsSession _settings;
    private readonly ISpeechRecognitionUseCases _speech;
    private readonly ISpeechRecognitionModelCatalog _models;
    private readonly IAudioCaptureSourceCatalog _audioSources;
    private readonly IAudioPlaybackDeviceCatalog _playbackDevices;
    private readonly IPlatformCapabilities _capabilities;
    private readonly IPlatformAccessUseCases _platformAccess;
    private readonly TranslationLanguageOptions _languages;
    private readonly SubtitleWindowCoordinator _subtitleWindow;
    private readonly ILogger<SpeechRecognitionViewModel> _logger;
    private readonly SpeechInterpretationHotkeyController _hotkeyController;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly SpeechSubtitleProjection _subtitleProjection = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SpeechModeSnapshot[] _modeSnapshots = new SpeechModeSnapshot[2];
    private readonly CancellationTokenSource?[] _recognitionCancellations = new CancellationTokenSource?[2];
    private readonly Task?[] _recognitionTasks = new Task?[2];
    private bool _synchronizingOutputSelection;
    private IReadOnlyList<AudioCaptureSourceDescriptor> _availableAudioSources = [];
    private SpeechRecognitionCommand? _preparedAudioTranslationCommand;
    private SpeechRecognitionCommand? _preparedRealtimeInterpretationCommand;
    private SpeechEngineOption? _selectedEngineOption;
    private LanguageSettings? _selectedTargetLanguage;
    private SpeechRecognitionModel? _selectedRecognitionModel;
    private string _selectedSourcesSummary = Resources.Speech_AllSystemAudio;
    private int _speechModeTabIndex;
    private bool _isSupported;
    private bool _isBusy;
    private bool _isRecording;
    private bool _isAudioTranslationRecording;
    private bool _isAudioTranslationArmed;
    private bool _isRealtimeInterpretationRecording;
    private bool _isRealtimeInterpretationArmed;
    private bool _isVirtualCableAvailable;
    private bool _isVirtualCableCheckSkipped;
    private bool _isCheckingVirtualCable;
    private bool _isFloatingWindowOpen;
#if DEBUG
    // Keep the first debug launch in the missing-driver state so the recovery path can be exercised.
    private bool _debugVirtualCableNeedsManualCheck = true;
#endif
    private int _initialized;
    private long _nextErrorId;

    public SpeechRecognitionViewModel(
        SettingsSession settings,
        ISpeechRecognitionUseCases speech,
        ISpeechRecognitionModelCatalog models,
        IAudioCaptureSourceCatalog audioSources,
        IAudioPlaybackDeviceCatalog playbackDevices,
        IPlatformCapabilities capabilities,
        IPlatformAccessUseCases platformAccess,
        TranslationLanguageOptions languages,
        SubtitleWindowCoordinator subtitleWindow,
        ILogger<SpeechRecognitionViewModel> logger,
        SpeechInterpretationHotkeyController hotkeyController,
        IExternalUriLauncher uriLauncher)
        : base(Resources.Page_LiveTranslate, MaterialIconKind.Microphone, 4)
    {
        _settings = settings;
        _speech = speech;
        _models = models;
        _audioSources = audioSources;
        _playbackDevices = playbackDevices;
        _capabilities = capabilities;
        _platformAccess = platformAccess;
        _languages = languages;
        _subtitleWindow = subtitleWindow;
        _logger = logger;
        _hotkeyController = hotkeyController;
        _uriLauncher = uriLauncher;
        _hotkeyController.Attach(this);
        _speechModeTabIndex = 0;
        _modeSnapshots[0] = new SpeechModeSnapshot();
        _modeSnapshots[1] = new SpeechModeSnapshot();
        settings.SpeechRecognition.SetActiveMode(SpeechTranslationMode.AudioTranslation);

        RecognitionLanguages = [];
        EngineOptions = [];
        TargetLanguages = [];
        PromptEntries = settings.Prompts.Entries;
        if (!UsesGlobalPrompt(settings.SpeechRecognition.PromptId)
            && !PromptEntries.Any(prompt => prompt.Id == settings.SpeechRecognition.PromptId))
        {
            settings.SpeechRecognition.PromptId =
                PromptEntries.FirstOrDefault(prompt => prompt.Id == settings.Prompts.SelectedPromptId)?.Id
                ?? PromptEntries.FirstOrDefault(prompt => prompt.IsDefault)?.Id;
        }
        AvailableFonts = new ObservableCollection<string>(
            FontManager.Current.SystemFonts.Select(font => font.Name).Order(StringComparer.CurrentCulture));
        AudioSources = [];
        VirtualMicrophoneOptions = [];
        AudioOutputOptions = [];
        SubtitleItems = _subtitleProjection.SubtitleItems;
        FloatingSubtitles = _subtitleProjection.FloatingSubtitles;

        LoadEngineOptions();

        ToggleRecordingCommand = ReactiveCommand.CreateFromTask(ToggleRecordingAsync);
        ToggleAudioTranslationCommand = ReactiveCommand.CreateFromTask(
            () => ToggleModeRecordingAsync(SpeechSessionMode.AudioTranslation));
        ToggleRealtimeInterpretationCommand = ReactiveCommand.CreateFromTask(
            () => ToggleInterpretationArmedAsync());
        RefreshSourcesCommand = ReactiveCommand.CreateFromTask(RefreshSourcesAsync);
        RefreshVirtualCableCommand = ReactiveCommand.CreateFromTask(() => RefreshVirtualCableAsync());
        SkipVirtualCableCheckCommand = ReactiveCommand.CreateFromTask(SkipVirtualCableCheckAsync);
        OpenAudioTranslationTutorialCommand = ReactiveCommand.Create(OpenAudioTranslationTutorial);
        OpenInterpretationTutorialCommand = ReactiveCommand.Create(OpenInterpretationTutorial);
        OpenVirtualCableInstallationTutorialCommand = ReactiveCommand.Create(OpenVirtualCableInstallationTutorial);
        ClearHistoryCommand = ReactiveCommand.Create(ClearHistory);
        ToggleFloatingWindowCommand = ReactiveCommand.Create(ToggleFloatingWindow);
        ToggleLockCommand = ReactiveCommand.Create(() =>
        {
            IsFloatingWindowLocked = !IsFloatingWindowLocked;
        });
        UnlockFloatingWindowCommand = ReactiveCommand.Create(() =>
        {
            IsFloatingWindowLocked = false;
        });
        IncreaseFontSizeCommand = ReactiveCommand.Create(() =>
        {
            PrimaryFontSize = Math.Min(100, PrimaryFontSize + 2);
            SecondaryFontSize = Math.Min(100, SecondaryFontSize + 2);
        });
        DecreaseFontSizeCommand = ReactiveCommand.Create(() =>
        {
            PrimaryFontSize = Math.Max(10, PrimaryFontSize - 2);
            SecondaryFontSize = Math.Max(10, SecondaryFontSize - 2);
        });

        _subtitleWindow.VisibilityChanged += OnSubtitleWindowVisibilityChanged;
        _models.ModelsChanged += OnModelsChanged;
        _settings.AiModel.ConfiguredModels.CollectionChanged += (_, _) => LoadEngineOptions();
        _settings.Prompts.Entries.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(PromptOptions));
            this.RaisePropertyChanged(nameof(SelectedPromptOption));
        };
        _settings.General.PropertyChanged += (_, _) =>
        {
            if (_settings.SpeechRecognition.EngineId == TranslationConfigurationOption.FollowGlobalId
                || UsesGlobalPrompt(_settings.SpeechRecognition.PromptId))
                RaiseGlobalTranslationConfigurationProperties();
        };
        _settings.Prompts.PropertyChanged += (_, _) =>
        {
            if (UsesGlobalPrompt(_settings.SpeechRecognition.PromptId))
                this.RaisePropertyChanged(nameof(SelectedPromptId));
        };
    }

    public ObservableCollection<SpeechRecognitionModel> RecognitionLanguages { get; }
    public ObservableCollection<SpeechEngineOption> EngineOptions { get; }
    public ObservableCollection<LanguageSettings> TargetLanguages { get; }
    public ObservableCollection<PromptEntryState> PromptEntries { get; }
    public IEnumerable<TranslationConfigurationOption> PromptOptions =>
        new[] { TranslationConfigurationOption.FollowGlobal(Resources.TextAssistFollowGlobal) }
            .Concat(PromptEntries.Select(prompt =>
                new TranslationConfigurationOption(prompt.Id, prompt.Name, false, MaterialIconKind.TextBox)));
    public ObservableCollection<string> AvailableFonts { get; }
    public ObservableCollection<SpeechAudioSourceItem> AudioSources { get; }
    public ObservableCollection<SpeechPlaybackOutputItem> VirtualMicrophoneOptions { get; }
    public ObservableCollection<SpeechPlaybackOutputItem> AudioOutputOptions { get; }
    public ObservableCollection<SpeechSubtitleItemViewModel> SubtitleItems { get; }
    public ObservableCollection<SpeechSubtitleItemViewModel> FloatingSubtitles { get; }

    public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleAudioTranslationCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleRealtimeInterpretationCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshSourcesCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshVirtualCableCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipVirtualCableCheckCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenAudioTranslationTutorialCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenInterpretationTutorialCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenVirtualCableInstallationTutorialCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearHistoryCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleFloatingWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleLockCommand { get; }
    public ReactiveCommand<Unit, Unit> UnlockFloatingWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> IncreaseFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> DecreaseFontSizeCommand { get; }
    public bool IsSupported { get => _isSupported; private set { this.RaiseAndSetIfChanged(ref _isSupported, value); this.RaisePropertyChanged(nameof(IsNotSupported)); } }
    public bool IsNotSupported => !IsSupported;
    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isRecording, value);
            this.RaisePropertyChanged(nameof(RecordingText));
            this.RaisePropertyChanged(nameof(RecordingIcon));
        }
    }
    public string RecordingText => IsRecording ? Resources.Speech_Stop : Resources.Speech_Start;
    public MaterialIconKind RecordingIcon => IsRecording ? MaterialIconKind.MicrophoneOff : MaterialIconKind.Microphone;
    public bool IsFloatingWindowOpen { get => _isFloatingWindowOpen; private set => this.RaiseAndSetIfChanged(ref _isFloatingWindowOpen, value); }
    public string SelectedSourcesSummary { get => _selectedSourcesSummary; private set => this.RaiseAndSetIfChanged(ref _selectedSourcesSummary, value); }
    public string SelectedVirtualMicrophonesSummary => GetOutputSummary(VirtualMicrophoneOptions);
    public string SelectedAudioOutputsSummary => GetOutputSummary(AudioOutputOptions);
    public SpeechRecognitionModel? SelectedRecognitionModel
    {
        get => _selectedRecognitionModel;
        set
        {
            if (ReferenceEquals(_selectedRecognitionModel, value)) return;
            this.RaiseAndSetIfChanged(ref _selectedRecognitionModel, value);
            if (value is not null)
                _settings.SpeechRecognition.RecognitionLanguage = value.Id;
        }
    }
    public SpeechEngineOption? SelectedEngineOption
    {
        get => _settings.SpeechRecognition.EngineId == TranslationConfigurationOption.FollowGlobalId
            ? EngineOptions.FirstOrDefault(option => option.IsGlobal)
            : _selectedEngineOption;
        set
        {
            if (value?.IsGlobal == true)
            {
                _settings.SpeechRecognition.EngineId = TranslationConfigurationOption.FollowGlobalId;
                this.RaisePropertyChanged(nameof(SelectedEngineOption));
                this.RaisePropertyChanged(nameof(IsRealTimePreviewVisible));
                this.RaisePropertyChanged(nameof(IsPromptSelectionVisible));
                return;
            }

            if (_selectedEngineOption == value
                && _settings.SpeechRecognition.EngineId == value?.Id)
                return;
            _settings.SpeechRecognition.EngineId = value?.Id ?? string.Empty;
            _settings.SpeechRecognition.EngineType = value?.IsMachine == true ? 0 : 1;
            this.RaiseAndSetIfChanged(ref _selectedEngineOption, value);
            this.RaisePropertyChanged(nameof(IsRealTimePreviewVisible));
            this.RaisePropertyChanged(nameof(IsPromptSelectionVisible));
            UpdateTargetLanguages(commitSelection: true);
        }
    }
    public LanguageSettings? SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set
        {
            if (_selectedTargetLanguage == value) return;
            this.RaiseAndSetIfChanged(ref _selectedTargetLanguage, value);
            if (value is not null)
                _settings.SpeechRecognition.TargetLanguage = value.Id;
        }
    }

    public string? SelectedPromptId
    {
        get
        {
            if (UsesGlobalPrompt(_settings.SpeechRecognition.PromptId))
                return _settings.Prompts.SelectedPromptId;

            var configured = _settings.SpeechRecognition.PromptId;
            if (PromptEntries.Any(prompt => prompt.Id == configured))
                return configured;
            var globallySelected = _settings.Prompts.SelectedPromptId;
            return PromptEntries.FirstOrDefault(prompt => prompt.Id == globallySelected)?.Id
                   ?? PromptEntries.FirstOrDefault(prompt => prompt.IsDefault)?.Id;
        }
        set
        {
            if (value == _settings.SpeechRecognition.PromptId)
                return;
            _settings.SpeechRecognition.PromptId = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(SelectedPromptOption));
        }
    }

    public bool IsTranslationEnabled { get => _settings.SpeechRecognition.IsTranslationEnabled; set { Set(value, _settings.SpeechRecognition.IsTranslationEnabled, next => _settings.SpeechRecognition.IsTranslationEnabled = next); this.RaisePropertyChanged(nameof(IsRealTimePreviewVisible)); this.RaisePropertyChanged(nameof(IsPromptSelectionVisible)); this.RaisePropertyChanged(nameof(IsAudioTranslationTargetLanguageVisible)); } }
    public TranslationConfigurationOption SelectedPromptOption
    {
        get => UsesGlobalPrompt(_settings.SpeechRecognition.PromptId)
            ? PromptOptions.First(option => option.IsGlobal)
            : PromptOptions.FirstOrDefault(option => option.Id == SelectedPromptId)
              ?? PromptOptions.First(option => option.IsGlobal);
        set
        {
            if (value.IsGlobal)
            {
                _settings.SpeechRecognition.PromptId = TranslationConfigurationOption.FollowGlobalId;
                this.RaisePropertyChanged(nameof(SelectedPromptId));
                this.RaisePropertyChanged(nameof(SelectedPromptOption));
                return;
            }

            SelectedPromptId = value.Id;
        }
    }
    public bool IsTranslatedSpeechEnabled
    {
        get => _settings.SpeechRecognition.IsTranslatedSpeechEnabled;
        set => Set(value, _settings.SpeechRecognition.IsTranslatedSpeechEnabled,
            next => _settings.SpeechRecognition.IsTranslatedSpeechEnabled = next);
    }
    private void OnOutputSelectionChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(SpeechPlaybackOutputItem.IsSelected)
            || sender is not SpeechPlaybackOutputItem changed
            || _synchronizingOutputSelection)
            return;

        var options = VirtualMicrophoneOptions.Contains(changed)
            ? VirtualMicrophoneOptions
            : AudioOutputOptions;
        _synchronizingOutputSelection = true;
        try
        {
            if (changed.IsNoOperation && changed.IsSelected)
            {
                foreach (var option in options.Where(option => !option.IsNoOperation && option.IsSelected))
                    option.IsSelected = false;
            }
            else if (!changed.IsNoOperation && changed.IsSelected)
            {
                var noOperation = options.FirstOrDefault(option => option.IsNoOperation);
                if (noOperation is not null)
                    noOperation.IsSelected = false;
            }

            var selectedDevices = GetSelectedOutputNames(options);
            if (selectedDevices.Length == 0)
            {
                var noOperation = options.FirstOrDefault(option => option.IsNoOperation);
                if (noOperation is not null)
                    noOperation.IsSelected = true;
            }

            if (ReferenceEquals(options, VirtualMicrophoneOptions))
                _settings.SpeechRecognition.TranslatedSpeechOutputDeviceNames = selectedDevices;
            else
                _settings.SpeechRecognition.AudioOutputDeviceNames = selectedDevices;
        }
        finally
        {
            _synchronizingOutputSelection = false;
        }

        this.RaisePropertyChanged(ReferenceEquals(options, VirtualMicrophoneOptions)
            ? nameof(SelectedVirtualMicrophonesSummary)
            : nameof(SelectedAudioOutputsSummary));
    }
    public bool IsAudioTranslationRecording
    {
        get => _isAudioTranslationRecording;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isAudioTranslationRecording, value);
            this.RaisePropertyChanged(nameof(AudioTranslationText));
            this.RaisePropertyChanged(nameof(AudioTranslationIcon));
        }
    }
    public bool IsAudioTranslationArmed
    {
        get => _isAudioTranslationArmed;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isAudioTranslationArmed, value);
            this.RaisePropertyChanged(nameof(AudioTranslationText));
            this.RaisePropertyChanged(nameof(AudioTranslationIcon));
        }
    }
    public bool IsRealtimeInterpretationRecording
    {
        get => _isRealtimeInterpretationRecording;
        private set => this.RaiseAndSetIfChanged(ref _isRealtimeInterpretationRecording, value);
    }
    public bool IsRealtimeInterpretationArmed
    {
        get => _isRealtimeInterpretationArmed;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isRealtimeInterpretationArmed, value);
            this.RaisePropertyChanged(nameof(RealtimeInterpretationText));
            this.RaisePropertyChanged(nameof(CanToggleRealtimeInterpretation));
        }
    }
    public bool IsVirtualCableAvailable
    {
        get => _isVirtualCableAvailable;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isVirtualCableAvailable, value);
            this.RaisePropertyChanged(nameof(IsVirtualCableMissing));
            this.RaisePropertyChanged(nameof(IsRealtimeInterpretationAvailable));
            this.RaisePropertyChanged(nameof(IsRealtimeInterpretationDriverMissing));
            this.RaisePropertyChanged(nameof(CanToggleRealtimeInterpretation));
        }
    }
    public bool IsVirtualCableCheckSkipped
    {
        get => _isVirtualCableCheckSkipped;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isVirtualCableCheckSkipped, value);
            this.RaisePropertyChanged(nameof(IsRealtimeInterpretationAvailable));
            this.RaisePropertyChanged(nameof(IsRealtimeInterpretationDriverMissing));
            this.RaisePropertyChanged(nameof(CanToggleRealtimeInterpretation));
        }
    }
    public bool IsVirtualCableMissing => !IsVirtualCableAvailable;
    public bool IsRealtimeInterpretationDriverMissing =>
        IsVoiceTranslationMode && IsVirtualCableMissing && !IsVirtualCableCheckSkipped;
    public bool IsRealtimeInterpretationAvailable =>
        (IsVirtualCableAvailable || IsVirtualCableCheckSkipped) && !IsCheckingVirtualCable;
    public bool CanToggleRealtimeInterpretation =>
        IsRealtimeInterpretationAvailable || IsRealtimeInterpretationArmed;
    public bool IsCheckingVirtualCable
    {
        get => _isCheckingVirtualCable;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCheckingVirtualCable, value);
            this.RaisePropertyChanged(nameof(IsRealtimeInterpretationAvailable));
            this.RaisePropertyChanged(nameof(CanToggleRealtimeInterpretation));
        }
    }

    internal static bool HasVirtualCableEndpoints(
        IEnumerable<AudioPlaybackDeviceDescriptor> playbackDevices,
        IEnumerable<AudioCaptureSourceDescriptor> captureSources) =>
        playbackDevices.Any(device => device.IsVirtualCable)
        && captureSources.Any(source => source.IsVirtualCable);

    public string AudioTranslationText => IsAudioTranslationArmed
        ? Resources.Speech_Stop
        : Resources.Speech_Start;
    public MaterialIconKind AudioTranslationIcon => IsAudioTranslationArmed
        ? MaterialIconKind.MicrophoneOff
        : MaterialIconKind.Microphone;
    public string RealtimeInterpretationText => IsRealtimeInterpretationArmed
        ? Resources.Speech_Stop
        : Resources.Speech_Start;

    /// <summary>0 = live captions, 1 = voice translation.</summary>
    public int SpeechModeTabIndex
    {
        get => _speechModeTabIndex;
        set
        {
            var normalized = value == 1 ? 1 : 0;
            if (_speechModeTabIndex == normalized)
                return;

            CaptureCurrentModeSnapshot();
            this.RaiseAndSetIfChanged(ref _speechModeTabIndex, normalized);
            _settings.SpeechRecognition.SetActiveMode(
                normalized == 0
                    ? SpeechTranslationMode.AudioTranslation
                    : SpeechTranslationMode.RealtimeInterpretation);
            RestoreModeSnapshot();

            this.RaisePropertyChanged(nameof(IsVoiceTranslationMode));
            this.RaisePropertyChanged(nameof(IsLiveCaptionsMode));
            this.RaisePropertyChanged(nameof(IsAudioTranslationTargetLanguageVisible));
            this.RaisePropertyChanged(nameof(IsRealtimeInterpretationDriverMissing));
        }
    }

    public bool IsVoiceTranslationMode => _speechModeTabIndex == 1;
    public bool IsLiveCaptionsMode => !IsVoiceTranslationMode;
    public bool IsAudioTranslationTargetLanguageVisible =>
        IsLiveCaptionsMode && IsTranslationEnabled;
    public bool IsRealTimePreviewEnabled { get => _settings.SpeechRecognition.IsRealTimePreviewEnabled; set => Set(value, _settings.SpeechRecognition.IsRealTimePreviewEnabled, next => _settings.SpeechRecognition.IsRealTimePreviewEnabled = next); }
    public bool IsRealTimePreviewVisible =>
        ShouldShowRealTimePreview(IsTranslationEnabled, ResolveEffectiveEngine()?.IsMachine == true);
    public bool IsPromptSelectionVisible =>
        IsTranslationEnabled && ResolveEffectiveEngine()?.IsMachine == false;
    public int AutoClearInterval { get => _settings.SpeechRecognition.AutoClearInterval; set => Set(value, _settings.SpeechRecognition.AutoClearInterval, next => _settings.SpeechRecognition.AutoClearInterval = next); }
    public int MaxSentencesPerLine { get => _settings.SpeechRecognition.MaxSentencesPerLine; set => Set(value, _settings.SpeechRecognition.MaxSentencesPerLine, next => _settings.SpeechRecognition.MaxSentencesPerLine = next); }
    public FloatingDisplayMode FloatingDisplayMode { get => _settings.SpeechRecognition.FloatingDisplayMode; set => Set(value, _settings.SpeechRecognition.FloatingDisplayMode, next => _settings.SpeechRecognition.FloatingDisplayMode = next); }
    public int MaxFloatingHistory { get => _settings.SpeechRecognition.MaxFloatingHistory; set => Set(value, _settings.SpeechRecognition.MaxFloatingHistory, next => _settings.SpeechRecognition.MaxFloatingHistory = next); }
    public SubtitleSource MainSubtitleSource { get => _settings.SpeechRecognition.MainSubtitleSource; set => Set(value, _settings.SpeechRecognition.MainSubtitleSource, next => _settings.SpeechRecognition.MainSubtitleSource = next); }
    public double PrimaryFontSize { get => _settings.SpeechRecognition.PrimaryFontSize; set => Set(value, _settings.SpeechRecognition.PrimaryFontSize, next => _settings.SpeechRecognition.PrimaryFontSize = next); }
    public string PrimaryFontFamily { get => _settings.SpeechRecognition.PrimaryFontFamily; set => Set(value, _settings.SpeechRecognition.PrimaryFontFamily, next => _settings.SpeechRecognition.PrimaryFontFamily = next); }
    public string PrimaryFontColor { get => _settings.SpeechRecognition.PrimaryFontColor; set => Set(value, _settings.SpeechRecognition.PrimaryFontColor, next => _settings.SpeechRecognition.PrimaryFontColor = next); }
    public SubtitleSource SecondarySubtitleSource { get => _settings.SpeechRecognition.SecondarySubtitleSource; set => Set(value, _settings.SpeechRecognition.SecondarySubtitleSource, next => _settings.SpeechRecognition.SecondarySubtitleSource = next); }
    public double SecondaryFontSize { get => _settings.SpeechRecognition.SecondaryFontSize; set => Set(value, _settings.SpeechRecognition.SecondaryFontSize, next => _settings.SpeechRecognition.SecondaryFontSize = next); }
    public string SecondaryFontFamily { get => _settings.SpeechRecognition.SecondaryFontFamily; set => Set(value, _settings.SpeechRecognition.SecondaryFontFamily, next => _settings.SpeechRecognition.SecondaryFontFamily = next); }
    public string SecondaryFontColor { get => _settings.SpeechRecognition.SecondaryFontColor; set => Set(value, _settings.SpeechRecognition.SecondaryFontColor, next => _settings.SpeechRecognition.SecondaryFontColor = next); }
    public string BackgroundColor { get => _settings.SpeechRecognition.BackgroundColor; set => Set(value, _settings.SpeechRecognition.BackgroundColor, next => _settings.SpeechRecognition.BackgroundColor = next); }
    public string SubtitleBackgroundColor { get => _settings.SpeechRecognition.SubtitleBackgroundColor; set => Set(value, _settings.SpeechRecognition.SubtitleBackgroundColor, next => _settings.SpeechRecognition.SubtitleBackgroundColor = next); }
    public double WindowOpacity { get => _settings.SpeechRecognition.WindowOpacity; set => Set(value, _settings.SpeechRecognition.WindowOpacity, next => _settings.SpeechRecognition.WindowOpacity = next); }
    public bool IsFloatingWindowLocked { get => _settings.SpeechRecognition.IsFloatingWindowLocked; set => Set(value, _settings.SpeechRecognition.IsFloatingWindowLocked, next => _settings.SpeechRecognition.IsFloatingWindowLocked = next); }
    public string FloatingWindowOrientation { get => _settings.SpeechRecognition.FloatingWindowOrientation; set => Set(value, _settings.SpeechRecognition.FloatingWindowOrientation, next => _settings.SpeechRecognition.FloatingWindowOrientation = next); }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;
        var capability = await _capabilities.GetStatusAsync(
            PlatformCapability.SpeechRecognition,
            cancellationToken);
        IsSupported = capability.State != CapabilityState.Unsupported;
        if (!IsSupported)
            return;

        await RefreshRecognitionLanguagesAsync(cancellationToken);
        await RefreshSourcesAsync(cancellationToken);
#if DEBUG
        if (!_debugVirtualCableNeedsManualCheck)
            await RefreshVirtualCableAsync(cancellationToken);
#else
        await RefreshVirtualCableAsync(cancellationToken);
#endif
    }

    public void StoreFloatingWindowBounds(int x, int y, double width, double height)
    {
        _settings.SpeechRecognition.WindowX = x;
        _settings.SpeechRecognition.WindowY = y;
        _settings.SpeechRecognition.WindowWidth = width;
        _settings.SpeechRecognition.WindowHeight = height;
    }

    public void Dispose()
    {
        ReleasePreparedRecognitionOnDispose();
        foreach (var cancellation in _recognitionCancellations)
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
        }
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _subtitleWindow.VisibilityChanged -= OnSubtitleWindowVisibilityChanged;
        _models.ModelsChanged -= OnModelsChanged;
        _subtitleWindow.Close();
        _hotkeyController.Detach(this);
        foreach (var source in AudioSources)
            source.Dispose();
    }

    private void OnModelsChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _initialized) == 0 || !IsSupported)
            return;
        Dispatcher.UIThread.Post(() => _ = RefreshRecognitionLanguagesAfterChangeAsync());
    }

    private async Task RefreshRecognitionLanguagesAfterChangeAsync()
    {
        try
        {
            await RefreshRecognitionLanguagesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to refresh speech recognition models.");
        }
    }

    private async Task RefreshRecognitionLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var models = await _models.GetModelsAsync(cancellationToken);
        var current = SelectedRecognitionModel?.Id;
        RecognitionLanguages.Clear();
        foreach (var model in models)
            RecognitionLanguages.Add(model);

        var configured = string.IsNullOrWhiteSpace(current)
            ? _settings.SpeechRecognition.RecognitionLanguage
            : current;
        SelectedRecognitionModel = RecognitionLanguages.FirstOrDefault(model => model.Id == configured)
            ?? RecognitionLanguages.FirstOrDefault(model => model.Id.Contains("zh", StringComparison.OrdinalIgnoreCase))
            ?? RecognitionLanguages.FirstOrDefault();
    }

    private async Task ToggleRecordingAsync()
    {
        await ToggleModeRecordingAsync(
            _speechModeTabIndex == 1
                ? SpeechSessionMode.RealtimeInterpretation
                : SpeechSessionMode.AudioTranslation);
    }

    private async Task ToggleModeRecordingAsync(SpeechSessionMode mode)
    {
        if (!IsSupported)
            return;

        var index = (int)mode;
        if (GetPreparedCommand(mode) is not null
            || _recognitionCancellations[index] is not null)
        {
            SetModeArmed(mode, false);
            await StopModeAsync(mode);
            await ReleasePreparedModeAsync(mode);
            return;
        }

        CaptureCurrentModeSnapshot();
        if (!TryCreateRecognitionCommand(mode, out var command))
            return;

        IsBusy = true;
        try
        {
            var result = await _speech.PrepareAsync(command, _lifetimeCancellation.Token);
            if (result.IsFailure)
            {
                AddError(result.Error.Message);
                return;
            }

            SetPreparedCommand(mode, command);
            SetModeArmed(mode, true);
            if (mode == SpeechSessionMode.AudioTranslation
                && !StartModeRecording(mode, command))
            {
                SetModeArmed(mode, false);
                await ReleasePreparedModeAsync(mode);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to prepare speech recognition.");
            AddError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryCreateRecognitionCommand(
        SpeechSessionMode mode,
        out SpeechRecognitionCommand command)
    {
        var snapshot = _modeSnapshots[(int)mode];
        var modeSettings = _settings.SpeechRecognition.ToContractForMode(
            mode == SpeechSessionMode.AudioTranslation
                ? SpeechTranslationMode.AudioTranslation
                : SpeechTranslationMode.RealtimeInterpretation);
        var modelId = modeSettings.RecognitionLanguage;
        if (string.IsNullOrWhiteSpace(modelId))
            modelId = SelectedRecognitionModel?.Id;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            command = null!;
            return false;
        }

        var sources = snapshot.Sources.Count > 0
            ? snapshot.Sources.ToArray()
            : AudioSources.Where(source => source.IsSelected)
                .Select(source => new AudioCaptureSourceReference(source.Token, source.Kind))
                .ToArray();
        if (sources.Length == 0)
        {
            command = null!;
            return false;
        }

        command = new SpeechRecognitionCommand(
            modelId,
            modelId,
            sources,
            modeSettings with { RecognitionLanguage = modelId },
            CompleteOnCancellation: mode == SpeechSessionMode.RealtimeInterpretation,
            SubtitleOrigin: mode == SpeechSessionMode.RealtimeInterpretation
                ? SpeechSubtitleOrigin.RealtimeInterpretation
                : SpeechSubtitleOrigin.AudioTranslation,
            SegmentationMode: mode == SpeechSessionMode.RealtimeInterpretation
                ? SpeechRecognitionSegmentationMode.SingleUtterance
                : SpeechRecognitionSegmentationMode.Standard);
        return true;
    }

    private bool StartModeRecording(
        SpeechSessionMode mode,
        SpeechRecognitionCommand command)
    {
        var index = (int)mode;
        if (_recognitionTasks[index]?.IsCompleted == true)
        {
            _recognitionCancellations[index]?.Dispose();
            _recognitionCancellations[index] = null;
            _recognitionTasks[index] = null;
        }
        if (_recognitionCancellations[index] is not null)
            return false;

        var cancellation = new CancellationTokenSource();
        _recognitionCancellations[index] = cancellation;
        if (mode == SpeechSessionMode.AudioTranslation)
            IsAudioTranslationRecording = true;
        else
            IsRealtimeInterpretationRecording = true;
        IsRecording = true;
        _recognitionTasks[index] = ConsumeRecognitionAsync(mode, command, cancellation.Token);
        return true;
    }

    private async Task ToggleInterpretationArmedAsync()
    {
        if (!IsRealtimeInterpretationAvailable && !IsRealtimeInterpretationArmed)
            return;
        await ToggleModeRecordingAsync(SpeechSessionMode.RealtimeInterpretation);
    }

    public ValueTask<bool> BeginRealtimeInterpretationHoldAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<bool>(cancellationToken);
        if (!IsRealtimeInterpretationAvailable
            || !IsRealtimeInterpretationArmed
            || IsRealtimeInterpretationRecording
            || _preparedRealtimeInterpretationCommand is null)
            return ValueTask.FromResult(false);

        return ValueTask.FromResult(StartModeRecording(
            SpeechSessionMode.RealtimeInterpretation,
            _preparedRealtimeInterpretationCommand));
    }

    public async ValueTask EndRealtimeInterpretationHoldAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsRealtimeInterpretationRecording)
            return;
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        await StopModeAsync(SpeechSessionMode.RealtimeInterpretation);
    }

    private async Task StopModeAsync(SpeechSessionMode mode)
    {
        var index = (int)mode;
        var cancellation = _recognitionCancellations[index];
        if (cancellation is null)
            return;
        cancellation.Cancel();
        var task = _recognitionTasks[index];
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
        _recognitionCancellations[index] = null;
        _recognitionTasks[index] = null;
    }

    private SpeechRecognitionCommand? GetPreparedCommand(SpeechSessionMode mode) =>
        mode == SpeechSessionMode.AudioTranslation
            ? _preparedAudioTranslationCommand
            : _preparedRealtimeInterpretationCommand;

    private void SetPreparedCommand(
        SpeechSessionMode mode,
        SpeechRecognitionCommand? command)
    {
        if (mode == SpeechSessionMode.AudioTranslation)
            _preparedAudioTranslationCommand = command;
        else
            _preparedRealtimeInterpretationCommand = command;
    }

    private void SetModeArmed(SpeechSessionMode mode, bool armed)
    {
        if (mode == SpeechSessionMode.AudioTranslation)
            IsAudioTranslationArmed = armed;
        else
            IsRealtimeInterpretationArmed = armed;
    }

    private async Task ReleasePreparedModeAsync(SpeechSessionMode mode)
    {
        var command = GetPreparedCommand(mode);
        SetPreparedCommand(mode, null);
        if (command is null)
            return;

        var result = await _speech.ReleasePreparationAsync(command, CancellationToken.None);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Unable to release the prepared speech recognition resources: {Message}",
                result.Error.Message);
            AddError(result.Error.Message);
        }
    }

    private void ReleasePreparedRecognitionOnDispose()
    {
        var commands = new[]
        {
            _preparedAudioTranslationCommand,
            _preparedRealtimeInterpretationCommand
        };
        _preparedAudioTranslationCommand = null;
        _preparedRealtimeInterpretationCommand = null;
        foreach (var command in commands.Where(command => command is not null))
            _ = ReleasePreparedRecognitionOnDisposeAsync(command!);
    }

    private async Task ReleasePreparedRecognitionOnDisposeAsync(
        SpeechRecognitionCommand command)
    {
        try
        {
            var result = await _speech.ReleasePreparationAsync(command, CancellationToken.None);
            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "Unable to release prepared speech recognition resources during disposal: {Message}",
                    result.Error.Message);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to release prepared speech recognition resources during disposal.");
        }
    }

    private async Task ConsumeRecognitionAsync(
        SpeechSessionMode mode,
        SpeechRecognitionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _speech.RecognizeAsync(command, cancellationToken)
                               .ConfigureAwait(false))
            {
                await Dispatcher.UIThread.InvokeAsync(() => Apply(mode, item));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Speech recognition failed.");
            await Dispatcher.UIThread.InvokeAsync(() => AddError(exception.Message));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _subtitleProjection.StopLoading();
                if (mode == SpeechSessionMode.AudioTranslation)
                    IsAudioTranslationRecording = false;
                else
                    IsRealtimeInterpretationRecording = false;
                IsRecording = IsAudioTranslationRecording || IsRealtimeInterpretationRecording;
                IsBusy = false;
            });
        }
    }

    private void Apply(SpeechSessionMode mode, SpeechSessionEvent item)
    {
        switch (item)
        {
            case SpeechSessionStartedEvent:
                IsRecording = true;
                IsBusy = false;
                break;
            case SpeechSubtitleChangedEvent changed:
                UpdateSubtitle(changed.Subtitle);
                break;
            case SpeechTranslationCompletedEvent completed
                when mode == SpeechSessionMode.RealtimeInterpretation
                     && completed.Origin == SpeechSubtitleOrigin.RealtimeInterpretation:
                _ = _hotkeyController.PlayTranslationCompletedFeedbackAsync().AsTask();
                break;
            case SpeechFloatingSubtitleRemovedEvent removed:
                BeginFloatingSubtitleRemoval(removed.SubtitleId);
                break;
            case SpeechSessionErrorEvent error:
                AddError(error.Message);
                break;
            case SpeechSessionStoppedEvent:
                if (mode == SpeechSessionMode.AudioTranslation)
                    IsAudioTranslationRecording = false;
                else
                    IsRealtimeInterpretationRecording = false;
                IsRecording = IsAudioTranslationRecording || IsRealtimeInterpretationRecording;
                IsBusy = false;
                break;
        }
    }

    private void UpdateSubtitle(SpeechSubtitleLine subtitle)
    {
        _subtitleProjection.Update(subtitle);
    }

    private void AddError(string message)
    {
        var line = new SpeechSubtitleLine(
            Interlocked.Decrement(ref _nextErrorId),
            DateTime.Now.TimeOfDay,
            message,
            string.Empty,
            string.Empty,
            false,
            false);
        _subtitleProjection.Update(line);
    }

    private async Task RefreshVirtualCableAsync(
        CancellationToken cancellationToken = default,
        bool resetCheck = true)
    {
        if (resetCheck)
            IsVirtualCableCheckSkipped = false;
#if DEBUG
        _debugVirtualCableNeedsManualCheck = false;
#endif
        IsCheckingVirtualCable = true;
        try
        {
            // VB-CABLE is an audio pair: CABLE Input is the playback endpoint and
            // CABLE Output is the capture endpoint. Check both endpoint catalogs so
            // a stale or incomplete device list cannot be mistaken for an install.
            var playbackDevicesTask = _playbackDevices.GetDevicesAsync(cancellationToken).AsTask();
            var captureSourcesTask = _audioSources.GetSourcesAsync(cancellationToken).AsTask();
            await Task.WhenAll(playbackDevicesTask, captureSourcesTask);

            var playbackDevices = playbackDevicesTask.Result;
            var captureSources = captureSourcesTask.Result;
            VirtualMicrophoneOptions.Clear();
            AudioOutputOptions.Clear();
            AddOutputOptions(VirtualMicrophoneOptions, playbackDevices);
            AddOutputOptions(AudioOutputOptions, playbackDevices);
            ApplyOutputSelections(VirtualMicrophoneOptions, _settings.SpeechRecognition.TranslatedSpeechOutputDeviceNames);
            ApplyOutputSelections(AudioOutputOptions, _settings.SpeechRecognition.AudioOutputDeviceNames);
            ReplaceUnavailableOutputNames(
                VirtualMicrophoneOptions,
                _settings.SpeechRecognition.TranslatedSpeechOutputDeviceNames,
                names => _settings.SpeechRecognition.TranslatedSpeechOutputDeviceNames = names);
            ReplaceUnavailableOutputNames(
                AudioOutputOptions,
                _settings.SpeechRecognition.AudioOutputDeviceNames,
                names => _settings.SpeechRecognition.AudioOutputDeviceNames = names);
            this.RaisePropertyChanged(nameof(SelectedVirtualMicrophonesSummary));
            this.RaisePropertyChanged(nameof(SelectedAudioOutputsSummary));
            IsVirtualCableAvailable = HasVirtualCableEndpoints(playbackDevices, captureSources);
            _logger.LogInformation(
                "VB-CABLE endpoint check: Available={Available}; Playback={PlaybackDevices}; Capture={CaptureDevices}",
                IsVirtualCableAvailable,
                string.Join(", ", playbackDevices.Select(device => device.Name)),
                string.Join(", ", captureSources
                    .Where(source => source.Kind == AudioCaptureSourceKind.Microphone)
                    .Select(source => source.Name)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to enumerate virtual cable playback devices.");
            IsVirtualCableAvailable = false;
        }
        finally
        {
            IsCheckingVirtualCable = false;
        }
    }

    private void AddOutputOptions(
        ObservableCollection<SpeechPlaybackOutputItem> options,
        IEnumerable<AudioPlaybackDeviceDescriptor> devices)
    {
        var noOperation = new SpeechPlaybackOutputItem(Resources.Speech_NoAudioOutput)
        {
            IsSelected = true
        };
        noOperation.PropertyChanged += OnOutputSelectionChanged;
        options.Add(noOperation);
        foreach (var device in devices)
        {
            var item = new SpeechPlaybackOutputItem(device, isSelected: false);
            item.PropertyChanged += OnOutputSelectionChanged;
            options.Add(item);
        }
    }

    private void ApplyOutputSelections(
        ObservableCollection<SpeechPlaybackOutputItem> options,
        IReadOnlyList<string>? configuredNames)
    {
        var names = configuredNames ?? [];
        _synchronizingOutputSelection = true;
        try
        {
            var matched = 0;
            foreach (var option in options)
            {
                var selected = !option.IsNoOperation
                               && option.DeviceName is not null
                               && names.Any(name => string.Equals(
                                   name,
                                   option.DeviceName,
                                   StringComparison.OrdinalIgnoreCase));
                option.IsSelected = selected;
                if (selected)
                    matched++;
            }

            var noOperation = options.FirstOrDefault(option => option.IsNoOperation);
            if (noOperation is not null)
                noOperation.IsSelected = matched == 0;
        }
        finally
        {
            _synchronizingOutputSelection = false;
        }
    }

    private static string GetOutputSummary(IEnumerable<SpeechPlaybackOutputItem> options)
    {
        var selected = options
            .Where(option => !option.IsNoOperation && option.IsSelected)
            .ToArray();
        return selected.Length switch
        {
            0 => Resources.Speech_NoAudioOutput,
            1 => selected[0].DisplayName,
            _ => string.Format(Resources.Speech_SelectedOutputsCount, selected.Length)
        };
    }

    private static string[] GetSelectedOutputNames(IEnumerable<SpeechPlaybackOutputItem> options) =>
        options
            .Where(option => !option.IsNoOperation && option.IsSelected)
            .Select(option => option.DeviceName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

    private static void ReplaceUnavailableOutputNames(
        ObservableCollection<SpeechPlaybackOutputItem> options,
        IReadOnlyList<string>? configuredNames,
        Action<IReadOnlyList<string>> replace)
    {
        var selected = GetSelectedOutputNames(options);
        if (!(configuredNames ?? []).SequenceEqual(selected, StringComparer.OrdinalIgnoreCase))
            replace(selected);
    }

    private async Task SkipVirtualCableCheckAsync()
    {
        IsVirtualCableCheckSkipped = true;
        await RefreshVirtualCableAsync(resetCheck: false);
    }

    private void OpenAudioTranslationTutorial()
    {
        OpenLocalizedTutorial(
            "https://easychat.ncii.cn/zh/docs/feature/simultaneous-interpretation/asr",
            "https://easychat.ncii.cn/en/docs/feature/simultaneous-interpretation/asr");
    }

    private void OpenInterpretationTutorial()
    {
        OpenLocalizedTutorial(
            "https://easychat.ncii.cn/zh/docs/feature/simultaneous-interpretation/speak",
            "https://easychat.ncii.cn/en/docs/feature/simultaneous-interpretation/speak");
    }

    private void OpenVirtualCableInstallationTutorial()
    {
        OpenLocalizedTutorial(
            "https://easychat.ncii.cn/zh/docs/feature/simultaneous-interpretation/speak#%E5%AE%89%E8%A3%85-asr-%E6%A8%A1%E5%9E%8B",
            "https://easychat.ncii.cn/en/docs/feature/simultaneous-interpretation/speak#install-a-virtual-audio-driver");
    }

    private void OpenLocalizedTutorial(string chineseUri, string englishUri)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var uri = new Uri(language.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? chineseUri
            : englishUri);
        var result = _uriLauncher.Open(uri);
        if (result.IsFailure)
            AddError(result.Error.Message);
    }

    private async Task RefreshSourcesAsync(CancellationToken cancellationToken = default)
    {
        var access = await _platformAccess.EnsureAvailableAsync(
            PlatformCapability.AudioCaptureSources,
            cancellationToken);
        if (access.IsFailure)
        {
            _logger.LogWarning(
                "Audio capture sources are unavailable: {Message}",
                access.Error.Message);
            return;
        }

        CaptureCurrentModeSources();
        _availableAudioSources = await _audioSources.GetSourcesAsync(cancellationToken);
        ApplyAudioSourcesForCurrentMode();
    }

    private void ApplyAudioSourcesForCurrentMode()
    {
        var modeSnapshot = _modeSnapshots[_speechModeTabIndex];
        foreach (var source in AudioSources)
        {
            source.PropertyChanged -= OnSourcePropertyChanged;
            source.Dispose();
        }
        AudioSources.Clear();

        IReadOnlyList<AudioCaptureSourceDescriptor> available = _availableAudioSources;
        if (_speechModeTabIndex == (int)SpeechSessionMode.AudioTranslation)
        {
            available = available
                .Where(source => source.Kind != AudioCaptureSourceKind.Microphone)
                .ToArray();
        }
        var restored = modeSnapshot.Sources.Count > 0 && available.Any(source => modeSnapshot.Sources.Contains(
            new AudioCaptureSourceReference(source.Token, source.Kind)));
        var defaultMicrophone = available.FirstOrDefault(source =>
                source.Kind == AudioCaptureSourceKind.Microphone
                && !source.IsVirtualCable
                && source.IsDefault)
            ?? available.FirstOrDefault(source =>
                source.Kind == AudioCaptureSourceKind.Microphone
                && !source.IsVirtualCable);
        foreach (var descriptor in available)
        {
            var item = new SpeechAudioSourceItem(
                descriptor,
                !restored
                    ? (_speechModeTabIndex == (int)SpeechSessionMode.RealtimeInterpretation
                        ? (defaultMicrophone is not null
                            ? descriptor.Token == defaultMicrophone.Token
                            : descriptor.Kind == AudioCaptureSourceKind.Microphone)
                        : descriptor.Kind == AudioCaptureSourceKind.SystemOutput)
                    : modeSnapshot.Sources.Contains(new AudioCaptureSourceReference(
                        descriptor.Token,
                        descriptor.Kind)));
            item.PropertyChanged += OnSourcePropertyChanged;
            AudioSources.Add(item);
        }
        CaptureCurrentModeSources();
        UpdateSourceSummary();
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SpeechAudioSourceItem.IsSelected))
        {
            CaptureCurrentModeSources();
            UpdateSourceSummary();
        }
    }

    private void CaptureCurrentModeSources()
    {
        if (_modeSnapshots[_speechModeTabIndex] is null)
            return;
        var target = _modeSnapshots[_speechModeTabIndex].Sources;
        target.Clear();
        foreach (var source in AudioSources.Where(source => source.IsSelected))
            target.Add(new AudioCaptureSourceReference(source.Token, source.Kind));
    }

    private void CaptureCurrentModeSnapshot()
    {
        var snapshot = _modeSnapshots[_speechModeTabIndex];
        if (snapshot is null)
            return;
        CaptureCurrentModeSources();
    }

    private void RestoreModeSnapshot()
    {
        var settings = _settings.SpeechRecognition.ToContract();
        _selectedRecognitionModel = RecognitionLanguages.FirstOrDefault(model =>
            model.Id == settings.RecognitionLanguage);
        _selectedTargetLanguage = null;
        _selectedEngineOption = settings.EngineId == TranslationConfigurationOption.FollowGlobalId
            ? ResolveGlobalEngineOption()
            : EngineOptions.FirstOrDefault(option =>
                option.Id == settings.EngineId
                && option.IsMachine == (settings.EngineType == 0));
        this.RaisePropertyChanged(nameof(SelectedRecognitionModel));
        this.RaisePropertyChanged(nameof(SelectedEngineOption));
        UpdateTargetLanguages(commitSelection: false);
        ApplyAudioSourcesForCurrentMode();
        RaiseModePropertiesChanged();
    }

    private void RaiseModePropertiesChanged()
    {
        ApplyOutputSelections(VirtualMicrophoneOptions, _settings.SpeechRecognition.TranslatedSpeechOutputDeviceNames);
        ApplyOutputSelections(AudioOutputOptions, _settings.SpeechRecognition.AudioOutputDeviceNames);
        foreach (var property in new[]
                 {
                     nameof(IsTranslationEnabled), nameof(IsTranslatedSpeechEnabled),
                      nameof(SelectedVirtualMicrophonesSummary), nameof(SelectedAudioOutputsSummary),
                      nameof(IsRealTimePreviewEnabled), nameof(IsRealTimePreviewVisible),
                      nameof(IsAudioTranslationTargetLanguageVisible),
                      nameof(IsPromptSelectionVisible), nameof(SelectedPromptId),
                      nameof(SelectedPromptOption),
                      nameof(SelectedEngineOption),
                     nameof(AudioTranslationText), nameof(RealtimeInterpretationText)
                 })
            this.RaisePropertyChanged(property);
    }

    private void UpdateSourceSummary()
    {
        var selected = AudioSources.Where(source => source.IsSelected).ToArray();
        SelectedSourcesSummary = selected.Length switch
        {
            0 => Resources.Speech_AllSystemAudio,
            1 => selected[0].Name,
            _ => string.Format(Resources.Speech_SelectedSourcesCount, selected.Length)
        };
    }

    private void LoadEngineOptions()
    {
        var configuredId = _settings.SpeechRecognition.EngineId;
        var selectedId = _selectedEngineOption?.Id
                         ?? (configuredId == TranslationConfigurationOption.FollowGlobalId
                             ? string.Empty
                             : configuredId);
        var selectedMachine = _selectedEngineOption?.IsMachine
                              ?? (configuredId != TranslationConfigurationOption.FollowGlobalId
                                  && _settings.SpeechRecognition.EngineType == 0);
        EngineOptions.Clear();
        EngineOptions.Add(new SpeechEngineOption(
            Resources.TextAssistFollowGlobal,
            TranslationConfigurationOption.FollowGlobalId,
            IsMachine: false,
            IsGlobal: true));
        foreach (var option in CreateMachineEngineOptions(_settings.MachineTranslation))
            EngineOptions.Add(option);
        foreach (var model in _settings.AiModel.ConfiguredModels)
            EngineOptions.Add(new SpeechEngineOption(model.Name, model.Id, false)
            {
                ImageValue = model.ModelType
            });
        _selectedEngineOption = configuredId == TranslationConfigurationOption.FollowGlobalId
            ? ResolveGlobalEngineOption()
            : ResolveAndSynchronizeEngineOption(
                EngineOptions,
                selectedId,
                selectedMachine,
                _settings.SpeechRecognition);
        var engineFellBack = !MatchesEngineSelection(
            _selectedEngineOption,
            selectedId,
            selectedMachine);
        this.RaisePropertyChanged(nameof(SelectedEngineOption));
        this.RaisePropertyChanged(nameof(IsRealTimePreviewVisible));
        this.RaisePropertyChanged(nameof(IsPromptSelectionVisible));
        UpdateTargetLanguages(commitSelection: engineFellBack);
    }

    private SpeechEngineOption? ResolveGlobalEngineOption()
    {
        var general = _settings.General;
        var useMachine = string.Equals(
            general.TransEngine,
            TranslationEngineNames.MachineTrans,
            StringComparison.OrdinalIgnoreCase);
        var id = useMachine
            ? general.UsingMachineTransId ?? general.UsingMachineTrans
            : general.UsingAiModelId ?? general.UsingAiModel;
        return EngineOptions.FirstOrDefault(option =>
                   !option.IsGlobal
                   && option.IsMachine == useMachine
                   && (string.Equals(option.Id, id, StringComparison.Ordinal)
                       || string.Equals(option.Name, id, StringComparison.OrdinalIgnoreCase)))
               ?? EngineOptions.FirstOrDefault(option => !option.IsGlobal && option.IsMachine == useMachine);
    }

    private SpeechEngineOption? ResolveEffectiveEngine() =>
        _settings.SpeechRecognition.EngineId == TranslationConfigurationOption.FollowGlobalId
            ? ResolveGlobalEngineOption()
            : _selectedEngineOption;

    private static bool UsesGlobalPrompt(string? promptId) =>
        string.IsNullOrWhiteSpace(promptId)
        || promptId == TranslationConfigurationOption.FollowGlobalId;

    private void RaiseGlobalTranslationConfigurationProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(SelectedEngineOption),
                     nameof(SelectedPromptId),
                     nameof(SelectedPromptOption),
                     nameof(IsRealTimePreviewVisible),
                     nameof(IsPromptSelectionVisible)
                 })
            this.RaisePropertyChanged(property);
        UpdateTargetLanguages(commitSelection: false);
    }

    internal static IReadOnlyList<SpeechEngineOption> CreateMachineEngineOptions(
        LiveMachineTranslationSettings settings) =>
    [
        new(MachineTranslationProviderNames.Baidu, settings.Baidu.Id, IsMachine: true)
        {
            ImageValue = settings.Baidu.Id
        },
        new(MachineTranslationProviderNames.Tencent, settings.Tencent.Id, IsMachine: true)
        {
            ImageValue = settings.Tencent.Id
        },
        new(MachineTranslationProviderNames.Google, settings.Google.Id, IsMachine: true)
        {
            ImageValue = settings.Google.Id
        },
        new(MachineTranslationProviderNames.DeepL, settings.DeepL.Id, IsMachine: true)
        {
            ImageValue = settings.DeepL.Id
        }
    ];

    internal static SpeechEngineOption? ResolveAndSynchronizeEngineOption(
        IReadOnlyList<SpeechEngineOption> options,
        string selectedId,
        bool selectedMachine,
        LiveSpeechRecognitionSettings settings)
    {
        var selected = options.Where(option => !option.IsGlobal).FirstOrDefault(option =>
                           option.Id == selectedId && option.IsMachine == selectedMachine)
                       ?? (selectedMachine
                           ? options.Where(option => !option.IsGlobal).FirstOrDefault(option =>
                               option.IsMachine
                               && option.Name.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                           : null)
                       ?? options.FirstOrDefault(option => !option.IsGlobal);
        if (selected is null)
            return null;

        var engineType = selected.IsMachine ? 0 : 1;
        if (!string.Equals(settings.EngineId, selected.Id, StringComparison.Ordinal))
            settings.EngineId = selected.Id;
        if (settings.EngineType != engineType)
            settings.EngineType = engineType;
        return selected;
    }

    internal static bool MatchesEngineSelection(
        SpeechEngineOption? option,
        string selectedId,
        bool selectedMachine) =>
        option is not null
        && !option.IsGlobal
        && (string.Equals(option.Id, selectedId, StringComparison.Ordinal)
            || (selectedMachine
                && option.Name.Equals(selectedId, StringComparison.OrdinalIgnoreCase)))
        && option.IsMachine == selectedMachine;

    internal static LanguageSettings? ResolveAndSynchronizeTargetLanguage(
        IReadOnlyList<LanguageSettings> options,
        string targetId,
        bool synchronizeSelection,
        LiveSpeechRecognitionSettings settings)
    {
        var selected = options.FirstOrDefault(language => language.Id == targetId)
                       ?? options.FirstOrDefault(language => language.Id == "zh-Hans")
                       ?? options.FirstOrDefault();
        if (synchronizeSelection && selected is not null)
            settings.TargetLanguage = selected.Id;
        return selected;
    }

    internal static bool ShouldShowMaxSentencesPerLine(
        FloatingDisplayMode displayMode,
        bool isMachineTranslation) =>
        displayMode == FloatingDisplayMode.Segmented && isMachineTranslation;

    internal static bool ShouldShowRealTimePreview(
        bool isTranslationEnabled,
        bool isMachineTranslation) =>
        isTranslationEnabled && isMachineTranslation;

    internal static bool SupportsTargetLanguage(
        LanguageSettings language,
        SpeechEngineOption? option) =>
        option?.IsMachine != true
        || language.Id == "auto"
        || language.ProviderCodes.ContainsKey(option.Name);

    private void UpdateTargetLanguages(bool commitSelection)
    {
        var targetId = _selectedTargetLanguage?.Id ?? _settings.SpeechRecognition.TargetLanguage;
        TargetLanguages.Clear();
        var effectiveEngine = ResolveEffectiveEngine();
        foreach (var language in _languages.All.Where(language =>
                     SupportsTargetLanguage(language, effectiveEngine)))
        {
            TargetLanguages.Add(language);
        }
        _selectedTargetLanguage = ResolveAndSynchronizeTargetLanguage(
            TargetLanguages,
            targetId,
            commitSelection,
            _settings.SpeechRecognition);
        this.RaisePropertyChanged(nameof(SelectedTargetLanguage));
    }

    private void ToggleFloatingWindow()
    {
        if (_subtitleWindow.IsOpen)
            _subtitleWindow.Close();
        else
            _subtitleWindow.Open(this);
    }

    private void OnSubtitleWindowVisibilityChanged(object? sender, bool isOpen) =>
        IsFloatingWindowOpen = isOpen;

    private void ClearHistory()
    {
        _subtitleProjection.Clear();
    }

    private void BeginFloatingSubtitleRemoval(long subtitleId)
    {
        var item = _subtitleProjection.BeginFloatingRemoval(subtitleId);
        if (item is not null)
            _ = CompleteFloatingSubtitleRemovalAsync(item, _lifetimeCancellation.Token);
    }

    private async Task CompleteFloatingSubtitleRemovalAsync(
        SpeechSubtitleItemViewModel item,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FloatingSubtitleFadeDuration, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(
                () => _subtitleProjection.CompleteFloatingRemoval(item),
                DispatcherPriority.Normal,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Set<T>(T value, T current, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(value, current))
            return;
        apply(value);
        this.RaisePropertyChanged(propertyName);
    }
}
