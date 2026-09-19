using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.Speech.Recognition;

public sealed class MicroAsrSpeechRecognitionEngine : ISpeechRecognitionEngine, IAsyncDisposable
{
    private readonly IPcmAudioCapture _audioCapture;
    private readonly IMicroAsrRecognizerFactory _recognizers;
    private readonly Func<string> _modelsDirectory;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SemaphoreSlim _preparationGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private readonly object _preparationSync = new();
    private readonly Dictionary<PreparedRecognitionKey, PreparedRecognizer> _preparedRecognizers = [];
    private TaskCompletionSource? _sessionsDrained;
    private Task? _disposeTask;
    private int _sessionOperations;
    private bool _disposed;

    public MicroAsrSpeechRecognitionEngine(
        IPcmAudioCapture audioCapture,
        IApplicationDataPaths applicationData)
        : this(
            audioCapture,
            new MicroAsrRecognizerFactory(),
            () => applicationData.SpeechModelsDirectory)
    {
        ArgumentNullException.ThrowIfNull(applicationData);
    }

    internal MicroAsrSpeechRecognitionEngine(
        IPcmAudioCapture audioCapture,
        IMicroAsrRecognizerFactory recognizers,
        string modelsDirectory)
        : this(audioCapture, recognizers, () => modelsDirectory)
    {
    }

    private MicroAsrSpeechRecognitionEngine(
        IPcmAudioCapture audioCapture,
        IMicroAsrRecognizerFactory recognizers,
        Func<string> modelsDirectory)
    {
        _audioCapture = audioCapture ?? throw new ArgumentNullException(nameof(audioCapture));
        _recognizers = recognizers ?? throw new ArgumentNullException(nameof(recognizers));
        _modelsDirectory = modelsDirectory;
    }

    public async ValueTask PrepareAsync(
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var session = await EnterSessionAsync(
            cancellationToken,
            options.AllowConcurrentSessions).ConfigureAwait(false);
        await _preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var key = PreparedRecognitionKey.Create(options);
        try
        {
            lock (_preparationSync)
            {
                if (_preparedRecognizers.TryGetValue(key, out var existing))
                {
                    existing.ReferenceCount++;
                    return;
                }
            }

            IMicroAsrRecognizer? recognizer = null;
            var capturePrepared = false;
            var modelDirectory = ResolveModelDirectory(options.ModelPath);
            recognizer = await Task.Run(
                () => _recognizers.Create(modelDirectory),
                cancellationToken).ConfigureAwait(false);
            try
            {
                if (_audioCapture is IPreparablePcmAudioCapture preparable)
                {
                    await preparable.PrepareCaptureAsync(
                        options.Sources,
                        PcmAudioFormat.SpeechRecognition,
                        cancellationToken).ConfigureAwait(false);
                    capturePrepared = true;
                }

                lock (_preparationSync)
                {
                    _preparedRecognizers.Add(key, new PreparedRecognizer(options, recognizer));
                }
                recognizer = null;
            }
            finally
            {
                if (recognizer is not null)
                    await recognizer.DisposeAsync().ConfigureAwait(false);
                if (capturePrepared && _audioCapture is IPreparablePcmAudioCapture preparable)
                {
                    await preparable.ReleasePreparedCaptureAsync(
                        options.Sources,
                        PcmAudioFormat.SpeechRecognition).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    public async ValueTask ReleasePreparationAsync(
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var session = await EnterSessionAsync(
            cancellationToken,
            options.AllowConcurrentSessions).ConfigureAwait(false);
        await _preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var key = PreparedRecognitionKey.Create(options);
        PreparedRecognizer? prepared;
        try
        {
            lock (_preparationSync)
            {
                if (!_preparedRecognizers.TryGetValue(key, out prepared))
                    return;

                prepared.ReferenceCount--;
                if (prepared.ReferenceCount > 0 || prepared.ActiveSessions > 0)
                    return;
                _preparedRecognizers.Remove(key);
            }

            await prepared.Recognizer.DisposeAsync().ConfigureAwait(false);
            await ReleasePreparedCaptureAsync(prepared.Options).ConfigureAwait(false);
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    public async IAsyncEnumerable<SpeechRecognitionEvent> RecognizeAsync(
        SpeechRecognitionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var session = await EnterSessionAsync(
            cancellationToken,
            options.AllowConcurrentSessions).ConfigureAwait(false);

        var key = PreparedRecognitionKey.Create(options);
        IMicroAsrRecognizer? recognizer = null;
        PreparedRecognizer? preparedCaptureSession = null;
        var retainsPreparedRecognizer = false;
        Exception? startFailure = null;
        try
        {
            lock (_preparationSync)
            {
                if (_preparedRecognizers.TryGetValue(key, out var prepared))
                {
                    prepared.ActiveSessions++;
                    preparedCaptureSession = prepared;
                    if (prepared.ActiveSessions == 1)
                    {
                        recognizer = prepared.Recognizer;
                        retainsPreparedRecognizer = true;
                    }
                }
            }
            recognizer ??= _recognizers.Create(ResolveModelDirectory(options.ModelPath));
        }
        catch (Exception exception)
        {
            startFailure = exception;
            if (preparedCaptureSession is not null)
                await ReleasePreparedRecognizerSessionAsync(key, preparedCaptureSession)
                    .ConfigureAwait(false);
        }

        if (startFailure is not null)
        {
            yield return new SpeechRecognitionEvent(
                SpeechRecognitionEventKind.Error,
                startFailure.Message);
            yield return new SpeechRecognitionEvent(SpeechRecognitionEventKind.Stopped);
            yield break;
        }

        var events = new RecognitionEventBuffer();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var eventLifetime = new CancellationTokenSource();
        void OnResult(MicroAsrResult result)
        {
            var item = result.Kind switch
            {
                MicroAsrResultKind.Partial => new SpeechRecognitionEvent(
                    SpeechRecognitionEventKind.Partial,
                    result.Text),
                MicroAsrResultKind.Final => new SpeechRecognitionEvent(
                    SpeechRecognitionEventKind.Final,
                    result.Text),
                MicroAsrResultKind.Error => new SpeechRecognitionEvent(
                    SpeechRecognitionEventKind.Error,
                    result.Exception?.Message ?? result.Text),
                _ => null
            };
            if (item is not null)
            {
                if (item.Kind == SpeechRecognitionEventKind.Partial)
                    events.TryWritePartial(item);
                else
                    events.TryWriteReliable(item, eventLifetime.Token);
            }
            if (result.Kind == MicroAsrResultKind.Error)
                lifetime.Cancel();
        }

        recognizer!.ResultAvailable += OnResult;
        var pump = Task.Run(
            () => PumpAudioAsync(
                recognizer,
                options.Sources,
                events,
                retainsPreparedRecognizer,
                lifetime.Token,
                eventLifetime.Token),
            CancellationToken.None);
        try
        {
            yield return new SpeechRecognitionEvent(SpeechRecognitionEventKind.Started);
            await foreach (var item in events.ReadAllAsync(CancellationToken.None)
                               .ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            eventLifetime.Cancel();
            lifetime.Cancel();
            try
            {
                try
                {
                    await pump.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                }
            }
            finally
            {
                recognizer.ResultAvailable -= OnResult;
                if (!retainsPreparedRecognizer)
                    await recognizer.DisposeAsync().ConfigureAwait(false);
                if (preparedCaptureSession is not null)
                    await ReleasePreparedRecognizerSessionAsync(key, preparedCaptureSession).ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleSync)
        {
            _disposed = true;
            _disposeTask ??= DisposeWhenSessionsDrainAsync(
                _sessionOperations == 0
                    ? Task.CompletedTask
                    : _sessionsDrained!.Task);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task<SessionLease> EnterSessionAsync(
        CancellationToken cancellationToken,
        bool allowConcurrent)
    {
        RegisterSessionOperation();
        var gateEntered = false;
        try
        {
            if (!allowConcurrent)
            {
                await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateEntered = true;
            }
            lock (_lifecycleSync)
                ObjectDisposedException.ThrowIf(_disposed, this);
            return new SessionLease(this, gateEntered);
        }
        catch
        {
            if (gateEntered)
                _sessionGate.Release();
            UnregisterSessionOperation();
            throw;
        }
    }

    private void RegisterSessionOperation()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessionOperations++ == 0)
            {
                _sessionsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void UnregisterSessionOperation()
    {
        TaskCompletionSource? sessionsDrained = null;
        lock (_lifecycleSync)
        {
            if (--_sessionOperations == 0)
            {
                sessionsDrained = _sessionsDrained;
                _sessionsDrained = null;
            }
        }
        sessionsDrained?.TrySetResult();
    }

    private async Task DisposeWhenSessionsDrainAsync(Task sessionsDrained)
    {
        try
        {
            await sessionsDrained.ConfigureAwait(false);
            await ReleaseAllPreparedRecognizersAsync().ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Dispose();
            _preparationGate.Dispose();
        }
    }

    private async Task ReleaseAllPreparedRecognizersAsync()
    {
        PreparedRecognizer[] prepared;
        lock (_preparationSync)
        {
            prepared = _preparedRecognizers.Values.ToArray();
            _preparedRecognizers.Clear();
        }

        List<Exception>? failures = null;
        foreach (var entry in prepared)
        {
            try
            {
                await entry.Recognizer.DisposeAsync().ConfigureAwait(false);
                await ReleasePreparedCaptureAsync(entry.Options).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: 1 })
            throw failures[0];
        if (failures is { Count: > 1 })
            throw new AggregateException("MicroASR preparation cleanup failed.", failures);
    }

    private async Task PumpAudioAsync(
        IMicroAsrRecognizer recognizer,
        IReadOnlyList<AudioCaptureSourceToken> sources,
        RecognitionEventBuffer events,
        bool retainsModelAfterSession,
        CancellationToken captureCancellationToken,
        CancellationToken eventCancellationToken)
    {
        Exception? failure = null;
        try
        {
            await foreach (var pcm in _audioCapture.CaptureAsync(
                               sources,
                               PcmAudioFormat.SpeechRecognition,
                               captureCancellationToken).ConfigureAwait(false))
            {
                await recognizer.WriteAsync(pcm, captureCancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (captureCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                try
                {
                    if (retainsModelAfterSession)
                    {
                        await recognizer.CompleteUtteranceAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await recognizer.CompleteAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }

                if (failure is not null)
                {
                    events.TryWriteReliable(new SpeechRecognitionEvent(
                        SpeechRecognitionEventKind.Error,
                        failure.Message), eventCancellationToken);
                }
            }
            finally
            {
                events.CompleteWithStopped();
            }
        }
    }

    private async Task ReleasePreparedRecognizerSessionAsync(
        PreparedRecognitionKey key,
        PreparedRecognizer prepared)
    {
        await _preparationGate.WaitAsync().ConfigureAwait(false);
        var shouldDispose = false;
        try
        {
            lock (_preparationSync)
            {
                if (prepared.ActiveSessions > 0)
                    prepared.ActiveSessions--;
                if (prepared.ActiveSessions == 0 && prepared.ReferenceCount == 0)
                {
                    if (_preparedRecognizers.TryGetValue(key, out var current)
                        && ReferenceEquals(current, prepared))
                    {
                        _preparedRecognizers.Remove(key);
                        shouldDispose = true;
                    }
                }
            }
            if (!shouldDispose)
                return;

            await prepared.Recognizer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (shouldDispose)
                    await ReleasePreparedCaptureAsync(prepared.Options).ConfigureAwait(false);
            }
            finally
            {
                _preparationGate.Release();
            }
        }
    }

    private async Task ReleasePreparedCaptureAsync(SpeechRecognitionOptions options)
    {
        if (_audioCapture is IPreparablePcmAudioCapture preparable)
        {
            await preparable.ReleasePreparedCaptureAsync(
                options.Sources,
                PcmAudioFormat.SpeechRecognition).ConfigureAwait(false);
        }
    }

    private string ResolveModelDirectory(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        var modelsDirectory = Path.GetFullPath(_modelsDirectory());
        var candidate = Path.GetFullPath(Path.Combine(modelsDirectory, modelPath));
        var root = modelsDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? modelsDirectory
            : modelsDirectory + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, pathComparison))
            throw new ArgumentException("The speech model must be inside the model library.", nameof(modelPath));
        return candidate;
    }

    private readonly record struct PreparedRecognitionKey(
        string ModelPath,
        string Language,
        string SourcesKey)
    {
        public static PreparedRecognitionKey Create(SpeechRecognitionOptions options) =>
            new(
                options.ModelPath,
                options.Language,
                string.Join(
                    "\u001f",
                    options.Sources.Select(source => source.Value)
                        .OrderBy(value => value, StringComparer.Ordinal)));
    }

    private sealed record PreparedRecognizer(
        SpeechRecognitionOptions Options,
        IMicroAsrRecognizer Recognizer)
    {
        public int ReferenceCount { get; set; } = 1;
        public int ActiveSessions { get; set; }
    }

    private sealed class SessionLease(
        MicroAsrSpeechRecognitionEngine owner,
        bool gateEntered) : IDisposable
    {
        private MicroAsrSpeechRecognitionEngine? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is null)
                return;
            try
            {
                if (gateEntered)
                    current._sessionGate.Release();
            }
            finally
            {
                current.UnregisterSessionOperation();
            }
        }
    }

    private sealed class RecognitionEventBuffer
    {
        private const int Capacity = 32;
        private const int ProducerCapacity = Capacity - 1;
        private readonly object _sync = new();
        private readonly LinkedList<SpeechRecognitionEvent> _pending = [];
        private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite,
                AllowSynchronousContinuations = false
            });
        private bool _completed;

        public bool TryWritePartial(SpeechRecognitionEvent item)
        {
            lock (_sync)
            {
                if (_completed)
                    return false;

                if (_pending.Last?.Value.Kind == SpeechRecognitionEventKind.Partial)
                {
                    _pending.Last.Value = item;
                    return true;
                }

                if (_pending.Count >= ProducerCapacity)
                {
                    var stalePartial = FindOldestPartial();
                    if (stalePartial is null)
                        return false;
                    _pending.Remove(stalePartial);
                }

                _pending.AddLast(item);
            }

            _signal.Writer.TryWrite(0);
            return true;
        }

        public bool TryWriteReliable(
            SpeechRecognitionEvent item,
            CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.UnsafeRegister(
                static state => ((RecognitionEventBuffer)state!).PulseWriters(),
                this);

            lock (_sync)
            {
                while (!_completed && _pending.Count >= ProducerCapacity)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    var stalePartial = FindOldestPartial();
                    if (stalePartial is not null)
                    {
                        _pending.Remove(stalePartial);
                        break;
                    }

                    Monitor.Wait(_sync);
                }

                if (_completed || cancellationToken.IsCancellationRequested)
                    return false;

                _pending.AddLast(item);
            }

            _signal.Writer.TryWrite(0);
            return true;
        }

        public void CompleteWithStopped()
        {
            lock (_sync)
            {
                if (_completed)
                    return;

                _completed = true;
                _pending.AddLast(new SpeechRecognitionEvent(SpeechRecognitionEventKind.Stopped));
                Monitor.PulseAll(_sync);
            }

            _signal.Writer.TryWrite(0);
        }

        public async IAsyncEnumerable<SpeechRecognitionEvent> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (true)
            {
                SpeechRecognitionEvent? item = null;
                bool completed;
                lock (_sync)
                {
                    if (_pending.First is not null)
                    {
                        item = _pending.First.Value;
                        _pending.RemoveFirst();
                        Monitor.PulseAll(_sync);
                    }
                    completed = _completed;
                }

                if (item is not null)
                {
                    yield return item;
                    continue;
                }
                if (completed)
                    yield break;

                await _signal.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private LinkedListNode<SpeechRecognitionEvent>? FindOldestPartial()
        {
            for (var node = _pending.First; node is not null; node = node.Next)
            {
                if (node.Value.Kind == SpeechRecognitionEventKind.Partial)
                    return node;
            }
            return null;
        }

        private void PulseWriters()
        {
            lock (_sync)
                Monitor.PulseAll(_sync);
        }
    }
}
