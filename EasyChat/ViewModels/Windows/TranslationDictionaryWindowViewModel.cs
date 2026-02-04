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
    private TaskCompletionSource<bool>? _initializationTcs;

    public string SourceText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string TranslationResult
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool IsLoading
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsWordMode
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public DictionaryResult? DictionaryResult
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private ObservableCollection<TextToken> _sourceTokens = new();
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

    public TranslationDictionaryWindowViewModel(
        ISelectionTranslationProvider translationProvider,
        IConfigurationService configurationService,
        ITtsService ttsService,
        IAudioPlayer audioPlayer)
    {
        _translationProvider = translationProvider;
        _configurationService = configurationService;
        _ttsService = ttsService;
        _audioPlayer = audioPlayer;

        // Default tokenizer for now. In a real app, inject this.
        ITextTokenizer tokenizer = new EnglishTokenizer();

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
                
                // Tokenize for Sentence Mode
                var tokens = tokenizer.Tokenize(text);
                
                // Auto-detect mode assumption (initial heuristic before AI result)
                // We rely on AI to tell us the truth, but we prepare tokens for sentence view anyway.
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
                
                // We don't trigger AI here automatically because InitializeAsync does it.
                // But if SourceText changes due to binding (not InitializeAsync), we might want to trigger?
                // Usually SourceText is set via InitializeAsync in this specific window flow.
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
        var targetLang = _configurationService.General?.TargetLanguage.EnglishName ?? "Chinese";

        _currentSourceLang = _configurationService.General?.SourceLanguage.Id ?? "en";
        _currentTargetLang = _configurationService.General?.TargetLanguage.Id ?? "zh-CN";

        if (sourceLang == LanguageKeys.Auto.EnglishName) _currentSourceLang = "en"; // Default fallback for TTS if auto?

        var result = await _translationProvider.TranslateAsync(text, sourceLang, targetLang);

        // Update source language if auto-detected
        if (result.DetectedSourceLanguage is { } detected && !string.IsNullOrWhiteSpace(detected))
        {
            _currentSourceLang = detected;
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
            var sourceLang = "Auto"; 
            var targetLang = _configurationService.General?.TargetLanguage.EnglishName ?? "Chinese";
            
            var result = await _translationProvider.TranslateAsync(word, sourceLang, targetLang);

            // Update source language if auto-detected
            if (result.DetectedSourceLanguage is { } detected && !string.IsNullOrWhiteSpace(detected))
            {
                _currentSourceLang = detected;
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
        string? textToSpeak = null;
        string langId = _currentSourceLang;
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
            var provider = _configurationService.Tts?.Provider ?? TtsProviders.EdgeTTS;
            var voiceId = _configurationService.Tts?.GetVoiceForLanguage(provider, langId);
            
            if (string.IsNullOrEmpty(voiceId))
            {
                 // Fallback: pick first available voice for language or default
                 var voices = _ttsService.GetVoices();
                 var match = voices.FirstOrDefault(v => v.LanguageId.StartsWith(langId.Split("-").FirstOrDefault() ?? langId, StringComparison.OrdinalIgnoreCase));
                 
                 // If specific lang not found and we are defaulting to EN, try generic EN
                 if (match == null && langId.StartsWith(LanguageKeys.EnglishId))
                 {
                      match = voices.FirstOrDefault(v => v.Id.Contains(LanguageKeys.EnglishId));
                 }

                 voiceId = match?.Id;
            }

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