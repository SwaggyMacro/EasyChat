using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Threading;
using EasyChat.Contracts.SelectionTranslation;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Translation;
using EasyChat.Presentation.Features.Translation.Models;
using LiveMarkdown.Avalonia;
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
    private bool _isLoading;
    private bool _isWordMode;
    private bool _canNavigateBack;
    private bool _showBackButton;
    private bool _showCloseButton;
    private bool _isWordTtsLoading;
    private bool _isSourceTtsLoading;
    private bool _isResultTtsLoading;
    private int _loadingOperations;
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
        PlayTtsCommand = ReactiveCommand.CreateFromTask<object?>(PlayTtsAsync);
        PlaySourceAudioCommand = ReactiveCommand.CreateFromTask<object?>(PlaySourceAudioAsync);
        PlayTargetAudioCommand = ReactiveCommand.CreateFromTask<object?>(PlayTargetAudioAsync);
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
        }
    }
    public DictionaryResultViewModel? DictionaryResult
    {
        get => _dictionaryResult;
        private set
        {
            this.RaiseAndSetIfChanged(ref _dictionaryResult, value);
            this.RaisePropertyChanged(nameof(ShowDictionarySkeleton));
        }
    }
    public ObservableCollection<TextToken> SourceTokens { get => _sourceTokens; private set => this.RaiseAndSetIfChanged(ref _sourceTokens, value); }
    public bool ShowDictionarySkeleton => IsWordMode && IsLoading && (DictionaryResult is null || DictionaryResult.Parts.Count == 0);
    public bool ShowTranslationSkeleton => !IsWordMode && IsLoading && string.IsNullOrEmpty(TranslationResult);
    public bool ShowBackButton { get => _showBackButton; private set => this.RaiseAndSetIfChanged(ref _showBackButton, value); }
    public bool ShowCloseButton { get => _showCloseButton; set => this.RaiseAndSetIfChanged(ref _showCloseButton, value); }
    public bool IsWordTtsLoading { get => _isWordTtsLoading; private set => this.RaiseAndSetIfChanged(ref _isWordTtsLoading, value); }
    public bool IsSourceTtsLoading { get => _isSourceTtsLoading; private set => this.RaiseAndSetIfChanged(ref _isSourceTtsLoading, value); }
    public bool IsResultTtsLoading { get => _isResultTtsLoading; private set => this.RaiseAndSetIfChanged(ref _isResultTtsLoading, value); }
    public ReactiveCommand<string, Unit> LookupWordCommand { get; }
    public ReactiveCommand<Unit, Unit> SwitchToSentenceModeCommand { get; }
    public ReactiveCommand<object?, Unit> PlayTtsCommand { get; }
    public ReactiveCommand<object?, Unit> PlaySourceAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayTargetAudioCommand { get; }

    public Task InitializeAsync(string text) => InitializeAsync(
        text,
        _settings.General.SourceLanguage.Id,
        _settings.General.TargetLanguage.Id);

    public async Task InitializeAsync(string text, string sourceLanguageId, string targetLanguageId)
    {
        _sourceLanguageId = string.IsNullOrWhiteSpace(sourceLanguageId) ? "auto" : sourceLanguageId;
        _targetLanguageId = string.IsNullOrWhiteSpace(targetLanguageId) ? "zh-Hans" : targetLanguageId;
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

    public async Task InitializeDictionaryAsync(string text, string sourceLanguageId, string targetLanguageId)
    {
        _sourceLanguageId = string.IsNullOrWhiteSpace(sourceLanguageId) ? "auto" : sourceLanguageId;
        _targetLanguageId = string.IsNullOrWhiteSpace(targetLanguageId) ? "zh-Hans" : targetLanguageId;
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
            ? _translation.StreamDictionaryAsync(request)
            : _translation.StreamAsync(request);
        // Background priority: coalesce layout passes so stream deltas don't hitch the float.
        await foreach (var item in stream)
            await Dispatcher.UIThread.InvokeAsync(
                () => Apply(item, canNavigateBack, dictionary),
                DispatcherPriority.Background);
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
                    SourceTokens = new ObservableCollection<TextToken>(
                        TranslationTextTokenizer.Tokenize(SourceText, detected.Language));
                }
                break;
            case SelectionTranslationDeltaEvent delta:
                TranslationResult += delta.Text;
                TranslationMarkdown.Append(delta.Text);
                if (!lookup)
                    _sentenceTranslationSnapshot = TranslationResult;
                break;
            case SelectionTranslationWordHeaderEvent header:
                EnsureDictionary().Word = header.Word;
                EnsureDictionary().Phonetic = header.Phonetic ?? string.Empty;
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
        TranslationMarkdown.Append(TranslationResult);
    }

    private async Task SetErrorAsync(Exception exception)
    {
        var message = FormatError(exception);
        TranslationResult = message;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TranslationMarkdown.Clear();
            TranslationMarkdown.Append(message);
        });
    }

    private Task PlayTtsAsync(object? parameter)
    {
        var text = parameter as string ?? (IsWordMode ? DictionaryResult?.Word : TranslationResult);
        return PlayAsync(text, IsWordMode ? _sourceLanguageId : _targetLanguageId, value => IsWordTtsLoading = value);
    }

    private Task PlaySourceAudioAsync(object? parameter) => parameter switch
    {
        DictionaryFormViewModel form => PlayAsync(form.Word, _sourceLanguageId, value => form.IsLoading = value),
        DictionaryExampleViewModel example => PlayAsync(example.Origin, _sourceLanguageId, value => example.IsOriginLoading = value),
        string text => PlayAsync(text, _sourceLanguageId, value => IsSourceTtsLoading = value),
        _ => Task.CompletedTask
    };

    private Task PlayTargetAudioAsync(object? parameter) => parameter switch
    {
        DictionaryExampleViewModel example => PlayAsync(example.Translation, _targetLanguageId, value => example.IsTranslationLoading = value),
        string text => PlayAsync(text, _targetLanguageId, value => IsResultTtsLoading = value),
        _ => Task.CompletedTask
    };

    private async Task PlayAsync(string? text, string languageId, Action<bool> setLoading)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        setLoading(true);
        try
        {
            var voice = await _tts.ResolvePreferredVoiceAsync(languageId);
            if (voice.IsSuccess && !string.IsNullOrWhiteSpace(voice.Value))
                await _tts.EnqueueAsync(new TtsSynthesisRequest(text, voice.Value), interruptCurrent: true);
        }
        finally
        {
            setLoading(false);
        }
    }

    private void BeginLoading() => IsLoading = ++_loadingOperations > 0;
    private void EndLoading() => IsLoading = --_loadingOperations > 0;
    private static string FormatError(Exception exception) =>
        exception.Message.Contains("No active AI model", StringComparison.OrdinalIgnoreCase)
            ? Resources.TextAssistNoAiModel
            : Resources.SelectionTranslate_Failed + exception.Message;
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
    public string Label { get; init; } = string.Empty;
    public string Word { get; init; } = string.Empty;
    public bool IsLoading { get => _isLoading; set => this.RaiseAndSetIfChanged(ref _isLoading, value); }
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
    public string Origin { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
    public bool IsOriginLoading { get => _isOriginLoading; set => this.RaiseAndSetIfChanged(ref _isOriginLoading, value); }
    public bool IsTranslationLoading { get => _isTranslationLoading; set => this.RaiseAndSetIfChanged(ref _isTranslationLoading, value); }
}
