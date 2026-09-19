namespace EasyChat.Contracts.Platform;

public sealed record SpeechRecognitionOptions(
    string ModelPath,
    string Language,
    IReadOnlyList<AudioCaptureSourceToken> Sources,
    bool AllowConcurrentSessions = false);

public enum SpeechRecognitionEventKind
{
    Started,
    Partial,
    Final,
    Error,
    Stopped
}

public sealed record SpeechRecognitionEvent(SpeechRecognitionEventKind Kind, string? Text = null);

public interface ISpeechRecognitionEngine
{
    ValueTask PrepareAsync(
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    ValueTask ReleasePreparationAsync(
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    IAsyncEnumerable<SpeechRecognitionEvent> RecognizeAsync(
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken = default);
}
