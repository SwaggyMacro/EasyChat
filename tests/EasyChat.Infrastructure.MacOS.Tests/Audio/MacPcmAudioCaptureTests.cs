using System.Runtime.CompilerServices;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Audio;

/// <summary>
/// The capture orchestration — buffering, framing, mixing and shutdown — driven by scripted sources.
/// This is where the concurrency mistakes live, and none of them need a microphone to provoke.
/// </summary>
[TestClass]
public sealed class MacPcmAudioCaptureTests
{
    private static readonly PcmAudioFormat Format = PcmAudioFormat.SpeechRecognition;

    /// <summary>16 kHz mono 16-bit for twenty milliseconds.</summary>
    private const int FrameSize = 640;

    private static MacPcmAudioCapture Create(FakeSourceFactory factory) =>
        new(factory, NullLogger<MacPcmAudioCapture>.Instance);

    [TestMethod]
    public async Task AudioIsEmittedAsWholeFramesOfTheExpectedSize()
    {
        var factory = new FakeSourceFactory();
        factory.Add(MacAudioSourceTokens.ForSystemOutput(), Chunks(FrameSize * 3));

        var frames = await CollectAsync(Create(factory), factory.Tokens);

        Assert.HasCount(3, frames);
        foreach (var frame in frames)
            Assert.HasCount(FrameSize, frame);
    }

    [TestMethod]
    public async Task ChunksThatDoNotDivideIntoFramesAreCarriedForwardRatherThanPadded()
    {
        // Devices hand over whatever size their clock produces. Padding would insert clicks and
        // dropping the remainder would drift, so the leftover has to join the next chunk.
        var factory = new FakeSourceFactory();
        factory.Add(
            MacAudioSourceTokens.ForSystemOutput(),
            [Bytes(FrameSize / 2, 1), Bytes(FrameSize / 2, 2), Bytes(FrameSize, 3)]);

        var frames = await CollectAsync(Create(factory), factory.Tokens);

        Assert.HasCount(2, frames);
        Assert.AreEqual(1, frames[0][0], "the first frame starts with the first chunk");
        Assert.AreEqual(2, frames[0][FrameSize / 2], "and continues into the second");
    }

    [TestMethod]
    public async Task AnIncompleteTailIsNotEmittedAsAShortFrame()
    {
        var factory = new FakeSourceFactory();
        factory.Add(MacAudioSourceTokens.ForSystemOutput(), [Bytes(FrameSize + 100, 7)]);

        var frames = await CollectAsync(Create(factory), factory.Tokens);

        Assert.HasCount(1, frames);
    }

    [TestMethod]
    public async Task TwoSourcesAreMixedIntoOneStream()
    {
        var factory = new FakeSourceFactory();
        factory.Add(MacAudioSourceTokens.ForSystemOutput(), [Pcm(FrameSize, 1000)]);
        factory.Add(MacAudioSourceTokens.ForMicrophone("device"), [Pcm(FrameSize, 500)]);

        var frames = await CollectAsync(Create(factory), factory.Tokens);

        Assert.IsNotEmpty(frames);
        Assert.AreEqual(1500, ReadSample(frames[0], 0), "the sources are summed");
    }

    [TestMethod]
    public async Task ASourceThatEndsEarlyDoesNotStopTheOthers()
    {
        var factory = new FakeSourceFactory();
        factory.Add(MacAudioSourceTokens.ForSystemOutput(), Chunks(FrameSize * 4));
        factory.Add(MacAudioSourceTokens.ForMicrophone("device"), [Bytes(FrameSize, 9)]);

        var frames = await CollectAsync(Create(factory), factory.Tokens);

        Assert.IsGreaterThanOrEqualTo(3, frames.Count, "the longer source keeps producing");
    }

    [TestMethod]
    public async Task AFailingSourceEndsTheCaptureInsteadOfGoingSilent()
    {
        var factory = new FakeSourceFactory();
        factory.Add(
            MacAudioSourceTokens.ForSystemOutput(),
            [Bytes(FrameSize, 1)],
            failAfterChunks: 1);

        var frames = await CollectAsync(Create(factory), factory.Tokens);

        // The capture completes rather than hanging; the workflow can then report the failure.
        Assert.IsLessThanOrEqualTo(1, frames.Count);
    }

    [TestMethod]
    public async Task TokensThisPlatformDidNotIssueAreIgnoredAndProduceNothing()
    {
        var factory = new FakeSourceFactory();

        var frames = await CollectAsync(
            Create(factory),
            [new AudioCaptureSourceToken("windows:speakers")]);

        Assert.IsEmpty(frames);
        Assert.AreEqual(0, factory.OpenCount, "a foreign token must not reach the platform.");
    }

    [TestMethod]
    public async Task NoSourcesProducesNoAudio()
    {
        var frames = await CollectAsync(Create(new FakeSourceFactory()), []);

        Assert.IsEmpty(frames);
    }

    [TestMethod]
    public async Task CancellingStopsPromptlyAndClosesEverySource()
    {
        var factory = new FakeSourceFactory();
        factory.Add(MacAudioSourceTokens.ForSystemOutput(), Endless());
        factory.Add(MacAudioSourceTokens.ForMicrophone("device"), Endless());

        using var cancellation = new CancellationTokenSource();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var collected = 0;
        await foreach (var _ in Create(factory).CaptureAsync(
                           factory.Tokens,
                           Format,
                           cancellation.Token))
        {
            if (++collected == 3)
                await cancellation.CancelAsync();
        }

        elapsed.Stop();
        Assert.IsLessThan(TimeSpan.FromSeconds(5), elapsed.Elapsed, "cancellation must not hang");
        Assert.AreEqual(2, factory.DisposedCount, "every opened source must be closed");
    }

    [TestMethod]
    public async Task PreparingOpensAndImmediatelyClosesEachSource()
    {
        var factory = new FakeSourceFactory();
        factory.Add(MacAudioSourceTokens.ForMicrophone("device"), Endless());

        await Create(factory).PrepareCaptureAsync(factory.Tokens, Format);

        Assert.AreEqual(1, factory.OpenCount);
        Assert.AreEqual(1, factory.DisposedCount, "warm-up must not leave the device open");
    }

    [TestMethod]
    public async Task AFailureWhileWarmingUpDoesNotPreventStarting()
    {
        var factory = new FakeSourceFactory { FailToOpen = true };
        factory.Add(MacAudioSourceTokens.ForMicrophone("device"), Endless());

        // Warming up is an optimisation, so a failure is logged and swallowed.
        await Create(factory).PrepareCaptureAsync(factory.Tokens, Format);
    }

    private static async Task<List<byte[]>> CollectAsync(
        MacPcmAudioCapture capture,
        IReadOnlyList<AudioCaptureSourceToken> tokens)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var frames = new List<byte[]>();
        await foreach (var frame in capture.CaptureAsync(tokens, Format, timeout.Token))
            frames.Add(frame.ToArray());
        return frames;
    }

    private static byte[][] Chunks(int totalBytes)
    {
        var chunks = new List<byte[]>();
        for (var written = 0; written < totalBytes; written += FrameSize)
            chunks.Add(Bytes(Math.Min(FrameSize, totalBytes - written), (byte)(chunks.Count + 1)));
        return [.. chunks];
    }

    private static IEnumerable<byte[]> Endless()
    {
        while (true)
            yield return Bytes(FrameSize, 1);
    }

    private static byte[] Bytes(int length, byte value)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    private static byte[] Pcm(int length, short sample)
    {
        var bytes = new byte[length];
        for (var offset = 0; offset + 1 < length; offset += 2)
        {
            bytes[offset] = (byte)(sample & 0xFF);
            bytes[offset + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return bytes;
    }

    private static short ReadSample(byte[] pcm, int index) =>
        (short)(pcm[index * 2] | (pcm[(index * 2) + 1] << 8));

    private sealed class FakeSourceFactory : IMacAudioSourceFactory
    {
        private readonly List<AudioCaptureSourceToken> _tokens = [];
        private readonly Dictionary<string, (IEnumerable<byte[]> Chunks, int FailAfter)> _scripts = [];

        internal bool FailToOpen { get; set; }

        internal int OpenCount { get; private set; }

        internal int DisposedCount { get; private set; }

        internal IReadOnlyList<AudioCaptureSourceToken> Tokens => _tokens;

        internal void Add(
            AudioCaptureSourceToken token,
            IEnumerable<byte[]> chunks,
            int failAfterChunks = -1)
        {
            _tokens.Add(token);
            _scripts[token.Value] = (chunks, failAfterChunks);
        }

        public ValueTask<IMacAudioSourceStream> OpenAsync(
            MacAudioSource source,
            PcmAudioFormat format,
            CancellationToken cancellationToken)
        {
            if (FailToOpen)
                throw new InvalidOperationException("The device is unavailable.");

            OpenCount++;
            var key = _tokens[Math.Min(OpenCount - 1, _tokens.Count - 1)].Value;
            var script = _scripts[key];
            return ValueTask.FromResult<IMacAudioSourceStream>(
                new FakeStream(script.Chunks, script.FailAfter, () => DisposedCount++));
        }
    }

    private sealed class FakeStream(
        IEnumerable<byte[]> chunks,
        int failAfter,
        Action onDisposed) : IMacAudioSourceStream
    {
        private int _disposed;

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var delivered = 0;
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (failAfter >= 0 && delivered >= failAfter)
                    throw new InvalidOperationException("The device stopped responding.");

                delivered++;
                yield return chunk;
                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDisposed();
            return ValueTask.CompletedTask;
        }
    }
}
