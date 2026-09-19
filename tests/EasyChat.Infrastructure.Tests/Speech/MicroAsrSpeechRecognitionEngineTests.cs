using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Speech.Recognition;

namespace EasyChat.Infrastructure.Tests.Speech;

[TestClass]
public sealed class MicroAsrSpeechRecognitionEngineTests
{
    [TestMethod]
    public async Task RecognitionStreamsPcmAndMapsMicroAsrEvents()
    {
        var modelsDirectory = Path.Combine(Path.GetTempPath(), "easychat-microasr-tests", "models");
        var capture = new FakePcmCapture();
        var recognizer = new FakeRecognizer();
        var factory = new FakeRecognizerFactory(recognizer);
        await using var engine = new MicroAsrSpeechRecognitionEngine(
            capture,
            factory,
            modelsDirectory);
        var source = new AudioCaptureSourceToken("platform:opaque-source");
        var events = new List<SpeechRecognitionEvent>();

        await foreach (var item in engine.RecognizeAsync(
                           new SpeechRecognitionOptions("en-US", "en-US", [source])))
        {
            events.Add(item);
        }

        Assert.AreEqual(Path.Combine(modelsDirectory, "en-US"), factory.ModelDirectory);
        CollectionAssert.AreEqual(new[] { source }, capture.Sources.ToArray());
        Assert.AreEqual(PcmAudioFormat.SpeechRecognition, capture.Format);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 2, 0 }, recognizer.WrittenPcm.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                SpeechRecognitionEventKind.Started,
                SpeechRecognitionEventKind.Partial,
                SpeechRecognitionEventKind.Final,
                SpeechRecognitionEventKind.Stopped
            },
            events.Select(item => item.Kind).ToArray());
        Assert.IsTrue(recognizer.Completed);
        Assert.IsTrue(recognizer.Disposed);
    }

    [TestMethod]
    public async Task PreparationLoadsTheRecognizerAndReleaseUnloadsItAfterUtterances()
    {
        var capture = new PreparablePcmCapture();
        var recognizer = new ReusableRecognizer();
        var factory = new FakeRecognizerFactory(recognizer);
        await using var engine = new MicroAsrSpeechRecognitionEngine(
            capture,
            factory,
            Path.Combine(Path.GetTempPath(), "easychat-microasr-tests", "models"));
        var options = CreateOptions();

        await engine.PrepareAsync(options);

        Assert.AreEqual(1, factory.CreateCount);
        Assert.IsTrue(capture.Prepared);
        Assert.IsFalse(recognizer.Disposed);

        var events = await CollectAsync(engine.RecognizeAsync(options));

        Assert.IsTrue(recognizer.UtteranceCompleted);
        Assert.IsFalse(recognizer.Completed);
        Assert.IsFalse(recognizer.Disposed);
        Assert.IsTrue(events.Any(item => item.Kind == SpeechRecognitionEventKind.Final));

        await engine.ReleasePreparationAsync(options);

        Assert.IsTrue(capture.Released);
        Assert.IsTrue(recognizer.Disposed);
    }

    [TestMethod]
    public async Task BackloggedPartialsAreCoalescedWithoutCrossingFinals()
    {
        var recognizer = new BurstRecognizer(
        [
            new MicroAsrResult(MicroAsrResultKind.Partial, "draft-1"),
            new MicroAsrResult(MicroAsrResultKind.Partial, "draft-2"),
            new MicroAsrResult(MicroAsrResultKind.Partial, "draft-3"),
            new MicroAsrResult(MicroAsrResultKind.Final, "final-1"),
            new MicroAsrResult(MicroAsrResultKind.Partial, "next-1"),
            new MicroAsrResult(MicroAsrResultKind.Partial, "next-2"),
            new MicroAsrResult(MicroAsrResultKind.Final, "final-2")
        ]);
        await using var engine = CreateEngine(recognizer);
        await using var events = engine.RecognizeAsync(CreateOptions()).GetAsyncEnumerator();

        Assert.IsTrue(await events.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, events.Current.Kind);
        await recognizer.PublishedAll.WaitAsync(TimeSpan.FromSeconds(1));

        var remaining = new List<SpeechRecognitionEvent>();
        while (await events.MoveNextAsync())
            remaining.Add(events.Current);

        CollectionAssert.AreEqual(
            new[]
            {
                SpeechRecognitionEventKind.Partial,
                SpeechRecognitionEventKind.Final,
                SpeechRecognitionEventKind.Partial,
                SpeechRecognitionEventKind.Final,
                SpeechRecognitionEventKind.Stopped
            },
            remaining.Select(item => item.Kind).ToArray());
        CollectionAssert.AreEqual(
            new[] { "draft-3", "final-1", "next-2", "final-2", null },
            remaining.Select(item => item.Text).ToArray());
    }

    [TestMethod]
    public async Task ControlEventsRemainOrderedWhenTheBoundedBufferIsFull()
    {
        const int resultCount = 64;
        var recognizer = new BurstRecognizer(
            Enumerable.Range(0, resultCount)
                .Select(index => new MicroAsrResult(MicroAsrResultKind.Final, $"final-{index}"))
                .ToArray(),
            signalBeforeIndex: 31);
        await using var engine = CreateEngine(recognizer);
        await using var events = engine.RecognizeAsync(CreateOptions()).GetAsyncEnumerator();

        Assert.IsTrue(await events.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, events.Current.Kind);
        await recognizer.BeforeSignaledResult.WaitAsync(TimeSpan.FromSeconds(1));

        var finals = new List<string?>();
        SpeechRecognitionEventKind lastKind = default;
        while (await events.MoveNextAsync())
        {
            lastKind = events.Current.Kind;
            if (lastKind == SpeechRecognitionEventKind.Final)
                finals.Add(events.Current.Text);
        }

        CollectionAssert.AreEqual(
            Enumerable.Range(0, resultCount).Select(index => $"final-{index}").ToArray(),
            finals.ToArray());
        Assert.AreEqual(SpeechRecognitionEventKind.Stopped, lastKind);
    }

    [TestMethod]
    public async Task ErrorAndStoppedArePreservedWhenTheBoundedBufferIsFull()
    {
        var results = Enumerable.Range(0, 31)
            .Select(index => new MicroAsrResult(MicroAsrResultKind.Final, $"final-{index}"))
            .Append(new MicroAsrResult(
                MicroAsrResultKind.Error,
                string.Empty,
                new InvalidOperationException("recognizer failed")))
            .ToArray();
        var recognizer = new BurstRecognizer(results, signalBeforeIndex: 31);
        await using var engine = CreateEngine(recognizer);
        await using var events = engine.RecognizeAsync(CreateOptions()).GetAsyncEnumerator();

        Assert.IsTrue(await events.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, events.Current.Kind);
        await recognizer.BeforeSignaledResult.WaitAsync(TimeSpan.FromSeconds(1));

        var remaining = new List<SpeechRecognitionEvent>();
        while (await events.MoveNextAsync())
            remaining.Add(events.Current);

        Assert.HasCount(33, remaining);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 31).Select(index => $"final-{index}").ToArray(),
            remaining.Take(31).Select(item => item.Text).ToArray());
        Assert.AreEqual(SpeechRecognitionEventKind.Error, remaining[^2].Kind);
        Assert.AreEqual("recognizer failed", remaining[^2].Text);
        Assert.AreEqual(SpeechRecognitionEventKind.Stopped, remaining[^1].Kind);
    }

    [TestMethod]
    public async Task StoppingConsumptionUnblocksAFullReliableWriter()
    {
        var recognizer = new BurstRecognizer(
            Enumerable.Range(0, 64)
                .Select(index => new MicroAsrResult(MicroAsrResultKind.Final, $"final-{index}"))
                .ToArray(),
            signalBeforeIndex: 31);
        await using var engine = CreateEngine(recognizer);
        await using var events = engine.RecognizeAsync(CreateOptions()).GetAsyncEnumerator();

        Assert.IsTrue(await events.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, events.Current.Kind);
        await recognizer.BeforeSignaledResult.WaitAsync(TimeSpan.FromSeconds(1));

        await events.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(recognizer.Disposed);
    }

    [TestMethod]
    public async Task CancellationDrainsTheRecognizerBeforePublishingStopped()
    {
        using var cancellation = new CancellationTokenSource();
        var recognizer = new CompletingRecognizer();
        await using var engine = CreateEngine(recognizer, new BlockingPcmCapture());
        await using var events = engine.RecognizeAsync(
                CreateOptions(),
                cancellation.Token)
            .GetAsyncEnumerator();

        Assert.IsTrue(await events.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, events.Current.Kind);
        Assert.IsTrue(await events.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Partial, events.Current.Kind);
        cancellation.Cancel();

        var remaining = new List<SpeechRecognitionEvent>();
        while (await events.MoveNextAsync())
            remaining.Add(events.Current);

        CollectionAssert.AreEqual(
            new[] { SpeechRecognitionEventKind.Final, SpeechRecognitionEventKind.Stopped },
            remaining.Select(item => item.Kind).ToArray());
        Assert.AreEqual("flushed final", remaining[0].Text);
        Assert.IsTrue(recognizer.Completed);
    }

    [TestMethod]
    public async Task DisposeWaitsForActiveAndQueuedSessionsToExitBeforeDisposingTheGate()
    {
        using var cancellation = new CancellationTokenSource();
        var recognizer = new CompletingRecognizer();
        var engine = CreateEngine(recognizer, new BlockingPcmCapture());
        await using var active = engine.RecognizeAsync(
                CreateOptions(),
                cancellation.Token)
            .GetAsyncEnumerator();

        Assert.IsTrue(await active.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, active.Current.Kind);
        Assert.IsTrue(await active.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Partial, active.Current.Kind);

        await using var queued = engine.RecognizeAsync(CreateOptions()).GetAsyncEnumerator();
        var queuedMove = queued.MoveNextAsync().AsTask();
        await Task.Yield();
        Assert.IsFalse(queuedMove.IsCompleted);

        var firstDispose = engine.DisposeAsync().AsTask();
        var secondDispose = engine.DisposeAsync().AsTask();
        await Task.Yield();
        Assert.IsFalse(firstDispose.IsCompleted);
        Assert.IsFalse(secondDispose.IsCompleted);

        cancellation.Cancel();
        var remaining = new List<SpeechRecognitionEvent>();
        while (await active.MoveNextAsync())
            remaining.Add(active.Current);

        CollectionAssert.AreEqual(
            new[] { SpeechRecognitionEventKind.Final, SpeechRecognitionEventKind.Stopped },
            remaining.Select(item => item.Kind).ToArray());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
        {
            await queuedMove;
        });
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(recognizer.Completed);
    }

    [TestMethod]
    public async Task ConcurrentSessionsCanRunWhenAllowed()
    {
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var firstRecognizer = new ReusableRecognizer();
        var secondRecognizer = new ReusableRecognizer();
        var factory = new SequenceRecognizerFactory(firstRecognizer, secondRecognizer);
        await using var engine = new MicroAsrSpeechRecognitionEngine(
            new BlockingPcmCapture(),
            factory,
            Path.Combine(Path.GetTempPath(), "easychat-microasr-tests", "models"));
        var options = CreateOptions() with { AllowConcurrentSessions = true };
        await using var first = engine.RecognizeAsync(options, firstCancellation.Token).GetAsyncEnumerator();
        await using var second = engine.RecognizeAsync(options, secondCancellation.Token).GetAsyncEnumerator();

        Assert.IsTrue(await first.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, first.Current.Kind);
        Assert.IsTrue(await second.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, second.Current.Kind);

        firstCancellation.Cancel();
        secondCancellation.Cancel();
        while (await first.MoveNextAsync())
        {
        }
        while (await second.MoveNextAsync())
        {
        }

        Assert.IsTrue(firstRecognizer.Completed);
        Assert.IsTrue(secondRecognizer.Completed);
    }

    [TestMethod]
    public async Task CaptureAndCompletionFailuresStillPublishErrorThenStopped()
    {
        var recognizer = new ThrowingCompleteRecognizer("completion failed");
        await using var engine = CreateEngine(
            recognizer,
            new ThrowingPcmCapture("capture failed"));

        var events = await CollectAsync(engine.RecognizeAsync(CreateOptions()))
            .WaitAsync(TimeSpan.FromSeconds(1));

        CollectionAssert.AreEqual(
            new[]
            {
                SpeechRecognitionEventKind.Started,
                SpeechRecognitionEventKind.Error,
                SpeechRecognitionEventKind.Stopped
            },
            events.Select(item => item.Kind).ToArray());
        Assert.AreEqual("capture failed", events[1].Text);
        Assert.IsTrue(recognizer.CompletionAttempted);
    }

    [TestMethod]
    public async Task CancellationCompletionFailureIsReportedBeforeStopped()
    {
        using var cancellation = new CancellationTokenSource();
        var recognizer = new ThrowingCompleteRecognizer("flush failed");
        await using var engine = CreateEngine(recognizer, new BlockingPcmCapture());
        await using var events = engine.RecognizeAsync(
                CreateOptions(),
                cancellation.Token)
            .GetAsyncEnumerator();

        Assert.IsTrue(await events.MoveNextAsync());
        Assert.AreEqual(SpeechRecognitionEventKind.Started, events.Current.Kind);
        cancellation.Cancel();

        var remaining = new List<SpeechRecognitionEvent>();
        while (await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)))
            remaining.Add(events.Current);

        CollectionAssert.AreEqual(
            new[] { SpeechRecognitionEventKind.Error, SpeechRecognitionEventKind.Stopped },
            remaining.Select(item => item.Kind).ToArray());
        Assert.AreEqual("flush failed", remaining[0].Text);
    }

    [TestMethod]
    public async Task DisposeFailureDoesNotLeakTheSessionGate()
    {
        var factory = new SequenceRecognizerFactory(
            new ThrowingDisposeRecognizer("dispose failed"),
            new FakeRecognizer());
        await using var engine = new MicroAsrSpeechRecognitionEngine(
            new FakePcmCapture(),
            factory,
            Path.Combine(Path.GetTempPath(), "easychat-microasr-tests", "models"));

        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in engine.RecognizeAsync(CreateOptions()))
            {
            }
        });

        Assert.AreEqual("dispose failed", failure.Message);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var secondEvents = await CollectAsync(engine.RecognizeAsync(CreateOptions(), timeout.Token));
        CollectionAssert.AreEqual(
            new[]
            {
                SpeechRecognitionEventKind.Started,
                SpeechRecognitionEventKind.Partial,
                SpeechRecognitionEventKind.Final,
                SpeechRecognitionEventKind.Stopped
            },
            secondEvents.Select(item => item.Kind).ToArray());
    }

    [TestMethod]
    public async Task ModelCreationFailureIsReportedAsErrorThenStopped()
    {
        var factory = new FakeRecognizerFactory(new InvalidDataException("bad model"));
        await using var engine = new MicroAsrSpeechRecognitionEngine(
            new FakePcmCapture(),
            factory,
            Path.Combine(Path.GetTempPath(), "easychat-microasr-tests", "models"));
        var events = new List<SpeechRecognitionEvent>();

        await foreach (var item in engine.RecognizeAsync(
                           new SpeechRecognitionOptions("invalid", "invalid", [])))
        {
            events.Add(item);
        }

        CollectionAssert.AreEqual(
            new[] { SpeechRecognitionEventKind.Error, SpeechRecognitionEventKind.Stopped },
            events.Select(item => item.Kind).ToArray());
        Assert.AreEqual("bad model", events[0].Text);
    }

    [TestMethod]
    public async Task ModelPathCannotEscapeTheModelLibrary()
    {
        var factory = new FakeRecognizerFactory(new FakeRecognizer());
        await using var engine = new MicroAsrSpeechRecognitionEngine(
            new FakePcmCapture(),
            factory,
            Path.Combine(Path.GetTempPath(), "easychat-microasr-tests", "models"));
        var events = new List<SpeechRecognitionEvent>();

        await foreach (var item in engine.RecognizeAsync(
                           new SpeechRecognitionOptions("..", "invalid", [])))
        {
            events.Add(item);
        }

        CollectionAssert.AreEqual(
            new[] { SpeechRecognitionEventKind.Error, SpeechRecognitionEventKind.Stopped },
            events.Select(item => item.Kind).ToArray());
        Assert.IsTrue(string.IsNullOrEmpty(factory.ModelDirectory));
    }

    private static MicroAsrSpeechRecognitionEngine CreateEngine(
        IMicroAsrRecognizer recognizer,
        IPcmAudioCapture? capture = null) =>
        new(
            capture ?? new FakePcmCapture(),
            new FakeRecognizerFactory(recognizer),
            Path.Combine(Path.GetTempPath(), "easychat-microasr-tests", "models"));

    private static SpeechRecognitionOptions CreateOptions() =>
        new("en-US", "en-US", []);

    private static async Task<List<SpeechRecognitionEvent>> CollectAsync(
        IAsyncEnumerable<SpeechRecognitionEvent> source)
    {
        var events = new List<SpeechRecognitionEvent>();
        await foreach (var item in source)
            events.Add(item);
        return events;
    }

    private sealed class FakePcmCapture : IPcmAudioCapture
    {
        public IReadOnlyList<AudioCaptureSourceToken> Sources { get; private set; } = [];
        public PcmAudioFormat Format { get; private set; }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
            IReadOnlyList<AudioCaptureSourceToken> sources,
            PcmAudioFormat format,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Sources = sources;
            Format = format;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new byte[] { 1, 0, 2, 0 };
        }
    }

    private sealed class PreparablePcmCapture : IPcmAudioCapture, IPreparablePcmAudioCapture
    {
        public bool Prepared { get; private set; }
        public bool Released { get; private set; }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
            IReadOnlyList<AudioCaptureSourceToken> sources,
            PcmAudioFormat format,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new byte[] { 1, 0, 2, 0 };
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }

        public ValueTask PrepareCaptureAsync(
            IReadOnlyList<AudioCaptureSourceToken> sources,
            PcmAudioFormat format,
            CancellationToken cancellationToken = default)
        {
            Prepared = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleasePreparedCaptureAsync(
            IReadOnlyList<AudioCaptureSourceToken> sources,
            PcmAudioFormat format,
            CancellationToken cancellationToken = default)
        {
            Released = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingPcmCapture : IPcmAudioCapture
    {
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
            IReadOnlyList<AudioCaptureSourceToken> sources,
            PcmAudioFormat format,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new byte[] { 1, 0, 2, 0 };
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class ThrowingPcmCapture(string message) : IPcmAudioCapture
    {
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
            IReadOnlyList<AudioCaptureSourceToken> sources,
            PcmAudioFormat format,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new byte[] { 1, 0, 2, 0 };
            await Task.Yield();
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeRecognizerFactory : IMicroAsrRecognizerFactory
    {
        private readonly IMicroAsrRecognizer? _recognizer;
        private readonly Exception? _failure;

        public FakeRecognizerFactory(IMicroAsrRecognizer recognizer)
        {
            _recognizer = recognizer;
        }

        public FakeRecognizerFactory(Exception failure)
        {
            _failure = failure;
        }

        public string ModelDirectory { get; private set; } = string.Empty;
        public int CreateCount { get; private set; }

        public IMicroAsrRecognizer Create(string modelDirectory)
        {
            CreateCount++;
            ModelDirectory = modelDirectory;
            if (_failure is not null)
                throw _failure;
            return _recognizer!;
        }
    }

    private sealed class FakeRecognizer : IMicroAsrRecognizer
    {
        public event Action<MicroAsrResult>? ResultAvailable;
        public List<byte> WrittenPcm { get; } = [];
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask WriteAsync(
            ReadOnlyMemory<byte> pcm16,
            CancellationToken cancellationToken = default)
        {
            WrittenPcm.AddRange(pcm16.ToArray());
            ResultAvailable?.Invoke(new MicroAsrResult(MicroAsrResultKind.Partial, "partial"));
            ResultAvailable?.Invoke(new MicroAsrResult(MicroAsrResultKind.Final, "final"));
            return ValueTask.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReusableRecognizer : IMicroAsrRecognizer
    {
        public event Action<MicroAsrResult>? ResultAvailable;
        public bool UtteranceCompleted { get; private set; }
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask WriteAsync(
            ReadOnlyMemory<byte> pcm16,
            CancellationToken cancellationToken = default)
        {
            ResultAvailable?.Invoke(new MicroAsrResult(MicroAsrResultKind.Partial, "partial"));
            return ValueTask.CompletedTask;
        }

        public Task CompleteUtteranceAsync(CancellationToken cancellationToken = default)
        {
            UtteranceCompleted = true;
            ResultAvailable?.Invoke(new MicroAsrResult(MicroAsrResultKind.Final, "final"));
            return Task.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequenceRecognizerFactory(
        params IMicroAsrRecognizer[] recognizers) : IMicroAsrRecognizerFactory
    {
        private int _next;

        public IMicroAsrRecognizer Create(string modelDirectory) =>
            recognizers[_next++];
    }

    private sealed class CompletingRecognizer : IMicroAsrRecognizer
    {
        public event Action<MicroAsrResult>? ResultAvailable;
        public bool Completed { get; private set; }

        public ValueTask WriteAsync(
            ReadOnlyMemory<byte> pcm16,
            CancellationToken cancellationToken = default)
        {
            ResultAvailable?.Invoke(new MicroAsrResult(MicroAsrResultKind.Partial, "draft"));
            return ValueTask.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            Completed = true;
            ResultAvailable?.Invoke(new MicroAsrResult(MicroAsrResultKind.Final, "flushed final"));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingCompleteRecognizer(string message) : IMicroAsrRecognizer
    {
        public event Action<MicroAsrResult>? ResultAvailable
        {
            add { }
            remove { }
        }
        public bool CompletionAttempted { get; private set; }

        public ValueTask WriteAsync(
            ReadOnlyMemory<byte> pcm16,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            CompletionAttempted = true;
            throw new InvalidOperationException(message);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingDisposeRecognizer(string message) : IMicroAsrRecognizer
    {
        public event Action<MicroAsrResult>? ResultAvailable
        {
            add { }
            remove { }
        }

        public ValueTask WriteAsync(
            ReadOnlyMemory<byte> pcm16,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException(message));
    }

    private sealed class BurstRecognizer : IMicroAsrRecognizer
    {
        private readonly IReadOnlyList<MicroAsrResult> _results;
        private readonly int? _signalBeforeIndex;
        private readonly TaskCompletionSource _publishedAll = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _beforeSignaledResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public BurstRecognizer(
            IReadOnlyList<MicroAsrResult> results,
            int? signalBeforeIndex = null)
        {
            _results = results;
            _signalBeforeIndex = signalBeforeIndex;
            if (signalBeforeIndex is null)
                _beforeSignaledResult.TrySetResult();
        }

        public event Action<MicroAsrResult>? ResultAvailable;
        public Task PublishedAll => _publishedAll.Task;
        public Task BeforeSignaledResult => _beforeSignaledResult.Task;
        public bool Disposed { get; private set; }

        public ValueTask WriteAsync(
            ReadOnlyMemory<byte> pcm16,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < _results.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_signalBeforeIndex == index)
                    _beforeSignaledResult.TrySetResult();
                ResultAvailable?.Invoke(_results[index]);
            }
            _publishedAll.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
