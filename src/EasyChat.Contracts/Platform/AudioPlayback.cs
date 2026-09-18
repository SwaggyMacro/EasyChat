namespace EasyChat.Contracts.Platform;

public sealed record AudioTrack(ReadOnlyMemory<byte> Content, string MediaType);

public enum AudioPlaybackTarget
{
    Default,
    VirtualCable
}

public enum AudioPlaybackContext
{
    None,
    RealtimeInterpretation
}

public sealed class AudioPlaybackEventArgs(
    Guid playbackId,
    AudioPlaybackTarget target,
    AudioPlaybackContext context) : EventArgs
{
    public Guid PlaybackId { get; } = playbackId;
    public AudioPlaybackTarget Target { get; } = target;
    public AudioPlaybackContext Context { get; } = context;
}

public interface IAudioPlaybackLifecycle
{
    event EventHandler<AudioPlaybackEventArgs>? PlaybackStarted;

    event EventHandler<AudioPlaybackEventArgs>? PlaybackCompleted;
}

/// <summary>
/// Platform-owned identifier for a playback endpoint. Consumers must treat it as opaque and
/// must not persist it.
/// </summary>
public readonly record struct AudioPlaybackDeviceToken(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
}

public sealed record AudioPlaybackDeviceDescriptor(
    AudioPlaybackDeviceToken Token,
    string Name,
    string DisplayName,
    string? Description,
    bool IsVirtualCable);

public interface IAudioPlaybackDeviceCatalog
{
    ValueTask<IReadOnlyList<AudioPlaybackDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default);
}

public interface IAudioPlaybackQueue
{
    ValueTask EnqueueAsync(AudioTrack track, CancellationToken cancellationToken = default);

    ValueTask EnqueueAsync(
        AudioTrack track,
        AudioPlaybackTarget target,
        CancellationToken cancellationToken = default);

    ValueTask EnqueueAsync(
        AudioTrack track,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(track, target, cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
