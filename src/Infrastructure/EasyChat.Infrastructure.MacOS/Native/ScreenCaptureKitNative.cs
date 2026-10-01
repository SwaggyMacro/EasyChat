using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// ScreenCaptureKit, the only non-deprecated way to take a screenshot on macOS 26.
/// </summary>
/// <remarks>
/// Every entry point here is asynchronous and answers through a block, so a capture is serialised by
/// the caller and each completion handler is a process-wide global block that fulfils a pending
/// task. That mirrors how the microphone authorisation bridge works, and the block machinery itself
/// is covered by <c>ObjectiveCBlockTests</c>.
/// </remarks>
internal static class ScreenCaptureKitNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/ScreenCaptureKit.framework/ScreenCaptureKit";

    /// <summary>Value of <c>kCVPixelFormatType_32BGRA</c>, the four-character code <c>BGRA</c>.</summary>
    private const uint Bgra32PixelFormat = 1111970369;

    private static readonly Lazy<bool> Loaded = new(
        () => NativeLibrary.Load(LibraryPath) != IntPtr.Zero,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IntPtr> ContentBlock = new(
        CreateContentBlock,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IntPtr> ImageBlock = new(
        CreateImageBlock,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static TaskCompletionSource<(IntPtr Content, string? Error)>? _pendingContent;
    private static TaskCompletionSource<(IntPtr Image, string? Error)>? _pendingImage;

    internal static void EnsureAvailable()
    {
        if (!Loaded.Value)
            throw new PlatformNotSupportedException("ScreenCaptureKit could not be loaded.");
    }

    /// <summary>
    /// Asks macOS what is capturable. Without Screen Recording approval this answers an error rather
    /// than an empty list, which is what lets the adapter report a missing permission instead of an
    /// empty desktop.
    /// </summary>
    internal static Task<(IntPtr Content, string? Error)> GetShareableContentAsync()
    {
        EnsureAvailable();
        var completion = new TaskCompletionSource<(IntPtr, string?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingContent = completion;
        ObjectiveCNative.Send(
            ObjectiveCNative.GetClass("SCShareableContent"),
            ObjectiveCNative.GetSelector("getShareableContentWithCompletionHandler:"),
            ContentBlock.Value);
        return completion.Task;
    }

    internal static Task<(IntPtr Image, string? Error)> CaptureImageAsync(
        IntPtr filter,
        IntPtr configuration)
    {
        var completion = new TaskCompletionSource<(IntPtr, string?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingImage = completion;
        ObjectiveCNative.Send(
            ObjectiveCNative.GetClass("SCScreenshotManager"),
            ObjectiveCNative.GetSelector("captureImageWithFilter:configuration:completionHandler:"),
            filter,
            configuration,
            ImageBlock.Value);
        return completion.Task;
    }

    internal static IntPtr Displays(IntPtr content) =>
        ObjectiveCNative.SendReturningHandle(content, ObjectiveCNative.GetSelector("displays"));

    internal static IntPtr Windows(IntPtr content) =>
        ObjectiveCNative.SendReturningHandle(content, ObjectiveCNative.GetSelector("windows"));

    internal static uint DisplayId(IntPtr display) =>
        (uint)ObjectiveCNative.SendReturningInt32(
            display,
            ObjectiveCNative.GetSelector("displayID"));

    /// <summary>The process that owns a window, or zero when macOS does not say.</summary>
    internal static int OwningProcessIdentifier(IntPtr window)
    {
        var application = ObjectiveCNative.SendReturningHandle(
            window,
            ObjectiveCNative.GetSelector("owningApplication"));
        return application == IntPtr.Zero
            ? 0
            : ObjectiveCNative.SendReturningInt32(
                application,
                ObjectiveCNative.GetSelector("processID"));
    }

    /// <summary>Value of <c>SCStreamOutputTypeAudio</c>.</summary>
    internal const nint AudioOutputType = 1;

    /// <summary>
    /// A filter covering everything on a display, which is how system-wide audio is captured: the
    /// audio a filter yields is the audio of the content it covers.
    /// </summary>
    internal static IntPtr CreateDisplayFilter(IntPtr display, IntPtr excludedWindows) =>
        CreateFilter(display, excludedWindows);

    /// <summary>A filter covering one application, so only that application's audio is captured.</summary>
    internal static IntPtr CreateApplicationFilter(
        IntPtr display,
        IntPtr applications,
        IntPtr exceptedWindows) =>
        ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.GetClass("SCContentFilter"),
                ObjectiveCNative.GetSelector("alloc")),
            ObjectiveCNative.GetSelector("initWithDisplay:includingApplications:exceptingWindows:"),
            display,
            applications,
            exceptedWindows);

    internal static IntPtr Applications(IntPtr content) =>
        ObjectiveCNative.SendReturningHandle(
            content,
            ObjectiveCNative.GetSelector("applications"));

    internal static int ApplicationProcessIdentifier(IntPtr application) =>
        ObjectiveCNative.SendReturningInt32(
            application,
            ObjectiveCNative.GetSelector("processID"));

    /// <summary>
    /// A configuration that captures audio and as little video as the API allows.
    /// </summary>
    /// <remarks>
    /// ScreenCaptureKit has no audio-only mode, so a stream always carries video. Asking for a
    /// two-by-two frame and a shallow queue keeps that cost near zero rather than compositing and
    /// delivering full screens that are immediately discarded.
    ///
    /// One channel is requested deliberately: the delivered planes are non-interleaved, and a single
    /// plane is a contiguous run of floats that needs no interleaving before conversion.
    /// </remarks>
    internal static IntPtr CreateAudioConfiguration(int sampleRateHz)
    {
        var configuration = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.GetClass("SCStreamConfiguration"),
                ObjectiveCNative.GetSelector("alloc")),
            ObjectiveCNative.GetSelector("init"));
        if (configuration == IntPtr.Zero)
            throw new InvalidOperationException("The capture configuration could not be created.");

        ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("setCapturesAudio:"), true);
        ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("setSampleRate:"), sampleRateHz);
        ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("setChannelCount:"), 1);

        // Without this EasyChat would hear its own synthesised speech and subtitles and feed them
        // back into recognition.
        ObjectiveCNative.Send(
            configuration,
            ObjectiveCNative.GetSelector("setExcludesCurrentProcessAudio:"),
            true);

        ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("setWidth:"), 2);
        ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("setHeight:"), 2);
        ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("setQueueDepth:"), 3);
        return configuration;
    }

    internal static IntPtr CreateStream(IntPtr filter, IntPtr configuration, IntPtr streamDelegate) =>
        ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.GetClass("SCStream"),
                ObjectiveCNative.GetSelector("alloc")),
            ObjectiveCNative.GetSelector("initWithFilter:configuration:delegate:"),
            filter,
            configuration,
            streamDelegate);

    internal static IntPtr CreateFilter(IntPtr display, IntPtr excludedWindows) =>
        ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.GetClass("SCContentFilter"),
                ObjectiveCNative.GetSelector("alloc")),
            ObjectiveCNative.GetSelector("initWithDisplay:excludingWindows:"),
            display,
            excludedWindows);

    /// <summary>
    /// Builds the capture configuration. Width and height are the pixel size EasyChat wants, and
    /// <paramref name="sourceRect"/> is the region in the display's own point space; setting both is
    /// what makes a Retina capture come back at full resolution rather than scaled to points.
    /// </summary>
    internal static IntPtr CreateConfiguration(
        int pixelWidth,
        int pixelHeight,
        CoreGraphicsRect sourceRect)
    {
        var configuration = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.GetClass("SCStreamConfiguration"),
                ObjectiveCNative.GetSelector("alloc")),
            ObjectiveCNative.GetSelector("init"));
        if (configuration == IntPtr.Zero)
            throw new InvalidOperationException("The capture configuration could not be created.");

        ObjectiveCNative.Send(
            configuration,
            ObjectiveCNative.GetSelector("setWidth:"),
            pixelWidth);
        ObjectiveCNative.Send(
            configuration,
            ObjectiveCNative.GetSelector("setHeight:"),
            pixelHeight);
        SetSourceRect(configuration, ObjectiveCNative.GetSelector("setSourceRect:"), sourceRect);
        ObjectiveCNative.Send(
            configuration,
            ObjectiveCNative.GetSelector("setPixelFormat:"),
            (nint)Bgra32PixelFormat);

        // The pointer is a transient artefact of when the shot was taken, and translating it would
        // be nonsense, so it is never captured.
        ObjectiveCNative.Send(configuration, ObjectiveCNative.GetSelector("setShowsCursor:"), false);
        ObjectiveCNative.Send(
            configuration,
            ObjectiveCNative.GetSelector("setScalesToFit:"),
            false);
        return configuration;
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetSourceRect(
        IntPtr receiver,
        IntPtr selector,
        CoreGraphicsRect rect);

    internal static string? ErrorMessage(IntPtr error) =>
        error == IntPtr.Zero
            ? null
            : FoundationNative.ReadString(ObjectiveCNative.SendReturningHandle(
                error,
                ObjectiveCNative.GetSelector("localizedDescription")));

    private static unsafe IntPtr CreateContentBlock() =>
        ObjectiveCBlockFactory.CreateGlobalBlock(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnContent);

    private static unsafe IntPtr CreateImageBlock() =>
        ObjectiveCBlockFactory.CreateGlobalBlock(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnImage);

    [UnmanagedCallersOnly]
    private static void OnContent(IntPtr block, IntPtr content, IntPtr error)
    {
        var completion = Interlocked.Exchange(ref _pendingContent, null);
        completion?.TrySetResult((
            content == IntPtr.Zero
                ? IntPtr.Zero
                : ObjectiveCNative.SendReturningHandle(
                    content,
                    ObjectiveCNative.GetSelector("retain")),
            ErrorMessage(error)));
    }

    [UnmanagedCallersOnly]
    private static void OnImage(IntPtr block, IntPtr image, IntPtr error)
    {
        var completion = Interlocked.Exchange(ref _pendingImage, null);
        completion?.TrySetResult((
            // The image belongs to the callback frame, so it is retained before the block returns.
            image == IntPtr.Zero ? IntPtr.Zero : CoreFoundationNative.CFRetain(image),
            ErrorMessage(error)));
    }
}
