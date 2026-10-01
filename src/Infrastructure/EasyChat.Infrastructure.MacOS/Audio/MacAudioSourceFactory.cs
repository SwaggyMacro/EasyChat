using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Opens the Apple audio source a decoded token names.
/// </summary>
/// <remarks>
/// Each kind takes the simplest API that can serve it: a microphone through AudioQueue, which is
/// plain C, and system or application audio through ScreenCaptureKit, which is the only way macOS
/// offers to hear another process. A failure is reported rather than answered with an empty stream,
/// because silence from a capture that never started is indistinguishable from a quiet room.
/// </remarks>
internal sealed class MacAudioSourceFactory : IMacAudioSourceFactory
{
    public async ValueTask<IMacAudioSourceStream> OpenAsync(
        MacAudioSource source,
        PcmAudioFormat format,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return source.Kind switch
        {
            AudioCaptureSourceKind.Microphone =>
                MacMicrophoneAudioStream.Open(source.Identifier, format),

            // Process identifier zero means the whole display, which is how system-wide audio is
            // captured: a filter's audio is the audio of the content it covers.
            AudioCaptureSourceKind.SystemOutput => await MacScreenCaptureAudioStream
                .StartAsync(0, format, cancellationToken)
                .ConfigureAwait(false),

            AudioCaptureSourceKind.Application => await MacScreenCaptureAudioStream
                .StartAsync(RequireProcess(source), format, cancellationToken)
                .ConfigureAwait(false),

            _ => throw new NotSupportedException($"Unsupported audio source: {source.Kind}.")
        };
    }

    private static int RequireProcess(MacAudioSource source) =>
        source.ProcessIdentifier > 0
            ? source.ProcessIdentifier
            : throw new InvalidOperationException(
                "The application audio source does not name a running process.");
}
