using EasyChat.Shared.Results;
using System.Text.Json.Serialization;

namespace EasyChat.Contracts.Translation;

public static class TranslationEngineNames
{
    public const string AiModel = "AiModel";
    public const string MachineTrans = "MachineTrans";
}

public sealed record TranslationLanguage(
    string Id,
    string EnglishName,
    string? NativeName = null,
    IReadOnlyDictionary<string, string>? ProviderCodes = null,
    string? Icon = null);

public interface ITranslationLanguageCatalog
{
    IReadOnlyList<TranslationLanguage> All { get; }
    TranslationLanguage Get(string id);
}

/// <summary>
/// Optional provider selection carried by a translation request. When null,
/// the application resolves the provider from persisted global configuration.
/// </summary>
public sealed record TranslationProviderSelection(
    string Engine,
    string? AiModelId = null,
    string? AiModelName = null,
    string? MachineProviderId = null,
    string? MachineProviderName = null,
    string? PromptOverride = null,
    string? PromptId = null);

public sealed record TranslationRequest(
    string Text,
    TranslationLanguage? Source,
    TranslationLanguage Target,
    bool ShowOriginal = false,
    TranslationProviderSelection? Provider = null,
    bool PlainText = false);

public sealed record TranslationResponse(string Text);

public sealed record IdentifiedTranslationDelta(string Id, string Text);

public sealed record TranslationProviderRequest(
    string Text,
    string SourceLanguageCode,
    string TargetLanguageCode,
    bool ShowOriginal = false);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(TranslationStartedEvent), "start")]
[JsonDerivedType(typeof(TranslationSourceDetectedEvent), "source_detected")]
[JsonDerivedType(typeof(TranslationDeltaEvent), "translation_delta")]
[JsonDerivedType(typeof(TranslationCompletedEvent), "done")]
public abstract record TranslationEvent;

public sealed record TranslationStartedEvent(
    string Mode,
    [property: JsonPropertyName("source_language")]
    string SourceLanguage,
    [property: JsonPropertyName("target_language")]
    string TargetLanguage) : TranslationEvent;

public sealed record TranslationSourceDetectedEvent(string Language) : TranslationEvent;

public sealed record TranslationDeltaEvent(string Text) : TranslationEvent;

public sealed record TranslationCompletedEvent : TranslationEvent;

public sealed record TranslationFailedEvent(Error Error) : TranslationEvent;

/// <summary>
/// A synchronously prepared provider session. Preparing the session is allowed
/// to throw so callers can retain their existing window and error timing.
/// </summary>
public interface ITranslationSession
{
    bool SupportsIdentifiedStreaming { get; }

    Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TranslationEvent> StreamAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITranslationProvider
{
    Task<string> TranslateAsync(
        TranslationProviderRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITranslationFailureSink
{
    void Report(Exception exception);
}

public interface ITranslationUseCases
{
    ITranslationSession Prepare(TranslationProviderSelection? provider = null);

    Task<Result<TranslationResponse>> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TranslationEvent> StreamAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}
