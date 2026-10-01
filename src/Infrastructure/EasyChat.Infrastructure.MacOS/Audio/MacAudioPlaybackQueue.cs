using System.Threading.Channels;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Plays synthesised speech one clip at a time, optionally through a loopback device.
/// </summary>
/// <remarks>
/// <para>
/// Clips are written to a temporary file because <c>AVPlayer</c> plays from a URL, and
/// <c>AVPlayer</c> is used because it is the only straightforward macOS player that can be pointed
/// at a specific output device — which is the whole point of the interpretation route.
/// </para>
/// <para>
/// <c>Stop</c> has to be immediate from the user's point of view, so it cancels the clip that is
/// playing and empties the queue rather than letting the backlog drain.
/// </para>
/// </remarks>
internal sealed class MacAudioPlaybackQueue : IAudioPlaybackQueue, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<QueuedTrack> _queue =
        Channel.CreateUnbounded<QueuedTrack>(new UnboundedChannelOptions { SingleReader = true });

    private readonly IAudioPlaybackDeviceCatalog _devices;
    private readonly ILogger<MacAudioPlaybackQueue> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _pump;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _current;
    private int _disposed;

    public MacAudioPlaybackQueue(
        IAudioPlaybackDeviceCatalog devices,
        ILogger<MacAudioPlaybackQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(logger);
        _devices = devices;
        _logger = logger;
        _pump = Task.Run(PumpAsync);
    }

    public ValueTask EnqueueAsync(AudioTrack track, CancellationToken cancellationToken = default) =>
        EnqueueAsync(track, AudioPlaybackTarget.Default, cancellationToken);

    public ValueTask EnqueueAsync(
        AudioTrack track,
        AudioPlaybackTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();
        _queue.Writer.TryWrite(new QueuedTrack(track, target));
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_queue.Reader.TryRead(out _))
        {
        }

        lock (_gate)
        {
            try
            {
                _current?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The clip finished between reading the field and cancelling it.
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Disposal is idempotent, because a queue owned by the container can also be stopped and
    /// disposed by the workflow that was using it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _queue.Writer.TryComplete();
        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _lifetime.Dispose();
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var queued in _queue.Reader.ReadAllAsync(_lifetime.Token)
                               .ConfigureAwait(false))
            {
                using var clip = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                lock (_gate)
                    _current = clip;
                try
                {
                    await PlayAsync(queued, clip.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Stop was asked for; the next clip, if any, still gets its turn.
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Playing a speech clip failed.");
                }
                finally
                {
                    lock (_gate)
                        _current = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PlayAsync(QueuedTrack queued, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException("Playback requires macOS 26 or later.");

        var deviceUid = await ResolveDeviceAsync(queued.Target, cancellationToken)
            .ConfigureAwait(false);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"easychat-tts-{Guid.NewGuid():N}{WaveAudio.ExtensionFor(queued.Track.MediaType)}");
        await File.WriteAllBytesAsync(path, queued.Track.Content.ToArray(), cancellationToken)
            .ConfigureAwait(false);

        IntPtr player = IntPtr.Zero;
        try
        {
            using var pool = AutoreleasePool.Push();
            player = AVPlayerNative.Create(path, deviceUid);
            if (player == IntPtr.Zero)
                throw new InvalidOperationException("The audio player could not be created.");

            await WaitUntilReadyAsync(player, cancellationToken).ConfigureAwait(false);
            AVPlayerNative.Play(player);
            await WaitUntilFinishedAsync(player, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (player != IntPtr.Zero)
            {
                AVPlayerNative.Pause(player);
                AVPlayerNative.Release(player);
            }

            TryDelete(path);
        }
    }

    private static async Task WaitUntilReadyAsync(IntPtr player, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (AVPlayerNative.ItemStatus(player) != AVPlayerNative.ItemReady)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AVPlayerNative.HasFailed(player))
                throw new InvalidOperationException("The audio clip could not be decoded.");
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The audio clip did not become playable in time.");
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits for the clip to end. The rate has to be seen rising before a zero rate can be read as
    /// "finished", because it is also zero in the moment between asking to play and playback
    /// actually starting.
    /// </summary>
    private static async Task WaitUntilFinishedAsync(
        IntPtr player,
        CancellationToken cancellationToken)
    {
        var started = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rate = AVPlayerNative.Rate(player);
            if (rate > 0)
                started = true;
            else if (started)
                return;

            if (AVPlayerNative.HasFailed(player))
                throw new InvalidOperationException("Playback failed part way through the clip.");
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the output device, answering null for the system default. A request to use the
    /// loopback route when no loopback driver is installed falls back to the default device with a
    /// warning rather than failing: the user still hears the translation.
    /// </summary>
    private async Task<string?> ResolveDeviceAsync(
        AudioPlaybackTarget target,
        CancellationToken cancellationToken)
    {
        if (target != AudioPlaybackTarget.VirtualCable)
            return null;

        var devices = await _devices.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        var loopback = devices.FirstOrDefault(device => device.IsVirtualCable);
        if (loopback is not null)
            return loopback.Token.Value;

        _logger.LogWarning(
            "No loopback audio device is installed; playing through the default output instead.");
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct QueuedTrack(AudioTrack Track, AudioPlaybackTarget Target);
}
