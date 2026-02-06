using System;
using EasyChat.Services.Languages;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Speech.Tts;
using EasyChat.Services.Text;
using EasyChat.Services.Translation.Selection;
using ReactiveUI;

namespace EasyChat.ViewModels.Windows;

public class TranslationDictionaryWindowViewModel : ViewModelBase
{
    private readonly ISelectionTranslationProvider _translationProvider;
    private readonly IConfigurationService _configurationService;
    private readonly ITtsService _ttsService;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ITokenizerFactory _tokenizerFactory;
    private TaskCompletionSource<bool>? _initializationTcs;

    private string _sourceText = string.Empty;
    public string SourceText
    {
        get => _sourceText;
        set => this.RaiseAndSetIfChanged(ref _sourceText, value);
    }

    private string _translationResult = string.Empty;
    public string TranslationResult
    {
        get => _translationResult;
        set => this.RaiseAndSetIfChanged(ref _translationResult, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private bool _isWordMode;
    public bool IsWordMode
    {
        get => _isWordMode;
        set => this.RaiseAndSetIfChanged(ref _isWordMode, value);
    }

    private DictionaryResult? _dictionaryResult;
    public DictionaryResult? DictionaryResult
    {
        get => _dictionaryResult;
        set => this.RaiseAndSetIfChanged(ref _dictionaryResult, value);
    }

    private ObservableCollection<TextToken> _sourceTokens = [];
    public ObservableCollection<TextToken> SourceTokens
    {
        get => _sourceTokens;
        set => this.RaiseAndSetIfChanged(ref _sourceTokens, value);
    }

    // Independent Loading States for Main UI Elements
    private bool _isWordTtsLoading;
    public bool IsWordTtsLoading
    {
        get => _isWordTtsLoading;
        set => this.RaiseAndSetIfChanged(ref _isWordTtsLoading, value);
    }

    private bool _isSourceTtsLoading;
    public bool IsSourceTtsLoading
    {
        get => _isSourceTtsLoading;
        set => this.RaiseAndSetIfChanged(ref _isSourceTtsLoading, value);
    }

    private bool _isResultTtsLoading;
    public bool IsResultTtsLoading
    {
        get => _isResultTtsLoading;
        set => this.RaiseAndSetIfChanged(ref _isResultTtsLoading, value);
    }

    public ReactiveCommand<string, Unit> LookupWordCommand { get; }
    public ReactiveCommand<Unit, Unit> SwitchToSentenceModeCommand { get; }
    public ReactiveCommand<object?, Unit> PlayTtsCommand { get; }
    public ReactiveCommand<object?, Unit> PlaySourceAudioCommand { get; }
    public ReactiveCommand<object?, Unit> PlayTargetAudioCommand { get; }

    private bool _canNavigateBack;

    private bool _showBackButton;
    public bool ShowBackButton
    {
        get => _showBackButton;
        set => this.RaiseAndSetIfChanged(ref _showBackButton, value);
    }

    private bool _showCloseButton;
    public bool ShowCloseButton
    {
        get => _showCloseButton;
        set => this.RaiseAndSetIfChanged(ref _showCloseButton, value);
    }

    private bool _isScreenshotMode;
    public bool IsScreenshotMode
    {
        get => _isScreenshotMode;
        set => this.RaiseAndSetIfChanged(ref _isScreenshotMode, value);
    }

    public TranslationDictionaryWindowViewModel(
        ISelectionTranslationProvider translationProvider,
        IConfigurationService configurationService,
        ITtsService ttsService,
        IAudioPlayer audioPlayer,
        ITokenizerFactory tokenizerFactory)
    {
        _translationProvider = translationProvider;
        _configurationService = configurationService;
        _ttsService = ttsService;
        _audioPlayer = audioPlayer;
        _tokenizerFactory = tokenizerFactory;

        LookupWordCommand = ReactiveCommand.CreateFromTask<string>(LookupWordAsync);
        SwitchToSentenceModeCommand = ReactiveCommand.Create(SwitchToSentenceMode);
        PlayTtsCommand = ReactiveCommand.CreateFromTask<object?>(PlayTtsAsync);
        PlaySourceAudioCommand = ReactiveCommand.CreateFromTask<object?>(PlaySourceAudioAsync);
        PlayTargetAudioCommand = ReactiveCommand.CreateFromTask<object?>(PlayTargetAudioAsync);

        // React to SourceText changes
        this.WhenAnyValue(x => x.SourceText)
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Select(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }
                
                // Use factory to get appropriate tokenizer
                var tokenizer = _tokenizerFactory.GetTokenizer(_currentSourceLang);
                var tokens = tokenizer.Tokenize(text);
                
                return new { Text = text, Tokens = tokens };
            })
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(result =>
            {
                if (result == null)
                {
                    SourceTokens = new ObservableCollection<TextToken>();
                    IsWordMode = false;
                    _initializationTcs?.TrySetResult(true);
                    return;
                }

                SourceTokens = new ObservableCollection<TextToken>(result.Tokens);
            });
            
        // Update ShowBackButton when mode changes
        this.WhenAnyValue(x => x.IsWordMode)
            .Subscribe(_ => UpdateShowBackButton());
    }
    
    /// <summary>
    /// Initializes the ViewModel with source text and waits for data processing to complete.
    /// This allows the caller to await until the UI is ready to be displayed.
    /// </summary>
    public async Task InitializeAsync(string text)
    {
        _initializationTcs = new TaskCompletionSource<bool>();
        
        SourceText = text;
        
        IsLoading = true;

        try 
        {
            await PerformTranslationAsync(text);
        }
        catch (Exception ex)
        {
            // Error handling (maybe show error state)
            TranslationResult = $"Translation Engine Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _initializationTcs.TrySetResult(true);
        }
    }

    private string _currentSourceLang = "en";
    private string _currentTargetLang = "zh-CN";

    private async Task PerformTranslationAsync(string text)
    {
        var sourceLang = _configurationService.General?.SourceLanguage.EnglishName ?? LanguageKeys.Auto.EnglishName;
        var targetLang = _configurationService.General?.TargetLanguage.EnglishName ?? throw new InvalidOperationException("Target language is not configured.");

        _currentSourceLang = _configurationService.General?.SourceLanguage.Id ?? LanguageKeys.AutoId;
        _currentTargetLang = _configurationService.General?.TargetLanguage.Id ?? throw new InvalidOperationException("Target language is not configured.");

        if (sourceLang == LanguageKeys.Auto.EnglishName) _currentSourceLang = "en"; // Default fallback for TTS if auto?

        var result = await _translationProvider.TranslateAsync(text, sourceLang, targetLang);

        // Update source language if auto-detected
        if (result.DetectedSourceLanguage is { } detected && !string.IsNullOrWhiteSpace(detected))
        {
            if (_currentSourceLang != detected)
            {
                _currentSourceLang = detected;
                // Trigger re-tokenization with new language
                // Since this is reactive, setting SourceText again might trigger the pipeline, 
                // but SourceText hasn't changed, so WhenAnyValue might not fire.
                // We force update tokens here.
                var tokenizer = _tokenizerFactory.GetTokenizer(_currentSourceLang);
                var tokens = tokenizer.Tokenize(SourceText);
                SourceTokens = new ObservableCollection<TextToken>(tokens);
            }
        }

        if (result is WordTranslationResult wordResult)
        {
            IsWordMode = true;
            _canNavigateBack = false; // Initial load is word -> no back
            
            // Map to DictionaryResult
            DictionaryResult = new DictionaryResult
            {
                Word = wordResult.Word,
                Phonetic = wordResult.Phonetic,
                Parts = wordResult.Definitions
                    .GroupBy(d => d.Pos)
                    .Select(g => new DictionaryPart
                    {
                        PartOfSpeech = g.Key,
                        Definitions = g.Select(d => d.Meaning).ToList()
                    }).ToList(),
                Tips = wordResult.Tips,
                Examples = wordResult.Examples.Select(e => new DictionaryExample
                {
                    Origin = e.Origin,
                    Translation = e.Translation
                }).ToList(),
                Forms = wordResult.Forms.Select(f => new DictionaryForm
                {
                    Label = f.Label,
                    Word = f.Word
                }).ToList()
            };
        }
        else if (result is SentenceTranslationResult sentenceResult)
        {
             IsWordMode = false;
             _canNavigateBack = true; // Can go back if we dive into words later (logic pending)
             
             TranslationResult = sentenceResult.Translation;
             // We could also process Keywords here if we had a UI for it.
        }
    }

    private void UpdateShowBackButton()
    {
        ShowBackButton = IsWordMode && _canNavigateBack;
    }

    private async Task LookupWordAsync(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;

        IsLoading = true;
        
        try
        {
            var sourceLang = _configurationService.General?.SourceLanguage.EnglishName ?? LanguageKeys.Auto.EnglishName;
            var targetLang = _configurationService.General?.TargetLanguage.EnglishName ?? throw new InvalidOperationException("Target language is not configured.");
            
            var result = await _translationProvider.TranslateAsync(word, sourceLang, targetLang);

            // Update source language if auto-detected
            if (result.DetectedSourceLanguage is { } detected && !string.IsNullOrWhiteSpace(detected))
            {
                if (_currentSourceLang != detected)
                {
                    _currentSourceLang = detected;
                    // Trigger re-tokenization with new language
                    var tokenizer = _tokenizerFactory.GetTokenizer(_currentSourceLang);
                    var tokens = tokenizer.Tokenize(SourceText);
                    SourceTokens = new ObservableCollection<TextToken>(tokens);
                }
            }
            
            if (result is WordTranslationResult wordResult)
            {
                IsWordMode = true;
                _canNavigateBack = true; 
                
                 DictionaryResult = new DictionaryResult
                {
                    Word = wordResult.Word,
                    Phonetic = wordResult.Phonetic,
                    Parts = wordResult.Definitions
                        .GroupBy(d => d.Pos)
                        .Select(g => new DictionaryPart
                        {
                            PartOfSpeech = g.Key,
                            Definitions = g.Select(d => d.Meaning).ToList()
                        }).ToList(),
                    Tips = wordResult.Tips,
                    Examples = wordResult.Examples.Select(e => new DictionaryExample
                    {
                         Origin = e.Origin,
                         Translation = e.Translation
                    }).ToList(),
                    Forms = wordResult.Forms.Select(f => new DictionaryForm
                    {
                        Label = f.Label,
                        Word = f.Word
                    }).ToList()
                };
            }
            else
            {
                 IsWordMode = false; 
                 TranslationResult = (result as SentenceTranslationResult)?.Translation ?? "";
            }
        }
        catch (Exception)
        {
             // Ignore
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private void SwitchToSentenceMode()
    {
        IsWordMode = false;
    }

    private async Task PlayTtsAsync(object? parameter)
    {
        string? textToSpeak;
        string langId;
        Action<bool>? setLoading = null;

        if (parameter is string text)
        {
            textToSpeak = text;
            langId = _currentSourceLang;
            setLoading = val => IsWordTtsLoading = val;
        }
        else if (IsWordMode && DictionaryResult != null)
        {
            textToSpeak = DictionaryResult.Word;
            langId = _currentSourceLang;
            setLoading = val => IsWordTtsLoading = val;
        }
        else
        {
            textToSpeak = TranslationResult;
            langId = _currentTargetLang;
            // No specific loading indicator for generic fallback yet, or reuse Result
        }

        await PlayTtsWithLanguageAsync(textToSpeak, langId, setLoading);
    }

    private async Task PlaySourceAudioAsync(object? parameter)
    {
        string? textToSpeak = null;
        Action<bool>? setLoading = null;

        if (parameter is DictionaryForm form)
        {
            textToSpeak = form.Word;
            setLoading = val => form.IsLoading = val;
        }
        else if (parameter is DictionaryExample example)
        {
            textToSpeak = example.Origin;
            setLoading = val => example.IsOriginLoading = val;
        }
        else if (parameter is string text)
        {
            textToSpeak = text;
            setLoading = val => IsSourceTtsLoading = val;
        }

        if (!string.IsNullOrWhiteSpace(textToSpeak))
        {
            await PlayTtsWithLanguageAsync(textToSpeak, _currentSourceLang, setLoading);
        }
    }

    private async Task PlayTargetAudioAsync(object? parameter)
    {
        string? textToSpeak = null;
        Action<bool>? setLoading = null;

        if (parameter is DictionaryExample example)
        {
            textToSpeak = example.Translation;
            setLoading = val => example.IsTranslationLoading = val;
        }
        else if (parameter is string text)
        {
            textToSpeak = text;
            setLoading = val => IsResultTtsLoading = val;
        }

        if (!string.IsNullOrWhiteSpace(textToSpeak))
        {
            await PlayTtsWithLanguageAsync(textToSpeak, _currentTargetLang, setLoading);
        }
    }

    private async Task PlayTtsWithLanguageAsync(string? text, string langId, Action<bool>? setLoadingState)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            _audioPlayer.Stop(); // Ensure previous is stopped
            setLoadingState?.Invoke(true);

            // Get Voice
            var voiceId = TtsHelper.GetPreferredVoiceId(_ttsService, _configurationService, langId);

            if (voiceId != null)
            {
                var stream = await _ttsService.StreamAsync(text, voiceId);
                if (stream != null)
                {
                    _audioPlayer.Enqueue(stream);
                }
            }
        }
        catch (Exception)
        {
            // Ignore for now
        }
        finally
        {
            setLoadingState?.Invoke(false);
        }
    }
}