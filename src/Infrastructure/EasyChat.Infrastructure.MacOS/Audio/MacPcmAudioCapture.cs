using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EasyChat.Contracts.Platform;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Feeds speech recognition a single stream of fixed-size frames, mixed from every source the user
/// selected.
/// </summary>
/// <remarks>
/// <para>
/// Each source is buffered separately and read by its own pump, because they arrive at different
/// times and in different sized chunks: a microphone delivers on its device's clock, a screen
/// capture on the compositor's. Mixing only happens once every source has contributed a whole
/// frame's worth, or has fallen far enough behind to be treated as silent.
/// </para>
/// <para>
/// Buffers are bounded and drop the oldest audio when they overflow. If recognition cannot keep up,
/// the useful thing is the most recent speech; letting the buffer grow would trade a fixed delay
/// for an unbounded one and eventually for the process's memory.
/// </para>
/// </remarks>
internal sealed class MacPcmAudioCapture(
    IMacAudioSourceFactory sources,
    ILogger<MacPcmAudioCapture> logger) : IPcmAudioCapture, IPreparablePcmAudioCapture
{
    /// <summary>
    /// One frame of audio. Twenty milliseconds is what the recogniser's segmenter expects, and it
    /// is short enough that a source arriving late costs little.
    /// </summary>
    private const int FrameMilliseconds = 20;

    /// <summary>
    /// Two seconds of frames per source. Long enough to ride out a scheduling hiccup, short enough
    /// that the audio which survives a backlog is still recent.
    /// </summary>
    private const int MaximumBufferedFrames = 1000 / FrameMilliseconds * 2;

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
        IReadOnlyList<AudioCaptureSourceToken> sources_,
        PcmAudioFormat format,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources_);
        cancellationToken.ThrowIfCancellationRequested();

        var decoded = Decode(sources_);
        if (decoded.Count == 0)
            yield break;

        var frameSize = PcmAudioConversion.FrameSizeInBytes(format, FrameMilliseconds);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pumps = new List<SourcePump>(decoded.Count);
        try
        {
            foreach (var source in decoded)
            {
                var stream = await sources.OpenAsync(source, format, lifetime.Token)
                    .ConfigureAwait(false);
                pumps.Add(new SourcePump(stream, frameSize, MaximumBufferedFrames, logger));
            }

            foreach (var pump in pumps)
                pump.Start(lifetime.Token);

            await foreach (var frame in MixAsync(pumps, frameSize, lifetime.Token)
                               .ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            foreach (var pump in pumps)
                await pump.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens the sources ahead of time so the first frame after the user presses the shortcut is
    /// real audio rather than the device still starting up. Anything read before capture begins is
    /// discarded.
    /// </summary>
    public async ValueTask PrepareCaptureAsync(
        IReadOnlyList<AudioCaptureSourceToken> sources_,
        PcmAudioFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources_);
        foreach (var source in Decode(sources_))
        {
            try
            {
                var stream = await sources.OpenAsync(source, format, cancellationToken)
                    .ConfigureAwait(false);
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Warming up is an optimisation; failing it must not stop the user from starting.
                logger.LogWarning(
                    exception,
                    "Preparing the {Kind} audio source failed.",
                    source.Kind);
            }
        }
    }

    public ValueTask ReleasePreparedCaptureAsync(
        IReadOnlyList<AudioCaptureSourceToken> sources_,
        PcmAudioFormat format,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Emits one mixed frame at a time, and stops as soon as every source has ended. A source that
    /// has nothing ready contributes silence rather than holding the others up.
    /// </summary>
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> MixAsync(
        IReadOnlyList<SourcePump> pumps,
        int frameSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var ready = await WaitForFrameAsync(pumps, cancellationToken).ConfigureAwait(false);
            if (!ready)
                yield break;

            var contributions = new List<byte[]>(pumps.Count);
            foreach (var pump in pumps)
            {
                if (pump.TryReadFrame(out var frame))
                    contributions.Add(frame);
            }

            if (contributions.Count == 0)
                continue;

            yield return contributions.Count == 1
                ? contributions[0]
                : PcmAudioConversion.Mix(contributions);
        }
    }

    /// <summary>
    /// Answers true once at least one source has a frame, and false when every source has finished.
    /// </summary>
    private static async ValueTask<bool> WaitForFrameAsync(
        IReadOnlyList<SourcePump> pumps,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var live = false;
            foreach (var pump in pumps)
            {
                if (pump.HasFrame)
                    return true;
                if (!pump.HasFinished)
                    live = true;
            }

            if (!live)
                return false;

            try
            {
                await Task.Delay(FrameMilliseconds / 2, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    private IReadOnlyList<MacAudioSource> Decode(IReadOnlyList<AudioCaptureSourceToken> tokens)
    {
        var decoded = new List<MacAudioSource>(tokens.Count);
        foreach (var token in tokens)
        {
            if (MacAudioSourceTokens.TryDecode(token, out var source))
                decoded.Add(source);
            else
                logger.LogWarning("Ignoring an audio source token this platform did not issue.");
        }

        return decoded;
    }

    /// <summary>
    /// Reads one source into a bounded buffer of whole frames.
    /// </summary>
    private sealed class SourcePump(
        IMacAudioSourceStream stream,
        int frameSize,
        int maximumFrames,
        ILogger logger) : IAsyncDisposable
    {
        private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(maximumFrames)
            {
                // The newest audio is the useful audio when recognition falls behind.
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

        private readonly List<byte> _partial = new(frameSize);
        private Task? _pump;

        internal bool HasFrame => _frames.Reader.Count > 0;

        internal bool HasFinished { get; private set; }

        internal void Start(CancellationToken cancellationToken) =>
            _pump = Task.Run(() => PumpAsync(cancellationToken), CancellationToken.None);

        internal bool TryReadFrame(out byte[] frame) => _frames.Reader.TryRead(out frame!);

        public async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            if (_pump is null)
                return;

            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task PumpAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var chunk in stream.ReadAsync(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    Accumulate(chunk.Span);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                // A failed source ends this stream rather than going quiet, so the workflow can
                // report it instead of appearing to listen to nothing.
                logger.LogError(exception, "An audio source failed while capturing.");
            }
            finally
            {
                HasFinished = true;
                _frames.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Collects incoming audio into whole frames. A source hands over whatever size its device
        /// produces, which rarely divides evenly into a frame, so the remainder is carried forward
        /// instead of being padded or dropped — padding would insert clicks and dropping would drift.
        /// </summary>
        private void Accumulate(ReadOnlySpan<byte> chunk)
        {
            _partial.AddRange(chunk);
            while (_partial.Count >= frameSize)
            {
                var frame = _partial.GetRange(0, frameSize).ToArray();
                _partial.RemoveRange(0, frameSize);
                _frames.Writer.TryWrite(frame);
            }
        }
    }
}
