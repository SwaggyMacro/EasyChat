using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Captures system or single-application audio through ScreenCaptureKit.
/// </summary>
/// <remarks>
/// <para>
/// ScreenCaptureKit has no audio-only mode and no block-based output, so this needs two things the
/// rest of the adapter did not: a stream that also carries a token amount of video, and a real
/// Objective-C delegate object to receive the samples. The delegate class is built at runtime by
/// <see cref="ObjectiveCClassBuilder"/>, whose dispatch is verified independently.
/// </para>
/// <para>
/// Samples arrive as non-interleaved 32-bit float at the rate the configuration asked for. A single
/// channel is requested so the buffer is one contiguous plane, and the conversion to the
/// recogniser's format is the ordinary shared one.
/// </para>
/// </remarks>
internal sealed class MacScreenCaptureAudioStream : IMacAudioSourceStream
{
    /// <summary>
    /// ScreenCaptureKit's own rate. Asking for the recogniser's 16 kHz directly is not reliably
    /// honoured, so the capture runs at the native rate and is resampled by the shared converter.
    /// </summary>
    private const int CaptureSampleRateHz = 48_000;

    private static readonly ConcurrentDictionary<IntPtr, MacScreenCaptureAudioStream> Streams = new();
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private static readonly Lock DelegateGate = new();
    private static IntPtr _delegateType;
    private static TaskCompletionSource<string?>? _pendingStart;
    private static IntPtr _startBlock;

    private readonly Channel<ReadOnlyMemory<byte>> _audio =
        Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    private readonly PcmAudioFormat _format;
    private IntPtr _stream;
    private IntPtr _delegate;
    private IntPtr _queue;
    private int _disposed;

    private MacScreenCaptureAudioStream(PcmAudioFormat format) => _format = format;

    /// <summary>
    /// Starts capturing. <paramref name="processIdentifier"/> of zero captures everything on the
    /// main display; otherwise only that application's audio is captured.
    /// </summary>
    internal static async ValueTask<MacScreenCaptureAudioStream> StartAsync(
        int processIdentifier,
        PcmAudioFormat format,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException("Audio capture requires macOS 26 or later.");
        if (!ScreenCaptureAccessNative.HasAccess())
        {
            // Preflighted rather than inferred from an opaque ScreenCaptureKit error, so the user is
            // told which approval is missing.
            throw new InvalidOperationException(
                "Screen Recording access has not been granted to EasyChat, which macOS also requires to capture audio.");
        }

        var content = await ScreenCaptureKitNative.GetShareableContentAsync()
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
        if (content.Content == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                content.Error ?? "macOS did not report any capturable content.");
        }

        var stream = new MacScreenCaptureAudioStream(format);
        try
        {
            await stream.BuildAsync(content.Content, processIdentifier, cancellationToken)
                .ConfigureAwait(false);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            CoreFoundationNative.CFRelease(content.Content);
        }
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

        var streamHandle = Interlocked.Exchange(ref _stream, IntPtr.Zero);
        var delegateHandle = Interlocked.Exchange(ref _delegate, IntPtr.Zero);
        var queue = Interlocked.Exchange(ref _queue, IntPtr.Zero);

        if (delegateHandle != IntPtr.Zero)
            Streams.TryRemove(delegateHandle, out _);

        using var pool = AutoreleasePool.Push();
        if (streamHandle != IntPtr.Zero)
        {
            ObjectiveCNative.Send(
                streamHandle,
                ObjectiveCNative.GetSelector("stopCaptureWithCompletionHandler:"),
                IntPtr.Zero);
            ObjectiveCNative.Send(streamHandle, ObjectiveCNative.GetSelector("release"));
        }

        if (delegateHandle != IntPtr.Zero)
            ObjectiveCNative.Send(delegateHandle, ObjectiveCNative.GetSelector("release"));
        if (queue != IntPtr.Zero)
            DispatchNative.dispatch_release(queue);

        _audio.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async Task BuildAsync(
        IntPtr content,
        int processIdentifier,
        CancellationToken cancellationToken)
    {
        IntPtr filter;
        IntPtr configuration;
        using (AutoreleasePool.Push())
        {
            var display = FirstDisplay(content);
            if (display == IntPtr.Zero)
                throw new InvalidOperationException("macOS reported no capturable display.");

            filter = processIdentifier == 0
                ? ScreenCaptureKitNative.CreateDisplayFilter(
                    display,
                    FoundationNative.CreateMutableArray())
                : ScreenCaptureKitNative.CreateApplicationFilter(
                    display,
                    ApplicationsMatching(content, processIdentifier),
                    FoundationNative.CreateMutableArray());
            if (filter == IntPtr.Zero)
                throw new InvalidOperationException("The capture filter could not be built.");

            configuration = ScreenCaptureKitNative.CreateAudioConfiguration(CaptureSampleRateHz);
            _delegate = ObjectiveCClassBuilder.CreateInstance(DelegateType());
            if (_delegate == IntPtr.Zero)
                throw new InvalidOperationException("The stream delegate could not be created.");

            Streams[_delegate] = this;
            _stream = ScreenCaptureKitNative.CreateStream(filter, configuration, _delegate);
            ObjectiveCNative.Send(filter, ObjectiveCNative.GetSelector("release"));
            ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("release"));
            if (_stream == IntPtr.Zero)
                throw new InvalidOperationException("The capture stream could not be created.");

            _queue = DispatchNative.CreateSerialQueue("cn.ncii.easychat.audio-capture");
            var added = ObjectiveCNative.SendReturningBool(
                _stream,
                ObjectiveCNative.GetSelector("addStreamOutput:type:sampleHandlerQueue:error:"),
                _delegate,
                ScreenCaptureKitNative.AudioOutputType,
                _queue,
                IntPtr.Zero);
            if (!added)
                throw new InvalidOperationException("The audio output could not be attached.");
        }

        var error = await StartCaptureAsync(_stream, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Starts the stream and waits for its completion handler. Starts are serialised because the
    /// handler is a single process-wide block, and because two captures are only ever started as
    /// part of one user action anyway.
    /// </summary>
    private static async Task<string?> StartCaptureAsync(
        IntPtr stream,
        CancellationToken cancellationToken)
    {
        await StartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingStart = completion;
            using var pool = AutoreleasePool.Push();
            ObjectiveCNative.Send(
                stream,
                ObjectiveCNative.GetSelector("startCaptureWithCompletionHandler:"),
                StartBlock());
            return await completion.Task
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            StartGate.Release();
        }
    }

    private static IntPtr FirstDisplay(IntPtr content)
    {
        var displays = ScreenCaptureKitNative.Displays(content);
        return FoundationNative.CountOf(displays) > 0
            ? FoundationNative.ItemAt(displays, 0)
            : IntPtr.Zero;
    }

    private static IntPtr ApplicationsMatching(IntPtr content, int processIdentifier)
    {
        var matching = FoundationNative.CreateMutableArray();
        var applications = ScreenCaptureKitNative.Applications(content);
        for (nint index = 0; index < FoundationNative.CountOf(applications); index++)
        {
            var application = FoundationNative.ItemAt(applications, index);
            if (ScreenCaptureKitNative.ApplicationProcessIdentifier(application) == processIdentifier)
                FoundationNative.Add(matching, application);
        }

        if (FoundationNative.CountOf(matching) == 0)
        {
            throw new InvalidOperationException(
                "The target application is no longer producing capturable content.");
        }

        return matching;
    }

    private static IntPtr DelegateType()
    {
        lock (DelegateGate)
        {
            if (_delegateType != IntPtr.Zero)
                return _delegateType;

            _delegateType = ObjectiveCClassBuilder.DefineClass(
                "EasyChatStreamAudioOutput",
                [
                    new ObjectiveCClassBuilder.RuntimeMethod(
                        "stream:didOutputSampleBuffer:ofType:",
                        SampleCallbackPointer,
                        // void, self, _cmd, SCStream*, CMSampleBufferRef, SCStreamOutputType.
                        "v@:@@q"),
                    new ObjectiveCClassBuilder.RuntimeMethod(
                        "stream:didStopWithError:",
                        StoppedCallbackPointer,
                        // void, self, _cmd, SCStream*, NSError*.
                        "v@:@@")
                ],
                "SCStreamOutput");
            return _delegateType;
        }
    }

    private static IntPtr StartBlock()
    {
        lock (DelegateGate)
        {
            if (_startBlock == IntPtr.Zero)
                _startBlock = CreateStartBlock();
            return _startBlock;
        }
    }

    private static unsafe IntPtr CreateStartBlock() =>
        ObjectiveCBlockFactory.CreateGlobalBlock(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnStarted);

    private static unsafe IntPtr SampleCallbackPointer =>
        (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, nint, void>)&OnSampleBuffer;

    private static unsafe IntPtr StoppedCallbackPointer =>
        (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnStopped;

    [UnmanagedCallersOnly]
    private static void OnStarted(IntPtr block, IntPtr error)
    {
        var completion = Interlocked.Exchange(ref _pendingStart, null);
        completion?.TrySetResult(ScreenCaptureKitNative.ErrorMessage(error));
    }

    /// <summary>
    /// Runs on the capture queue for every audio buffer, so it converts and hands over without
    /// blocking: anything slow here shows up as dropped system audio.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void OnSampleBuffer(
        IntPtr self,
        IntPtr selector,
        IntPtr stream,
        IntPtr sampleBuffer,
        nint outputType)
    {
        try
        {
            if (outputType != ScreenCaptureKitNative.AudioOutputType
                || !Streams.TryGetValue(self, out var owner)
                || owner._disposed != 0)
            {
                return;
            }

            var samples = CoreMediaNative.ReadFloat32(sampleBuffer);
            if (samples.Length == 0)
                return;

            var pcm = PcmAudioConversion.FromFloat32(
                samples,
                CaptureSampleRateHz,
                1,
                owner._format);
            if (pcm.Length > 0)
                owner._audio.Writer.TryWrite(pcm);
        }
        catch
        {
            // Never let a managed fault escape into ScreenCaptureKit's callback frame.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnStopped(IntPtr self, IntPtr selector, IntPtr stream, IntPtr error)
    {
        try
        {
            if (Streams.TryGetValue(self, out var owner))
                owner._audio.Writer.TryComplete();
        }
        catch
        {
        }
    }
}
