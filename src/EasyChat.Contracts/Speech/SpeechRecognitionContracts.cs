using System.Globalization;
using System.Text.Json.Serialization;
using EasyChat.Contracts.Platform;

namespace EasyChat.Contracts.Speech;

public sealed record SpeechRecognitionCommand(
    string ModelPath,
    string Language,
    IReadOnlyList<AudioCaptureSourceReference> Sources);

public sealed record SpeechRecognitionModel
{
    public SpeechRecognitionModel(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        (ChineseName, EnglishName, Icon) = id.ToLowerInvariant() switch
        {
            "da-dk" => ("丹麦语", "Danish", "dk.png"),
            "de-de" => ("德语", "German", "de.png"),
            "en-us" => ("英语（美国）", "English (United States)", "us.png"),
            "es-es" => ("西班牙语（西班牙）", "Spanish (Spain)", "es.png"),
            "fr-fr" => ("法语（法国）", "French (France)", "fr.png"),
            "it-it" => ("意大利语", "Italian", "it.png"),
            "ja-jp" => ("日语", "Japanese", "jp.png"),
            "ko-kr" => ("韩语", "Korean", "kr.png"),
            "pt-br" => ("葡萄牙语（巴西）", "Portuguese (Brazil)", "br.png"),
            "zh-cn" => ("中文（简体）", "Chinese (Simplified)", "cn.png"),
            _ => (id, id, "unknown.png")
        };
    }

    public string Id { get; }
    public string ChineseName { get; }
    public string EnglishName { get; }
    public string Icon { get; }

    public string DisplayName =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
        && !string.IsNullOrWhiteSpace(ChineseName)
            ? ChineseName
            : EnglishName;
}

public interface ISpeechRecognitionModelCatalog
{
    event EventHandler? ModelsChanged;

    ValueTask<IReadOnlyList<SpeechRecognitionModel>> GetModelsAsync(
        CancellationToken cancellationToken = default);
}

public enum SpeechRecognitionModelImportSourceKind
{
    Directory,
    Archive
}

public sealed record SpeechRecognitionModelImportRequest(
    IReadOnlyList<string> SourcePaths,
    SpeechRecognitionModelImportSourceKind SourceKind)
{
    public SpeechRecognitionModelImportRequest(
        string sourcePath,
        SpeechRecognitionModelImportSourceKind sourceKind)
        : this([sourcePath], sourceKind)
    {
    }
}

public sealed record SpeechRecognitionModelImportResult(
    IReadOnlyList<SpeechRecognitionModel> ImportedModels,
    IReadOnlyList<SpeechRecognitionModel> SkippedModels);

public interface ISpeechRecognitionModelInstaller
{
    ValueTask<SpeechRecognitionModelImportResult> ImportAsync(
        SpeechRecognitionModelImportRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISpeechRecognitionModelRemover
{
    ValueTask<bool> DeleteAsync(
        string modelId,
        CancellationToken cancellationToken = default);
}

public sealed record SpeechSubtitleLine(
    long Id,
    TimeSpan Timestamp,
    string OriginalText,
    string TranslatedText,
    string DisplayTranslatedText,
    bool IsTranslating,
    bool IsTemporary);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(SpeechSessionStartedEvent), "started")]
[JsonDerivedType(typeof(SpeechSubtitleChangedEvent), "subtitle_changed")]
[JsonDerivedType(typeof(SpeechFloatingSubtitleRemovedEvent), "floating_subtitle_removed")]
[JsonDerivedType(typeof(SpeechSessionErrorEvent), "error")]
[JsonDerivedType(typeof(SpeechSessionStoppedEvent), "stopped")]
public abstract record SpeechSessionEvent;

public sealed record SpeechSessionStartedEvent : SpeechSessionEvent;
public sealed record SpeechSubtitleChangedEvent(SpeechSubtitleLine Subtitle) : SpeechSessionEvent;
public sealed record SpeechFloatingSubtitleRemovedEvent(long SubtitleId) : SpeechSessionEvent;
public sealed record SpeechSessionErrorEvent(string Message) : SpeechSessionEvent;
public sealed record SpeechSessionStoppedEvent : SpeechSessionEvent;

public interface ISpeechRecognitionUseCases
{
    IAsyncEnumerable<SpeechSessionEvent> RecognizeAsync(
        SpeechRecognitionCommand command,
        CancellationToken cancellationToken = default);
}
