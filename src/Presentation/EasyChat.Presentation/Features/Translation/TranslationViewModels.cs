using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Threading;
using EasyChat.Contracts.SelectionTranslation;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Translation;
using EasyChat.Presentation.Shared.Controls;
using EasyChat.Presentation.Foundation.Localization;
using LiveMarkdown.Avalonia;
using Markdig;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Translation;

public sealed class TranslationDictionaryWindowViewModel : EasyChat.Presentation.Foundation.Navigation.ViewModelBase
{
    private readonly ISelectionTranslationUseCases _translation;
    private readonly ITranslationLanguageCatalog _languages;
    private readonly ITtsUseCases _tts;
    private readonly SettingsSession _settings;
    private string _sourceText = string.Empty;
    private string _translationResult = string.Empty;
    private string? _sentenceTranslationSnapshot;
    private string _sourceLanguageId = "auto";
    private string _targetLanguageId = "zh-Hans";
    private string _configuredSourceLanguageId = "auto";
    private string _configuredTargetLanguageId = "zh-Hans";
    private bool _isLoading;
    private bool _isWordMode;
    private bool _canNavigateBack;
    private bool _showBackButton;
    private bool _showCloseButton;
    private bool _isWordTtsLoading;
    private bool _isSourceTtsLoading;
    private bool _isResultTtsLoading;
    private bool _isWordTtsPlaying;
    private bool _isSourceTtsPlaying;
    private bool _isResultTtsPlaying;
    private ITtsPlaybackSession? _wordTtsSession;
    private ITtsPlaybackSession? _sourceTtsSession;
    private ITtsPlaybackSession? _resultTtsSession;
    private CancellationTokenSource? _wordTtsCancellation;
    private CancellationTokenSource? _sourceTtsCancellation;
    private CancellationTokenSource? _resultTtsCancellation;
    private bool _isBritishTtsLoading;
    private bool _isBritishTtsPlaying;
    private bool _isAmericanTtsLoading;
    private bool _isAmericanTtsPlaying;
    private ITtsPlaybackSession? _britishTtsSession;
    private ITtsPlaybackSession? _americanTtsSession;
    private CancellationTokenSource? _britishTtsCancellation;
    private CancellationTokenSource? _americanTtsCancellation;
    private bool _isBritishSourceTtsLoading;
    private bool _isBritishSourceTtsPlaying;
    private bool _isAmericanSourceTtsLoading;
    private bool _isAmericanSourceTtsPlaying;
    private bool _isBritishTargetTtsLoading;
    private bool _isBritishTargetTtsPlaying;
    private bool _isAmericanTargetTtsLoading;
    private bool _isAmericanTargetTtsPlaying;
    private ITtsPlaybackSession? _britishSourceTtsSession;
    private ITtsPlaybackSession? _americanSourceTtsSession;
    private ITtsPlaybackSession? _britishTargetTtsSession;
    private ITtsPlaybackSession? _americanTargetTtsSession;
    private CancellationTokenSource? _britishSourceTtsCancellation;
    private CancellationTokenSource? _americanSourceTtsCancellation;
    private CancellationTokenSource? _britishTargetTtsCancellation;
    private CancellationTokenSource? _americanTargetTtsCancellation;
    private int _loadingOperations;
    private SelectionTranslationConfigurationScope _configurationScope =
        SelectionTranslationConfigurationScope.Selection;
    private DictionaryResultViewModel? _dictionaryResult;
    private ObservableCollection<TextToken> _sourceTokens = [];

    public TranslationDictionaryWindowViewModel(
        ISelectionTranslationUseCases translation,
        ITranslationLanguageCatalog languages,
        ITtsUseCases tts,
        SettingsSession settings)
    {
        _translation = translation;
        _languages = languages;
        _tts = tts;
        _settings = settings;
        LookupWordCommand = ReactiveCommand.CreateFromTask<string>(LookupWordAsync);
        SwitchToSentenceModeCommand = ReactiveCommand.Create(SwitchToSentenceMode);
        // These commands must remain executable while a playback session is active so the same
        // button can stop it. ReactiveCommand.CreateFromTask disables itself until the task ends.
        PlayTtsCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayTtsAsync(parameter); });
        PlaySourceAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlaySourceAudioAsync(parameter); });
        PlayTargetAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayTargetAudioAsync(parameter); });
        PlayBritishAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayBritishAudioAsync(parameter); });
        PlayAmericanAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayAmericanAudioAsync(parameter); });
        PlayBritishSourceAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayBritishSourceAudioAsync(parameter); });
        PlayAmericanSourceAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayAmericanSourceAudioAsync(parameter); });
        PlayBritishTargetAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayBritishTargetAudioAsync(parameter); });
        PlayAmericanTargetAudioCommand = ReactiveCommand.Create<object?>(parameter => { _ = PlayAmericanTargetAudioAsync(parameter); });
    }

    public string SourceText
    {
        get => _sourceText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _sourceText, value);
            SourceTokens = new ObservableCollection<TextToken>(
                TranslationTextTokenizer.Tokenize(value, _sourceLanguageId));
        }
    }
    public string TranslationResult
    {
        get => _translationResult;
        private set
        {
            this.RaiseAndSetIfChanged(ref _translationResult, value);
            this.RaisePropertyChanged(nameof(ShowTranslationSkeleton));
        }
    }
    public ObservableStringBuilder TranslationMarkdown { get; } = new();
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isLoading, value);
            this.RaisePropertyChanged(nameof(ShowDictionarySkeleton));
            this.RaisePropertyChanged(nameof(ShowTranslationSkeleton));
        }
    }
    public bool IsWordMode
    {
        get => _isWordMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isWordMode, value);
            ShowBackButton = value && _canNavigateBack;
            this.RaisePropertyChanged(nameof(ShowDictionarySkeleton));
            this.RaisePropertyChanged(nameof(ShowTranslationSkeleton));
            this.RaisePropertyChanged(nameof(ShowEnglishSourceAudio));
            this.RaisePropertyChanged(nameof(ShowEnglishTargetAudio));
        }
    }
    public DictionaryResultViewModel? DictionaryResult
    {
        get => _dictionaryResult;
        private set
        {
            this.RaiseAndSetIfChanged(ref _dictionaryResult, value);
            this.RaisePropertyChanged(nameof(ShowDictionarySkeleton));
            this.RaisePropertyChanged(nameof(ShowGenericWordAudio));
        }
    }
    public ObservableCollection<TextToken> SourceTokens { get => _sourceTokens; private set => this.RaiseAndSetIfChanged(ref _sourceTokens, value); }
    public string SourceLanguageDisplay => DisplayLanguage(_configuredSourceLanguageId);
    public string TargetLanguageDisplay => DisplayLanguage(_configuredTargetLanguageId);
    public bool ShowDictionarySkeleton => IsWordMode && IsLoading && (DictionaryResult is null || DictionaryResult.Parts.Count == 0);
    public bool ShowTranslationSkeleton => !IsWordMode && IsLoading && string.IsNullOrEmpty(TranslationResult);
    public bool ShowBackButton { get => _showBackButton; private set => this.RaiseAndSetIfChanged(ref _showBackButton, value); }
    public bool ShowCloseButton { get => _showCloseButton; set => this.RaiseAndSetIfChanged(ref _showCloseButton, value); }
    public bool IsWordTtsLoading { get => _isWordTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isWordTtsLoading, value); this.RaisePropertyChanged(nameof(ShowWordTtsIcon)); } }
    public bool IsSourceTtsLoading { get => _isSourceTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isSourceTtsLoading, value); this.RaisePropertyChanged(nameof(ShowSourceTtsIcon)); } }
    public bool IsResultTtsLoading { get => _isResultTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isResultTtsLoading, value); this.RaisePropertyChanged(nameof(ShowResultTtsIcon)); } }
    public bool IsWordTtsPlaying { get => _isWordTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isWordTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowWordTtsIcon)); } }
    public bool IsSourceTtsPlaying { get => _isSourceTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isSourceTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowSourceTtsIcon)); } }
    public bool IsResultTtsPlaying { get => _isResultTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isResultTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowResultTtsIcon)); } }
    public bool IsBritishTtsLoading { get => _isBritishTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isBritishTtsLoading, value); this.RaisePropertyChanged(nameof(ShowBritishTtsIcon)); } }
    public bool IsBritishTtsPlaying { get => _isBritishTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isBritishTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowBritishTtsIcon)); } }
    public bool IsAmericanTtsLoading { get => _isAmericanTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isAmericanTtsLoading, value); this.RaisePropertyChanged(nameof(ShowAmericanTtsIcon)); } }
    public bool IsAmericanTtsPlaying { get => _isAmericanTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isAmericanTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowAmericanTtsIcon)); } }
    public bool IsBritishSourceTtsLoading { get => _isBritishSourceTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isBritishSourceTtsLoading, value); this.RaisePropertyChanged(nameof(ShowBritishSourceTtsIcon)); } }
    public bool IsBritishSourceTtsPlaying { get => _isBritishSourceTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isBritishSourceTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowBritishSourceTtsIcon)); } }
    public bool IsAmericanSourceTtsLoading { get => _isAmericanSourceTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isAmericanSourceTtsLoading, value); this.RaisePropertyChanged(nameof(ShowAmericanSourceTtsIcon)); } }
    public bool IsAmericanSourceTtsPlaying { get => _isAmericanSourceTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isAmericanSourceTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowAmericanSourceTtsIcon)); } }
    public bool IsBritishTargetTtsLoading { get => _isBritishTargetTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isBritishTargetTtsLoading, value); this.RaisePropertyChanged(nameof(ShowBritishTargetTtsIcon)); } }
    public bool IsBritishTargetTtsPlaying { get => _isBritishTargetTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isBritishTargetTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowBritishTargetTtsIcon)); } }
    public bool IsAmericanTargetTtsLoading { get => _isAmericanTargetTtsLoading; private set { this.RaiseAndSetIfChanged(ref _isAmericanTargetTtsLoading, value); this.RaisePropertyChanged(nameof(ShowAmericanTargetTtsIcon)); } }
    public bool IsAmericanTargetTtsPlaying { get => _isAmericanTargetTtsPlaying; private set { this.RaiseAndSetIfChanged(ref _isAmericanTargetTtsPlaying, value); this.RaisePropertyChanged(nameof(ShowAmericanTargetTtsIcon)); } }
    public bool ShowWordTtsIcon => !IsWordTtsLoading && !IsWordTtsPlaying;
    public bool ShowSourceTtsIcon => !IsSourceTtsLoading && !IsSourceTtsPlaying;
    public bool ShowResultTtsIcon => !IsResultTtsLoading && !IsResultTtsPlaying;
    public bool ShowBritishTtsIcon => !IsBritishTtsLoading && !IsBritishTtsPlaying;
    public bool ShowAmericanTtsIcon => !IsAmericanTtsLoading && !IsAmericanTtsPlaying;
    public bool ShowBritishSourceTtsIcon => !IsBritishSourceTtsLoading && !IsBritishSourceTtsPlaying;
    public bool ShowAmericanSourceTtsIcon => !IsAmericanSourceTtsLoading && !IsAmericanSourceTtsPlaying;
    public bool ShowBritishTargetTtsIcon => !IsBritishTargetTtsLoading && !IsBritishTargetTtsPlaying;
    public bool ShowAmericanTargetTtsIcon => !IsAmericanTargetTtsLoading && !IsAmericanTargetTtsPlaying;
    public string BritishFlagIcon => "gb.png";
    public string AmericanFlagIcon => "us.png";
    public bool ShowEnglishPronunciation => string.Equals(
        GetPrimaryLanguage(_sourceLanguageId), "en", StringComparison.OrdinalIgnoreCase);
    public bool ShowEnglishSourceAudio => !IsWordMode && ShowEnglishLanguage(_sourceLanguageId);
    public bool ShowEnglishTargetAudio => !IsWordMode && ShowEnglishLanguage(_targetLanguageId);
    public bool ShowGenericWordAudio => !ShowEnglishPronunciation
        && !string.IsNullOrWhiteSpace(DictionaryResult?.Phonetic);
    public ReactiveCommand<string, Unit> LookupWordCommand { get; }
    public ReactiveCommand<Unit, Unit> SwitchToSentenceModeCommand { get; }
    public ReactiveCommand<object?, Unit> PlayTtsCommand { get; }
    public ReactiveCommand<object?, Unit> PlaySourceAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayTargetAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayBritishAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayAmericanAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayBritishSourceAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayAmericanSourceAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayBritishTargetAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayAmericanTargetAudioCommand { get; }

    public Task InitializeAsync(
        string text,
        SelectionTranslationConfigurationScope configurationScope = SelectionTranslationConfigurationScope.Selection) => InitializeAsync(
        text,
        _settings.General.SourceLanguage.Id,
        _settings.General.TargetLanguage.Id,
        configurationScope);

    public async Task InitializeAsync(
        string text,
        string sourceLanguageId,
        string targetLanguageId,
        SelectionTranslationConfigurationScope configurationScope = SelectionTranslationConfigurationScope.Selection)
    {
        _configurationScope = configurationScope;
        SetLanguageDirection(sourceLanguageId, targetLanguageId);
        SourceText = text;
        _sentenceTranslationSnapshot = null;
        await Dispatcher.UIThread.InvokeAsync(() => TranslationMarkdown.Clear());
        BeginLoading();
        try
        {
            await StreamAsync(text, dictionary: false, canNavigateBack: false);
        }
        catch (Exception exception)
        {
            await SetErrorAsync(exception);
        }
        finally
        {
            EndLoading();
        }
    }

    public async Task InitializeDictionaryAsync(
        string text,
        string sourceLanguageId,
        string targetLanguageId,
        SelectionTranslationConfigurationScope configurationScope = SelectionTranslationConfigurationScope.Selection)
    {
        _configurationScope = configurationScope;
        SetLanguageDirection(sourceLanguageId, targetLanguageId);
        SourceText = text;
        IsWordMode = true;
        _canNavigateBack = false;
        DictionaryResult = new DictionaryResultViewModel { Word = text };
        TranslationResult = string.Empty;
        _sentenceTranslationSnapshot = null;
        await Dispatcher.UIThread.InvokeAsync(() => TranslationMarkdown.Clear());
        BeginLoading();
        try
        {
            await StreamAsync(text, dictionary: true, canNavigateBack: false);
        }
        catch (Exception exception)
        {
            await SetErrorAsync(exception);
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task StreamAsync(string text, bool dictionary, bool canNavigateBack)
    {
        var request = new SelectionTranslationRequest(
            text,
            _languages.Get(_sourceLanguageId),
            _languages.Get(_targetLanguageId));
        var stream = dictionary
            ? _translation.StreamDictionaryAsync(request, configurationScope: _configurationScope)
            : _translation.StreamAsync(request, configurationScope: _configurationScope);
        await foreach (var item in stream)
            await Dispatcher.UIThread.InvokeAsync(() => Apply(item, canNavigateBack, dictionary));
    }

    private void SetLanguageDirection(string sourceLanguageId, string targetLanguageId)
    {
        _sourceLanguageId = string.IsNullOrWhiteSpace(sourceLanguageId) ? "auto" : sourceLanguageId;
        _targetLanguageId = string.IsNullOrWhiteSpace(targetLanguageId) ? "zh-Hans" : targetLanguageId;
        _configuredSourceLanguageId = _sourceLanguageId;
        _configuredTargetLanguageId = _targetLanguageId;
        this.RaisePropertyChanged(nameof(SourceLanguageDisplay));
        this.RaisePropertyChanged(nameof(TargetLanguageDisplay));
        this.RaisePropertyChanged(nameof(ShowEnglishPronunciation));
        this.RaisePropertyChanged(nameof(ShowEnglishSourceAudio));
        this.RaisePropertyChanged(nameof(ShowEnglishTargetAudio));
    }

    private string DisplayLanguage(string languageId)
    {
        var language = _languages.Get(languageId);
        return LanguageDisplayNames.ForUi(language.NativeName, language.EnglishName);
    }

    private void Apply(SelectionTranslationEvent item, bool canNavigateBack, bool lookup)
    {
        switch (item)
        {
            case SelectionTranslationStartedEvent started:
                IsWordMode = started.Mode == SelectionTranslationMode.Word;
                _canNavigateBack = IsWordMode && canNavigateBack;
                ShowBackButton = IsWordMode && _canNavigateBack;
                TranslationResult = string.Empty;
                TranslationMarkdown.Clear();
                DictionaryResult = IsWordMode ? new DictionaryResultViewModel { Word = DictionaryResult?.Word ?? SourceText } : null;
                if (!IsWordMode && !lookup)
                    _sentenceTranslationSnapshot = string.Empty;
                break;
            case SelectionTranslationSourceDetectedEvent detected:
                if (!string.IsNullOrWhiteSpace(detected.Language))
                {
                    _sourceLanguageId = detected.Language;
                    this.RaisePropertyChanged(nameof(ShowEnglishPronunciation));
                    this.RaisePropertyChanged(nameof(ShowEnglishSourceAudio));
                    this.RaisePropertyChanged(nameof(ShowEnglishTargetAudio));
                    SourceTokens = new ObservableCollection<TextToken>(
                        TranslationTextTokenizer.Tokenize(SourceText, detected.Language));
                }
                break;
            case SelectionTranslationDeltaEvent delta:
                TranslationResult += delta.Text;
                TranslationMarkdown.Append(TranslationMarkdownFormatter.PreserveLineBreaks(delta.Text));
                if (!lookup)
                    _sentenceTranslationSnapshot = TranslationResult;
                break;
            case SelectionTranslationWordHeaderEvent header:
                EnsureDictionary().Word = header.Word;
                EnsureDictionary().Phonetic = header.Phonetic ?? string.Empty;
                this.RaisePropertyChanged(nameof(ShowGenericWordAudio));
                break;
            case SelectionTranslationDefinitionEvent definition:
                var part = EnsureDictionary().Parts.FirstOrDefault(value => value.PartOfSpeech == (definition.Pos ?? string.Empty));
                if (part is null)
                {
                    part = new DictionaryPartViewModel { PartOfSpeech = definition.Pos ?? string.Empty };
                    EnsureDictionary().Parts.Add(part);
                }
                part.Definitions.Add(definition.Meaning);
                this.RaisePropertyChanged(nameof(ShowDictionarySkeleton));
                break;
            case SelectionTranslationFormEvent form:
                EnsureDictionary().Forms.Add(new DictionaryFormViewModel { Label = form.Label, Word = form.Word });
                break;
            case SelectionTranslationTipsEvent tips:
                EnsureDictionary().Tips = tips.Text;
                break;
            case SelectionTranslationExampleEvent example:
                EnsureDictionary().Examples.Add(new DictionaryExampleViewModel
                {
                    Origin = example.Origin,
                    Translation = example.Translation
                });
                break;
        }
    }

    private DictionaryResultViewModel EnsureDictionary() => DictionaryResult ??= new DictionaryResultViewModel();

    private async Task LookupWordAsync(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;
        _sentenceTranslationSnapshot ??= TranslationResult;
        IsWordMode = true;
        _canNavigateBack = true;
        ShowBackButton = true;
        DictionaryResult = new DictionaryResultViewModel { Word = word };
        TranslationResult = string.Empty;
        TranslationMarkdown.Clear();
        BeginLoading();
        try
        {
            await StreamAsync(word, dictionary: true, canNavigateBack: true);
        }
        catch (Exception exception)
        {
            await SetErrorAsync(exception);
        }
        finally
        {
            EndLoading();
        }
    }

    private void SwitchToSentenceMode()
    {
        IsWordMode = false;
        TranslationResult = _sentenceTranslationSnapshot ?? TranslationResult;
        TranslationMarkdown.Clear();
        TranslationMarkdown.Append(TranslationMarkdownFormatter.PreserveLineBreaks(TranslationResult));
    }

    private async Task SetErrorAsync(Exception exception)
    {
        var message = FormatError(exception);
        TranslationResult = message;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TranslationMarkdown.Clear();
            TranslationMarkdown.Append(TranslationMarkdownFormatter.PreserveLineBreaks(message));
        });
    }

    private Task PlayTtsAsync(object? parameter)
    {
        var text = parameter as string ?? (IsWordMode ? DictionaryResult?.Word : TranslationResult);
        return PlayAsync(text, IsWordMode ? _sourceLanguageId : _targetLanguageId,
            () => IsWordTtsLoading, value => IsWordTtsLoading = value,
            () => IsWordTtsPlaying, value => IsWordTtsPlaying = value,
            () => _wordTtsSession, value => _wordTtsSession = value,
            () => _wordTtsCancellation, value => _wordTtsCancellation = value);
    }

    private Task PlaySourceAudioAsync(object? parameter) => parameter switch
    {
        DictionaryFormViewModel form => PlayAsync(form.Word, _sourceLanguageId,
            () => form.IsLoading, value => form.IsLoading = value,
            () => form.IsPlaying, value => form.IsPlaying = value,
            () => form.Session, value => form.Session = value,
            () => form.Cancellation, value => form.Cancellation = value),
        DictionaryExampleViewModel example => PlayAsync(example.Origin, _sourceLanguageId,
            () => example.IsOriginLoading, value => example.IsOriginLoading = value,
            () => example.IsOriginPlaying, value => example.IsOriginPlaying = value,
            () => example.OriginSession, value => example.OriginSession = value,
            () => example.OriginCancellation, value => example.OriginCancellation = value),
        string text => PlayAsync(text, _sourceLanguageId,
            () => IsSourceTtsLoading, value => IsSourceTtsLoading = value,
            () => IsSourceTtsPlaying, value => IsSourceTtsPlaying = value,
            () => _sourceTtsSession, value => _sourceTtsSession = value,
            () => _sourceTtsCancellation, value => _sourceTtsCancellation = value),
        _ => Task.CompletedTask
    };

    private Task PlayTargetAudioAsync(object? parameter) => parameter switch
    {
        DictionaryExampleViewModel example => PlayAsync(example.Translation, _targetLanguageId,
            () => example.IsTranslationLoading, value => example.IsTranslationLoading = value,
            () => example.IsTranslationPlaying, value => example.IsTranslationPlaying = value,
            () => example.TranslationSession, value => example.TranslationSession = value,
            () => example.TranslationCancellation, value => example.TranslationCancellation = value),
        string text => PlayAsync(text, _targetLanguageId,
            () => IsResultTtsLoading, value => IsResultTtsLoading = value,
            () => IsResultTtsPlaying, value => IsResultTtsPlaying = value,
            () => _resultTtsSession, value => _resultTtsSession = value,
            () => _resultTtsCancellation, value => _resultTtsCancellation = value),
        _ => Task.CompletedTask
    };

    private Task PlayBritishAudioAsync(object? parameter) => PlayRegionalAudioAsync(parameter, "en-GB");

    private Task PlayAmericanAudioAsync(object? parameter) => PlayRegionalAudioAsync(parameter, "en-US");

    private Task PlayBritishSourceAudioAsync(object? parameter) => PlaySentenceRegionalAudioAsync(
        parameter as string ?? SourceText, "en-GB", source: true, british: true);

    private Task PlayAmericanSourceAudioAsync(object? parameter) => PlaySentenceRegionalAudioAsync(
        parameter as string ?? SourceText, "en-US", source: true, british: false);

    private Task PlayBritishTargetAudioAsync(object? parameter) => PlaySentenceRegionalAudioAsync(
        parameter as string ?? TranslationResult, "en-GB", source: false, british: true);

    private Task PlayAmericanTargetAudioAsync(object? parameter) => PlaySentenceRegionalAudioAsync(
        parameter as string ?? TranslationResult, "en-US", source: false, british: false);

    private Task PlaySentenceRegionalAudioAsync(string text, string locale, bool source, bool british) =>
        PlayAsync(
            text,
            locale,
            source
                ? british ? () => IsBritishSourceTtsLoading : () => IsAmericanSourceTtsLoading
                : british ? () => IsBritishTargetTtsLoading : () => IsAmericanTargetTtsLoading,
            source
                ? british ? value => IsBritishSourceTtsLoading = value : value => IsAmericanSourceTtsLoading = value
                : british ? value => IsBritishTargetTtsLoading = value : value => IsAmericanTargetTtsLoading = value,
            source
                ? british ? () => IsBritishSourceTtsPlaying : () => IsAmericanSourceTtsPlaying
                : british ? () => IsBritishTargetTtsPlaying : () => IsAmericanTargetTtsPlaying,
            source
                ? british ? value => IsBritishSourceTtsPlaying = value : value => IsAmericanSourceTtsPlaying = value
                : british ? value => IsBritishTargetTtsPlaying = value : value => IsAmericanTargetTtsPlaying = value,
            source
                ? british ? () => _britishSourceTtsSession : () => _americanSourceTtsSession
                : british ? () => _britishTargetTtsSession : () => _americanTargetTtsSession,
            source
                ? british ? value => _britishSourceTtsSession = value : value => _americanSourceTtsSession = value
                : british ? value => _britishTargetTtsSession = value : value => _americanTargetTtsSession = value,
            source
                ? british ? () => _britishSourceTtsCancellation : () => _americanSourceTtsCancellation
                : british ? () => _britishTargetTtsCancellation : () => _americanTargetTtsCancellation,
            source
                ? british ? value => _britishSourceTtsCancellation = value : value => _americanSourceTtsCancellation = value
                : british ? value => _britishTargetTtsCancellation = value : value => _americanTargetTtsCancellation = value);

    private Task PlayRegionalAudioAsync(object? parameter, string locale)
    {
        if (parameter is DictionaryExampleViewModel example)
        {
            var isBritish = string.Equals(locale, "en-GB", StringComparison.OrdinalIgnoreCase);
            return PlayAsync(example.Origin, locale,
                isBritish ? () => example.IsBritishLoading : () => example.IsAmericanLoading,
                isBritish ? value => example.IsBritishLoading = value : value => example.IsAmericanLoading = value,
                isBritish ? () => example.IsBritishPlaying : () => example.IsAmericanPlaying,
                isBritish ? value => example.IsBritishPlaying = value : value => example.IsAmericanPlaying = value,
                isBritish ? () => example.BritishSession : () => example.AmericanSession,
                isBritish ? value => example.BritishSession = value : value => example.AmericanSession = value,
                isBritish ? () => example.BritishCancellation : () => example.AmericanCancellation,
                isBritish ? value => example.BritishCancellation = value : value => example.AmericanCancellation = value);
        }

        var text = parameter as string ?? DictionaryResult?.Word;
        return PlayAsync(text, locale,
            locale == "en-GB" ? () => IsBritishTtsLoading : () => IsAmericanTtsLoading,
            locale == "en-GB" ? value => IsBritishTtsLoading = value : value => IsAmericanTtsLoading = value,
            locale == "en-GB" ? () => IsBritishTtsPlaying : () => IsAmericanTtsPlaying,
            locale == "en-GB" ? value => IsBritishTtsPlaying = value : value => IsAmericanTtsPlaying = value,
            locale == "en-GB" ? () => _britishTtsSession : () => _americanTtsSession,
            locale == "en-GB" ? value => _britishTtsSession = value : value => _americanTtsSession = value,
            locale == "en-GB" ? () => _britishTtsCancellation : () => _americanTtsCancellation,
            locale == "en-GB" ? value => _britishTtsCancellation = value : value => _americanTtsCancellation = value);
    }

    private static string GetPrimaryLanguage(string languageId) =>
        languageId.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];

    private static bool ShowEnglishLanguage(string languageId) =>
        string.Equals(GetPrimaryLanguage(languageId), "en", StringComparison.OrdinalIgnoreCase);

    private async Task PlayAsync(
        string? text,
        string languageId,
        Func<bool> isLoading,
        Action<bool> setLoading,
        Func<bool> isPlaying,
        Action<bool> setPlaying,
        Func<ITtsPlaybackSession?> getSession,
        Action<ITtsPlaybackSession?> setSession,
        Func<CancellationTokenSource?> getCancellation,
        Action<CancellationTokenSource?> setCancellation)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (isPlaying())
        {
            getCancellation()?.Cancel();
            var current = getSession();
            setSession(null);
            setPlaying(false);
            if (current is not null)
                await current.StopAsync().ConfigureAwait(false);
            return;
        }
        if (isLoading())
        {
            getCancellation()?.Cancel();
            return;
        }

        using var cancellation = new CancellationTokenSource();
        setCancellation(cancellation);
        setLoading(true);
        ITtsPlaybackSession? session = null;
        try
        {
            var voice = await _tts.ResolvePreferredVoiceAsync(languageId, cancellationToken: cancellation.Token)
                .ConfigureAwait(false);
            if (!voice.IsSuccess || string.IsNullOrWhiteSpace(voice.Value))
                return;
            var result = await _tts.EnqueueWithSessionAsync(
                new TtsSynthesisRequest(text, voice.Value),
                interruptCurrent: true,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
            if (result.IsFailure)
                return;

            session = result.Value;
            if (cancellation.IsCancellationRequested)
            {
                await session.StopAsync().ConfigureAwait(false);
                return;
            }
            setSession(session);
            setLoading(false);
            setPlaying(true);
            await session.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            setLoading(false);
            if (ReferenceEquals(getSession(), session))
            {
                setSession(null);
                setPlaying(false);
            }
            if (ReferenceEquals(getCancellation(), cancellation))
                setCancellation(null);
        }
    }

    private void BeginLoading() => IsLoading = ++_loadingOperations > 0;
    private void EndLoading() => IsLoading = --_loadingOperations > 0;
    private static string FormatError(Exception exception) =>
        exception.Message.Contains("No active AI model", StringComparison.OrdinalIgnoreCase)
            ? Resources.TextAssistNoAiModel
            : Resources.SelectionTranslate_Failed + exception.Message;
}

internal static class TranslationMarkdownFormatter
{
    public static string PreserveLineBreaks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return normalized.Replace("\n", "  \n", StringComparison.Ordinal);
    }
}

internal static class MarkdownPlainTextFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string ToPlainText(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? string.Empty
            : NormalizeLineEndings(Markdown.ToPlainText(markdown, Pipeline)).Trim();

    public static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}

public sealed class DictionaryResultViewModel : ReactiveObject
{
    private string _word = string.Empty;
    private string _phonetic = string.Empty;
    private string _tips = string.Empty;
    public string Word { get => _word; set => this.RaiseAndSetIfChanged(ref _word, value); }
    public string Phonetic { get => _phonetic; set => this.RaiseAndSetIfChanged(ref _phonetic, value); }
    public string Tips { get => _tips; set => this.RaiseAndSetIfChanged(ref _tips, value); }
    public ObservableCollection<DictionaryFormViewModel> Forms { get; } = [];
    public ObservableCollection<DictionaryPartViewModel> Parts { get; } = [];
    public ObservableCollection<DictionaryExampleViewModel> Examples { get; } = [];
    public bool HasForms => Forms.Count > 0;
    public bool HasExamples => Examples.Count > 0;

    public DictionaryResultViewModel()
    {
        Forms.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasForms));
        Examples.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasExamples));
    }
}

public sealed class DictionaryFormViewModel : ReactiveObject
{
    private bool _isLoading;
    private bool _isPlaying;
    public string Label { get; init; } = string.Empty;
    public string Word { get; init; } = string.Empty;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            this.RaiseAndSetIfChanged(ref _isLoading, value);
            this.RaisePropertyChanged(nameof(ShowPlayIcon));
        }
    }
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPlaying, value);
            this.RaisePropertyChanged(nameof(ShowPlayIcon));
        }
    }
    public bool ShowPlayIcon => !IsLoading && !IsPlaying;
    internal ITtsPlaybackSession? Session { get; set; }
    internal CancellationTokenSource? Cancellation { get; set; }
}

public sealed class DictionaryPartViewModel : ReactiveObject
{
    public string PartOfSpeech { get; init; } = string.Empty;
    public ObservableCollection<string> Definitions { get; } = [];
}

public sealed class DictionaryExampleViewModel : ReactiveObject
{
    private bool _isOriginLoading;
    private bool _isTranslationLoading;
    private bool _isOriginPlaying;
    private bool _isTranslationPlaying;
    private bool _isBritishLoading;
    private bool _isBritishPlaying;
    private bool _isAmericanLoading;
    private bool _isAmericanPlaying;
    public string Origin { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
    public string BritishFlagIcon => "gb.png";
    public string AmericanFlagIcon => "us.png";
    public bool IsOriginLoading
    {
        get => _isOriginLoading;
        set
        {
            this.RaiseAndSetIfChanged(ref _isOriginLoading, value);
            this.RaisePropertyChanged(nameof(ShowOriginPlayIcon));
        }
    }
    public bool IsTranslationLoading
    {
        get => _isTranslationLoading;
        set
        {
            this.RaiseAndSetIfChanged(ref _isTranslationLoading, value);
            this.RaisePropertyChanged(nameof(ShowTranslationPlayIcon));
        }
    }
    public bool IsOriginPlaying
    {
        get => _isOriginPlaying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isOriginPlaying, value);
            this.RaisePropertyChanged(nameof(ShowOriginPlayIcon));
        }
    }
    public bool IsTranslationPlaying
    {
        get => _isTranslationPlaying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isTranslationPlaying, value);
            this.RaisePropertyChanged(nameof(ShowTranslationPlayIcon));
        }
    }
    public bool IsBritishLoading
    {
        get => _isBritishLoading;
        set
        {
            this.RaiseAndSetIfChanged(ref _isBritishLoading, value);
            this.RaisePropertyChanged(nameof(ShowBritishIcon));
        }
    }
    public bool IsBritishPlaying
    {
        get => _isBritishPlaying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isBritishPlaying, value);
            this.RaisePropertyChanged(nameof(ShowBritishIcon));
        }
    }
    public bool IsAmericanLoading
    {
        get => _isAmericanLoading;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAmericanLoading, value);
            this.RaisePropertyChanged(nameof(ShowAmericanIcon));
        }
    }
    public bool IsAmericanPlaying
    {
        get => _isAmericanPlaying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAmericanPlaying, value);
            this.RaisePropertyChanged(nameof(ShowAmericanIcon));
        }
    }
    public bool ShowOriginPlayIcon => !IsOriginLoading && !IsOriginPlaying;
    public bool ShowTranslationPlayIcon => !IsTranslationLoading && !IsTranslationPlaying;
    public bool ShowBritishIcon => !IsBritishLoading && !IsBritishPlaying;
    public bool ShowAmericanIcon => !IsAmericanLoading && !IsAmericanPlaying;
    internal ITtsPlaybackSession? OriginSession { get; set; }
    internal ITtsPlaybackSession? TranslationSession { get; set; }
    internal ITtsPlaybackSession? BritishSession { get; set; }
    internal ITtsPlaybackSession? AmericanSession { get; set; }
    internal CancellationTokenSource? OriginCancellation { get; set; }
    internal CancellationTokenSource? TranslationCancellation { get; set; }
    internal CancellationTokenSource? BritishCancellation { get; set; }
    internal CancellationTokenSource? AmericanCancellation { get; set; }
}
