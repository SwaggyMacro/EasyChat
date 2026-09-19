using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.Windows.Speech;

internal interface IWindowsPcmCaptureSession : IAsyncDisposable
{
    event Action<ReadOnlyMemory<byte>>? DataAvailable;
    event Action<Exception>? Failed;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
}

internal interface IWindowsPcmCaptureSessionFactory
{
    IWindowsPcmCaptureSession CreateSystemOutput(PcmAudioFormat format);
    IWindowsPcmCaptureSession CreateProcess(int processId, PcmAudioFormat format);
    IWindowsPcmCaptureSession CreateMicrophone(string deviceId, PcmAudioFormat format);
}

internal sealed class WindowsPcmCaptureSessionFactory : IWindowsPcmCaptureSessionFactory
{
    public IWindowsPcmCaptureSession CreateSystemOutput(PcmAudioFormat format) =>
        new WindowsSystemAudioCaptureSession(format);

    public IWindowsPcmCaptureSession CreateProcess(int processId, PcmAudioFormat format) =>
        new WindowsProcessAudioCaptureSession(processId, format);

    public IWindowsPcmCaptureSession CreateMicrophone(string deviceId, PcmAudioFormat format) =>
        new WindowsMicrophoneAudioCaptureSession(deviceId, format);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPcmAudioCapture : IPcmAudioCapture, IPreparablePcmAudioCapture, IAsyncDisposable
{
    private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(20);
    private const int BufferedFrameCapacity = 50;
    private readonly IWindowsPcmCaptureSessionFactory _sessions;
    private readonly object _preparedSync = new();
    private readonly Dictionary<PreparedCaptureKey, PreparedCapture> _preparedCaptures = [];

    public WindowsPcmAudioCapture()
        : this(new WindowsPcmCaptureSessionFactory())
    {
    }

    internal WindowsPcmAudioCapture(IWindowsPcmCaptureSessionFactory sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
        IReadOnlyList<AudioCaptureSourceToken> sources,
        PcmAudioFormat format,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ValidateFormat(format);

        var key = PreparedCaptureKey.Create(sources, format);
        PreparedCapture? prepared;
        lock (_preparedSync)
            _preparedCaptures.TryGetValue(key, out prepared);
        if (prepared is not null)
        {
            await foreach (var frame in prepared.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return frame;
            yield break;
        }

        await foreach (var frame in CaptureCoreAsync(sources, format, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    public async ValueTask PrepareCaptureAsync(
        IReadOnlyList<AudioCaptureSourceToken> sources,
        PcmAudioFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ValidateFormat(format);

        var key = PreparedCaptureKey.Create(sources, format);
        PreparedCapture prepared;
        lock (_preparedSync)
        {
            if (!_preparedCaptures.TryGetValue(key, out prepared!))
            {
                prepared = new PreparedCapture(this, key, sources.ToArray(), format);
                _preparedCaptures.Add(key, prepared);
            }
        }

        try
        {
            await prepared.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RemovePreparedCapture(key, prepared);
            await prepared.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask ReleasePreparedCaptureAsync(
        IReadOnlyList<AudioCaptureSourceToken> sources,
        PcmAudioFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        cancellationToken.ThrowIfCancellationRequested();
        var key = PreparedCaptureKey.Create(sources, format);
        PreparedCapture? prepared;
        lock (_preparedSync)
        {
            if (!_preparedCaptures.Remove(key, out prepared))
                return;
        }

        await prepared.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        PreparedCapture[] prepared;
        lock (_preparedSync)
        {
            prepared = _preparedCaptures.Values.ToArray();
            _preparedCaptures.Clear();
        }

        foreach (var capture in prepared)
            await capture.DisposeAsync().ConfigureAwait(false);
    }

    private void RemovePreparedCapture(
        PreparedCaptureKey key,
        PreparedCapture capture)
    {
        lock (_preparedSync)
        {
            if (_preparedCaptures.TryGetValue(key, out var current)
                && ReferenceEquals(current, capture))
            {
                _preparedCaptures.Remove(key);
            }
        }
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureCoreAsync(
        IReadOnlyList<AudioCaptureSourceToken> sources,
        PcmAudioFormat format,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ValidateFormat(format);

        var captures = CreateSessions(sources, format);
        var buffers = captures.Select(_ => new PcmByteBuffer(format.SampleRateHz * 4)).ToArray();
        var failures = new Queue<Exception>();
        var sync = new object();
        var dataHandlers = new Action<ReadOnlyMemory<byte>>[captures.Count];
        var failureHandlers = new Action<Exception>[captures.Count];
        var frames = Channel.CreateBounded<ReadOnlyMemory<byte>>(
            new BoundedChannelOptions(BufferedFrameCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        Task producer = Task.CompletedTask;

        for (var index = 0; index < captures.Count; index++)
        {
            var captureIndex = index;
            dataHandlers[index] = pcm => buffers[captureIndex].Write(pcm.Span);
            failureHandlers[index] = exception =>
            {
                lock (sync)
                    failures.Enqueue(exception);
            };
            captures[index].DataAvailable += dataHandlers[index];
            captures[index].Failed += failureHandlers[index];
        }

        try
        {
            foreach (var capture in captures)
                await capture.StartAsync(cancellationToken).ConfigureAwait(false);

            producer = ProduceFramesAsync(
                buffers,
                format,
                failures,
                sync,
                frames.Writer,
                producerCancellation.Token);
            await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
                yield return frame;
        }
        finally
        {
            producerCancellation.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            finally
            {
                await StopAndDisposeAsync(
                    captures,
                    dataHandlers,
                    failureHandlers).ConfigureAwait(false);
            }
        }
    }

    private static async Task ProduceFramesAsync(
        IReadOnlyList<PcmByteBuffer> buffers,
        PcmAudioFormat format,
        Queue<Exception> failures,
        object sync,
        ChannelWriter<ReadOnlyMemory<byte>> writer,
        CancellationToken cancellationToken)
    {
        Exception? completionFailure = null;
        try
        {
            using var timer = new PeriodicTimer(FrameDuration);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Exception? failure = null;
                lock (sync)
                {
                    if (failures.Count > 0)
                        failure = failures.Dequeue();
                }
                if (failure is not null)
                    throw new InvalidOperationException("Windows audio capture failed.", failure);

                if (!writer.TryWrite(MixFrame(buffers, format)))
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }
        finally
        {
            writer.TryComplete(completionFailure);
        }
    }

    private static async Task StopAndDisposeAsync(
        IReadOnlyList<IWindowsPcmCaptureSession> captures,
        IReadOnlyList<Action<ReadOnlyMemory<byte>>> dataHandlers,
        IReadOnlyList<Action<Exception>> failureHandlers)
    {
        List<Exception>? cleanupFailures = null;
        for (var index = captures.Count - 1; index >= 0; index--)
        {
            captures[index].DataAvailable -= dataHandlers[index];
            captures[index].Failed -= failureHandlers[index];
            try
            {
                await captures[index].StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (cleanupFailures ??= []).Add(exception);
            }

            try
            {
                await captures[index].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (cleanupFailures ??= []).Add(exception);
            }
        }

        if (cleanupFailures is { Count: 1 })
            throw cleanupFailures[0];
        if (cleanupFailures is { Count: > 1 })
            throw new AggregateException("Windows audio capture cleanup failed.", cleanupFailures);
    }

    private IReadOnlyList<IWindowsPcmCaptureSession> CreateSessions(
        IReadOnlyList<AudioCaptureSourceToken> sources,
        PcmAudioFormat format)
    {
        // An empty selection preserves the legacy system-audio default. Once the user
        // explicitly selects multiple sources, every selected source participates in the mix.
        if (sources.Count == 0)
            return [_sessions.CreateSystemOutput(format)];

        var result = new List<IWindowsPcmCaptureSession>(sources.Count);
        foreach (var source in sources.Distinct())
        {
            if (source == WindowsAudioCaptureSourceCatalog.SystemOutputToken)
            {
                result.Add(_sessions.CreateSystemOutput(format));
                continue;
            }
            if (WindowsAudioCaptureSourceCatalog.TryGetProcessId(source, out var processId))
            {
                result.Add(_sessions.CreateProcess(processId, format));
                continue;
            }
            if (WindowsAudioCaptureSourceCatalog.TryGetCaptureDeviceId(source, out var deviceId))
            {
                result.Add(_sessions.CreateMicrophone(deviceId, format));
                continue;
            }

            throw new ArgumentException(
                $"Audio source '{source.Value}' is not supported by the Windows adapter.",
                nameof(sources));
        }
        return result;
    }

    private static ReadOnlyMemory<byte> MixFrame(
        IReadOnlyList<PcmByteBuffer> sources,
        PcmAudioFormat format)
    {
        var bytesPerFrame = format.SampleRateHz * format.ChannelCount *
                            (format.BitsPerSample / 8) * (int)FrameDuration.TotalMilliseconds / 1000;
        var output = new byte[bytesPerFrame];
        var sourceFrame = new byte[bytesPerFrame];
        var sums = new int[bytesPerFrame / 2];

        foreach (var source in sources)
        {
            Array.Clear(sourceFrame);
            var read = source.Read(sourceFrame);
            if (read == 0)
                continue;
            for (var offset = 0; offset + 1 < read; offset += 2)
                sums[offset / 2] += BitConverter.ToInt16(sourceFrame, offset);
        }

        for (var sample = 0; sample < sums.Length; sample++)
        {
            var value = (short)Math.Clamp(sums[sample], short.MinValue, short.MaxValue);
            output[sample * 2] = (byte)value;
            output[(sample * 2) + 1] = (byte)(value >> 8);
        }
        return output;
    }

    private static void ValidateFormat(PcmAudioFormat format)
    {
        if (format != PcmAudioFormat.SpeechRecognition)
        {
            throw new NotSupportedException(
                "Windows speech capture currently supports 16 kHz mono PCM16 only.");
        }
    }

    private readonly record struct PreparedCaptureKey(
        string SourcesKey,
        PcmAudioFormat Format)
    {
        public static PreparedCaptureKey Create(
            IReadOnlyList<AudioCaptureSourceToken> sources,
            PcmAudioFormat format) =>
            new(
                string.Join(
                    "\u001f",
                    sources.Select(source => source.Value)
                        .OrderBy(value => value, StringComparer.Ordinal)),
                format);
    }

    private sealed class PreparedCapture(
        WindowsPcmAudioCapture owner,
        PreparedCaptureKey key,
        IReadOnlyList<AudioCaptureSourceToken> sources,
        PcmAudioFormat format) : IAsyncDisposable
    {
        private const int Capacity = 8;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly object _readersSync = new();
        private readonly List<Channel<ReadOnlyMemory<byte>>> _readers = [];
        private readonly TaskCompletionSource _ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _startSync = new();
        private Task? _runner;
        private int _disposed;

        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            Task runner;
            lock (_startSync)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                _runner ??= Task.Run(RunAsync, CancellationToken.None);
                runner = _runner;
            }

            try
            {
                await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (runner.IsFaulted)
                    await runner.ConfigureAwait(false);
                throw;
            }
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var frames = Channel.CreateBounded<ReadOnlyMemory<byte>>(
                new BoundedChannelOptions(Capacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    AllowSynchronousContinuations = false
                });
            lock (_readersSync)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    frames.Writer.TryComplete();
                }
                else
                {
                    _readers.Add(frames);
                }
            }

            try
            {
                await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    yield return frame;
                }
            }
            finally
            {
                lock (_readersSync)
                    _readers.Remove(frames);
                frames.Writer.TryComplete();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _lifetime.Cancel();
            _ready.TrySetCanceled(_lifetime.Token);
            Task? runner;
            lock (_startSync)
                runner = _runner;
            if (runner is not null)
            {
                try
                {
                    await runner.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                }
            }
            _lifetime.Dispose();
        }

        private async Task RunAsync()
        {
            Exception? failure = null;
            try
            {
                await foreach (var frame in owner.CaptureCoreAsync(sources, format, _lifetime.Token)
                                   .ConfigureAwait(false))
                {
                    // A timed PCM frame proves all native sessions have started successfully.
                    _ready.TrySetResult();
                    Channel<ReadOnlyMemory<byte>>[] readers;
                    lock (_readersSync)
                        readers = _readers.ToArray();
                    foreach (var reader in readers)
                        reader.Writer.TryWrite(frame);
                }

                if (!_lifetime.IsCancellationRequested)
                {
                    failure = new InvalidOperationException(
                        "The prepared Windows audio capture stopped unexpectedly.");
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (failure is not null)
                    _ready.TrySetException(failure);
                else if (_lifetime.IsCancellationRequested)
                    _ready.TrySetCanceled(_lifetime.Token);
                else
                    _ready.TrySetException(new InvalidOperationException(
                        "The prepared Windows audio capture stopped before it became ready."));

                Channel<ReadOnlyMemory<byte>>[] readers;
                lock (_readersSync)
                {
                    readers = _readers.ToArray();
                    _readers.Clear();
                }
                foreach (var reader in readers)
                    reader.Writer.TryComplete(failure);
                if (failure is not null)
                    owner.RemovePreparedCapture(key, this);
            }
        }
    }

    internal sealed class PcmByteBuffer(int capacity)
    {
        private readonly object _sync = new();
        private readonly Queue<byte[]> _chunks = new();
        private readonly int _capacity = capacity;
        private int _headOffset;
        private int _length;

        public void Write(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                return;
            lock (_sync)
            {
                var chunk = data.ToArray();
                _chunks.Enqueue(chunk);
                _length += chunk.Length;
                while (_length > _capacity && _chunks.Count > 0)
                {
                    var removed = _chunks.Dequeue();
                    _length -= removed.Length - _headOffset;
                    _headOffset = 0;
                }
            }
        }

        public int Read(Span<byte> destination)
        {
            lock (_sync)
            {
                var written = 0;
                while (written < destination.Length && _chunks.TryPeek(out var chunk))
                {
                    var available = chunk.Length - _headOffset;
                    var count = Math.Min(available, destination.Length - written);
                    chunk.AsSpan(_headOffset, count).CopyTo(destination[written..]);
                    written += count;
                    _headOffset += count;
                    _length -= count;
                    if (_headOffset == chunk.Length)
                    {
                        _chunks.Dequeue();
                        _headOffset = 0;
                    }
                }
                return written;
            }
        }
    }
}
