using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Plays the short cues that tell the user interpretation has started, been released, or finished.
/// </summary>
/// <remarks>
/// The Windows adapter produces these with <c>Console.Beep</c>, which does not exist off Windows.
/// Rather than substituting a system alert sound, the same three pitches and lengths are synthesised
/// so the cues stay audibly identical across platforms. They always go to the default output: a cue
/// routed into a loopback device would be picked up by whoever is listening on the other end, which
/// is exactly what the contract forbids.
/// </remarks>
internal sealed class MacAudioFeedbackCuePlayer(IAudioPlaybackQueue playback) : IAudioFeedbackCuePlayer
{
    public ValueTask PlayAsync(
        AudioFeedbackCue cue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playback);
        cancellationToken.ThrowIfCancellationRequested();

        var (frequency, duration) = cue switch
        {
            AudioFeedbackCue.RealtimeInterpretationStarted => (880, 75),
            AudioFeedbackCue.RealtimeInterpretationReleased => (740, 75),
            AudioFeedbackCue.RealtimeInterpretationCompleted => (660, 100),
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, null)
        };

        return playback.EnqueueAsync(
            new AudioTrack(WaveAudio.CreateTone(frequency, duration), "audio/wav"),
            AudioPlaybackTarget.Default,
            cancellationToken);
    }
}
