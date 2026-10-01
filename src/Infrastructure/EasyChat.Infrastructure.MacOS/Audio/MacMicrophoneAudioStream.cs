using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Records one input device through <c>AudioQueue</c>.
/// </summary>
/// <remarks>
/// The queue is asked for the recogniser's format directly, so CoreAudio performs the rate and
/// channel conversion itself. That is not only less code than converting here: it also keeps working
/// when a device changes rate mid-session, which AirPods do whenever they switch between listening
/// and recording profiles.
/// </remarks>
internal sealed class MacMicrophoneAudioStream : IMacAudioSourceStream
{
    /// <summary>Roughly a tenth of a second at the recogniser's format.</summary>
    private const uint BufferBytes = 3200;

    /// <summary>Three buffers in flight, so recording continues while one is being drained.</summary>
    private const int BufferCount = 3;

    private static readonly ConcurrentDictionary<IntPtr, MacMicrophoneAudioStream> Streams = new();
    private static long _nextKey;

    private readonly Channel<ReadOnlyMemory<byte>> _audio =
        Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    private readonly IntPtr _key;
    private IntPtr _queue;
    private int _disposed;

    private MacMicrophoneAudioStream(IntPtr key) => _key = key;

    /// <summary>
    /// Opens and starts the device. A failure at any step is reported rather than producing a stream
    /// that yields silence, because silence is indistinguishable from a quiet room.
    /// </summary>
    internal static MacMicrophoneAudioStream Open(string? deviceUid, PcmAudioFormat format)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException("Recording requires macOS 26 or later.");

        var key = (IntPtr)Interlocked.Increment(ref _nextKey);
        var stream = new MacMicrophoneAudioStream(key);
        Streams[key] = stream;

        var description = AudioQueueNative.DescribePcm(
            format.SampleRateHz,
            format.ChannelCount,
            format.BitsPerSample);
        var status = AudioQueueNative.AudioQueueNewInput(
            ref description,
            CallbackPointer,
            key,
            // A null run loop asks AudioQueue to service the callback on a thread of its own, which
            // is what this needs: there is no run loop on a thread pool thread to borrow.
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            out var queue);
        if (status != 0 || queue == IntPtr.Zero)
        {
            Streams.TryRemove(key, out _);
            throw new InvalidOperationException(
                $"The audio input queue could not be created (status {status}).");
        }

        stream._queue = queue;
        try
        {
            if (!string.IsNullOrWhiteSpace(deviceUid))
            {
                var deviceStatus = AudioQueueNative.SetInputDevice(queue, deviceUid);
                if (deviceStatus != 0)
                {
                    throw new InvalidOperationException(
                        $"The input device '{deviceUid}' could not be selected (status {deviceStatus}).");
                }
            }

            for (var index = 0; index < BufferCount; index++)
            {
                var allocated = AudioQueueNative.AudioQueueAllocateBuffer(
                    queue,
                    BufferBytes,
                    out var buffer);
                if (allocated != 0)
                {
                    throw new InvalidOperationException(
                        $"An audio buffer could not be allocated (status {allocated}).");
                }

                AudioQueueNative.AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
            }

            var started = AudioQueueNative.AudioQueueStart(queue, IntPtr.Zero);
            if (started != 0)
            {
                throw new InvalidOperationException(
                    $"Recording could not be started (status {started}).");
            }
        }
        catch
        {
            stream.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        return stream;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in _audio.Reader.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        Streams.TryRemove(_key, out _);
        var queue = Interlocked.Exchange(ref _queue, IntPtr.Zero);
        if (queue != IntPtr.Zero)
        {
            // Stopping before disposing, both synchronously, so no callback can still be running
            // against a queue that is about to be freed.
            AudioQueueNative.AudioQueueStop(queue, true);
            AudioQueueNative.AudioQueueDispose(queue, true);
        }

        _audio.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private static unsafe IntPtr CallbackPointer =>
        (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, uint, IntPtr, void>)&OnBufferFilled;

    /// <summary>
    /// Runs on an AudioQueue thread for every filled buffer, so it copies the audio, hands it to the
    /// channel and immediately returns the buffer to the queue. Holding the buffer would starve
    /// recording of somewhere to write.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void OnBufferFilled(
        IntPtr userData,
        IntPtr queue,
        IntPtr buffer,
        IntPtr startTime,
        uint packetCount,
        IntPtr packetDescriptions)
    {
        try
        {
            if (Streams.TryGetValue(userData, out var stream) && stream._disposed == 0)
            {
                var audio = AudioQueueNative.ReadBuffer(buffer);
                if (audio.Length > 0)
                    stream._audio.Writer.TryWrite(audio);

                AudioQueueNative.AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
            }
        }
        catch
        {
            // Never let a managed fault escape into CoreAudio's callback frame.
        }
    }
}
