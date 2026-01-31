using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Text;
using ReactiveUI;

namespace EasyChat.ViewModels.Windows;

public class SelectionTranslateWindowViewModel : ViewModelBase
{
    private readonly ITextTokenizer _tokenizer;
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

    public SelectionTranslateWindowViewModel()
    {
        // Default tokenizer for now. In a real app, inject this.
        _tokenizer = new EnglishTokenizer();

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
                var tokens = _tokenizer.Tokenize(text);
                
                // Auto-detect mode
                // Simple heuristic: if it's a single word (no spaces, short length), try Dictionary Mode
                var isSingleWord = !text.Trim().Contains(' ') && text.Length < 30;

                return new { Text = text, Tokens = tokens, IsSingleWord = isSingleWord };
            })
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(result =>
            {
                SourceTokens.Clear();

                if (result == null)
                {
                    IsWordMode = false;
                    // Signal initialization complete if waiting
                    _initializationTcs?.TrySetResult(true);
                    return;
                }

                foreach (var token in result.Tokens)
                {
                    SourceTokens.Add(token);
                }
                
                if (result.IsSingleWord)
                {
                    _canNavigateBack = false;
                    LookupWordCommand.Execute(result.Text.Trim()).Subscribe(_ =>
                    {
                        // Signal initialization complete after word lookup finishes
                        _initializationTcs?.TrySetResult(true);
                    });
                }
                else
                {
                    _canNavigateBack = true;
                    IsWordMode = false;
                    // TODO: Trigger sentence translation here
                    TranslationResult = "Translation logic pending..."; 
                    // Signal initialization complete
                    _initializationTcs?.TrySetResult(true);
                }
                UpdateShowBackButton();
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
        
        // Set source text which triggers the reactive pipeline
        SourceText = text;
        
        // Wait for processing to complete (with timeout)
        var timeoutTask = Task.Delay(5000);
        var completedTask = await Task.WhenAny(_initializationTcs.Task, timeoutTask);
        
        // If timeout, just continue anyway
        if (completedTask == timeoutTask)
        {
            _initializationTcs.TrySetResult(true);
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
        IsWordMode = true;

        try
        {
            // Simulate network delay
            await Task.Delay(300);

            // Mock Data Logic
            DictionaryResult = GenerateMockResult(word);
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

    private DictionaryResult GenerateMockResult(string word)
    {
        // specific mock for "Download" as per screenshot
        if (string.Equals(word, "Download", StringComparison.OrdinalIgnoreCase))
        {
            return new DictionaryResult
            {
                Word = "Download",
                Phonetic = "/ˌdaʊnˈləʊd/",
                Parts = new List<DictionaryPart>
                {
                    new DictionaryPart
                    {
                        PartOfSpeech = "n.",
                        Definitions = new List<string> { "【计】 下载; 【计】 下载的文件" }
                    },
                    new DictionaryPart
                    {
                        PartOfSpeech = "v.",
                        Definitions = new List<string> { "【计】 下载" }
                    },
                    new DictionaryPart
                    {
                        PartOfSpeech = "web.",
                        Definitions = new List<string> { "下载中心; 资料下载; 文档下载" }
                    }
                }
            };
        }

        // Generic mock for other words
        return new DictionaryResult
        {
            Word = word,
            Phonetic = $"/test-{word.ToLower()}/",
            Parts = new List<DictionaryPart>
            {
                new DictionaryPart
                {
                    PartOfSpeech = "n.",
                    Definitions = new List<string> { $"The noun definition of {word}" }
                },
                new DictionaryPart
                {
                    PartOfSpeech = "v.",
                    Definitions = new List<string> { $"The verb definition of {word}" }
                }
            }
        };
    }
}
