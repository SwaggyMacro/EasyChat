using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EasyChat.Models.Translation.Selection;

public enum TranslationSourceType
{
    Ai,
    Machine
}

/// <summary>
/// Base class for translation results.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WordTranslationResult), typeDiscriminator: "word")]
[JsonDerivedType(typeof(SentenceTranslationResult), typeDiscriminator: "sentence")]
public abstract class SelectionTranslationResult
{
    [JsonPropertyName("source_type")]
    public TranslationSourceType SourceType { get; set; } = TranslationSourceType.Ai;

    [JsonPropertyName("detected_source_language")]
    public string? DetectedSourceLanguage { get; set; }
}

public class WordTranslationResult : SelectionTranslationResult
{
    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;

    [JsonPropertyName("phonetic")]
    public string Phonetic { get; set; } = string.Empty;

    [JsonPropertyName("definitions")]
    public List<WordDefinition> Definitions { get; set; } = new();

    [JsonPropertyName("tips")]
    public string Tips { get; set; } = string.Empty;

    [JsonPropertyName("examples")]
    public List<WordExample> Examples { get; set; } = new();

    [JsonPropertyName("forms")]
    public List<WordForm> Forms { get; set; } = new();
}

public class WordForm
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;
}

public class WordDefinition
{
    [JsonPropertyName("pos")]
    public string Pos { get; set; } = string.Empty;

    [JsonPropertyName("meaning")]
    public string Meaning { get; set; } = string.Empty;
}

public class WordExample
{
    [JsonPropertyName("origin")]
    public string Origin { get; set; } = string.Empty;

    [JsonPropertyName("translation")]
    public string Translation { get; set; } = string.Empty;
}

public class SentenceTranslationResult : SelectionTranslationResult
{
    [JsonPropertyName("origin")]
    public string Origin { get; set; } = string.Empty;

    [JsonPropertyName("translation")]
    public string Translation { get; set; } = string.Empty;

    [JsonPropertyName("key_words")]
    public List<SentenceKeyWord> KeyWords { get; set; } = new();
}

public class SentenceKeyWord
{
    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;

    [JsonPropertyName("meaning")]
    public string Meaning { get; set; } = string.Empty;
}
