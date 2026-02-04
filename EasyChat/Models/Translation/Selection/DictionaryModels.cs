using System.Collections.Generic;
using ReactiveUI;

namespace EasyChat.Models.Translation.Selection;

public class DictionaryResult
{
    public string Word { get; set; } = string.Empty;
    public string Phonetic { get; set; } = string.Empty;
    public string? PronunciationUrl { get; set; }
    public string? Tips { get; set; }
    public List<DictionaryExample> Examples { get; set; } = new();
    public bool HasExamples => Examples.Count > 0;
    public List<DictionaryPart> Parts { get; set; } = new();
    public List<DictionaryForm> Forms { get; set; } = new();
    public bool HasForms => Forms.Count > 0;
}

public class DictionaryForm : ReactiveObject
{
    public string Label { get; set; } = string.Empty;
    public string Word { get; set; } = string.Empty;
    
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }
}

public class DictionaryExample : ReactiveObject
{
    public string Origin { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
    
    private bool _isOriginLoading;
    public bool IsOriginLoading
    {
        get => _isOriginLoading;
        set => this.RaiseAndSetIfChanged(ref _isOriginLoading, value);
    }
    
    private bool _isTranslationLoading;
    public bool IsTranslationLoading
    {
        get => _isTranslationLoading;
        set => this.RaiseAndSetIfChanged(ref _isTranslationLoading, value);
    }
}

public class DictionaryPart
{
    public string PartOfSpeech { get; set; } = string.Empty; // e.g., "n.", "v."
    public List<string> Definitions { get; set; } = new();
}

public class TextToken
{
    public string Text { get; set; } = string.Empty;
    public bool IsWord { get; set; }
    public int StartIndex { get; set; }
    public int Length { get; set; }
}
