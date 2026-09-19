using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Speech;

public static class TtsProviderIds
{
    public const string EdgeTts = "EdgeTTS";
}

public sealed record TtsProviderDescriptor(string Id);

public sealed record TtsVoice(
    string Id,
    string Name,
    string LanguageId,
    string Gender,
    IReadOnlyList<string> ContentCategories,
    IReadOnlyList<string> VoicePersonalities);

public sealed record TtsLanguage(
    string Locale,
    string Language,
    string Region,
    string EnglishName,
    string ChineseName,
    string Icon);

public sealed record TtsSynthesisRequest(
    string Text,
    string VoiceId,
    string? ProviderId = null,
    string? Rate = null,
    string? Volume = null,
    string? Pitch = null);

public interface ITtsSynthesisProvider
{
    string ProviderId { get; }

    ValueTask<Result<IReadOnlyList<TtsVoice>>> GetVoicesAsync(
        CancellationToken cancellationToken = default);

    ValueTask<Result<IReadOnlyList<TtsLanguage>>> GetLanguagesAsync(
        CancellationToken cancellationToken = default);

    ValueTask<Result<AudioTrack>> SynthesizeAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITtsOutputWriter
{
    ValueTask<Result> WriteAsync(
        string path,
        AudioTrack track,
        CancellationToken cancellationToken = default);
}

public interface ITtsUseCases
{
    IReadOnlyList<TtsProviderDescriptor> GetProviders();

    ValueTask<Result<IReadOnlyList<TtsVoice>>> GetVoicesAsync(
        string? providerId = null,
        CancellationToken cancellationToken = default);

    ValueTask<Result<IReadOnlyList<TtsLanguage>>> GetLanguagesAsync(
        string? providerId = null,
        CancellationToken cancellationToken = default);

    ValueTask<Result<string?>> ResolvePreferredVoiceAsync(
        string languageId,
        string? providerId = null,
        CancellationToken cancellationToken = default);

    ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent = false,
        CancellationToken cancellationToken = default);

    ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        CancellationToken cancellationToken = default);

    ValueTask<Result<AudioTrack>> SynthesizeAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<Result> SynthesizeToFileAsync(
        TtsSynthesisRequest request,
        string outputPath,
        CancellationToken cancellationToken = default);

    ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent = false,
        CancellationToken cancellationToken = default);

    ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(request, interruptCurrent, target, cancellationToken);

    ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        string? outputDeviceName,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(request, interruptCurrent, target, context, cancellationToken);
}

public interface ITtsPlaybackSession
{
    Guid PlaybackId { get; }

    Task Completion { get; }

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
