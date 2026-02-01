using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Text;
using EasyChat.Services.Translation;
using ReactiveUI;

namespace EasyChat.ViewModels.Windows;

public class SelectionTranslateWindowViewModel : ViewModelBase
{
    private readonly ISelectionTranslationProvider _translationProvider;
    private readonly IConfigurationService _configurationService;
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

    public ObservableCollection<TextToken> SourceTokens { get; } = new();

    public ReactiveCommand<string, Unit> LookupWordCommand { get; }
    public ReactiveCommand<Unit, Unit> SwitchToSentenceModeCommand { get; }

    private bool _canNavigateBack;
    private bool _showBackButton;
    public bool ShowBackButton
    {
        get => _showBackButton;
        set => this.RaiseAndSetIfChanged(ref _showBackButton, value);
    }

    public SelectionTranslateWindowViewModel(
        ISelectionTranslationProvider translationProvider,
        IConfigurationService configurationService)
    {
        _translationProvider = translationProvider;
        _configurationService = configurationService;

        // Default tokenizer for now. In a real app, inject this.
        ITextTokenizer tokenizer = new EnglishTokenizer();

        LookupWordCommand = ReactiveCommand.CreateFromTask<string>(LookupWordAsync);
        SwitchToSentenceModeCommand = ReactiveCommand.Create(SwitchToSentenceMode);

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
                SourceTokens.Clear();

                if (result == null)
                {
                    IsWordMode = false;
                    _initializationTcs?.TrySetResult(true);
                    return;
                }

                foreach (var token in result.Tokens)
                {
                    SourceTokens.Add(token);
                }
                
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

    private async Task PerformTranslationAsync(string text)
    {
        var sourceLang = _configurationService.General?.SourceLanguage.EnglishName ?? "Auto";
        var targetLang = _configurationService.General?.TargetLanguage.EnglishName ?? "Chinese";

        var result = await _translationProvider.TranslateAsync(text, sourceLang, targetLang);

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
        // Don't switch mode immediately, wait for result? 
        // Or switch to show loading in word mode?
        // Current UI has shared loading.
        
        try
        {
            var sourceLang = "Auto"; // Looking up a word from sentence is usually Auto
            var targetLang = _configurationService.General?.TargetLanguage.EnglishName ?? "Chinese";
            
            // Force "Word Mode" expectation? The provider detects.
            // If I click a single word, AI should detect Word Mode.
            
            var result = await _translationProvider.TranslateAsync(word, sourceLang, targetLang);
            
            if (result is WordTranslationResult wordResult)
            {
                IsWordMode = true;
                _canNavigateBack = true; // Came from sentence view
                
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
                    }).ToList()
                };
            }
            else
            {
                // Fallback if AI thinks it's a sentence?
                // Treat as simple translation?
                 IsWordMode = false; 
                 TranslationResult = (result as SentenceTranslationResult)?.Translation ?? "";
            }
        }
        catch (Exception)
        {
             // Fallback for lookup failure
             // Could show a snackbar or message, for now just log/ignore or reset loading
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
}
