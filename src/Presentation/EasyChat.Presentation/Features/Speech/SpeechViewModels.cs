using System.Collections.ObjectModel;
using System.ComponentModel;
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
using Material.Icons;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Speech;

public sealed record SpeechEngineOption(string Name, string Id, bool IsMachine);

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
    public Bitmap? AppIcon { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public void Dispose() => AppIcon?.Dispose();
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
        Update(subtitle);
    }

    public long Id { get; }
    public TimeSpan Timestamp { get; }
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
    private readonly IPlatformCapabilities _capabilities;
    private readonly IPlatformAccessUseCases _platformAccess;
    private readonly TranslationLanguageOptions _languages;
    private readonly SubtitleWindowCoordinator _subtitleWindow;
    private readonly ILogger<SpeechRecognitionViewModel> _logger;
    private readonly SpeechSubtitleProjection _subtitleProjection = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _recognitionCancellation;
    private Task? _recognitionTask;
    private SpeechEngineOption? _selectedEngineOption;
    private LanguageSettings? _selectedTargetLanguage;
    private SpeechRecognitionModel? _selectedRecognitionModel;
    private string _selectedSourcesSummary = Resources.Speech_AllSystemAudio;
    private bool _isSupported;
    private bool _isBusy;
    private bool _isRecording;
    private bool _isFloatingWindowOpen;
    private int _initialized;
    private long _nextErrorId;

    public SpeechRecognitionViewModel(
        SettingsSession settings,
        ISpeechRecognitionUseCases speech,
        ISpeechRecognitionModelCatalog models,
        IAudioCaptureSourceCatalog audioSources,
        IPlatformCapabilities capabilities,
        IPlatformAccessUseCases platformAccess,
        TranslationLanguageOptions languages,
        SubtitleWindowCoordinator subtitleWindow,
        ILogger<SpeechRecognitionViewModel> logger)
        : base(Resources.Page_SpeechRecognition, MaterialIconKind.Microphone, 4)
    {
        _settings = settings;
        _speech = speech;
        _models = models;
        _audioSources = audioSources;
        _capabilities = capabilities;
        _platformAccess = platformAccess;
        _languages = languages;
        _subtitleWindow = subtitleWindow;
        _logger = logger;

        RecognitionLanguages = [];
        EngineOptions = [];
        TargetLanguages = [];
        AvailableFonts = new ObservableCollection<string>(
            FontManager.Current.SystemFonts.Select(font => font.Name).Order(StringComparer.CurrentCulture));
        AudioSources = [];
        SubtitleItems = _subtitleProjection.SubtitleItems;
        FloatingSubtitles = _subtitleProjection.FloatingSubtitles;

        LoadEngineOptions();

        ToggleRecordingCommand = ReactiveCommand.CreateFromTask(ToggleRecordingAsync);
        RefreshSourcesCommand = ReactiveCommand.CreateFromTask(RefreshSourcesAsync);
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
        ApplyAppearancePresetCommand = ReactiveCommand.Create<SubtitleAppearancePreset>(ApplyAppearancePreset);
        ShowLiveWorkspaceCommand = ReactiveCommand.Create(() => { IsLiveWorkspace = true; });
        ShowOverlayWorkspaceCommand = ReactiveCommand.Create(() => { IsLiveWorkspace = false; });

        _subtitleWindow.VisibilityChanged += OnSubtitleWindowVisibilityChanged;
        _models.ModelsChanged += OnModelsChanged;
        _settings.AiModel.ConfiguredModels.CollectionChanged += (_, _) => LoadEngineOptions();
    }

    public ObservableCollection<SpeechRecognitionModel> RecognitionLanguages { get; }
    public ObservableCollection<SpeechEngineOption> EngineOptions { get; }
    public ObservableCollection<LanguageSettings> TargetLanguages { get; }
    public ObservableCollection<string> AvailableFonts { get; }
    public ObservableCollection<SpeechAudioSourceItem> AudioSources { get; }
    public ObservableCollection<SpeechSubtitleItemViewModel> SubtitleItems { get; }
    public ObservableCollection<SpeechSubtitleItemViewModel> FloatingSubtitles { get; }

    public IReadOnlyList<string> OrientationOptions { get; } = ["Horizontal", "Vertical"];
    public IReadOnlyList<SubtitleAppearancePreset> AppearancePresets { get; } = SubtitleAppearancePresets.All;
    public IReadOnlyList<KeyValuePair<FloatingDisplayMode, string>> DisplayModeOptions { get; } =
    [
        new(FloatingDisplayMode.Segmented, Resources.Speech_DisplayMode_Segmented),
        new(FloatingDisplayMode.AutoScroll, Resources.Speech_DisplayMode_AutoScroll)
    ];
    public IReadOnlyList<KeyValuePair<SubtitleSource, string>> MainSourceOptions { get; } =
    [
        new(SubtitleSource.Original, Resources.Subtitle_Source_Original),
        new(SubtitleSource.Translated, Resources.Subtitle_Source_Translated)
    ];
    public IReadOnlyList<KeyValuePair<SubtitleSource, string>> SecondarySourceOptions { get; } =
    [
        new(SubtitleSource.None, Resources.Subtitle_Source_None),
        new(SubtitleSource.Original, Resources.Subtitle_Source_Original),
        new(SubtitleSource.Translated, Resources.Subtitle_Source_Translated)
    ];

    public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshSourcesCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearHistoryCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleFloatingWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleLockCommand { get; }
    public ReactiveCommand<Unit, Unit> UnlockFloatingWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> IncreaseFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> DecreaseFontSizeCommand { get; }
    public ReactiveCommand<SubtitleAppearancePreset, Unit> ApplyAppearancePresetCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowLiveWorkspaceCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowOverlayWorkspaceCommand { get; }

    private bool _isLiveWorkspace = true;
    public bool IsLiveWorkspace
    {
        get => _isLiveWorkspace;
        set
        {
            if (_isLiveWorkspace == value)
                return;
            this.RaiseAndSetIfChanged(ref _isLiveWorkspace, value);
            this.RaisePropertyChanged(nameof(IsOverlayWorkspace));
        }
    }
    public bool IsOverlayWorkspace
    {
        get => !_isLiveWorkspace;
        set
        {
            if (value)
                IsLiveWorkspace = false;
        }
    }

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
        get => _selectedEngineOption;
        set
        {
            if (_selectedEngineOption == value) return;
            this.RaiseAndSetIfChanged(ref _selectedEngineOption, value);
            if (value is not null)
            {
                _settings.SpeechRecognition.EngineId = value.Id;
                _settings.SpeechRecognition.EngineType = value.IsMachine ? 0 : 1;
            }
            this.RaisePropertyChanged(nameof(IsMaxSentencesPerLineVisible));
            this.RaisePropertyChanged(nameof(IsRealTimePreviewVisible));
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

    public bool IsTranslationEnabled { get => _settings.SpeechRecognition.IsTranslationEnabled; set { Set(value, _settings.SpeechRecognition.IsTranslationEnabled, next => _settings.SpeechRecognition.IsTranslationEnabled = next); this.RaisePropertyChanged(nameof(IsRealTimePreviewVisible)); } }
    public bool IsRealTimePreviewEnabled { get => _settings.SpeechRecognition.IsRealTimePreviewEnabled; set => Set(value, _settings.SpeechRecognition.IsRealTimePreviewEnabled, next => _settings.SpeechRecognition.IsRealTimePreviewEnabled = next); }
    public bool IsRealTimePreviewVisible =>
        ShouldShowRealTimePreview(IsTranslationEnabled, SelectedEngineOption?.IsMachine == true);
    public int AutoClearInterval { get => _settings.SpeechRecognition.AutoClearInterval; set => Set(value, _settings.SpeechRecognition.AutoClearInterval, next => _settings.SpeechRecognition.AutoClearInterval = next); }
    public int MaxSentencesPerLine { get => _settings.SpeechRecognition.MaxSentencesPerLine; set => Set(value, _settings.SpeechRecognition.MaxSentencesPerLine, next => _settings.SpeechRecognition.MaxSentencesPerLine = next); }
    public FloatingDisplayMode FloatingDisplayMode { get => _settings.SpeechRecognition.FloatingDisplayMode; set { Set(value, _settings.SpeechRecognition.FloatingDisplayMode, next => _settings.SpeechRecognition.FloatingDisplayMode = next); this.RaisePropertyChanged(nameof(IsSegmentedMode)); this.RaisePropertyChanged(nameof(IsMaxSentencesPerLineVisible)); } }
    public bool IsSegmentedMode => FloatingDisplayMode == FloatingDisplayMode.Segmented;
    public bool IsMaxSentencesPerLineVisible =>
        ShouldShowMaxSentencesPerLine(FloatingDisplayMode, SelectedEngineOption?.IsMachine == true);
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
        _recognitionCancellation?.Cancel();
        _recognitionCancellation?.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _subtitleWindow.VisibilityChanged -= OnSubtitleWindowVisibilityChanged;
        _models.ModelsChanged -= OnModelsChanged;
        _subtitleWindow.Close();
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
        if (IsBusy || !IsSupported)
            return;
        if (IsRecording)
        {
            IsBusy = true;
            _recognitionCancellation?.Cancel();
            if (_recognitionTask is not null)
            {
                try { await _recognitionTask; }
                catch (OperationCanceledException) { }
            }
            IsBusy = false;
            return;
        }
        if (SelectedRecognitionModel is null)
            return;

        if (_recognitionCancellation is not null)
        {
            _recognitionCancellation.Cancel();
            if (_recognitionTask is not null)
            {
                try { await _recognitionTask; }
                catch (OperationCanceledException) { }
            }
            _recognitionCancellation.Dispose();
        }
        _recognitionCancellation = new CancellationTokenSource();
        var command = new SpeechRecognitionCommand(
            SelectedRecognitionModel.Id,
            SelectedRecognitionModel.Id,
            AudioSources.Where(source => source.IsSelected)
                .Select(source => new AudioCaptureSourceReference(source.Token, source.Kind))
                .ToArray());
        IsRecording = true;
        _recognitionTask = ConsumeRecognitionAsync(command, _recognitionCancellation.Token);
    }

    private async Task ConsumeRecognitionAsync(
        SpeechRecognitionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _speech.RecognizeAsync(command, cancellationToken)
                               .ConfigureAwait(false))
            {
                await Dispatcher.UIThread.InvokeAsync(() => Apply(item));
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
                IsRecording = false;
                IsBusy = false;
            });
        }
    }

    private void Apply(SpeechSessionEvent item)
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
            case SpeechFloatingSubtitleRemovedEvent removed:
                BeginFloatingSubtitleRemoval(removed.SubtitleId);
                break;
            case SpeechSessionErrorEvent error:
                AddError(error.Message);
                break;
            case SpeechSessionStoppedEvent:
                IsRecording = false;
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

        var selected = AudioSources.Where(source => source.IsSelected)
            .Select(source => source.Token)
            .ToHashSet();
        foreach (var source in AudioSources)
        {
            source.PropertyChanged -= OnSourcePropertyChanged;
            source.Dispose();
        }
        AudioSources.Clear();

        var available = await _audioSources.GetSourcesAsync(cancellationToken);
        foreach (var descriptor in available)
        {
            var item = new SpeechAudioSourceItem(
                descriptor,
                selected.Count == 0
                    ? descriptor.Kind == AudioCaptureSourceKind.SystemOutput
                    : selected.Contains(descriptor.Token));
            item.PropertyChanged += OnSourcePropertyChanged;
            AudioSources.Add(item);
        }
        UpdateSourceSummary();
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SpeechAudioSourceItem.IsSelected))
            UpdateSourceSummary();
    }

    private void UpdateSourceSummary()
    {
        var selected = AudioSources.Where(source => source.IsSelected).ToArray();
        SelectedSourcesSummary = selected.Length switch
        {
            0 => Resources.Speech_AllSystemAudio,
            1 => selected[0].Name,
            _ => string.Format(Resources.Speech_SelectedAppsCount, selected.Length)
        };
    }

    private void LoadEngineOptions()
    {
        var selectedId = _selectedEngineOption?.Id ?? _settings.SpeechRecognition.EngineId;
        var selectedMachine = _selectedEngineOption?.IsMachine
                              ?? _settings.SpeechRecognition.EngineType == 0;
        EngineOptions.Clear();
        foreach (var option in CreateMachineEngineOptions(_settings.MachineTranslation))
            EngineOptions.Add(option);
        foreach (var model in _settings.AiModel.ConfiguredModels)
            EngineOptions.Add(new SpeechEngineOption(model.Name, model.Id, false));
        _selectedEngineOption = ResolveAndSynchronizeEngineOption(
            EngineOptions,
            selectedId,
            selectedMachine,
            _settings.SpeechRecognition);
        var engineFellBack = !MatchesEngineSelection(
            _selectedEngineOption,
            selectedId,
            selectedMachine);
        this.RaisePropertyChanged(nameof(SelectedEngineOption));
        this.RaisePropertyChanged(nameof(IsMaxSentencesPerLineVisible));
        this.RaisePropertyChanged(nameof(IsRealTimePreviewVisible));
        UpdateTargetLanguages(commitSelection: engineFellBack);
    }

    internal static IReadOnlyList<SpeechEngineOption> CreateMachineEngineOptions(
        LiveMachineTranslationSettings settings) =>
    [
        new(MachineTranslationProviderNames.Baidu, settings.Baidu.Id, IsMachine: true),
        new(MachineTranslationProviderNames.Tencent, settings.Tencent.Id, IsMachine: true),
        new(MachineTranslationProviderNames.Google, settings.Google.Id, IsMachine: true),
        new(MachineTranslationProviderNames.DeepL, settings.DeepL.Id, IsMachine: true)
    ];

    internal static SpeechEngineOption? ResolveAndSynchronizeEngineOption(
        IReadOnlyList<SpeechEngineOption> options,
        string selectedId,
        bool selectedMachine,
        LiveSpeechRecognitionSettings settings)
    {
        var selected = options.FirstOrDefault(option =>
                           option.Id == selectedId && option.IsMachine == selectedMachine)
                       ?? (selectedMachine
                           ? options.FirstOrDefault(option =>
                               option.IsMachine
                               && option.Name.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                           : null)
                       ?? options.FirstOrDefault();
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
        foreach (var language in _languages.All.Where(language =>
                     SupportsTargetLanguage(language, _selectedEngineOption)))
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

    private void ApplyAppearancePreset(SubtitleAppearancePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        PrimaryFontSize = preset.PrimaryFontSize;
        PrimaryFontColor = preset.PrimaryFontColor;
        SecondaryFontSize = preset.SecondaryFontSize;
        SecondaryFontColor = preset.SecondaryFontColor;
        BackgroundColor = preset.BackgroundColor;
        SubtitleBackgroundColor = preset.SubtitleBackgroundColor;
        WindowOpacity = preset.WindowOpacity;
    }

    private void Set<T>(T value, T current, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(value, current))
            return;
        apply(value);
        this.RaisePropertyChanged(propertyName);
    }
}
