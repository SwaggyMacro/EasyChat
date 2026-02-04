using System.Collections.Generic;

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
}

public class DictionaryExample
{
    public string Origin { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
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
