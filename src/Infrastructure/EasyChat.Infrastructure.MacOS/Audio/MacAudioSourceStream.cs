using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// One opened audio source, already converted to the format speech recognition consumes.
/// </summary>
/// <remarks>
/// This is the seam between the capture orchestration — buffering, framing, mixing, shutdown — and
/// the Apple API that actually produces samples. The orchestration is where the concurrency
/// mistakes live and is worth testing on its own; the acquisition needs a microphone or a screen
/// recording approval and cannot be.
/// </remarks>
internal interface IMacAudioSourceStream : IAsyncDisposable
{
    /// <summary>
    /// Yields PCM as it arrives, and completes when the source ends. Throwing here means the source
    /// failed, which the caller turns into the end of the whole capture rather than silence.
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Opens the Apple audio source a decoded token names.</summary>
internal interface IMacAudioSourceFactory
{
    ValueTask<IMacAudioSourceStream> OpenAsync(
        MacAudioSource source,
        PcmAudioFormat format,
        CancellationToken cancellationToken);
}
