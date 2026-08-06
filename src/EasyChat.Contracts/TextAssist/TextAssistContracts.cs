using System.Text.Json.Serialization;
using EasyChat.Contracts.Translation;

namespace EasyChat.Contracts.TextAssist;

public enum TextAssistOperation
{
    Translation,
    Correction,
    Polish,
    Summary,
    Explanation
}

public sealed record TextAssistProfile(
    TranslationLanguage Source,
    TranslationLanguage Target,
    string Provider,
    string? AiModelId,
    string? MachineProvider,
    bool UsesGlobalConfiguration = false,
    string? PromptId = null,
    bool DetailedExplanation = false);

public sealed record TextAssistRequest(
    string Text,
    TextAssistOperation Operation,
    TextAssistProfile? Profile = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(TextAssistStartedEvent), "start")]
[JsonDerivedType(typeof(TextAssistSourceDetectedEvent), "source_detected")]
[JsonDerivedType(typeof(TextAssistTranslationDeltaEvent), "translation_delta")]
[JsonDerivedType(typeof(TextAssistTranslationAnnotationEvent), "annotation")]
[JsonDerivedType(typeof(TextAssistPolishExplanationEvent), "polish_explanation")]
[JsonDerivedType(typeof(TextAssistIssueEvent), "issue")]
[JsonDerivedType(typeof(TextAssistCorrectedDeltaEvent), "corrected_delta")]
[JsonDerivedType(typeof(TextAssistCorrectionTranslationDeltaEvent), "correction_translation_delta")]
[JsonDerivedType(typeof(TextAssistCompletedEvent), "done")]
public abstract record TextAssistEvent;

public sealed record TextAssistStartedEvent : TextAssistEvent
{
    public TextAssistStartedEvent(string mode, string sourceLanguage, string? targetLanguage)
        : this(mode, sourceLanguage, targetLanguage, null)
    {
    }

    [JsonConstructor]
    public TextAssistStartedEvent(
        string mode,
        string? sourceLanguage,
        string? targetLanguage,
        string? language)
    {
        Mode = mode;
        SourceLanguage = sourceLanguage ?? language ?? string.Empty;
        TargetLanguage = targetLanguage;
        Language = language;
    }

    public string Mode { get; }

    [JsonPropertyName("sourceLanguage")]
    public string SourceLanguage { get; }

    public string? TargetLanguage { get; }

    [JsonPropertyName("language")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; }
}

public sealed record TextAssistSourceDetectedEvent(string Language) : TextAssistEvent;
public sealed record TextAssistTranslationDeltaEvent(string Text) : TextAssistEvent;

public sealed record TextAssistTranslationAnnotationEvent(
    string Term,
    string Category,
    string Meaning,
    string? Note = null,
    string[]? RelatedTerms = null) : TextAssistEvent
{
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public bool HasRelatedTerms => RelatedTerms is { Length: > 0 };
}

public sealed record TextAssistPolishExplanationEvent(
    string Category,
    string Original,
    string Revised,
    string Explanation) : TextAssistEvent
{
    public bool HasOriginal => !string.IsNullOrWhiteSpace(Original);
    public bool HasRevised => !string.IsNullOrWhiteSpace(Revised);
}

public sealed record TextAssistIssueEvent(
    int Start,
    int Length,
    string Category,
    string Message,
    string Suggestion) : TextAssistEvent;

public sealed record TextAssistCorrectedDeltaEvent(string Text, int Variant = 1) : TextAssistEvent;
public sealed record TextAssistCorrectionTranslationDeltaEvent(string Text, int Variant = 1) : TextAssistEvent;
public sealed record TextAssistCompletedEvent : TextAssistEvent;

public interface ITextAssistUseCases
{
    TextAssistProfile ResolveProfile(TextAssistOperation operation);

    IAsyncEnumerable<TextAssistEvent> StreamAsync(
        TextAssistRequest request,
        CancellationToken cancellationToken = default);
}
